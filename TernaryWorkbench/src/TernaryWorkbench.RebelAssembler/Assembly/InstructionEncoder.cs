using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionEncoder
{
    /// <summary>Translate a single-instruction string to a 10-trit machine code string.</summary>
    public static string Translate(string instruction, IReadOnlyDictionary<string, InstructionPattern>? patterns = null)
    {
        var parsed = InstructionParser.ParsePage(instruction, PageInstructionCount, RegisterDictionary);
        if (parsed.Instructions.Count != 1)
            throw new InvalidOperationException("Translate expects exactly one instruction.");
        return Translate(parsed.Instructions[0], parsed.Labels, patterns);
    }

    /// <summary>Translate a parsed instruction (with label context) to a 10-trit machine code string.</summary>
    public static string Translate(ParsedInstruction instruction, IReadOnlyDictionary<string, LabelDefinition>? labels = null, IReadOnlyDictionary<string, InstructionPattern>? patterns = null)
    {
        patterns ??= Patterns;
        var mnemonic = instruction.Parts[0];
        var pattern = ResolvePattern(mnemonic, patterns)
            ?? throw new InvalidOperationException($"Unknown mnemonic '{mnemonic}' on line {instruction.LineNumber}.");

        var operands = instruction.Parts.Skip(1).ToList();
        if (operands.Count != pattern.AssemblyOperands.Count)
            throw new InvalidOperationException(
                $"Mnemonic '{mnemonic}' expects {pattern.AssemblyOperands.Count} operand(s) but received {operands.Count} on line {instruction.LineNumber}.");

        // Initialise fields with defaults (00)
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Rs1, DefaultField },
            { Rs2, DefaultField },
            { Rd1, DefaultField },
            { Rd2, DefaultField }
        };

        // Apply pattern-level defaults (e.g. Rd2="--" for SUB.T)
        if (pattern.Defaults != null)
            foreach (var (key, value) in pattern.Defaults)
                fields[key] = value;

        // Place assembly operands in declared order
        for (var i = 0; i < operands.Count; i++)
        {
            var fieldName = pattern.AssemblyOperands[i];
            // Imm uses the Rs2 slot in the encoding
            var targetField = string.Equals(fieldName, Imm, StringComparison.OrdinalIgnoreCase) ? Rs2 : fieldName;
            fields[targetField] = ParseOperand(operands[i], targetField, instruction.LineNumber, labels);
        }

        // Field layout: opcode Rs1 Rs2 Rd1 Rd2  (2 trits each = 10 total)
        return string.Concat(
            pattern.Opcode,
            fields[Rs1],
            fields[Rs2],
            fields[Rd1],
            fields[Rd2]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ParseOperand(string operand, string field, int lineNumber, IReadOnlyDictionary<string, LabelDefinition>? labels)
    {
        var token = operand.Trim();

        // Label reference → resolve to address in AddressSpace
        if (labels != null && labels.TryGetValue(token, out var label))
        {
            if (label.InstructionIndex is < 0 or >= PageInstructionCount)
                throw new InvalidOperationException($"Label '{token}' on line {lineNumber} resolved to invalid address {label.InstructionIndex}.");
            return AddressSpace[label.InstructionIndex];
        }

        // Register name (e.g. X1, X-4)
        if (RegisterDictionary.TryGetValue(token, out var registerValue))
            return registerValue;

        // Explicit trit pair (e.g. "++" or "0-")
        if (TryParseTritPair(token, out var explicitTrits))
            return explicitTrits;

        // Numeric immediate (-4..4)
        if (int.TryParse(token, out var numericValue))
            return ToBalancedTritPair(numericValue, lineNumber);

        throw new InvalidOperationException(
            $"Unable to parse operand '{operand}' for field '{field}' on line {lineNumber}. Unknown register, immediate value, or label.");
    }

    private static InstructionPattern? ResolvePattern(string mnemonic, IReadOnlyDictionary<string, InstructionPattern> patterns)
    {
        if (patterns.TryGetValue(mnemonic, out var pattern))
            return pattern;
        // Tolerate missing .T suffix
        return mnemonic.EndsWith(".T", StringComparison.OrdinalIgnoreCase)
            ? null
            : patterns.GetValueOrDefault($"{mnemonic}.T");
    }

    private static bool TryParseTritPair(string token, out string result)
    {
        if (token.Length == 2 && token.All(ch => ch is '+' or '-' or '0'))
        {
            result = token;
            return true;
        }
        result = string.Empty;
        return false;
    }

    private static string ToBalancedTritPair(int value, int lineNumber)
    {
        if (value is < -4 or > 4)
            throw new InvalidOperationException(
                $"Immediate value {value} is outside the 2-trit range (-4..4) on line {lineNumber}.");
        return AddressSpace[value + 4];
    }
}
