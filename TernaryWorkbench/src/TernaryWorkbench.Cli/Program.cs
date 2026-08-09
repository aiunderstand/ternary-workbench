using TernaryWorkbench.CharTStringConverter;
using TernaryWorkbench.Core;
using TernaryWorkbench.RebelAssembler;

// -----------------------------------------------------------------------
// TernaryWorkbench CLI
//
// Usage:
//   twb --from <radix> --to <radix> <value> [--lsd-first] [--bct]
//   twb chart encode <text>
//   twb chart decode <ternary>
//   twb chart decode-u8 <ternary>
//   twb chart decode-tc <ternary>
//   twb chart detect <ternary>
//
// Radix aliases (case-insensitive):
//   bin / base2           → Base2Unsigned
//   bin1c / base2c1       → Base2Signed1C
//   bin2c / base2c        → Base2Signed2C
//   oct  / base8          → Base8Unsigned
//   hex  / base16         → Base16Unsigned
//   base64                → Base64Rfc4648
//   ter  / base3          → Base3Unbalanced
//   ter2c / base3c2       → Base3Signed2C
//   ter3c / base3c3       → Base3Signed3C
//   balanced / base3b     → Base3Balanced
//   bsdpnx / base3pnx     → Base3BsdPnx
//   base9                 → Base9Unbalanced
//   base27                → Base27Unbalanced
//   dec  / base10         → Base10
// -----------------------------------------------------------------------

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

// charT-string converter subcommand: twb chart <subcommand> <value>
if (args[0].Equals("chart", StringComparison.OrdinalIgnoreCase))
    return RunChart(args[1..]);

// REBEL-2 assembler subcommand: twb rebel2 asm <assembly>  |  twb rebel2 dis <code...>
if (args[0].Equals("rebel2", StringComparison.OrdinalIgnoreCase))
    return RunRebel2(args[1..]);

// REBEL-2v2 assembler subcommand: twb rebel2v2 asm <assembly>  |  twb rebel2v2 dis <code...>
if (args[0].Equals("rebel2v2", StringComparison.OrdinalIgnoreCase))
    return RunRebel2v2(args[1..]);

// REBEL-6 assembler subcommand: twb rebel6 asm <assembly>  |  twb rebel6 dis <code...>
if (args[0].Equals("rebel6", StringComparison.OrdinalIgnoreCase))
    return RunRebel6(args[1..]);

Radix? fromRadix = null, toRadix = null;
bool lsdFirst = false, bct = false;
string? value = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--from":
            fromRadix = ParseRadix(NextArg(args, ref i, "--from"));
            break;
        case "--to":
            toRadix = ParseRadix(NextArg(args, ref i, "--to"));
            break;
        case "--lsd-first":
        case "--lsd":
            lsdFirst = true;
            break;
        case "--bct":
            bct = true;
            break;
        default:
            if (value is not null)
            {
                Error($"Unexpected argument: {args[i]}");
                return 1;
            }
            value = args[i];
            break;
    }
}

if (fromRadix is null || toRadix is null || value is null)
{
    Error("Required: --from <radix>, --to <radix>, and a value to convert.");
    PrintHelp();
    return 1;
}

try
{
    var opts = new OutputOptions(LsdFirst: lsdFirst, BctEncoding: bct);
    string result = RadixConverter.Convert(value, fromRadix.Value, toRadix.Value, opts);
    Console.WriteLine(result);
    return 0;
}
catch (FormatException ex)
{
    Error($"Conversion error: {ex.Message}");
    return 2;
}
catch (OverflowException ex)
{
    Error($"Overflow error: {ex.Message}");
    return 2;
}

// -----------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------

static string NextArg(string[] args, ref int i, string flag)
{
    if (++i >= args.Length)
    {
        Error($"Flag {flag} requires an argument.");
        Environment.Exit(1);
    }
    return args[i];
}

static Radix ParseRadix(string s) => s.ToLowerInvariant() switch
{
    "bin"      or "base2"     => Radix.Base2Unsigned,
    "bin1c"    or "base2c1"   => Radix.Base2Signed1C,
    "bin2c"    or "base2c"    => Radix.Base2Signed2C,
    "oct"      or "base8"     => Radix.Base8Unsigned,
    "hex"      or "base16"    => Radix.Base16Unsigned,
    "base64"                  => Radix.Base64Rfc4648,
    "ter"      or "base3"     => Radix.Base3Unbalanced,
    "ter2c"    or "base3c2"   => Radix.Base3Signed2C,
    "ter3c"    or "base3c3"   => Radix.Base3Signed3C,
    "balanced" or "base3b"    => Radix.Base3Balanced,
    "bsdpnx"   or "base3pnx"  => Radix.Base3BsdPnx,
    "base9"                   => Radix.Base9Unbalanced,
    "base27"                  => Radix.Base27Unbalanced,
    "dec"      or "base10"    => Radix.Base10,
    _ => throw new ArgumentException($"Unknown radix '{s}'. Run with --help for valid names.")
};

static void Error(string msg) => Console.Error.WriteLine($"Error: {msg}");

// -----------------------------------------------------------------------
// charT-string subcommand
// -----------------------------------------------------------------------

static int RunChart(string[] chartArgs)
{
    if (chartArgs.Length == 0 || chartArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
    {
        PrintChartHelp();
        return 0;
    }

    string subcommand = chartArgs[0].ToLowerInvariant();

    // Remaining arguments after the subcommand word
    string[] rest = chartArgs[1..];
    string? input = rest.Length > 0 ? string.Join(" ", rest) : null;

    // Allow reading ternary/text from stdin with "-"
    if (input == "-")
        input = Console.In.ReadToEnd();

    if (string.IsNullOrWhiteSpace(input))
    {
        Error($"chart {subcommand}: no input provided. Pass a value or '-' to read from stdin.");
        return 1;
    }

    switch (subcommand)
    {
        case "encode":
            return RunChartEncode(input);

        case "decode":
            return RunChartDecode(input);

        case "decode-u8":
            return RunChartDecodeU8(input);

        case "decode-tc":
            return RunChartDecodeTc(input);

        case "detect":
            return RunChartDetect(input);

        default:
            Error($"Unknown chart subcommand '{chartArgs[0]}'. Run 'twb chart --help' for usage.");
            return 1;
    }
}

static int RunChartEncode(string utf8Text)
{
    bool anyErrors = false;

    var r8 = CharTu8Codec.Encode(utf8Text);
    Console.WriteLine("=== charT_u8 ===");
    Console.WriteLine(r8.EncodedText);
    if (r8.Errors.Count > 0)
    {
        anyErrors = true;
        foreach (var (msg, count) in r8.Errors.CollapseRepeated())
            Console.Error.WriteLine($"  charT_u8 error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    }

    Console.WriteLine();

    var rc = CharTCu8Codec.Encode(utf8Text);
    Console.WriteLine("=== charTC_u8 ===");
    Console.WriteLine(rc.EncodedText);
    if (rc.Errors.Count > 0)
    {
        anyErrors = true;
        foreach (var (msg, count) in rc.Errors.CollapseRepeated())
            Console.Error.WriteLine($"  charTC_u8 error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    }

    return anyErrors ? 2 : 0;
}

static int RunChartDecode(string ternary)
{
    bool anyErrors = false;

    var r8 = CharTu8Codec.Decode(ternary);
    Console.WriteLine("=== charT_u8 ===");
    Console.WriteLine(r8.DecodedText);
    if (r8.Errors.Count > 0)
    {
        anyErrors = true;
        foreach (var (msg, count) in r8.Errors.CollapseRepeated())
            Console.Error.WriteLine($"  charT_u8 error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    }

    Console.WriteLine();

    var rc = CharTCu8Codec.Decode(ternary);
    Console.WriteLine("=== charTC_u8 ===");
    Console.WriteLine(rc.DecodedText);
    if (rc.Errors.Count > 0)
    {
        anyErrors = true;
        foreach (var (msg, count) in rc.Errors.CollapseRepeated())
            Console.Error.WriteLine($"  charTC_u8 error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    }

    return anyErrors ? 2 : 0;
}

static int RunChartDecodeU8(string ternary)
{
    var r8 = CharTu8Codec.Decode(ternary);
    Console.WriteLine(r8.DecodedText);
    foreach (var (msg, count) in r8.Errors.CollapseRepeated())
        Console.Error.WriteLine($"Error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    return r8.Errors.Count > 0 ? 2 : 0;
}

static int RunChartDecodeTc(string ternary)
{
    var rc = CharTCu8Codec.Decode(ternary);
    Console.WriteLine(rc.DecodedText);
    foreach (var (msg, count) in rc.Errors.CollapseRepeated())
        Console.Error.WriteLine($"Error: {msg}{(count > 1 ? $" [×{count}]" : "")}");
    return rc.Errors.Count > 0 ? 2 : 0;
}

static int RunChartDetect(string ternary)
{
    var detected = CharTStringEncodingDetector.Detect(ternary);
    Console.WriteLine(detected.ToDisplayString());
    return detected == DetectedEncoding.Unknown ? 2 : 0;
}

// -----------------------------------------------------------------------
// REBEL-2 subcommand
// -----------------------------------------------------------------------

static int RunRebel2(string[] rebel2Args)
{
    if (rebel2Args.Length == 0 || rebel2Args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
    {
        PrintRebel2Help();
        return 0;
    }

    string sub = rebel2Args[0].ToLowerInvariant();
    string[] rest = rebel2Args[1..];

    switch (sub)
    {
        case "asm":
        case "assemble":
        {
            string input = rest.Length > 0 ? string.Join(" ", rest) : string.Empty;
            if (input == "-") input = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(input))
            {
                Error("rebel2 asm: no assembly input provided. Pass a value or '-' to read from stdin.");
                return 1;
            }
            try
            {
                var instructions = Rebel2Assembler.AssembleInstructions(input);
                foreach (var inst in instructions)
                    Console.WriteLine(inst.MachineCode);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Error($"rebel2 asm: {ex.Message}");
                return 2;
            }
        }

        case "dis":
        case "disassemble":
        {
            // Each remaining arg is a 10-trit machine code word.
            // Also support reading one-per-line from stdin with '-'.
            IEnumerable<string> words;
            if (rest.Length == 1 && rest[0] == "-")
                words = Console.In.ReadToEnd()
                    .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l));
            else
                words = rest;

            var wordList = words.ToList();
            if (wordList.Count == 0)
            {
                Error("rebel2 dis: no machine code provided. Pass 10-trit words or '-' to read from stdin.");
                return 1;
            }

            bool anyErrors = false;
            foreach (var word in wordList)
            {
                try
                {
                    Console.WriteLine(Rebel2Assembler.Disassemble(word));
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error ({word}): {ex.Message}");
                    anyErrors = true;
                }
            }
            return anyErrors ? 2 : 0;
        }

        default:
            Error($"Unknown rebel2 subcommand '{rebel2Args[0]}'. Run 'twb rebel2 --help' for usage.");
            return 1;
    }
}

// -----------------------------------------------------------------------
// REBEL-2v2 subcommand
// -----------------------------------------------------------------------

static int RunRebel2v2(string[] rebel2v2Args)
{
    if (rebel2v2Args.Length == 0 || rebel2v2Args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
    {
        PrintRebel2v2Help();
        return 0;
    }

    string sub = rebel2v2Args[0].ToLowerInvariant();
    string[] rest = rebel2v2Args[1..];

    switch (sub)
    {
        case "asm":
        case "assemble":
        {
            string input = rest.Length > 0 ? string.Join(" ", rest) : string.Empty;
            if (input == "-") input = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(input))
            {
                Error("rebel2v2 asm: no assembly input provided. Pass a value or '-' to read from stdin.");
                return 1;
            }
            try
            {
                var instructions = Rebel2v2Assembler.AssembleInstructions(input);
                foreach (var inst in instructions)
                    Console.WriteLine(inst.MachineCode);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Error($"rebel2v2 asm: {ex.Message}");
                return 2;
            }
        }

        case "dis":
        case "disassemble":
        {
            IEnumerable<string> words;
            if (rest.Length == 1 && rest[0] == "-")
                words = Console.In.ReadToEnd()
                    .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l));
            else
                words = rest;

            var wordList = words.ToList();
            if (wordList.Count == 0)
            {
                Error("rebel2v2 dis: no machine code provided. Pass 10-trit words or '-' to read from stdin.");
                return 1;
            }

            bool anyErrors = false;
            foreach (var word in wordList)
            {
                try
                {
                    Console.WriteLine(Rebel2v2Assembler.Disassemble(word));
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error ({word}): {ex.Message}");
                    anyErrors = true;
                }
            }
            return anyErrors ? 2 : 0;
        }

        default:
            Error($"Unknown rebel2v2 subcommand '{rebel2v2Args[0]}'. Run 'twb rebel2v2 --help' for usage.");
            return 1;
    }
}

// -----------------------------------------------------------------------
// REBEL-6 subcommand
// -----------------------------------------------------------------------

static int RunRebel6(string[] rebel6Args)
{
    if (rebel6Args.Length == 0 || rebel6Args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
    {
        PrintRebel6Help();
        return 0;
    }

    string sub = rebel6Args[0].ToLowerInvariant();
    string[] rest = rebel6Args[1..];

    switch (sub)
    {
        case "asm":
        case "assemble":
        {
            string input = rest.Length > 0 ? string.Join(" ", rest) : string.Empty;
            if (input == "-") input = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(input))
            {
                Error("rebel6 asm: no assembly input provided. Pass a value or '-' to read from stdin.");
                return 1;
            }
            try
            {
                var instructions = Rebel6Assembler.AssembleInstructions(input);
                foreach (var inst in instructions)
                    Console.WriteLine(inst.MachineCode);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Error($"rebel6 asm: {ex.Message}");
                return 2;
            }
        }

        case "dis":
        case "disassemble":
        {
            IEnumerable<string> words;
            if (rest.Length == 1 && rest[0] == "-")
                words = Console.In.ReadToEnd()
                    .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l));
            else
                words = rest;

            var wordList = words.ToList();
            if (wordList.Count == 0)
            {
                Error("rebel6 dis: no machine code provided. Pass 32-trit words or '-' to read from stdin.");
                return 1;
            }

            bool anyErrors = false;
            foreach (var word in wordList)
            {
                try
                {
                    Console.WriteLine(Rebel6Assembler.Disassemble(word));
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error ({word}): {ex.Message}");
                    anyErrors = true;
                }
            }
            return anyErrors ? 2 : 0;
        }

        default:
            Error($"Unknown rebel6 subcommand '{rebel6Args[0]}'. Run 'twb rebel6 --help' for usage.");
            return 1;
    }
}

static void PrintRebel2Help()
{
    Console.WriteLine("""
        TernaryWorkbench CLI — REBEL-2 Assembler / Disassembler

        USAGE
          twb rebel2 asm <assembly>      Assemble one or more REBEL-2 mnemonics to 10-trit machine code
          twb rebel2 dis <code> [code…]  Disassemble 10-trit machine code word(s) to mnemonics

        INPUT
          <assembly>  One instruction per line, up to 9 instructions (one ROM page).
                      Labels, comments (#, ;, $, //), and annotations are supported.
                      Pass '-' to read from stdin.
          <code>      A 10-character string of balanced-ternary trits (+, -, 0).
                      Pass '-' to read one code per line from stdin.

        EXIT CODES
          0  Success
          1  Bad arguments
          2  Assembly or disassembly error

        EXAMPLES
          twb rebel2 asm "ADD.T X1, X2, X3"
          twb rebel2 asm "ADDI.T X1, X2, ++"
          twb rebel2 dis --+-+00+00
          twb rebel2 dis --+-+00+00 -0--++0000
          printf 'ADD.T X1, X2, X3\nNOP.T\n' | twb rebel2 asm -
        """);
}

static void PrintRebel2v2Help()
{
    Console.WriteLine("""
        TernaryWorkbench CLI — REBEL-2v2 Assembler / Disassembler (ISA v0.5)

        USAGE
          twb rebel2v2 asm <assembly>      Assemble one or more REBEL-2v2 mnemonics to 10-trit machine code
          twb rebel2v2 dis <code> [code…]  Disassemble 10-trit machine code word(s) to mnemonics

        INPUT
          <assembly>  One instruction per line, up to 9 instructions (one ROM page).
                      Labels, comments (#, ;, $, //), and annotations are supported.
                      Pass '-' to read from stdin.
          <code>      A 10-character string of balanced-ternary trits (+, -, 0).
                      Pass '-' to read one code per line from stdin.

        EXIT CODES
          0  Success
          1  Bad arguments
          2  Assembly or disassembly error

        EXAMPLES
          twb rebel2v2 asm "ADD.T X1, X2, X3"
          twb rebel2v2 asm "ADDI.T X1, X2, ++"
          twb rebel2v2 dis --+-+00+00+
          twb rebel2v2 dis --+-+00+00+ -0--++0000
          printf 'ADD.T X1, X2, X3\nNOP.T\n' | twb rebel2v2 asm -
        """);
}

static void PrintRebel6Help()
{
    Console.WriteLine("""
        TernaryWorkbench CLI — REBEL-6 Assembler / Disassembler

        USAGE
          twb rebel6 asm <assembly>      Assemble one or more REBEL-6 mnemonics to 32-trit machine code
          twb rebel6 dis <code> [code…]  Disassemble 32-trit machine code word(s) to mnemonics

        INPUT
          <assembly>  One instruction per line, up to 729 instructions (one ROM page).
                      Labels, comments (#, ;, $, //), and annotations are supported.
                      Ternary (.t), binary (RV32I-compatible), and extension mnemonics
                      (M, A, F, P, Ztl, Ztb, Zicsr) are all assemblable; CSR operands
                      accept standard names (mstatus, mepc, ...) or numbers.
                      Pass '-' to read from stdin.
          <code>      A 32-character string of balanced-ternary trits (+, -, 0).
                      Pass '-' to read one code per line from stdin.

        EXIT CODES
          0  Success
          1  Bad arguments
          2  Assembly or disassembly error

        EXAMPLES
          twb rebel6 asm "add.t X1, X2, X3"
          twb rebel6 asm "addi.t X1, X2, 5"
          twb rebel6 asm "lwa.t X1, 42"
          twb rebel6 asm "add X1, X2, X3"
          twb rebel6 asm "mul.t X1, X2, X3"
          twb rebel6 asm "lr.t X1, X2"
          twb rebel6 asm "csrrw X5, mstatus, X6"
          twb rebel6 dis 000000000000000000000000000000++
          printf 'add.t X1, X2, X3\nnop.t\n' | twb rebel6 asm -
        """);
}

static void PrintChartHelp()
{
    Console.WriteLine("""
        TernaryWorkbench CLI — charT-string converter

        USAGE
          twb chart encode   <text>    Encode UTF-8 text to both charT_u8 and charTC_u8
          twb chart decode   <ternary> Decode ternary with both charT_u8 and charTC_u8
          twb chart decode-u8 <ternary> Decode ternary with charT_u8 only
          twb chart decode-tc <ternary> Decode ternary with charTC_u8 only
          twb chart detect   <ternary> Detect which charT encoding the ternary uses

        INPUT
          <ternary>   Space- or newline-separated trytes (6 trits each from {-,0,+}).
                      Symbols may also be separated by '|'.
                      Pass '-' to read from stdin.

        EXIT CODES
          0  Success (no errors)
          1  Bad arguments
          2  Conversion error(s)

        EXAMPLES
          twb chart encode "Hello"
          twb chart decode "+----+ -0--0-"
          twb chart decode-tc "+----+ -0--0- +000-0"
          twb chart detect "+----+ -0--0-"
          echo "+----+" | twb chart decode-tc -
        """);
}

static void PrintHelp()
{
    Console.WriteLine("""
        TernaryWorkbench CLI

        SUBCOMMANDS
          (none)              Radix converter (default)
          chart               charT-string encoder/decoder/detector
          rebel2              REBEL-2 (ISA v1) assembler and disassembler
          rebel2v2            REBEL-2v2 (ISA v0.5) assembler and disassembler
          rebel6              REBEL-6 assembler and disassembler

        RADIX CONVERTER USAGE
          twb --from <radix> --to <radix> <value> [options]

        RADIX NAMES
          bin / base2         Binary (unsigned)
          bin1c / base2c1     Binary (1's complement)
          bin2c / base2c      Binary (2's complement)
          oct / base8         Octal
          hex / base16        Hexadecimal
          base64              Base-64 (RFC 4648)
          ter / base3         Ternary (unbalanced)
          ter2c / base3c2     Ternary (2's complement)
          ter3c / base3c3     Ternary (3's complement)
          balanced / base3b   Ternary (balanced)
          bsdpnx / base3pnx   Ternary (BSD-PNX)
          base9               Nonary (unbalanced)
          base27              Heptavintimal (D.W. Jones)
          dec / base10        Decimal (signed)

        OPTIONS
          --lsd-first         Output digits from least significant to most significant
          --bct               BCT encoding (BSD-PNX: 10=+, 01=-, 11=0)
                              Only meaningful with --to balanced
          --help / -h         Show this help

        EXAMPLES
          twb --from dec --to balanced 42
          twb --from balanced --to dec "+---0"
          twb --from dec --to balanced --bct 42
          twb --from bin --to dec 1010
          twb --from dec --to hex 255

        For charT-string converter help: twb chart --help
        For REBEL-2 assembler help:      twb rebel2 --help
        For REBEL-2v2 assembler help:    twb rebel2v2 --help
        For REBEL-6 assembler help:      twb rebel6 --help
        """);
}
