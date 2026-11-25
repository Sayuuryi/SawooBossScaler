using Dalamud.Game;
using Lumina.Excel.Sheets;
using SawooBossScaler.Core;

namespace SawooBossScaler;

/// <summary>
/// Maps the game's NPC name ids to the English boss names used in <see cref="Ultimates"/>,
/// so bosses are recognised in every client language.
/// </summary>
public sealed class BossNames
{
    private readonly Dictionary<uint, string> _byNameId = [];

    public BossNames(IDataManager dataManager, IPluginLog log)
    {
        var wanted = Ultimates.All
            .SelectMany(u => u.Bosses)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataManager.GetExcelSheet<BNpcName>(ClientLanguage.English))
        {
            if (wanted.TryGetValue(row.Singular.ToString(), out var boss))
                _byNameId[row.RowId] = boss;
        }

        var missing = wanted.Keys.Where(n => !_byNameId.ContainsValue(n)).ToList();
        if (missing.Count > 0)
            log.Warning("No name id found for {bosses}", string.Join(", ", missing));
    }

    public bool TryGet(uint nameId, out string boss) => _byNameId.TryGetValue(nameId, out boss!);
}
