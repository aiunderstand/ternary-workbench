# Pending conformance tests

Best-effort drafts for instruction groups the reference simulator (R2R
`RV32IToREBEL`) cannot execute yet. They are **excluded from `run.sh`**
(the runner only scans `../tests/`) and have **never executed** — the
expected values are hand-computed from the spec and must be re-verified
against a second source the first time each file runs.

Each file is written to the frozen spec, in the same self-checking
style as the live suite (exit 0 = pass, nonzero exit = index of the
failed check in `a0`).

| Draft | Covers | Spec | Unlocks with |
|-------|--------|------|--------------|
| `m_ext_t.tas` | `MUL.T`, `MULH.T`, `DIV.T`, `REM.T`, `MOD.T`, `MAC.T` — balanced wrap, truncating division, `REM` sign = dividend / `MOD` sign = divisor, `MIN / -1 = MAX` without trap, div-by-zero q=0 convention | rebel6-extensions.md, "M — integer multiply / divide" | A1.5 |
| `ztb_t.tas` | `CLZT.T` (0…24 including `CLZT.T(0) = 24`), `TCNT.T` against hand-computed popcounts | rebel6-extensions.md, "Ztb — trit manipulation" | A1.6 |
| `ztl_t.tas` | `TLUTI.T` raw + the unary canonical gates (`NTI.T`, `PTI.T`, `MTI.T`, `CYU.T`, `CYD.T`), `TLUT.T` with the `CONS.T` and `KIMP.T` canonical tables | rebel6-extensions.md, "Ztl — ternary logic" | A1.6 |
| `trap_causes_t.tas` | Cause-code cross-check asserting the **platform** table's numbers: `EBREAK.T` → −10, `ECALL.T` from M → −9 (S → −8 and U → −7 stubbed pending privilege plumbing); `mepc` points at the faulting instruction; resume via `TRET.T` | rebel6-platform.md, "Balanced cause codes" / "Trap entry" | A1.3 |

Known open points in the drafts:

- `trap_causes_t.tas` uses the platform register *names* (`mtvec`,
  `mcause`, `mepc`) for the negative-range registers; the R2R
  assembler syntax for X-1…X-23 is not yet defined. It also assumes
  that once a handler is installed via `mtvec`, `ECALL.T` traps to it
  instead of being intercepted as semihosting (the draft uninstalls
  the handler before the final exit call for exactly this reason).
  The S- and U-mode `ECALL.T` arms (−8, −7) still need `mstatus.MPP`
  staging plus `TRET.T` descent once those are testable.
- `ztl_t.tas` assumes the assembler supports the unary gate
  pseudo-instructions (the spec makes them "fully assembler-supported");
  if only raw `TLUTI.T` lands first, the pseudo checks can be rewritten
  with the inline tables given in the comments.
- Simulator behaviour today: every mnemonic in these drafts aborts the
  run with `Undefined instruction <mnemonic>` (process exit 134), and
  `EBREAK.T` / `TRET.T` abort as unimplemented traps — which is why
  these files live here and not in `../tests/`.

When a group lands in the simulator, run the draft, re-verify each
expected constant independently, then move the file into `../tests/`
and add it to the coverage table in `../README.md`.
