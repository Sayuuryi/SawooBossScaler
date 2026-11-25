namespace SawooBossScaler.Core;

/// <summary>Something seen in the fight that means a phase has started.</summary>
/// <param name="ActionId">A boss action, either when its cast bar starts or when it goes off.</param>
/// <param name="Appears">A boss that becomes targetable (English name).</param>
/// <param name="MinPhase">Only counts once the fight has reached this phase (for actions reused earlier in the fight).</param>
public sealed record PhaseSignal(uint ActionId = 0, string? Appears = null, int MinPhase = 0)
{
    public static PhaseSignal Action(uint id, int minPhase = 0) => new(id, null, minPhase);
    public static PhaseSignal Appear(string boss, int minPhase = 0) => new(0, boss, minPhase);
}

/// <param name="Number">Order in the fight, starting at 1. Only used to move forward through the fight.</param>
/// <param name="Label">What players call it: "P1", "P2", "Intermission".</param>
public sealed record Phase(int Number, string Label, string Name, IReadOnlyList<string> Bosses, IReadOnlyList<PhaseSignal> Signals)
{
    public string Title => $"{Label} · {Name}";
}

public sealed record Ultimate(string Key, string Name, uint TerritoryId, IReadOnlyList<Phase> Phases)
{
    public Phase? GetPhase(int number) => Phases.FirstOrDefault(p => p.Number == number);

    /// <summary>Every boss in the fight, in order of first appearance.</summary>
    public IEnumerable<string> Bosses => Phases.SelectMany(p => p.Bosses).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The ultimate fights, their phases and the bosses on the field in each one.
/// Boss names are the English names; they are turned into name ids at runtime so any client language works.
/// Phase signals are the action ids the fights use at the start of each phase.
/// </summary>
public static class Ultimates
{
    private static Phase P(int number, string name, string[] bosses, params PhaseSignal[] signals) => new(number, $"P{number}", name, bosses, signals);
    private static Phase P(int number, string label, string name, string[] bosses, params PhaseSignal[] signals) => new(number, label, name, bosses, signals);
    private static PhaseSignal A(uint id, int minPhase = 0) => PhaseSignal.Action(id, minPhase);
    private static PhaseSignal Seen(string boss, int minPhase = 0) => PhaseSignal.Appear(boss, minPhase);

    public static readonly IReadOnlyList<Ultimate> All =
    [
        new("UCoB", "The Unending Coil of Bahamut", 733,
        [
            P(1, "Twintania", ["Twintania"]),
            P(2, "Nael deus Darnus", ["Nael deus Darnus"], Seen("Nael deus Darnus")),
            P(3, "Bahamut Prime", ["Bahamut Prime", "Twintania", "Nael deus Darnus"], A(0x26D1), Seen("Bahamut Prime")),
            P(4, "Triple Threat", ["Twintania", "Nael deus Darnus", "Bahamut Prime"], A(0x26E8)),
            P(5, "Golden Bahamut", ["Bahamut Prime"], A(0x26E9)),
        ]),
        new("UWU", "The Weapon's Refrain", 777,
        [
            P(1, "Garuda", ["Garuda"]),
            P(2, "Ifrit", ["Ifrit", "Garuda"], A(0x2B5F), Seen("Ifrit")),
            P(3, "Titan", ["Titan", "Garuda", "Ifrit"], A(0x2CFD), Seen("Titan")),
            P(4, "Lahabrea", ["Lahabrea"], A(0x2CF5), Seen("Lahabrea")),
            P(5, "The Ultima Weapon", ["The Ultima Weapon", "Garuda", "Ifrit", "Titan"], A(0x2B87), Seen("The Ultima Weapon")),
        ]),
        new("TEA", "The Epic of Alexander", 887,
        [
            P(1, "Living Liquid", ["Living Liquid", "Liquid Hand", "Liquid Rage"]),
            P(2, "Cruise Chaser and Brute Justice", ["Cruise Chaser", "Brute Justice"], A(0x4830), Seen("Cruise Chaser"), Seen("Brute Justice")),
            P(3, "Alexander Prime", ["Alexander Prime", "Cruise Chaser", "Brute Justice"], A(0x485A), Seen("Alexander Prime")),
            P(4, "Perfect Alexander", ["Perfect Alexander"], A(0x4A8B), A(0x487B), Seen("Perfect Alexander")),
        ]),
        new("DSR", "Dragonsong's Reprise", 968,
        [
            P(1, "Adelphel, Grinnaux and Charibert", ["Ser Adelphel", "Ser Grinnaux", "Ser Charibert"]),
            P(2, "King Thordan", ["King Thordan", .. Knights], A(0x63C8)),
            P(3, "Nidhogg", ["Nidhogg"], A(0x6708), Seen("Nidhogg")),
            P(4, "The Eyes", ["Right Eye", "Left Eye", "Nidhogg"], A(0x717A, minPhase: 3)),
            P(5, "Intermission", "Haurchefant", ["Haurchefant", "Ser Grinnaux", "Ser Charibert", "Ser Adelphel"], A(0x62E1, minPhase: 4)),
            P(6, "P5", "King Thordan II", ["King Thordan", .. Knights], A(0x6B86)),
            P(7, "P6", "Nidhogg and Hraesvelgr", ["Nidhogg", "Hraesvelgr"], A(0x6667), Seen("Hraesvelgr")),
            P(8, "P7", "Dragon-king Thordan", ["Dragon-king Thordan"], Seen("Dragon-king Thordan")),
        ]),
        new("TOP", "The Omega Protocol", 1122,
        [
            P(1, "Omega", ["Omega"]),
            P(2, "Omega-M and Omega-F", ["Omega-M", "Omega-F"], A(0x7B40), Seen("Omega-M"), Seen("Omega-F")),
            P(3, "Final Omega", ["Omega"], A(0x7B13)),
            P(4, "Blue Screen", ["Omega"], A(0x7B47)),
            P(5, "Run: Dynamis", ["Omega-M", "Omega-F", "Omega"], A(0x7B7C)),
            P(6, "Alpha Omega", ["Alpha Omega"], A(0x7F72), Seen("Alpha Omega")),
        ]),
        new("FRU", "Futures Rewritten", 1238,
        [
            P(1, "Fatebreaker", ["Fatebreaker"]),
            P(2, "Usurper of Frost", ["Usurper of Frost"], A(0x9CFF), Seen("Usurper of Frost")),
            P(3, "Oracle of Darkness", ["Oracle of Darkness"], A(0x9D49), Seen("Oracle of Darkness")),
            P(4, "Usurper and Oracle", ["Usurper of Frost", "Oracle of Darkness"], A(0x9D36)),
            P(5, "Pandora", ["Pandora"], A(0x9D72), Seen("Pandora")),
        ]),
        new("DMU", "Dancing Mad", 1363,
        [
            P(1, "Kefka", ["Kefka"]),
            P(2, "God Kefka", ["Kefka"], A(0xC24C)),
            P(3, "Chaos and Exdeath", ["Chaos", "Exdeath", "Kefka"], A(0xC3F7), Seen("Chaos"), Seen("Exdeath")),
            P(4, "Kefka, Chaos and Neo Exdeath", ["Kefka", "Chaos", "Neo Exdeath"], A(0xC2DC), Seen("Neo Exdeath")),
            P(5, "Kefka", ["Kefka"], A(0xC24A, minPhase: 4), A(0xBB40, minPhase: 4)),
        ]),
    ];

    /// <summary>The twelve knights of King Thordan's phases.</summary>
    private static string[] Knights =>
    [
        "Ser Adelphel", "Ser Janlenoux", "Ser Vellguine", "Ser Paulecrain", "Ser Ignasse", "Ser Hermenost",
        "Ser Grinnaux", "Ser Guerrique", "Ser Charibert", "Ser Haumeric", "Ser Noudenet", "Ser Zephirin",
    ];

    public static Ultimate? ForTerritory(uint territoryId) => All.FirstOrDefault(u => u.TerritoryId == territoryId);

    public static Ultimate? ForKey(string key) => All.FirstOrDefault(u => u.Key == key);
}
