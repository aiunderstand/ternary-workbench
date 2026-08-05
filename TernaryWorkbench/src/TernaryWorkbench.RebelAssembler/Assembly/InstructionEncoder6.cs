using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionEncoder6
{
    /// <summary>Translate a single-instruction string to a 32-trit machine code string.</summary>
    public static string Translate(string instruction, IReadOnlyDictionary<string, InstructionPattern>? patterns = null)
    {
        var parsed = InstructionParser.ParsePage(instruction);
        if (parsed.Instructions.Count != 1)
            throw new InvalidOperationException("Translate expects exactly one instruction.");
        return Translate(parsed.Instructions[0], parsed.Labels, patterns);
    }

    /// <summary>Translate a parsed instruction to a 32-trit machine code string.</summary>
    public static string Translate(
        ParsedInstruction instruction,
        IReadOnlyDictionary<string, LabelDefinition>? labels = null,
        IReadOnlyDictionary<string, InstructionPattern>? patterns = null,
        int currentIndex = 0)
    {
        patterns ??= Patterns;
        var mnemonic = instruction.Parts[0];
        var operands = instruction.Parts.Skip(1).ToList();

        ExpandPseudo(ref mnemonic, operands, instruction.LineNumber);

        var pattern = ResolvePattern(mnemonic, patterns)
            ?? throw new InvalidOperationException($"Unknown mnemonic '{mnemonic}' on line {instruction.LineNumber}.");

        if (operands.Count != pattern.AssemblyOperands.Count)
            throw new InvalidOperationException(
                $"Mnemonic '{mnemonic}' expects {pattern.AssemblyOperands.Count} operand(s) but received {operands.Count} on line {instruction.LineNumber}.");

        // ----------------------------------------------------------------
        // Long-immediate (G/Y) encoding: 2-trit opcode
        // ----------------------------------------------------------------
        if (pattern.Opcode.Length == 2)
            return EncodeLongImmediate(pattern, operands, instruction.LineNumber, labels, currentIndex);

        // ----------------------------------------------------------------
        // Standard encoding: 4-trit opcode
        // Layout: rs1(6) | rs2(6) | rd1(6) | rd2(6) | func(4) | opcode(4)
        // ----------------------------------------------------------------
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Rs1,  DefaultField },
            { Rs2,  DefaultField },
            { Rd1,  DefaultField },
            { Rd2,  DefaultField },
            { Func, DefaultFunc  },
        };

        if (pattern.Defaults != null)
            foreach (var (key, value) in pattern.Defaults)
                fields[key] = value;

        for (var i = 0; i < operands.Count; i++)
        {
            var fieldName   = pattern.AssemblyOperands[i];
            var targetField = MapFieldToSlot(fieldName);
            fields[targetField] = ParseOperand(
                operands[i], targetField, instruction.LineNumber, labels, currentIndex,
                isBranchOffset: IsOffsetField(fieldName));
        }

        return string.Concat(
            fields[Rs1],
            fields[Rs2],
            fields[Rd1],
            fields[Rd2],
            fields[Func],
            pattern.Opcode);
    }

    // -------------------------------------------------------------------------
    // Pseudo-instruction expansion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rewrites a pseudo-instruction into its architectural form, e.g.
    /// <c>BNE.T rs1, rs2, off</c> → <c>BCGS.T rs1, rs2, off, off</c>. No-op for mnemonics that
    /// are not pseudo-instructions.
    /// </summary>
    private static void ExpandPseudo(ref string mnemonic, List<string> operands, int lineNumber)
    {
        if (!PseudoExpansions.TryGetValue(mnemonic, out var expansion)
            && !PseudoExpansions.TryGetValue($"{mnemonic}.T", out expansion))
            return;

        if (operands.Count != expansion.OperandCount)
            throw new InvalidOperationException(
                $"Mnemonic '{mnemonic}' expects {expansion.OperandCount} operand(s) but received {operands.Count} on line {lineNumber}.");

        var expanded = expansion.Template
            .Select(slot => slot.StartsWith('$') ? operands[int.Parse(slot[1..])] : slot)
            .ToList();

        operands.Clear();
        operands.AddRange(expanded);
        mnemonic = expansion.Target;
    }

    // -------------------------------------------------------------------------
    // Long-immediate encoding
    // -------------------------------------------------------------------------

    private static string EncodeLongImmediate(
        InstructionPattern pattern,
        List<string> operands,
        int lineNumber,
        IReadOnlyDictionary<string, LabelDefinition>? labels,
        int currentIndex)
    {
        bool hasDestReg = pattern.AssemblyOperands.Count > 0
            && string.Equals(pattern.AssemblyOperands[0], Rd1, StringComparison.OrdinalIgnoreCase);
        bool hasSrcReg = pattern.AssemblyOperands.Count > 0
            && string.Equals(pattern.AssemblyOperands[0], Rs1, StringComparison.OrdinalIgnoreCase);

        if (hasDestReg)
        {
            // G-type: imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
            var rd1Trits = ParseRegisterOrTrit(operands[0], lineNumber);
            var immTok   = operands[1];
            var imm24    = ParseLongImmediate(immTok, 24, lineNumber, labels, currentIndex);
            return imm24[0..12] + rd1Trits + imm24[12..24] + pattern.Opcode;
        }
        else if (hasSrcReg)
        {
            // Y-type: rs1(6) | imm[23:0](24) | opc(2)
            var rs1Trits = ParseRegisterOrTrit(operands[0], lineNumber);
            var immTok   = operands[1];
            var imm24    = ParseLongImmediate(immTok, 24, lineNumber, labels, currentIndex);
            return rs1Trits + imm24 + pattern.Opcode;
        }
        else
        {
            throw new InvalidOperationException(
                $"G/Y-type instruction '{pattern.Mnemonic}' has unexpected operand layout on line {lineNumber}.");
        }
    }

    // -------------------------------------------------------------------------
    // Operand parsing
    // -------------------------------------------------------------------------

    private static string ParseOperand(
        string operand, string field, int lineNumber,
        IReadOnlyDictionary<string, LabelDefinition>? labels, int currentIndex,
        bool isBranchOffset)
    {
        var token = operand.Trim();

        // Label reference
        if (labels != null && labels.TryGetValue(token, out var label))
        {
            if (isBranchOffset)
            {
                // PC-relative: offset = target_index - current_index
                int offset = label.InstructionIndex - currentIndex;
                if (offset < -364 || offset > 364)
                    throw new InvalidOperationException(
                        $"Branch to label '{token}' on line {lineNumber} produces offset {offset} which is outside the 6-trit range (-364..364).");
                return ToBalancedTernaryN(offset, 6);
            }
            else
            {
                // Absolute address (for non-branch label use, e.g., immediate load)
                if (label.InstructionIndex < 0 || label.InstructionIndex >= PageInstructionCount)
                    throw new InvalidOperationException($"Label '{token}' on line {lineNumber} has invalid instruction index {label.InstructionIndex}.");
                return AddressSpace[label.InstructionIndex];
            }
        }

        // Register name
        if (RegisterDictionary.TryGetValue(token, out var regValue))
            return regValue;

        // Explicit 6-trit string (e.g. "000++-")
        if (TryParseTritString(token, 6, out var tritStr6))
            return tritStr6;

        // Explicit 4-trit string for func field
        if (string.Equals(field, Func, StringComparison.OrdinalIgnoreCase)
            && TryParseTritString(token, 4, out var tritStr4))
            return tritStr4;

        // Numeric immediate → 6-trit balanced ternary
        if (int.TryParse(token, out var numericValue))
        {
            if (numericValue < -364 || numericValue > 364)
                throw new InvalidOperationException(
                    $"Immediate {numericValue} is outside the 6-trit range (-364..364) on line {lineNumber}.");
            return ToBalancedTernaryN(numericValue, 6);
        }

        throw new InvalidOperationException(
            $"Unable to parse operand '{operand}' for field '{field}' on line {lineNumber}. Unknown register, immediate value, or label.");
    }

    private static string ParseRegisterOrTrit(string operand, int lineNumber)
    {
        var token = operand.Trim();
        if (RegisterDictionary.TryGetValue(token, out var regValue))
            return regValue;
        if (TryParseTritString(token, 6, out var trits))
            return trits;
        if (int.TryParse(token, out var n))
            return ToBalancedTernaryN(n, 6);
        throw new InvalidOperationException($"Cannot parse register or trit-string '{operand}' on line {lineNumber}.");
    }

    private static string ParseLongImmediate(
        string token, int width, int lineNumber,
        IReadOnlyDictionary<string, LabelDefinition>? labels, int currentIndex)
    {
        token = token.Trim();

        if (labels != null && labels.TryGetValue(token, out var label))
        {
            // PC-relative offset for jal.t, absolute index for others — use raw index as value
            int val = label.InstructionIndex - currentIndex;
            return ToBalancedTernaryN(val, width);
        }

        if (TryParseTritString(token, width, out var trits))
            return trits;

        if (long.TryParse(token, out var numericValue))
        {
            // Range check: -(3^width-1)/2 .. +(3^width-1)/2
            long maxVal = (long)((Math.Pow(3, width) - 1) / 2);
            if (numericValue < -maxVal || numericValue > maxVal)
                throw new InvalidOperationException(
                    $"Immediate {numericValue} is outside the {width}-trit range on line {lineNumber}.");
            return ToBalancedTernaryN((int)numericValue, width);
        }

        throw new InvalidOperationException(
            $"Cannot parse {width}-trit immediate '{token}' on line {lineNumber}.");
    }

    private static bool TryParseTritString(string token, int expectedLength, out string result)
    {
        if (token.Length == expectedLength && token.All(ch => ch is '+' or '-' or '0'))
        {
            result = token;
            return true;
        }
        result = string.Empty;
        return false;
    }

    private static InstructionPattern? ResolvePattern(
        string mnemonic, IReadOnlyDictionary<string, InstructionPattern> patterns)
    {
        if (patterns.TryGetValue(mnemonic, out var pattern))
            return pattern;
        // Tolerate missing .T suffix
        return mnemonic.EndsWith(".T", StringComparison.OrdinalIgnoreCase)
            ? null
            : patterns.GetValueOrDefault($"{mnemonic}.T");
    }
}
