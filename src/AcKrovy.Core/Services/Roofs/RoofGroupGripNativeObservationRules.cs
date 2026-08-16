using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Portable checks for native GROUP GRIP_STRETCH display observations captured
/// before plugin rebuild can restore canonical geometry.
/// </summary>
public static class RoofGroupGripNativeObservationRules
{
    public static bool IsCompleteSevenRoles(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D>? observed)
    {
        if (observed is null || observed.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            return false;
        }

        foreach (RoofDisplayEdgeRole role in Enum.GetValues(typeof(RoofDisplayEdgeRole)))
        {
            if (!observed.ContainsKey(role))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasMeaningfulDeltaFromExpected(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> expected,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> observed,
        double toleranceMm)
    {
        if (!IsCompleteSevenRoles(expected) || !IsCompleteSevenRoles(observed))
        {
            return false;
        }

        foreach (var pair in expected)
        {
            if (!observed.TryGetValue(pair.Key, out var actual))
            {
                return false;
            }

            if (!SegmentsEqual(pair.Value, actual, toleranceMm))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsEqual(
        RoofSegment3D first,
        RoofSegment3D second,
        double toleranceMm) =>
        PointsEqual(first.Start, second.Start, toleranceMm) &&
        PointsEqual(first.End, second.End, toleranceMm) ||
        PointsEqual(first.Start, second.End, toleranceMm) &&
        PointsEqual(first.End, second.Start, toleranceMm);

    private static bool PointsEqual(RoofPoint3D first, RoofPoint3D second, double toleranceMm) =>
        Math.Abs(first.X - second.X) <= toleranceMm &&
        Math.Abs(first.Y - second.Y) <= toleranceMm &&
        Math.Abs(first.Z - second.Z) <= toleranceMm;
}
