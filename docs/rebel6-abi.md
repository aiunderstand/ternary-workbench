# REBEL-6 ABI

The application binary interface for REBEL-6: register roles, calling convention, type model,
stack discipline, runtime contract and semihosting. Companion to the
[base ISA](rebel6-isa.md), [platform](rebel6-platform.md) and
[extensions](rebel6-extensions.md) documents.

**Design rule: identity mapping.** RV32I's register roles map onto REBEL-6 as **xN ↔ X+N** —
R2R's register translation is the identity function, every RISC-V calling-convention document
reads directly onto REBEL-6, and transpiled code needs no register renaming ever. The two
deliberate divergences are `gp` (retired — the [zero window](rebel6-isa.md#memory-map) replaces
it; `X3` becomes a reserved platform register) and the stack alignment (word, not 16 bytes).

## Register roles (ilp32 variants: r6-single, r6-mp3, r6-mp9)

Hart-local indices; the negative range is system/streaming state
([platform register map](rebel6-platform.md#register-map--negative-range-standard-layout)) and is
**never** allocatable.

| REBEL-6 | RV32I | ABI name | Role | Saved by |
|---------|-------|----------|------|----------|
| `X0` | x0 | `zero` | hardwired zero | — |
| `X1` | x1 | `ra` | return address | caller |
| `X2` | x2 | `sp` | stack pointer | callee |
| `X3` | x3 | — | **reserved platform register** (gp retired; do not allocate) | — |
| `X4` | x4 | `tp` | thread pointer (reserved until threads exist) | — |
| `X5`–`X7` | x5–x7 | `t0`–`t2` | temporaries | caller |
| `X8` | x8 | `s0`/`fp` | saved / optional frame pointer | callee |
| `X9` | x9 | `s1` | saved | callee |
| `X10`–`X17` | x10–x17 | `a0`–`a7` | arguments / return values | caller |
| `X18`–`X27` | x18–x27 | `s2`–`s11` | saved | callee |
| `X28`–`X31` | x28–x31 | `t3`–`t6` | temporaries | caller |
| `X32` … top of window | — | `e0`, `e1`, … | **extended pool** — caller-saved scratch | caller |

**The extended pool** is REBEL-6's dividend: 8 registers on `r6-mp9` (X32–X40), 89 on `r6-mp3`,
332 on `r6-single`. All caller-saved, free for register allocators, link-time optimisers and
hand-written kernels; no unwind or debug metadata obligations attach to them. Whether a slice
should become callee-saved is an open measurement (see [Open items](#open-items)) — until then the
simple rule stands: everything above `X31` is caller-saved.

The negative window (`X-1` and below) is not part of the ABI's allocatable set at any privilege
level: system registers trap on unprivileged access, and **streaming registers**
(`X-10 … X-14`) form a distinct register class — reads volatile, never CSE'd, never spilled,
never saved across calls or context switches
([platform](rebel6-platform.md#streaming-registers)).

## Register roles (ilp32e variant: r6-mp27)

RV32E's 16 registers on a 27-register hart-local window (`X-13 … X+13`); roles x0–x13 map
identically at `X0`–`X+13`, and the last two spill into the deepest negative indices:

| REBEL-6 | RV32E | ABI name | | REBEL-6 | RV32E | ABI name |
|---------|-------|----------|-|---------|-------|----------|
| `X0`–`X+13` | x0–x13 | as the ilp32 table above (through `t2`, `s0`, `s1`, `a0`–`a3`) | | `X-12` | x14 | `a4` |
| `X-7`–`X-11` | — | extended pool (5, caller-saved) | | `X-13` | x15 | `a5` |

The compact system layout (`X-1 … X-6`) is defined in the
[platform document](rebel6-platform.md#register-map--negative-range-standard-layout). Calling
convention follows ilp32e: arguments in `a0`–`a5`, the rest on the stack.

## Type model

ilp32 sizes and layout, transposed to trytes (1 byte ↔ 1 tryte under
[RV32I translation](rebel6-isa.md#rv32i-translation)); natural alignment throughout
([Alignment](rebel6-isa.md#alignment)).

| C type | Size (trytes) | Alignment | Held in |
|--------|---------------|-----------|---------|
| `char` / `_Bool` | 1 | 1 | low tryte of a word (narrow loads zero-pad exactly) |
| `short` | 2 | 2 | low halfword |
| `int`, `long`, all pointers | 4 (one word) | 4 | one register |
| `long long` | 8 (two words) | 4 | register pair / two stack words |
| `float` | 4 — [trifloat24](rebel6-trifloat24.md) | 4 | one register (no separate float file) |
| `double` | 4 — **maps to trifloat24 for now** | 4 | one register |
| `enum` | as `int` | 4 | |

Notes: a REBEL-6 word carries ±(3²⁴−1)/2 ≈ ±1.41×10¹¹ — a strict superset of `int32_t`'s range —
and Base Binary instructions wrap it mod 2³², so ilp32 semantics are exact under translation.
`double` = trifloat24 is a deliberate interim ruling (trifloat24 already exceeds binary32; a
48-trit double is not designed until a workload demands it — trifloat24 §5.3); code requiring true
binary64 semantics must use softfloat i64 routines. `long double` is not provided.

## Argument passing and return

RV32I ilp32 rules, verbatim, with the identity register map:

- **Integer/pointer arguments** go in `a0`–`a7`, one word each, left to right; exhausted → stack.
- **Two-word scalars** (`long long`, two-word structs) occupy an **aligned register pair**
  (`a0/a1`, `a2/a3`, `a4/a5`, `a6/a7` — skip a register if needed); split pairs at the
  register/stack boundary follow the ilp32 rule (low word in the last register, high word on the
  stack).
- **Aggregates ≤ 2 words** are passed in registers as above; **larger aggregates by reference**
  (caller allocates a copy, passes the pointer).
- **`float` (trifloat24)** is passed and returned in the ordinary argument registers — there is no
  separate float register file, hence no hard-float/soft-float ABI split, ever.
- **Return values**: one word in `a0`; two words in `a0`/`a1`; larger via an sret pointer passed
  as an implicit first argument in `a0`.
- **Variadic arguments** follow the ilp32 rule: named-argument passing is unchanged; anonymous
  two-word values take aligned pairs; on-stack variadics are word-aligned.

**Two-register return is one instruction.** `MV2.T a0, a1, lo, hi` moves both return words at
once, and `SWAP.T`/`LI2.T` cover the common shuffles — REBEL-6 formats were chosen with the
two-word return in mind, where RV32I needs two moves.

## Stack

- `sp` = `X2`, grows **downward**; `[sp]` is the lowest live address.
- **Alignment: one word (4 trytes), at all times, including at every call boundary.** This
  diverges from RISC-V's 16-byte rule deliberately: the 16-byte alignment exists for vector
  spills and cache-line economics REBEL-6's MCU profile does not have; a word is the largest
  natural alignment in the type model. If a future V extension needs more, its ABI addendum will
  raise the rule for code that opts in.
- Frame pointer `s0`/`X8` is **optional** (`-fomit-frame-pointer` default); when used, it points
  at the top of the fixed frame, RISC-V style.
- No red zone: nothing below `sp` is preserved across any trap
  ([platform](rebel6-platform.md#traps)).
- The argument-overflow area sits at the caller's `sp` upward, word-aligned slots in argument
  order.

## Program image and runtime contract (crt0)

Sections and placement follow the [memory map](rebel6-isa.md#memory-map): `.text` in I-ROM
(instructions only — normative), `.rodata` + the `.data` load image in D-ROM, `.data`/`.bss`/heap/
stack in D-RAM with `.sdata`/`.sbss` straddling address zero inside the ±265,720-tryte window.

The linker provides, and crt0 consumes, exactly these symbols:

| Symbol | Meaning |
|--------|---------|
| `_start` | entry point (I-space address; reset vector target) |
| `__data_load_start`, `__data_load_end` | `.data` initialiser image in D-ROM |
| `__data_start`, `__data_end` | `.data` run addresses in D-RAM |
| `__bss_start`, `__bss_end` | zero-fill region |
| `__heap_start`, `__heap_end` | heap bounds (`sbrk` arena) |
| `__stack_top` | initial `sp` (top of populated D-RAM, word-aligned) |

crt0 order: set `sp = __stack_top` → copy `[__data_load_start, __data_load_end)` to
`__data_start` → zero `[__bss_start, __bss_end)` → call `main` → pass the return value to the
`exit` semihosting call. **Program end is the `exit` call, never a return-address sentinel**: the
simulator halts on it and reports `a0` as the exit status.

## Semihosting

Over `ECALL.T` from M-mode (bare metal): call number in **`a7`** (`X17`), arguments in
**`a0`–`a2`**, result in `a0` (negative = −errno). Numbers follow the RISC-V Linux syscall ABI so
picolibc/newlib stubs port unchanged:

| Call | `a7` | Arguments | Returns |
|------|------|-----------|---------|
| `openat` | 56 | dirfd (`AT_FDCWD` = −100 only), path (NUL-terminated, one byte/tryte), flags (Linux values), mode | fd (≥3) |
| `close` | 57 | fd | 0 |
| `lseek` | 62 | fd, offset, whence (0 SET / 1 CUR / 2 END) | new offset |
| `read` | 63 | fd, buffer (D-space address), count (trytes) | trytes read |
| `write` | 64 | fd, buffer, count | trytes written |
| `exit` | 93 | status | does not return — halts |
| `brk` | 214 | new break (0 queries) | break after the call (Linux semantics: unchanged on failure) |

File descriptors: 0/1/2 are the host console; `openat` hands out guest fds from 3 and `read`/
`write` accept them (Doom plan T1-M0 extension — `openat` takes a fourth argument in `a3`,
the only semihosting call that does). `brk` is real: the break starts at `__heap_start` (the
first address past the static data image) and may grow to the top of data memory minus a
3^13-tryte stack guard.

Anything else returns −38 (`ENOSYS`). The framebuffer, timer and input device are **not**
semihosted — they are [MMIO](rebel6-platform.md#mmio); a syscall per pixel is not viable.

## Linking model

Static only, ELF32-alike relocatable objects, seven relocation types, no relaxation — normative in
[Linking](rebel6-isa.md#linking). ABI additions: `.text` contains no data (restated as an ABI
rule); code addresses stored in data are `R_REBEL6_ABS24_CODE` regardless of section; the zero
window is reached register-free (`LW.T rd, X0, imm12`) with no relocation beyond `DISP12`.

## Open items

1. **Caller/callee split for the extended pool** — with 40–670 spare registers the RV32I split is
   not obviously optimal; measure spill counts against inline depth before promoting any `eN`
   registers to callee-saved.
2. **Link-time global register allocation** — static linking + whole-program visibility + the
   extended pool invite assigning hot globals to registers at link time; needs an annotation
   convention.
3. **Hard-float naming** — if an FPU materialises, the calling convention does not change (floats
   already ride integer registers); only performance guarantees would be profiled.
4. **TLS** — `tp`/`X4` is reserved; no model is defined until an OS needs one.
