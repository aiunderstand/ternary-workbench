using System.Numerics;
using TernaryWorkbench.Core;

namespace TernaryWorkbench.DebugAdapter;

public enum RegisterFormat
{
    Trits,
    Decimal,
    Hex,
}

/// <summary>
/// Balanced-ternary presentation of the 64-bit sign-extended values the debug wire
/// carries (rebel6-debug.md "Data representation": the wire carries integers, tools
/// render trits). REBEL-6 registers are 24 trits wide.
/// </summary>
public static class TritFormat
{
    public const int WordSizeTrits = 24;

    private static readonly OutputOptions WordOptions = new() { FixedOutputLength = WordSizeTrits };

    /// <summary>MST-first 24-trit rendering, e.g. "00000000000000000000+++0".</summary>
    public static string ToTrits(long value) =>
        RadixConverter.Format(new BigInteger(value), Radix.Base3Balanced, WordOptions);

    public static string Render(long value, RegisterFormat format) => format switch
    {
        RegisterFormat.Trits => $"{ToTrits(value)} = {value}",
        RegisterFormat.Hex => value < 0 ? $"-0x{-value:x}" : $"0x{value:x}",
        _ => value.ToString(),
    };

    /// <summary>
    /// Parses a user-entered register value: a balanced trit string (+0- characters),
    /// a 0x-prefixed hex value, or a decimal value.
    /// </summary>
    public static bool TryParseValue(string text, out long value)
    {
        value = 0;
        text = text.Trim();
        if (text.Length == 0)
            return false;

        if (text.Length > 1 && text.IndexOfAny(['+', '-'], text.StartsWith('-') ? 1 : 0) >= 0 ||
            text.StartsWith('+'))
        {
            // Trit strings are the only form with '+' or an interior sign character.
            foreach (char c in text)
            {
                if (c is not ('+' or '0' or '-'))
                    return false;
            }
            BigInteger parsed = RadixConverter.Parse(text, Radix.Base3Balanced);
            if (parsed < long.MinValue || parsed > long.MaxValue)
                return false;
            value = (long)parsed;
            return true;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        if (text.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(text[3..], System.Globalization.NumberStyles.HexNumber, null, out value))
                return false;
            value = -value;
            return true;
        }

        return long.TryParse(text, out value);
    }
}
