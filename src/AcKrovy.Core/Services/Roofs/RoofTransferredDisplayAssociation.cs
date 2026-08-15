using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Associates a complete transferred display set with a selected source when the
/// stored owner handle no longer matches, without weakening display validation.
/// </summary>
public static class RoofTransferredDisplayAssociation
{
    public static bool TryMatch(
        string selectedOwnerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string expectedSignature,
        IReadOnlyList<RoofDisplayObservation> candidates,
        IReadOnlyCollection<string> liveForeignOwnerReferences,
        out RoofTransferredDisplayMatch match)
    {
        if (string.IsNullOrWhiteSpace(selectedOwnerReference))
        {
            throw new ArgumentException("Owner reference is required.", nameof(selectedOwnerReference));
        }

        if (expectedEdges is null)
        {
            throw new ArgumentNullException(nameof(expectedEdges));
        }
        if (expectedSignature is null)
        {
            throw new ArgumentNullException(nameof(expectedSignature));
        }
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }
        if (liveForeignOwnerReferences is null)
        {
            throw new ArgumentNullException(nameof(liveForeignOwnerReferences));
        }

        match = null!;
        var foreignOwners = liveForeignOwnerReferences.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(liveForeignOwnerReferences, StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, List<RoofDisplayObservation>>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in candidates)
        {
            var storedOwner = observation.OwnerReference;
            if (string.IsNullOrWhiteSpace(storedOwner) ||
                string.Equals(
                    storedOwner,
                    selectedOwnerReference,
                    StringComparison.OrdinalIgnoreCase) ||
                foreignOwners.Contains(storedOwner!))
            {
                continue;
            }

            if (!groups.TryGetValue(storedOwner!, out var group))
            {
                group = [];
                groups.Add(storedOwner!, group);
            }

            group.Add(observation);
        }

        RoofTransferredDisplayMatch? currentMatch = null;
        RoofTransferredDisplayMatch? futureMatch = null;
        var ambiguousFuture = false;
        foreach (var pair in groups)
        {
            var validation = RoofDisplayValidator.Validate(
                selectedOwnerReference,
                expectedEdges,
                expectedSignature,
                RemapOwner(pair.Value, selectedOwnerReference));
            if (validation.IsCurrent)
            {
                if (currentMatch is not null)
                {
                    return false;
                }

                currentMatch = new RoofTransferredDisplayMatch(pair.Key, validation);
                continue;
            }

            if (!validation.Issues.HasFlag(RoofDisplayValidationIssue.UnsupportedFutureSchema))
            {
                continue;
            }

            if (futureMatch is not null)
            {
                ambiguousFuture = true;
                continue;
            }

            futureMatch = new RoofTransferredDisplayMatch(pair.Key, validation);
        }

        if (currentMatch is not null)
        {
            match = currentMatch;
            return true;
        }

        if (!ambiguousFuture && futureMatch is not null)
        {
            match = futureMatch;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<RoofDisplayObservation> RemapOwner(
        IReadOnlyList<RoofDisplayObservation> observations,
        string ownerReference) =>
        observations.Select(observation => observation with
        {
            OwnerReference = ownerReference,
            Data = observation.Data is null
                ? null
                : observation.Data with { OwnerReference = ownerReference },
        }).ToArray();
}

public sealed record RoofTransferredDisplayMatch(
    string StoredOwnerReference,
    RoofDisplayValidationResult Validation);
