using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementItemNumbering
{
    /// <summary>
    /// Explicitly compacts all item numbers by final cutting length. Unlike
    /// <see cref="AssignElementIds(IEnumerable{TimberElementMeasurement})"/>,
    /// this method intentionally does not preserve gaps or previous numbers.
    /// </summary>
    public static IReadOnlyList<TimberElementRenumberingAssignment> RenumberElementIdsByCuttingLength(
        IEnumerable<TimberElementMeasurement> measurements)
    {
        if (measurements is null)
        {
            throw new ArgumentNullException(nameof(measurements));
        }

        var materialized = measurements.ToList();
        var prefixesBySeries = ReadSeriesPrefixes(materialized.Select(measurement => measurement.Data));
        var newIdsBySignature = materialized
            .Select(TimberElementSignature.FromMeasurement)
            .Distinct()
            .OrderBy(signature => TimberElementSeriesRules.GetKey(signature).ElementType)
            .ThenBy(signature => TimberElementSeriesRules.GetKey(signature).CustomElementTypeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signature => signature.CuttingLengthMm)
            .ThenBy(signature => signature.WidthMm)
            .ThenBy(signature => signature.HeightMm)
            .ThenBy(signature => signature.Material, StringComparer.Ordinal)
            .GroupBy(TimberElementSeriesRules.GetKey)
            .SelectMany(group => group.Select((signature, index) => new
            {
                Signature = signature,
                ElementId = TimberElementIdentityRules.CreateElementId(
                    prefixesBySeries[group.Key],
                    index + 1),
            }))
            .ToDictionary(item => item.Signature, item => item.ElementId);

        return materialized
            .Select(measurement =>
            {
                var signature = TimberElementSignature.FromMeasurement(measurement);
                return new TimberElementRenumberingAssignment(
                    measurement,
                    signature,
                    measurement.Data.ElementId,
                    newIdsBySignature[signature]);
            })
            .ToList();
    }

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
        var allocatedNumbersBySeries = ReadAllocatedNumbers(materialized);
        var prefixesBySeries = ReadSeriesPrefixes(
            materialized.Select(candidate => candidate.Measurement.Data));
        var result = new List<TimberElementItemAssignment>(materialized.Count);

        foreach (var candidate in materialized)
        {
            var measurement = candidate.Measurement;
            var signature = TimberElementSignature.FromMeasurement(measurement);
            if (!assignedIdsBySignature.TryGetValue(signature, out var elementId))
            {
                var seriesKey = TimberElementSeriesRules.GetKey(signature);
                elementId = CreateNextElementId(
                    seriesKey,
                    prefixesBySeries[seriesKey],
                    allocatedNumbersBySeries);
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
            if (TimberElementIdentityRules.TryParseElementNumber(elementId, measurement.Data) is null ||
                assignments.ContainsKey(signature) ||
                !ElementIdBelongsToPreferredSignature(candidates, elementId, signature))
            {
                continue;
            }

            assignments.Add(signature, elementId.Trim());
        }

        return assignments;
    }

    private static Dictionary<TimberElementSeriesKey, HashSet<int>> ReadAllocatedNumbers(
        IReadOnlyList<TimberElementItemNumberingCandidate> candidates)
    {
        var allocatedNumbersBySeries = candidates
            .Select(candidate => TimberElementSeriesRules.GetKey(candidate.Measurement.Data))
            .Distinct()
            .ToDictionary(key => key, _ => new HashSet<int>());

        foreach (var candidate in candidates)
        {
            var measurement = candidate.Measurement;
            var key = TimberElementSeriesRules.GetKey(measurement.Data);
            var number = TimberElementIdentityRules.TryParseElementNumber(
                measurement.Data.ElementId,
                measurement.Data);
            if (number is null)
            {
                continue;
            }

            allocatedNumbersBySeries[key].Add(number.Value);
        }

        return allocatedNumbersBySeries;
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
        TimberElementSeriesKey key,
        string prefix,
        IDictionary<TimberElementSeriesKey, HashSet<int>> allocatedNumbersBySeries)
    {
        var allocatedNumbers = allocatedNumbersBySeries[key];
        var number = 1;
        while (allocatedNumbers.Contains(number))
        {
            number++;
        }

        allocatedNumbers.Add(number);
        return TimberElementIdentityRules.CreateElementId(prefix, number);
    }

    private static Dictionary<TimberElementSeriesKey, string> ReadSeriesPrefixes(
        IEnumerable<TimberElementData> elements)
    {
        var prefixes = new Dictionary<TimberElementSeriesKey, string>();
        var seriesByPrefix = new Dictionary<string, TimberElementSeriesKey>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var data in elements)
        {
            var key = TimberElementSeriesRules.GetKey(data);
            var prefix = TimberElementSeriesRules.GetPrefix(data);
            if (prefixes.TryGetValue(key, out var existing) &&
                !string.Equals(existing, prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Numbering series '{key.CustomElementTypeId}' contains conflicting prefixes.");
            }

            prefixes[key] = prefix;
            if (seriesByPrefix.TryGetValue(prefix, out var existingKey) &&
                existingKey != key)
            {
                throw new ArgumentException(
                    $"Prefix '{prefix}' is assigned to multiple numbering series.");
            }

            seriesByPrefix[prefix] = key;
        }

        return prefixes;
    }
}
