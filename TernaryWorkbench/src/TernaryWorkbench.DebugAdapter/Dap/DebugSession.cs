using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using TernaryWorkbench.DebugAdapter.Rsp;

namespace TernaryWorkbench.DebugAdapter.Dap;

/// <summary>
/// DAP session against the R2R REBEL-6 simulator's RSP stub: launch mode spawns
/// the simulator with --debug-port and connects; attach mode connects to a stub
/// already listening. Line breakpoints map through the .tasmap (Layer 1) onto the
/// spec's execute triggers; registers render trit-native (rebel6-debug.md: trit
/// display is a tool concern - this is that tool).
/// </summary>
public sealed class DebugSession
{
    private const int ThreadId = 1;
    private const int RegistersScopeRef = 1;
    private const int SystemScopeRef = 2;

    private readonly DapConnection _dap;
    private readonly object _stateLock = new();

    private RspClient? _rsp;
    private Process? _process;
    private TasMap? _tasMap;
    private SourceLocMap? _locMap;
    private string? _programPath;
    private RegisterFormat _format = RegisterFormat.Trits;
    private bool _stopOnEntry = true;
    private bool _launched;
    private bool _running;
    private bool _exited;
    private readonly List<long> _armedBreakpoints = new();

    public DebugSession(DapConnection dap)
    {
        _dap = dap;
    }

    public void Run()
    {
        while (true)
        {
            JsonObject? message = _dap.ReadMessage();
            if (message is null)
                break;
            if (message["type"]?.GetValue<string>() != "request")
                continue;
            if (!HandleRequest(message))
                break;
        }
        Cleanup();
    }

    private bool HandleRequest(JsonObject request)
    {
        string command = request["command"]?.GetValue<string>() ?? string.Empty;
        JsonObject args = request["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            switch (command)
            {
                case "initialize":
                    _dap.SendResponse(request, true, new JsonObject
                    {
                        ["supportsConfigurationDoneRequest"] = true,
                        ["supportsSetVariable"] = true,
                        ["supportsEvaluateForHovers"] = true,
                        ["supportTerminateDebuggee"] = true,
                    });
                    break;
                case "launch":
                    HandleLaunch(request, args);
                    break;
                case "attach":
                    HandleAttach(request, args);
                    break;
                case "setBreakpoints":
                    HandleSetBreakpoints(request, args);
                    break;
                case "setExceptionBreakpoints":
                    _dap.SendResponse(request, true);
                    break;
                case "configurationDone":
                    _dap.SendResponse(request, true);
                    if (_stopOnEntry)
                        SendStopped("entry");
                    else
                        StartResume();
                    break;
                case "threads":
                    _dap.SendResponse(request, true, new JsonObject
                    {
                        ["threads"] = new JsonArray(new JsonObject
                        {
                            ["id"] = ThreadId,
                            ["name"] = "REBEL-6 hart 0",
                        }),
                    });
                    break;
                case "stackTrace":
                    HandleStackTrace(request);
                    break;
                case "scopes":
                    _dap.SendResponse(request, true, new JsonObject
                    {
                        ["scopes"] = new JsonArray(
                            new JsonObject { ["name"] = "Registers", ["variablesReference"] = RegistersScopeRef, ["expensive"] = false },
                            new JsonObject { ["name"] = "System registers", ["variablesReference"] = SystemScopeRef, ["expensive"] = true }),
                    });
                    break;
                case "variables":
                    HandleVariables(request, args);
                    break;
                case "setVariable":
                    HandleSetVariable(request, args);
                    break;
                case "continue":
                    _dap.SendResponse(request, true, new JsonObject { ["allThreadsContinued"] = true });
                    StartResume();
                    break;
                case "next":
                case "stepIn":
                case "stepOut":
                    HandleStep(request);
                    break;
                case "pause":
                    _dap.SendResponse(request, true);
                    _rsp?.Interrupt();
                    break;
                case "evaluate":
                    HandleEvaluate(request, args);
                    break;
                case "disconnect":
                    HandleDisconnect(request, args);
                    return false;
                default:
                    _dap.SendResponse(request, false, message: $"Unsupported request: {command}");
                    break;
            }
        }
        catch (Exception e)
        {
            _dap.SendResponse(request, false, message: e.Message);
        }
        return true;
    }

    private void HandleLaunch(JsonObject request, JsonObject args)
    {
        string? program = args["program"]?.GetValue<string>();
        if (string.IsNullOrEmpty(program))
        {
            _dap.SendResponse(request, false, message: "launch requires \"program\": the .tas file to debug");
            return;
        }
        string simulator = args["simulator"]?.GetValue<string>() ?? "RV32IToREBEL";
        int port = args["port"]?.GetValue<int>() ?? 3333;
        ReadOptions(args);
        if (args["noDebug"]?.GetValue<bool>() == true)
            _stopOnEntry = false;

        _programPath = Path.GetFullPath(program);
        var startInfo = new ProcessStartInfo
        {
            FileName = simulator,
            WorkingDirectory = Path.GetDirectoryName(_programPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--debug-port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add(_programPath);

        _process = Process.Start(startInfo);
        if (_process is null)
        {
            _dap.SendResponse(request, false, message: $"Failed to start simulator: {simulator}");
            return;
        }
        _launched = true;
        PipeProcessOutput(_process.StandardOutput, "stdout");
        PipeProcessOutput(_process.StandardError, "stderr");

        if (!TryConnect(port, TimeSpan.FromSeconds(10)))
        {
            _dap.SendResponse(request, false, message: $"Simulator did not open debug port {port}");
            return;
        }
        LoadTasMap();
        _dap.SendResponse(request, true);
        _dap.SendEvent("initialized");
    }

    private void HandleAttach(JsonObject request, JsonObject args)
    {
        int port = args["port"]?.GetValue<int>() ?? 3333;
        string? program = args["program"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(program))
            _programPath = Path.GetFullPath(program);
        ReadOptions(args);

        if (!TryConnect(port, TimeSpan.FromSeconds(5)))
        {
            _dap.SendResponse(request, false, message: $"No RSP stub listening on port {port}");
            return;
        }
        LoadTasMap();
        _dap.SendResponse(request, true);
        _dap.SendEvent("initialized");
    }

    private void ReadOptions(JsonObject args)
    {
        _stopOnEntry = args["stopOnEntry"]?.GetValue<bool>() ?? true;
        string? format = args["registerFormat"]?.GetValue<string>();
        if (format is not null && Enum.TryParse(format, ignoreCase: true, out RegisterFormat parsed))
            _format = parsed;
    }

    private bool TryConnect(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                tcp.NoDelay = true;
                _rsp = new RspClient(tcp.GetStream());
                _rsp.Negotiate();
                return true;
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
            }
        }
        return false;
    }

    private void LoadTasMap()
    {
        if (_programPath is null)
            return;
        string mapPath = Path.ChangeExtension(_programPath, ".tasmap");
        if (File.Exists(mapPath))
            _tasMap = TasMap.Load(mapPath);
        else
            SendOutput("console", $"No source map at {mapPath}; line breakpoints and source-level stepping unavailable.\n");
        if (File.Exists(_programPath))
        {
            // Layer 2: "# .loc" markers left by r6cc -g enable C/C++-level
            // breakpoints and frames for compiled kernels.
            SourceLocMap locMap = SourceLocMap.Load(_programPath);
            if (locMap.Count > 0)
                _locMap = locMap;
        }
    }

    private void HandleSetBreakpoints(JsonObject request, JsonObject args)
    {
        if (!RequireHalted(request, out RspClient rsp))
            return;

        var requested = new List<int>();
        if (args["breakpoints"] is JsonArray bps)
        {
            foreach (JsonNode? bp in bps)
            {
                if (bp?["line"]?.GetValue<int>() is int line)
                    requested.Add(line);
            }
        }

        // Breakpoints in a C/C++ source compose Layer 2 (source line -> .tas
        // line) with Layer 1 (.tas line -> address); breakpoints in the .tas
        // itself use Layer 1 directly.
        string? sourcePath = args["source"]?["path"]?.GetValue<string>();
        bool isCompiledSource = sourcePath is not null && _programPath is not null &&
            !string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(_programPath), StringComparison.OrdinalIgnoreCase) &&
            _locMap is not null && _locMap.CoversFile(sourcePath);

        foreach (long address in _armedBreakpoints)
            rsp.ClearBreakpoint(address);
        _armedBreakpoints.Clear();

        var results = new JsonArray();
        foreach (int line in requested)
        {
            (long Address, int Line)? target;
            if (isCompiledSource)
            {
                (int TasLine, int SourceLine)? loc = _locMap!.TasLineForSourceLine(sourcePath!, line);
                (long Address, int Line)? slot = loc is null ? null : _tasMap?.AddressForLine(loc.Value.TasLine);
                target = slot is null ? null : (slot.Value.Address, loc!.Value.SourceLine);
            }
            else
            {
                target = _tasMap?.AddressForLine(line);
            }
            if (target is null)
            {
                results.Add(new JsonObject
                {
                    ["verified"] = false,
                    ["line"] = line,
                    ["message"] = _tasMap is null ? "no .tasmap source map" : "no instruction at or after this line",
                });
                continue;
            }
            if (_armedBreakpoints.Contains(target.Value.Address))
            {
                results.Add(new JsonObject { ["verified"] = true, ["line"] = target.Value.Line });
                continue;
            }
            if (rsp.TrySetBreakpoint(target.Value.Address))
            {
                _armedBreakpoints.Add(target.Value.Address);
                results.Add(new JsonObject { ["verified"] = true, ["line"] = target.Value.Line });
            }
            else
            {
                results.Add(new JsonObject
                {
                    ["verified"] = false,
                    ["line"] = line,
                    ["message"] = "all 4 execute triggers are armed (rebel6-debug.md trigger module)",
                });
            }
        }
        _dap.SendResponse(request, true, new JsonObject { ["breakpoints"] = results });
    }

    private void HandleStackTrace(JsonObject request)
    {
        if (!RequireHalted(request, out RspClient rsp))
            return;

        long pc = rsp.ReadRegister(RspClient.PcRegnum);
        var frame = new JsonObject
        {
            ["id"] = 0,
            ["name"] = $"hart 0 @ {pc}",
            ["line"] = 0,
            ["column"] = 0,
        };
        int? line = _tasMap?.LineForAddress(pc);
        if (line is not null && _programPath is not null)
        {
            frame["line"] = line.Value;
            frame["column"] = 1;
            frame["source"] = new JsonObject
            {
                ["name"] = Path.GetFileName(_programPath),
                ["path"] = _programPath,
            };
            // Layer 2 ∘ Layer 1: a compiled kernel's frame reports the C/C++
            // position its "# .loc" marker binds, not the .tas line.
            (string File, int Line)? loc = _locMap?.LocForTasLine(line.Value);
            if (loc is not null && File.Exists(loc.Value.File))
            {
                frame["line"] = loc.Value.Line;
                frame["source"] = new JsonObject
                {
                    ["name"] = Path.GetFileName(loc.Value.File),
                    ["path"] = loc.Value.File,
                };
            }
        }
        _dap.SendResponse(request, true, new JsonObject
        {
            ["stackFrames"] = new JsonArray(frame),
            ["totalFrames"] = 1,
        });
    }

    private void HandleVariables(JsonObject request, JsonObject args)
    {
        if (!RequireHalted(request, out RspClient rsp))
            return;

        int reference = args["variablesReference"]?.GetValue<int>() ?? 0;
        var variables = new JsonArray();
        if (reference == RegistersScopeRef)
        {
            long[] values = rsp.ReadAllRegisters();
            for (int i = 0; i < 32; i++)
            {
                variables.Add(new JsonObject
                {
                    ["name"] = $"x{i} ({RspClient.AbiNames[i]})",
                    ["value"] = TritFormat.Render(values[i], _format),
                    ["variablesReference"] = 0,
                });
            }
            variables.Add(new JsonObject
            {
                ["name"] = "pc",
                ["value"] = values[RspClient.PcRegnum].ToString(),
                ["variablesReference"] = 0,
            });
        }
        else if (reference == SystemScopeRef)
        {
            foreach (string name in RspClient.SystemRegisterNames)
            {
                variables.Add(new JsonObject
                {
                    ["name"] = name,
                    ["value"] = ReadSystemRegister(rsp, name),
                    ["variablesReference"] = 0,
                });
            }
        }
        _dap.SendResponse(request, true, new JsonObject { ["variables"] = variables });
    }

    // The stub's p/P regnums stop at pc; the system range is reachable through
    // "monitor reg", whose reply is "name (Xn) = value = trits".
    private string ReadSystemRegister(RspClient rsp, string name)
    {
        string reply = rsp.Monitor($"reg {name}").Trim();
        int eq = reply.IndexOf(" = ", StringComparison.Ordinal);
        if (eq < 0)
            return reply;
        string[] parts = reply[(eq + 3)..].Split(" = ");
        if (parts.Length == 2 && long.TryParse(parts[0], out long value))
            return TritFormat.Render(value, _format);
        return reply[(eq + 3)..];
    }

    private void HandleSetVariable(JsonObject request, JsonObject args)
    {
        if (!RequireHalted(request, out RspClient rsp))
            return;

        string name = args["name"]?.GetValue<string>() ?? string.Empty;
        string text = args["value"]?.GetValue<string>() ?? string.Empty;
        int? regnum = ResolveGdbRegnum(name);
        if (args["variablesReference"]?.GetValue<int>() != RegistersScopeRef || regnum is null)
        {
            _dap.SendResponse(request, false, message: $"Not a writable register: {name}");
            return;
        }
        if (!TritFormat.TryParseValue(text, out long value))
        {
            _dap.SendResponse(request, false, message: $"Not a trit string, decimal, or hex value: {text}");
            return;
        }
        rsp.WriteRegister(regnum.Value, value);
        _dap.SendResponse(request, true, new JsonObject
        {
            ["value"] = regnum == RspClient.PcRegnum ? value.ToString() : TritFormat.Render(value, _format),
        });
    }

    private void HandleStep(JsonObject request)
    {
        if (!RequireHalted(request, out RspClient rsp))
            return;
        _dap.SendResponse(request, true);
        HandleStopReply(rsp.Step(), stepped: true);
    }

    private void StartResume()
    {
        RspClient? rsp;
        lock (_stateLock)
        {
            rsp = _rsp;
            if (rsp is null || _running || _exited)
                return;
            _running = true;
        }
        Task.Run(() =>
        {
            try
            {
                HandleStopReply(rsp.Continue(), stepped: false);
            }
            catch (Exception e)
            {
                SendOutput("console", $"debug connection lost: {e.Message}\n");
                lock (_stateLock)
                {
                    _running = false;
                    _exited = true;
                }
                _dap.SendEvent("terminated");
            }
        });
    }

    private void HandleStopReply(RspStopReply reply, bool stepped)
    {
        lock (_stateLock)
        {
            _running = false;
        }
        if (reply.Kind == StopKind.Exited)
        {
            lock (_stateLock)
            {
                _exited = true;
            }
            SendOutput("console", $"Program exited with status {reply.ExitStatus}\n");
            _dap.SendEvent("exited", new JsonObject { ["exitCode"] = reply.ExitStatus });
            _dap.SendEvent("terminated");
            return;
        }

        string reason = reply.Signal == 2 ? "pause"
            : stepped ? "step"
            : "breakpoint";
        SendStopped(reason);
    }

    private void SendStopped(string reason)
    {
        _dap.SendEvent("stopped", new JsonObject
        {
            ["reason"] = reason,
            ["threadId"] = ThreadId,
            ["allThreadsStopped"] = true,
        });
    }

    private void HandleEvaluate(JsonObject request, JsonObject args)
    {
        string expression = (args["expression"]?.GetValue<string>() ?? string.Empty).Trim();

        if (expression.StartsWith("format ", StringComparison.OrdinalIgnoreCase))
        {
            string wanted = expression["format ".Length..].Trim();
            if (Enum.TryParse(wanted, ignoreCase: true, out RegisterFormat parsed))
            {
                _format = parsed;
                Respond($"register format: {parsed.ToString().ToLowerInvariant()}");
                return;
            }
            _dap.SendResponse(request, false, message: "format trits|decimal|hex");
            return;
        }

        if (!RequireHalted(request, out RspClient rsp))
            return;

        string? monitor = null;
        if (expression.StartsWith("monitor ", StringComparison.Ordinal))
            monitor = expression["monitor ".Length..];
        else if (expression.StartsWith("mon ", StringComparison.Ordinal))
            monitor = expression["mon ".Length..];
        if (monitor is not null)
        {
            Respond(rsp.Monitor(monitor).TrimEnd('\n'));
            return;
        }

        int? regnum = ResolveGdbRegnum(expression);
        if (regnum is not null)
        {
            long value = rsp.ReadRegister(regnum.Value);
            Respond(regnum == RspClient.PcRegnum ? value.ToString() : TritFormat.Render(value, _format));
            return;
        }
        if (RspClient.SystemRegisterNames.Contains(expression.ToLowerInvariant()))
        {
            Respond(ReadSystemRegister(rsp, expression.ToLowerInvariant()));
            return;
        }

        _dap.SendResponse(request, false,
            message: "Not a register; use a register name, \"monitor <cmd>\", or \"format trits|decimal|hex\"");

        void Respond(string result) => _dap.SendResponse(request, true, new JsonObject
        {
            ["result"] = result,
            ["variablesReference"] = 0,
        });
    }

    /// <summary>"pc", "x10", ABI names, or the variables-view "x10 (a0)" form.</summary>
    private static int? ResolveGdbRegnum(string name)
    {
        name = name.Trim().ToLowerInvariant();
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0)
            name = name[..paren];
        if (name == "pc")
            return RspClient.PcRegnum;
        if (name == "fp")
            return 8;
        if (name.Length > 1 && name[0] == 'x' && int.TryParse(name[1..], out int index))
            return index is >= 0 and < 32 ? index : null;
        int abi = Array.IndexOf(RspClient.AbiNames, name);
        return abi >= 0 ? abi : null;
    }

    private bool RequireHalted(JsonObject request, out RspClient rsp)
    {
        lock (_stateLock)
        {
            if (_rsp is not null && !_running && !_exited)
            {
                rsp = _rsp;
                return true;
            }
        }
        rsp = null!;
        _dap.SendResponse(request, false, message: _rsp is null
            ? "No debug connection"
            : _exited ? "The program has exited" : "The target is running; pause first");
        return false;
    }

    private void HandleDisconnect(JsonObject request, JsonObject args)
    {
        bool terminate = args["terminateDebuggee"]?.GetValue<bool>() ?? _launched;
        bool wasRunning;
        lock (_stateLock)
        {
            wasRunning = _running;
        }
        try
        {
            if (_rsp is not null && !_exited)
            {
                if (terminate)
                {
                    if (wasRunning)
                        _rsp.Interrupt();
                    _rsp.Kill();
                }
                else if (!wasRunning)
                {
                    _rsp.Detach(); // The stub runs the program to completion
                }
            }
        }
        catch (Exception)
        {
            // The stub may already be gone; disconnect must still succeed.
        }
        _dap.SendResponse(request, true);
    }

    private void PipeProcessOutput(StreamReader reader, string category)
    {
        Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                    SendOutput(category, line + "\n");
            }
            catch (Exception)
            {
                // Process ended; output pipe closed.
            }
        });
    }

    private void SendOutput(string category, string text)
    {
        _dap.SendEvent("output", new JsonObject
        {
            ["category"] = category,
            ["output"] = text,
        });
    }

    private void Cleanup()
    {
        try
        {
            _rsp?.Dispose();
        }
        catch (Exception)
        {
        }
        if (_launched && _process is not null)
        {
            try
            {
                if (!_process.WaitForExit(2000))
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }
        }
    }
}
