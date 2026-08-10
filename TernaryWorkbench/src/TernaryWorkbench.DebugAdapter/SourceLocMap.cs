namespace TernaryWorkbench.DebugAdapter;

/// <summary>
/// The Layer 2 source map: "# .loc file:line" markers interleaved in a compiled
/// .tas by r6cc -g (REBEL-toolchain TasEmitter). A marker binds the .tas lines
/// after it to a C/C++ source position; composed with the .tasmap (Layer 1) this
/// gives address ↔ C-source mapping for compiled kernels.
/// </summary>
public sealed class SourceLocMap
{
    private readonly record struct Marker(int TasLine, string File, int SourceLine);

    private readonly List<Marker> _markers = new();

    public int Count => _markers.Count;

    public static SourceLocMap Load(string tasPath) => Parse(File.ReadLines(tasPath));

    public static SourceLocMap Parse(IEnumerable<string> tasLines)
    {
        var map = new SourceLocMap();
        int tasLine = 0;
        foreach (string raw in tasLines)
        {
            tasLine++;
            string text = raw.Trim();
            const string prefix = "# .loc ";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string spec = text[prefix.Length..].Trim();
            int colon = spec.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(spec[(colon + 1)..], out int line) || line < 1)
                continue;
            map._markers.Add(new Marker(tasLine, spec[..colon], line));
        }
        return map;
    }

    /// <summary>C/C++ source position governing a .tas line: the nearest marker above it.</summary>
    public (string File, int Line)? LocForTasLine(int tasLine)
    {
        (string, int)? best = null;
        foreach (Marker marker in _markers)
        {
            if (marker.TasLine > tasLine)
                break;
            best = (marker.File, marker.SourceLine);
        }
        return best;
    }

    /// <summary>
    /// Breakpoint placement for a C/C++ line: the first marker for that file at the
    /// requested line, else the lowest following line (slide-down, mirroring TasMap).
    /// Returns the marker's .tas line (the instructions follow it) and the source
    /// line actually bound.
    /// </summary>
    public (int TasLine, int SourceLine)? TasLineForSourceLine(string file, int line)
    {
        (int TasLine, int SourceLine)? best = null;
        foreach (Marker marker in _markers)
        {
            if (!SameFile(marker.File, file) || marker.SourceLine < line)
                continue;
            if (best is null ||
                marker.SourceLine < best.Value.SourceLine ||
                (marker.SourceLine == best.Value.SourceLine && marker.TasLine < best.Value.TasLine))
            {
                best = (marker.TasLine, marker.SourceLine);
            }
        }
        return best;
    }

    /// <summary>True when any marker refers to this file (is it a source of this program?).</summary>
    public bool CoversFile(string file) => _markers.Any(m => SameFile(m.File, file));

    private static bool SameFile(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}
