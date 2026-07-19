using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementItemNumbering
{
    public static IReadOnlyList<TimberElementItemAssignment> AssignElementIds(
        IEnumerable<TimberElementMeasurement> measurements)
    {
        if (measurements is null)
        {
            throw new ArgumentNullException(nameof(measurements));
        }

        return AssignElementIds(measurements.Select(measurement =>
            new TimberElementItemNumberingCandidate(measurement, IsChanged: false)));
    }

    public static IReadOnlyList<TimberElementItemAssignment> AssignElementIds(
        IEnumerable<TimberElementItemNumberingCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var materialized = candidates.ToList();
        var assignedIdsBySignature = FindExistingStableAssignments(materialized);
        var allocatedNumbersByType = ReadAllocatedNumbers(materialized);
        var result = new List<TimberElementItemAssignment>(materialized.Count);

        foreach (var candidate in materialized)
        {
            var measurement = candidate.Measurement;
            var signature = TimberElementSignature.FromMeasurement(measurement);
            if (!assignedIdsBySignature.TryGetValue(signature, out var elementId))
            {
                elementId = CreateNextElementId(signature.ElementType, allocatedNumbersByType);
                assignedIdsBySignature.Add(signature, elementId);
            }

            result.Add(new TimberElementItemAssignment(measurement, signature, elementId));
        }

        return result;
    }

    private static Dictionary<TimberElementSignature, string> FindExistingStableAssignments(
        IReadOnlyList<TimberElementItemNumberingCandidate> candidates)
    {
        var assignments = new Dictionary<TimberElementSignature, string>();

        foreach (var candidate in candidates)
        {
            var measurement = candidate.Measurement;
            var signature = TimberElementSignature.FromMeasurement(measurement);
            var elementId = measurement.Data.ElementId;
            if (TimberElementIdentityRules.TryParseElementNumber(elementId, signature.ElementType) is null ||
                assignments.ContainsKey(signature) ||
                !ElementIdBelongsToPreferredSignature(candidates, elementId, signature))
            {
                continue;
            }

            assignments.Add(signature, elementId.Trim());
        }

        return assignments;
    }

    private static Dictionary<TimberElementType, HashSet<int>> ReadAllocatedNumbers(
        IReadOnlyList<TimberElementItemNumberingCandidate> candidates)
    {
        var allocatedNumbersByType = new Dictionary<TimberElementType, HashSet<int>>();

        foreach (TimberElementType type in Enum.GetValues(typeof(TimberElementType)))
        {
            allocatedNumbersByType[type] = new HashSet<int>();
        }

        foreach (var candidate in candidates)
        {
            var measurement = candidate.Measurement;
            var type = measurement.Data.ElementType;
            var number = TimberElementIdentityRules.TryParseElementNumber(measurement.Data.ElementId, type);
            if (number is null)
            {
                continue;
            }

            allocatedNumbersByType[type].Add(number.Value);
        }

        return allocatedNumbersByType;
    }

    private static bool ElementIdBelongsToPreferredSignature(
        IReadOnlyList<TimberElementItemNumberingCandidate> candidates,
        string elementId,
        TimberElementSignature signature)
    {
        var owners = candidates
            .Where(candidate => string.Equals(
                candidate.Measurement.Data.ElementId,
                elementId,
                StringComparison.OrdinalIgnoreCase))
            .Select((candidate, index) => new
            {
                Signature = TimberElementSignature.FromMeasurement(candidate.Measurement),
                candidate.IsChanged,
                Index = index,
            })
            .GroupBy(candidate => candidate.Signature)
            .Select((group, index) => new
            {
                Signature = group.Key,
                Count = group.Count(),
                UnchangedCount = group.Count(candidate => !candidate.IsChanged),
                FirstIndex = group.Min(candidate => candidate.Index),
            })
            .OrderByDescending(group => group.UnchangedCount)
            .ThenByDescending(group => group.Count)
            .ThenBy(group => group.FirstIndex)
            .ToList();

        return owners.Count > 0 && owners[0].Signature == signature;
    }

    private static string CreateNextElementId(
        TimberElementType type,
        IDictionary<TimberElementType, HashSet<int>> allocatedNumbersByType)
    {
        var allocatedNumbers = allocatedNumbersByType[type];
        var number = 1;
        while (allocatedNumbers.Contains(number))
        {
            number++;
        }

        allocatedNumbers.Add(number);
        return TimberElementIdentityRules.CreateElementId(type, number);
    }
}
