# REBEL-6 Instruction Set Reference

## Overview & Comparison with REBEL-2

REBEL-6 is the successor to REBEL-2, designed for real-world applications rather than education.
It extends REBEL-2 from a minimal 10-trit, 9-register ISA to a full 32-trit, 729-register architecture
with direct RV32I binary compatibility. The **.t** suffix marks ternary instructions;
binary (RV32I-compatible) instructions have no suffix. PC increments by 1 per instruction slot.

| Property | REBEL-2 | **REBEL-6** |
|----------|---------|------------|
| Radix | Balanced ternary | **Balanced ternary** |
| Instruction width | 10 trits | **32 trits** |
| Instruction count | 23 + 3 pseudo (9 opcode groups) | **34 ternary + 27 binary + 8 pseudo = 69** |
| Register count | 9 (X-4 … X4) | **729 (X-364 … X364; X0 = zero)** |
| Register width | 2 trits | **24 trits** |
| Operand count | 2–4 | **2–4** |
| PC jump/instr. | 1 | **1** |
| Binary compat. | None | **Full RV32I (L-type, hardware flag)** |
| Formats | R, I, D | **R, I, B, D, X, G, Y, L** |
| Primary use | Education | **Real-world applications** |

## RV32I Binary Compatibility (L-type)

A hardware flag enables direct execution of existing RV32I 32-bit binaries — no recompilation needed.
Binary is a strict subset of ternary: each 32-bit RV32I instruction fits exactly in 32 trits by
mapping binary 0 → trit **−** and binary 1 → trit **+**. A hardware binary-ternary ALU
and instruction translator handle the conversion transparently. The `xx-0` opcode group
provides a software-accessible RV32I instruction space for explicit binary-mode code.

## Instruction Formats

Fields shown MST-first. 4-trit opcodes: last trit = 0. 2-trit opcodes (G/Y): last trit ≠ 0.
B-type carries two 6-trit PC-relative displacements: three-way branches use both, stores and the
binary two-way branches use `off2` only and leave `off1` zero.

| Format | Layout (MST → LST) | Examples |
|--------|--------------------|---------|
| **R** | rs1[6] \| rs2[6] \| rd1[6] \| rd2[6] \| func[4] \| opcode[4] | ADD.T, CMP.T, STI.T, ADD, SLL |
| **I** | rs1[6] \| imm[11:6][6] \| rd1[6] \| imm[5:0][6] \| func[4] \| opcode[4] | ADDI.T, LW.T, JALR.T, ADDI, LW |
| **B** | rs1[6] \| rs2[6] \| off1[6] \| off2[6] \| func[4] \| opcode[4] | BCGS.T, BCEG.T, SW.T, BLTU, SW, SB |
| **G** | imm[23:12][12] \| rd1[6] \| imm[11:0][12] \| opc[2] | LWA.T, LI.T, JAL.T, AIPC.T |
| **X** | imm1[5:0][6] \| imm2[5:0][6] \| rd1[6] \| rd2[6] \| func[4] \| opcode[4] | LI2.T |
| **D** | rs1[6] \| rs2[6] \| rd1[6] \| rs3[6] \| func[4] \| opcode[4] | MAJV.T, MINV.T |
| **Y** | rs1[6] \| imm[23:0][24] \| opc[2] | SWA.T |
| **L** | RV32I 32-bit instruction format (binary compatibility, requires hardware flag) | native RV32I passthrough |

## Three-Way Branching

The ternary comparator produces a three-valued result — `rs1 < rs2`, `rs1 == rs2`, `rs1 > rs2` — the
same signal `CMP.T` writes to a register. A two-way branch discards two thirds of it. REBEL-6 has two
architectural ternary branches, each consuming the comparison directly and dispatching to three
destinations in one instruction. They differ only in **which outcome falls through to PC+1**:

| Instruction | rs1 > rs2 | rs1 == rs2 | rs1 < rs2 |
|-------------|-----------|------------|-----------|
| **BCGS.T** rs1, rs2, off1, off2 — **C**ompare, branch **G**reater / **S**maller | PC + off1 | PC + 1 | PC + off2 |
| **BCEG.T** rs1, rs2, off1, off2 — **C**ompare, branch **E**qual / **G**reater | PC + off2 | PC + off1 | PC + 1 |

Both displacements are 6-trit PC-relative offsets (−364 … +364) in the B-type `off1`/`off2` slots.
The fall-through outcome costs no encoding space, so a three-way branch is the same 32 trits and the
same single cycle as a two-way one. `BCEG.T` keeps REBEL-2's `BCEG.T` semantics, with PC-relative
displacements in place of REBEL-2's destination registers.

The third arrangement — *greater* falling through — needs no third instruction: `BCEG.T` with the
source operands swapped provides it, since swapping rs1 and rs2 exchanges the greater and smaller
outcomes.

### Two-way branches are pseudo-instructions

Between them the two branches make **every** two-way comparison branch synthesisable: pick the
instruction whose fall-through outcome is the one the predicate excludes, then point the remaining
two outcomes at the target `L` or at PC+1 (displacement `1`). REBEL-6 therefore has no architectural
two-way ternary branch at all — the familiar mnemonics are assembler pseudo-instructions that emit a
single three-way instruction:

| Pseudo-instruction | Expands to | Outcome left on the fall-through |
|--------------------|------------|----------------------------------|
| `BEQ.T rs1, rs2, L` | `BCEG.T rs1, rs2, L, 1` | smaller |
| `BNE.T rs1, rs2, L` | `BCGS.T rs1, rs2, L, L` | equal |
| `BLT.T rs1, rs2, L` | `BCGS.T rs1, rs2, 1, L` | equal |
| `BGT.T rs1, rs2, L` | `BCGS.T rs1, rs2, L, 1` | equal |
| `BGE.T rs1, rs2, L` | `BCEG.T rs1, rs2, L, L` | smaller |
| `BLE.T rs1, rs2, L` | `BCEG.T rs2, rs1, L, L` | greater (operands swapped) |

There is no cost to this: each pseudo-instruction is one instruction and one cycle, exactly as a
dedicated encoding would be. The disassembler always emits the architectural `BCGS.T`/`BCEG.T` form.

See the [Errata](#errata) for why the original RV32I-style branch group was replaced.

## Mnemonics

**Opcode groups (by last 2 trits):** `xx00` = Base Ternary (729 slots); `xx-0` = Base Binary / RV32I (729 slots); `xx+0` = Extensions (729 slots, reserved).
The upper 2 trits encode the instruction category — same in both ternary and binary:
`00`=I-type ALU, `0-`=Branch, `0+`=Store, `--`=R-type ALU, `-+`=I-type Load, `+-`=D/Control, `+0`=X/Imm, `++`=System.

**Func:** upper 2 trits always `00`; lower 2 trits (LST) shown in table discriminate the instruction.
Pseudo-instructions carry the func of the architectural instruction they expand to.

**2-trit long-immediate (last trit ≠ 0):** `++` LWA.T, `0+` LI.T, `-+` SWA.T, `+-` JAL.T, `0-` AIPC.T; `--` *reserved*.

**Comments:** `#`, `;`, `$`, `//` strip to end-of-line.

| Mnemonic | Format | Opcode | Func | Operands | Category | Description |
|----------|--------|--------|------|----------|----------|-------------|
| ADD.T | R | --00 | -- | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 + rs2 |
| SUB.T | R | --00 | -0 | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 − rs2 |
| SL.T | R | --00 | -+ | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 << rs2 |
| SR.T | R | --00 | 0- | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 >> rs2 |
| SLT.T | R | --00 | 00 | rd1, rs1, rs2 | Ternary ALU | rd1 = (rs1 < rs2) ? +1 : 0 |
| OR.T | R | --00 | 0+ | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 OR rs2 |
| XOR.T | R | --00 | +- | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 XOR rs2 |
| AND.T | R | --00 | +0 | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 AND rs2 |
| CMP.T | R | -000 | -- | rd1, rs1, rs2 | Ternary ALU | rd1 = three-way compare: +1, 0, or -1 |
| STI.T | R | -000 | -0 | rd1, rs1 | Ternary ALU | rd1 = −rs1 (standard ternary inversion) |
| ADDI.T | I | 0000 | 00 | rd1, rs1, imm | Ternary ALU | rd1 = rs1 + imm |
| SLI.T | I | 0000 | -- | rd1, rs1, imm | Ternary ALU | rd1 = rs1 << shamt |
| SRI.T | I | 0000 | -0 | rd1, rs1, imm | Ternary ALU | rd1 = rs1 >> shamt |
| SLTI.T | I | 0000 | -+ | rd1, rs1, imm | Ternary ALU | rd1 = (rs1 < imm) ? +1 : 0 |
| ORI.T | I | 0000 | 0- | rd1, rs1, imm | Ternary ALU | rd1 = rs1 OR imm |
| XORI.T | I | 0000 | 0+ | rd1, rs1, imm | Ternary ALU | rd1 = rs1 XOR imm |
| ANDI.T | I | 0000 | +- | rd1, rs1, imm | Ternary ALU | rd1 = rs1 AND imm |
| LW.T | I | -+00 | -- | rd1, rs1, imm | Ternary Load | rd1 = mem[rs1 + imm] (word) |
| LH.T | I | -+00 | -0 | rd1, rs1, imm | Ternary Load | rd1 = mem[rs1 + imm] (halfword) |
| LT.T | I | -+00 | -+ | rd1, rs1, imm | Ternary Load | rd1 = mem[rs1 + imm] (trit-word) |
| JALR.T | I | -+00 | 0- | rd1, rs1, imm | Ternary Control | rd1 = PC+1; PC = rs1 + imm |
| BCGS.T | B | 0-00 | -- | rs1, rs2, off1, off2 | Ternary Branch | rs1 > rs2 → PC+off1; rs1 < rs2 → PC+off2; else PC+1 |
| BCEG.T | B | 0-00 | -0 | rs1, rs2, off1, off2 | Ternary Branch | rs1 == rs2 → PC+off1; rs1 > rs2 → PC+off2; else PC+1 |
| SW.T | B | 0+00 | -- | rs1, rs2, off2 | Ternary Store | mem[rs1 + off2] = rs2 (word) |
| SH.T | B | 0+00 | -0 | rs1, rs2, off2 | Ternary Store | mem[rs1 + off2] = rs2 (halfword) |
| ST.T | B | 0+00 | -+ | rs1, rs2, off2 | Ternary Store | mem[rs1 + off2] = rs2 (trit-word) |
| MAJV.T | D | +-00 | -- | rd1, rs1, rs2, rs3 | Ternary ALU | rd1 = majority(rs1, rs2, rs3) |
| MINV.T | D | +-00 | -0 | rd1, rs1, rs2, rs3 | Ternary ALU | rd1 = minority(rs1, rs2, rs3) |
| LI2.T | X | +000 | -- | rd1, rd2, imm1, imm2 | Ternary ALU | rd1 = imm1;  rd2 = imm2 |
| NOP.T | I | 0000 | 00 | | Pseudo | no-op (all-zero 32 trits = ADDI.T X0, X0, 0) |
| MV.T | I | 0000 | 00 | rd1, rs1 | Pseudo | rd1 = rs1 (ADDI.T rd1, rs1, 0) |
| BEQ.T | B | 0-00 | -0 | rs1, rs2, offset | Pseudo | branch if rs1 == rs2 (BCEG.T rs1, rs2, offset, 1) |
| BNE.T | B | 0-00 | -- | rs1, rs2, offset | Pseudo | branch if rs1 ≠ rs2 (BCGS.T rs1, rs2, offset, offset) |
| BLT.T | B | 0-00 | -- | rs1, rs2, offset | Pseudo | branch if rs1 < rs2 (BCGS.T rs1, rs2, 1, offset) |
| BGT.T | B | 0-00 | -- | rs1, rs2, offset | Pseudo | branch if rs1 > rs2 (BCGS.T rs1, rs2, offset, 1) |
| BGE.T | B | 0-00 | -0 | rs1, rs2, offset | Pseudo | branch if rs1 ≥ rs2 (BCEG.T rs1, rs2, offset, offset) |
| BLE.T | B | 0-00 | -0 | rs1, rs2, offset | Pseudo | branch if rs1 ≤ rs2 (BCEG.T rs2, rs1, offset, offset) |
| LWA.T | G | ++ | — | rd1, imm24 | Ternary Load | rd1 = mem[imm24] (absolute word load) |
| LI.T | G | 0+ | — | rd1, imm24 | Ternary ALU | rd1 = imm24 (24-trit load immediate) |
| SWA.T | Y | -+ | — | rs1, imm24 | Ternary Store | mem[imm24] = rs1 (absolute word store) |
| JAL.T | G | +- | — | rd1, imm24 | Ternary Control | rd1 = PC+1; PC = PC + imm24 |
| AIPC.T | G | 0- | — | rd1, imm24 | Ternary Control | rd1 = PC + imm24 |
| ADD | R | ---0 | -- | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 + rs2 |
| SUB | R | ---0 | -0 | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 − rs2 |
| SLL | R | ---0 | -+ | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 << rs2 |
| SRL | R | ---0 | 0- | rd1, rs1, rs2 | Binary ALU | logical shift right |
| SRA | R | ---0 | 00 | rd1, rs1, rs2 | Binary ALU | arithmetic shift right |
| SLTU | R | ---0 | 0+ | rd1, rs1, rs2 | Binary ALU | rd1 = (rs1 <u rs2) ? 1 : 0 |
| OR | R | ---0 | +- | rd1, rs1, rs2 | Binary ALU | bitwise OR |
| XOR | R | ---0 | +0 | rd1, rs1, rs2 | Binary ALU | bitwise XOR |
| AND | R | ---0 | ++ | rd1, rs1, rs2 | Binary ALU | bitwise AND |
| ADDI | I | 00-0 | -- | rd1, rs1, imm | Binary ALU | rd1 = rs1 + imm |
| SLLI | I | 00-0 | -0 | rd1, rs1, imm | Binary ALU | logical left shift immediate |
| SRLI | I | 00-0 | -+ | rd1, rs1, imm | Binary ALU | logical right shift immediate |
| SRAI | I | 00-0 | 0- | rd1, rs1, imm | Binary ALU | arithmetic right shift immediate |
| SLTIU | I | 00-0 | 00 | rd1, rs1, imm | Binary ALU | rd1 = (rs1 <u imm) ? 1 : 0 |
| ORI | I | 00-0 | 0+ | rd1, rs1, imm | Binary ALU | bitwise OR immediate |
| XORI | I | 00-0 | +- | rd1, rs1, imm | Binary ALU | bitwise XOR immediate |
| ANDI | I | 00-0 | +0 | rd1, rs1, imm | Binary ALU | bitwise AND immediate |
| LW | I | -+-0 | -- | rd1, rs1, imm | Binary Load | load word |
| LH | I | -+-0 | -0 | rd1, rs1, imm | Binary Load | load halfword signed |
| LB | I | -+-0 | -+ | rd1, rs1, imm | Binary Load | load byte signed |
| LHU | I | -+-0 | 0- | rd1, rs1, imm | Binary Load | load halfword unsigned |
| LBU | I | -+-0 | 00 | rd1, rs1, imm | Binary Load | load byte unsigned |
| BLTU | B | 0--0 | 0+ | rs1, rs2, offset | Binary Branch | branch if rs1 <u rs2 |
| BGEU | B | 0--0 | +- | rs1, rs2, offset | Binary Branch | branch if rs1 ≥u rs2 |
| SW | B | 0+-0 | -- | rs1, rs2, offset | Binary Store | store word |
| SH | B | 0+-0 | -0 | rs1, rs2, offset | Binary Store | store halfword |
| SB | B | 0+-0 | -+ | rs1, rs2, offset | Binary Store | store byte |

## Errata

Corrections to the REBEL-6 definition as originally published in the MSc thesis of Bodahl. Neither
item is discussed in the 2025 ISMVL REBEL-6 paper, which does not cover the branch encoding.

### E-1 — the two-way ternary branch group is replaced by `BCGS.T` and `BCEG.T`

**Defect.** REBEL-6 adopted RV32I's two-way branch model wholesale (`BEQ.T`, `BNE.T`, `BLT.T`,
`BGE.T`). Every one of these instructions evaluates a three-valued comparison and then collapses it
to a binary taken/not-taken decision, discarding two thirds of the comparator's output. REBEL-2 did
not have this defect: its D-format `BCEG.T` dispatches to two explicit destinations with a third
implicit fall-through path. The capability was lost in the move from REBEL-2 to REBEL-6, and the loss
was not noted.

**Why two three-way branches and not one.** A three-way branch can give explicit destinations to only
two of the three comparison outcomes; the third is structurally pinned to PC+1. Which outcome that is
determines what can be synthesised from it, and no single choice covers everything:

- `BCEG.T` alone (REBEL-2's choice) pins **smaller** to the fall-through. Any synthesised predicate
  must therefore place "smaller than" on the not-taken side, but `rs1 ≠ rs2` = {smaller, greater}
  straddles both sides — so branch-if-not-equal cannot be expressed. This is precisely why REBEL-2
  V2.2 had to add a separate architectural `BNE.T`.
- `BCGS.T` alone pins **equal** to the fall-through, which gives `BNE.T` for free but leaves equality
  with no assignable destination. A compiler can only reach an equal-target by inverting the branch —
  `BCGS.T rs1, rs2, 2, 2` followed by a jump — costing an extra instruction on every `==` test.

Defining both, at a cost of one func slot in a group with 729, makes all six two-way comparison
branches synthesisable as single-instruction pseudo-instructions and leaves nothing that needs a
dedicated encoding. The third possible arrangement (greater falling through) is `BCEG.T` with the
source operands swapped and needs no instruction of its own.

**Resolution.**

- `BEQ.T`, `BNE.T`, `BLT.T` and `BGE.T` are removed as architectural instructions. They are retained,
  together with `BGT.T` and `BLE.T`, as assembler pseudo-instructions — see
  [Three-Way Branching](#three-way-branching).
- `BCGS.T` (func `--`) and `BCEG.T` (func `-0`) are the only architectural ternary branches, in
  branch opcode group `0-00`. They need no new format or encoding space: the B-type `off1` slot was
  already unused in every branch (E-2).

**Compatibility.** Assembly source using the two-way mnemonics continues to assemble unchanged; only
`BGT.T` and `BLE.T` are new. Machine code predating this erratum is *not* binary compatible and must
be reassembled — REBEL-6 has no hardware or software in production, so the branch func assignments
were reallocated from scratch rather than worked around. RV32I binary compatibility is unaffected:
L-type execution and the binary branch group `0--0` do not use the ternary branch group.

### E-2 — B-type carries two 6-trit displacements, not one 12-trit immediate

**Defect.** The B-type format was described as carrying a contiguous 12-trit immediate spanning the
rd1 and rd2 slots. The displacement is in fact encoded in the rd2 slot only — 6 trits, PC-relative
range −364 … +364 — with the rd1 slot left zero. This matches the Ternary Workbench reference
assembler, which has always range-checked branch and store offsets against ±364.

**Resolution.** B-type is documented as two independent 6-trit displacement slots:
`rs1[6] | rs2[6] | off1[6] | off2[6] | func[4] | opcode[4]`. Stores and the binary two-way branches
use `off2` alone; the three-way branches use both, which is what lets them fit the existing format
with no new encoding space. Programs needing a branch beyond ±364 instructions use `JAL.T` (24-trit
PC-relative) or `JALR.T`.
