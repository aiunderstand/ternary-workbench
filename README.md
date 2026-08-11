# Ternary Workbench

An open-source toolkit for balanced ternary computing — radix conversion, REBEL CPU assembly, ternary string encoding, and circuit design tooling.

## Features

- **Radix Converter** — Convert numbers between 15+ numeral systems including balanced ternary, binary (unsigned, 1's complement, 2's complement), octal, hexadecimal, base-64, base-9, base-27, and decimal.
- **REBEL Assembler / Disassembler** — Assemble and disassemble programs for three REBEL CPU ISA variants:
  - REBEL-2 V2.0 — minimal 10-trit, 9-register Harvard-architecture CPU
  - REBEL-2 V2.2 — extended with multiply, divide, memory access, and more instructions
  - REBEL-6 — 32-trit, 729-register, RV32I-compatible production architecture
- **charT String Converter** — Encode/decode UTF-8 text to/from balanced ternary using the charT_u8 (no CRC) and charTC_u8 (CRC-protected) standards.
- **MRCS Studio** — Links to the browser-based EDA tool for mixed-radix and ternary VLSI circuit design.

## Quick Start

### Web App

```bash
dotnet watch --project TernaryWorkbench/src/TernaryWorkbench.Web
```

Open `https://localhost:5001` in your browser.

### CLI

```bash
# Radix conversion
dotnet run --project TernaryWorkbench/src/TernaryWorkbench.Cli -- --from dec --to balanced 42

# REBEL-6 assembly
dotnet run --project TernaryWorkbench/src/TernaryWorkbench.Cli -- rebel6 asm "ADD.T X1, X2, X3"

# charT encoding
dotnet run --project TernaryWorkbench/src/TernaryWorkbench.Cli -- chart encode "hello"

# Full help
dotnet run --project TernaryWorkbench/src/TernaryWorkbench.Cli -- --help
```

### Build & Test

```bash
dotnet build TernaryWorkbench/TernaryWorkbench.slnx
dotnet test TernaryWorkbench/TernaryWorkbench.slnx
```

### Debugging REBEL-6 programs

REBEL-6 has a ratified [external debug specification](docs/rebel6-debug.md); the R2R
reference simulator implements it. Start a session with
`RV32IToREBEL --rebel6 prog.tas --debug-port 3333` and connect interactively with
`riscv64-elf-gdb` / `gdb-multiarch` (`target remote :3333`) — the full procedure, the
`monitor trits/reg/dis/info` commands and the trigger-based breakpoint rules are
documented in the [R2R README](https://github.com/Soppe/RV32IToREBEL). For VS Code
debugging with trit-native register rendering (including C/C++ source-level stepping of
compiled kernels) see
[TernaryWorkbench/src/vscode-rebel6](TernaryWorkbench/src/vscode-rebel6/README.md); for
the OpenOCD/JTAG conformance harness see [conformance/debug](conformance/debug/README.md).

## Documentation

- [REBEL-2 V2.0 ISA Reference](docs/rebel2-isa.md)
- [REBEL-2 V2.2 ISA Reference](docs/rebel2v2-isa.md)
- [REBEL-6 ISA Reference](docs/rebel6-isa.md)
- [REBEL-6 External Debug Specification](docs/rebel6-debug.md)
- [charT_u8 Encoding Standard](docs/chart-u8-standard.md)
- [charTC_u8 Encoding Standard](docs/chartc-u8-standard.md)
- [MRCS Studio](docs/mrcs-studio.md)

## Project Structure

| Directory | Description |
|-----------|-------------|
| `TernaryWorkbench/src/TernaryWorkbench.Core` | Radix conversion library |
| `TernaryWorkbench/src/TernaryWorkbench.RebelAssembler` | REBEL assembler/disassembler |
| `TernaryWorkbench/src/TernaryWorkbench.CharTStringConverter` | charT codec library |
| `TernaryWorkbench/src/TernaryWorkbench.Cli` | Command-line tool |
| `TernaryWorkbench/src/TernaryWorkbench.Web` | Blazor WebAssembly web app |
| `docs/` | ISA references and encoding specifications (editable Markdown) |

## License

MIT — Copyright 2024 Steven Bos

## Contributors

Steven Bos, Sondre Bitubekk, Ole Christian Moholth, Halvor Nybø Risto, Henning Gundersen, Vetle Bodahl, Erika Fegri, Anders Minde
