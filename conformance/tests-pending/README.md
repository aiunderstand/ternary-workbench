# Pending conformance tests

Best-effort drafts for instruction groups the reference simulator (R2R
`RV32IToREBEL`) cannot execute yet. They are **excluded from `run.sh`**
(the runner only scans `../tests/`) and have **never executed** — the
expected values are hand-computed from the spec and must be re-verified
against a second source the first time each file runs.

**This directory is now empty.** Every draft has graduated to
`../tests/`:

- The M (`m_ext_t.tas`), Ztb (`ztb_t.tas`) and Ztl (`ztl_t.tas`)
  drafts graduated when A1.5/A1.6 landed in the simulator (expected
  constants re-verified independently against `stdternary-cpp` and
  wide-integer arithmetic on first run).
- The trap-cause draft (`trap_causes_t.tas`) graduated when A1.3
  landed (negative-range system registers, privilege modes, trap
  entry, `TRET.T`). Its two provisional assumptions resolved as
  follows: (1) the R2R dialect accepts the platform register *names*
  (`mtvec`, `mepc`, `mcause`, `mstatus`, `sstatus`, `sepc`, ...) as
  operands for the negative-range registers; (2) the
  semihosting/trap split follows the ABI, not the draft's
  handler-installed guess — semihosting is `ECALL.T` from **M** with
  `a7` ∈ {63, 64, 93, 214} regardless of any installed handler, and
  every other `ECALL.T` takes the architectural environment-call
  trap. The stubbed S/U arms (−8/−7) were completed with real
  M→S→U descent via `mstatus.MPP` / `sstatus.SPP` staging and
  `TRET.T`.

If a future extension lands in the spec before the simulator (the A
extension is the standing candidate), stage its draft here in the same
self-checking style (exit 0 = pass, nonzero exit = index of the failed
check in `a0`), then move it into `../tests/` and add it to the
coverage table in `../README.md` once it runs.
