# REBEL-6 conformance suite

Self-checking REBEL-6 assembly programs, one file per architectural
instruction (or small instruction family), plus directed edge cases.
This is workstream **A2** of the implementation plan: the shared
conformance asset every implementation meets at — today the R2R
reference simulator, later the Workbench C# simulator and MRCS
hardware, unchanged.

Ground truth is the frozen spec in `../docs/`:
[rebel6-isa.md](../docs/rebel6-isa.md),
[rebel6-platform.md](../docs/rebel6-platform.md),
[rebel6-abi.md](../docs/rebel6-abi.md),
[rebel6-extensions.md](../docs/rebel6-extensions.md).

## How to run

```sh
REBEL6_SIM=<path-to-executor> ./run.sh [pattern]   # execute on a simulator
./assemble.sh [pattern]                            # assemble through the Workbench
```

- `REBEL6_SIM` defaults to the R2R reference executor
  (`RV32IToREBEL/build/RV32IToREBEL`), which executes `.tas` REBEL-6
  assembly directly.
- `assemble.sh` exercises the other half of the shared-asset property:
  every test must also assemble through the Workbench C# assembler
  (`twb rebel6 asm`). No execution happens there — PASS means the
  Workbench accepts the dialect and produces machine code. It builds
  the CLI itself; `TWB_CLI=<path-to-TernaryWorkbench.Cli.dll>`
  overrides.
- The optional `pattern` is a shell glob over test basenames, e.g.
  `./run.sh 'shifts_*'`.
- Both runners print `PASS`/`FAIL` per file plus a summary, and exit
  nonzero if any test fails.

## Conventions

- **Pass** = the program exits through the ABI contract
  `li.t a7, 93` / `li.t a0, 0` / `ecall.t`, which the executor
  propagates as process exit code 0.
- **Fail** = exit with `a0` = the **index of the failed check** inside
  the file (checks are numbered in comments), so a red run identifies
  the failing assertion from the exit code alone.
- `a0` doubles as the running check index: each check loads its index
  into `a0` before asserting, and the shared `fail:` tail exits with
  whatever index was live.
- Tests avoid the instruction under test in their own checking
  scaffolding where possible; the scaffolding baseline
  (`li.t`, `bne.t`, `ecall.t`, `jal.t`) is itself covered by dedicated
  tests.
- Files named `*.tas.xfail` are spec-correct tests that a known
  simulator bug breaks; the runner reports them as `XFAIL` and skips
  them (they never count as failures). None currently exist.
- A test may ship a **stream-replay script** as `<test>.script` next
  to its `.tas` (rebel6-platform.md "Streaming registers" makes
  scripted replay a conformance requirement on simulators); the
  runner auto-detects it and passes it to the executor as
  `--stream-script <file>`. The script format is currently
  R2R-defined (`<stream-index> <at-count> <value>` — see the R2R
  README) pending a spec ruling; only `stream0..stream2` are
  scriptable, and unscripted streams read 0.
- `tests-pending/` held drafts for groups the simulator could not run
  yet; it is now **empty** — the last draft (`trap_causes_t`, keyed to
  A1.3) graduated to `tests/` when the trap architecture landed — see
  [tests-pending/README.md](tests-pending/README.md).

## Coverage

All 49 tests pass against the R2R reference simulator as of this
commit, and all of them except `legacy_shift_aliases_t` (see the
dialect notes) also assemble through the Workbench assembler via
`assemble.sh`.

| Test | Instructions | Directed edge cases |
|------|--------------|---------------------|
| `add_t` | `ADD.T` | balanced wrap `MAX+1 = MIN` (via `li.t 141214768240`) and `MIN-1 = MAX` |
| `sub_t` | `SUB.T` | wrap in both directions |
| `sti_t` | `STI.T` | negate is total: `-MIN = MAX`, `-MAX = MIN`, `-0 = 0` |
| `cmp_t` | `CMP.T` | all three outcomes (+1/0/−1), negative operands, `MIN` vs `MAX` |
| `min_t` | `MIN.T` | wordwise (not tritwise) select, commutativity |
| `max_t` | `MAX.T` (+`MIN.T`, `STI.T`) | duality `min(a,b) = -max(-a,-b)` on two vectors |
| `slt_t` | `SLT.T` | <, ==, > outcomes, signed compare |
| `addi_t` | `ADDI.T` | imm12 extremes ±265720, wrap via immediate path |
| `slti_t` | `SLTI.T` | <, ==, > outcomes, negative immediate |
| `or_t` / `and_t` / `xor_t` | `OR.T` / `AND.T` / `XOR.T` | full 9-pair spec truth table in one vector (a=9464, b=6056 enumerate every trit pair); identities: max-identity is the all-minus word, min-identity all-plus, `xor(x,0) = 0`, `xor(MAX,MAX) = MIN` |
| `ori_t` / `xori_t` / `andi_t` | `ORI.T` / `XORI.T` / `ANDI.T` | same truth-table vector via imm12; `ori(x,0)` is **not** identity |
| `mv2_t` | `MV2.T` | reads-before-writes proven by the swap form `mv2.t a, b, b, a` |
| `majv_t` | `MAJV.T` | per-trit majority; two-equal-words dominance; all-distinct lane → 0 (median reading, see note below) |
| `minv_t` | `MINV.T` | minority = negated majority, cross-checked against `sti(majv)` |
| `li2_t` | `LI2.T` | 6-trit immediate extremes ±364 |
| `shi_fills_t` | `SHIN.T`, `SHIZ.T`, `SHIP.T` | signed amounts both directions: each fill lands in **every** vacated trit for `k > 0` (low trits) and `k < 0` (`3^22`, `3^23` terms); `k = 0` identity; `±24` and over-range `±30` all-fill |
| `sh_reg_t` | `SHN.T`, `SHZ.T`, `SHP.T` | register amounts both signs, all three fills, amount-0 identity; **low-4-trit rule** proven by rs2 = 79 (positive word, low 4 trits = −2) and rs2 = 408 (low 4 trits = +3 under nonzero higher trits); register `|k| ≥ 24` all-fill |
| `rot_t` | `ROT.T` | signed amount mod 24: `rot ±24` = identity; `rot k` then `rot 24−k` = identity; `rot k` then `rot −k` = identity; `rot −k` = `rot 24−k`; MST↔LST wraparound both directions |
| `rotr_t` | `ROTR.T` | register-amount rotate, signed mod 24: agreement with `ROT.T` for `k = +5` and `k = −7`; `rotr k` then `rotr −k` = identity; `rotr 24` = identity; directed ±1 wraparound |
| `legacy_shift_aliases_t` | deprecated `SLIZ.T`, `SRIZ.T`, `SLZ.T`, `SRZ.T` | deprecated-alias regression guard only (aliases of `SHIZ.T`/`SHZ.T` with negated amounts for the right forms) — delete with the aliases |
| `lw_sw_t` | `LW.T`, `SW.T` | word round-trips incl. `MAX`/`MIN`, adjacent words independent |
| `lh_lt_narrow_t` | `LH.T`, `LT.T`, `SH.T`, `ST.T` | narrow loads zero-pad (negative values survive exactly); narrow stores truncate balanced (tryte mod 729 → ±364, halfword mod 3¹² → ±265720); tryte store touches only its tryte; little-endian tryte order |
| `lwa_swa_t` | `LWA.T`, `SWA.T` | absolute round-trips, agreement with register-indirect `LW.T` |
| `beq_t` / `bne_t` / `blt_t` / `bge_t` | `BEQ.T` / `BNE.T` / `BLT.T` / `BGE.T` | taken **and** not-taken per instruction, signed compares, `MIN`/`MAX` operands |
| `bcgs_t` | `BCGS.T` | all three arms (> → off1, < → off2, == falls through) + numeric displacement |
| `bceg_t` | `BCEG.T` | all three arms (== → off1, > → off2, < falls through) + numeric displacement |
| `jal_jalr_t` | `JAL.T`, `JALR.T` | link = PC+1 proven by consecutive call sites linking 2 apart; call/return; register+offset jump entering past an instruction |
| `ecall_exit_t` | `ECALL.T` (semihosting) | `write` (a7=64) returns tryte count in `a0`; `exit` (a7=93) reports `a0` as status |
| `fence_wfi_t` | `FENCE.T`, `WFI.T` | `FENCE.T` orders nothing observable single-hart; `WFI.T` with the CLINT timer armed in `mie` as wake source (global `MIE` = 0, so nothing is delivered) — portable across stalling and NOP-with-hint implementations; state preserved |
| `clint_t` | CLINT-analog MMIO (A1.4): `MTIME`, `MTIMECMP`, `MSIP` via tryte-granular `LT.T`/`ST.T`, lines observed in `mip` | MTIME monotone; **wrap-aware** compare discriminator (`MTIMECMP = MIN` with small MTIME → *not* pending, where naive signed ≥ says pending); pending/clear for `MTIMECMP` = 0/`MAX`; `MSIP` set/clear reflected in `mip` trit 1; store to read-only `MTIME` → −6; `mie` = 0 throughout, so no delivery — race-free |
| `mmio_faults_t` | MMIO access semantics (normative): tryte granularity + population | word-wide `LW.T`/`SW.T` and halfword `LH.T` on CLINT registers → misaligned −3/−4; load/store on reserved device slot 11 → access fault −5/−6; trap-count guard against silently completed accesses |
| `x0_t` | X0 semantics | writes via `li.t`/ALU/`addi`/load all discarded; reads return 0 |
| `pseudos_t` | `NOP.T`, `MV.T`, `SWAP.T`, `BGT.T`, `BLE.T` (pseudos) | 1:1 assembler expansions: `nop.t` changes no state; `mv.t` copies and preserves the source; `swap.t` proven an exchange (not a double move); `bgt.t`/`ble.t` taken **and** not-taken incl. the `==` boundary and a signed compare |
| `comments_t` | comment markers (`#`, `;`, `$`, `//`) | all four strip to end-of-line from any position: trailing same-line comments after instructions and labels; `;` is a comment marker, not a separator — code hidden after a `;` (even inside a `#` comment) is inert |
| `m_ext_t` | `MUL.T`, `MULH.T`, `DIV.T`, `REM.T`, `MOD.T`, `MAC.T` (M extension) | balanced wrap `MAX*2 = -1`; `MULH.T:MUL.T` composes the exact product (high word 1 over low word −1); truncating division all sign pairs; `MIN / -1 = MAX` without trap; div-by-zero q=0, `x rem 0 = x`, `x mod 0 = x`; `REM` sign = dividend, `MOD` sign = divisor; `MAC.T` single balanced wrap |
| `ztb_t` | `CLZT.T`, `TCNT.T` (Ztb extension) | `clzt(0) = 24`, sign-blind (`clzt(-1) = clzt(1) = 23`), top-trit boundary (`3^23` → 0, `3^22` → 1); `tcnt` of 0/`MAX`/`MIN` (0/24/24) and hand-computed popcounts |
| `ztl_t` | `TLUT.T`, `TLUTI.T` + unary gate pseudos `NTI.T`, `PTI.T`, `MTI.T`, `CYU.T`, `CYD.T` (Ztl extension) | each unary canonical table applied to a vector exercising all three inputs, high-lane `f(0)` propagation verified over all 24 lanes; raw `TLUTI.T` matches its pseudo; `TLUT.T` with the `CONS.T` and `KIMP.T` canonical tables over the 9-pair enumeration vector (a=9464, b=6056) hitting every table position |
| `stream_replay_t` | streaming registers `stream0..stream2` (`X-12..X-14`) under scripted replay (A1.7) + `minstret`/`mcycle` reads | scripted values honored per the timing rule (value from the entry with the largest at-count ≤ the retired count); value-change timing (switch at count 100 never observed early, observed by a bounded spin); unscripted stream reads 0; streaming **write** still faults −11 under replay (cause + `mepc` verified, trap-counter guard); `mcycle`/`minstret` stay hardware-written — scripts never touch them |
| `trap_causes_t` | `EBREAK.T`, `TRET.T`, `ECALL.T` (trap path), system registers `mtvec`/`mepc`/`mcause`/`mstatus`/`sstatus`/`sepc` (A1.3) | the **platform** cause table asserted per level: ebreak → −10, ecall from M/S/U → −9/−8/−7; `mepc` points AT the trapping instruction; `TRET.T` resume; M→S descent via `mstatus.MPP`, S→U via the `sstatus.SPP` view + S-bank `TRET.T`; semihosting stays M-only (a7 = 93 from S/U traps); a verified-trap counter guards against calls being serviced instead of trapped |

Scaffolding instructions `LI.T`, `BNE.T`, `JAL.T`, `ECALL.T` are
exercised by every file in addition to their dedicated tests.

**Not yet covered**: `AIPC.T`, vectored trap dispatch and the −11
privilege faults (covered by the R2R repo's
`RiscvTests/Rebel6Tests/traps_*.tas` microtests pending promotion
here; the −11 streaming-**write** fault is now covered by
`stream_replay_t`), interrupt *delivery* — cause `+1`/`+2` dispatch above `tvec`,
`mepc` = next instruction, `WFI.T` wake (timing-sensitive; covered by
the R2R repo's `RiscvTests/Rebel6Tests/dev_clint.tas`), SIMCON/SIMFB
(host-side observable; `dev_simcon.tas`/`dev_simfb.tas` there),
negative assembler tests, and the Base Binary group.

### Spec observation: `MAJV.T` all-distinct lane

The spec defines `MAJV.T` as per-trit "majority" but does not pin the
all-distinct lane `(−, 0, +)`, which has no majority. The reference
simulator returns `0` — the median reading, which is the standard
majority-gate extension and keeps `MINV.T = −MAJV.T` consistent.
`majv_t` check 4 asserts this reading; the spec should state it
explicitly.

## Dialect notes (Workbench canonical syntax and the R2R parser)

The suite is written in the dialect the R2R executor's parser accepts
(modelled on `RiscvTests/Rebel6Tests/smoke.tas`). The divergences
found while building the suite — `;` acting as a statement separator
even inside comments, trailing same-line comments crashing the
parser, the spec pseudo-instructions being unimplemented, and unknown
mnemonics dying in a C++ abort (exit 134) — are fixed in the R2R
parser: comments now conform to the spec (`comments_t` covers them),
the pseudos expand 1:1 in the assembler (`pseudos_t` covers them),
and unknown/unimplemented mnemonics (the A-extension group — the
M/Ztb/Ztl groups landed with A1.5/A1.6, `ebreak.t`/`tret.t` with
A1.3) print `unsupported instruction '<name>' (<reason>)` and exit 2.
The A1.3 dialect addition: the platform register names (`mtvec`,
`mepc`, `mcause`, `mstatus`, `sstatus`, `sepc`, `stream0`, ...) are
accepted as register operands, normalized to their negative indices
(`X-1` ... `X-22`). Suite convention remains own-line `#` comments except where a
test deliberately exercises the other styles.

**Workbench dialect parity.** The Workbench assembler
(`twb rebel6 asm`, `Rebel6Assembler.AssembleProgram`) accepts the same
dialect: the ABI register names (`zero`, `ra`, `sp`, `a0`–`a7`,
`s0`–`s11`/`fp`, `t0`–`t6`, extended pool `e0`…; `gp` is rejected —
the ABI retires it), the platform register names normalized to
`X-1 … X-22`, the directive set `.text`/`.data`/`.word`/`.zero` with
flat pre-linker data placement after the instruction image (matching
R2R's 4-tryte-aligned layout), and leading-underscore labels. The one
deliberate divergence: R2R's deprecated register right-shift aliases
(`srz.t` etc.) negate the amount at runtime, which a spec-conformant
assembler cannot express — the ratified signed-shift design retired
`SR{N,Z,P}.T` outright — so `assemble.sh` skips
`legacy_shift_aliases_t` until the aliases are deleted.

Documented dialect behavior, kept by design:

1. **Loads/stores accept dual syntax.** Every ternary load/store and
   `jalr.t` accepts both the spec-canonical flat form
   (`lw.t rd, rs1, imm12` / `sw.t rs1, rs2, imm12`, base register =
   rs1, `mem[rs1+imm12] = rs2`) and the RISC-V-style `off(base)` form
   the translator emits; the assembler normalizes both to one
   internal form. Flat-form base-first matches the spec's
   encoding-order operand column, but note the store *source* is the
   second operand — the reverse of RISC-V assembler convention. The
   suite uses the flat form.
2. **Flat pre-linker addressing:** `li.t` accepts both data labels
   and code labels as its imm24, and `jalr.t` through such an address
   works. There are no `%`-modifier relocations in this dialect. This
   is revisited in Phase B, when REBEL-ld introduces the I/D
   address-space split.

## Shift tests: signed-shift design ratified

The shift tests encode the **ratified signed-shift design** (Option A
full collapse plus `ROTR.T`, accepted 2026-08-09 — see
`REBEL-toolchain/rebel6-signed-shifts-proposal.md`): one shift family
`SH<f>.T`/`SHI<f>.T` with a signed amount whose sign is the direction
(`k > 0` toward MST, `k < 0` toward LST, `k = 0` identity,
`|k| ≥ 24` all-fill), register amounts read from the low 4 trits of
rs2 (±40), and rotates (`ROT.T`/`ROTR.T`) signed mod 24. The old
per-direction spellings (`SL*`/`SLI*`/`SR*`/`SRI*`) survive in the
simulator as deprecated aliases; `legacy_shift_aliases_t` guards them
and is deleted when they are removed.
