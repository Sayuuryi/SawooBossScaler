using SawooBossScaler.Core;

namespace SawooBossScaler.Tests;

public class SizeTableTests
{
    private static readonly Ultimate Dsr = Ultimates.ForKey("DSR")!;

    [Fact]
    public void UnsetSizesAreNormal()
        => Assert.Equal(1f, SizeTable.Get(new Dictionary<string, float>(), Dsr, Dsr.Phases[2], "Nidhogg"));

    [Fact]
    public void SizesArePerPhase()
    {
        var sizes = new Dictionary<string, float>();
        var p3 = Dsr.Phases[2];
        var p6 = Dsr.Phases.Single(p => p.Label == "P6");

        SizeTable.Set(sizes, Dsr, p3, "Nidhogg", 0.4f);

        Assert.Equal(0.4f, SizeTable.Get(sizes, Dsr, p3, "Nidhogg"));
        Assert.Equal(1f, SizeTable.Get(sizes, Dsr, p6, "Nidhogg"));
        Assert.Equal("DSR/P3/Nidhogg", Assert.Single(sizes.Keys));
    }

    [Fact]
    public void NormalSizesAreNotStored()
    {
        var sizes = new Dictionary<string, float>();
        var phase = Dsr.Phases[0];
        SizeTable.Set(sizes, Dsr, phase, "Ser Adelphel", 0.5f);
        SizeTable.Set(sizes, Dsr, phase, "Ser Adelphel", 1f);
        Assert.Empty(sizes);
    }

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(5f, 2f)]
    [InlineData(float.NaN, 1f)]
    public void SizesStayInRange(float input, float expected)
        => Assert.Equal(expected, SizeTable.Clamp(input));

    [Fact]
    public void BossesOutsideTheCurrentPhaseKeepTheirNormalSize()
    {
        var sizes = new Dictionary<string, float>();
        var p1 = Dsr.Phases[0];
        SizeTable.Set(sizes, Dsr, p1, "Ser Adelphel", 0.5f);

        Assert.Equal(0.5f, SizeTable.ForBoss(sizes, Dsr, p1, "Ser Adelphel"));
        Assert.Equal(1f, SizeTable.ForBoss(sizes, Dsr, Dsr.Phases[2], "Ser Adelphel"));
        Assert.Equal(1f, SizeTable.ForBoss(sizes, Dsr, null, "Ser Adelphel"));
    }
}
