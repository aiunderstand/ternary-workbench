using System.Text;
using TernaryWorkbench.RebelAssembler.Assembly;

namespace TernaryWorkbench.RebelAssembler;

/// <summary>
/// Serialises <see cref="AssemblyRecord"/> collections to the TernaryWorkbench
/// assembler CSV exchange format and deserialises them back.
/// </summary>
/// <remarks>
/// <para><b>Format specification</b></para>
/// <list type="bullet">
///   <item>Field delimiter: <c>;</c></item>
///   <item>Line ending: <c>\n</c> (CRLF accepted on input)</item>
///   <item>Header row: <c>assembly;machine code;isa;direction</c></item>
///   <item><c>direction</c>: <c>assemble</c> or <c>disassemble</c></item>
///   <item>Fields containing <c>;</c> or <c>"</c> are double-quote escaped (RFC 4180).</item>
/// </list>
/// </remarks>
public static class AssemblyCsvSerializer
{
    /// <summary>Header line expected in every file produced or accepted by this serialiser.</summary>
    public const string Header = "assembly;machine code;isa;direction";

    // -------------------------------------------------------------------------
    // Serialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serialises a sequence of <see cref="AssemblyRecord"/>s to a CSV string.
    /// </summary>
    public static string Serialize(IEnumerable<AssemblyRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);

        foreach (var r in records)
        {
            sb.Append(EscapeField(r.Assembly));
            sb.Append(';');
            sb.Append(EscapeField(r.MachineCode));
            sb.Append(';');
            sb.Append(EscapeField(r.Isa));
            sb.Append(';');
            sb.Append(r.Direction == AssemblyDirection.Assemble ? "assemble" : "disassemble");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Deserialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a CSV string produced by <see cref="Serialize"/> back into records.
    /// </summary>
    /// <remarks>
    /// The <c>machine code</c> column is re-validated (disassembled) on import
    /// to verify integrity; the <c>assembly</c> column is stored as-is.
    /// </remarks>
    public static (List<AssemblyRecord> ValidRows, List<CsvParseError> Errors) Deserialize(string csv)
    {
        var validRows = new List<AssemblyRecord>();
        var errors    = new List<CsvParseError>();

        var lines = csv.Replace("\r\n", "\n").Split('\n');
        int fileLineNumber = 0;
        bool headerSeen = false;

        foreach (var rawLine in lines)
        {
            fileLineNumber++;

            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            if (!headerSeen)
            {
                if (!string.Equals(rawLine.Trim(), Header, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new CsvParseError(
                        fileLineNumber,
                        rawLine,
                        $"Expected header \"{Header}\" but got \"{rawLine.Trim()}\"."));
                    return (validRows, errors);
                }
                headerSeen = true;
                continue;
            }

            var fields = SplitFields(rawLine);
            if (fields.Length != 4)
            {
                errors.Add(new CsvParseError(
                    fileLineNumber,
                    rawLine,
                    $"Expected 4 semicolon-separated fields, found {fields.Length}."));
                continue;
            }

            var assemblyField  = fields[0].Trim();
            var machineField   = fields[1].Trim();
            var isaField       = fields[2].Trim();
            var directionField = fields[3].Trim();

            // Validate ISA
            if (!string.Equals(isaField, Isa.Rebel2,   StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(isaField, Isa.Rebel2v2, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(isaField, Isa.Rebel6,   StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new CsvParseError(
                    fileLineNumber,
                    rawLine,
                    $"Unknown ISA \"{isaField}\". Expected \"{Isa.Rebel2}\", \"{Isa.Rebel2v2}\", or \"{Isa.Rebel6}\"."));
                continue;
            }

            // Validate direction
            AssemblyDirection direction;
            if (directionField.Equals("assemble", StringComparison.OrdinalIgnoreCase))
                direction = AssemblyDirection.Assemble;
            else if (directionField.Equals("disassemble", StringComparison.OrdinalIgnoreCase))
                direction = AssemblyDirection.Disassemble;
            else
            {
                errors.Add(new CsvParseError(
                    fileLineNumber,
                    rawLine,
                    $"Direction must be \"assemble\" or \"disassemble\", got \"{directionField}\"."));
                continue;
            }

            // Validate machine code format and opcode recognition (REBEL-2 and REBEL-2v2)
            if (string.Equals(isaField, Isa.Rebel2,   StringComparison.OrdinalIgnoreCase) ||
                string.Equals(isaField, Isa.Rebel2v2, StringComparison.OrdinalIgnoreCase))
            {
                if (machineField.Length != 10 || !machineField.All(ch => ch is '+' or '-' or '0'))
                {
                    errors.Add(new CsvParseError(
                        fileLineNumber,
                        rawLine,
                        $"Machine code \"{machineField}\" is not a valid 10-trit {isaField} instruction."));
                    continue;
                }

                // Verify the opcode is recognised by attempting a disassembly
                try
                {
                    var patterns = string.Equals(isaField, Isa.Rebel2v2, StringComparison.OrdinalIgnoreCase)
                        ? InstructionSet2v2.Patterns
                        : null; // null → defaults to v1 patterns inside Disassemble
                    InstructionDisassembler.Disassemble(machineField, patterns);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(new CsvParseError(
                        fileLineNumber,
                        rawLine,
                        $"Machine code \"{machineField}\" could not be disassembled: {ex.Message}"));
                    continue;
                }
            }

            // Validate machine code format and opcode recognition (REBEL-6: 32 trits)
            if (string.Equals(isaField, Isa.Rebel6, StringComparison.OrdinalIgnoreCase))
            {
                if (machineField.Length != 32 || !machineField.All(ch => ch is '+' or '-' or '0'))
                {
                    errors.Add(new CsvParseError(
                        fileLineNumber,
                        rawLine,
                        $"Machine code \"{machineField}\" is not a valid 32-trit {isaField} instruction."));
                    continue;
                }

                try
                {
                    InstructionDisassembler6.Disassemble(machineField, InstructionSet6.Patterns);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(new CsvParseError(
                        fileLineNumber,
                        rawLine,
                        $"Machine code \"{machineField}\" could not be disassembled: {ex.Message}"));
                    continue;
                }
            }

            validRows.Add(new AssemblyRecord(assemblyField, machineField, isaField, direction));
        }

        return (validRows, errors);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string[] SplitFields(string line)
    {
        // Simple split on ';'; proper RFC-4180 quoted-field support is not needed
        // because our fields never contain embedded newlines.
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip second quote
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                    inQuotes = true;
                else if (ch == ';')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                    current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return [.. result];
    }

    private static string EscapeField(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

/// <summary>Describes a single parse failure for one CSV data row.</summary>
/// <param name="RowNumber">1-based row number in the CSV file (header = row 1).</param>
/// <param name="RawLine">The raw text of the offending line.</param>
/// <param name="Message">Human-readable description of the parse failure.</param>
public record CsvParseError(int RowNumber, string RawLine, string Message);
