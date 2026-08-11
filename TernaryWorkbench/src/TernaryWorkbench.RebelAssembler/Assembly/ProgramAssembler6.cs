using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet6;

namespace TernaryWorkbench.RebelAssembler.Assembly;

/// <summary>
/// Section-aware REBEL-6 assembly: the directive set the conformance dialect needs
/// (<c>.text</c>, <c>.data</c>, <c>.word</c>, <c>.zero</c>) on top of the single-page
/// instruction encoder. <c>.text</c> holds instructions only (a normative ABI/ISA rule —
/// there are no literal pools), <c>.data</c> holds data only. Labels bind at their point
/// of definition to the current section's location counter: instruction index in
/// <c>.text</c>, tryte offset in <c>.data</c>.
/// </summary>
internal static class ProgramAssembler6
{
    private const int WordTrytes = 4;
    private const string ZeroTryte = "000000";

    private sealed record ParsedDatum(int LineNumber, string Text, string Directive, string Operand, int TryteOffset, int SizeTrytes);

    public static Rebel6Program Assemble(string assembly)
    {
        var (parsedInstructions, data, labels) = ParseProgram(assembly);

        // Flat pre-linker layout, matching the R2R reference toolchain: the data image sits
        // after the instruction image, whose tryte size (ceil(N x 32 / 6)) is rounded up to
        // the next 4-tryte boundary - strictly greater when already aligned, as R2R rounds.
        int instructionTrytes = (parsedInstructions.Count * InstructionWidth + 5) / 6;
        int dataBase = instructionTrytes + WordTrytes - (instructionTrytes % WordTrytes);

        var finalLabels = new Dictionary<string, LabelDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, label) in labels)
            finalLabels[name] = label.IsData
                ? label with { InstructionIndex = label.InstructionIndex + dataBase }
                : label;

        var instructions = new List<AssembledInstruction>(parsedInstructions.Count);
        foreach (var parsed in parsedInstructions)
        {
            var machineCode = InstructionEncoder6.Translate(parsed, finalLabels, Patterns, instructions.Count);
            instructions.Add(new AssembledInstruction(
                instructions.Count,
                InstructionSet6.AddressSpace[instructions.Count],
                parsed.Text,
                machineCode));
        }

        var assembledData = new List<AssembledDatum>(data.Count);
        foreach (var datum in data)
            assembledData.Add(EncodeDatum(datum, dataBase, finalLabels));

        return new Rebel6Program(instructions, assembledData, dataBase);
    }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    private static (List<ParsedInstruction> Instructions, List<ParsedDatum> Data, Dictionary<string, LabelDefinition> Labels)
        ParseProgram(string assembly)
    {
        var instructions = new List<ParsedInstruction>();
        var data = new List<ParsedDatum>();
        var labels = new Dictionary<string, LabelDefinition>(StringComparer.OrdinalIgnoreCase);
        bool inData = false;
        int dataOffset = 0;

        var lines = assembly.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = InstructionParser.StripComments(lines[i]).Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            while (true)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0)
                    break;

                var label = line[..colonIndex].Trim();
                if (string.IsNullOrWhiteSpace(label))
                    throw new InvalidOperationException($"Missing label name on line {lineNumber}.");
                if (!InstructionParser.IsValidLabel(label))
                    throw new InvalidOperationException(
                        $"Invalid label '{label}' on line {lineNumber}. Labels must start with a letter or underscore and contain only letters, digits, or underscores.");
                if (RegisterDictionary.ContainsKey(label))
                    throw new InvalidOperationException($"Label '{label}' on line {lineNumber} conflicts with a register name.");
                if (labels.TryGetValue(label, out var existing))
                    throw new InvalidOperationException($"Label '{label}' is already defined (first seen on line {existing.LineNumber}).");

                labels[label] = inData
                    ? new LabelDefinition(dataOffset, lineNumber, IsData: true)
                    : new LabelDefinition(instructions.Count, lineNumber);

                line = line[(colonIndex + 1)..].TrimStart();
                if (string.IsNullOrEmpty(line))
                    break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (parts[0].StartsWith('.'))
            {
                var directive = parts[0].ToLowerInvariant();
                switch (directive)
                {
                    case ".text":
                        RequireOperands(parts, 0, directive, lineNumber);
                        inData = false;
                        break;

                    case ".data":
                        RequireOperands(parts, 0, directive, lineNumber);
                        inData = true;
                        break;

                    case ".word":
                        RequireOperands(parts, 1, directive, lineNumber);
                        if (!inData)
                            throw new InvalidOperationException(
                                $"'.word' on line {lineNumber} is not allowed in .text: the section holds instructions only (docs/rebel6-isa.md, Memory map). Move data to .data.");
                        data.Add(new ParsedDatum(lineNumber, line, directive, parts[1], dataOffset, WordTrytes));
                        dataOffset += WordTrytes;
                        break;

                    case ".zero":
                        RequireOperands(parts, 1, directive, lineNumber);
                        if (!inData)
                            throw new InvalidOperationException(
                                $"'.zero' on line {lineNumber} is not allowed in .text: the section holds instructions only (docs/rebel6-isa.md, Memory map). Move data to .data.");
                        if (!int.TryParse(parts[1], out var zeroCount) || zeroCount <= 0)
                            throw new InvalidOperationException(
                                $"'.zero' on line {lineNumber} expects a positive tryte count, got '{parts[1]}'.");
                        data.Add(new ParsedDatum(lineNumber, line, directive, parts[1], dataOffset, zeroCount));
                        dataOffset += zeroCount;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported directive '{parts[0]}' on line {lineNumber}. Supported directives: .text, .data, .word, .zero.");
                }
                continue;
            }

            if (inData)
                throw new InvalidOperationException(
                    $"Instruction '{parts[0]}' on line {lineNumber} is not allowed in .data — instructions belong in .text.");

            instructions.Add(new ParsedInstruction(lineNumber, line, parts));
            if (instructions.Count > PageInstructionCount)
                throw new InvalidOperationException($"Cannot encode more than {PageInstructionCount} instructions in a single ROM page.");
        }

        return (instructions, data, labels);
    }

    private static void RequireOperands(string[] parts, int count, string directive, int lineNumber)
    {
        if (parts.Length != count + 1)
            throw new InvalidOperationException(
                $"'{directive}' on line {lineNumber} expects {count} operand(s) but received {parts.Length - 1}.");
    }

    // -------------------------------------------------------------------------
    // Data encoding
    // -------------------------------------------------------------------------

    private static AssembledDatum EncodeDatum(
        ParsedDatum datum, int dataBase, IReadOnlyDictionary<string, LabelDefinition> labels)
    {
        var address = dataBase + datum.TryteOffset;

        if (datum.Directive == ".zero")
            return new AssembledDatum(address, datum.Text, [.. Enumerable.Repeat(ZeroTryte, datum.SizeTrytes)]);

        long value;
        var token = datum.Operand.Trim();
        if (labels.TryGetValue(token, out var label))
        {
            // A code label yields its instruction index (I-space), a data label its tryte
            // address (D-space) - the ABS24_CODE / ABS24_DATA relocation split, resolved
            // flat pre-linker.
            value = label.InstructionIndex;
        }
        else if (!long.TryParse(token, out value))
        {
            throw new InvalidOperationException(
                $"Unable to parse '.word' operand '{datum.Operand}' on line {datum.LineNumber}. Expected a number or a label.");
        }

        string trits;
        try
        {
            trits = ToBalancedTernaryN(value, 24);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(
                $"'.word' value {value} on line {datum.LineNumber} is outside the 24-trit word range.");
        }

        // Little-endian tryte order: the lowest address holds the least significant tryte.
        return new AssembledDatum(address, datum.Text,
            [trits[18..24], trits[12..18], trits[6..12], trits[0..6]]);
    }
}
