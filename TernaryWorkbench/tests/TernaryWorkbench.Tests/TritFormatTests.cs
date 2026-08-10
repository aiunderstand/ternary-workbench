using FluentAssertions;
using TernaryWorkbench.DebugAdapter;
using Xunit;

namespace TernaryWorkbench.Tests;

public class TritFormatTests
{
    [Fact]
    public void ToTrits_RendersMstFirst24Trits()
    {
        TritFormat.ToTrits(0).Should().Be(new string('0', 24));
        TritFormat.ToTrits(1).Should().Be(new string('0', 23) + "+");
        TritFormat.ToTrits(-1).Should().Be(new string('0', 23) + "-");
        TritFormat.ToTrits(4).Should().Be(new string('0', 22) + "++");
        TritFormat.ToTrits(-4).Should().Be(new string('0', 22) + "--");
    }

    [Fact]
    public void ToTrits_MaxWord()
    {
        // (3^24 - 1) / 2: all 24 trits at +
        TritFormat.ToTrits(141_214_768_240).Should().Be(new string('+', 24));
        TritFormat.ToTrits(-141_214_768_240).Should().Be(new string('-', 24));
    }

    [Fact]
    public void Render_TritsFormat_ShowsTritsAndDecimal()
    {
        TritFormat.Render(4, RegisterFormat.Trits).Should().Be(new string('0', 22) + "++ = 4");
    }

    [Fact]
    public void Render_DecimalAndHex()
    {
        TritFormat.Render(-42, RegisterFormat.Decimal).Should().Be("-42");
        TritFormat.Render(255, RegisterFormat.Hex).Should().Be("0xff");
        TritFormat.Render(-255, RegisterFormat.Hex).Should().Be("-0xff");
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-42", -42)]
    [InlineData("0x2a", 42)]
    [InlineData("-0x2a", -42)]
    [InlineData("++", 4)]
    [InlineData("--", -4)]
    [InlineData("+-0", 6)]
    [InlineData("0000000000000000000000++", 4)]
    public void TryParseValue_AcceptedForms(string text, long expected)
    {
        TritFormat.TryParseValue(text, out long value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("+2-")]
    [InlineData("12+3")]
    public void TryParseValue_RejectedForms(string text)
    {
        TritFormat.TryParseValue(text, out _).Should().BeFalse();
    }
}
