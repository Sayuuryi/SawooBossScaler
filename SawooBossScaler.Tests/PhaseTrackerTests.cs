using SawooBossScaler.Core;

namespace SawooBossScaler.Tests;

public class PhaseTrackerTests
{
    private static PhaseTracker In(string key)
    {
        var tracker = new PhaseTracker();
        tracker.Enter(Ultimates.ForKey(key));
        return tracker;
    }

    [Fact]
    public void StartsAtFirstPhaseAndStopsOutside()
    {
        var tracker = In("TOP");
        Assert.Equal(1, tracker.Number);

        tracker.Enter(null);
        Assert.Equal(0, tracker.Number);
        Assert.Null(tracker.Current);
        Assert.False(tracker.OnAction(0x7B40));
    }

    [Fact]
    public void FollowsTheOmegaProtocol()
    {
        var tracker = In("TOP");
        Assert.True(tracker.OnAction(0x7B40));
        Assert.Equal("P2", tracker.Current!.Label);
        Assert.True(tracker.OnAction(0x7B13));
        Assert.Equal("P3", tracker.Current!.Label);
        Assert.True(tracker.OnAction(0x7B47));
        Assert.True(tracker.OnAction(0x7B7C));
        Assert.True(tracker.OnBossSeen("Alpha Omega"));
        Assert.Equal("P6", tracker.Current!.Label);
    }

    [Fact]
    public void NeverGoesBack()
    {
        var tracker = In("UCoB");
        tracker.OnAction(0x26D1);
        Assert.Equal("P3", tracker.Current!.Label);

        // Nael shows up again during Bahamut Prime's phase
        Assert.False(tracker.OnBossSeen("Nael deus Darnus"));
        Assert.Equal("P3", tracker.Current!.Label);
    }

    [Fact]
    public void SkipsAheadWhenASignalWasMissed()
    {
        var tracker = In("FRU");
        Assert.True(tracker.OnAction(0x9D72));
        Assert.Equal("P5", tracker.Current!.Label);
    }

    [Fact]
    public void ActionsReusedEarlyAreIgnoredUntilTheirPhase()
    {
        var tracker = In("DSR");

        // Planar Prison is also used in P1
        Assert.False(tracker.OnAction(0x62E1));
        Assert.Equal("P1", tracker.Current!.Label);

        tracker.OnAction(0x63C8);
        tracker.OnAction(0x6708);
        tracker.OnAction(0x717A);
        Assert.Equal("P4", tracker.Current!.Label);
        Assert.True(tracker.OnAction(0x62E1));
        Assert.Equal("Intermission", tracker.Current!.Label);
        Assert.True(tracker.OnAction(0x6B86));
        Assert.Equal("P5", tracker.Current!.Label);
    }

    [Fact]
    public void DancingMadPhaseFiveNeedsPhaseFour()
    {
        var tracker = In("DMU");
        Assert.False(tracker.OnAction(0xC24A));
        tracker.OnAction(0xC2DC);
        Assert.Equal("P4", tracker.Current!.Label);
        Assert.True(tracker.OnAction(0xC24A));
        Assert.Equal("P5", tracker.Current!.Label);
    }

    [Fact]
    public void ResetGoesBackToTheStart()
    {
        var tracker = In("UWU");
        tracker.OnBossSeen("Titan");
        Assert.Equal("P3", tracker.Current!.Label);

        tracker.Reset();
        Assert.Equal("P1", tracker.Current!.Label);
    }

    [Fact]
    public void ReportsPhaseChanges()
    {
        var tracker = In("TEA");
        var seen = new List<string>();
        tracker.PhaseChanged += p => seen.Add(p.Label);

        tracker.OnBossSeen("Cruise Chaser");
        tracker.OnBossSeen("Brute Justice");
        tracker.OnAction(0x485A);

        Assert.Equal(["P2", "P3"], seen);
    }
}
