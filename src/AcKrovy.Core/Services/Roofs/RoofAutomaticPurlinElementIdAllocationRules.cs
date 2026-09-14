using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral ElementId assignment for automatic Purlin materialization.
/// ElementId is a globally unique timber identity, not geometry-key identity.
/// Matching members preserve a valid current ID unless another owner holds it.
/// </summary>
public static class RoofAutomaticPurlinElementIdAllocationRules
{
    public static IReadOnlyList<string> Assign(
        TimberElementType type,
        IReadOnlyList<RoofAutomaticPurlinElementIdRequest>? requests,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? ownersByElementId)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        if (ownersByElementId is null)
        {
            throw new ArgumentNullException(nameof(ownersByElementId));
        }

        var prefix = TimberElementIdentityPrefixes.GetPrefix(type);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assigned = new string[requests.Count];
        var needsAllocation = new List<int>(requests.Count);

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index] ??
                throw new ArgumentException("ElementId request is required.", nameof(requests));
            var current = NormalizeElementId(request.CurrentElementId);
            if (request.EntityToken is not null &&
                IsPreservable(current, prefix, request.EntityToken, ownersByElementId) &&
                reserved.Add(current!))
            {
                assigned[index] = current!;
                continue;
            }

            needsAllocation.Add(index);
        }

        var nextNumber = 1;
        foreach (var index in needsAllocation)
        {
            string candidate;
            do
            {
                candidate = TimberElementIdentityRules.CreateElementId(prefix, nextNumber++);
            }
            while (reserved.Contains(candidate) ||
                   IsOwnedByOther(
                       candidate,
                       requests[index].EntityToken,
                       ownersByElementId,
                       requests,
                       assigned));

            reserved.Add(candidate);
            assigned[index] = candidate;
        }

        return assigned;
    }

    public static bool IsPreservable(
        string? elementId,
        string prefix,
        string entityToken,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ownersByElementId)
    {
        if (string.IsNullOrWhiteSpace(entityToken))
        {
            return false;
        }

        var normalized = NormalizeElementId(elementId);
        if (normalized is null ||
            TimberElementIdentityRules.TryParseElementNumber(normalized, prefix) is not > 0)
        {
            return false;
        }

        if (!ownersByElementId.TryGetValue(normalized, out var owners) ||
            owners is null ||
            owners.Count == 0)
        {
            // Current ID is absent from the global scan. The matching entity may still
            // keep it only when no other scanned owner exists (none do).
            return true;
        }

        foreach (var owner in owners)
        {
            if (!string.Equals(owner, entityToken, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOwnedByOther(
        string candidate,
        string? selfToken,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ownersByElementId,
        IReadOnlyList<RoofAutomaticPurlinElementIdRequest> requests,
        IReadOnlyList<string?> assigned)
    {
        if (!ownersByElementId.TryGetValue(candidate, out var owners) || owners is null)
        {
            return false;
        }

        foreach (var owner in owners)
        {
            if (selfToken is not null &&
                string.Equals(owner, selfToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsAbandonedByMatchingBatch(owner, candidate, requests, assigned))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsAbandonedByMatchingBatch(
        string ownerToken,
        string candidate,
        IReadOnlyList<RoofAutomaticPurlinElementIdRequest> requests,
        IReadOnlyList<string?> assigned)
    {
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request.EntityToken is null ||
                !string.Equals(
                    request.EntityToken,
                    ownerToken,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var current = NormalizeElementId(request.CurrentElementId);
            if (!string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Matching batch member that currently holds the ID but was not preserved.
            return assigned[index] is null ||
                   !string.Equals(assigned[index], candidate, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? NormalizeElementId(string? elementId)
    {
        if (elementId is null || string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        return elementId.Trim();
    }
}

/// <summary>One ElementId assignment request for automatic Purlin materialization.</summary>
public sealed record RoofAutomaticPurlinElementIdRequest(
    string? EntityToken,
    string CurrentElementId);
