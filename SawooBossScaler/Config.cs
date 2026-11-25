using Dalamud.Configuration;

namespace SawooBossScaler;

public enum SizeMethod
{
    /// <summary>Changes the size on screen right away.</summary>
    Instant,

    /// <summary>Changes the boss's own size and asks Penumbra to redraw it.</summary>
    Redraw,
}

[Serializable]
public sealed class Config : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public SizeMethod Method { get; set; } = SizeMethod.Instant;

    /// <summary>Boss sizes, keyed "Fight/Phase/Boss". Only sizes other than normal are kept.</summary>
    public Dictionary<string, float> Sizes { get; set; } = [];

    /// <summary>The fight shown in the window.</summary>
    public string SelectedFight { get; set; } = "DMU";
}
