using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace SawooBossScaler;

/// <summary>Reports the actions enemies use. Only switched on inside an ultimate.</summary>
public sealed unsafe class ActionWatcher : IDisposable
{
    private readonly IPluginLog _log;
    private readonly Hook<ActionEffectHandler.Delegates.Receive> _hook;

    public event Action<uint>? ActionUsed;

    public ActionWatcher(IGameInteropProvider interop, IPluginLog log)
    {
        _log = log;
        _hook = interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.Addresses.Receive.Value, OnReceive);
    }

    public bool Enabled
    {
        get => _hook.IsEnabled;
        set
        {
            if (value == _hook.IsEnabled)
                return;

            if (value)
                _hook.Enable();
            else
                _hook.Disable();
        }
    }

    private void OnReceive(uint casterEntityId, Character* caster, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        _hook.Original(casterEntityId, caster, targetPos, header, effects, targetEntityIds);

        try
        {
            if (header == null || caster == null || caster->GameObject.ObjectKind != ObjectKind.BattleNpc)
                return;

            ActionUsed?.Invoke(header->ActionId);
            if (header->SpellId != 0 && header->SpellId != header->ActionId)
                ActionUsed?.Invoke(header->SpellId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error reading an enemy action");
        }
    }

    public void Dispose() => _hook.Dispose();

    /// <summary>Null when the game function can't be found (e.g. right after a game patch); the plugin works without it.</summary>
    public static ActionWatcher? TryCreate(IGameInteropProvider interop, IPluginLog log)
    {
        try
        {
            if (ActionEffectHandler.Addresses.Receive.Value == 0)
            {
                log.Warning("Enemy actions can't be read on this game version");
                return null;
            }

            return new ActionWatcher(interop, log);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Enemy actions can't be read on this game version");
            return null;
        }
    }
}
