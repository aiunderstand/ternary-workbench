# REBEL-6 Instruction Set Reference

REBEL-6 is specified across five documents:

| Document | Contents |
|----------|----------|
| **rebel6-isa.md** (this document) | Base ISA: encoding, formats, arithmetic semantics, memory model, Base Ternary, Base Binary, linking, errata |
| [rebel6-platform.md](rebel6-platform.md) | Harts, privilege, traps, interrupts, streaming registers, MMIO, peripherals |
| [rebel6-extensions.md](rebel6-extensions.md) | Extensions: M, A, F, P, Ztl, Ztb, Zicsr, V (reserved) |
| [rebel6-trifloat24.md](rebel6-trifloat24.md) | The 24-trit floating-point format |
| [rebel6-abi.md](rebel6-abi.md) | ABI: register roles (xN ↔ X+N), calling convention, stack, crt0, semihosting |

## Overview & Comparison with REBEL-2

REBEL-6 is the successor to REBEL-2, designed for real-world applications rather than education.
It extends REBEL-2 from a minimal 10-trit, 9-register ISA to a full 32-trit, 729-register architecture
with direct RV32I binary compatibility. The **.t** suffix marks ternary instructions;
binary (RV32I-compatible) instructions have no suffix. PC increments by 1 per instruction slot.

| Property | REBEL-2 | **REBEL-6** |
|----------|---------|------------|
| Radix | Balanced ternary | **Balanced ternary** |
| Instruction width | 10 trits | **32 trits** |
| Base instruction count | 23 + 3 pseudo (9 opcode groups) | **50 ternary + 27 binary + 14 pseudo = 91** |
| Register count | 9 (X-4 … X4) | **729 (X-364 … X364; X0 = zero)** |
| Register width | 2 trits | **24 trits** |
| Operand count | 2–4 | **2–4** |
| PC jump/instr. | 1 | **1** |
| Binary compat. | None | **RV32I (optional layer; L-type, hardware flag)** |
| Formats | R, I, D | **R, I, B, D, X, G, Y, L** |
| Harts / privilege | 1 / none | **1–27 harts, 3 privilege levels — see [platform](rebel6-platform.md)** |
| Extensions | None | **M, A, F, P, Ztl, Ztb, Zicsr — see [extensions](rebel6-extensions.md)** |
| Primary use | Education | **Real-world applications** |

## Architecture profile

<span class="r6-badge r6-t">Base Ternary</span> is the **mandatory core**: a conforming REBEL-6
implementation implements every Base Ternary instruction. <span class="r6-badge r6-b">Base
Binary</span> is an **optional compatibility layer** — it exists so that software targeting RV32I
runs on ternary hardware, either as stock binaries (L-type) or transpiled assembly (R2R), but a
ternary-only implementation may omit it entirely. <span class="r6-badge r6-e">Extensions</span> are
**ternary-only designs**; the single exception is Zicsr, a binary-compatibility extension that
requires Base Binary and is marked as such.

An implementation names its profile as `R6T[B][extensions]`: `R6T` is the ternary core alone,
`R6TB` adds the binary layer, and extension letters follow — e.g. `R6TB-MAFP_Ztl_Ztb_Zicsr` for the
full MCU profile. Growth of the binary layer beyond RV32I is **not planned**.

## RV32I Binary Compatibility (L-type)

An external flag enables direct execution of existing RV32I 32-bit binaries — no recompilation
needed. Binary is a strict subset of ternary: each 32-bit RV32I instruction fits exactly in 32 trits
by mapping binary 0 → trit **−** and binary 1 → trit **+**. A hardware binary-ternary ALU and
instruction translator convert each RV32I instruction into its REBEL-6 binary form as it is fetched,
performing in hardware exactly what the software toolchain does ahead of time — including rescaling
branch displacements so the PC still advances by 1 (see [Memory Model](#memory-model)). The `xx-0`
opcode group provides a software-accessible RV32I instruction space for explicit binary-mode code.

L-type requires the Base Binary layer. Its coverage is **RV32I** (plus Zicsr, when that extension is
implemented — see [extensions](rebel6-extensions.md#zicsr--csr-access-requires-base-binary)); an
RV32I instruction outside the implemented set raises an illegal-instruction trap, which permits
software emulation. Compressed (RVC), M, A, F and D binary encodings are not translated: binary
software must be built for plain `rv32i` (multiply/divide via the compiler's soft routines).

## Instruction Formats

Fields shown MST-first. 4-trit opcodes: last trit = 0. 2-trit opcodes (G/Y): last trit ≠ 0.

| Format | Layout (MST → LST) | Examples |
|--------|--------------------|---------|
| **R** | rs1[6] \| rs2[6] \| rd1[6] \| rd2[6] \| func[4] \| opcode[4] | ADD.T, CMP.T, STI.T, ADD, SLL |
| **I** | rs1[6] \| imm[11:6][6] \| rd1[6] \| imm[5:0][6] \| func[4] \| opcode[4] | ADDI.T, LW.T, JALR.T, ADDI, LW |
| **B** | rs1[6] \| rs2[6] \| imm[11:0][12] \| func[4] \| opcode[4] | BEQ.T, BCGS.T, SW.T, BLTU, SW, SB |
| **G** | imm[23:12][12] \| rd1[6] \| imm[11:0][12] \| opc[2] | LWA.T, LI.T, JAL.T, AIPC.T |
| **X** | imm6a[6] \| imm6b[6] \| rd1[6] \| rd2[6] \| func[4] \| opcode[4] | LI2.T |
| **D** | rs1[6] \| rs2[6] \| rd1[6] \| rs3[6] \| func[4] \| opcode[4] | MAJV.T, MINV.T; MAC.T, FMA.T, TMAC.T, TLUT.T (extensions) |
| **Y** | rs1[6] \| imm[23:0][24] \| opc[2] | SWA.T |
| **L** | RV32I 32-bit instruction format (binary compatibility, requires hardware flag) | native RV32I passthrough |

I-type and B-type both carry a 12-trit immediate; they differ only in how it is laid out. I-type
needs the rd1 slot for its destination register, so its immediate straddles it — `imm[11:6]` in the
rs2 slot, `imm[5:0]` in the rd2 slot. B-type has no destination, so its 12 trits are contiguous
across the rd1 and rd2 slots. Range is ±265,720 in both cases, comfortably past the 12-bit RV32I
immediates the binary instructions must reproduce.

The three-way branches are the one exception: they read the B-type field as **two independent 6-trit
displacements** `off1`\|`off2`, ±364 each, trading reach for a second branch target.

The D format supplies a third source in the rd2 slot. Base Ternary uses it for the majority and
minority votes; the extensions lean on it heavily, because `rd = f(rs1, rs2, rs3)` is exactly the
shape of a multiply-accumulate (`MAC.T`, `FMA.T`, `TMAC.T`) and of a two-input gate applied under a
programmable truth table (`TLUT.T`).

## Arithmetic Semantics

**Ternary integer arithmetic wraps, balanced, modulo 3²⁴.** The result of `ADD.T`, `SUB.T`,
`ADDI.T` — and of the M extension's multiply — is the unique value in
±(3²⁴−1)/2 = ±141,214,768,240 congruent to the exact result modulo 3²⁴. This is the balanced-radix
transposition of two's-complement wrap: carries out of trit 23 are dropped, the operation is
associative, and modular identities hold, which is what compilers, bignum layers and softfloat
libraries assume.

```
  MAX + 1  →  MIN            +++…+ (24)  =  +141,214,768,240
                              plus 1
                             = ---…- (24) =  −141,214,768,240
```

Balanced wrap is kinder than binary wrap in one structural way: **the range is symmetric**. There is
no `INT_MIN` whose negation overflows — `STI.T` (negate) is total, `|x|` is always representable,
and division has no overflow case (`MIN ÷ −1 = MAX` is representable; contrast RV32I, which must
define `INT_MIN ÷ −1` specially). Wide products lose nothing: `MULH.T` exposes the upper word.

Saturating arithmetic is deliberately **not** part of the base — DSP-style saturation belongs to the
[P extension](rebel6-extensions.md#p--packed-ternary-simd), where its consumers live.

**Base Binary arithmetic wraps modulo 2³².** That divergence is not an inconsistency; it is the
*definition* of the binary layer — see
[Base Binary](#rebel-6-base-binary-optional-rv32i-compatibility-layer).

**X0 is hardwired zero** in every variant: reads return 0, writes are discarded. The all-zero
32-trit word decodes as `ADDI.T X0, X0, 0` = `NOP.T`, so blank instruction memory is a NOP sled.

## Memory Model

REBEL-6 is a **Harvard** architecture: instruction and data memory are separate address spaces with
different addressing units. This is forced rather than chosen — a 32-trit instruction is neither a
whole number of 6-trit trytes (5⅓) nor of 24-trit words (1⅓), so instruction addresses cannot be
expressed in data units without instructions straddling boundaries.

| | Instruction space | Data space |
|---|---|---|
| Addressing unit | one instruction slot | one tryte (6 trits) |
| Cell width | 32 trits | 6 trits |
| Address width | 24 trits (PC) | 24 trits (register) |
| Port | fetch only | load/store only |
| Displacement unit | instructions (`BEQ.T`, `JAL.T`) | trytes (`LW.T`, `SW.T`) |
| Regions | I-ROM | D-ROM, D-RAM, MMIO |

The two spaces are **separate physical arrays**, each at its natural cell width. They are not two
views of one array: 32 trits is 5 trytes plus 2 trits, so exposing instruction storage through a
tryte-addressed window would need width-conversion logic for a case that never has to arise. Keeping
them separate also gives W^X and non-executable data for free, since neither port can reach the
other's array.

Because the units differ, a code address and a data address are not interchangeable. `AIPC.T`
(`rd = PC + imm24`) therefore produces a **code** address, and PC-relative addressing is restricted
to code symbols — computed jumps, jump tables, far tail calls. Under a Harvard split there is no data
address corresponding to a PC, so a data symbol cannot be reached PC-relatively.

Nothing is lost by that restriction. RISC-V needs `auipc` for data because a 32-bit address does not
fit in one instruction; REBEL-6's 24-trit immediate does, so the two-instruction global-access idiom
collapses to one:

```
auipc rd, %pcrel_hi(sym)          LWA.T rd, sym
lw    rd, %pcrel_lo(sym)(rd)  →                     ; 2 instructions → 1
```

The residual cost is position-independent code, which needs PC-relative *data* addressing and so
would require a separate mechanism (a GOT-style indirection, or a base register held by the ABI).

### Assembler modifiers

A modifier tells the assembler how to form an address from a label. Because any REBEL-6
address-forming instruction holds a full address in its immediate, the RISC-V `hi`/`lo` split
disappears — one instruction loads the whole address, absolute or PC-relative:

| RISC-V | REBEL-6 | Address formed |
|--------|---------|----------------|
| `%hi(symbol)` + `%lo(symbol)` | `symbol` — no modifier required | absolute |
| `label: %pcrel_hi(symbol)` + `%pcrel_lo(label)` | `%pc_rel(symbol)` | PC-relative |

`%pc_rel` resolves to `symbol − PC` in **instruction** units and is valid only for code symbols; data
symbols use the absolute form. A REBEL-6 linker therefore needs one relocation type per form, and the
absolute form — the common case — needs no PC bookkeeping at all.

### RV32I translation

RV32I reaches REBEL-6 by translation, in software ahead of time or in hardware on the fly. **The PC
increments by 1 in both cases** — there is no mode-dependent increment anywhere in the design.
Two rules do the work:

- **Instruction displacements are rescaled.** `convertBinaryOffsetToTernary` computes
  `(byteOffset / 4) × REBEL6_JUMPS_PER_INSTRUCTION`, and `REBEL6_JUMPS_PER_INSTRUCTION = 1`, so
  RV32I's byte offsets become REBEL-6 instruction counts. This is what PC+1 costs and buys: one
  divide at translation time, and no wasted low displacement trit thereafter.
- **Data displacements pass through unchanged.** Load and store offsets are *not* rescaled, so an
  RV32I byte offset is a REBEL-6 tryte offset 1:1. This is the concrete reason data memory is
  tryte-addressed: it makes RV32I address arithmetic translate without adjustment.

`lui rd, imm` translates to a single `LI.T`, since a 24-trit immediate needs no upper/lower split.

The **L-type** hardware-compatibility flag applies exactly these rules in hardware: the instruction
translator rewrites each RV32I instruction into its REBEL-6 binary form, rescaling branch and jump
displacements by the same ÷4, so the PC still advances by 1. L-type is the hardware realisation of
what the software toolchain does at translation time, not a second execution mode with different PC
semantics.

The `RV32IToREBEL` reference translator implements the software path. Its
`convertBinaryOffsetToTernary` is the ÷4 rule above, and `REBEL6_TRITS_PER_TRYTE = 6` /
`REBEL6_TRITS_PER_INSTRUCTION = 32` match this document.

*Divergence to reconcile:* that translator maps `auipc` to `aipc.t` paired with a zero-offset load,
using the result as a **data** address. This document restricts `%pc_rel` to code symbols, so data
access should target `LWA.T`/`SWA.T` with the absolute form instead. The reference simulator's
ternary load/store handlers are not yet implemented, so the path has never executed and no working
code depends on the old behaviour; this specification leads and the toolchain follows.

### Data widths

| Name | Trits | Trytes | Load / Store |
|------|-------|--------|--------------|
| tryte | 6 | 1 | `LT.T` / `ST.T` |
| halfword | 12 | 2 | `LH.T` / `SH.T` |
| word | 24 | 4 | `LW.T` / `SW.T`, `LWA.T` / `SWA.T` |

A word is the register width. All three loads compute the same effective address `rs1 + imm12` and
differ only in how many trytes they move; the offset selects any tryte, so every tryte and halfword
position within a word is directly addressable without a selector field.

**Tryte order is little-endian**: the lowest address holds the least significant tryte. A word at
address `A` therefore has value `t(A) + 3⁶·t(A+1) + 3¹²·t(A+2) + 3¹⁸·t(A+3)`. This is forced rather
than chosen — RV32I is little-endian and data displacements translate 1:1, so `lb rd, 0(rs1)` must
read the same datum in both — and it is what the reference implementation does.

**Narrow loads zero-pad, and there are no unsigned variants.** In balanced ternary a value's sign is
carried by its most significant non-zero trit, not by a sign bit, so padding with `0` trits cannot
change the value — widening is exact in every case. This is why the ternary load group has three
instructions (`LW.T`, `LH.T`, `LT.T`) where the binary group needs five (`LW`, `LH`, `LB`, `LHU`,
`LBU`): there is nothing for `LTU.T`/`LHU.T` to do. Narrow stores write the low trytes of `rs2`.

### Memory map

> **Revision note.** This map supersedes the earlier two-region arrangement (D-ROM low, D-RAM high).
> The data space is now split into **three equal regions selected by the most significant trit** —
> the cheapest possible region decode, a single trit inspection.

No instruction reads instruction storage as data — `LW.T`/`LH.T`/`LT.T`/`LWA.T` address the data
space only. Read-only data therefore cannot live in I-ROM: `.rodata` placed there would be
unreadable, and the `.data` initialiser image would be unreachable by startup code, so the program
could not boot. REBEL-6 resolves this in the **data address space**, not by adding a way to read
instruction storage.

| MST | Region | Address range (trytes) | Access | Holds |
|-----|--------|------------------------|--------|-------|
| `+` | **D-ROM** | +47,071,589,414 … +141,214,768,240 | load | `.rodata`, `.data` initialiser image |
| `0` | **D-RAM** | −47,071,589,413 … +47,071,589,413 | load/store | `.data`, `.bss`, heap, stack |
| `−` | **MMIO** | −141,214,768,240 … −47,071,589,414 | load/store, device semantics | peripheral registers — see [platform](rebel6-platform.md#mmio) |

The instruction space is unchanged: **I-ROM**, *N* slots × 32 trits, fetch-only, holding `.text`.

Two consequences of the zero-centred D-RAM:

- **The zero window replaces `gp`.** `LW.T rd, X0, imm12` reaches ±265,720 trytes around address
  zero — a 531,441-tryte small-data area with no global-pointer register, no setup code and no
  relocation pass. `.sdata`/`.sbss` straddle zero; the stack starts at the top of populated D-RAM
  and grows down; the heap grows up.
- **`.rodata` needs `LWA.T`**, since D-ROM sits in the `+` region outside the zero window — still
  one instruction per access.

**Sparseness costs nothing.** REBEL-6 has no concept of a "far" address: `LWA.T`/`SWA.T` carry a
full 24-trit absolute immediate, so every address in the entire 282-billion-tryte space is reachable
in exactly one instruction. The near/far machinery that makes sparse maps expensive on binary
machines (`lui`+`addi` pairs, GOTs, base registers) does not exist here. An implementation decodes
only as many trits as its populated windows require; the unpopulated remainder is never decoded.

Startup copies `.data` from D-ROM to D-RAM with ordinary loads and stores. A linker script needs two
output regions in one address space (`MEMORY { drom … dram … }`, `.data : AT> drom`) — nothing about
this is REBEL-6-specific, and a stock C library needs no changes. Writes to D-ROM have no effect;
whether they additionally raise a trap is implementation-defined. MMIO access semantics (no
reordering, no elision, tryte-granular access, misalignment faults) are normative and specified in
the [platform document](rebel6-platform.md#mmio).

**`.text` contains instructions only.** There are no literal pools: a target that dumps constants
into the code stream and reads them PC-relatively, as ARM does with `ldr r0, =…`, cannot work here,
because nothing can load from I-space. Nor is the trick needed — `LI.T` places a full 24-trit
constant in a register in one instruction. All constants reside in `.rodata`. This is an ABI
requirement, not a code-generation preference.

**Code addresses are ordinary data words.** The PC is 24 trits and so is a register, so a code
address fits exactly in one word. Every C construct that stores one therefore works with no special
handling: function pointers, jump tables in `.rodata` (`LWA.T` the entry, `JALR.T` through it),
return addresses spilled to the stack, `setjmp`/`longjmp`, vtables, `atexit` chains, `qsort`
comparators, ISR vector tables, and GCC's `&&label`. Only the *value* refers to I-space; the table
itself is ordinary data. Conforming C never performs arithmetic on function pointers or dereferences
them as data — `void *` ↔ function-pointer round-tripping is POSIX, not ISO — so the fact that the
two spaces count different units is invisible to portable code.

### Representative sizing

An MCU-class part uses a small fraction of the architectural address space. State the regions in
their own units, since "1 MT of ROM" is ambiguous about both which space and which cell:

- **I-ROM** ≈ 187,500 slots (≈ 6×10⁶ trits). This is *inside* the ±265,720-instruction reach of a
  single two-way branch, so branch relaxation never fires on such a part. The three-way branches'
  ±364 does not span it, which is why both forms exist.
- **D-ROM** ≈ 1 MT (megatrytes), populated from the region base +47,071,589,414 upward.
- **D-RAM** ≈ 3 MT, populated −1,500,000 … +1,500,000 around zero, with `__stack_top` at the top.
- **MMIO** devices occupy small windows at the least-negative addresses of the `−` region — see the
  [platform document](rebel6-platform.md#mmio) for the device map.

Both address spaces are 24 trits wide — 3²⁴ ≈ 282 billion slots or trytes — so the architecture
imposes no limit near these figures. Sizes are implementation parameters: a games-class part (DOOM's
shareware WAD alone is ≈4.2 MT of read-only data) simply populates more of each region; no
addressing mode, relocation or instruction changes.

### Alignment

**Misaligned data access is implementation-defined.** An implementation may trap, split the access,
or return an implementation-defined result; no alignment check is mandated. The **ABI** guarantees
natural alignment for all compiler-managed storage (word-aligned stack pointer, word-aligned
allocations), and the **toolchain** enforces it: the assembler statically checks constant offsets
against the access width, and the compiler expands to tryte sequences where alignment cannot be
proven. The single exception is MMIO, where misaligned access is a fault by rule — see the
[platform document](rebel6-platform.md#mmio).

This costs a minimal implementation nothing, which matters more in radix 3 than it would in radix 2.
Testing `A ≡ 0 (mod k)` is a digit inspection only when every prime factor of `k` divides the radix.
Binary machines pick datum sizes 1/2/4/8, all powers of 2, so alignment is a two-wire test. REBEL-6's
sizes are 1/2/4 **trytes** — inherited from RV32I parity — and 2 and 4 are not powers of 3, so:

| Check | Cost in balanced ternary |
|-------|--------------------------|
| `A ≡ 0 (mod 3)`, `(mod 9)` | free — inspect the low 1 or 2 trits |
| `A ≡ 0 (mod 2)` | parity of the count of non-zero trits (3 ≡ 1 mod 2) — XOR reduction |
| `A ≡ 0 (mod 4)` | alternating trit sum (3 ≡ −1 mod 4) — adder tree over all 24 trits |

The mod-4 reducer can overlap the memory access, so it need not add latency, but it is area and
switching power on every load and store — exactly the budget an MCU cares about. Mandating no check
deletes it entirely.

Setun, Brusentsov's 1958 balanced-ternary machine, used 6-trit trytes and an **18-trit word = 3
trytes**, which is radix-coherent and makes alignment free. REBEL-6 gave that up because 18 trits
reaches only ±193 million, short of 32-bit range, while 24 trits reaches ±141 billion. The awkward
alignment arithmetic is the price of clearing RV32I's range.

### Unaligned access in software

Where alignment cannot be proven, a word load expands to tryte loads recombined by **`ADD.T`**:

```
LT.T  t0, rs1, 0        ; SHIZ.T t1, t1, 6        ; ADD.T t0, t0, t1
LT.T  t1, rs1, 1        ; SHIZ.T t2, t2, 12       ; ADD.T t0, t0, t2
LT.T  t2, rs1, 2        ; SHIZ.T t3, t3, 18       ; ADD.T t0, t0, t3
LT.T  t3, rs1, 3
```

10 instructions for a load, 7 for a store (`ST.T` truncates to the low tryte, so only shifts are
needed). This computes `v₀ + 3⁶v₁ + 3¹²v₂ + 3¹⁸v₃` exactly: the fields are disjoint so no carries
propagate, and the top tryte's maximum contribution `3¹⁸ × 364` sits just inside the 24-trit range.

**Use `ADD.T`, never `OR.T`.** The binary shift-and-OR idiom does not port. `OR.T` is tritwise max
whose identity is `−`, but shifts and narrow loads pad with `0`, so `max(−, 0) = 0` destroys every
negative trit — and it fails on both sides of the merge, since the shifted operand's vacated trits
and the loaded operand's upper trits are both `0`.

Two practical notes:

- **Bulk work with genuinely unknown alignment** (`memcpy`, byte-stream and wire-format processing)
  should *not* emit a 10-instruction expansion per access. Use a tryte-at-a-time loop, or an aligned
  fast path with an unaligned prologue and epilogue, so the worst case stays off the hot path.
- **Application-class workloads need hardware help.** Both MIPS (`LWL`/`LWR`) and ARM (unaligned
  support in ARMv6) added it after software expansion proved too expensive at that scale. For
  MCU-class parts software expansion is the standard and correct answer — Cortex-M0 requires
  alignment outright — but if REBEL-6 scales up, this is the decision that returns. The natural
  landing site is an explicit `LWU.T`/`SWU.T` pair in reserved extension encoding space: one
  instruction instead of ten, with the merge network and the mod-4 residue confined to that
  instruction's datapath rather than sitting in every access path.

## Three-Way Branching

The ternary comparator produces a three-valued result — `rs1 < rs2`, `rs1 == rs2`, `rs1 > rs2` — the
same signal `CMP.T` writes to a register. A two-way branch discards two thirds of it. REBEL-6 has two
three-way branches that consume the comparison directly and dispatch to three destinations in one
instruction. They differ only in **which outcome falls through to PC+1**:

| Instruction | rs1 > rs2 | rs1 == rs2 | rs1 < rs2 |
|-------------|-----------|------------|-----------|
| **BCGS.T** rs1, rs2, off1, off2 — **C**ompare, branch **G**reater / **S**maller | PC + off1 | PC + 1 | PC + off2 |
| **BCEG.T** rs1, rs2, off1, off2 — **C**ompare, branch **E**qual / **G**reater | PC + off2 | PC + off1 | PC + 1 |

Both displacements are 6-trit PC-relative offsets (−364 … +364) in the B-type `off1`/`off2` slots.
The fall-through outcome costs no encoding space, so a three-way branch is the same 32 trits and the
same single cycle as a two-way one. `BCEG.T` keeps REBEL-2's `BCEG.T` semantics, with PC-relative
displacements in place of REBEL-2's destination registers — REBEL-2's 2-trit fields could not hold a
displacement that reached past the current page, which is why it had to spend registers on targets.

The third arrangement — *greater* falling through — needs no third instruction: `BCEG.T` with the
source operands swapped provides it, since swapping rs1 and rs2 exchanges the greater and smaller
outcomes.

### Two-way branches: reach instead of arity

The three-way forms spend their 12 trits of displacement on *two* targets, which caps each at ±364.
The two-way branches make the opposite trade and read the same field as **one contiguous 12-trit
displacement**, reaching ±265,720 instructions — 259× the RV32I branch range:

| Mnemonic | Taken when | Reach |
|----------|------------|-------|
| `BEQ.T rs1, rs2, L` | rs1 == rs2 | ±265,720 |
| `BNE.T rs1, rs2, L` | rs1 ≠ rs2 | ±265,720 |
| `BLT.T rs1, rs2, L` | rs1 < rs2 | ±265,720 |
| `BGE.T rs1, rs2, L` | rs1 ≥ rs2 | ±265,720 |

Swapping the sources exchanges the greater and smaller outcomes, so the two remaining orderings need
no encoding of their own — as in RISC-V, they are pseudo-instructions:

| Pseudo-instruction | Expands to |
|--------------------|------------|
| `BGT.T rs1, rs2, L` | `BLT.T rs2, rs1, L` |
| `BLE.T rs1, rs2, L` | `BGE.T rs2, rs1, L` |

**Choosing between the two forms.** Use a three-way branch when the comparison genuinely has three
outcomes worth separating — comparison-driven search, sort and merge kernels — and it saves an entire
instruction and a redundant re-compare. Use a two-way branch for ordinary predicate control flow and
for any target beyond ±364. A three-way branch that needs a far target inverts and jumps, exactly as
RISC-V does past ±4 KiB:

```
BCGS.T rs1, rs2, 2, 2    # skip the jump unless equal
JAL.T  X0, far_label     # 24-trit reach
```

See the [Errata](#errata) for why the original branch group was replaced.

## REBEL-6 Base Ternary

The mandatory core. Every conforming implementation provides all instructions in this section.

**Opcode groups (by last 2 trits):** `xx00` = Base Ternary (729 slots); `xx-0` = Base Binary / RV32I
(729 slots); `xx+0` = Extensions (729 slots). The upper 2 trits encode the instruction category —
the same nine categories in all three groups:

| Upper trits | Category | Upper trits | Category | Upper trits | Category |
|---|---|---|---|---|---|
| `00` | I-type ALU | `--` | R-type ALU | `+-` | D / Control |
| `0-` | Branch | `-0` | R-type Compare/Unary | `+0` | X / Imm |
| `0+` | Store | `-+` | I-type Load | `++` | System |

**Func:** upper 2 trits `00` in the base groups; lower 2 trits (LST) shown in the table discriminate
the instruction. Pseudo-instructions carry the func of the architectural instruction they expand to.
Extensions may additionally use non-`00` upper func trits — see
[rebel6-extensions.md](rebel6-extensions.md).

**2-trit long-immediate (last trit ≠ 0):** `++` LWA.T, `0+` LI.T, `-+` SWA.T, `+-` JAL.T, `0-` AIPC.T;
`--` *reserved*.

**Immediates:** `imm12` is a 12-trit immediate (±265,720); `imm24` is 24 trits (±141,214,768,240).
`shamt` is a **signed** shift amount, **4 trits** (±40) in the rd2 slot — wide enough for RV32I's
5-bit field (0…31) and for any shift of a 24-trit register in either direction.

**Shifts: direction from the sign, fill from one trit.** A shift amount is a balanced-ternary
value, and a balanced value carries its direction in its own sign — so REBEL-6 has **one shift
family with a signed amount** rather than separate left and right instructions
([Errata E-4](#errata)):

- `k > 0` — shift **toward the MST** (the conventional left shift) by `k`;
- `k < 0` — shift **toward the LST** by `|k|`;
- `k = 0` — identity;
- `|k| ≥ 24` — the result is all-fill.

For the register forms the amount is read from the **low 4 trits of rs2** (balanced, ±40) —
mirroring RV32I's 5-bit masking — so the sign inspection is a 4-trit scan in hardware, not a
24-trit priority scan, and an out-of-range register amount is well defined. The signed amount is
the exponent: `x·3^k` is `SHIZ.T x, k` and exact division `x/3^k` is `SHIZ.T x, −k`, and
dynamic-direction shifts (normalization loops, `CLZT.T` feeding a shift) are single instructions
where a binary ISA needs a branch or a negate-and-select.

A shift vacates trits, and the value shifted in is selected by a single **fill trit**: `−`, `0` or
`+`. One trit expresses the three-valued choice exactly — a binary machine would need two bits and
waste an encoding — and it costs no func slot, because the fill lives in a field the shift
instructions do not otherwise use:

| Shift form | Shift amount | Fill trit |
|------------|--------------|-----------|
| immediate (`SHI<f>.T`) | rd2 slot = `imm[5:0]` (signed) | rs2 slot = `imm[11:6]` |
| register (`SH<f>.T`) | low 4 trits of rs2 (signed) | rd2 slot (otherwise unused) |

The mnemonic suffix `<f>` names the fill: **N** = negative (`−`), **Z** = zero (`0`), **P** =
positive (`+`). Because the fill is a datapath signal rather than a decode decision, it wires
straight to the shifter's fill input — cheaper than synthesising the constant from separate func
values — and under the signed rule direction is the same kind of signal, arriving as the amount
field's sign rather than as a func compare. The consequence for decoders: **func alone does not
identify a shift**; the fill trit must be read too.

`ROT.T` (immediate) and `ROTR.T` (register amount) are cyclic, taking the same signed amounts
normalized mod 24, so they have no fill and require the selector field to be zero (`ROT.T` was
named `SC.T` before the A extension reclaimed that mnemonic — see [Errata E-3](#errata);
`ROTR.T` occupies the R-type func slot freed by E-4). The pre-E-4 direction-split names survive as
pseudo-instructions where expressible: `SL<f>.T` and `SLI<f>.T` map 1:1 onto `SH<f>.T`/`SHI<f>.T`,
`SRI<f>.T` maps onto `SHI<f>.T` with the amount negated, and the register right shifts `SR<f>.T`
are **retired** (a runtime amount cannot be negated at assembly time — materialize a negative
amount with `STI.T` and use `SH<f>.T`). The I-type func `-0` slot the `SRI*` family occupied is
*reserved*.

**Ternary logic operations** are the balanced-ternary extensions of the binary operations, chosen so
that they agree *exactly* with RV32I's `or`/`and`/`xor` on the binary subset under REBEL-6's own
0 → `−`, 1 → `+` mapping. `OR.T` is tritwise **max**, `AND.T` is tritwise **min**, and `XOR.T` is
tritwise **−(a × b)**:

| a | b | OR.T (max) | AND.T (min) | XOR.T (−a×b) |
|---|---|------------|-------------|---------------|
| `−` | `−` | `−` | `−` | `−` |
| `−` | `0` | `0` | `−` | `0` |
| `−` | `+` | `+` | `−` | `+` |
| `0` | `0` | `0` | `0` | `0` |
| `0` | `+` | `+` | `0` | `0` |
| `+` | `+` | `+` | `+` | `−` |

All three are commutative. Restricted to `{−, +}` these reproduce the binary truth tables exactly,
so a binary-mode program and its ternary transliteration compute the same result. `XOR.T` is realised
in the MRCS standard cell library in 18 transistors. Because these are per-trit-lane operations, they
double as the packed 1-trit SIMD primitives — the [P extension](rebel6-extensions.md#p--packed-ternary-simd)
builds on them rather than duplicating them. Arbitrary tritwise gates beyond these three are provided
by the [Ztl extension](rebel6-extensions.md#ztl--ternary-logic)'s programmable truth-table
instructions.

Note that `0` is **not** the identity for `OR.T` — the identity for max is `−`. Merging disjoint
zero-padded fields must use `ADD.T`, not `OR.T`; see [Memory Model](#memory-model).

**Wordwise min/max, dual by negation.** `MIN.T` and `MAX.T` compare whole words (arithmetic select),
unlike tritwise `AND.T`/`OR.T`. Their func codes are negatives of each other (`0-` ↔ `0+`),
mirroring the identity `min(a,b) = −max(−a,−b)`.

**Dual move.** `MV2.T rd1, rd2, rs1, rs2` performs two register copies in one instruction
(both reads complete before either write). `SWAP.T a, b` is its operand-crossed pseudo-instruction —
the R2v2 register exchange, generalised.

**System group.** The five System instructions take no operands; all four operand slots are encoded
zero. Their func assignments deliberately match REBEL-2 V2.2's `++` control group, with `TRET.T`
taking the slot of its predecessor `IRET.T`. `ECALL.T`, `EBREAK.T` and `TRET.T` semantics — trap
entry, cause codes, privilege banks — are specified in the
[platform document](rebel6-platform.md#traps); `FENCE.T` and `WFI.T` in its
[memory-consistency](rebel6-platform.md#memory-consistency) and
[interrupt](rebel6-platform.md#interrupts) sections.

**Comments:** `#`, `;`, `$`, `//` strip to end-of-line.

| Mnemonic | Format | Opcode | Func | Operands | Category | Description |
|----------|--------|--------|------|----------|----------|-------------|
| ADD.T | R | --00 | -- | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 + rs2 |
| SUB.T | R | --00 | -0 | rd1, rs1, rs2 | Ternary ALU | rd1 = rs1 − rs2 |
| SHN.T | R | --00 | -+ | rd1, rs1, rs2 | Ternary Shift | rd1 = rs1 shifted by signed low 4 trits of rs2 (+ = toward MST), fill − |
| SHZ.T | R | --00 | -+ | rd1, rs1, rs2 | Ternary Shift | rd1 = rs1 shifted by signed low 4 trits of rs2 (+ = toward MST), fill 0 |
| SHP.T | R | --00 | -+ | rd1, rs1, rs2 | Ternary Shift | rd1 = rs1 shifted by signed low 4 trits of rs2 (+ = toward MST), fill + |
| ROTR.T | R | --00 | 0- | rd1, rs1, rs2 | Ternary Shift | rd1 = rs1 rotated by signed low 4 trits of rs2, mod 24 (cyclic, no fill; + = toward MST) |
| SLT.T | R | --00 | 00 | rd1, rs1, rs2 | Ternary ALU | rd1 = (rs1 < rs2) ? +1 : 0 |
| OR.T | R | --00 | 0+ | rd1, rs1, rs2 | Ternary ALU | rd1 = tritwise max(rs1, rs2) |
| XOR.T | R | --00 | +- | rd1, rs1, rs2 | Ternary ALU | rd1 = tritwise −(rs1 × rs2) |
| AND.T | R | --00 | +0 | rd1, rs1, rs2 | Ternary ALU | rd1 = tritwise min(rs1, rs2) |
| CMP.T | R | -000 | -- | rd1, rs1, rs2 | Ternary Compare | rd1 = three-way compare: +1, 0, or -1 |
| STI.T | R | -000 | -0 | rd1, rs1 | Ternary ALU | rd1 = −rs1 (standard ternary inversion) |
| MV2.T | R | -000 | -+ | rd1, rd2, rs1, rs2 | Ternary ALU | rd1 = rs1; rd2 = rs2 (dual move; reads before writes) |
| MIN.T | R | -000 | 0- | rd1, rs1, rs2 | Ternary ALU | rd1 = min(rs1, rs2) (wordwise) |
| MAX.T | R | -000 | 0+ | rd1, rs1, rs2 | Ternary ALU | rd1 = max(rs1, rs2) (wordwise) |
| ADDI.T | I | 0000 | 00 | rd1, rs1, imm12 | Ternary ALU | rd1 = rs1 + imm |
| SHIN.T | I | 0000 | -- | rd1, rs1, shamt | Ternary Shift | rd1 = rs1 shifted by signed shamt (+ = toward MST), fill − |
| SHIZ.T | I | 0000 | -- | rd1, rs1, shamt | Ternary Shift | rd1 = rs1 shifted by signed shamt (+ = toward MST), fill 0 |
| SHIP.T | I | 0000 | -- | rd1, rs1, shamt | Ternary Shift | rd1 = rs1 shifted by signed shamt (+ = toward MST), fill + |
| ROT.T | I | 0000 | -+ | rd1, rs1, shamt | Ternary Shift | rd1 = rs1 rotated by signed shamt, mod 24 (cyclic, no fill; + = toward MST) |
| SLTI.T | I | 0000 | 0- | rd1, rs1, imm12 | Ternary ALU | rd1 = (rs1 < imm12) ? +1 : 0 |
| ORI.T | I | 0000 | 0+ | rd1, rs1, imm12 | Ternary ALU | rd1 = tritwise max(rs1, imm12) |
| XORI.T | I | 0000 | +- | rd1, rs1, imm12 | Ternary ALU | rd1 = tritwise −(rs1 × imm12) |
| ANDI.T | I | 0000 | +0 | rd1, rs1, imm12 | Ternary ALU | rd1 = tritwise min(rs1, imm12) |
| LW.T | I | -+00 | -- | rd1, rs1, imm12 | Ternary Load | rd1 = mem[rs1 + imm12] (word) |
| LH.T | I | -+00 | -0 | rd1, rs1, imm12 | Ternary Load | rd1 = mem[rs1 + imm12] (halfword) |
| LT.T | I | -+00 | -+ | rd1, rs1, imm12 | Ternary Load | rd1 = mem[rs1 + imm12] (tryte) |
| JALR.T | I | -+00 | 0- | rd1, rs1, imm12 | Ternary Control | rd1 = PC+1; PC = rs1 + imm12 |
| BCGS.T | B | 0-00 | -- | rs1, rs2, off1, off2 | Ternary Branch | rs1 > rs2 → PC+off1; rs1 < rs2 → PC+off2; else PC+1 (±364) |
| BCEG.T | B | 0-00 | -0 | rs1, rs2, off1, off2 | Ternary Branch | rs1 == rs2 → PC+off1; rs1 > rs2 → PC+off2; else PC+1 (±364) |
| BEQ.T | B | 0-00 | -+ | rs1, rs2, disp | Ternary Branch | branch if rs1 == rs2 (±265720) |
| BNE.T | B | 0-00 | 0- | rs1, rs2, disp | Ternary Branch | branch if rs1 ≠ rs2 (±265720) |
| BLT.T | B | 0-00 | 00 | rs1, rs2, disp | Ternary Branch | branch if rs1 < rs2 (±265720) |
| BGE.T | B | 0-00 | 0+ | rs1, rs2, disp | Ternary Branch | branch if rs1 ≥ rs2 (±265720) |
| SW.T | B | 0+00 | -- | rs1, rs2, imm12 | Ternary Store | mem[rs1 + imm12] = rs2 (word) |
| SH.T | B | 0+00 | -0 | rs1, rs2, imm12 | Ternary Store | mem[rs1 + imm12] = rs2 (halfword) |
| ST.T | B | 0+00 | -+ | rs1, rs2, imm12 | Ternary Store | mem[rs1 + imm12] = rs2 (tryte) |
| MAJV.T | D | +-00 | -- | rd1, rs1, rs2, rs3 | Ternary ALU | rd1 = majority(rs1, rs2, rs3) |
| MINV.T | D | +-00 | -0 | rd1, rs1, rs2, rs3 | Ternary ALU | rd1 = minority(rs1, rs2, rs3) |
| LI2.T | X | +000 | -- | rd1, rd2, imm1, imm2 | Ternary ALU | rd1 = imm1;  rd2 = imm2 |
| FENCE.T | R | ++00 | -0 | | Ternary System | full memory fence (pred/succ fields reserved zero) |
| WFI.T | R | ++00 | -+ | | Ternary System | wait for interrupt (see [platform](rebel6-platform.md#interrupts)) |
| TRET.T | R | ++00 | +- | | Ternary System | trap return from the current privilege level's bank |
| EBREAK.T | R | ++00 | +0 | | Ternary System | breakpoint trap (cause −10) |
| ECALL.T | R | ++00 | ++ | | Ternary System | environment call (cause −7/−8/−9 by mode) |
| NOP.T | I | 0000 | 00 | | Pseudo | no-op (all-zero 32 trits = ADDI.T X0, X0, 0) |
| MV.T | I | 0000 | 00 | rd1, rs1 | Pseudo | rd1 = rs1 (ADDI.T rd1, rs1, 0) |
| SLN.T | R | --00 | -+ | rd1, rs1, rs2 | Pseudo | shift toward MST, fill − (SHN.T rd1, rs1, rs2) — [E-4](#errata) |
| SLZ.T | R | --00 | -+ | rd1, rs1, rs2 | Pseudo | shift toward MST, fill 0 (SHZ.T rd1, rs1, rs2) — [E-4](#errata) |
| SLP.T | R | --00 | -+ | rd1, rs1, rs2 | Pseudo | shift toward MST, fill + (SHP.T rd1, rs1, rs2) — [E-4](#errata) |
| SLIN.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward MST, fill − (SHIN.T rd1, rs1, shamt) — [E-4](#errata) |
| SLIZ.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward MST, fill 0 (SHIZ.T rd1, rs1, shamt) — [E-4](#errata) |
| SLIP.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward MST, fill + (SHIP.T rd1, rs1, shamt) — [E-4](#errata) |
| SRIN.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward LST, fill − (SHIN.T rd1, rs1, −shamt) — [E-4](#errata) |
| SRIZ.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward LST, fill 0 (SHIZ.T rd1, rs1, −shamt) — [E-4](#errata) |
| SRIP.T | I | 0000 | -- | rd1, rs1, shamt | Pseudo | shift toward LST, fill + (SHIP.T rd1, rs1, −shamt) — [E-4](#errata) |
| SWAP.T | R | -000 | -+ | rd1, rd2 | Pseudo | exchange rd1 ↔ rd2 (MV2.T rd1, rd2, rd2, rd1) |
| BGT.T | B | 0-00 | 00 | rs1, rs2, disp | Pseudo | branch if rs1 > rs2 (BLT.T rs2, rs1, disp) |
| BLE.T | B | 0-00 | 0+ | rs1, rs2, disp | Pseudo | branch if rs1 ≤ rs2 (BGE.T rs2, rs1, disp) |
| LWA.T | G | ++ | — | rd1, imm24 | Ternary Load | rd1 = mem[imm24] (load word absolute) |
| LI.T | G | 0+ | — | rd1, imm24 | Ternary ALU | rd1 = imm24 (24-trit load immediate) |
| SWA.T | Y | -+ | — | rs1, imm24 | Ternary Store | mem[imm24] = rs1 (store word absolute) |
| JAL.T | G | +- | — | rd1, imm24 | Ternary Control | rd1 = PC+1; PC = PC + imm24 |
| AIPC.T | G | 0- | — | rd1, imm24 | Ternary Control | rd1 = PC + imm24 (code address; see [Memory Model](#memory-model)) |

## REBEL-6 Base Binary (optional RV32I compatibility layer)

**Optional.** A ternary-only implementation (`R6T` profile) omits this entire group; the L-type
passthrough and the Zicsr extension require it. When present, it is complete: **every RV32I
instruction executes** — but not every RV32I instruction has a binary encoding here, and that is by
rule, not omission.

**The completeness rule.** The binary group contains exactly the RV32I instructions whose semantics
are **representation-dependent** — operations whose result depends on the operand being a 32-bit
two's-complement bit pattern rather than an abstract integer:

| Family | Why representation-dependent |
|--------|------------------------------|
| ADD, SUB, ADDI | wrap modulo 2³² (ternary wraps modulo 3²⁴) |
| SLL, SRL, SRA + immediates | shift bit patterns; logical vs arithmetic right split |
| SLTU, SLTIU, BLTU, BGEU | unsigned comparison — an artefact of the bit-pattern reading |
| OR, AND, XOR + immediates | bitwise logic on bits, not trits |
| LB, LH, LBU, LHU, SB, SH | byte/halfword granularity with sign/zero extension |
| LW, SW | word access with binary wrap semantics on the loaded value's use |

Everything **representation-independent** — where the RV32I semantics coincide with the ternary
instruction's on translated values — is an **assembler alias** onto Base Ternary, exactly what the
R2R translator emits:

| RV32I mnemonic | Assembles / translates to | Note |
|----------------|---------------------------|------|
| `lui rd, imm` | `LI.T rd, imm<<12` | one instruction; no hi/lo split |
| `auipc rd, imm` | `AIPC.T rd, %pc_rel(...)` | code symbols only — see [Memory Model](#memory-model) |
| `jal rd, off` | `JAL.T rd, off÷4` | displacement rescaled |
| `jalr rd, rs, off` | `JALR.T rd, rs, off÷4` | displacement rescaled |
| `beq/bne/blt/bge` | `BEQ.T/BNE.T/BLT.T/BGE.T` | signed compare = value compare |
| `slt/slti` | `SLT.T/SLTI.T` | signed compare = value compare |
| `ecall / ebreak` | `ECALL.T / EBREAK.T` | trap semantics identical |
| `fence` | `FENCE.T` | full fence |
| `mv/nop/li` (pseudos) | `MV.T/NOP.T/LI.T` | |

The assembler accepts these RV32I mnemonics and emits the ternary encodings; the L-type hardware
translator performs the same rewrite at fetch. This is why the binary group's 27 instructions
suffice for all of RV32I.

**No growth beyond RV32I is planned.** RV32M/RV32A binary encodings do not exist: `rv32i` binaries
perform multiply and divide through the compiler's soft routines (`__mulsi3`, `__divsi3`), which run
unmodified. Ternary-native code uses the [M and A extensions](rebel6-extensions.md) instead. The one
sanctioned binary-compatibility extension is
[Zicsr](rebel6-extensions.md#zicsr--csr-access-requires-base-binary).

| Mnemonic | Format | Opcode | Func | Operands | Category | Description |
|----------|--------|--------|------|----------|----------|-------------|
| ADD | R | ---0 | -- | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 + rs2 (mod 2³²) |
| SUB | R | ---0 | -0 | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 − rs2 (mod 2³²) |
| SLL | R | ---0 | -+ | rd1, rs1, rs2 | Binary ALU | rd1 = rs1 << rs2 |
| SRL | R | ---0 | 0- | rd1, rs1, rs2 | Binary ALU | logical shift right |
| SRA | R | ---0 | 00 | rd1, rs1, rs2 | Binary ALU | arithmetic shift right |
| SLTU | R | ---0 | 0+ | rd1, rs1, rs2 | Binary ALU | rd1 = (rs1 <u rs2) ? 1 : 0 |
| OR | R | ---0 | +- | rd1, rs1, rs2 | Binary ALU | bitwise OR |
| XOR | R | ---0 | +0 | rd1, rs1, rs2 | Binary ALU | bitwise XOR |
| AND | R | ---0 | ++ | rd1, rs1, rs2 | Binary ALU | bitwise AND |
| ADDI | I | 00-0 | -- | rd1, rs1, imm12 | Binary ALU | rd1 = rs1 + imm (mod 2³²) |
| SLLI | I | 00-0 | -0 | rd1, rs1, shamt | Binary ALU | logical left shift immediate |
| SRLI | I | 00-0 | -+ | rd1, rs1, shamt | Binary ALU | logical right shift immediate |
| SRAI | I | 00-0 | 0- | rd1, rs1, shamt | Binary ALU | arithmetic right shift immediate |
| SLTIU | I | 00-0 | 00 | rd1, rs1, imm12 | Binary ALU | rd1 = (rs1 <u imm) ? 1 : 0 |
| ORI | I | 00-0 | 0+ | rd1, rs1, imm12 | Binary ALU | bitwise OR immediate |
| XORI | I | 00-0 | +- | rd1, rs1, imm12 | Binary ALU | bitwise XOR immediate |
| ANDI | I | 00-0 | +0 | rd1, rs1, imm12 | Binary ALU | bitwise AND immediate |
| LW | I | -+-0 | -- | rd1, rs1, imm12 | Binary Load | load word |
| LH | I | -+-0 | -0 | rd1, rs1, imm12 | Binary Load | load halfword signed |
| LB | I | -+-0 | -+ | rd1, rs1, imm12 | Binary Load | load byte signed |
| LHU | I | -+-0 | 0- | rd1, rs1, imm12 | Binary Load | load halfword unsigned |
| LBU | I | -+-0 | 00 | rd1, rs1, imm12 | Binary Load | load byte unsigned |
| BLTU | B | 0--0 | -- | rs1, rs2, imm12 | Binary Branch | branch if rs1 <u rs2 |
| BGEU | B | 0--0 | -0 | rs1, rs2, imm12 | Binary Branch | branch if rs1 ≥u rs2 |
| SW | B | 0+-0 | -- | rs1, rs2, imm12 | Binary Store | store word |
| SH | B | 0+-0 | -0 | rs1, rs2, imm12 | Binary Store | store halfword |
| SB | B | 0+-0 | -+ | rs1, rs2, imm12 | Binary Store | store byte |

## Extensions

All extensions are **ternary-only designs**; the exception is Zicsr, a binary-compatibility
extension that requires Base Binary. Full specifications, encodings and rationale:
[rebel6-extensions.md](rebel6-extensions.md).

| Ext | Name | Contents | Status |
|-----|------|----------|--------|
| **M** | Multiply / divide | MUL.T, MULH.T, DIV.T, REM.T, MOD.T, MAC.T | specified |
| **A** | Atomics | LR.T, SC.T, AMO{SWAP,ADD,AND,OR,XOR,MIN,MAX}.T | specified |
| **F** | Float ([trifloat24](rebel6-trifloat24.md)) | FADD.T, FSUB.T, FMUL.T, FDIV.T, FMA.T, FCMP.T, FCVT | specified (softfloat-first) |
| **P** | Packed ternary SIMD | TMAC.T, TSUM.T, QNT.T, HMAX.T (+ equivalences) | specified; tryte-lane ops reserved |
| **Ztl** | Ternary logic | TLUT.T, TLUTI.T + canonical gate library (NTI, PTI, MTI, KIMP, cycles, …) | specified |
| **Ztb** | Trit manipulation | CLZT.T, TCNT.T | specified |
| **Zicsr** | CSR access | CSRRW/S/C(+I) — decodes onto trap registers | specified; **requires Base Binary** |
| **V** | Vector | — | reserved |

### Opcode map

Occupancy of the canonical func plane (func upper trits `00`; 9 funcs per opcode). Each group also
holds 8 further func planes per opcode, all reserved.

<table class="r6-opmap">
<thead>
<tr><th>Category (upper trits)</th><th class="r6-t">Base Ternary <code>xx00</code></th><th class="r6-b">Base Binary <code>xx-0</code></th><th class="r6-e">Extensions <code>xx+0</code></th></tr>
</thead>
<tbody>
<tr><td><code>00</code> I-ALU</td><td class="r6-t"><code>0000</code> — 7/9 (ADDI, SHI, ROT, SLTI, ORI, XORI, ANDI; <code>-0</code> reserved — E-4)</td><td class="r6-b"><code>00-0</code> — 8/9 (RV32I I-ALU)</td><td class="r6-e"><code>00+0</code> — 1/9 (Ztl: TLUTI)</td></tr>
<tr><td><code>0-</code> Branch</td><td class="r6-t"><code>0-00</code> — 6/9 (3-way + 2-way)</td><td class="r6-b"><code>0--0</code> — 2/9 (BLTU, BGEU)</td><td class="r6-e"><code>0-+0</code> — reserved</td></tr>
<tr><td><code>0+</code> Store</td><td class="r6-t"><code>0+00</code> — 3/9 (SW, SH, ST)</td><td class="r6-b"><code>0+-0</code> — 3/9 (SW, SH, SB)</td><td class="r6-e"><code>0++0</code> — reserved</td></tr>
<tr><td><code>--</code> R-ALU</td><td class="r6-t"><code>--00</code> — 8/9 (ALU, SH, ROTR, logic)</td><td class="r6-b"><code>---0</code> — <b>9/9 full</b></td><td class="r6-e"><code>--+0</code> — <b>9/9 full</b> (M: 5, F arith: 4)</td></tr>
<tr><td><code>-0</code> Compare/Unary</td><td class="r6-t"><code>-000</code> — 5/9 (CMP, STI, MV2, MIN, MAX)</td><td class="r6-b"><code>-0-0</code> — 0/9</td><td class="r6-e"><code>-0+0</code> — 8/9 (F cmp/cvt: 3, Ztb: 2, P scalar: 3)</td></tr>
<tr><td><code>-+</code> Load</td><td class="r6-t"><code>-+00</code> — 4/9 (LW, LH, LT, JALR)</td><td class="r6-b"><code>-+-0</code> — 5/9 (RV32I loads)</td><td class="r6-e"><code>-++0</code> — <b>9/9 full</b> (A: LR, SC, 7×AMO)</td></tr>
<tr><td><code>+-</code> D/Control</td><td class="r6-t"><code>+-00</code> — 2/9 (MAJV, MINV)</td><td class="r6-b"><code>+--0</code> — 0/9</td><td class="r6-e"><code>+-+0</code> — 4/9 (TLUT, MAC, FMA, TMAC)</td></tr>
<tr><td><code>+0</code> X/Imm</td><td class="r6-t"><code>+000</code> — 1/9 (LI2)</td><td class="r6-b"><code>+0-0</code> — 0/9</td><td class="r6-e"><code>+0+0</code> — 0/9 (reserved for P growth)</td></tr>
<tr><td><code>++</code> System</td><td class="r6-t"><code>++00</code> — 5/9 (FENCE, WFI, TRET, EBREAK, ECALL)</td><td class="r6-b"><code>++-0</code> — 6/9 (Zicsr)</td><td class="r6-e"><code>+++0</code> — reserved</td></tr>
</tbody>
</table>

**2-trit long-immediate space** (last trit ≠ 0): 5 of 6 slots used (`LWA.T`, `LI.T`, `SWA.T`,
`JAL.T`, `AIPC.T`); `--` remains reserved — it is the scarcest encoding resource in the ISA and is
not to be spent casually.

## Linking

REBEL-6 links **statically**: object files and libraries are combined into a single machine-code
image at a known ROM base. There is no dynamic linking and no position-independent code (see
[Memory Model](#memory-model)). Because the linker knows every symbol's final address, it patches
absolute immediates directly — there is no GOT, no PLT, no lazy binding, no startup relocation pass,
and no register reserved for a base pointer. A global access is one instruction (`LWA.T rd, sym`)
where RISC-V needs two.

The object format is **ELF32-alike**: relocations are `Elf32_Rela`-shaped records of
(`r_offset`, `r_info`, `r_addend`), and symbols carry standard binding and type.

### Relocations

`S` is the resolved symbol address, `P` the address of the patch site — the instruction being
patched, not the one after it, since `JAL.T` computes `PC = PC + imm24` from its own address.

| Relocation | Value | Field | Width | Unit |
|------------|-------|-------|-------|------|
| `R_REBEL6_ABS24_DATA` | `S` | G/Y `imm24` | 24 trits | trytes |
| `R_REBEL6_ABS24_CODE` | `S` | G/Y `imm24` | 24 trits | instructions |
| `R_REBEL6_PCREL24` | `S − P` | G `imm24` | 24 trits | instructions |
| `R_REBEL6_PCREL12` | `S − P` | B-type `imm12`, contiguous | 12 trits | instructions |
| `R_REBEL6_PCREL6_OFF1` | `S − P` | B-type `off1` (rd1 slot) | 6 trits | instructions |
| `R_REBEL6_PCREL6_OFF2` | `S − P` | B-type `off2` (rd2 slot) | 6 trits | instructions |
| `R_REBEL6_DISP12` | `S` + addend | I-type `imm12` split, or B-type `imm12` contiguous | 12 trits | trytes |

Used by: `ABS24_*` — `LI.T`, `LWA.T`, `SWA.T`, and data words holding an address; `PCREL24` —
`JAL.T`, `AIPC.T`; `PCREL12` — `BEQ.T`, `BNE.T`, `BLT.T`, `BGE.T`; `PCREL6_*` — `BCGS.T`, `BCEG.T`;
`DISP12` — `LW.T`/`LH.T`/`LT.T`, `SW.T`/`SH.T`/`ST.T`.

Three properties are encoded in the relocation type rather than inferred:

- **Unit.** PC-relative displacements count *instructions*; data addresses count *trytes*. The two
  are not interchangeable under the Harvard split, so `ABS24` splits by symbol class: `ABS24_CODE`
  for a function address or a jump-table entry, `ABS24_DATA` for an object address. Deriving this
  from the symbol type instead would let an untyped `.word sym` produce an address in the wrong
  space — a wild pointer, not a rounding error. Note that `ABS24_CODE` is routinely emitted *into a
  data section*: jump tables, `.init_array`/`.fini_array`, vtables and ISR vector tables are all
  arrays of code addresses living in `.rodata` or `.data`. A relocation design that assumes section
  class implies address class gets these wrong.
- **Layout.** I-type's `imm12` is split around the destination register (rs2 slot = `imm[11:6]`,
  rd2 slot = `imm[5:0]`); B-type's is contiguous across rd1+rd2. Same width, different scatter.
- **Slot.** The three-way branches need `OFF1` and `OFF2` as distinct types — see below.

`PCREL24` is required even though there is no position-independent code, because `JAL.T` is
PC-relative by definition.

`DISP12` is needed only where a load/store displacement is not a compile-time constant. With
gp-relative addressing subsumed by the zero window and the `%lo` pairs already collapsed, a minimal
C toolchain may never emit one; it is listed for completeness rather than as a requirement.

### Two fixups in one instruction

`BCGS.T` and `BCEG.T` carry two independent 6-trit displacements in a single 32-trit word, so one
instruction address can require **two** relocations. This is emitted as **two ordinary records at the
same `r_offset`**, distinguished by type (`PCREL6_OFF1`, `PCREL6_OFF2`) rather than by a selector
field inside a composite record.

Two records keep the standard three-field relocation shape, so ELF tooling continues to work; they
give each branch target its own symbol and addend, which it needs since the two targets are
genuinely independent symbols; and duplicate `r_offset` values are legal. A composite record would
buy atomicity during relaxation — which the next section removes the need for.

### No relaxation

Two-way branches reach ±265,720 instructions, comfortably more than the ≈187,500 instructions of a
1 MT ROM, so they never overflow on an MCU-class part. Only the three-way branches can, at ±364 per
target. Relaxing one means rewriting it as invert-and-jump, which changes code size, shifts every
subsequent address, and forces the iterative layout loop RISC-V linkers have to implement.

REBEL-6 avoids that by rule rather than by machinery. **Normative:**

> A three-way branch may be emitted only when **both** targets are provably within ±364
> instructions. Otherwise the two-way forms must be used.
>
> The linker does not relax. A `PCREL6` displacement that does not fit is a link error.

Three-way branching is a local-dispatch construct — the comparison-driven search, sort and merge
kernels it exists for have nearby targets — so the rule costs nothing in practice, and it makes the
linker a single-pass patcher with no layout iteration. Truncating an overflowing displacement is
never permitted.

### Link-time checks

The linker rejects rather than truncates:

- a displacement outside its field's range;
- a PC-relative reference to a data symbol;
- a data symbol placed at an address not naturally aligned for the accesses that reach it. The
  assembler checks constant offsets; only the linker knows final symbol addresses. Hardware performs
  no alignment check (see [Alignment](#alignment)), so the toolchain is the only place misalignment
  is caught.

### ABI

The calling convention is inherited from RV32I ilp32 — register roles (`sp`, `ra`, `a0`–`a7`,
`s0`–`s11`, `t0`–`t6`), argument and return passing, the caller/callee-saved split, and ilp32 type
sizes and struct layout — with `gp` retired in favour of the zero window. The hart/register-window
**variants** (`r6-single`, `r6-mp3`, `r6-mp9`, `r6-mp27`) and the fixed system/streaming register
indices are specified in the [platform document](rebel6-platform.md#harts-and-variants); the
register role table (identity mapping xN ↔ X+N), the two-register return, the crt0 contract and
the semihosting convention are normative in [rebel6-abi.md](rebel6-abi.md).

## REBEL-2 V2.2 migration

Complete disposition of every REBEL-2 V2.2 instruction in REBEL-6. Nothing is dropped silently:
each mnemonic is carried, renamed, subsumed by an equivalent, or expressible as a documented
composition.

| R2v2 | REBEL-6 disposition |
|------|---------------------|
| ADD.T, ADDI.T, SUB.T, STI.T, BCEG.T, BNE.T, JAL.T, JALR.T, LI.T, LI2.T, MAJV.T, MV.T, NOP.T | Base Ternary (present, widened) |
| SLI\*.T, SRI\*.T | pseudos over the signed-amount `SHI<f>.T` — [Errata E-4](#errata) |
| CMPW.T | ≡ `CMP.T` (wordwise three-way compare) |
| CMPT.T (tritwise compare) | `CMPT.T` canonical gate over `TLUT.T` — [Ztl](rebel6-extensions.md#ztl--ternary-logic) |
| MAXT.T / MINT.T (tritwise) | ≡ `OR.T` / `AND.T` — tritwise max/min already in base |
| MAXI.T / MINI.T (tritwise, imm) | ≡ `ORI.T` / `ANDI.T` |
| MAXW.T / MINW.T (wordwise) | `MAX.T` / `MIN.T` — new in base group `-000` |
| CMPWI.T / CMPTI.T | composition: `LI.T` + register form (no dedicated encoding) |
| MUL.T, MULH.T, DIV.T, REM.T, MOD.T | [M extension](rebel6-extensions.md#m--integer-multiply--divide) |
| SWAP.T | pseudo over new `MV2.T` (base `-000`) |
| WFI.T, ECALL.T, EBREAK.T, FENCE.T | Base Ternary System group `++00` (same funcs as R2v2) |
| IRET.T | superseded by `TRET.T` (same func slot `+-`) |
| LPC.T | ≡ `AIPC.T` |
| SC.T (cyclic shift) | renamed `ROT.T`, encoding unchanged — [Errata E-3](#errata) |
| CYCLEUP.T | `CYU.T` canonical gate over `TLUTI.T` — [Ztl](rebel6-extensions.md#ztl--ternary-logic) |
| NTI.T, PTI.T, MTI.T, KIMP.T | canonical gates over `TLUTI.T`/`TLUT.T`, truth tables now pinned — [Ztl](rebel6-extensions.md#ztl--ternary-logic) |
| LD2.T / ST2.T | not carried — REBEL-2-specific trit-pair memory ops; superseded by the tryte-addressed `LT.T`/`LH.T`/`LW.T` family |

## Errata

Corrections to the REBEL-6 definition as originally published in the MSc thesis of Bodahl. None of
these items is discussed in the 2025 ISMVL REBEL-6 paper, which does not cover the branch encoding.

A further divergence is *not* an erratum against the specification: the Ternary Workbench reference
assembler encoded I-type and B-type immediates in a single 6-trit slot (±364) rather than the
specified 12 trits, which silently narrowed every immediate and offset — including the RV32I-parity
binary instructions — to roughly a sixth of the intended range. That was an implementation bug and
has been fixed; the format definitions were correct as published.

### E-1 — no three-way branch; `BCGS.T` and `BCEG.T` added

**Defect.** REBEL-6 adopted RV32I's two-way branch model wholesale (`BEQ.T`, `BNE.T`, `BLT.T`,
`BGE.T`) and had nothing else. Every one of these instructions evaluates a three-valued comparison
and then collapses it to a binary taken/not-taken decision, discarding two thirds of the comparator's
output. REBEL-2 did not have this defect: its D-format `BCEG.T` dispatches to two explicit
destinations with a third implicit fall-through path. The capability was lost in the move from
REBEL-2 to REBEL-6, and the loss was not noted.

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

Defining both costs one extra func slot in a group with 729 and covers every three-way dispatch
pattern: the third possible arrangement (greater falling through) is `BCEG.T` with the source
operands swapped and needs no instruction of its own.

**Resolution.** `BCGS.T` (func `--`) and `BCEG.T` (func `-0`) are added to branch opcode group
`0-00`. They need no new format: they read the existing B-type 12-trit immediate field as two 6-trit
halves. The two-way branches `BEQ.T`, `BNE.T`, `BLT.T` and `BGE.T` are retained alongside them, still
using the full 12 trits, and `BGT.T` / `BLE.T` are added as operand-swap pseudo-instructions. See
[Three-Way Branching](#three-way-branching).

**Compatibility.** Assembly source is unaffected apart from the new mnemonics `BCGS.T`, `BCEG.T`,
`BGT.T` and `BLE.T`. Machine code predating this erratum is *not* binary compatible and must be
reassembled — REBEL-6 has no hardware or software in production, so the branch func assignments were
reallocated from scratch rather than worked around. RV32I binary compatibility is unaffected: L-type
execution and the binary branch group `0--0` do not use the ternary branch group.

### E-2 — the absolute word load and store are misnamed `lw.t` / `sw.t`

**Defect.** The mnemonics `lw.t` and `sw.t` appear in two different tables with two different
encodings: as indexed I-type / B-type forms taking a base register plus `imm12`
(`lw.t rd, imm12(rs1)`, `sw.t rs2, imm12(rs1)`), and again in the *Load Global* table as G-type /
Y-type forms taking a 24-trit address and no base register (`lw.t rd1, imm24`, `sw.t rs1, imm24`).
An assembler cannot resolve `LW.T` to one encoding or the other.

**Resolution.** This is a naming slip, not a duplicated instruction — both instructions are real and
neither is withdrawn. The absolute forms take the names their semantics imply:

| Mnemonic | Meaning | Format | Opcode | Address |
|----------|---------|--------|--------|---------|
| `LW.T rd1, rs1, imm12` | load word | I | `-+00` | rs1 + imm12 |
| `SW.T rs1, rs2, imm12` | store word | B | `0+00` | rs1 + imm12 |
| `LWA.T rd1, imm24` | load word **absolute** | G | `++` | imm24 |
| `SWA.T rs1, imm24` | store word **absolute** | Y | `-+` | imm24 |

The two pairs sit in different opcode groups — 4-trit `-+00`/`0+00` against 2-trit `++`/`-+`, which
the decoder already separates on the last trit — so there is no encoding conflict, only the mnemonic
collision. The `A` suffix matches the *Load Global* grouping the absolute forms are printed under
alongside `aipc.t` and `li.t`.

**Why both are needed.** They are complementary, not redundant. `LWA.T`/`SWA.T` reach a
link-time-known global anywhere in the 24-trit address space in **one** instruction, where RV32I
needs `lui`+`lw`; they consume no base register. `LW.T`/`SW.T` handle addresses computed at runtime —
stack slots, struct fields, array elements — which an absolute form structurally cannot express.
Withdrawing either one would leave a gap that the other cannot fill, and withdrawing the indexed word
form in particular would leave the ISA able to index a halfword and a tryte but not a word.

### E-3 — the cyclic shift is renamed `SC.T` → `ROT.T`

**Defect.** The [A extension](rebel6-extensions.md#a--atomics) adopts the RISC-V atomics naming —
`LR.T` (load-reserved) and `SC.T` (store-conditional) — because those names are load-bearing across
the entire RTOS and systems-software literature the extension exists to serve. The base ISA's cyclic
shift already used `SC.T` ("shift cyclic"), inherited from REBEL-2. One mnemonic cannot name both a
shift and a store-conditional.

**Resolution.** The cyclic shift is renamed **`ROT.T`** (rotate) — the universal name for the
operation. Its encoding is unchanged (opcode `0000`, func `-+`, selector field zero); the balanced
`shamt` already expresses both directions (positive rotates toward the MST, negative away), so one
mnemonic still covers left and right rotation. `SC.T` now refers exclusively to the A extension's
store-conditional. REBEL-2 V2.2's documentation is annotated with the same rename to keep the family
consistent.

**Compatibility.** Machine code is unaffected — this is a rename, not a re-encoding. Assemblers
should accept `SC.T` as a deprecated alias for the rotate *only* when the A extension is not enabled,
and must reject the ambiguity otherwise. The Ternary Workbench reference assembler implements the
rename: on REBEL-6 (where the A extension is always in the table) `ROT.T` is the rotate and `SC.T`
is exclusively the store-conditional; on REBEL-2 V2.0 and V2.2 (no atomics) `SC.T` remains a
deprecated alias and disassembly prints `ROT.T`.

### E-4 — shift direction belongs in the amount's sign; the left/right split is removed

**Defect.** A REBEL-6 shift amount is already a balanced-ternary value — `shamt` is 4 trits, ±40 —
and a balanced number carries its direction in its own sign. Yet the ISA spent separate mnemonics
*and separate func codes* on left versus right (`SL*`/`SR*`, `SLI*`/`SRI*`), and left negative
amounts undefined: the reference simulator aborted on them with "spec open". That is binary
thinking imported into a balanced ISA — a two's-complement machine must encode direction
out-of-band because an unsigned shift field cannot say "the other way"; a balanced field says it
for free. The same insight that gave REBEL-6 one fill *trit* where binary would need two bits (see
[Shift fill](#rebel-6-base-ternary)) applies to direction, and adopting it removes the
undefined-negative-amount hole instead of papering over it.

**Resolution.** One shift family with a **signed amount**, direction = sign: `k > 0` shifts toward
the MST (the old left), `k < 0` toward the LST, `k = 0` is the identity, `|k| ≥ 24` yields
all-fill. The fill-trit mechanism is unchanged. Register amounts are read from the **low 4 trits
of rs2** (balanced, ±40), which makes the sign inspection a 4-trit scan in hardware and finally
defines out-of-range register amounts. The architectural set shrinks from 13 shift instructions to
7:

| | Before | After |
|---|---|---|
| Register shifts | SLN/SLZ/SLP (func `-+`), SRN/SRZ/SRP (func `0-`) | **SHN/SHZ/SHP.T** — one func (`-+`) |
| Immediate shifts | SLIN/SLIZ/SLIP (func `--`), SRIN/SRIZ/SRIP (func `-0`) | **SHIN/SHIZ/SHIP.T** — one func (`--`) |
| Rotate | ROT.T | ROT.T (signed, both directions, mod 24) + **ROTR.T** (register amount, freed func `0-`) |

The freed R-type func slot (`0-` in group `--00`) is taken by **`ROTR.T`**, the register-amount
rotate that completes the group's orthogonality; its rd2 slot is required zero, the same selector
rule as `ROT.T`. The freed I-type func slot (`-0` in group `0000`) is *reserved*. The signed
amount is the exponent: `x·3^k` and `x/3^k` are the same instruction with the amount's sign
flipped, so strength reduction becomes sign-preserving arithmetic on the exponent rather than a
left/right case split, and dynamic-direction shifts (normalization loops, funnel shifts, softfloat
exponent alignment — `CLZT.T` feeding a shift directly) are single instructions.

**The register-form ruling (Option A — full collapse).** Immediate forms collapse losslessly — the
assembler rewrites `SRIZ.T rd, rs, k` as `SHIZ.T rd, rs, −k` at zero cost. Register forms cannot:
an assembler cannot negate a runtime value. The full collapse is adopted anyway: code that wants a
right shift by a register amount materializes a negative amount (one `STI.T`, outside the loop in
the common case; a compiler simply folds the negation into how it computes the amount).
Dynamic-direction shifts are precisely where the signed form shines, RV32I translation is
unaffected (RV32I `sll`/`srl`/`sra` live in the Base Binary group, untouched), and hand-written
ternary assembly with dynamic right-shift amounts is rare.

**Disposition of the old names.** `SL<f>.T` and `SLI<f>.T` live on as pseudo-instructions mapping
1:1 onto `SH<f>.T`/`SHI<f>.T`; `SRI<f>.T` lives on as a pseudo onto `SHI<f>.T` with the amount
negated. The register right shifts `SR{N,Z,P}.T` are **retired** — assembling them is an error.
Disassembly always prints the signed canonical names.

**Compatibility.** Assembly source assembles unchanged except for uses of the retired `SR*.T`
register forms, which must be rewritten (negate the amount register once, then `SH<f>.T`). Machine
code predating this erratum is *not* binary compatible where it used the right-shift funcs
(`0-` in `--00`, now `ROTR.T`; `-0` in `0000`, now reserved) — as with E-1, REBEL-6 has no hardware
or software in production, so the funcs were reallocated rather than worked around. RV32I binary
compatibility is unaffected.
