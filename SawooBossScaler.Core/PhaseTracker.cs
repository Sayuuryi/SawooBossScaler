namespace SawooBossScaler.Core;

/// <summary>
/// Follows which phase of an ultimate is running. Phases only move forward; a wipe sends it back to the first one.
/// </summary>
public sealed class PhaseTracker
{
    public Ultimate? Fight { get; private set; }

    /// <summary>The current phase number (1 = first phase). 0 when not in an ultimate.</summary>
    public int Number { get; private set; }

    public Phase? Current => Fight?.GetPhase(Number);

    public event Action<Phase>? PhaseChanged;

    /// <summary>Starts following a fight (or stops, with null) from its first phase.</summary>
    public void Enter(Ultimate? fight)
    {
        Fight = fight;
        Number = fight == null ? 0 : 1;
    }

    /// <summary>Back to the first phase, after a wipe.</summary>
    public void Reset()
    {
        if (Fight != null)
            Number = 1;
    }

    /// <summary>A boss started casting or used an action.</summary>
    public bool OnAction(uint actionId)
        => actionId != 0 && Advance(s => s.ActionId == actionId);

    /// <summary>A boss is on the field and targetable.</summary>
    public bool OnBossSeen(string boss)
        => Advance(s => s.Appears != null && string.Equals(s.Appears, boss, StringComparison.OrdinalIgnoreCase));

    /// <summary>Moves to the earliest later phase that this signal starts.</summary>
    private bool Advance(Func<PhaseSignal, bool> matches)
    {
        if (Fight == null)
            return false;

        foreach (var phase in Fight.Phases)
        {
            if (phase.Number <= Number)
                continue;

            if (!phase.Signals.Any(s => Number >= s.MinPhase && matches(s)))
                continue;

            Number = phase.Number;
            PhaseChanged?.Invoke(phase);
            return true;
        }

        return false;
    }
}
