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
/// Anything resized is put back as soon as it shouldn't be anymore: phase change, turning it off, switching method, unloading.
/// </summary>
public sealed unsafe class BossScaler : IDisposable
{
    /// <summary>Out of combat this long, alive, inside an ultimate counts as a wipe.</summary>
    private static readonly TimeSpan WipeDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstRedraw = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SecondRedraw = TimeSpan.FromMilliseconds(850);
    private const string TargetLabel = "Your target";

    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly ITargetManager _targetManager;
    private readonly IPluginLog _log;
    private readonly Config _config;
    private readonly BossNames _names;
    private readonly ActionWatcher? _actions;
    private readonly PenumbraRedraw _penumbra;

    private readonly Dictionary<ulong, Scaled> _scaled = [];
    private readonly List<(IBattleNpc Npc, string Boss, float Size)> _found = [];
    private readonly List<BossOnField> _onField = [];
    private DateTime? _outOfCombatSince;
    private SizeMethod _activeMethod;

    public PhaseTracker Tracker { get; } = new();

    public IReadOnlyList<BossOnField> OnField => _onField;

    /// <summary>False when enemy actions can't be read; phases then follow casts and bosses showing up only.</summary>
    public bool ReadsActions => _actions != null;

    /// <summary>When set, your current target (an enemy) gets this size. For trying the size method anywhere.</summary>
    public float? TargetTestSize { get; set; }

    public BossScaler(IFramework framework, IClientState clientState, IObjectTable objectTable, ICondition condition, ITargetManager targetManager,
        IPluginLog log, Config config, BossNames names, ActionWatcher? actions, PenumbraRedraw penumbra)
    {
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _condition = condition;
        _targetManager = targetManager;
        _log = log;
        _config = config;
        _names = names;
        _actions = actions;
        _penumbra = penumbra;
        _activeMethod = config.Method;

        Tracker.PhaseChanged += phase => _log.Debug("{fight} {phase}", Tracker.Fight?.Key ?? "", phase.Title);
        if (_actions != null)
            _actions.ActionUsed += OnAction;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _framework.Update += OnUpdate;

        OnTerritoryChanged(_clientState.TerritoryType);
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        if (_actions != null)
        {
            _actions.ActionUsed -= OnAction;
            _actions.Enabled = false;
        }

        // the object list may only be read on the game's thread; don't hang the unload if it's busy
        try
        {
            if (_framework.IsInFrameworkUpdateThread)
                RestoreWhere(_ => true);
            else
                _framework.RunOnFrameworkThread(() => RestoreWhere(_ => true)).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not put the bosses back while unloading");
        }

        _scaled.Clear();
    }

    private void OnTerritoryChanged(uint territory)
    {
        // the old objects are gone, nothing to put back
        _scaled.Clear();
        _onField.Clear();
        _outOfCombatSince = null;

        Tracker.Enter(Ultimates.ForTerritory(territory));
        if (_actions != null)
            _actions.Enabled = Tracker.Fight != null;
    }

    private void OnAction(uint actionId) => Tracker.OnAction(actionId);

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error while resizing bosses");
        }
    }

    private void Update()
    {
        var fight = Tracker.Fight;
        if (fight == null && TargetTestSize == null && _scaled.Count == 0)
            return;

        if (_activeMethod != _config.Method)
        {
            RestoreWhere(_ => true);
            _scaled.Clear();
            _activeMethod = _config.Method;
        }

        _found.Clear();
        if (fight != null)
        {
            TrackWipes();
            FindBosses(fight);
        }

        AddTestTarget();

        var now = DateTime.UtcNow;
        var present = new HashSet<ulong>();
        _onField.Clear();

        foreach (var (npc, boss, size) in _found)
        {
            var key = Key(npc);
            if (!present.Add(key))
                continue;

            if (boss != TargetLabel)
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

        // still on the field but no longer to be resized (e.g. the test target changed): put back
        if (_scaled.Keys.Any(k => !present.Contains(k)))
        {
            RestoreWhere(k => !present.Contains(k));
            foreach (var key in _scaled.Keys.Where(k => !present.Contains(k)).ToList())
                _scaled.Remove(key);
        }
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
        var bosses = new List<(IBattleNpc Npc, string Boss)>();

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc || obj.Address == nint.Zero || obj is not IBattleNpc npc)
                continue;

            // names shared with other content only count when this fight has them
            if (!_names.TryGet(npc.NameId, out var boss) || !fight.Bosses.Contains(boss, StringComparer.OrdinalIgnoreCase))
                continue;

            if (npc.IsTargetable && !npc.IsDead)
                Tracker.OnBossSeen(boss);

            if (npc.IsCasting)
                Tracker.OnAction(npc.CastActionId);

            bosses.Add((npc, boss));
        }

        // sizes after the phase is updated, so a boss that starts a phase gets that phase's size right away
        var phase = Tracker.Current;
        foreach (var (npc, boss) in bosses)
        {
            var size = _config.Enabled ? SizeTable.ForBoss(_config.Sizes, fight, phase, boss) : SizeTable.Normal;
            _found.Add((npc, boss, size));
        }
    }

    private void AddTestTarget()
    {
        if (TargetTestSize is not { } size)
            return;

        if (_targetManager.Target is IBattleNpc npc && npc.Address != nint.Zero && npc.ObjectKind == ObjectKind.BattleNpc)
            _found.Insert(0, (npc, TargetLabel, SizeTable.Clamp(size)));
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
        if (!IsUsable(current))
        {
            scaled.Done = true;
            return;
        }

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

    /// <summary>Puts back the resized objects that are still on the field and match the filter.</summary>
    private void RestoreWhere(Func<ulong, bool> filter)
    {
        if (_scaled.Count == 0)
            return;

        foreach (var obj in _objectTable)
        {
            if (obj.Address == nint.Zero || obj is not IBattleNpc npc)
                continue;

            var key = Key(npc);
            if (!filter(key) || !_scaled.TryGetValue(key, out var scaled) || scaled.Address != npc.Address)
                continue;

            try
            {
                Restore(npc, scaled);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Could not put a boss back to its normal size");
            }
        }
    }

    private void Restore(IBattleNpc npc, Scaled scaled)
    {
        if (_activeMethod == SizeMethod.Instant)
        {
            var draw = ((GameObject*)npc.Address)->DrawObject;

            // only undo our own change; if the model was replaced, the new one is already normal
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

    private static bool Near(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < 1e-8f;

    private static bool IsUsable(Vector3 scale)
        => float.IsFinite(scale.X) && float.IsFinite(scale.Y) && float.IsFinite(scale.Z) && scale.X > 0.0001f && scale.Y > 0.0001f && scale.Z > 0.0001f;

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
