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
REBEL6_SIM=<path-to-executor> ./run.sh [pattern]
```

- `REBEL6_SIM` defaults to the R2R reference executor
  (`RV32IToREBEL/build/RV32IToREBEL`), which executes `.tas` REBEL-6
  assembly directly.
- The optional `pattern` is a shell glob over test basenames, e.g.
  `./run.sh 'shifts_*'`.
- The runner prints `PASS`/`FAIL` per file plus a summary, and exits
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
- `tests-pending/` holds drafts for groups the simulator cannot run
  yet (M, Ztb, Ztl, trap causes) — see
  [tests-pending/README.md](tests-pending/README.md).

## Coverage

All 37 tests pass against the R2R reference simulator as of this
commit.

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
| `fence_wfi_t` | `FENCE.T`, `WFI.T` | execute as observable no-ops single-hart, state preserved |
| `x0_t` | X0 semantics | writes via `li.t`/ALU/`addi`/load all discarded; reads return 0 |

Scaffolding instructions `LI.T`, `BNE.T`, `JAL.T`, `ECALL.T` are
exercised by every file in addition to their dedicated tests.

**Not yet covered** (beyond `tests-pending/`): `AIPC.T`, `EBREAK.T`,
`TRET.T`, the spec's pseudo-instructions (blocked — see divergence 3
below), negative assembler tests, and the Base Binary group.

### Spec observation: `MAJV.T` all-distinct lane

The spec defines `MAJV.T` as per-trit "majority" but does not pin the
all-distinct lane `(−, 0, +)`, which has no majority. The reference
simulator returns `0` — the median reading, which is the standard
majority-gate extension and keeps `MINV.T = −MAJV.T` consistent.
`majv_t` check 4 asserts this reading; the spec should state it
explicitly.

## Dialect divergences (Workbench canonical syntax vs R2R parser)

The suite is written in the dialect the R2R executor's parser actually
accepts (modelled on `RiscvTests/Rebel6Tests/smoke.tas`). Divergences
from the spec/Workbench canonical syntax found while building the
suite, each verified by experiment:

1. **`;` is a statement separator, not a comment character — even
   inside comments.** The spec says `#`, `;`, `$`, `//` all strip to
   end-of-line. In the R2R parser, text after `;` on a `#` comment
   line is *parsed and executed as code* (verified: a
   `# comment; li.t a0, 7` line changed the program's exit status
   to 7). Never use `;` anywhere in a `.tas` file.
2. **Trailing same-line comments crash the parser.** `li.t a0, 0 # c`
   aborts the run (exit 134). Comments must sit on their own lines,
   starting with `#`.
3. **The spec's pseudo-instructions are not implemented.** `nop.t`,
   `mv.t`, `swap.t`, `bgt.t` (and by extension `ble.t`) parse but
   abort at execution with `Undefined instruction`. The suite spells
   out the expansions instead (`add.t rd, rs, zero` for moves, swapped
   `blt.t`/`bge.t` operands, `mv2.t a, b, b, a` for swap).
4. **Load/store operand order is a flat list, base register first.**
   R2R dialect: `sw.t base, src, offset` and `lw.t rd, base, offset`.
   There is no RISC-V-style `off(base)` addressing form. (Base-first
   for stores matches the spec's encoding-order operand column
   `rs1, rs2, imm12` with `mem[rs1+imm12] = rs2`, but note the
   *source* is the second operand — the reverse of RISC-V assembler
   convention.)
5. **Flat pre-linker addressing:** `li.t` accepts both data labels and
   code labels as its imm24, and `jalr.t` through such an address
   works. There are no `%`-modifier relocations in this dialect.
6. **Unimplemented instructions abort rather than diagnose.** Any
   unknown-to-the-executor mnemonic (all of M/Ztb/Ztl, plus the
   pseudos above) terminates the process with a C++ abort (exit 134),
   not a parse error — a failing conformance run may therefore show
   exit 134 rather than a check index. `ebreak.t`/`tret.t` parse but
   abort as unimplemented traps.

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
