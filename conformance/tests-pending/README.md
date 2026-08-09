# Pending conformance tests

Best-effort drafts for instruction groups the reference simulator (R2R
`RV32IToREBEL`) cannot execute yet. They are **excluded from `run.sh`**
(the runner only scans `../tests/`) and have **never executed** — the
expected values are hand-computed from the spec and must be re-verified
against a second source the first time each file runs.

Each file is written to the frozen spec, in the same self-checking
style as the live suite (exit 0 = pass, nonzero exit = index of the
failed check in `a0`).

Only one draft remains; the M (`m_ext_t.tas`), Ztb (`ztb_t.tas`) and
Ztl (`ztl_t.tas`) drafts graduated to `../tests/` when A1.5/A1.6
landed in the simulator (expected constants re-verified independently
against `stdternary-cpp` and wide-integer arithmetic on first run).

| Draft | Covers | Spec | Unlocks with |
|-------|--------|------|--------------|
| `trap_causes_t.tas` | Cause-code cross-check asserting the **platform** table's numbers: `EBREAK.T` → −10, `ECALL.T` from M → −9 (S → −8 and U → −7 stubbed pending privilege plumbing); `mepc` points at the faulting instruction; resume via `TRET.T` | rebel6-platform.md, "Balanced cause codes" / "Trap entry" | A1.3 |

Known open points in the draft:

- `trap_causes_t.tas` uses the platform register *names* (`mtvec`,
  `mcause`, `mepc`) for the negative-range registers; the R2R
  assembler syntax for X-1…X-23 is not yet defined. It also assumes
  that once a handler is installed via `mtvec`, `ECALL.T` traps to it
  instead of being intercepted as semihosting (the draft uninstalls
  the handler before the final exit call for exactly this reason).
  The S- and U-mode `ECALL.T` arms (−8, −7) still need `mstatus.MPP`
  staging plus `TRET.T` descent once those are testable.
- Simulator behaviour today: `EBREAK.T` and `TRET.T` print
  `unsupported instruction '<name>' (trap machinery - pending A1.3)`
  and exit 2 — which is why this file lives here and not in
  `../tests/`.

When the trap machinery lands in the simulator, run the draft,
re-verify each expected constant independently, then move the file
into `../tests/` and add it to the coverage table in `../README.md`.
