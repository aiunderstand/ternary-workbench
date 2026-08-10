using FluentAssertions;
using TernaryWorkbench.DebugAdapter;
using Xunit;

namespace TernaryWorkbench.Tests;

public class SourceLocMapTests
{
    private static readonly SourceLocMap Map = SourceLocMap.Parse(new[]
    {
        "\t.text",                     // tas line 1
        "main:",                       // 2
        "\t# .loc /src/kern.cpp:10",   // 3
        "\tli.t a0, 1",                // 4
        "\tadd.t t0, a0, a0",          // 5
        "\t# .loc /src/kern.cpp:12",   // 6
        "\tsub.t t1, t0, a0",          // 7
        "\t# .loc /src/other.hpp:99",  // 8
        "\tmv.t a0, t1",               // 9
    });

    [Fact]
    public void ParsesMarkers()
    {
        Map.Count.Should().Be(3);
    }

    [Fact]
    public void LocForTasLine_BindsFollowingLines()
    {
        Map.LocForTasLine(4).Should().Be(("/src/kern.cpp", 10));
        Map.LocForTasLine(5).Should().Be(("/src/kern.cpp", 10));
        Map.LocForTasLine(7).Should().Be(("/src/kern.cpp", 12));
        Map.LocForTasLine(9).Should().Be(("/src/other.hpp", 99));
    }

    [Fact]
    public void LocForTasLine_BeforeFirstMarker_IsNull()
    {
        Map.LocForTasLine(2).Should().BeNull();
    }

    [Fact]
    public void TasLineForSourceLine_ExactHit()
    {
        Map.TasLineForSourceLine("/src/kern.cpp", 12).Should().Be((6, 12));
    }

    [Fact]
    public void TasLineForSourceLine_SlidesDownToNextMarkedLine()
    {
        Map.TasLineForSourceLine("/src/kern.cpp", 11).Should().Be((6, 12));
    }

    [Fact]
    public void TasLineForSourceLine_WrongFile_IsNull()
    {
        Map.TasLineForSourceLine("/src/missing.cpp", 10).Should().BeNull();
    }

    [Fact]
    public void CoversFile_MatchesMarkedSourcesOnly()
    {
        Map.CoversFile("/src/kern.cpp").Should().BeTrue();
        Map.CoversFile("/src/other.hpp").Should().BeTrue();
        Map.CoversFile("/src/missing.cpp").Should().BeFalse();
    }
}
