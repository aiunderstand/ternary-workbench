namespace TernaryWorkbench.DebugAdapter;

/// <summary>
/// The Layer 1 source map (rebel6-debug.md tooling appendix): "address&lt;TAB&gt;line"
/// per instruction, emitted by the R2R assembler as &lt;prog&gt;.tasmap when run with
/// --debug-port. Addresses are REBEL-6 instruction-slot indices (the PC values the
/// RSP stub reports); lines are 1-based .tas source lines.
/// </summary>
public sealed class TasMap
{
    private readonly SortedDictionary<long, int> _lineByAddress = new();
    private readonly SortedDictionary<int, long> _firstAddressByLine = new();

    public int Count => _lineByAddress.Count;

    public static TasMap Load(string path) => Parse(File.ReadLines(path));

    public static TasMap Parse(IEnumerable<string> lines)
    {
        var map = new TasMap();
        foreach (string raw in lines)
        {
            string text = raw.Trim();
            if (text.Length == 0)
                continue;
            string[] parts = text.Split('\t');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], out long address) ||
                !int.TryParse(parts[1], out int line) ||
                line < 1)
            {
                throw new FormatException($"Malformed .tasmap entry: \"{raw}\"");
            }
            map._lineByAddress[address] = line;
            if (!map._firstAddressByLine.ContainsKey(line))
                map._firstAddressByLine[line] = address;
        }
        return map;
    }

    /// <summary>Source line for a PC: exact entry, else the nearest mapped address below it.</summary>
    public int? LineForAddress(long address)
    {
        int? best = null;
        foreach (var (addr, line) in _lineByAddress)
        {
            if (addr > address)
                break;
            best = line;
        }
        return best;
    }

    /// <summary>
    /// Breakpoint placement for a requested line: the first instruction on that line,
    /// else the first instruction on the nearest following mapped line (a breakpoint on
    /// a comment or label slides down to the next executable line).
    /// </summary>
    public (long Address, int Line)? AddressForLine(int line)
    {
        foreach (var (mappedLine, address) in _firstAddressByLine)
        {
            if (mappedLine >= line)
                return (address, mappedLine);
        }
        return null;
    }
}
