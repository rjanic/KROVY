using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementSimilarityFilter
{
    private const double DimensionEpsilon = 0.000001d;

    public static bool Matches(
        TimberElementSnapshot? seed,
        TimberElementSnapshot? candidate,
        TimberElementSimilarityCriteria criteria,
        double roundingIncrementMm = TimberCalculator.CuttingLengthRoundingIncrementMm)
    {
        if (criteria is null)
        {
            throw new ArgumentNullException(nameof(criteria));
        }

        if (!IsFinite(criteria.CuttingLengthToleranceMm) ||
            criteria.CuttingLengthToleranceMm < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "Cutting-length tolerance must be a finite nonnegative value.");
        }

        try
        {
            if (!IsUsable(seed) || !IsUsable(candidate))
            {
                return false;
            }

            var seedData = seed!.Data;
            var candidateData = candidate!.Data;

            if (criteria.MatchElementType &&
                seedData.ElementType != candidateData.ElementType)
            {
                return false;
            }

            if (criteria.MatchCrossSection &&
                (!NearlyEqual(seedData.WidthMm, candidateData.WidthMm) ||
                 !NearlyEqual(seedData.HeightMm, candidateData.HeightMm)))
            {
                return false;
            }

            if (criteria.MatchMaterial &&
                !string.Equals(
                    seedData.Material,
                    candidateData.Material,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (criteria.MatchElementId &&
                !string.Equals(
                    seedData.ElementId,
                    candidateData.ElementId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (criteria.MatchCustomElementTypeId &&
                !string.Equals(
                    seedData.CustomElementTypeId,
                    candidateData.CustomElementTypeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (criteria.MatchCuttingLength)
            {
                var seedLength = TimberElementMeasurer.Measure(seed, roundingIncrementMm).CuttingLengthMm;
                var candidateLength = TimberElementMeasurer.Measure(candidate, roundingIncrementMm).CuttingLengthMm;
                if (Math.Abs(seedLength - candidateLength) >
                    criteria.CuttingLengthToleranceMm + DimensionEpsilon)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            // Invalid or incomplete metadata is simply not a filter match.
            return false;
        }
    }

    private static bool IsUsable(TimberElementSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Data is null)
        {
            return false;
        }

        var data = snapshot.Data;
        if (!Enum.IsDefined(typeof(TimberElementType), data.ElementType) ||
            string.IsNullOrWhiteSpace(data.ElementId) ||
            string.IsNullOrWhiteSpace(data.Material) ||
            !IsFinite(data.WidthMm) ||
            !IsFinite(data.HeightMm) ||
            data.WidthMm <= 0d ||
            data.HeightMm <= 0d ||
            snapshot.PlanLengthMm is { } planLength &&
            (!IsFinite(planLength) || planLength < 0d))
        {
            return false;
        }

        return data.ElementType != TimberElementType.Custom ||
            !string.IsNullOrWhiteSpace(data.CustomElementTypeId);
    }

    private static bool NearlyEqual(double first, double second) =>
        Math.Abs(first - second) <= DimensionEpsilon;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
