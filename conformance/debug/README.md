# Debug conformance (JTAG / OpenOCD)

The debug conformance category of [rebel6-debug.md](../../docs/rebel6-debug.md): stock
OpenOCD drives the simulated JTAG stack (remote_bitbang → TAP → DTM → DM) end to end.

```bash
# One-shot: starts the simulator with --jtag-bitbang-port and runs the checks
./run-openocd.sh [program.tas]        # defaults to ../tests/add_t.tas
```

| File | Role |
|---|---|
| `rebel6-sim.cfg` | OpenOCD adapter config: remote_bitbang to the simulator, TAP declaration with the ratified IDCODE `0x52454201` |
| `rebel6-dm.tcl` | The TCL-level DMI driver: `rebel6_dmi_read/write`, `rebel6_readreg/writereg`, `rebel6_resume/step/halt`, `rebel6_readmem/writemem`, `rebel6_trigger`, `rebel6_reset_halt` — generic `irscan`/`drscan` only, because the spec's 73-bit dmi DR rules out OpenOCD's `riscv` target (`dtmcs.version` = 7 is the guard) |
| `rebel6-sim-test.tcl` | The scripted checks: DTM identification, run control, exec triggers, `dcsr.step`, `resethaltreq` |

Adapter independence is the point: the same TCL runs unchanged over
`adapter driver jlink` against a fabric TAP in the FPGA phase — only the
adapter block of the `.cfg` changes.

gdb debugging is *not* routed through OpenOCD in this phase (that needs a
REBEL-6 OpenOCD target driver); gdb and the VS Code adapter use the simulator's
native RSP stub (`--debug-port`). The R2R-side twin of this suite
(`code/Simulators/REBEL6/Debug/tests/run-jtag-tests.sh`) covers the same DM
surface through a scripted bitbang client and cross-validates register reads
against a parallel RSP session.
