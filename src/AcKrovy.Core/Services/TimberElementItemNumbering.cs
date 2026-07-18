using System.Text.RegularExpressions;
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

        var materialized = measurements.ToList();
        var assignedIdsBySignature = new Dictionary<TimberElementSignature, string>();
        var nextNumberByType = InitializeNextNumbers(materialized);
        var result = new List<TimberElementItemAssignment>(materialized.Count);

        foreach (var measurement in materialized)
        {
            var signature = TimberElementSignature.FromMeasurement(measurement);
            if (!assignedIdsBySignature.TryGetValue(signature, out var elementId))
            {
                elementId = FindStableElementId(materialized, signature)
                    ?? CreateNextElementId(signature.ElementType, nextNumberByType);
                assignedIdsBySignature.Add(signature, elementId);
            }

            result.Add(new TimberElementItemAssignment(measurement, signature, elementId));
        }

        return result;
    }

    private static Dictionary<TimberElementType, int> InitializeNextNumbers(
        IReadOnlyList<TimberElementMeasurement> measurements)
    {
        var nextNumberByType = new Dictionary<TimberElementType, int>();

        foreach (var measurement in measurements)
        {
            var type = measurement.Data.ElementType;
            var number = TryParseElementNumber(measurement.Data.ElementId, type);
            if (number is null)
            {
                continue;
            }

            if (!nextNumberByType.TryGetValue(type, out var highest) || number.Value > highest)
            {
                nextNumberByType[type] = number.Value;
            }
        }

        foreach (TimberElementType type in Enum.GetValues(typeof(TimberElementType)))
        {
            nextNumberByType[type] = nextNumberByType.TryGetValue(type, out var highest)
                ? highest + 1
                : 1;
        }

        return nextNumberByType;
    }

    private static string? FindStableElementId(
        IReadOnlyList<TimberElementMeasurement> measurements,
        TimberElementSignature signature)
    {
        return measurements
            .Where(measurement =>
                TimberElementSignature.FromMeasurement(measurement) == signature &&
                TryParseElementNumber(measurement.Data.ElementId, signature.ElementType) is not null &&
                ElementIdBelongsToPreferredSignature(measurements, measurement.Data.ElementId, signature))
            .Select(measurement => measurement.Data.ElementId)
            .OrderBy(elementId => TryParseElementNumber(elementId, signature.ElementType))
            .ThenBy(elementId => elementId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool ElementIdBelongsToPreferredSignature(
        IReadOnlyList<TimberElementMeasurement> measurements,
        string elementId,
        TimberElementSignature signature)
    {
        var firstOwner = measurements.FirstOrDefault(measurement => string.Equals(
            measurement.Data.ElementId,
            elementId,
            StringComparison.OrdinalIgnoreCase));

        return firstOwner is not null && TimberElementSignature.FromMeasurement(firstOwner) == signature;
    }

    private static string CreateNextElementId(
        TimberElementType type,
        IDictionary<TimberElementType, int> nextNumberByType)
    {
        var number = nextNumberByType[type];
        nextNumberByType[type] = number + 1;
        return TimberElementIdentityRules.CreateElementId(type, number);
    }

    private static int? TryParseElementNumber(string elementId, TimberElementType type)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var prefix = TimberElementLabels.Prefix(type);
        var match = Regex.Match(
            elementId.Trim(),
            $"^{Regex.Escape(prefix)}(?<number>\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : null;
    }
}
