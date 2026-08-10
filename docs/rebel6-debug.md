# REBEL-6 External Debug Specification

Run control, breakpoints, and external state access for REBEL-6 harts — everything a debugger
needs and nothing a program can see. Version **REBEL-6 Debug 0.1**. Modeled on the RISC-V External
Debug Support specification 1.0; it diverges only where balanced ternary or REBEL-6's architecture
requires, and every divergence carries its justification inline.

**Design principle.** The debugger lives **outside the architecture**. No instruction, register,
trap cause, extension letter or MMIO slot visible to hart software is added by this document: a
program cannot detect whether a debugger is attached, and the [base ISA](rebel6-isa.md) and
[platform](rebel6-platform.md) documents remain complete descriptions of everything software can
observe. All debug state lives in the **Debug Module (DM)**, reached over a transport (JTAG); the
hart's own register file, cause space and memory map are untouched.

Two architectural facts shape everything below:

- **Software breakpoints cannot exist.** I-space is fetch-only ROM — nothing writes it, nothing
  reads it as data ([memory model](rebel6-isa.md#memory-map),
  [memory consistency](rebel6-platform.md#memory-consistency)). The binary-world technique of
  patching a breakpoint instruction into `.text` is architecturally impossible, not merely
  inconvenient. **All breakpoints are comparators** — the [trigger module](#trigger-module) is
  therefore mandatory, not optional.
- **No program buffer can exist.** For the same reason, the debugger cannot inject instructions
  for the hart to execute. The DM operates on **abstract commands only**
  (`abstractcs.progbufsize = 0`), a configuration the RISC-V debug specification explicitly
  permits. Every hart-state access is a direct DM operation; nothing is bounced through hart
  execution.

## Debug Mode

A hart is either **running** or **halted** (in Debug Mode). Debug Mode is **orthogonal state**,
not a privilege level — the privilege trit's three values are fully assigned
(`+`/`0`/`−` = M/S/U, [platform](rebel6-platform.md#privilege)), and no fourth level is needed:
since there is no program buffer, the hart never executes instructions while halted, so Debug Mode
never needs a privilege encoding of its own. The privilege trit is preserved across halt and
resume.

While halted:

- No instructions are fetched or retire. The PC and all registers hold still.
- Interrupts are **pended, not taken**. They deliver normally on resume, subject to the ordinary
  delivery rules.
- `mcycle` stops counting when `dcsr.stopcount` = 1, and counts when it is 0. `minstret` never
  advances while halted (nothing retires).
- The streaming registers ([platform](rebel6-platform.md#streaming-registers)) continue to be
  hardware-written; a halted hart is not a halted platform. On the simulator profile, replay-script
  entries are indexed by retired-instruction count, which does not advance while halted — replay
  and debug compose without a special rule.
- All hart state is readable and writable by the DM through
  [abstract commands](#abstract-commands).

**Debug entry is out-of-band.** Entering Debug Mode writes **no architectural state**: `mcause`,
`mepc`, `mstatus` and their S-level counterparts are untouched. The balanced cause space
(interrupts `+`, exceptions `−`, [platform](rebel6-platform.md#balanced-cause-codes)) remains
purely architectural — there is no debug cause value, and a handler can never observe a debug
entry. The entry reason is recorded in [`dcsr.cause`](#dcsr), visible only over the DMI.

## Debug register bank

Four registers, addressable **only through the DM** ([regno map](#access-register)). They are not
negative-index registers, not CSRs, and not reachable by any instruction — hart software never
needs them (it cannot execute while halted) and must not be able to see them. Values cross the DMI
in binary; field layouts below are bit layouts, not trit layouts, because these registers exist
only on the binary side of the transport.

### dcsr

| Bits | Field | Meaning |
|------|-------|---------|
| 2:0 | `cause` | why the hart halted: 1 = `EBREAK.T`, 2 = trigger, 3 = `haltreq`, 4 = single step, 5 = `resethaltreq` (read-only) |
| 3 | `step` | single step: on resume, execute one instruction, then re-enter Debug Mode with cause 4 |
| 4 | `ebreakm` | `EBREAK.T` at M enters Debug Mode instead of trapping |
| 5 | `ebreaks` | `EBREAK.T` at S enters Debug Mode instead of trapping |
| 6 | `ebreaku` | `EBREAK.T` at U enters Debug Mode instead of trapping |
| 7 | `stopcount` | `mcycle` stops while halted |
| 9:8 | `prv` | privilege trit at halt, encoded 0/1/2 = U/S/M (read-only; informational — the trit itself is architectural state and is not modified by resume) |

One `ebreak` enable per privilege value mirrors RISC-V's `ebreakm/s/u` exactly; the three-valued
privilege costs three enable bits either way.

### dpc

The PC captured at halt — a 24-trit instruction-slot index
([the PC is 24 trits and code addresses are ordinary words](rebel6-isa.md#memory-map)) — carried
over the DMI as a sign-extended 64-bit two's-complement integer, like every other transported
value ([data representation](#data-representation)). On halt, `dpc` holds:

- the address of the `EBREAK.T` itself (cause 1) — not the next instruction;
- the address of the instruction that matched an execute trigger, which has **not** executed
  (cause 2);
- the next instruction not yet executed (causes 3, 4);
- the address of `_start` (cause 5).

Writing `dpc` before resume redirects execution: the hart resumes at the written address. This is
the only way a debugger moves the PC — there is no PC regno in the
[Access Register command](#access-register), matching RISC-V, where PC access is `dpc` access.

### dscratch0, dscratch1

Two 64-bit scratch words for debugger bookkeeping. The DM stores and returns them; no REBEL-6
behaviour attaches.

## Run control

- **Halt.** `dmcontrol.haltreq` = 1 requests a halt; the hart enters Debug Mode before executing
  its next instruction, with `dcsr.cause` = 3. Halting is permitted at any privilege level and in
  any state, including inside a `WFI.T` stall.
- **Resume.** `dmcontrol.resumereq` = 1 resumes the hart at `dpc`. `dmstatus.allresumeack`
  acknowledges. Resume restores nothing architectural, because entry saved nothing architectural.
- **Single step.** With `dcsr.step` = 1, a resume executes exactly one instruction and re-enters
  Debug Mode with cause 4. If that instruction traps, the trap entry sequence
  ([platform](rebel6-platform.md#trap-entry)) completes architecturally — `Lepc`/`Lcause`/`Lstatus`
  are written, `MODE` changes — and the hart halts with `dpc` = the handler address: stepping
  descends into handlers rather than skipping them. An interrupt pending at the step is taken
  first under the same rule. `EBREAK.T` under `step` with the matching `ebreak` enable set halts
  with cause 1 (ebreak wins over step).
- **Halt at reset.** `dmcontrol.resethaltreq` arms halt-on-reset: the hart comes out of reset
  having retired nothing, halted at `_start` (the reset vector target,
  [ABI](rebel6-abi.md)) with `dcsr.cause` = 5. This is the only way to debug from the first
  instruction, and the default posture a debug session should adopt.

## EBREAK.T

When the hart executes `EBREAK.T` and the `dcsr.ebreak` bit matching the current privilege trit is
set, the hart enters Debug Mode with `dcsr.cause` = 1 and `dpc` = the address of the `EBREAK.T`.
No trap entry occurs; `mcause`/`mepc` are untouched.

When the matching bit is clear — including whenever no debugger has ever attached — `EBREAK.T`
behaves exactly as the [base ISA](rebel6-isa.md) and [platform](rebel6-platform.md#traps) specify:
breakpoint trap, cause −10, ordinary trap entry. The unattached behaviour of every program is
trit-for-trit identical with and without a DM present. Since programs cannot patch `EBREAK.T` into
I-space, its debug role is limited to breakpoints compiled or assembled into the program;
debugger-planted breakpoints use the trigger module.

## Trigger module

**4 triggers**, minimum and reference count. Triggers live **entirely in DM address space**: no
trigger CSRs exist (the Zicsr shim's table is unchanged — CSR numbers 0x7A0–0x7BF stay absent and
raise −12), no negative-index registers are added, and hart software at any privilege level cannot
detect, read, or fire-and-observe a trigger. This is stronger isolation than RISC-V native
triggers (which U-mode can sometimes observe via `tselect`), and it is forced anyway: the debugger
owns triggers through the DMI, and nothing else needs them.

Trigger state, selected by [`tselect`](#dm-registers) (0…3):

| Register | Function |
|----------|----------|
| `tcontrol` | bit 0 `en` — armed; bits 2:1 `type`: 0 = execute (PC match), 1 = load, 2 = store, 3 = load-or-store |
| `tvalue` | the comparand — an instruction-slot index (execute) or a tryte address (load/store), sign-extended 64-bit |

- **Execute triggers** compare against the PC **before fetch**: the matched instruction has not
  executed when the hart halts (`dcsr.cause` = 2, `dpc` = the matched address). This is the
  breakpoint primitive.
- **Load/store triggers** compare against the effective tryte address of a data access before it
  is performed; halting is imprecise by at most nothing — the access has not happened, `dpc` = the
  accessing instruction. Address-range and data-value matching are not in 0.1; a future revision
  may widen `tcontrol`.
- Trigger evaluation while running costs one comparison per armed trigger; with no triggers armed
  and no halt request pending, a conforming implementation's fast path is a single "debug idle"
  test.

## Debug Module

One DM per platform, controlling all harts. DMI register addresses follow RISC-V 1.0 where a
register has a RISC-V counterpart — a deliberate aid to tool porting — and the register width over
the DMI is uniformly 64 bits ([data representation](#data-representation)).

**The DM is not memory-mapped.** Harts cannot reach it: no MMIO slot is assigned
(the [platform MMIO map](rebel6-platform.md#mmio)'s reserved slots 11–15 stay reserved), matching
RISC-V's separation of the DM from the hart's address space. A future self-hosted-debug ruling
could claim an MMIO slot; 0.1 deliberately does not.

### DM registers

| DMI addr | Register | Function |
|----------|----------|----------|
| 0x10 | `dmcontrol` | bit 0 `dmactive` (DM reset, 0 → 1 to activate); bit 1 `ndmreset` (reset the hart(s), not the DM); bit 30 `resumereq`; bit 31 `haltreq`; bits 27:26 `resethaltreq` set/clear; bits 25:16 `hartsel` — **hardwired 0** in 0.1 (single-hart debug; the field is sized for the 27-hart variant and a future revision assigns it) |
| 0x11 | `dmstatus` | version and hart status: `allhalted`, `allrunning`, `allresumeack`, `allhavereset`, `authenticated` (hardwired 1 — no authentication in 0.1) |
| 0x12 | `hartinfo` | capabilities of the selected hart; reads 0 in 0.1 |
| 0x16 | `abstractcs` | `progbufsize` — **hardwired 0** (see [design principle](#rebel-6-external-debug-specification)); `datacount` = 1; `busy`; `cmderr` (0 none, 1 busy, 2 not supported, 3 exception, 4 halt/resume, 5 bus, 7 other) |
| 0x17 | `command` | abstract command register — write executes ([below](#abstract-commands)) |
| 0x04 | `data0` | 64-bit abstract data register (one, per `datacount`) |
| 0x20 | `tselect` | trigger select, 0…3 |
| 0x21 | `tcontrol` | selected trigger's control ([trigger module](#trigger-module)) |
| 0x22 | `tvalue` | selected trigger's comparand |
| 0x40 | `haltsum0` | halted-harts summary bit vector |

Trigger registers sit at DM addresses rather than behind an abstract command because triggers are
DM state, not hart state — the hart never sees them, so routing them through Access Register would
be a fiction. (RISC-V reaches its triggers through CSR-number regnos; REBEL-6 has no trigger CSRs
to number.)

### Abstract commands

Two commands. A command issued while the hart is running fails with `cmderr` = 4 (halt first);
`busy` covers DMI-speed races.

#### Access Register

Reads or writes one register of the selected hart by **regno** — a 16-bit binary index (the DMI is
binary; inventing a ternary index encoding for the debugger's side of the wire would buy nothing):

| regno | Registers | Mapping |
|-------|-----------|---------|
| 0x0001 … 0x016C | system registers `X-1` … `X-364` | regno = \|negative index\| — `mtvec` = 0x0001, `mepc` = 0x0002, `mcause` = 0x0003, `mstatus` = 0x0004 … per the [platform register map](rebel6-platform.md#register-map--negative-range-standard-layout) |
| 0x0F00 … 0x0F03 | debug bank | `dcsr` = 0x0F00, `dpc` = 0x0F01, `dscratch0/1` = 0x0F02/0x0F03 |
| 0x1000 … 0x116C | `X0` … `X+364` | regno = 0x1000 + index — mirrors RISC-V's GPRs-at-0x1000 convention |

The window is hart-local, like the architecture's own register numbering. Reads and writes are
**raw**: the DM bypasses the decode-time privilege check (exception −11 is a rule about hart
instructions, not about the DM), bypasses the streaming-register write fault (a DM write to a
streaming register is `cmderr` = 2, not supported — hardware-written state has no meaningful
debugger write), and reads views (`sstatus`, `sie`, `sip`) exactly as a maximally privileged hart
read would. Writing `X0` is `cmderr` = 2. Unimplemented regnos (outside the variant's window, or
gaps) are `cmderr` = 2.

#### Access Memory

**Mandatory.** With no program buffer this is the only memory path, so the RISC-V-optional command
is required here. The command carries an address, a size (tryte / halfword / word), a
write flag, and one field RISC-V does not have:

- **`aspace`** (1 bit): 0 = **data space** — tryte addresses, all three regions (D-ROM, D-RAM,
  MMIO) reachable, reads and writes; 1 = **I-space** — instruction-slot indices, **read-only**.
  An I-space read returns the sign-extended integer value of the addressed 32-trit instruction
  word (range ±(3³²−1)/2, needing 51 bits — one 64-bit `data0` read per instruction, no hi/lo
  pairing). An I-space write is `cmderr` = 2 — program loading is the platform's concern (ROM
  image), not the DM's, in 0.1.

The two address spaces count different units and overlap numerically
([Harvard rule](rebel6-isa.md#memory-map)); a one-bit selector is the honest encoding, where a
flat-address fiction would import the very confusion the architecture avoids. D-space accesses
through the DM follow MMIO access semantics when they land in the `−` region (performed exactly
as issued); a faulting access (unpopulated MMIO, misaligned MMIO) is `cmderr` = 5, and the hart
takes no trap — DM accesses are not hart accesses.

**The debugger may read I-space — ruling.** The fetch-only rule
([memory consistency](rebel6-platform.md#memory-consistency)) binds *hart instructions*; it
exists to make self-modifying code impossible and instruction storage single-purpose. The DM is
not the hart. A debugger that could not read code could not disassemble, and the prohibition
would protect nothing. I-space reads through the DM are therefore legal, read-only, and invisible
to the hart.

### Data representation

Every value crossing the DMI — register contents, `dpc`, memory data, trigger comparands — is the
**sign-extended 64-bit two's-complement integer value** of the balanced-ternary word. A 24-trit
word spans ±(3²⁴−1)/2 = ±141,214,768,240, needing 39 bits; 64 is the smallest power-of-two
transport width that holds it with headroom, and one uniform width keeps every DMI transaction
single-shot. A write whose value lies outside the target's balanced range is `cmderr` = 2 and
writes nothing. Trit-string rendering (`+0−` notation) is a presentation concern for tools, not a
wire format: the wire carries integers, tools render trits.

## JTAG Debug Transport Module

The transport is a standard **IEEE 1149.1 JTAG TAP** — four wires (TCK, TMS, TDI, TDO), the
16-state FSM, binary throughout. JTAG is an electrical and protocol standard below the level where
radix matters; a ternary core debugs over binary JTAG with nothing lost, and every commodity probe
(Segger J-Link, FTDI, CMSIS-DAP adapters in JTAG mode) can drive the wire. Nothing below the DMI
data field knows the machine is ternary.

- **IR length 5.** IDCODE = 0x00001, DTMCS = 0x10, DMI = 0x11 — the RISC-V DTM's IR assignments,
  kept so probe-side tooling ports by table edit. BYPASS = 0x1F.
- **IDCODE**: a REBEL-6 identifier word. 0.1 assigns `0x52454201` ("REB" + version 01) as the
  provisional value; a JEDEC-coded assignment replaces it when one exists.
- **DTMCS** (32-bit DR): `version` = **7** — a deliberately non-RISC-V value so tools detect the
  wide-DMI format below and refuse to misparse; `abits` = 7; `dmireset`; `dmihardreset`.
- **DMI** (73-bit DR): `abits(7) + data(64) + op(2)`, address at the MST end, op at the LST end,
  op semantics as RISC-V (00 nop, 01 read, 10 write; status returned in the same field: 00 ok,
  10 failed, 11 busy).

**Divergence — the 64-bit data field** (RISC-V: 32). One 24-trit word needs 39 bits; with RISC-V's
32-bit field every register or memory access would be a hi/lo transaction pair and `datacount`
would double. A 73-bit DR costs nothing at the TAP level (DR length is arbitrary in 1149.1) and
halves every access. The consequence is acknowledged: **stock RISC-V debug tooling that hardwires
the 32-bit DMI (OpenOCD's `riscv` target) cannot drive this DTM** — tooling reaches the DM through
generic scan primitives or a REBEL-6 target driver instead. `dtmcs.version` = 7 is the guard.

## Reset

- `dmcontrol.dmactive` is the DM's own reset: 0 holds the DM in reset, 1 activates it. DM state
  (triggers, `resethaltreq` arming, scratch) survives hart resets and is lost only on DM reset.
- `dmcontrol.ndmreset` resets the hart(s) — equivalent to the platform reset ruling
  ([platform](rebel6-platform.md#register-map--negative-range-standard-layout)): system registers
  to 0, `mstatus.MODE` to `+` — without resetting the DM.
- With `resethaltreq` armed, a hart (however reset) halts at `_start` having retired nothing
  ([run control](#run-control)).

## Conformance

Every behaviour in this document is testable against a simulator implementing the DM and DTM;
hardware adds only electrical conformance (TAP timing, which IEEE 1149.1 owns). Sim-testable, to
enter the conformance suite as a debug category: halt/resume/step semantics, `dpc` capture rules
per cause, `EBREAK.T` attached/unattached equivalence, trigger match-before-execute, out-of-band
entry (architectural state identical before and after a halt/resume pair), regno map coverage,
`aspace` semantics and error codes, DTMCS/DMI framing, `resethaltreq`. Hardware-only: TAP
electricals, `dmactive` power-on behaviour.

## Non-normative appendix: tooling conventions

Recommendations for debugger implementations; nothing here binds an implementation of the DM.

- **GDB remote serial protocol.** A simulator or probe server should expose registers with the
  [Access Register regnos](#access-register) as GDB register numbers, a g-packet of the 27
  ilp32 ABI registers plus PC, and the remaining window via `p`/`P`. RSP has a single flat
  address space; the convention is: data-space addresses pass through natively, and I-space is
  windowed at offset **0x4000_0000_0000_0000** (an address bit no 24-trit tryte address can
  reach), mapping `m`-packet reads onto `aspace` = 1.
- **Breakpoint packets.** `Z0` (software) and `Z1` (hardware) both map to execute triggers —
  there is no software breakpoint to insert. A server should answer `Z0` rather than erroring,
  so stock front-ends work, and report exhaustion when the 4 triggers are armed.
- **Ternary rendering.** Balanced-ternary display (`+0−` strings) belongs in the tool (monitor
  commands, DAP formatters), not in the wire protocols.
- **Source maps.** Two composable layers: the executor emits `<prog>.tasmap`
  (`address<TAB>line`, addresses = instruction-slot indices) at assembly time — Layer 1; a
  compiler's `-g` mode interleaves `# .loc file:line` comment directives in the `.tas` (comments
  strip in every assembler, so the program is unchanged) — Layer 2. A DAP adapter composes
  Layer 2 ∘ Layer 1 for source-level stepping of compiled programs.

## Non-normative appendix: 0.1 field encodings

The normative text pins DMI addresses, field *names*, and semantics; the bit positions below are
what the 0.1 reference implementation (R2R simulator DM/DTM) and the conformance TCL use. RISC-V
positions wherever a counterpart exists, so probe-side tooling ports by table edit.

- **`dmcontrol`**: `dmactive` [0], `ndmreset` [1], `clrresethaltreq` [26], `setresethaltreq`
  [27], `ackhavereset` [28], `resumereq` [30], `haltreq` [31], `hartsel` [25:16] hardwired 0.
- **`dmstatus`**: `version` [3:0] = 7 (matches `dtmcs.version`), `authenticated` [7] = 1,
  `any`/`allhalted` [8]/[9], `any`/`allrunning` [10]/[11], `any`/`allunavail` [12]/[13] (set
  after the program runs to exit), `any`/`allresumeack` [16]/[17], `any`/`allhavereset`
  [18]/[19].
- **`abstractcs`**: `datacount` [3:0] = 1, `cmderr` [10:8] (W1C), `busy` [12], `progbufsize`
  [28:24] = 0.
- **`command`** (64-bit, like every DM register): `cmdtype` [63:56] — 0 = Access Register,
  2 = Access Memory (RISC-V values); `write` [55]. Access Register: `regno` [15:0]. Access
  Memory: `aspace` [54], `aamsize` [53:52] (0 tryte / 1 halfword / 2 word), `address` [51:0]
  two's-complement sign-extended — the 64-bit command is what lets the address ride in the
  command itself, keeping `datacount` = 1 with no second data register.
- **`dcsr` view** (regno 0x0F00): `cause` [3:0], `ebreak+`/`ebreak0`/`ebreak−` [4]/[5]/[6],
  `step` [7], `prv` [9:8] (0/1/2 = U/S/M).
- **`tcontrol`**: `enable` [0], `type` [2:1] (0 execute / 1 load / 2 store / 3 load+store).

## Non-normative appendix: narrow-DMI compatibility mode

Reserved, not specified. If reuse of 32-bit-DMI tooling (OpenOCD `riscv`, probe firmware with
RISC-V DM support such as the J-Link's) ever justifies it, a DTM could offer `dtmcs.version` = 1
with a 32-bit data field and hi/lo paired `data0`/`data1`, at double the transaction count. 0.1
reserves the possibility and deliberately does not specify it — one wire format, one truth.
