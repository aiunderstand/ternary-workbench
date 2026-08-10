using FluentAssertions;
using TernaryWorkbench.DebugAdapter;
using Xunit;

namespace TernaryWorkbench.Tests;

public class TasMapTests
{
    private static readonly TasMap Map = TasMap.Parse(new[]
    {
        "0\t6",
        "1\t7",
        "2\t10",
        "3\t10",
        "4\t15",
    });

    [Fact]
    public void ParsesEntries()
    {
        Map.Count.Should().Be(5);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 7)]
    [InlineData(3, 10)]
    public void LineForAddress_ExactHit(long address, int line)
    {
        Map.LineForAddress(address).Should().Be(line);
    }

    [Fact]
    public void LineForAddress_PastEnd_UsesNearestBelow()
    {
        Map.LineForAddress(99).Should().Be(15);
    }

    [Fact]
    public void LineForAddress_BeforeFirst_IsNull()
    {
        Map.LineForAddress(-1).Should().BeNull();
    }

    [Fact]
    public void AddressForLine_ExactHit()
    {
        Map.AddressForLine(10).Should().Be((2L, 10));
    }

    [Fact]
    public void AddressForLine_CommentLine_SlidesToNextInstruction()
    {
        // Line 8 has no instruction; the breakpoint lands on line 10's first slot.
        Map.AddressForLine(8).Should().Be((2L, 10));
    }

    [Fact]
    public void AddressForLine_PastLastInstruction_IsNull()
    {
        Map.AddressForLine(16).Should().BeNull();
    }

    [Fact]
    public void MalformedEntry_Throws()
    {
        var act = () => TasMap.Parse(new[] { "0 6" });
        act.Should().Throw<FormatException>();
    }
}
