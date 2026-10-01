using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Physical selection contributes rebuild identities only, never logical edits.</summary>
public static class RoofPhysicalStretchRules
{
    public static bool ShouldRecover(string? command, bool sourceModified) =>
        (RoofGeneratedMemberEditCommandRules.IsClassicStretch(command) ||
         RoofGeneratedMemberEditCommandRules.IsGripStretchCommand(command)) && !sourceModified;

    public static bool ShouldRejectDirectEdit(bool acceptedPlanEdit) => !acceptedPlanEdit;

    public static string PhysicalMemberId(RoofGeneratedMemberKey key) => string.Join(":",
        key.MemberKind, key.RoofFace, key.StationIndex.ToString(CultureInfo.InvariantCulture));

    public static bool TrySelectRebuildKeys(
        IReadOnlyCollection<RoofGeneratedMemberKey> authoritativeKeys,
        IReadOnlyCollection<RoofGeneratedMemberKey> changedPlanKeys,
        IReadOnlyCollection<string> collateralPhysicalIds,
        out IReadOnlyCollection<RoofGeneratedMemberKey> rebuildKeys)
    {
        rebuildKeys = Array.Empty<RoofGeneratedMemberKey>();
        var byId = new Dictionary<string, RoofGeneratedMemberKey>(StringComparer.Ordinal);
        foreach (var key in authoritativeKeys)
        {
            var id = PhysicalMemberId(key);
            if (byId.ContainsKey(id)) return false;
            byId.Add(id, key);
        }
        var selected = new HashSet<RoofGeneratedMemberKey>(changedPlanKeys);
        if (selected.Any(key => !byId.ContainsKey(PhysicalMemberId(key)))) return false;
        foreach (var id in collateralPhysicalIds)
        {
            if (!byId.TryGetValue(id, out var key)) return false;
            selected.Add(key);
        }
        rebuildKeys = selected.ToArray();
        return true;
    }
}
