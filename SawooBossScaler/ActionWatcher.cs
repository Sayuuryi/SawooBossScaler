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
            if (header != null && caster != null && caster->GameObject.ObjectKind == ObjectKind.BattleNpc)
                ActionUsed?.Invoke(header->ActionId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error reading an enemy action");
        }
    }

    public void Dispose() => _hook.Dispose();
}
