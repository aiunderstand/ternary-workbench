# REBEL-6 Platform Specification

Harts, privilege, traps, interrupts, streaming registers, MMIO and peripherals — everything a
REBEL-6 system-on-chip provides beyond the [base ISA](rebel6-isa.md). Extension instruction
encodings referenced here (Zicsr, A) are specified in [rebel6-extensions.md](rebel6-extensions.md).

**Design principle.** All architectural system state lives **inside the register file**, in the
negative index range of each hart's window. There is no separate CSR file: ternary code reaches trap
state with ordinary register instructions (`MV.T`, `BCGS.T`), and the optional
[Zicsr shim](#zicsr-shim-requires-base-binary) lets RV32I binaries reach the *same* registers
through standard CSR instructions. One physical state, two access idioms, no shadow copies, no
synchronisation. The only architectural state outside the register file is the **PC** (readable via
`AIPC.T rd, 0`) and, when the A extension is present, the LR/SC reservation.

## Harts and variants

The 729-register file partitions evenly across 1, 3, 9 or 27 harts — 729 = 3⁶, so every split is
exact. Register count per hart is ABI-visible, so the partition cannot be left to the implementer;
it is named, as RISC-V names ilp32/lp64:

| Variant | Harts | Regs/hart | Hart-local window | Base ABI | Privilege profile | Trap layout |
|---------|-------|-----------|-------------------|----------|-------------------|-------------|
| `r6-single` | 1 | 729 | `X-364 … X+364` | ilp32 | M+S+U | standard |
| `r6-mp3` | 3 | 243 | `X-121 … X+121` | ilp32 | M+S+U | standard |
| `r6-mp9` | 9 | 81 | `X-40 … X+40` | ilp32 (or M+U profile) | M+S+U | standard |
| `r6-mp27` | 27 | 27 | `X-13 … X+13` | **ilp32e (RV32E)** | M+U | **compact** |

**Hart-local numbering.** Code names registers by their hart-local index; the same binary runs on
every hart. `X0` is hardwired zero in every window. The mapping onto the physical file is
implementation-defined; the reference mapping is trit-sliced — the physical 6-trit index is
`hartid ⋅ W + r` where `W` is the window size, i.e. the upper trits of the physical index are the
hart id and the lower trits are the hart-local register:

```
r6-mp9:   physical[6 trits] = hartid[2 trits] ⋅ 81 + r[4 trits]     (hartid −4 … +4)
r6-mp27:  physical[6 trits] = hartid[3 trits] ⋅ 27 + r[3 trits]     (hartid −13 … +13)
```

**The mp27 design point.** 27 registers cannot hold the 32-register ilp32 set — but they hold
RV32E's 16 with room for system state. A 27-hart REBEL-6 part is therefore 27 independent
RV32E-class harts on one register file, a design point binary register files cannot reach cheaply.
Two of RV32E's registers spill into the negative range (`x14 → X-12`, `x15 → X-13` in the compact
layout below); the ABI document pins the full role table.

**Fixed across all variants:** ilp32 (or ilp32e) roles occupy the low positive indices, and the
negative range is reserved for system and streaming state, laid out as follows.

## Register map — negative range (standard layout)

Fixed hart-local indices, identical on `r6-single`, `r6-mp3` and `r6-mp9`. A variant whose window
covers an index provides it; an index outside the window does not exist, and the features needing it
degrade accordingly (this is how the privilege profile falls out of the geometry).

| Index | Name | Access | Contents |
|-------|------|--------|----------|
| `X-1` | `mtvec` | M | Machine trap vector: handler address (I-space) |
| `X-2` | `mepc` | M | Machine exception PC |
| `X-3` | `mcause` | M | Trap cause — balanced: interrupts `+`, exceptions `−`, 0 = none |
| `X-4` | `mstatus` | M | Status: mode, IE/PIE/PP fields per level, vectoring — see below |
| `X-5` | `mscratch` | M | Machine scratch |
| `X-6` | `mie` | M | Interrupt-enable trits, indexed by cause magnitude |
| `X-7` | `mip` | M | Interrupt-pending trits; trits driven by hardware interrupt lines are read-only views of those lines |
| `X-8` | `mhartid` | M, read-only | Hart id |
| `X-9` | `sstatus` | S | **View** of `mstatus` restricted to the S-level fields {`SIE`, `SPIE`, `SPP`, `SVECT`} |
| `X-10` | `mcycle` | M/S/U read | Cycle counter — streaming-class, hardware-written |
| `X-11` | `minstret` | M/S/U read | Instructions-retired counter — streaming-class |
| `X-12 … X-14` | `stream0 … stream2` | M/S/U read | Streaming registers (minimum 3; see [Streaming registers](#streaming-registers)) |
| `X-15` | `stvec` | S | Supervisor trap vector |
| `X-16` | `sepc` | S | Supervisor exception PC |
| `X-17` | `scause` | S | Supervisor trap cause (balanced, as `mcause`) |
| `X-18` | `sscratch` | S | Supervisor scratch |
| `X-19` | `medeleg` | M | Exception delegation to S: trit at magnitude *k* delegates cause −*k* |
| `X-20` | `mideleg` | M | Interrupt delegation to S: trit at magnitude *k* delegates cause +*k* |
| `X-21` | `sie` | S | **View** of `mie` masked by `mideleg` |
| `X-22` | `sip` | S | **View** of `mip` masked by `mideleg` |
| `X-23` … | platform | variant-defined | Additional streaming registers, platform state |

`sstatus`, `sie` and `sip` are **views, not copies** — reads return the masked source register,
writes update only the S-visible fields. `sstatus` is a **live view** of exactly
`mstatus.{SIE, SPIE, SPP, SVECT}`: reads return those fields (0 elsewhere), writes update only
them. This mirrors RISC-V's definition of the s-registers as restricted windows onto machine state
and keeps the no-shadow-state property.

Indices from `X-23` down that a variant does **not** define are plain storage with minimum
privilege M — ordinary read/write registers at M, −11 below M, with no hardware behaviour attached.

**Compact layout (`r6-mp27` only).** The 13 negative indices hold a merged, M-only set:

| Index | Name | Notes |
|-------|------|-------|
| `X-1 … X-5` | `mtvec`, `mepc`, `mcause`, `mstatus`, `mscratch` | as standard |
| `X-6` | `stream0` | single streaming register (mp27 minimum is 1) |
| `X-7 … X-11` | general purpose | the ilp32e ABI's 5 spare registers |
| `X-12`, `X-13` | `x14`, `x15` | RV32E argument registers a4, a5 |

On the compact layout the `mie`/`mip` trit vectors and the hart id are **fields inside `mstatus`**
(trits 12–23; layout in the ABI document). There is no S-mode and no delegation.

**Privilege check.** Negative-index access is checked at decode: each architectural index has a
minimum privilege (M for the M-set and delegation registers, S for the S-set, any for
counter/streaming reads). A violating access raises exception −11. Positive indices are never
checked — the check is off the hot path for all ordinary computation. Writes to any streaming-class
register (counters included) raise −11 at every privilege level, and so do writes to read-only
system registers (`mhartid`).

**Reset.** All system registers reset to 0, with one exception: `mstatus.MODE` resets to `+` — a
hart comes out of reset in M.

## Privilege

Three levels, encoded on **one trit** — the encoding native to the machine:

| Trit | Level | Purpose |
|------|-------|---------|
| `+` | **M** — Machine | Full access; trap handling; MVP firmware |
| `0` | **S** — Supervisor | OS kernels; receives delegated traps |
| `−` | **U** — User | Applications; no system-register access |

A variant implements a **profile**: `M`, `M+U`, or `M+S+U`. The MVP MCU profile is `M` (or `M+U`
where task isolation is wanted); `S` exists for the OS ambition and costs nothing to leave
unimplemented — its registers simply lie outside the implemented set and access to them traps.
The current level is readable as `mstatus.MODE` (below); it is not writable directly — it changes
only on trap entry and `TRET.T`.

### mstatus fields

24 trits; native layout (trit 0 = LST). Reserved trits read 0; writes to them are ignored.

| Trit | Field | Meaning |
|------|-------|---------|
| 0 | `MODE` | current privilege (read-only: `−`/`0`/`+` = U/S/M) |
| 1 | `MIE` | machine interrupt enable (0 = off, `+` = on) |
| 2 | `MPIE` | previous `MIE`, saved on trap entry to M |
| 3 | `MPP` | previous privilege on trap entry to M (one trit — U/S/M) |
| 4 | `SIE` | supervisor interrupt enable |
| 5 | `SPIE` | previous `SIE` |
| 6 | `SPP` | previous privilege on trap entry to S |
| 7 | `MVECT` | M vectoring: 0 = direct, `+` = vectored; `−` behaves as direct (0) |
| 8 | `SVECT` | S vectoring: 0 = direct, `+` = vectored; `−` behaves as direct (0) |
| 9–11 | — | reserved |
| 12–23 | — | reserved (standard layout); `mie`/`mip`/`hartid` fields (compact layout) |

The one-trit `MPP`/`SPP` fields are the ternary dividend of the three-level design: RISC-V spends
two bits on `MPP` and wastes an encoding; one trit holds exactly U/S/M.

## Traps

A trap is a transfer of control to a handler at a privilege level, with cause, return address and
prior status captured **in registers**. Trap state is ordinary register state; a ternary handler
begins work with its cause already in `X-3` — no CSR read instruction, no decode of a cause
register format.

### Balanced cause codes

The sign of the cause is the interrupt/exception discriminant, so a handler's very first
instruction can be a three-way dispatch:

```
handler:
    BCGS.T  X-3, X0, irq_path, exc_path    # cause > 0 → interrupt; < 0 → exception; = 0 impossible
```

| Cause | Meaning | | Cause | Meaning |
|-------|---------|-|-------|---------|
| `+1` | software interrupt (`MSIP`) | | `−1` | illegal instruction |
| `+2` | timer interrupt (`MTIMECMP`) | | `−2` | instruction access fault |
| `+3` | external interrupt (TIC) | | `−3` | misaligned load (trapping implementations) |
| `+4 … +40` | platform-direct interrupts (optional) | | `−4` | misaligned store |
| | | | `−5` | load access fault |
| | | | `−6` | store access fault |
| | | | `−7` | environment call from U (`ECALL.T`) |
| | | | `−8` | environment call from S |
| | | | `−9` | environment call from M |
| | | | `−10` | breakpoint (`EBREAK.T`) |
| | | | `−11` | privileged-register access violation (incl. streaming-register write) |
| | | | `−12` | illegal CSR access (Zicsr) |

Cause 0 means "no trap" and is the reset value of `mcause`/`scause`.

**Semihosting precedence.** `ECALL.T` from M with `a7` ∈ {56, 57, 62, 63, 64, 93, 214} is handled by the
execution environment without trap entry — no cause is recorded, no handler runs. Every other
`ECALL.T` — any other `a7` value, or any call from S or U — takes the environment-call trap for
its level (−7/−8/−9).

### Trap entry

A trap targets M unless the corresponding `medeleg`/`mideleg` trit delegates it to S (and the
current level is not M — traps never descend). Entry to level *L* (hardware, atomic; may be
microsequenced over a few cycles):

1. `Lepc ← PC` (the faulting/interrupted instruction for exceptions; the not-yet-executed
   instruction for interrupts)
2. `Lcause ← cause`
3. `Lstatus.LPIE ← Lstatus.LIE`; `Lstatus.LIE ← 0`; `Lstatus.LPP ← MODE`
4. `MODE ← L`
5. `PC ← Ltvec` (direct) or `PC ← Ltvec + cause` (vectored)

**Vectored mode is two-sided**: causes are balanced, so interrupt slots sit *above* the vector base
and exception slots *below* it — one table, centred on `tvec`, reached with no masking or scaling.
Each slot is one instruction, conventionally `JAL.T X0, handler_k`.

### TRET.T

One return instruction serves every level — it restores from the bank of the **current** level
(RISC-V's `MRET`/`SRET` collapsed, selected by `MODE`):

- From M: `PC ← mepc`; `MIE ← MPIE`; `MODE ← MPP`; `MPIE ← +`; `MPP ← −` (U)
- From S: `PC ← sepc`; `SIE ← SPIE`; `MODE ← SPP`; `SPIE ← +`; `SPP ← −`
- From U: illegal instruction (−1)

### Handler conventions

- `mscratch`/`sscratch` hold the handler stack or context pointer, swapped with a working register
  at entry and exit — the standard RISC-V idiom, verbatim.
- Single instructions are atomic with respect to interrupts on the same hart. A full write of
  `mstatus` (`MV.T X-4, t0`) is therefore atomic; read-modify-write *sequences* on `mstatus` are
  interruptible, and a handler must leave the interrupted context's `mstatus` unchanged at `TRET.T`
  beyond the architected `PIE`/`PP` updates — this rule is what makes open-coded RMW safe.
- Nested traps at the same level follow RISC-V practice: `LIE` is 0 on entry, so same-level
  interrupts are deferred; a same-level *exception* inside a handler overwrites `Lepc`/`Lcause` and
  is fatal unless the handler has already saved them.

## Zicsr shim (requires Base Binary)

The [Zicsr extension](rebel6-extensions.md#zicsr--csr-access-requires-base-binary) gives RV32I
binaries their native idiom — `csrr`, `csrw`, `csrrs`, `csrrc` — decoding CSR **addresses** onto the
**same registers** specified above. The shim is a decode table plus, for a handful of CSRs, a
**layout view** that presents the RISC-V bit layout over the native trit layout. It holds no state.

| CSR | Number | Register | View |
|-----|--------|----------|------|
| `mstatus` | 0x300 | `X-4` | field map: MIE↔bit 3, MPIE↔bit 7, MPP↔bits 12:11, SIE↔bit 1, SPIE↔bit 5, SPP↔bit 8 |
| `medeleg` | 0x302 | `X-19` | bit *k* ↔ trit at the mapped exception magnitude |
| `mideleg` | 0x303 | `X-20` | bit *k* ↔ trit at the mapped interrupt magnitude |
| `mie` | 0x304 | `X-6` | MSIE↔bit 3↔trit 1, MTIE↔bit 7↔trit 2, MEIE↔bit 11↔trit 3 |
| `mtvec` | 0x305 | `X-1` | address view (see below); mode bits 1:0 ↔ `mstatus.MVECT` |
| `mscratch` | 0x340 | `X-5` | value — no view |
| `mepc` | 0x341 | `X-2` | address view |
| `mcause` | 0x342 | `X-3` | cause view: sign ↔ bit 31; magnitude ↔ standard RISC-V code (table below) |
| `mip` | 0x344 | `X-7` | as `mie` |
| `mhartid` | 0xF14 | `X-8` | value — read-only |
| `sstatus` | 0x100 | `X-9` (view of `X-4`) | S-restricted field map |
| `sie` / `sip` | 0x104 / 0x144 | `X-21` / `X-22` | delegation-masked, field map as `mie` |
| `stvec` | 0x105 | `X-15` | address view; mode ↔ `SVECT` |
| `sscratch` | 0x140 | `X-18` | value |
| `sepc` | 0x141 | `X-16` | address view |
| `scause` | 0x142 | `X-17` | cause view |
| `cycle`/`cycleh` | 0xC00/0xC80 | `X-10` | low/high 32 bits of the counter value |
| `instret`/`instreth` | 0xC02/0xC82 | `X-11` | low/high 32 bits |
| `time` | 0xC01 | — | implementation choice: alias CLINT `MTIME` or trap-and-emulate |
| `satp` | 0x180 | — | **not implemented** — illegal CSR (−12); reserved for a future MMU |

Any CSR number not in the table raises −12, which permits trap-and-emulate.

**Address view.** Binary code holds code addresses in **bytes**; the PC counts **instructions**.
Address-holding CSRs therefore scale on the shim boundary: reads return `value × 4`, writes store
`value ÷ 4` — the same ÷4 rule the L-type translator applies to displacements, extended to trap
state. The canonical kernel idiom `csrr t0, mepc; addi t0, t0, 4; csrw mepc, t0` (skip the trapped
instruction) works unmodified: it adds one instruction slot.

**Cause view.** Balanced causes map to the RISC-V numbering both ways:

| Native | RISC-V | | Native | RISC-V |
|--------|--------|-|--------|--------|
| `+1` | interrupt 3 (MSI) | | `−7` | exception 8 (ecall-U) |
| `+2` | interrupt 7 (MTI) | | `−8` | exception 9 (ecall-S) |
| `+3` | interrupt 11 (MEI) | | `−9` | exception 11 (ecall-M) |
| `−1` | exception 2 (illegal) | | `−10` | exception 3 (breakpoint) |
| `−2` | exception 1 (instr access) | | `−3`/`−4` | exceptions 4 / 6 (misaligned) |
| `−5`/`−6` | exceptions 5 / 7 (access) | | `−11`/`−12` | exception 2 (illegal instruction) |

**Atomicity.** `csrrs`/`csrrc` are single instructions and therefore atomic on their hart, exactly
the property RISC-V kernels rely on for `mstatus.MIE` twiddling. Ternary code obtains the same
atomicity by writing whole registers (`MV.T` is one instruction) under the handler convention above.

**What this buys.** A Zephyr or NOMMU-Linux RISC-V port executes its CSR accesses unmodified; the
port reduces to board work — memory map, drivers for the [CLINT-analog](#clint-analog) and
[TIC](#tic--ternary-interrupt-controller) below, UART. The binary kernel itself must be built
`rv32i` (see the growth rule in [Base Binary](rebel6-isa.md#rebel-6-base-binary-optional-rv32i-compatibility-layer)):
the binary-compatibility Linux path additionally needs RV32M/A binary encodings, which are **not
planned** — a Linux bring-up on current REBEL-6 is either a native ternary port or deferred.

## Interrupts

Two delivery rules are normative across all sources. **Order:** when multiple interrupts are
pending and deliverable for a hart, they are taken in the order software (`+1`), then timer
(`+2`), then external (`+3`). **Privilege gating:** an interrupt targeting M is taken when the
hart is executing below M regardless of the global `MIE`; an interrupt delegated to S is gated
on `SIE`.

### CLINT-analog

One hart-local interrupt block, register layout shaped after the RISC-V CLINT so existing drivers
port by address swap. All registers are 24-trit words unless noted; offsets in trytes from the
device base ([MMIO map](#mmio)). `H` = hart count.

| Offset | Register | Access | Function |
|--------|----------|--------|----------|
| +0 | `MTIME` | RO | free-running platform counter, one word (wraps modulo 3²⁴; at 10 MHz ≈ 327 days) |
| +4 + 4·h | `MTIMECMP[h]` | RW | timer compare, hart *h*: pending `+2` on hart *h* while `MTIME − MTIMECMP[h] ≥ 0` (wrap-aware signed compare); resets to the maximum word value, so the timer is quiet at boot |
| +400 + h | `MSIP[h]` | RW, tryte | write `+` → raise software interrupt `+1` on hart *h*; write `0` → clear |

### TIC — ternary interrupt controller

External interrupt fan-in, PLIC-subset register shapes. Up to 24 sources (one trit per source in a
word; source ids 1…24), per-hart enable/claim contexts. A source's pending trit is `+` when
asserted, `0` when idle; `−` is reserved.

| Offset | Register | Access | Function |
|--------|----------|--------|----------|
| +s (s = 1…24) | `PRIORITY[s]` | RW, tryte | 0 = never delivered; higher value = higher priority |
| +28 | `PENDING` | RO | word: pending trit per source |
| +32 + 4·c | `ENABLE[c]` | RW | word: enable trit per source, hart context *c* |
| +200 + c | `THRESHOLD[c]` | RW, tryte | sources with priority ≤ threshold are masked for context *c* |
| +300 + 4·c | `CLAIM[c]` | RW | read: claim — returns highest-priority pending·enabled source id (0 if none), clears its pending; write id: completion |

While any source is claimable for context *c*, external-interrupt `+3` pends on that context's hart.
The claim/complete flow is the PLIC's, verbatim.

**Default source map:** 1 GPIO0, 2 UART0, 3 TPWM0, 4 SPI0, 5 I2C0, 6 TADC0; 7…24 platform.

### WFI.T

Stalls the hart until an interrupt becomes both pending and individually enabled
(`mip ∧ mie ≠ 0`), **regardless of the global `MIE`** — the RISC-V WFI contract, which enables the
"disable globally, WFI, then handle" idle idiom. Resumes at PC+1; if the global enable and level
permit, the trap is taken first. Implementations may treat `WFI.T` as a NOP-with-hint; low-power
sleep is the intent. On the simulator profile the **halting reading is normative**: `WFI.T`
suspends execution and resumes when any enabled interrupt is pending (`mip ∧ mie ≠ 0`),
regardless of the global enable.

## Streaming registers

Per-hart registers **written by hardware without an instruction** — sensor samples, captured
timestamps, performance counts appear in them continuously. They are the zero-instruction sensor
path: a control loop reads its newest ADC sample with no load, no MMIO transaction, no interrupt.

Architectural indices: `X-12 … X-14` standard (minimum 3), `X-6` compact (minimum 1); platforms may
define more from `X-23` down. `mcycle`/`minstret` are streaming-class (hardware-written, read-only)
at fixed indices `X-10`/`X-11`.

Semantics — normative:

- **Reads are volatile.** Two reads of the same streaming register may differ; compilers must not
  common-subexpression, cache or reorder them across `FENCE.T`. (Toolchain: a register class the
  allocator never allocates, spills, or saves across calls.)
- **Writes fault** — exception −11 at every privilege level. Silent ignore is not permitted: a
  write to a streaming register is always a software bug, and this is where it surfaces.
- **Excluded from context switch.** Their values belong to the hart and instant, not the task;
  save/restore code must skip them.
- **Routing** is per-device: peripherals with a `ROUTE` register ([TPWM](#tpwm0--timercounter-with-three-level-pwm),
  [tADC](#sketched-devices)) name the stream slot they feed. One slot, one producer; configuring two
  producers onto one slot is implementation-defined.
- **Simulators must provide scripted replay** — stream values supplied from a file with defined
  timing. Streaming registers are non-deterministic by construction; without replay, nothing that
  reads them is differentially testable. This is a conformance requirement on simulators, not
  hardware.

**Replay-script format — normative.** The replay script is a line-based text file. `#` begins a
comment (to end of line); blank lines are ignored. Every other line is one entry,
`<stream-index> <at-count> <value>`, meaning: stream *stream-index* returns *value* for every
read at or after retired-instruction count *at-count* — the same count a read of `minstret` at
that point returns — until that stream's next entry takes effect. When entries for the same
stream share an at-count, the later entry wins. Values must fit the balanced 24-trit range. Only
the stream registers (`X-12 … X-14`) are scriptable; the hardware-written streaming counters
(`mcycle`, `minstret`) are not. Before a stream's first entry — or with no script loaded — reads
of that stream return 0. Replay is deterministic: the same program with the same script must
produce identical execution — final `minstret`, output, and exit status.

## Memory consistency

- **Single hart:** program order, full stop. The MVP in-order MCU satisfies everything below
  trivially.
- **Multiple harts:** RVWMO-compatible. Ordinary D-RAM accesses may be observed out of order across
  harts; `FENCE.T` is a full fence (all earlier loads and stores globally visible before any later
  ones); the A extension's `aq`/`rl` trits give acquire/release on atomics. Software written to
  RVWMO (every RISC-V RTOS) is correct here unchanged.
- **MMIO:** accesses to the `−` region are strongly ordered among themselves per hart, never
  merged, never elided, never speculated. `volatile` maps here with no extra fences for
  single-device driver code.
- **No FENCE.I.** I-space is ROM: there is no self-modifying code, no instruction-cache coherence
  problem, and nothing for a fetch fence to order. A future writable-I-space extension would bring
  its own synchronisation instruction.

## MMIO

The `−` region of the data space (MST trit `−`; addresses −141,214,768,240 … −47,071,589,414 —
see [Memory map](rebel6-isa.md#memory-map)) holds peripheral registers.

**Access semantics (normative):** reads and writes are performed exactly as issued — no
reordering, elision, merging or speculation; tryte-granular access (`LT.T`/`ST.T`) is permitted and
is the natural width for control registers; **misaligned MMIO access is an error** (misaligned
access, cause −3/−4), not implementation-defined; unpopulated MMIO addresses fault on access
(cause −5/−6); **stores to read-only device registers raise store access fault (−6)**.

**Device windows.** Each device occupies one or more **3⁸ = 6561-tryte** slots allocated from the
least-negative end of the region. Slot *n* spans addresses
`MMIO_TOP − (n+1)·6561 + 1 … MMIO_TOP − n·6561` with `MMIO_TOP = −47,071,589,414`; a device's
registers sit at ascending tryte offsets from its slot base (the slot's lowest address). Example:
slot 0 (CLINT) spans −47,071,595,974 … −47,071,589,414.

| Slot | Device | | Slot | Device |
|------|--------|-|------|--------|
| 0 | CLINT-analog | | 6 | I2C0 |
| 1 | TIC | | 7 | TADC0 |
| 2 | UART0 | | 8 | TDAC0 |
| 3 | GPIO0 | | 9 | SIMCON (simulator console + input) |
| 4 | TPWM0 | | 10 | SIMFB control |
| 5 | SPI0 | | 11–15 | reserved |
| | | | 16–25 | SIMFB pixel array (320×200 trytes) |
| | | | 26 | SIMKEY (simulator keyboard FIFO) |

An implementation populates the devices it has; the map fixes addresses so drivers and the
simulator agree. Sparse population costs nothing — see
[Memory map](rebel6-isa.md#memory-map).

The [external Debug Module](rebel6-debug.md#debug-module) is **not** memory-mapped: slots 11–15
remain reserved, and no hart-visible state is added by debug support.

## Peripherals

Detailed register maps for the blocks everything else depends on; sketches (register list, one line
each) for the rest, to be refined when the simulator implements them. All offsets in trytes from
the device slot base. Control-register trits use `0` = off, `+` = on unless stated; `−` values in
control fields are reserved.

### UART0

Binary 8N1 UART for console and host compatibility. A byte travels in the low portion of a tryte
(tryte range ±364 covers 0…255).

| Offset | Register | Access | Function |
|--------|----------|--------|----------|
| +0 | `TXD` | W, tryte | write byte value 0…255 to transmit; reads as 0 |
| +1 | `RXD` | RO, tryte | oldest received byte; undefined when `STAT.RXAVAIL` = 0 |
| +2 | `STAT` | RO, tryte | t0 `RXAVAIL`, t1 `TXBUSY`, t2 `RXOVERRUN`, t3 `FRAMEERR` |
| +3 | `CTRL` | RW, tryte | t0 `EN`, t1 `RXIE` (interrupt on RXAVAIL), t2 `TXIE` (interrupt on TX idle) |
| +4 | `BAUDDIV` | RW, word | clock divisor: baud = f_platform / BAUDDIV |

Interrupts via TIC source 2.

### GPIO0

Up to 24 pins, **one trit per pin** — the ternary pin model makes the direction register
disappear: driving `0` *is* releasing the pin.

| Offset | Register | Access | Function |
|--------|----------|--------|----------|
| +0 | `IN` | RO, word | per pin: `−` = driven low externally, `0` = floating, `+` = driven high |
| +4 | `OUT` | RW, word | per pin: `−` = drive low, `0` = **high-impedance (input mode)**, `+` = drive high |
| +8 | `IRQR` | RW, word | rising-edge interrupt enable per pin (`+` = enabled) |
| +12 | `IRQF` | RW, word | falling-edge interrupt enable per pin |
| +16 | `IRQZ` | RW, word | **to-Z edge** enable — interrupt when a pin *enters* the floating state; a third edge type binary GPIO cannot express |
| +20 | `PEND` | RW, word | pending per pin; write `+` at a position to clear it |

Any pending·enabled pin raises TIC source 1. Reading `IN` on a pin the hart itself drives returns
the driven value.

### TPWM0 — timer/counter with three-level PWM

| Offset | Register | Access | Function |
|--------|----------|--------|----------|
| +0 | `CTRL` | RW, tryte | t0 `EN`, t1 `WRAPIE` (interrupt on wrap), t2 `MODE`: `0` = timer, `+` = tPWM, `−` = capture |
| +1 | `PRESCALE` | RW, tryte | count every 1 + PRESCALE platform cycles (PRESCALE ≥ 0) |
| +4 | `PERIOD` | RW, word | counter wraps to 0 after PERIOD − 1 |
| +8 | `COUNT` | RO, word | current count |
| +12 | `CMPP` | RW, word | tPWM upper threshold |
| +16 | `CMPN` | RW, word | tPWM lower threshold (require 0 ≤ CMPP ≤ CMPN ≤ PERIOD) |
| +20 | `CAPT` | RO, word | capture: `COUNT` latched on the routed GPIO edge |
| +24 | `ROUTE` | RW, tryte | streaming slot fed with `COUNT` (timer/tPWM) or `CAPT` (capture); 0 = none |

**tPWM output:** `+` while `COUNT < CMPP`; `0` while `CMPP ≤ COUNT < CMPN`; `−` while
`COUNT ≥ CMPN`. One channel generates the three-level waveform of a T-type (three-level) inverter
leg directly — the duty of each level is set by the two thresholds. Binary controllers need two
coordinated PWM channels plus dead-time logic for the same waveform.

Wrap raises TIC source 3.

### Sketched devices

Register lists only — offsets reserved, semantics one line each, detail to follow with the
simulator implementation.

**SPI0** (binary protocol, commodity peripherals): `CTRL` (+0: EN, CPOL, CPHA, CS auto), `STAT`
(+1: BUSY, RXAVAIL), `TXD` (+2), `RXD` (+3), `CLKDIV` (+4, word), `CS` (+8: chip-select lines).
TIC source 4.

**I2C0**: `CTRL` (+0: EN, START, STOP, ACK), `STAT` (+1: BUSY, NACK, ARB), `DATA` (+2), `ADDR`
(+3), `CLKDIV` (+4, word). TIC source 5.

**TADC0** — ternary ADC: `CTRL` (+0: EN, channel select, single/continuous), `STAT` (+1: DONE),
`DATA` (+4, word RO: **balanced sample** — a bipolar input maps linearly to ±(3ᴿ−1)/2 around the
mid-rail; no offset binary, sign is native), `RES` (+8: resolution R in trits, implementation
maximum), `ROUTE` (+9: streaming slot for continuous mode — the zero-instruction sensor path).
TIC source 6 on DONE.

**TDAC0** — ternary DAC: `CTRL` (+0), `DATA` (+4: balanced value → multilevel output), `RATE`
(+8, word: sample clock divisor).

### Simulator profile

The minimal device set a REBEL-6 simulator implements for toolchain and benchmark work — a strict
subset of this map: **CLINT** (`MTIME` at slot 0), **SIMCON** (slot 9: +0 `CONOUT` W tryte, byte
0…255 to host console; +1 `CONIN` RO — returns −1 when no input is available, `STAT`'s AVAIL trit
reflects availability; +2 `STAT` t0 AVAIL), **SIMFB** (slot 10 control: +0 `EN`, +1 `MODE` — `0`
direct 729-value, `+` palette; +2 `FLIP` W; palette at +100…+867; slots 16–25: 320×200 pixel
trytes, row-major), and **SIMKEY** (slot 26: +0 `EVENT` RO, +1 `STAT` RO).

SIMFB rulings (T1-M2, normative — this palette/FLIP model is what the T3-H3 VGA/LCD scanout
implements): palette entries are **3 trytes each — R, G, B** — 256 entries at control +100…+867,
entry *i* at +100 + 3·*i*. Each component tryte maps linearly −364…+364 → intensity 0…255, the
same map direct mode applies to the pixel tryte (gray). In palette mode the pixel tryte is the
entry index 0…255; out-of-range indices clamp. `FLIP` (+2) is the frame-commit register, final
semantics: writing `+` presents the current pixel array through the current palette as one frame
(host-side: live window and/or numbered PPM dump, plus the retired-instructions-since-last-flip
log); `0`/`−` writes are accepted without commit; reads return 0 (the `CONOUT` rule). The frame
dump is independent of the enable register; the pixel array occupies slots 16–25 ascending from
the window's lowest address, row-major 320×200 (64,000 trytes), and the trailing trytes of the
window are unpopulated (faulting per the MMIO rule).

SIMKEY rulings (T1-M2, normative — the FPGA button/PS-2 input path presents the same event
format): a keyboard-event FIFO exploiting the balanced tryte. +0 `EVENT` (RO tryte) pops the
oldest event: **+code = key press, −code = key release, 0 = FIFO empty** — the sign is the
make/break flag, so one tryte read drains one event with no side registers. +1 `STAT` (RO
tryte): t0 `AVAIL` (FIFO non-empty), t1 `OVERRUN` — sticky, set when an event is dropped on a
full FIFO (depth ≥ 64, the newest event drops), cleared by reading `STAT`. All writes into the
window fault. Keycodes 1…255 follow the classic DOS/Doom convention: ASCII for printable keys
(letters lowercase), Tab 9, Enter 13, Escape 27, Backspace 127, and the extended 0x80+scan set —
Ctrl 157, left/up/right/down arrows 172/173/174/175, Shift 182, Alt 184, F1…F12 187…198, Pause
255. Console escape codes were rejected as the input path: terminals cannot report key-release,
and held-key game movement needs make/break.

Semihosted `write`/`exit`/`sbrk` go through `ECALL.T` per the ABI's semihosting
convention; the framebuffer and timer are MMIO because a syscall per pixel is not viable.

## OS enablement

What each software target needs, and its status on this specification:

| Target | Needs | Status |
|--------|-------|--------|
| Bare-metal C (picolibc) | ECALL.T semihosting, CLINT timer, SIMCON | **complete** |
| Ternary RTOS / Zephyr native port | M(+U), traps, CLINT + TIC, UART, context switch over the register window | **complete** — port is board + arch backend work |
| Zephyr / Linux as stock rv32i binaries | all of the above + Base Binary + L-type + Zicsr shim | **specified**; kernel must be built `rv32i` |
| NOMMU Linux (rv32ima binary) | RV32M/RV32A binary encodings | **not planned** — binary layer stays RV32I; path is a native port |
| MMU Linux | `satp`, ternary page tables | **reserved** — `satp` traps as illegal CSR; page-table design is future work |

A ternary OS — scheduling over 3ⁿ harts, tri-state IPC, balanced time — is out of scope for this
document; the hooks it would build on (register windows, streaming registers, balanced causes) are
the ones specified above.
