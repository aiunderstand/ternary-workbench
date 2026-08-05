# REBEL-2 Instruction Set Reference (V2.2)

Instructions use balanced-ternary trits: **+**, **0**, **-**.
Each 10-trit word is split into five 2-trit fields. Same register file and Harvard architecture as V2.0.

## Changes from V2.0 → V2.2

- **NOP.T** encoding changed to `0000000000` (opcode `00`).
- **MUL.T** moved to opcode `--` (func `-0`); opcode `0-` now hosts MAJV.T.
- **ADDI.T, LI.T, MV.T, STI.T** and the Min/Max family have new opcodes.
- **New instructions:** MULH.T, DIV.T, REM.T, MOD.T, LD2.T, ST2.T, NTI.T, PTI.T, MTI.T, CYCLEUP.T, SWAP.T, MAJV.T, MINI.T, MAXI.T, CMPWI.T, CMPTI.T, BNE.T, KIMP.T, FENCE.T, WFI.T, IRET.T, EBREAK.T, ECALL.T.
- **Memory access:** LD2.T and ST2.T add optional memory load/store (V2.2 extension; base REBEL-2 is register-oriented).
- **Expanded format set:** R, I, D, E (MAJV 3-source), U (unary, rs2 fixed to 00).

## Instruction Formats

Fields are shown MST-first (most significant trit at left).

| Format | Trits [9:8] | Trits [7:6] | Trits [5:4] | Trits [3:2] | Trits [1:0] | Examples |
|--------|-------------|-------------|-------------|-------------|-------------|---------|
| **R** | func | rd1 | rs2 | rs1 | op | ADD.T, SUB.T, MUL.T, MULH.T, DIV.T, REM.T, MOD.T — register × register, func selects variant |
| **I** | func | rd1 | imm | rs1 | op | ADDI.T, MINI.T, MAXI.T, SL*.T, SR*.T, SC.T, CMP*I.T — immediate operand, func selects variant |
| **D** | rs2 | rd1 | rs1(imm) | rs1 | op | LI2.T, BCEG.T — both rd1 and rd2 are explicit operand registers |
| **E** | rs3 | rd1 | rs2 | rs1 | op | MAJV.T — 3 sources; rs3 encoded in Rd2 slot |
| **U** | 00 | rd1 | func | rs1 | op | STI.T, NTI.T, PTI.T, MTI.T, CYCLEUP.T, SWAP.T — unary; rs2 fixed to 00 |

**Registers:** X-4 (--), X-3 (-0), X-2 (-+), X-1 (0-), X0 (00, hardwired zero), X1 (0+), X2 (+-), X3 (+0), X4 (++).
Immediates (imm2) are 2-trit balanced integers in range [−4, 4].

## Mnemonics

| Mnemonic | Format | Opcode | Operands | Func / Rd2 | Category | Description |
|----------|--------|--------|----------|------------|----------|-------------|
| ADD.T | R | -- | rd1, rs1, rs2 | 00 | Ternary ALU | rd1 = rs1 + rs2 |
| ADDI.T | I | 00 | rd1, rs1, imm2 | 00 | Ternary ALU | rd1 = rs1 + imm2 |
| BCEG.T | D | +0 | rd1, rd2, rs1, rs2 | | Ternary Branch | if rs1 == rs2 goto rd1; if rs1 > rs2 goto rd2 |
| BNE.T | R | +- | rd1, rs1, rs2 | 0- | Ternary Branch | if rs1 ≠ rs2 jump to rd1 |
| CMPTI.T | I | +- | rd1, rs1, imm2 | -+ | Ternary Compare | rd1 = tritwise compare(rs1, imm2) |
| CMPT.T | R | +- | rd1, rs1, rs2 | -- | Ternary Compare | rd1 = tritwise compare(rs1, rs2) |
| CMPWI.T | I | +- | rd1, rs1, imm2 | -0 | Ternary Compare | rd1 = wordwise compare(rs1, imm2) |
| CMPW.T | R | +- | rd1, rs1, rs2 | 00 | Ternary Compare | rd1 = wordwise compare(rs1, rs2) |
| CYCLEUP.T | U | 00 | rd1, rs1 | +0 | Ternary ALU | rd1 = cyclic shift up of rs1 |
| DIV.T | R | -- | rd1, rs1, rs2 | 0- | Ternary ALU | rd1 = rs1 ÷ rs2 |
| EBREAK.T | R | ++ | | +0 | Ternary Control | Debug breakpoint |
| ECALL.T | R | ++ | | ++ | Ternary Control | Environment call |
| FENCE.T | R | ++ | | -0 | Ternary Control | Memory fence |
| IRET.T | R | ++ | | +- | Ternary Control | Return from interrupt |
| JAL.T | I | ++ | rd1, imm2 | 0+ | Ternary Control | rd1 = PC+1; jump to PC+imm2 |
| JALR.T | I | ++ | rd1, rs1, imm2 | 00 | Ternary Control | rd1 = PC+1; jump to rs1+imm2 |
| KIMP.T | R | +- | rd1, rs1, rs2 | 0+ | Ternary ALU | rd1 = Kleene implication(rs1, rs2) |
| LD2.T | I | 00 | rd1, rs1 | -0 | Ternary Load | rd1 = memory[rs1] (load trit-pair) |
| LI.T | I | 00 | rd1, imm2 | 00 | Pseudo | rd1 = imm2 |
| LI2.T | D | -+ | rd1, rd2, rs1, rs2 | | Ternary ALU | rd1 = rs1;  rd2 = rs2 (load two immediates) |
| LPC.T | I | ++ | rd1, imm2 | 0- | Ternary Control | rd1 = PC+imm2; no jump |
| MAJV.T | E | 0- | rd1, rs1, rs2, rs3 | | Ternary ALU | rd1 = majority(rs1, rs2, rs3) |
| MAXI.T | I | -0 | rd1, rs1, imm2 | 0- | Ternary Min/Max | rd1 = max-tritwise(rs1, imm2) |
| MAXT.T | R | -0 | rd1, rs1, rs2 | +0 | Ternary Min/Max | rd1 = max-tritwise(rs1, rs2) |
| MAXW.T | R | -0 | rd1, rs1, rs2 | +- | Ternary Min/Max | rd1 = max-wordwise(rs1, rs2) |
| MINI.T | I | -0 | rd1, rs1, imm2 | -+ | Ternary Min/Max | rd1 = min-tritwise(rs1, imm2) |
| MINT.T | R | -0 | rd1, rs1, rs2 | -0 | Ternary Min/Max | rd1 = min-tritwise(rs1, rs2) |
| MINW.T | R | -0 | rd1, rs1, rs2 | -- | Ternary Min/Max | rd1 = min-wordwise(rs1, rs2) |
| MOD.T | R | -- | rd1, rs1, rs2 | +- | Ternary ALU | rd1 = rs1 mod rs2 (floored, towards −∞) |
| MTI.T | U | 00 | rd1, rs1 | +- | Ternary ALU | rd1 = magnitude(rs1) |
| MULH.T | R | -- | rd1, rs1, rs2 | -+ | Ternary ALU | rd1 = high word of rs1 × rs2 |
| MUL.T | R | -- | rd1, rs1, rs2 | -0 | Ternary ALU | rd1 = rs1 × rs2 |
| MV.T | I | 00 | rd1, rs1 | 00 | Pseudo | rd1 = rs1 |
| NOP.T | I | 00 | | 00 | Pseudo | No-op (0000000000) |
| NTI.T | U | 00 | rd1, rs1 | 0- | Ternary ALU | rd1 = negative ternary inversion(rs1) |
| PTI.T | U | 00 | rd1, rs1 | 0+ | Ternary ALU | rd1 = positive ternary inversion(rs1) |
| REM.T | R | -- | rd1, rs1, rs2 | 0+ | Ternary ALU | rd1 = rs1 rem rs2 (truncated, towards zero) |
| SC.T | I | 0+ | rd1, rs1, imm2 | 00 | Ternary Shift | Cyclic shift by imm2 |
| SLIN.T | I | 0+ | rd1, rs1, imm2 | -- | Ternary Shift | Shift left by imm2, fill with − |
| SLIZ.T | I | 0+ | rd1, rs1, imm2 | -0 | Ternary Shift | Shift left by imm2, fill with 0 |
| SLIP.T | I | 0+ | rd1, rs1, imm2 | -+ | Ternary Shift | Shift left by imm2, fill with + |
| SRIN.T | I | 0+ | rd1, rs1, imm2 | +- | Ternary Shift | Shift right by imm2, fill with − |
| SRIZ.T | I | 0+ | rd1, rs1, imm2 | +0 | Ternary Shift | Shift right by imm2, fill with 0 |
| SRIP.T | I | 0+ | rd1, rs1, imm2 | ++ | Ternary Shift | Shift right by imm2, fill with + |
| ST2.T | U | 00 | rs1, rs2 | -+ | Ternary Store | memory[rs1] = rs2 (store trit-pair) |
| STI.T | U | 00 | rd1, rs1 | -- | Ternary ALU | rd1 = −rs1 (standard ternary inversion) |
| SUB.T | R | -- | rd1, rs1, rs2 | -- | Ternary ALU | rd1 = rs1 − rs2 |
| SWAP.T | U | 00 | rd1, rs1 | ++ | Ternary ALU | swap contents of rd1 and rs1 |
| WFI.T | R | ++ | | -+ | Ternary Control | Wait for interrupt |

**Comments:** `#`, `;`, `$`, `//` strip to end-of-line. "quoted" strings and (parenthesised) annotations are also removed.
**Labels** are resolved within a page (up to 9 instructions).
