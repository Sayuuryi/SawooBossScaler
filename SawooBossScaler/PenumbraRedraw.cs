using Dalamud.Plugin.Ipc;

namespace SawooBossScaler;

/// <summary>Asks Penumbra to redraw an object, when it is installed.</summary>
public sealed class PenumbraRedraw
{
    private readonly ICallGateSubscriber<bool> _enabled;
    private readonly ICallGateSubscriber<int, int, object> _redraw;

    public PenumbraRedraw(IDalamudPluginInterface pluginInterface)
    {
        _enabled = pluginInterface.GetIpcSubscriber<bool>("Penumbra.GetEnabledState");
        _redraw = pluginInterface.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5");
    }

    public bool Available
    {
        get
        {
            try
            {
                return _enabled.InvokeFunc();
            }
            catch
            {
                return false;
            }
        }
    }

    public bool Redraw(int objectIndex)
    {
        try
        {
            _redraw.InvokeAction(objectIndex, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
