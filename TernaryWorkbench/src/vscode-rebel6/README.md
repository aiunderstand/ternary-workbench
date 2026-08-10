# REBEL-6 Debug for VS Code

Debug REBEL-6 balanced-ternary assembly (`.tas`) in VS Code: line breakpoints,
instruction stepping, and a trit-native register view
(`x10 (a0) = 00000000000000000000+++0 = 42`), backed by the
[TernaryWorkbench DAP adapter](../TernaryWorkbench.DebugAdapter/) speaking the GDB
remote serial protocol to the R2R reference simulator
([rebel6-debug.md](../../../docs/rebel6-debug.md)).

## Setup

1. Build the adapter and the simulator:

   ```bash
   dotnet publish TernaryWorkbench/src/TernaryWorkbench.DebugAdapter -c Release
   cmake --build <RV32IToREBEL>/build
   ```

2. Point the two settings at the binaries:

   ```json
   "rebel6.adapterPath": "<workbench>/TernaryWorkbench/src/TernaryWorkbench.DebugAdapter/bin/Release/net10.0/publish/TernaryWorkbench.DebugAdapter.dll",
   "rebel6.simulatorPath": "<RV32IToREBEL>/build/RV32IToREBEL"
   ```

3. Install this folder as an extension (symlink it into `~/.vscode/extensions/`,
   or package with `vsce package` and install the `.vsix`).

4. Open a `.tas` file and press F5.

## Launch configuration

```json
{
  "type": "rebel6",
  "request": "launch",
  "name": "Debug REBEL-6 program",
  "program": "${file}",
  "port": 3333,
  "stopOnEntry": true,
  "registerFormat": "trits"
}
```

Attach mode connects to a simulator already started with
`RV32IToREBEL --debug-port <port> prog.tas`.

## Debug console

- `monitor trits <reg>` / `monitor reg <name>` / `monitor dis <addr> <n>` / `monitor info`
- register names evaluate directly (`a0`, `x5`, `mepc`, ...)
- `format trits|decimal|hex` switches the register rendering at runtime

## Notes

- Breakpoints are the spec's execute triggers: at most 4 armed at once, and a
  breakpoint on a comment or label line slides to the next executable line.
- Source-line mapping comes from the `.tasmap` the simulator emits next to the
  program when started with `--debug-port`.
