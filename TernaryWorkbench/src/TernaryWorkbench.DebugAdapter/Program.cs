using TernaryWorkbench.DebugAdapter.Dap;

// DAP over stdio: VS Code (or any DAP host) spawns this executable and speaks
// Content-Length framed JSON on stdin/stdout. All diagnostics go to stderr -
// stdout belongs to the protocol.
var connection = new DapConnection(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput());
new DebugSession(connection).Run();
