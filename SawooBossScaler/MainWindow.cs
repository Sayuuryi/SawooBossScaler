using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using SawooBossScaler.Core;

namespace SawooBossScaler;

public sealed class MainWindow : Window
{
    public enum Tab { Bosses, Settings }

    private static readonly Vector4 Accent = new(0.55f, 0.78f, 1f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Good = new(0.35f, 0.8f, 0.45f, 1f);
    private static readonly Vector4 Changed = new(1f, 0.78f, 0.35f, 1f);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Config _config;
    private readonly BossScaler _scaler;
    private readonly PenumbraRedraw _penumbra;
    private Tab? _requestedTab;
    private float _allSize = 0.5f;

    public MainWindow(IDalamudPluginInterface pluginInterface, Config config, BossScaler scaler, PenumbraRedraw penumbra)
        : base("Sawoo Boss Scaler##SawooBossScalerMain")
    {
        _pluginInterface = pluginInterface;
        _config = config;
        _scaler = scaler;
        _penumbra = penumbra;

        Size = new Vector2(760, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private static float Scale => ImGuiHelpers.GlobalScale;

    public void Open(Tab tab)
    {
        _requestedTab = tab;
        IsOpen = true;
        BringToFront();
    }

    private void Save() => _pluginInterface.SavePluginConfig(_config);

    public override void Draw()
    {
        DrawStatus();
        ImGui.Spacing();

        using (var tabBar = ImRaii.TabBar("##Tabs"))
        {
            if (tabBar)
            {
                DrawTab(Tab.Bosses, "Bosses", DrawBosses);
                DrawTab(Tab.Settings, "Settings", DrawSettings);
            }
        }

        _requestedTab = null;
    }

    private void DrawTab(Tab tab, string label, Action draw)
    {
        var flags = _requestedTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var item = ImRaii.TabItem(label, flags);
        if (!item)
            return;

        ImGui.Spacing();
        draw();
    }

    private void DrawStatus()
    {
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            _config.Enabled = enabled;
            Save();
        }

        ImGui.SameLine(0, 20 * Scale);

        var fight = _scaler.Tracker.Fight;
        var phase = _scaler.Tracker.Current;
        if (fight == null || phase == null)
        {
            ImGui.TextColored(Dim, "Not in an ultimate");
            return;
        }

        ImGui.TextColored(Good, fight.Name);
        ImGui.SameLine();
        ImGui.TextUnformatted(phase.Title);

        foreach (var boss in _scaler.OnField.DistinctBy(b => b.Name))
        {
            ImGui.SameLine(0, 14 * Scale);
            ImGui.TextColored(SizeTable.IsNormal(boss.Size) ? Dim : Changed, $"{boss.Name} {Percent(boss.Size)}");
        }
    }

    private void DrawBosses()
    {
        var listWidth = 200 * Scale;

        using (var list = ImRaii.Child("##Fights", new Vector2(listWidth, 0), true))
        {
            if (list)
                DrawFightList();
        }

        ImGui.SameLine();

        using var details = ImRaii.Child("##Fight", Vector2.Zero, false);
        if (!details)
            return;

        var fight = Ultimates.ForKey(_config.SelectedFight) ?? Ultimates.All[^1];
        DrawFight(fight);
    }

    private void DrawFightList()
    {
        foreach (var fight in Ultimates.All)
        {
            var changed = CountChanged(fight);
            var label = changed > 0 ? $"{fight.Name} ({changed})" : fight.Name;

            if (ImGui.Selectable($"{label}##{fight.Key}", _config.SelectedFight == fight.Key))
            {
                _config.SelectedFight = fight.Key;
                Save();
            }
        }
    }

    private int CountChanged(Ultimate fight) => _config.Sizes.Keys.Count(k => k.StartsWith(fight.Key + "/", StringComparison.Ordinal));

    private void DrawFight(Ultimate fight)
    {
        ImGui.TextColored(Accent, fight.Name);
        ImGui.Spacing();

        // whole fight at once
        ImGui.SetNextItemWidth(160 * Scale);
        SizeSlider("##AllSize", ref _allSize);
        ImGui.SameLine();
        if (ImGui.Button("Set every boss"))
        {
            foreach (var phase in fight.Phases)
            foreach (var boss in phase.Bosses)
                SizeTable.Set(_config.Sizes, fight, phase, boss, _allSize);
            Save();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(CountChanged(fight) == 0))
        {
            if (ImGui.Button("Reset fight"))
            {
                foreach (var key in _config.Sizes.Keys.Where(k => k.StartsWith(fight.Key + "/", StringComparison.Ordinal)).ToList())
                    _config.Sizes.Remove(key);
                Save();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var current = _scaler.Tracker.Fight == fight ? _scaler.Tracker.Current : null;
        foreach (var phase in fight.Phases)
            DrawPhase(fight, phase, phase == current);
    }

    private void DrawPhase(Ultimate fight, Phase phase, bool isCurrent)
    {
        var title = isCurrent ? $"{phase.Title}  (now)" : phase.Title;
        using (ImRaii.PushColor(ImGuiCol.Text, Good, isCurrent))
        {
            if (!ImGui.CollapsingHeader($"{title}###{fight.Key}{phase.Label}", ImGuiTreeNodeFlags.DefaultOpen))
                return;
        }

        using var table = ImRaii.Table($"##{fight.Key}{phase.Label}", 3, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn("Boss", ImGuiTableColumnFlags.WidthFixed, 190 * Scale);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Buttons", ImGuiTableColumnFlags.WidthFixed, 150 * Scale);

        foreach (var boss in phase.Bosses)
            DrawBoss(fight, phase, boss);
    }

    private void DrawBoss(Ultimate fight, Phase phase, string boss)
    {
        using var id = ImRaii.PushId($"{fight.Key}/{phase.Label}/{boss}");
        var size = SizeTable.Get(_config.Sizes, fight, phase, boss);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(SizeTable.IsNormal(size) ? Vector4.One : Changed, boss);

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        if (SizeSlider("##Size", ref size))
            SizeTable.Set(_config.Sizes, fight, phase, boss, size);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Ctrl+click to type a size in %");

        ImGui.TableNextColumn();
        var others = fight.Phases.Where(p => p != phase && p.Bosses.Contains(boss)).ToList();
        using (ImRaii.Disabled(others.Count == 0))
        {
            if (ImGui.SmallButton("All phases"))
            {
                foreach (var other in others)
                    SizeTable.Set(_config.Sizes, fight, other, boss, size);
                Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(others.Count == 0 ? $"{boss} is only in this phase" : $"Use this size for {boss} in every phase");

        ImGui.SameLine();
        using (ImRaii.Disabled(SizeTable.IsNormal(size)))
        {
            if (ImGui.SmallButton("Reset"))
            {
                SizeTable.Set(_config.Sizes, fight, phase, boss, SizeTable.Normal);
                Save();
            }
        }
    }

    private void DrawSettings()
    {
        ImGui.TextColored(Accent, "How sizes change");
        ImGui.Spacing();

        var method = _config.Method;
        if (ImGui.RadioButton("Instant", method == SizeMethod.Instant))
            SetMethod(SizeMethod.Instant);
        ImGui.Indent();
        ImGui.TextColored(Dim, "Sizes change the moment a phase starts.");
        ImGui.Unindent();

        ImGui.Spacing();
        if (ImGui.RadioButton("Redraw with Penumbra", method == SizeMethod.Redraw))
            SetMethod(SizeMethod.Redraw);
        ImGui.Indent();
        ImGui.TextColored(Dim, "Try this if sizes don't change with Instant. The boss blinks once when its size changes.");
        if (!_penumbra.Available)
            ImGui.TextColored(Changed, "Penumbra isn't running.");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(Accent, "Commands");
        Command("/sbs", "Open this window");
        Command("/sbs config", "Open the settings");

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextColored(Dim, $"Sawoo Boss Scaler {_pluginInterface.Manifest.AssemblyVersion} · by Sayuuryi");
        if (ImGui.SmallButton("GitHub"))
            Util.OpenLink("https://github.com/Sayuuryi/SawooBossScaler");
    }

    private void SetMethod(SizeMethod method)
    {
        _config.Method = method;
        Save();
    }

    private static void Command(string command, string text)
    {
        ImGui.TextColored(Accent, command);
        ImGui.SameLine(130 * Scale);
        ImGui.TextUnformatted(text);
    }

    private static string Percent(float size) => $"{MathF.Round(size * 100)}%";

    /// <summary>A size slider in whole percent, so typing a value (Ctrl+click) means percent too.</summary>
    private static bool SizeSlider(string label, ref float size)
    {
        var percent = (int)MathF.Round(size * 100);
        if (!ImGui.SliderInt(label, ref percent, (int)(SizeTable.Min * 100), (int)(SizeTable.Max * 100), "%d%%"))
            return false;

        size = SizeTable.Clamp(percent / 100f);
        return true;
    }
}
