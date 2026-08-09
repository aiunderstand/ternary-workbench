# trifloat24 — a 24-trit balanced-ternary floating-point format

This document is self-contained. It specifies a floating-point format for the REBEL-6 balanced-ternary ISA, to be implemented first as a software library (C++ and C#) and later, possibly, in hardware. Read §1 before anything else — the representation differs from IEEE 754 in ways that invalidate most binary floating-point intuitions.

Companion documents: [REBEL-6 ISA](rebel6-isa.md) (base ISA), [REBEL-6 Extensions](rebel6-extensions.md) (the F extension defines the scalar float instructions over this format; the M extension provides the integer multiply/divide a softfloat library builds on; Ztb provides `CLZT.T` for normalization).

---

## 1. Background you need before reading the spec

### 1.1 Balanced ternary

Digits are `−` (−1), `0`, and `+` (+1). A digit is a **trit**. Value of an n-trit number: Σ dᵢ · 3ⁱ with dᵢ ∈ {−1, 0, +1}.

Four properties matter here:

- **There is no sign bit.** The sign of a number is the sign of its most significant non-zero trit. Negation is digit-wise: swap every `−` and `+`, leave `0` alone. There is exactly one representation of zero (all trits `0`) — no `+0` and `−0`.
- **Truncation is round-to-nearest.** Discarding trailing trits leaves a tail bounded by half the last retained trit's weight, so truncation always rounds to the nearest representable value. Exact ties cannot occur in a terminating expansion. No rounding-mode field and no tie-breaking rule are needed for the default mode.
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
├─────────────  significand  ────────────┤   ├ class ┤├ exponent ┤
│   6 trits   │   6 trits   │   6 trits   │   │1 trit ││  5 trits  │
        18 trits, balanced, signed              1          5
```

Total 24 trits. Layout is little-endian by tryte: significand occupies trytes 0–2, and the class trit plus exponent occupy tryte 3.

| Field | Width | Encoding |
|---|---|---|
| significand `S` | 18 trits | balanced integer, ±(3¹⁸−1)/2 = ±193,710,244. **Carries the sign of the whole number.** |
| class `C` | 1 trit | `0` = finite, `+` = infinity, `−` = NaN |
| exponent `E` | 5 trits | balanced integer, ±(3⁵−1)/2 = ±121. **No bias.** |

Value of a finite number: **v = S × 3^E**

### 2.1 Why each choice

**Tryte alignment is the load-bearing decision.** Softfloat will run for years before any FPU exists. With this layout, `LT.T` at offset +3 extracts class-and-exponent in one instruction, and the significand is three contiguous trytes. Unaligned fields would add a shift-and-merge to every call of `__addtf3`. Secondary benefit: the significand is three clean tryte slices for a future FPU datapath, and 4-tryte alignment is a digit inspection.

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
| Rounding-mode field | 2 bits | none for the default mode |
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
| `−` | NaN | payload may live in `S`; define whether NaN is quiet or signaling, and the propagation rule |

Open items to settle during implementation: NaN payload convention, quiet/signaling distinction, and whether `C = +` with `S = 0` is a valid encoding (recommend: reject as non-canonical).

---

## 5. Open questions

### 5.1 Field order — decide by benchmark, do not assume

The layout in §2 is significand-first. The alternative is exponent-first, which in IEEE enables comparing floats as integers.

**That argument mostly evaporates here.** IEEE can do the integer-comparison trick because it *has* a sign bit to flip when converting sign-magnitude to a monotone integer ordering. trifloat24 has no sign field:

- **Exponent-first fails on negatives.** Sign lives inside the significand, so a larger exponent makes a negative value *smaller* — the ordering inverts, and there is no sign bit to flip to fix it.
- **Significand-first gets the sign right** (leading trit) but orders wrongly within a sign class.

Neither gives free integer comparison. So the deciding factor is softfloat extraction cost — the `LT.T` at offset +3 — which favours significand-first. **Implement both, benchmark the softfloat library, and record the result.** Do not treat §2's layout as settled.

Note that `CMP.T` (three-way compare) and `BCGS.T` (three-way branch) already make explicit comparison cheap on REBEL-6, which further reduces the value of the integer-comparison trick.

### 5.2 Subnormal cost in software

§3.2's cheapness argument is a *hardware* argument. In software, gradual underflow still costs a branch and a variable shift on the underflow path. Measure it. If the cost is material, the answer is still gradual underflow with a fast path for the common case — not FTZ.

### 5.3 Is a double needed?

24 trits already carries ~38 bits of information, more than binary32. A hypothetical 48-trit `trifloat48` would carry ~76 bits, more than binary64. For the embedded target a single format may suffice. Do not design a double until a workload demands one.

---

## 6. Test requirements

The following must exist. The first two are the ones that catch real bugs.

**The underflow identity.** `∀ representable x, y: (x − y == 0) ⟺ (x == y)`. Property-tested across the full range, with directed fixtures at adjacent values near `E_min` — the exact case in §3.1. This test *fails* under flush-to-zero, which is the point.

**Canonical form.** Every operation's output is canonical per §3.4. Round-trip: encode → decode → encode is identity. Non-canonical input behaves as documented (normalized or faulted, per the §3.4 choice). Values equal in magnitude but differing trit-wise (`S = 3, E = 4` vs `S = 1, E = 5`) compare equal.

**Differential against an exact reference.** Use rational arithmetic (`Fraction` / `BigInteger` ratio), not `double` — `double` is neither a superset nor a subset of this format and will produce false failures. Cover all four operations, 10⁶ random pairs plus directed fixtures at: ±max normal, ±min normal, ±min subnormal, zero, `E_min`, `E_max`, and significands with 0…17 leading zero trits.

**Cross-language.** The C++ and C# implementations fuzzed against each other on the same corpus. Divergence indicates a specification ambiguity, not a bug in one implementation — fix the spec, then both.

**No signed zero.** No operation produces a second zero encoding. `1/x` and `1/y` agree in sign whenever `x == y`.

**No ties.** Truncation equals round-to-nearest: exhaustive sweep at small exponents confirming no result requires a tie-breaking rule.

**Special values.** Class-trit decode for ±Inf and NaN; propagation through all four operations; `E`'s full ±121 range usable (no encoding reserved).

**Field-order benchmark.** Both layouts implemented, softfloat operation counts and instruction counts recorded per operation. This is a deliverable, not an optional extra — §5.1 depends on it.

---

## 7. Summary of decisions for the implementer

**Fixed:** 18/1/5 field widths; tryte alignment; no sign field; no exponent bias; no signed zero; class trit for Inf/NaN; truncation as default rounding; **gradual underflow with exponent clamped at `E_min`**; canonicalization rule of §3.4.

**To decide during implementation, with evidence:** field order (§5.1, benchmark required); non-canonical input handling (normalize vs fault); NaN payload and quiet/signaling convention; whether `C = +, S = 0` is valid.

**Do not do:** flush-to-zero; add a sign field; add an exponent bias; add a rounding-mode field for the default mode; design a 48-trit double before a workload requires it.
