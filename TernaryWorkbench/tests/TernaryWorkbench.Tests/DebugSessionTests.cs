using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using TernaryWorkbench.DebugAdapter.Dap;
using Xunit;

namespace TernaryWorkbench.Tests;

/// <summary>
/// End-to-end DAP session against the mock RSP stub: the adapter runs on its real
/// code path (attach mode), the test plays the VS Code side over anonymous pipes.
/// </summary>
public class DebugSessionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly MockRspServer _server = new();
    private readonly AnonymousPipeServerStream _toSession = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream _fromSession = new(PipeDirection.In);
    private readonly Thread _sessionThread;
    private readonly Queue<JsonObject> _pendingEvents = new();
    private readonly string _programPath;
    private int _seq;

    public DebugSessionTests()
    {
        string dir = Directory.CreateTempSubdirectory("rebel6-dap-test").FullName;
        _programPath = Path.Combine(dir, "prog.tas");
        File.WriteAllText(_programPath, "main:\n\tli.t a0, 0\n");
        File.WriteAllText(Path.Combine(dir, "prog.tasmap"), "0\t6\n1\t7\n2\t10\n");

        var sessionIn = new AnonymousPipeClientStream(PipeDirection.In, _toSession.ClientSafePipeHandle);
        var sessionOut = new AnonymousPipeClientStream(PipeDirection.Out, _fromSession.ClientSafePipeHandle);
        _sessionThread = new Thread(() => new DebugSession(new DapConnection(sessionIn, sessionOut)).Run())
        {
            IsBackground = true,
        };
        _sessionThread.Start();
    }

    public void Dispose()
    {
        _toSession.Dispose();
        _fromSession.Dispose();
        _server.Dispose();
    }

    [Fact]
    public void FullSession_BreakpointsSteppingRegistersAndMonitor()
    {
        Request("initialize", new JsonObject())["success"]!.GetValue<bool>().Should().BeTrue();

        JsonObject attach = Request("attach", new JsonObject
        {
            ["port"] = _server.Port,
            ["program"] = _programPath,
        });
        attach["success"]!.GetValue<bool>().Should().BeTrue();
        WaitForEvent("initialized");

        // Line 6 is mapped exactly; line 8 has no instruction and slides to line 10.
        JsonObject bps = Request("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = _programPath },
            ["breakpoints"] = new JsonArray(
                new JsonObject { ["line"] = 6 },
                new JsonObject { ["line"] = 8 }),
        });
        JsonArray results = (JsonArray)bps["body"]!["breakpoints"]!;
        results[0]!["verified"]!.GetValue<bool>().Should().BeTrue();
        results[0]!["line"]!.GetValue<int>().Should().Be(6);
        results[1]!["verified"]!.GetValue<bool>().Should().BeTrue();
        results[1]!["line"]!.GetValue<int>().Should().Be(10);
        _server.ArmedTriggers.Should().BeEquivalentTo(new long[] { 0, 2 });

        Request("configurationDone", new JsonObject());
        WaitForEvent("stopped")["body"]!["reason"]!.GetValue<string>().Should().Be("entry");

        JsonObject threads = Request("threads", new JsonObject());
        ((JsonArray)threads["body"]!["threads"]!).Should().HaveCount(1);

        // pc = 1 → line 7 of the .tas source
        _server.Registers[32] = 1;
        _server.Registers[10] = 4;
        JsonObject stack = Request("stackTrace", new JsonObject { ["threadId"] = 1 });
        JsonObject frame = (JsonObject)((JsonArray)stack["body"]!["stackFrames"]!)[0]!;
        frame["line"]!.GetValue<int>().Should().Be(7);
        frame["source"]!["path"]!.GetValue<string>().Should().Be(_programPath);

        JsonObject scopes = Request("scopes", new JsonObject { ["frameId"] = 0 });
        JsonArray scopeList = (JsonArray)scopes["body"]!["scopes"]!;
        int registersRef = scopeList[0]!["variablesReference"]!.GetValue<int>();

        JsonObject variables = Request("variables", new JsonObject { ["variablesReference"] = registersRef });
        JsonArray vars = (JsonArray)variables["body"]!["variables"]!;
        vars.Should().HaveCount(33);
        JsonObject a0 = (JsonObject)vars.First(v => v!["name"]!.GetValue<string>() == "x10 (a0)")!;
        a0["value"]!.GetValue<string>().Should().Be(new string('0', 22) + "++ = 4");

        JsonObject setVar = Request("setVariable", new JsonObject
        {
            ["variablesReference"] = registersRef,
            ["name"] = "x10 (a0)",
            ["value"] = "-0x2a",
        });
        setVar["success"]!.GetValue<bool>().Should().BeTrue();
        _server.Registers[10].Should().Be(-42);

        Request("evaluate", new JsonObject { ["expression"] = "monitor info", ["context"] = "repl" })
            ["body"]!["result"]!.GetValue<string>().Should().Contain("tritflips = 12");
        Request("evaluate", new JsonObject { ["expression"] = "mepc", ["context"] = "watch" })
            ["body"]!["result"]!.GetValue<string>().Should().Contain("= 77");

        Request("continue", new JsonObject { ["threadId"] = 1 });
        WaitForEvent("stopped")["body"]!["reason"]!.GetValue<string>().Should().Be("breakpoint");

        Request("next", new JsonObject { ["threadId"] = 1 });
        WaitForEvent("stopped")["body"]!["reason"]!.GetValue<string>().Should().Be("step");

        _server.ContinueReply = "W07";
        Request("continue", new JsonObject { ["threadId"] = 1 });
        WaitForEvent("exited")["body"]!["exitCode"]!.GetValue<int>().Should().Be(7);
        WaitForEvent("terminated");

        Request("disconnect", new JsonObject())["success"]!.GetValue<bool>().Should().BeTrue();
    }

    private JsonObject Request(string command, JsonObject arguments)
    {
        int seq = ++_seq;
        var request = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
            ["arguments"] = arguments,
        };
        byte[] body = Encoding.UTF8.GetBytes(request.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        _toSession.Write(header, 0, header.Length);
        _toSession.Write(body, 0, body.Length);
        _toSession.Flush();

        while (true)
        {
            JsonObject message = ReadMessage();
            if (message["type"]!.GetValue<string>() == "event")
            {
                _pendingEvents.Enqueue(message);
                continue;
            }
            if (message["request_seq"]?.GetValue<int>() == seq)
                return message;
        }
    }

    private JsonObject WaitForEvent(string name)
    {
        while (_pendingEvents.Count > 0)
        {
            JsonObject queued = _pendingEvents.Dequeue();
            if (queued["event"]!.GetValue<string>() == name)
                return queued;
        }
        while (true)
        {
            JsonObject message = ReadMessage();
            if (message["type"]!.GetValue<string>() == "event" &&
                message["event"]!.GetValue<string>() == name)
            {
                return message;
            }
        }
    }

    private JsonObject ReadMessage()
    {
        var task = Task.Run(() =>
        {
            int contentLength = -1;
            while (true)
            {
                string line = ReadHeaderLine();
                if (line.Length == 0)
                    break;
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line["Content-Length:".Length..].Trim());
            }
            byte[] body = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int n = _fromSession.Read(body, read, contentLength - read);
                if (n <= 0)
                    throw new EndOfStreamException();
                read += n;
            }
            return (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(body))!;
        });
        task.Wait(Timeout).Should().BeTrue("the adapter must answer within the timeout");
        return task.Result;
    }

    private string ReadHeaderLine()
    {
        var line = new StringBuilder();
        while (true)
        {
            int b = _fromSession.ReadByte();
            if (b < 0)
                throw new EndOfStreamException();
            if (b == '\n')
                return line.ToString().TrimEnd('\r');
            line.Append((char)b);
        }
    }
}
