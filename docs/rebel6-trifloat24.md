# trifloat24 — a 24-trit balanced-ternary floating-point format

This document is self-contained. It specifies a floating-point format for the REBEL-6 balanced-ternary ISA, to be implemented first as a software library (C++ and C#) and later, possibly, in hardware. Read §1 before anything else — the representation differs from IEEE 754 in ways that invalidate most binary floating-point intuitions.

Companion documents: [REBEL-6 ISA](rebel6-isa.md) (base ISA), [REBEL-6 Extensions](rebel6-extensions.md) (the F extension defines the scalar float instructions over this format; the M extension provides the integer multiply/divide a softfloat library builds on; Ztb provides `CLZT.T` for normalization).

---

## 1. Background you need before reading the spec

### 1.1 Balanced ternary

Digits are `−` (−1), `0`, and `+` (+1). A digit is a **trit**. Value of an n-trit number: Σ dᵢ · 3ⁱ with dᵢ ∈ {−1, 0, +1}.

Four properties matter here:

- **There is no sign bit.** The sign of a number is the sign of its most significant non-zero trit. Negation is digit-wise: swap every `−` and `+`, leave `0` alone. There is exactly one representation of zero (all trits `0`) — no `+0` and `−0`.
- **Truncation is round-to-nearest — on the retained-trit grid.** Discarding trailing trits leaves a tail bounded by half the last retained trit's weight, so truncation (applied after normalization) always rounds to the nearest value **on the grid of retained trits**: the error is ≤ ½ ulp of the retained grid, always. That is nearest-*representable* almost everywhere, but not quite: the format's representables are per-exponent grids of different pitch, and within the 2-ulp gap just above each significand band boundary a nearer value exists on the finer grid of the next-lower exponent. With `M` the maximum significand, exact values in `(M·3^E, (M+2)·3^E)` must round on the coarser next grid, and truncation can land 2 fine-ulps from a representable that is nearer. Exact ties *on the retained grid* cannot occur in a terminating expansion — which covers `+`, `−` and `×` of representable operands — but under a nearest-representable reading a tie does exist at each band boundary: `(M+1)·3^E` is exactly equidistant from `M·3^E` and `(M+2)·3^E`, and truncation resolves it deterministically **away from zero** — note this is the opposite of the toward-zero tie rule §4.2 pins for division. Division remains the genuinely different case: quotients can have non-terminating expansions where exact half-ulp ties occur on the retained grid itself, and §4.2 pins that rule. No rounding-mode field is needed for the default mode.
- **The range is symmetric.** An n-trit balanced integer spans ±(3ⁿ−1)/2. There is no asymmetry analogous to two's complement's extra negative value.
- **A tryte is 6 trits** (729 values, ≈9.51 bits). REBEL-6 data memory is tryte-addressed, little-endian.

### 1.2 REBEL-6 facts the format depends on

- Registers are 24 trits = 4 trytes. A `trifloat24` occupies one register.
- `LT.T rd, rs1, imm` loads one tryte. Extracting a tryte-aligned field is one instruction with no shift or mask.
- Data memory is tryte-addressed little-endian: a word at address A is `t(A) + 3⁶·t(A+1) + 3¹²·t(A+2) + 3¹⁸·t(A+3)`.
- There is no FPU. Scalar integer multiply and divide exist only in the [M extension](rebel6-extensions.md#m--integer-multiply--divide) (`MUL.T`, `MULH.T`, `DIV.T`); a softfloat implementation should use them — and [Ztb](rebel6-extensions.md#ztb--trit-manipulation)'s `CLZT.T` for normalization — when the implementation provides them. Everything below runs in software over 24-trit integer operations. Assume the absence of an FPU remains true for the foreseeable future — **softfloat performance is the primary design constraint.**

---

## 2. Format

```
tryte 0        tryte 1        tryte 2        tryte 3
├ exponent ┤├ class ┤   ├─────────────  significand  ────────────┤
│  5 trits  ││1 trit │   │   6 trits   │   6 trits   │   6 trits   │
      5          1                18 trits, balanced, signed
```

Total 24 trits. Layout is little-endian by tryte and **exponent-first**: tryte 0 holds the exponent (trit offsets 0–4) and the class trit (offset 5); the significand occupies trytes 1–3 (trit offsets 6–23). Field order was decided by benchmark — §5.1 is the decision record.

| Field | Width | Trit offsets | Encoding |
|---|---|---|---|
| exponent `E` | 5 trits | 0–4 | balanced integer, ±(3⁵−1)/2 = ±121. **No bias.** |
| class `C` | 1 trit | 5 | `0` = finite, `+` = infinity, `−` = NaN |
| significand `S` | 18 trits | 6–23 | balanced integer, ±(3¹⁸−1)/2 = ±193,710,244. **Carries the sign of the whole number.** |

Value of a finite number: **v = S × 3^E**

### 2.1 Why each choice

**Tryte alignment is the load-bearing decision; field *order* within it was decided by measurement.** Softfloat will run for years before any FPU exists, and unaligned fields would add a shift-and-merge to every call of `__addtf3`. But tryte alignment holds for **both** candidate orders: `E` and `C` share one tryte and `S` is three contiguous trytes whether that tryte comes first or last, so one `LT.T` (here at offset +0) extracts exponent-and-class either way, and a future FPU datapath sees three clean tryte slices of significand either way. Neither extraction cost nor the FPU-slice argument ever discriminated between the orders. What did discriminate is the packed-word ordering property and its measured instruction counts — the §5.1 benchmark decided for exponent-first. Unchanged under either order: 4-tryte alignment is a digit inspection.

**No sign field.** Sign lives in the leading non-zero trit of `S`. Negating a trifloat24 is digit-wise negation of the significand — the same operation as integer negation (`STI.T`).

**No exponent bias.** A balanced-ternary exponent is already signed. IEEE's bias exists only because binary exponent fields are unsigned. Removing it deletes an add/subtract from every operation.

**No signed zero.** Balanced ternary has one zero. `S = 0` is zero regardless of `E`; canonical zero is `S = 0, E = 0, C = 0`. This eliminates the IEEE hazard where `x == y` holds while `1/x` and `1/y` are `+∞` and `−∞`.

**Class trit instead of reserved exponent encodings.** IEEE steals two of 256 exponent encodings for Inf and NaN, which is why the exponent is biased and why "all ones means special" exists. A dedicated trit uses all three of its states productively and keeps the **full ±121 exponent range usable**. Decoding a special value is one trit inspection, not a comparison against a reserved pattern. Because sign lives in `S`, `+∞` and `−∞` share one class encoding and differ only in the significand's sign.

### 2.2 Comparison with binary32

| | binary32 | trifloat24 |
|---|---|---|
| Storage | 32 bits | 24 trits (≈38 bits of information) |
| Precision | 24 bits ≈ 7.22 decimal digits | 18 trits ≈ **8.59 decimal digits** |
| Normal range | ~10^±38 | **~10^±58** |
| Sign field | 1 bit | none |
| Exponent bias | 127 | none |
| Reserved encodings | 2 of 256 | none |
| Signed zero | yes | no |
| Rounding-mode field | 2 bits | none for the default mode (truncation = round-to-nearest on the retained grid, §1.1) |
| Hidden digit | yes | **no** — see §3 |

---

## 3. Gradual underflow

**This is the part most likely to be got wrong. Implement gradual underflow, not flush-to-zero.**

### 3.1 What it is for

The property at stake is:

> **x − y = 0 ⟺ x = y**

Without it, this common guard fails:

```c
if (x != y)  z = 1.0 / (x - y);   // divides by zero anyway
```

Worked example. Let `E_min = −121` and take two adjacent representable numbers at the bottom of the normal range:

```
x = (S₀ + 1) × 3^E_min
y =  S₀      × 3^E_min
x − y = 1 × 3^E_min          exact, non-zero
```

Storing that result requires `S = 1`, which has seventeen leading zero trits. Normalizing would mean shifting left 17 and setting `E = E_min − 17`, below the exponent floor.

- **Flush-to-zero:** unrepresentable → return 0. But `x ≠ y`, so the guard passes and the division blows up.
- **Gradual underflow:** keep `S = 1`, `E = E_min`, do not normalize. Representable, non-zero, guard works.

Generally: any difference of two representable numbers is an integer multiple of the smallest ulp, `1 × 3^E_min`. Gradual underflow makes every such multiple representable. Flush-to-zero makes the smallest representable magnitude ~3¹⁷ times larger, collapsing a whole band of legitimate non-zero differences to zero.

**Do not attribute this property to balanced ternary's single zero.** The single zero fixes a different IEEE wart (signed-zero reciprocals). Only gradual underflow saves `x − y`. With flush-to-zero, trifloat24 breaks exactly as IEEE-with-FTZ breaks.

### 3.2 Why it is cheap here — the hidden-digit difference

In IEEE binary, normals store 23 bits and *imply* a leading 1. A subnormal has no leading 1, so it needs a **reserved exponent encoding** to signal "the hidden bit is 0 this time, and the exponent is E_min, not E_min − 1." That is a separate decode path, and it is why many embedded FPUs flush and why x86 subnormals were microcode-trapped for years.

**Balanced ternary has no hidden digit.** The leading trit can be `−`, `0`, or `+`, and it carries the sign — it cannot be hidden without reintroducing a sign field. Therefore:

1. A significand with leading zero trits is **already a legal encoding**. Nothing signals it.
2. The class trit already handles Inf and NaN, so no exponent encoding is stolen.
3. The normalization shifter already exists, since any subtraction can cancel catastrophically.

**The entire incremental cost is: clamp the exponent at `E_min` and stop normalizing.** A comparator and a mux, versus IEEE's separate decode path.

### 3.3 What it buys

Denormalizing across all 18 significand trits extends the bottom of the range by 17 trits ≈ 8 decades — roughly 10⁻⁵⁹ down to 10⁻⁶⁷. (binary32 gains a comparable ~7 decades from its 23 subnormal bits, for considerably more hardware.)

### 3.4 Canonical form — mandatory, because the encoding is redundant

Without a hidden digit, `S × 3^E` and `3S × 3^(E−1)` denote the same value. Binary normals are unique; this is closer to IEEE decimal's cohorts. Three rules:

- **Canonicalization rule.** A finite result is normalized (leading trit of `S` non-zero) **unless** `E = E_min`. At `E = E_min`, `S` may have leading zeros. Zero is canonically `S = 0, E = 0, C = 0`.
- **Producers.** Every operation returning a finite result must emit canonical form.
- **Consumers.** Non-canonical input must have defined behaviour. Choose one and document it: **normalize on read** (safer, costs a leading-zero count on every load) or **fault**. Recommend normalize-on-read for the software library; revisit for hardware.

Note that comparison must be canonical-form-aware regardless — `S = 3, E = 4` and `S = 1, E = 5` are equal in value but differ trit-wise.

---

## 4. Special values

| Class `C` | Meaning | Notes |
|---|---|---|
| `0` | finite | value = `S × 3^E` |
| `+` | infinity | sign from `S`'s leading non-zero trit; `S` must be non-zero and canonical (recommend `S = ±1, E = 0`) |
| `−` | NaN | quiet-only, single canonical encoding — see §4.3 |

`C = +` with `S = 0` is **not** a valid encoding; consumers **read it as NaN**. This is a deliberate
third consumer behaviour, distinct from §3.4's normalize-on-read (which applies to non-canonical
*finite* encodings): a signless infinity has no meaningful normalization, and NaN is the value that
says "this encoding carries no number". Both reference implementations do this.

### 4.1 Overflow and division by zero

Both are defined, non-trapping:

- **Overflow.** A finite result whose magnitude exceeds the largest normal (`S = ±(3¹⁸−1)/2, E = +121`) becomes **infinity with the sign of the exact result**. Saturating to the largest finite value instead would silently corrupt magnitudes and break the monotonicity of comparison across the overflow boundary; infinity preserves the "too large" signal that ported IEEE-shaped code expects. One consequence of §1.1's band-boundary gap is pinned here: at `E = E_max` the gap above `M·3^121` has nowhere to renormalize — there is no coarser grid above the exponent ceiling — so those exact values become **±Inf even though the largest finite value is nearer**. This is deliberate and stated, so no implementation "fixes" it by saturating.
- **Division by zero.** `x ÷ 0` with `x ≠ 0` yields **infinity carrying the dividend's sign** — the format has a single zero, so the divisor contributes no sign. `0 ÷ 0` yields NaN. This is what §3.1's guard idiom (`z = 1.0 / (x − y)`) already presumes: a defined result, never a trap.
- **Infinity arithmetic.** Infinity is absorbing for `+`, `−`, `×` with finite operands (sign by the usual rules). `Inf ÷ 0` yields infinity with the dividend's sign; `finite ÷ Inf` yields zero. The indeterminate forms — `Inf ÷ Inf`, `Inf × 0`, and addition/subtraction that cancels infinities of the same sign — yield NaN. These mirror both reference implementations.

### 4.2 Rounding of non-terminating quotients

`+`, `−` and `×` of representable operands have terminating expansions, so truncation is exactly round-to-nearest **on the retained-trit grid** and tie-free there (§1.1 — including its band-boundary exception, where the retained-grid result is not always the globally nearest representable and the exact `(M+1)·3^E` boundary value resolves away from zero). **Division does not**: a quotient is a rational whose balanced-ternary expansion may not terminate, and exact half-ulp ties occur (`1 ÷ 2` sits exactly between two representables). The tie rule is **toward zero**: it needs no extra comparator (cheapest in hardware and softfloat), it is consistent with the format's truncation ethos, and it is deterministic. It never arises in `+`, `−`, `×`; it applies to `÷` **and to conversion from binary floating point** (§4.4) — a binary rational such as `m × 2^k` is generally non-terminating in ternary, so binary→ternary conversion hits exact half-ulp ties through the same rounding path and resolves them the same way.

### 4.3 NaN convention

- NaN is **quiet-only**; there are no signaling NaNs (the format has no floating-point trap architecture to serve them).
- There is **one canonical NaN encoding**; payloads are not preserved — any operation with a NaN operand returns the canonical NaN.
- **Comparison involving NaN is unordered**: the F extension's `FCMP.T` returns `0` when either operand is NaN, and language-level relational operators (`<`, `>`, `==`) are false against NaN. Library environments that additionally require a total order for sorting (e.g. .NET `CompareTo`) may define one, ordering NaN consistently at one end; that total order is a library contract, not part of the format.

### 4.4 Conversions

- **float → integer** (`FCVT.W.T`): the value is converted by balanced truncation — which here is truly round-to-nearest-integer, with no caveat: §1.1's band-boundary exception does not apply, because the integer target is one uniform grid, not a union of per-exponent grids. Results beyond the 24-trit integer range, including ±Inf, **saturate** to ±(3²⁴−1)/2. **NaN converts to 0.** Rationale: saturation is thereby reserved for genuinely large magnitudes, so a NaN can never masquerade as a range-limit value — a distinction IEEE-convention ISAs (which convert NaN to the maximum integer) cannot make.
- **integer → float** (`FCVT.T.W`): exact whenever the magnitude fits in 18 significand trits; otherwise truncation-rounded per the default mode.
- **binary float ↔ trifloat24** (library-level): converted through exact decomposition of the binary value, never through host `double` arithmetic — `double` is neither a superset nor a subset of this format (§6).

---

## 5. Open questions and decision records

### 5.1 Field order — decision record: exponent-first, by benchmark

**Decided: exponent-first**, the layout §2 shows. The decision was made the way this section originally demanded — both layouts implemented and measured, on the reference simulator via `minstret` instruction counts (benchmark at `REBEL-toolchain/benchmarks/trifloat24-field-order`, with a value-exhaustive scaled model at `REBEL-toolchain/benchmarks/trifloat24-field-order-mini`). Do not revisit without re-running it.

**The packed-word ordering properties are complementary, not absent.** trifloat24 has no sign field, but raw integer inspection of the packed word still buys each layout something — a different something:

- **Exponent-first** (S in the top trits): for canonical values, **sign(packed) = sign(float), exactly**. Any non-zero canonical significand puts |S·3⁶| ≥ 729, which dominates |C·3⁵ + E| ≤ 364, and canonical zero packs to 0. The sign test is therefore one `CMP.T` against zero, with no unpack.
- **Significand-first** (E in the top trits): raw packed comparison orders same-sign values correctly for positives, but the both-negative case inverts **only when the exponents differ** — at equal exponents the signed S field dominates directly and already orders correctly. Handling that subtlety costs the corrected implementation its advantage: measured, all same-sign compares tie.

**Measured results.** Exponent-first ties or wins every criterion:

| Criterion | Result |
|---|---|
| mixed-sign / zero comparisons — 49 of the 101 exhaustively enumerated `FCMP` control paths | **−2 instructions**: the sign dispatch is free |
| unpack of a memory-resident operand | **−1 instruction** per operand (−3 per memory-fed `FMA.T`) |
| compare-heavy sort composite | **−4.2%** |
| memory-FMA dot-product composite | **−2.3%** |
| all 434 `FADD` control paths, all 248 `FMUL` control paths, `FDIV`, `FNEG`, `FCVT`, all same-sign compares | tie, exactly |

Significand-first wins nothing.

**Value-equivalence.** A value-exhaustive sweep of a structurally identical scaled 7-trit format — all 4.78M operand pairs — proved the two layouts value-equivalent everywhere: every operation returns identical results under either order. Field order is purely a cost question, and the costs are measured above.

**Correction.** The integer-comparison discussion this section previously carried mis-attributed the ordering properties between the two layouts; the corrected analysis is the one above.

### 5.2 Subnormal cost in software

§3.2's cheapness argument is a *hardware* argument. In software, gradual underflow still costs a branch and a variable shift on the underflow path. Measure it. If the cost is material, the answer is still gradual underflow with a fast path for the common case — not FTZ.

### 5.3 Is a double needed?

24 trits already carries ~38 bits of information, more than binary32. A hypothetical 48-trit `trifloat48` would carry ~76 bits, more than binary64. For the embedded target a single format may suffice. Do not design a double until a workload demands one.

---

## 6. Test requirements

The following must exist. The first two are the ones that catch real bugs.

**The underflow identity.** `∀ representable x, y: (x − y == 0) ⟺ (x == y)`. Property-tested across the full range, with directed fixtures at adjacent values near `E_min` — the exact case in §3.1. This test *fails* under flush-to-zero, which is the point.

**Canonical form.** Every operation's output is canonical per §3.4. Round-trip: encode → decode → encode is identity. Non-canonical input behaves as documented (normalized or faulted, per the §3.4 choice). Values equal in magnitude but differing trit-wise (`S = 3, E = 4` vs `S = 1, E = 5`) compare equal.

**Differential against an exact reference.** Use rational arithmetic (`Fraction` / `BigInteger` ratio), not `double` — `double` is neither a superset nor a subset of this format and will produce false failures. The reference must round as §1.1 specifies — normalize, then truncate on the retained grid — not by a global nearest-representable search. Cover all four operations, 10⁶ random pairs plus directed fixtures at: ±max normal, ±min normal, ±min subnormal, zero, `E_min`, `E_max`, and significands with 0…17 leading zero trits.

**Cross-language.** The C++ and C# implementations fuzzed against each other on the same corpus. Divergence indicates a specification ambiguity, not a bug in one implementation — fix the spec, then both.

**No signed zero.** No operation produces a second zero encoding. `1/x` and `1/y` agree in sign whenever `x == y`.

**Round-to-retained-grid in `+`, `−`, `×`.** The exhaustive sweep at small exponents must assert round-to-nearest **on the retained grid** (§1.1), not nearest-representable — a nearest-representable oracle falsely fails the band-boundary cases. The exact-rational reference must model the format's actual rule: normalize, then truncate on the retained grid. Directed fixtures at `M·3^E + δ` for small δ: values on both sides of the band-boundary gap's midpoint, the exact `(M+1)·3^E` tie asserting away-from-zero resolution, and the `E_max` overflow hole of §4.1 asserting ±Inf. On the retained grid, no result of these operations requires a tie-breaking rule. **Division ties.** Directed fixtures at exact half-ulp quotients (`1 ÷ 2` and analogues at other exponents) asserting the §4.2 toward-zero rule.

**Special values.** Class-trit decode for ±Inf and NaN; propagation through all four operations; `E`'s full ±121 range usable (no encoding reserved).

**Field-order benchmark.** Delivered: both layouts implemented and measured via `minstret` on the reference simulator (`REBEL-toolchain/benchmarks/trifloat24-field-order` and `-mini`); §5.1 records the results. It remains a conformance asset — re-run it before any change that touches packing or unpacking.

---

## 7. Summary of decisions for the implementer

**Fixed:** 5/1/18 field widths (E/C/S); tryte alignment; **exponent-first field order (§5.1, decided by benchmark)**; no sign field; no exponent bias; no signed zero; class trit for Inf/NaN; truncation as default rounding (round-to-nearest on the retained-trit grid — §1.1); **gradual underflow with exponent clamped at `E_min`**; canonicalization rule of §3.4; overflow → ±Inf and non-trapping division by zero (§4.1); toward-zero ties for `÷` only (§4.2); quiet-only canonical NaN, unordered comparison (§4.3); saturating `FCVT` with NaN → 0 (§4.4); `C = +, S = 0` read as NaN; normalize-on-read for the software library (§3.4).

**To decide during implementation, with evidence:** nothing. Field order was the last open decision; the §5.1 benchmark closed it.

**Do not do:** flush-to-zero; add a sign field; add an exponent bias; add a rounding-mode field for the default mode; design a 48-trit double before a workload requires it.
