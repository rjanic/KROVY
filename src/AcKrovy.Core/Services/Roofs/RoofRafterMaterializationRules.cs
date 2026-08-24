using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Defensive CAD-neutral contract for turning a solved rafter layout into one
/// persistent generated set. It validates the solved plane shape and deterministic
/// face/station identity without introducing a second geometry authority.
/// </summary>
public static class RoofRafterMaterializationRules
{
    public static bool IsConsistent(IRoofGeometry geometry, RoofRafterLayout layout)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var expectedFaces = geometry switch
        {
            MonopitchRoofGeometry => new[] { RafterRoofFace.Face0 },
            SimpleGableRoofGeometry => new[]
            {
                RafterRoofFace.Face0,
                RafterRoofFace.Face1,
            },
            _ => Array.Empty<RafterRoofFace>(),
        };
        if (expectedFaces.Length == 0 ||
            layout.Planes.Count != expectedFaces.Length ||
            !layout.Planes.Select(item => item.Face).SequenceEqual(expectedFaces) ||
            layout.StationCount < 2 ||
            layout.IntervalCount != layout.StationCount - 1 ||
            layout.Rafters.Count != layout.StationCount * expectedFaces.Length ||
            layout.Signature.IndexOf(geometry.Signature, StringComparison.Ordinal) < 0)
        {
            return false;
        }

        var keys = new HashSet<RoofGeneratedMemberKey>();
        for (var stationIndex = 0; stationIndex < layout.StationCount; stationIndex++)
        {
            for (var planeIndex = 0; planeIndex < layout.Planes.Count; planeIndex++)
            {
                var index = stationIndex * layout.Planes.Count + planeIndex;
                var rafter = layout.Rafters[index];
                var plane = layout.Planes[planeIndex];
                var planLength = rafter.PlanStart.DistanceTo(rafter.PlanEnd);
                var expectedTrueLength = planLength /
                    Math.Cos(rafter.SlopeDegrees * Math.PI / 180d);
                if (rafter.Face != plane.Face ||
                    rafter.StationIndex != stationIndex ||
                    rafter.StationCount != layout.StationCount ||
                    !IsFinite(rafter.StationFraction) ||
                    !IsFinite(rafter.StationPositionMm) ||
                    !IsFinite(planLength) ||
                    planLength <= RoofRafterLayoutSolver.CoordinateToleranceMm ||
                    !NearlyEqual(rafter.PlanLengthMm, planLength) ||
                    !NearlyEqual(rafter.TrueLengthMm, expectedTrueLength) ||
                    !NearlyEqual(rafter.SlopeDegrees, plane.SlopeDegrees) ||
                    !keys.Add(rafter.LogicalKey))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool NearlyEqual(double first, double second)
    {
        if (!IsFinite(first) || !IsFinite(second))
        {
            return false;
        }

        var scale = Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second)));
        return Math.Abs(first - second) <= 1e-9d * scale;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
