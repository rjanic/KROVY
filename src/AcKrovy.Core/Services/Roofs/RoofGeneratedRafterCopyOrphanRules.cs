using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Identifies same-DWG COPY clones of generated rafters that are not a complete
/// roof set. Those clones must leave the parent owner so FindByOwner cannot see
/// duplicate face/station keys.
/// </summary>
public static class RoofGeneratedRafterCopyOrphanRules
{
    public static IReadOnlyList<string> FindStandaloneDetachMemberKeys(
        RoofGeneratedRafterCopyAssociationPlan plan,
        IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> owners,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations,
        IReadOnlyCollection<string>? appendedMemberKeys = null)
    {
        if (plan is null || owners is null || observations is null || observations.Count == 0)
        {
            return [];
        }

        var knownOwners = new HashSet<string>(
            owners
                .Where(owner => owner is not null && !string.IsNullOrWhiteSpace(owner.OwnerReference))
                .Select(owner => owner.OwnerReference),
            StringComparer.OrdinalIgnoreCase);
        var ownersWithCompleteSet = new HashSet<string>(
            plan.Associations
                .Where(association => association is not null)
                .Select(association => association.OwnerReference),
            StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(
            plan.Associations
                .Where(association => association is not null)
                .SelectMany(association => association.Members)
                .Where(member => member is not null)
                .Select(member => member.MemberKey),
            StringComparer.Ordinal);
        var appended = appendedMemberKeys is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                appendedMemberKeys.Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

        var detached = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (observation is null ||
                string.IsNullOrWhiteSpace(observation.MemberKey) ||
                !seen.Add(observation.MemberKey) ||
                claimed.Contains(observation.MemberKey) ||
                !knownOwners.Contains(observation.EffectiveOwnerReference))
            {
                continue;
            }

            var isAppendedClone = appended.Contains(observation.MemberKey);
            var isUnclaimedSiblingOfCompleteOwner =
                ownersWithCompleteSet.Contains(observation.EffectiveOwnerReference);
            if (!isAppendedClone && !isUnclaimedSiblingOfCompleteOwner)
            {
                continue;
            }

            detached.Add(observation.MemberKey);
        }

        return detached;
    }

    /// <summary>
    /// When two physical Lines share the same owner reference and generated station,
    /// keep association members (or non-appended originals) and detach the rest.
    /// Covers single-rafter COPY even when complete-set association still matches the parent roof.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateStationDetachMemberKeys(
        RoofGeneratedRafterCopyAssociationPlan plan,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations,
        IReadOnlyCollection<string>? appendedMemberKeys = null)
    {
        if (plan is null || observations is null || observations.Count == 0)
        {
            return [];
        }

        var claimed = new HashSet<string>(
            plan.Associations
                .Where(association => association is not null)
                .SelectMany(association => association.Members)
                .Where(member => member is not null)
                .Select(member => member.MemberKey),
            StringComparer.Ordinal);
        var appended = appendedMemberKeys is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                appendedMemberKeys.Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

        var detach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = observations
            .Where(observation =>
                observation is not null &&
                !string.IsNullOrWhiteSpace(observation.MemberKey) &&
                !string.IsNullOrWhiteSpace(observation.EffectiveOwnerReference))
            .GroupBy(observation => (
                Owner: observation.EffectiveOwnerReference.ToUpperInvariant(),
                observation.Face,
                observation.StationIndex));

        foreach (var group in groups)
        {
            var members = group.ToArray();
            if (members.Length <= 1)
            {
                continue;
            }

            var keys = members.Select(item => item.MemberKey).ToArray();
            var claimedInGroup = keys.Where(claimed.Contains).ToArray();
            if (claimedInGroup.Length == 1)
            {
                foreach (var key in keys)
                {
                    if (!claimed.Contains(key))
                    {
                        detach.Add(key);
                    }
                }

                continue;
            }

            if (claimedInGroup.Length > 1)
            {
                continue;
            }

            var appendedInGroup = keys.Where(appended.Contains).ToArray();
            if (appendedInGroup.Length > 0 && appendedInGroup.Length < keys.Length)
            {
                foreach (var key in appendedInGroup)
                {
                    detach.Add(key);
                }
            }
        }

        return detach.ToArray();
    }

    public static IReadOnlyList<string> FindAllStandaloneDetachMemberKeys(
        RoofGeneratedRafterCopyAssociationPlan plan,
        IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> owners,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations,
        IReadOnlyCollection<string>? appendedMemberKeys = null)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in FindStandaloneDetachMemberKeys(
                     plan,
                     owners,
                     observations,
                     appendedMemberKeys))
        {
            merged.Add(key);
        }

        foreach (var key in FindDuplicateStationDetachMemberKeys(
                     plan,
                     observations,
                     appendedMemberKeys))
        {
            merged.Add(key);
        }

        return merged.ToArray();
    }
}
