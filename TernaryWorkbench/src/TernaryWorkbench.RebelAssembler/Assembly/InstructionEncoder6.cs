using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionEncoder6
{
    /// <summary>Translate a single-instruction string to a 32-trit machine code string.</summary>
    public static string Translate(string instruction, IReadOnlyDictionary<string, InstructionPattern>? patterns = null)
    {
        var parsed = InstructionParser.ParsePage(instruction, PageInstructionCount, RegisterDictionary);
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
            var fieldName = pattern.AssemblyOperands[i];

            if (WideFieldSlots(fieldName) is { } wide)
            {
                var value12 = ParseWide12(
                    operands[i], Displacement12Max, instruction.LineNumber, labels, currentIndex);
                fields[wide.Hi] = value12[0..6];
                fields[wide.Lo] = value12[6..12];
                continue;
            }

            if (IsShamtField(fieldName))
            {
                fields[Rd2] = ParseShamt(operands[i], instruction.LineNumber);
                continue;
            }

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
            .Select(slot => ResolveTemplateSlot(slot, operands, lineNumber))
            .ToList();

        operands.Clear();
        operands.AddRange(expanded);
        mnemonic = expansion.Target;
    }

    /// <summary>
    /// Resolves one pseudo-expansion template entry: <c>$n</c> copies source operand n, <c>$-n</c>
    /// copies it negated (numeric operands flip sign; trit strings invert tritwise), anything else
    /// is a literal operand.
    /// </summary>
    private static string ResolveTemplateSlot(string slot, IReadOnlyList<string> operands, int lineNumber)
    {
        if (!slot.StartsWith('$'))
            return slot;

        bool negate = slot.Length > 1 && slot[1] == '-';
        var operand = operands[int.Parse(slot[(negate ? 2 : 1)..])];
        return negate ? NegateOperand(operand, lineNumber) : operand;
    }

    /// <summary>Negates a numeric or trit-string operand during pseudo-expansion (errata E-4).</summary>
    private static string NegateOperand(string operand, int lineNumber)
    {
        var token = operand.Trim();

        if (int.TryParse(token, out var value))
            return (-value).ToString();

        if (token.Length > 0 && token.All(ch => ch is '+' or '-' or '0'))
            return new string([.. token.Select(ch => ch == '+' ? '-' : ch == '-' ? '+' : '0')]);

        throw new InvalidOperationException(
            $"Cannot negate operand '{operand}' during pseudo-instruction expansion on line {lineNumber}. Expected a number or a trit string.");
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

        // JAL.T (+-) and AIPC.T (0-) are PC-relative by definition (R_REBEL6_PCREL24);
        // LI.T, LWA.T and SWA.T take a symbol's absolute value (R_REBEL6_ABS24_*).
        bool pcRelative = pattern.Opcode is "+-" or "0-";

        if (hasDestReg)
        {
            // G-type: imm[23:12](12) | rd1(6) | imm[11:0](12) | opc(2)
            var rd1Trits = ParseRegisterOrTrit(operands[0], lineNumber);
            var immTok   = operands[1];
            var imm24    = ParseLongImmediate(immTok, 24, lineNumber, labels, currentIndex, pcRelative);
            return imm24[0..12] + rd1Trits + imm24[12..24] + pattern.Opcode;
        }
        else if (hasSrcReg)
        {
            // Y-type: rs1(6) | imm[23:0](24) | opc(2)
            var rs1Trits = ParseRegisterOrTrit(operands[0], lineNumber);
            var immTok   = operands[1];
            var imm24    = ParseLongImmediate(immTok, 24, lineNumber, labels, currentIndex, pcRelative);
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

    /// <summary>
    /// Parses a shift amount into the 6-trit rd2 slot, range-checked to 4 trits. The fill selector
    /// lives in the rs2 slot and is supplied by the instruction pattern, not by an operand.
    /// </summary>
    private static string ParseShamt(string operand, int lineNumber)
    {
        var token = operand.Trim();

        if (TryParseTritString(token, 6, out var trits))
            return trits;

        if (int.TryParse(token, out var value))
        {
            if (value < -Shamt4Max || value > Shamt4Max)
                throw new InvalidOperationException(
                    $"Shift amount {value} is outside the 4-trit range (-{Shamt4Max}..{Shamt4Max}) on line {lineNumber}.");
            return ToBalancedTernaryN(value, 6);
        }

        throw new InvalidOperationException(
            $"Unable to parse shift amount '{operand}' on line {lineNumber}. Expected a number or a 6-trit string.");
    }

    /// <summary>
    /// Parses a 12-trit immediate or PC-relative displacement, returning it MST-first for the
    /// caller to split across its two slots. Accepts a label, a 12-trit string, or a number.
    /// </summary>
    private static string ParseWide12(
        string operand, int max, int lineNumber,
        IReadOnlyDictionary<string, LabelDefinition>? labels, int currentIndex)
    {
        var token = operand.Trim();

        // CSR names (Zicsr): resolve before label lookup so `csrrw X5, mstatus, X6`
        // works without a label named mstatus ever shadowing it.
        if (CsrNames.TryGetValue(token, out var csrNumber))
            return ToBalancedTernaryN(csrNumber, 12);

        if (labels != null && labels.TryGetValue(token, out var label))
        {
            // Code label: PC-relative displacement (PCREL12). Data label: absolute tryte
            // address (DISP12 — a constant load/store displacement).
            int labelValue = label.IsData ? label.InstructionIndex : label.InstructionIndex - currentIndex;
            if (labelValue < -max || labelValue > max)
                throw new InvalidOperationException(
                    $"Reference to label '{token}' on line {lineNumber} produces value {labelValue} which is outside the permitted range (-{max}..{max}).");
            return ToBalancedTernaryN(labelValue, 12);
        }

        if (TryParseTritString(token, 12, out var trits))
            return trits;

        if (int.TryParse(token, out var value))
        {
            if (value < -max || value > max)
                throw new InvalidOperationException(
                    $"Value {value} is outside the permitted range (-{max}..{max}) on line {lineNumber}.");
            return ToBalancedTernaryN(value, 12);
        }

        throw new InvalidOperationException(
            $"Unable to parse 12-trit immediate '{operand}' on line {lineNumber}. Expected a label, a 12-trit string, or a number.");
    }

    private static string ParseOperand(
        string operand, string field, int lineNumber,
        IReadOnlyDictionary<string, LabelDefinition>? labels, int currentIndex,
        bool isBranchOffset)
    {
        var token = operand.Trim();

        // Label reference
        if (labels != null && labels.TryGetValue(token, out var label))
        {
            if (label.IsData)
                throw new InvalidOperationException(
                    $"Data symbol '{token}' on line {lineNumber} does not fit a 6-trit register/offset field. Load its address with LI.T, or address it absolutely with LWA.T/SWA.T.");
            if (isBranchOffset)
            {
                // PC-relative: offset = target_index - current_index
                int offset = label.InstructionIndex - currentIndex;
                if (offset < -Displacement6Max || offset > Displacement6Max)
                    throw new InvalidOperationException(
                        $"Branch to label '{token}' on line {lineNumber} produces offset {offset} which is outside the 6-trit range (-{Displacement6Max}..{Displacement6Max}).");
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
            if (numericValue < -Displacement6Max || numericValue > Displacement6Max)
                throw new InvalidOperationException(
                    $"Immediate {numericValue} is outside the 6-trit range (-{Displacement6Max}..{Displacement6Max}) on line {lineNumber}.");
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
        IReadOnlyDictionary<string, LabelDefinition>? labels, int currentIndex,
        bool pcRelative = false)
    {
        token = token.Trim();

        if (labels != null && labels.TryGetValue(token, out var label))
        {
            if (pcRelative)
            {
                if (label.IsData)
                    throw new InvalidOperationException(
                        $"PC-relative reference to data symbol '{token}' on line {lineNumber}: under the Harvard split, PC-relative addressing reaches code symbols only (docs/rebel6-isa.md, Linking).");
                return ToBalancedTernaryN(label.InstructionIndex - currentIndex, width);
            }
            // Absolute: a code label yields its instruction index (I-space), a data label
            // its tryte address (D-space) — the ABS24_CODE / ABS24_DATA split.
            return ToBalancedTernaryN(label.InstructionIndex, width);
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
            return ToBalancedTernaryN(numericValue, width);
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
