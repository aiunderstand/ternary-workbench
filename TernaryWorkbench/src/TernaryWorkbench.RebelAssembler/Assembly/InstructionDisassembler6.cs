using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionDisassembler6
{
    // Reverse-lookup: 6-trit string → register name (X0 first to prefer it over X-0)
    private static readonly IReadOnlyDictionary<string, string> ReverseRegister =
        RegisterDictionary
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(kv => kv.Key.Length).First().Key);

    /// <summary>Disassemble a 32-trit machine code string into a mnemonic+operands string.</summary>
    public static string Disassemble(
        string instruction,
        IReadOnlyDictionary<string, InstructionPattern>? patterns = null,
        int currentIndex = 0)
    {
        patterns ??= Patterns;

        if (instruction.Length != InstructionWidth)
            throw new InvalidOperationException(
                $"REBEL-6 instruction must be {InstructionWidth} trits long, got {instruction.Length}.");

        if (!instruction.All(ch => ch is '+' or '-' or '0'))
            throw new InvalidOperationException(
                $"Instruction contains invalid characters: '{instruction}'. Only '+', '-', '0' are allowed.");

        // Detect encoding class by last trit of the opcode.
        // All 4-trit opcodes end in '0' (suffix 00, -0, or +0).
        // All 2-trit opcodes (G/Y-type long-immediate) end in '+' or '-'.
        char lastTrit = instruction[^1]; // last trit of the 32-trit instruction = last trit of opcode
        if (lastTrit == '0')
            return DisassembleStandard(instruction, patterns, currentIndex);

        // 2-trit long-immediate form (G or Y type)
        return DisassembleLongImmediate(instruction, patterns, currentIndex);
    }

    // -------------------------------------------------------------------------
    // Standard (4-trit opcode)
    // Layout: rs1(6) | rs2(6) | rd1(6) | rd2(6) | func(4) | opcode(4)
    // -------------------------------------------------------------------------

    private static string DisassembleStandard(
        string mc,
        IReadOnlyDictionary<string, InstructionPattern> patterns,
        int currentIndex)
    {
        var opcode = mc[28..32]; // [3:0] last 4 trits
        var func   = mc[24..28]; // [7:4]
        var rd2    = mc[18..24]; // [13:8]
        var rd1    = mc[12..18]; // [19:14]
        var rs2    = mc[6..12];  // [25:20]
        var rs1    = mc[0..6];   // [31:26]

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Rs1,  rs1  },
            { Rs2,  rs2  },
            { Rd1,  rd1  },
            { Rd2,  rd2  },
            { Func, func },
        };

        var pattern = ResolveStandardPattern(opcode, fields, patterns)
            ?? throw new InvalidOperationException($"Unknown opcode '{opcode}'.");

        var operands = new List<string>();
        foreach (var fieldName in pattern.AssemblyOperands)
        {
            if (WideFieldSlots(fieldName) is { } wide)
            {
                operands.Add(BalancedTernaryToInt(fields[wide.Hi] + fields[wide.Lo]).ToString());
                continue;
            }
            operands.Add(FormatOperand(
                fields[MapFieldToSlot(fieldName)],
                IsOffsetField(fieldName) || IsShamtField(fieldName),
                currentIndex));
        }

        return operands.Count == 0
            ? pattern.Mnemonic
            : $"{pattern.Mnemonic} {string.Join(", ", operands)}";
    }

    // -------------------------------------------------------------------------
    // Long-immediate (G/Y, 2-trit opcode)
    // -------------------------------------------------------------------------

    private static string DisassembleLongImmediate(
        string mc,
        IReadOnlyDictionary<string, InstructionPattern> patterns,
        int currentIndex)
    {
        var opcode2 = mc[30..32]; // last 2 trits

        if (!patterns.TryGetValue(opcode2, out var pattern))
        {
            // Try scanning all patterns with 2-trit opcodes
            pattern = patterns.Values
                .FirstOrDefault(p => p.Opcode.Length == 2
                    && string.Equals(p.Opcode, opcode2, StringComparison.Ordinal));
        }

        if (pattern is null)
            throw new InvalidOperationException($"Unknown 2-trit opcode '{opcode2}'.");

        bool hasDestReg = pattern.AssemblyOperands.Count > 0
            && string.Equals(pattern.AssemblyOperands[0], Rd1, StringComparison.OrdinalIgnoreCase);

        if (hasDestReg)
        {
            // G-type: imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
            var rd1Trits = mc[12..18];            // rd1 slot [19:14]
            var imm24    = mc[0..12] + mc[18..30]; // upper 12 [31:20] + lower 12 [13:2]
            var rd1Name  = FormatRegister(rd1Trits);
            var immVal   = FormatLongImmediate(imm24, currentIndex);
            return $"{pattern.Mnemonic} {rd1Name}, {immVal}";
        }
        else
        {
            // Y-type: rs1(6) | imm[23:0](24) | opc(2)
            var rs1Trits = mc[0..6];   // rs1 slot [31:26]
            var imm24    = mc[6..30];  // imm [25:2]
            var rs1Name  = FormatRegister(rs1Trits);
            var immVal   = FormatLongImmediate(imm24, currentIndex);
            return $"{pattern.Mnemonic} {rs1Name}, {immVal}";
        }
    }

    // -------------------------------------------------------------------------
    // Pattern matching
    // -------------------------------------------------------------------------

    private static InstructionPattern? ResolveStandardPattern(
        string opcode,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, InstructionPattern> patterns)
    {
        return patterns.Values
            .Where(p => p.Opcode.Length == 4) // only standard patterns
            .Select((p, i) => (p, i, score: ScoreStandardPattern(p, opcode, fields)))
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.i)
            .Select(x => x.p)
            .FirstOrDefault();
    }

    private static int ScoreStandardPattern(
        InstructionPattern pattern,
        string opcode,
        IReadOnlyDictionary<string, string> fields)
    {
        if (!string.Equals(pattern.Opcode, opcode, StringComparison.Ordinal))
            return -1;

        // Fields that are supplied by assembly operands (variable)
        var assemblyFields = pattern.AssemblyOperands
            .SelectMany(MapFieldToSlots)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fixedFields = new[] { Rs1, Rs2, Rd1, Rd2, Func };
        var score = 0;
        foreach (var fieldName in fixedFields)
        {
            if (assemblyFields.Contains(fieldName))
                continue;

            var expected = fieldName == Func ? DefaultFunc : DefaultField;
            if (pattern.Defaults != null && pattern.Defaults.TryGetValue(fieldName, out var dv))
                expected = dv;

            if (!string.Equals(fields[fieldName], expected, StringComparison.Ordinal))
                return -1;

            score++;
        }
        return score;
    }

    // -------------------------------------------------------------------------
    // Operand formatting
    // -------------------------------------------------------------------------

    private static string FormatRegister(string trits)
    {
        if (ReverseRegister.TryGetValue(trits, out var name))
            return name;
        return trits; // fall back to raw trit string
    }

    private static string FormatOperand(string trits, bool isOffset, int currentIndex)
    {
        if (isOffset)
        {
            // Decode 6-trit balanced ternary to integer offset
            int val = BalancedTernaryToInt(trits);
            return val.ToString();
        }
        return FormatRegister(trits);
    }

    private static string FormatLongImmediate(string trits, int currentIndex)
    {
        long val = BalancedTernaryToLong(trits);
        return val.ToString();
    }

    // -------------------------------------------------------------------------
    // Balanced ternary → integer helpers
    // -------------------------------------------------------------------------

    private static int BalancedTernaryToInt(string trits)
    {
        int val = 0;
        foreach (char c in trits)
        {
            val *= 3;
            val += c == '+' ? 1 : c == '-' ? -1 : 0;
        }
        return val;
    }

    private static long BalancedTernaryToLong(string trits)
    {
        long val = 0;
        foreach (char c in trits)
        {
            val *= 3;
            val += c == '+' ? 1 : c == '-' ? -1 : 0;
        }
        return val;
    }
}
