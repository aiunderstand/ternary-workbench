# REBEL-6 Extensions

Optional instruction-set extensions over the [base ISA](rebel6-isa.md). **Every extension is a
ternary-only design** — extensions are specified for the ternary machine, not ported from RISC-V's
binary extensions. The single exception is [Zicsr](#zicsr--csr-access-requires-base-binary), a
binary-compatibility extension that exists to serve RV32I binaries and **requires Base Binary**; it
is marked as such. No further binary-compatibility extensions are planned — the binary layer stays
RV32I (see [Base Binary](rebel6-isa.md#rebel-6-base-binary-optional-rv32i-compatibility-layer)).

## Encoding conventions

Extensions live in the `xx+0` opcode group (group trit `+`), reusing the base category pairs by
dominant format shape. Funcs shown are the canonical plane (func upper trits `00`); the eight
non-canonical planes of every opcode are reserved. Extension presence is discovered by probe: an
unimplemented extension instruction raises illegal-instruction (−1); a platform feature register is
reserved for future enumeration.

| Opcode | Shape | Allocated to | Canonical plane |
|--------|-------|--------------|-----------------|
| `00+0` | I | Ztl (`TLUTI.T`) | 1/9 |
| `0-+0` | B | — reserved | 0/9 |
| `0++0` | B | — reserved | 0/9 |
| `--+0` | R | M arithmetic + F arithmetic | **9/9 full** |
| `-0+0` | R | F compare/convert + Ztb + P scalar | 8/9 |
| `-++0` | R | A (LR/SC/AMO) | **9/9 full** |
| `+-+0` | D | Ztl (`TLUT.T`) + M (`MAC.T`) + F (`FMA.T`) + P (`TMAC.T`) | 4/9 |
| `+0+0` | — | P growth (tryte-lane ops) — **reserved** | 0/9 |
| `+++0` | — | — reserved | 0/9 |
| `++-0` | I (binary group) | Zicsr | 6/9 |

## M — integer multiply / divide

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

Hardware multiply and divide, plus a fused multiply-accumulate on the D format. Required for
DOOM-class fixed-point workloads and for softfloat performance
([trifloat24](rebel6-trifloat24.md) names softfloat as its primary constraint).

| Mnemonic | Format | Opcode | Func | Operands | Description |
|----------|--------|--------|------|----------|-------------|
| MUL.T | R | --+0 | -- | rd1, rs1, rs2 | rd1 = rs1 × rs2 (balanced wrap mod 3²⁴ — low word) |
| MULH.T | R | --+0 | -0 | rd1, rs1, rs2 | rd1 = high word of the exact 48-trit product |
| DIV.T | R | --+0 | 0- | rd1, rs1, rs2 | rd1 = rs1 ÷ rs2, truncated toward zero |
| REM.T | R | --+0 | 0+ | rd1, rs1, rs2 | rd1 = remainder pairing DIV.T: rs1 = q·rs2 + r, sign(r) = sign(rs1), \|r\| < \|rs2\| |
| MOD.T | R | --+0 | +- | rd1, rs1, rs2 | rd1 = rs1 mod rs2, floored: sign(r) = sign(rs2) |
| MAC.T | D | +-+0 | -0 | rd1, rs1, rs2, rs3 | rd1 = rs1 × rs2 + rs3 (balanced wrap) |

**Division has no overflow case.** The balanced range is symmetric, so `MIN ÷ −1 = MAX` is
representable — RV32I's special-cased `INT_MIN ÷ −1` simply does not exist here. Division by zero
does not trap: `DIV.T x, 0 = 0` and `REM.T x, 0 = x` (`MOD.T x, 0 = x`), which preserves the
quotient–remainder identity with `q = 0`. This is the RISC-V no-trap convention transposed, with a
cleaner zero-quotient choice available because no encoding must be sacrificed to an overflow case.

`MULH.T` composes exact wide arithmetic: the full product of two 24-trit values is
`MULH.T:MUL.T` as a 48-trit pair. There are no unsigned variants — balanced ternary has no
unsigned reading (compare the load group's missing `LHU.T`/`LTU.T`,
[Data widths](rebel6-isa.md#data-widths)).

## A — atomics

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

Load-reserved/store-conditional and fetch-and-op atomics for multi-hart D-RAM sharing — the
substrate for every RTOS lock and lock-free structure. The RISC-V names are kept deliberately
(they are load-bearing across the systems literature); the base cyclic shift was renamed `ROT.T`
to free `SC.T` — [Errata E-3](rebel6-isa.md#e-3--the-cyclic-shift-is-renamed-sct--rott).

All A instructions are R-format in opcode `-++0` (the load-category extension slot), word-sized,
and require word alignment (misaligned → −3/−4). The rd2 slot carries the ordering trits: trit 0 =
`rl` (release), trit 1 = `aq` (acquire); `0` = relaxed, `+` = set, `−` reserved; remaining rd2
trits zero. `aq`/`rl` follow RVWMO acquire/release semantics
([memory consistency](rebel6-platform.md#memory-consistency)). **Assembler syntax:** the bare
mnemonics below assemble the relaxed forms (ordering trits zero); suffixed forms (`.AQ`, `.RL`,
`.AQRL`) are reserved assembler syntax, to be added when multi-hart code needs them.

| Mnemonic | Func | Operands | Description |
|----------|------|----------|-------------|
| LR.T | -- | rd1, rs1 | rd1 = mem[rs1]; register a reservation on that word |
| SC.T | -0 | rd1, rs1, rs2 | if reservation holds: mem[rs1] = rs2, rd1 = 0; else rd1 = +1, no store |
| AMOSWAP.T | -+ | rd1, rs1, rs2 | atomic: rd1 = mem[rs1]; mem[rs1] = rs2 |
| AMOADD.T | 0- | rd1, rs1, rs2 | atomic: rd1 = old; mem[rs1] = old + rs2 (balanced wrap) |
| AMOAND.T | 00 | rd1, rs1, rs2 | atomic: mem[rs1] = tritwise min(old, rs2) |
| AMOOR.T | 0+ | rd1, rs1, rs2 | atomic: mem[rs1] = tritwise max(old, rs2) |
| AMOXOR.T | +- | rd1, rs1, rs2 | atomic: mem[rs1] = tritwise −(old × rs2) |
| AMOMIN.T | +0 | rd1, rs1, rs2 | atomic: mem[rs1] = min(old, rs2) (wordwise) |
| AMOMAX.T | ++ | rd1, rs1, rs2 | atomic: mem[rs1] = max(old, rs2) (wordwise) |

Reservation rules: one reservation per hart, word granularity; cleared by another hart's store to
the reserved word, by any trap, and by `SC.T` itself. **Forward progress:** an LR/SC loop of at
most 16 instructions containing no other loads, stores or system instructions must eventually
succeed — the RVWMO constraint, transposed. AMOs to the MMIO region are implementation-defined;
the recommendation is an access fault.

`AMOAND.T`/`AMOOR.T`/`AMOXOR.T` are the atomic forms of the base tritwise logic (min, max,
−product) — flag-mask idioms written against them behave exactly as their non-atomic counterparts.
There are **no binary (RV32A) encodings**: `rv32i` binaries are single-context programs with no
atomics, and the binary layer does not grow (see the rule in
[Base Binary](rebel6-isa.md#rebel-6-base-binary-optional-rv32i-compatibility-layer)).

## F — trifloat24 scalar float

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

Scalar floating point over the [trifloat24 format](rebel6-trifloat24.md). **Softfloat-first**: this
extension defines the instruction semantics that a software library implements today and an FPU
may implement later; programs bind to the same interface either way.

**No separate float register file.** trifloat24 values live in ordinary 24-trit registers — with
729 registers there is nothing a second file would buy except calling-convention complexity. Float
argument/return conventions are ABI matter ([rebel6-abi.md](rebel6-abi.md)).

| Mnemonic | Format | Opcode | Func | Operands | Description |
|----------|--------|--------|------|----------|-------------|
| FADD.T | R | --+0 | -+ | rd1, rs1, rs2 | rd1 = rs1 + rs2 |
| FSUB.T | R | --+0 | 00 | rd1, rs1, rs2 | rd1 = rs1 − rs2 |
| FMUL.T | R | --+0 | +0 | rd1, rs1, rs2 | rd1 = rs1 × rs2 |
| FDIV.T | R | --+0 | ++ | rd1, rs1, rs2 | rd1 = rs1 ÷ rs2 |
| FMA.T | D | +-+0 | -+ | rd1, rs1, rs2, rs3 | rd1 = rs1 × rs2 + rs3, single truncation |
| FCMP.T | R | -0+0 | -- | rd1, rs1, rs2 | three-way compare → {−1, 0, +1}; see NaN note |
| FCVT.W.T | R | -0+0 | -0 | rd1, rs1 | float → integer, truncated toward zero; saturates out of range; NaN → 0 |
| FCVT.T.W | R | -0+0 | -+ | rd1, rs1 | integer → float, nearest representable (truncation), canonical |

Normative behaviour, all operations: results are **canonical** per
[trifloat24 §3.4](rebel6-trifloat24.md); underflow is **gradual** (never flush-to-zero); rounding
is truncation (round-to-nearest by construction); NaN propagates; `FMA.T` performs one truncation
on the exact product-sum. Negation needs no instruction — `STI.T` negates a trifloat24 exactly,
including specials, because sign lives in the significand.

**NaN and comparison.** `FCMP.T` with a NaN operand returns 0. At the instruction level, unordered
is therefore indistinguishable from equal; the softfloat library's comparison wrappers (`isnan`
guards, one `LT.T` at offset +3 reads class-and-exponent) provide the IEEE-style unordered
distinction. A dedicated classify instruction is deliberately absent — class inspection is already
a single tryte load.

## P — packed ternary SIMD

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

Packed operations for ternary neural networks and DSP. The register file *is* the vector file: a
24-trit register natively holds **24 BitNet-class weights** — values in {−1, 0, +1}, one trit
each, no packing arithmetic, no lookup tables (contrast binary BitNet kernels: 2-bit unpack →
table lookup → multiply-add per block).

**The base ISA already provides the packed lane arithmetic.** Tritwise operations are 24-lane
SIMD by construction:

| Packed operation | Already exists as |
|------------------|-------------------|
| lane max / lane min | `OR.T` / `AND.T` |
| lane negate | `STI.T` |
| lane product (negated) | `XOR.T` — `XOR.T` *is* −(a×b) per lane; `STI.T ∘ XOR.T` is the lane product |
| lane select / arbitrary lane gate | `TLUT.T` ([Ztl](#ztl--ternary-logic)) |

What P adds is the **reductions and quantization** those lanes feed:

| Mnemonic | Format | Opcode | Func | Operands | Description |
|----------|--------|--------|------|----------|-------------|
| TMAC.T | D | +-+0 | 0- | rd1, rs1, rs2, rs3 | rd1 = rs3 + Σᵢ rs1[i]·rs2[i] over 24 trit lanes (sum ∈ ±24) |
| TSUM.T | R | -0+0 | 0+ | rd1, rs1 | rd1 = Σᵢ rs1[i] — horizontal trit sum (∈ ±24) |
| QNT.T | R | -0+0 | +- | rd1, rs1, rs2 | ternarize: rd1 = +1 if rs1 > rs2; −1 if rs1 < −rs2; else 0 (rs2 ≥ 0 = threshold) |
| HMAX.T | R | -0+0 | +0 | rd1, rs1 | rd1 = max of the four tryte lanes of rs1 (each read as a balanced value) |

| Pseudo | Expands to | Meaning |
|--------|------------|---------|
| TDOT.T rd, rs1, rs2 | `TMAC.T rd, rs1, rs2, X0` | packed ternary dot product |

**The BitNet kernel.** A W1.58 × A1.58 GEMV inner loop — weights and activations both ternarized —
is one `TMAC.T` per 24 multiply-accumulates:

```
loop:  LW.T   w,  wp, 0        # 24 weights, one word
       LW.T   a,  ap, 0        # 24 activations
       TMAC.T acc, w, a, acc   # 24 MACs, one instruction
       ...
```

Activations reach 1.58-bit form through `QNT.T` with the layer's absmean threshold — the BitNet
quantization step as one instruction per value. Numerically-stable softmax takes its
max-reduction from `HMAX.T`/`MAX.T`; no activation-function or softmax instructions exist, and
none are needed — ReLU is `MAX.T x, X0`, sign is `CMP.T x, X0`, and the exponential belongs in
software.

**Reserved: tryte-lane operations** (`+0+0`, entire opcode). The 4×tryte-lane view of a register
(saturating lane add/sub, trit-weight × tryte-activation dot for the W1.58 × A8 path, lane shuffles)
is deliberately *reserved, not specified* — no named workload in the current goals requires it, and
the encoding space is set aside where it will land.

## Ztl — ternary logic

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

Two instructions that compute **any** tritwise gate — the entire 1- and 2-input ternary function
space (3³ = 27 unary, 3⁹ = 19,683 binary gates) — under a programmable truth table. This is the
circuit-simulation engine: an MRCS netlist evaluates 24 instances of a gate type per instruction,
batched by type, with the truth table loaded once per batch. The x86 precedent is AVX-512
`vpternlogd`, which does the same for 3-input *binary* gates and displaced a zoo of fixed logic
ops; here the table is ternary and the zoo never needs to exist.

| Mnemonic | Format | Opcode | Func | Operands | Description |
|----------|--------|--------|------|----------|-------------|
| TLUT.T | D | +-+0 | -- | rd1, rs1, rs2, rs3 | per lane: rd1[i] = table(rs3)[rs1[i], rs2[i]] — 2-input gate, table in rs3 |
| TLUTI.T | I | 00+0 | -- | rd1, rs1, imm12 | per lane: rd1[i] = table(imm)[rs1[i]] — 1-input gate, table in the immediate |

**Table encoding.** A 2-input table is 9 trits: the entry for input pair (a, b) lives at trit
position `(a·3 + b) + 4` (positions 0…8, LST-first) of rs3; rs3's upper 15 trits are ignored and
reserved. A 1-input table is 3 trits: the entry for input a at trit position `a + 1` of the
immediate; imm trits 3…11 must be zero.

**Canonical gate library.** The classical ternary gates are **pseudo-instructions** with pinned
canonical tables — they cost no opcode. REBEL-2 V2.2 named several of these without defining them;
the truth tables below are now normative. Assembler support differs by arity: the **unary** gates
are fully assembler-supported in both directions (they assemble to `TLUTI.T` with the table inline,
and a canonical table disassembles back to the gate name); the **binary** gates are canonical table
constants written as `LI.T`-plus-`TLUT.T` idioms — single-mnemonic assembly for them (a 1→2
instruction expansion) is future toolchain work.

Unary — table given as (f(−), f(0), f(+)):

| Pseudo | Table | Gate |
|--------|-------|------|
| NTI.T rd, rs | (+, −, −) | negative ternary inverter — 0 reads as high |
| PTI.T rd, rs | (+, +, −) | positive ternary inverter — 0 reads as low |
| MTI.T rd, rs | (+, 0, +) | magnitude — \|x\| per trit |
| CYU.T rd, rs | (0, +, −) | cycle up: − → 0 → + → − (R2v2 `CYCLEUP.T`; alias accepted) |
| CYD.T rd, rs | (+, −, 0) | cycle down: − → + → 0 → − |

(`STI.T` — table (+, 0, −) — remains a base instruction; `BUF` — (−, 0, +) — is `MV.T`.)

Binary — table given in pair order (−,−) (−,0) (−,+) (0,−) (0,0) (0,+) (+,−) (+,0) (+,+):

| Pseudo | Table | Gate |
|--------|-------|------|
| KIMP.T rd, rs1, rs2 | (+, +, +, 0, 0, +, −, 0, +) | Kleene implication: a → b = max(−a, b) |
| CMPT.T rd, rs1, rs2 | (0, −, −, +, 0, −, +, +, 0) | tritwise compare: sign(a − b) per lane (R2v2 `CMPT.T`) |
| CONS.T rd, rs1, rs2 | (−, 0, 0, 0, 0, 0, 0, 0, +) | consensus: a when a = b, else 0 |

Any further gate — Łukasiewicz implication, tritwise multiplexers, threshold gates — is a table
constant, not a specification change.

## Ztb — trit manipulation

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-t">ternary-only</span>

| Mnemonic | Format | Opcode | Func | Operands | Description |
|----------|--------|--------|------|----------|-------------|
| CLZT.T | R | -0+0 | 0- | rd1, rs1 | count of leading zero trits (0…24; CLZT.T of 0 = 24) |
| TCNT.T | R | -0+0 | 00 | rd1, rs1 | count of non-zero trits (0…24) |

`CLZT.T` is the softfloat workhorse: [trifloat24](rebel6-trifloat24.md) normalization and
normalize-on-read are a leading-zero count, which is a loop without this instruction and one cycle
with it. `TCNT.T` serves NN sparsity statistics, ternary-code weight/distance metrics and parity
computations (the mod-2 alignment check of
[Alignment](rebel6-isa.md#alignment) is `TCNT.T` parity).

## Zicsr — CSR access (requires Base Binary)

<span class="r6-badge r6-e">extension</span> <span class="r6-badge r6-b">requires Base Binary</span>

The **only** binary-compatibility extension, and the only planned growth of the binary surface
beyond RV32I. It exists for one reason: OS-class RV32I binaries (Zephyr, Linux kernels) manipulate
trap state through CSR instructions, and with this shim they do so **unmodified** — the CSR
address decodes onto the *same negative-range registers* ternary code reaches with `MV.T`. One
state, two idioms, no copies. The address map and per-CSR layout views are normative in the
[platform document](rebel6-platform.md#zicsr-shim-requires-base-binary).

Binary-group encodings (opcode `++-0`, I-format: CSR number as a value in the imm12 field; `zimm`
forms carry the 5-bit immediate as a value in the rs1 slot):

| Mnemonic | Func | Operands | Description |
|----------|------|----------|-------------|
| CSRRW | -- | rd1, csr, rs1 | rd1 = view(csr); view(csr) = rs1 (read skipped if rd1 = x0) |
| CSRRS | -0 | rd1, csr, rs1 | rd1 = view(csr); set the view bits of rs1 (write skipped if rs1 = x0) |
| CSRRC | -+ | rd1, csr, rs1 | rd1 = view(csr); clear the view bits of rs1 |
| CSRRWI | 0- | rd1, csr, zimm | as CSRRW with 5-bit immediate |
| CSRRSI | 00 | rd1, csr, zimm | as CSRRS with 5-bit immediate |
| CSRRCI | 0+ | rd1, csr, zimm | as CSRRC with 5-bit immediate |

Set/clear operate on the CSR's **view** bit layout; the shim translates the result back through
the same view on write-back, so the underlying trit fields update coherently. Each CSR instruction
is atomic on its hart. An unmapped CSR number, or access above the current privilege, raises
illegal-CSR (−12) — enabling trap-and-emulate for CSRs a platform chooses not to wire.

Ternary code does not use Zicsr — `MV.T X-4, t0` *is* the CSR write, one instruction, no
addressing indirection. That asymmetry is the design: the shim is a compatibility veneer, not the
architecture.

## V — vector (reserved)

A wide-vector extension (configurable-length register groups, RVV-shaped) is a **stretch goal**:
nothing is specified, and no encoding is allocated beyond the reserved opcodes in the
[allocation table](#encoding-conventions). The packed-SIMD path
([P](#p--packed-ternary-simd)) covers the ternary-NN workloads that motivated acceleration;
V becomes worth designing when a workload outgrows 24-lane registers.
