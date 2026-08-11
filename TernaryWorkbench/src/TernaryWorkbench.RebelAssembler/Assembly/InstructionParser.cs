using TernaryWorkbench.RebelAssembler.Assembly.Models;
using static TernaryWorkbench.RebelAssembler.Assembly.InstructionSet;

namespace TernaryWorkbench.RebelAssembler.Assembly;

internal static class InstructionParser
{
    /// <summary>
    /// Parse a page of assembly into instructions and labels. The page capacity and the
    /// register dictionary (used to reject labels that shadow register names) are ISA-specific,
    /// so callers supply them: REBEL-2/2v2 pass 9 + the 9-register dictionary, REBEL-6 passes
    /// 729 + the 729-register dictionary.
    /// </summary>
    public static ParsedPage ParsePage(
        string assembly,
        int maxInstructions,
        IReadOnlyDictionary<string, string> registerDictionary)
    {
        var instructions = new List<ParsedInstruction>(maxInstructions);
        var labels = new Dictionary<string, LabelDefinition>(StringComparer.OrdinalIgnoreCase);
        var pendingLabels = new List<(string Name, int LineNumber)>();
        var lines = assembly.Split(NewLineSeparators, StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = StripComments(lines[i]).Trim();
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
                if (!IsValidLabel(label))
                    throw new InvalidOperationException(
                        $"Invalid label '{label}' on line {lineNumber}. Labels must start with a letter or underscore and contain only letters, digits, or underscores.");
                if (registerDictionary.ContainsKey(label))
                    throw new InvalidOperationException($"Label '{label}' on line {lineNumber} conflicts with a register name.");

                if (labels.TryGetValue(label, out var existingLabel))
                    throw new InvalidOperationException($"Label '{label}' is already defined (first seen on line {existingLabel.LineNumber}).");
                var duplicatePending = pendingLabels.FirstOrDefault(l => string.Equals(l.Name, label, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(duplicatePending.Name))
                    throw new InvalidOperationException($"Label '{label}' is already defined (first seen on line {duplicatePending.LineNumber}).");

                pendingLabels.Add((label, lineNumber));
                line = line[(colonIndex + 1)..].TrimStart();
                if (string.IsNullOrEmpty(line))
                    break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            foreach (var (name, labelLine) in pendingLabels)
                labels[name] = new LabelDefinition(instructions.Count, labelLine);
            pendingLabels.Clear();

            var parts = line.Split(ArgumentSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new InvalidOperationException($"Missing mnemonic on line {lineNumber}.");

            instructions.Add(new ParsedInstruction(lineNumber, line, parts));
            if (instructions.Count > maxInstructions)
                throw new InvalidOperationException($"Cannot encode more than {maxInstructions} instructions in a single ROM page.");
        }

        if (pendingLabels.Count > 0)
        {
            var dangling = pendingLabels[0];
            throw new InvalidOperationException($"Label '{dangling.Name}' declared on line {dangling.LineNumber} is not attached to an instruction.");
        }

        return new ParsedPage(instructions, labels);
    }

    // -------------------------------------------------------------------------
    // Comment/annotation stripping
    // Mirrors MRCSStudio.Domain.Converters.TestStringConverter.StripComment
    // plus removal of "quoted" strings and (parenthesized) annotations.
    // -------------------------------------------------------------------------

    internal static string StripComments(string line)
    {
        // 1. Remove double-quoted spans (including the content between them)
        while (true)
        {
            var startQ = line.IndexOf('"');
            if (startQ < 0) break;
            var endQ = line.IndexOf('"', startQ + 1);
            line = endQ < 0
                ? line[..startQ]
                : line.Remove(startQ, endQ - startQ + 1);
        }

        // 2. Remove parenthesised annotations  (text)
        while (true)
        {
            var start = line.IndexOf('(');
            if (start < 0) break;
            var end = line.IndexOf(')', start + 1);
            line = end < 0 ? line[..start] : line.Remove(start, end - start + 1);
        }

        // 3. Strip trailing line comments: #  ;  $  //
        var hashIndex      = line.IndexOf('#');
        var semicolonIndex = line.IndexOf(';');
        var dollarIndex    = line.IndexOf('$');
        var slashIndex     = line.IndexOf("//", StringComparison.Ordinal);

        var first = new[] { hashIndex, semicolonIndex, dollarIndex, slashIndex }
            .Where(idx => idx >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        return first >= 0 ? line[..first] : line;
    }

    internal static bool IsValidLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        if (!(char.IsLetter(label[0]) || label[0] == '_')) return false;
        for (var i = 1; i < label.Length; i++)
        {
            var ch = label[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
        }
        return true;
    }
}
