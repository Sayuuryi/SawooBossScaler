namespace SawooBossScaler.Core;

/// <summary>
/// Size of each boss in each phase, as a factor of its normal size (1 = unchanged).
/// Only sizes that differ from normal are stored.
/// </summary>
public static class SizeTable
{
    public const float Min = 0.1f;
    public const float Max = 2f;
    public const float Normal = 1f;

    public static string Key(Ultimate fight, Phase phase, string boss) => $"{fight.Key}/{phase.Label}/{boss}";

    public static float Clamp(float size) => float.IsFinite(size) ? Math.Clamp(size, Min, Max) : Normal;

    public static bool IsNormal(float size) => Math.Abs(size - Normal) < 0.005f;

    public static float Get(IReadOnlyDictionary<string, float> sizes, Ultimate fight, Phase phase, string boss)
        => sizes.TryGetValue(Key(fight, phase, boss), out var size) ? Clamp(size) : Normal;

    public static void Set(IDictionary<string, float> sizes, Ultimate fight, Phase phase, string boss, float size)
    {
        size = Clamp(size);
        var key = Key(fight, phase, boss);
        if (IsNormal(size))
            sizes.Remove(key);
        else
            sizes[key] = size;
    }

    /// <summary>The size for a boss right now: its size in the current phase, or normal when it isn't part of it.</summary>
    public static float ForBoss(IReadOnlyDictionary<string, float> sizes, Ultimate fight, Phase? phase, string boss)
    {
        if (phase == null || !phase.Bosses.Contains(boss, StringComparer.OrdinalIgnoreCase))
            return Normal;

        return Get(sizes, fight, phase, boss);
    }
}
