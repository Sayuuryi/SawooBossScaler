using SawooBossScaler.Core;

namespace SawooBossScaler.Tests;

public class UltimatesTests
{
    [Fact]
    public void EveryFightIsListedOnce()
    {
        Assert.Equal(7, Ultimates.All.Count);
        Assert.Equal(Ultimates.All.Count, Ultimates.All.Select(u => u.Key).Distinct().Count());
        Assert.Equal(Ultimates.All.Count, Ultimates.All.Select(u => u.TerritoryId).Distinct().Count());
    }

    [Theory]
    [InlineData(733u, "UCoB")]
    [InlineData(777u, "UWU")]
    [InlineData(887u, "TEA")]
    [InlineData(968u, "DSR")]
    [InlineData(1122u, "TOP")]
    [InlineData(1238u, "FRU")]
    [InlineData(1363u, "DMU")]
    public void FindsFightByTerritory(uint territory, string key)
        => Assert.Equal(key, Ultimates.ForTerritory(territory)?.Key);

    [Fact]
    public void PhasesAreNumberedInOrderWithUniqueLabels()
    {
        foreach (var fight in Ultimates.All)
        {
            Assert.Equal(Enumerable.Range(1, fight.Phases.Count), fight.Phases.Select(p => p.Number));
            Assert.Equal(fight.Phases.Count, fight.Phases.Select(p => p.Label).Distinct().Count());
        }
    }

    [Fact]
    public void EveryLaterPhaseCanBeReached()
    {
        foreach (var fight in Ultimates.All)
        {
            Assert.Empty(fight.Phases[0].Signals);
            foreach (var phase in fight.Phases.Skip(1))
                Assert.True(phase.Signals.Count > 0, $"{fight.Key} {phase.Label} has no signal");
        }
    }

    [Fact]
    public void AppearSignalsNameABossOfThatPhase()
    {
        foreach (var fight in Ultimates.All)
        foreach (var phase in fight.Phases)
        foreach (var signal in phase.Signals.Where(s => s.Appears != null))
            Assert.Contains(signal.Appears, phase.Bosses);
    }

    [Fact]
    public void NoBossIsListedTwiceInAPhase()
    {
        foreach (var fight in Ultimates.All)
        foreach (var phase in fight.Phases)
            Assert.Equal(phase.Bosses.Count, phase.Bosses.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DragonsongUsesPlayerPhaseNames()
    {
        var dsr = Ultimates.ForKey("DSR")!;
        Assert.Equal(["P1", "P2", "P3", "P4", "Intermission", "P5", "P6", "P7"], dsr.Phases.Select(p => p.Label));
    }
}
