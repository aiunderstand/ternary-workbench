using System.Text;
using System.Text.Json.Nodes;

namespace TernaryWorkbench.DebugAdapter.Dap;

/// <summary>
/// Debug Adapter Protocol wire layer: Content-Length framed JSON messages over a
/// stream pair (stdio when hosted by VS Code). Sending is thread-safe - stop events
/// arrive from the background resume task.
/// </summary>
public sealed class DapConnection
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _sendLock = new();
    private int _seq;

    public DapConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Null on end-of-stream (the host closed the session).</summary>
    public JsonObject? ReadMessage()
    {
        int contentLength = -1;
        while (true)
        {
            string? line = ReadHeaderLine();
            if (line is null)
                return null;
            if (line.Length == 0)
                break;
            const string prefix = "Content-Length:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[prefix.Length..].Trim());
        }
        if (contentLength < 0)
            throw new InvalidDataException("DAP message without Content-Length");

        byte[] body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = _input.Read(body, read, contentLength - read);
            if (n <= 0)
                return null;
            read += n;
        }
        return JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject;
    }

    public void SendResponse(JsonObject request, bool success, JsonObject? body = null, string? message = null)
    {
        var response = new JsonObject
        {
            ["type"] = "response",
            ["request_seq"] = request["seq"]?.GetValue<int>() ?? 0,
            ["command"] = request["command"]?.GetValue<string>() ?? string.Empty,
            ["success"] = success,
        };
        if (body is not null)
            response["body"] = body;
        if (message is not null)
            response["message"] = message;
        Send(response);
    }

    public void SendEvent(string name, JsonObject? body = null)
    {
        var evt = new JsonObject
        {
            ["type"] = "event",
            ["event"] = name,
        };
        if (body is not null)
            evt["body"] = body;
        Send(evt);
    }

    private void Send(JsonObject message)
    {
        lock (_sendLock)
        {
            message["seq"] = ++_seq;
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            _output.Write(header, 0, header.Length);
            _output.Write(body, 0, body.Length);
            _output.Flush();
        }
    }

    private string? ReadHeaderLine()
    {
        var line = new StringBuilder();
        while (true)
        {
            int b = _input.ReadByte();
            if (b < 0)
                return line.Length == 0 ? null : line.ToString();
            if (b == '\n')
                return line.ToString().TrimEnd('\r');
            line.Append((char)b);
        }
    }
}
