using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using SawooBossScaler.Core;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using GameCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using DrawObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject;

namespace SawooBossScaler;

/// <summary>A boss on the field right now and the size it has.</summary>
public readonly record struct BossOnField(string Name, float Size);

/// <summary>
/// Inside an ultimate: follows the phase and gives every boss on the field the size picked for that phase.
/// Everything is put back when leaving, turning it off or unloading.
/// </summary>
public sealed unsafe class BossScaler : IDisposable
{
    /// <summary>Out of combat this long inside an ultimate counts as a wipe.</summary>
    private static readonly TimeSpan WipeDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstRedraw = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SecondRedraw = TimeSpan.FromMilliseconds(850);

    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly IPluginLog _log;
    private readonly Config _config;
    private readonly BossNames _names;
    private readonly ActionWatcher _actions;
    private readonly PenumbraRedraw _penumbra;

    private readonly Dictionary<ulong, Scaled> _scaled = [];
    private readonly List<(IBattleNpc Npc, string Boss)> _found = [];
    private readonly List<BossOnField> _onField = [];
    private DateTime? _outOfCombatSince;
    private SizeMethod _activeMethod;

    public PhaseTracker Tracker { get; } = new();

    public IReadOnlyList<BossOnField> OnField => _onField;

    public BossScaler(IFramework framework, IClientState clientState, IObjectTable objectTable, ICondition condition, IPluginLog log,
        Config config, BossNames names, ActionWatcher actions, PenumbraRedraw penumbra)
    {
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _condition = condition;
        _log = log;
        _config = config;
        _names = names;
        _actions = actions;
        _penumbra = penumbra;
        _activeMethod = config.Method;

        Tracker.PhaseChanged += phase => _log.Debug("{fight} {phase}", Tracker.Fight?.Key ?? "", phase.Title);
        _actions.ActionUsed += OnAction;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _framework.Update += OnUpdate;

        OnTerritoryChanged(_clientState.TerritoryType);
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _actions.ActionUsed -= OnAction;
        _actions.Enabled = false;
        RestoreAll();
    }

    private void OnTerritoryChanged(uint territory)
    {
        // the old objects are gone, nothing to put back
        _scaled.Clear();
        _onField.Clear();
        _outOfCombatSince = null;

        Tracker.Enter(Ultimates.ForTerritory(territory));
        _actions.Enabled = Tracker.Fight != null;
    }

    private void OnAction(uint actionId) => Tracker.OnAction(actionId);

    private void OnUpdate(IFramework framework)
    {
        var fight = Tracker.Fight;
        if (fight == null)
            return;

        if (_activeMethod != _config.Method)
        {
            RestoreAll();
            _activeMethod = _config.Method;
        }

        TrackWipes();
        FindBosses(fight);

        var phase = Tracker.Current;
        var now = DateTime.UtcNow;
        var present = new HashSet<ulong>();
        _onField.Clear();

        foreach (var (npc, boss) in _found)
        {
            var size = _config.Enabled ? SizeTable.ForBoss(_config.Sizes, fight, phase, boss) : SizeTable.Normal;
            var key = Key(npc);
            present.Add(key);
            _onField.Add(new BossOnField(boss, size));

            try
            {
                Apply(npc, key, size, now);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Could not resize {boss}", boss);
            }
        }

        // bosses that left the field took their size with them
        foreach (var key in _scaled.Keys.Where(k => !present.Contains(k)).ToList())
            _scaled.Remove(key);
    }

    private void TrackWipes()
    {
        // being dead mid-fight can also mean out of combat; a wipe ends with everyone up again
        var player = _objectTable.LocalPlayer;
        if (_condition[ConditionFlag.InCombat] || player == null || player.IsDead)
        {
            _outOfCombatSince = null;
            return;
        }

        _outOfCombatSince ??= DateTime.UtcNow;
        if (Tracker.Number > 1 && DateTime.UtcNow - _outOfCombatSince > WipeDelay)
            Tracker.Reset();
    }

    private void FindBosses(Ultimate fight)
    {
        _found.Clear();

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc || obj.Address == nint.Zero || obj is not IBattleNpc npc)
                continue;

            if (!_names.TryGet(npc.NameId, out var boss))
                continue;

            if (npc.IsTargetable && !npc.IsDead)
                Tracker.OnBossSeen(boss);

            if (npc.IsCasting)
                Tracker.OnAction(npc.CastActionId);

            _found.Add((npc, boss));
        }

        // names shared with other content are only bosses here when the fight has them
        _found.RemoveAll(f => !fight.Bosses.Contains(f.Boss, StringComparer.OrdinalIgnoreCase));
    }

    private static ulong Key(IGameObject obj)
        => obj.EntityId != 0 && obj.EntityId != 0xE0000000 ? obj.EntityId : 0x1_0000_0000UL | obj.ObjectIndex;

    private void Apply(IBattleNpc npc, ulong key, float size, DateTime now)
    {
        if (!_scaled.TryGetValue(key, out var scaled) || scaled.Address != npc.Address)
        {
            if (SizeTable.IsNormal(size))
                return;

            scaled = new Scaled(npc.Address);
            _scaled[key] = scaled;
        }

        if (_activeMethod == SizeMethod.Instant)
            ApplyInstant(npc, scaled, size);
        else
            ApplyRedraw(npc, scaled, size, now);

        if (SizeTable.IsNormal(size) && scaled.Done)
            _scaled.Remove(key);
    }

    /// <summary>Scales what's drawn on screen. Takes effect the same frame and doesn't change the boss itself.</summary>
    private static void ApplyInstant(IBattleNpc npc, Scaled scaled, float size)
    {
        var draw = ((GameObject*)npc.Address)->DrawObject;
        if (draw == null)
        {
            scaled.Draw = null;
            scaled.Done = true;
            return;
        }

        var current = (Vector3)draw->Object.Scale;

        // a new model, or the game resized it (some bosses grow): start again from its size now
        if (scaled.Draw != draw || !Near(current, scaled.Written))
        {
            scaled.Draw = draw;
            scaled.Baseline = current;
        }

        var wanted = scaled.Baseline * size;
        if (!Near(current, wanted))
            draw->Object.Scale = wanted;

        scaled.Written = wanted;
        scaled.Done = true;
    }

    /// <summary>Changes the boss's own size, then has Penumbra redraw it so the new size shows.</summary>
    private void ApplyRedraw(IBattleNpc npc, Scaled scaled, float size, DateTime now)
    {
        var obj = (GameObject*)npc.Address;
        var chara = (GameCharacter*)npc.Address;

        if (!scaled.HasOriginal)
        {
            scaled.OriginalObjectScale = Normalize(obj->Scale);
            scaled.OriginalModelScale = Normalize(chara->CharacterData.ModelScale);
            scaled.HasOriginal = true;
        }

        var objectScale = scaled.OriginalObjectScale * size;
        var modelScale = scaled.OriginalModelScale * size;
        if (Math.Abs(obj->Scale - objectScale) > 0.0001f)
            obj->Scale = objectScale;
        if (Math.Abs(chara->CharacterData.ModelScale - modelScale) > 0.0001f)
            chara->CharacterData.ModelScale = modelScale;

        if (Math.Abs(scaled.Size - size) > 0.0001f)
        {
            scaled.Size = size;
            scaled.Redraws.Clear();
            scaled.Redraws.Enqueue(now + FirstRedraw);
            scaled.Redraws.Enqueue(now + SecondRedraw);
        }

        if (scaled.Redraws.TryPeek(out var due) && now >= due)
        {
            scaled.Redraws.Dequeue();
            if (!_penumbra.Redraw(npc.ObjectIndex))
                scaled.Redraws.Clear();
        }

        scaled.Done = scaled.Redraws.Count == 0;
    }

    /// <summary>Puts back every boss that is still on the field.</summary>
    private void RestoreAll()
    {
        if (_scaled.Count == 0)
            return;

        try
        {
            foreach (var obj in _objectTable)
            {
                if (obj.Address == nint.Zero || obj is not IBattleNpc npc || !_scaled.TryGetValue(Key(npc), out var scaled) || scaled.Address != npc.Address)
                    continue;

                if (_activeMethod == SizeMethod.Instant)
                {
                    var draw = ((GameObject*)npc.Address)->DrawObject;
                    if (draw != null && draw == scaled.Draw && Near(draw->Object.Scale, scaled.Written))
                        draw->Object.Scale = scaled.Baseline;
                }
                else if (scaled.HasOriginal)
                {
                    ((GameObject*)npc.Address)->Scale = scaled.OriginalObjectScale;
                    ((GameCharacter*)npc.Address)->CharacterData.ModelScale = scaled.OriginalModelScale;
                    _penumbra.Redraw(npc.ObjectIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not put the bosses back to their normal size");
        }

        _scaled.Clear();
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < 1e-8f;

    private static float Normalize(float scale) => float.IsFinite(scale) && scale > 0.0001f ? scale : 1f;

    private sealed class Scaled(nint address)
    {
        public nint Address { get; } = address;
        public bool Done { get; set; }

        // instant
        public DrawObject* Draw { get; set; }
        public Vector3 Baseline { get; set; }
        public Vector3 Written { get; set; }

        // redraw
        public bool HasOriginal { get; set; }
        public float OriginalObjectScale { get; set; }
        public float OriginalModelScale { get; set; }
        public float Size { get; set; } = SizeTable.Normal;
        public Queue<DateTime> Redraws { get; } = new();
    }
}
