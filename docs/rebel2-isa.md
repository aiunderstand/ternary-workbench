# REBEL-2 Instruction Set Reference (V2.0)

## 1.1 Overview

REBEL-2 (RISC-V-like Energy efficient Balanced tErnary Logic CPU, 2-trit address postfix) is a novel balanced ternary (radix-3) CPU implemented in MRCS (logic gates synthesis, transistor level simulation), SONIC (gate level simulation, ternary HDL export for FPGA/ASIC) and R2R (assembly level simulation, C to REBEL compiler toolchain). It follows Brousentsov's ternary principles: ternary logic, ternary memory, and balanced ternary encoding. REBEL-2 is a modern, compact, Harvard-style ternary processor optimized for simplicity and educational use rather than high-throughput, compact area commercial deployment.

**Key design points:**

- Balanced ternary (+, 0, −) native implementation using MRCS standard cell library.
- Harvard architecture: instructions in ROM, data in RAM; instruction and data accesses can occur in the same cycle.
- Register-oriented processing (not load/store); no external memory required for processing.
- Single-cycle instruction execution (instruction-read at rising edge, write at falling edge).
- Multi-page programs: each page holds up to 9 instructions; labels are resolved within a page.

## 1.2 Intended Use & Strengths

- Educational platform for embedded ternary computing concepts and teaching.
- Low-power, simplicity-focused designs and prototyping.
- Demonstrator of balanced ternary technology stack.
- Easily scalable to wider address widths (e.g., tryte width) and to REBEL-6, a binary-compatible 32-trit architecture for practical applications.

## 1.3 Architecture Summary

- **Radix:** Balanced ternary (trits: +, 0, −).
- **Data path width:** 2-trit memory address bus (postfix "−2"); 9 registers (X−4 … X4).
- **Architecture style:** Harvard (split instruction/data memories).
- **Model:** Register-processing (instructions operate on registers directly, low data movement).
- **Instruction timing:** Single-cycle; read at rising edge, write at falling edge.
- **ISA inspiration:** RISC-V principles (simplicity, shift complexity to software).

## Naming note — SC.T → ROT.T

The cyclic shift's mnemonic is renamed **`ROT.T`** across the REBEL family: REBEL-6's A extension
adopts the RISC-V atomics names, in which `SC.T` is store-conditional, and one mnemonic cannot name
both (see [REBEL-6 Errata E-3](rebel6-isa.md#e-3--the-cyclic-shift-is-renamed-sct--rott)).
The encoding is unchanged — this is a rename, not a re-encoding. `SC.T` remains accepted as a
deprecated alias on REBEL-2; disassembly prints `ROT.T`.

## Instruction Formats

Fields are shown MST-first (most significant trit at left).

| Format | Trits [9:8] | Trits [7:6] | Trits [5:4] | Trits [3:2] | Trits [1:0] | Examples |
|--------|-------------|-------------|-------------|-------------|-------------|---------|
| **R** | func | rd1 | rs2 | rs1 | op | ADD.T, MUL.T, CMP*.T — register × register, func selects variant |
| **I** | func | rd1 | imm | rs1 | op | ADDI.T, SL*.T, SR*.T, ROT.T, JAL.T, JALR.T — immediate operand, func selects variant |
| **D** | rd2 | rd1 | rs2 | rs1 | op | ADDI2.T, BCEG.T — both rd1 and rd2 are explicit destination registers |

**Registers:** X-4 (--), X-3 (-0), X-2 (-+), X-1 (0-), X0 (00, hardwired zero), X1 (0+), X2 (+-), X3 (+0), X4 (++).
Immediates (imm2) are 2-trit balanced integers in range [−4, 4].

## Mnemonics

| Mnemonic | Format | Opcode | Operands | Func / Rd2 | Category | Description |
|----------|--------|--------|----------|------------|----------|-------------|
| ADD.T | R | -- | rd1, rs1, rs2 | 00 | Ternary ALU | rd1 = rs1 + rs2 |
| ADDI.T | I | -0 | rd1, rs1, imm2 | 00 | Ternary ALU | rd1 = rs1 + imm2 |
| ADDI2.T | D | -+ | rd1, rd2, rs1, rs2 | | Ternary ALU | rd1 = rs1 + imm1;  rd2 = rs2 + imm2 |
| BCEG.T | D | +0 | rd1, rd2, rs1, rs2 | | Ternary Branch | if rs1 == rs2 goto rd1; if rs1 > rs2 goto rd2 |
| CMPT.T | R | +- | rd1, rs1, rs2 | -- | Ternary Compare | rd1 = tritwise compare(rs1, rs2) |
| CMPW.T | R | +- | rd1, rs1, rs2 | 00 | Ternary Compare | rd1 = wordwise compare(rs1, rs2) |
| JAL.T | I | ++ | rd1, imm2 | 0+ | Ternary Control | rd1 = PC+1; jump to PC+imm2 |
| JALR.T | I | ++ | rd1, rs1, imm2 | 00 | Ternary Control | rd1 = PC+1; jump to rs1+imm2 |
| LI.T | I | -0 | rd1, imm2 | 00 | Pseudo | rd1 = imm2 |
| LPC.T | I | ++ | rd1, imm2 | 0- | Ternary Control | rd1 = PC+imm2; no jump |
| MAXT.T | R | 00 | rd1, rs1, rs2 | +0 | Ternary Min/Max | rd1 = max-tritwise(rs1, rs2) |
| MAXW.T | R | 00 | rd1, rs1, rs2 | +- | Ternary Min/Max | rd1 = max-wordwise(rs1, rs2) |
| MINT.T | R | 00 | rd1, rs1, rs2 | -0 | Ternary Min/Max | rd1 = min-tritwise(rs1, rs2) |
| MINW.T | R | 00 | rd1, rs1, rs2 | -- | Ternary Min/Max | rd1 = min-wordwise(rs1, rs2) |
| MUL.T | R | 0- | rd1, rs1, rs2 | 00 | Ternary ALU | rd1 = rs1 × rs2 |
| MV.T | I | -0 | rd1, rs1 | 00 | Pseudo | rd1 = rs1 |
| NOP.T | I | -0 | | 00 | Pseudo | No-op (write 0 to X0) |
| ROT.T | I | 0+ | rd1, rs1, imm2 | 00 | Ternary Shift | Cyclic shift by imm2 (formerly SC.T — see naming note; SC.T accepted as deprecated alias) |
| SLIN.T | I | 0+ | rd1, rs1, imm2 | -- | Ternary Shift | Shift left by imm2, fill with − |
| SLIZ.T | I | 0+ | rd1, rs1, imm2 | -0 | Ternary Shift | Shift left by imm2, fill with 0 |
| SLIP.T | I | 0+ | rd1, rs1, imm2 | -+ | Ternary Shift | Shift left by imm2, fill with + |
| SRIN.T | I | 0+ | rd1, rs1, imm2 | +- | Ternary Shift | Shift right by imm2, fill with − |
| SRIZ.T | I | 0+ | rd1, rs1, imm2 | +0 | Ternary Shift | Shift right by imm2, fill with 0 |
| SRIP.T | I | 0+ | rd1, rs1, imm2 | ++ | Ternary Shift | Shift right by imm2, fill with + |
| STI.T | R | -- | rd1, rs2 | -- | Ternary ALU | rd1 = −rs2 |
| SUB.T | R | -- | rd1, rs1, rs2 | -- | Ternary ALU | rd1 = rs1 − rs2 |

**Comments:** `#`, `;`, `$`, `//` strip to end-of-line. "quoted" strings and (parenthesised) annotations are also removed.
**Labels** are resolved within a page (up to 9 instructions).
