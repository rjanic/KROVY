using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Defensive CAD-neutral contract for turning a solved rafter layout into one
/// persistent generated set. It validates the solved plane shape and deterministic
/// face/station identity without introducing a second geometry authority.
/// Ordinary automatic layouts may omit candidates suppressed by the drawing
/// minimum true-length policy; consistency is therefore sparse-safe.
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

        if (geometry is HipRoofGeometry hip)
        {
            return IsConsistentHip(hip, layout);
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
            layout.Rafters.Count > layout.StationCount * expectedFaces.Length ||
            layout.Signature.IndexOf(geometry.Signature, StringComparison.Ordinal) < 0)
        {
            return false;
        }

        var planeByFace = layout.Planes.ToDictionary(plane => plane.Face);
        var keys = new HashSet<RoofGeneratedMemberKey>();
        var stationFaces = new HashSet<(int StationIndex, RafterRoofFace Face)>();
        foreach (var rafter in layout.Rafters)
        {
            if (rafter.StationIndex < 0 ||
                rafter.StationIndex >= layout.StationCount ||
                rafter.StationCount != layout.StationCount ||
                !planeByFace.TryGetValue(rafter.Face, out var plane) ||
                !stationFaces.Add((rafter.StationIndex, rafter.Face)))
            {
                return false;
            }

            var planLength = rafter.PlanStart.DistanceTo(rafter.PlanEnd);
            var expectedTrueLength = planLength /
                Math.Cos(rafter.SlopeDegrees * Math.PI / 180d);
            if (rafter.Face != plane.Face ||
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

        return true;
    }

    private static bool IsConsistentHip(HipRoofGeometry geometry, RoofRafterLayout layout)
    {
        if (!layout.Signature.StartsWith(
                "ROOF_FACE_RAFTER_LAYOUT_V2;" + geometry.Topology.Signature + ";",
                StringComparison.Ordinal) ||
            layout.StationCount < 2 ||
            layout.Rafters.Count > layout.StationCount ||
            layout.Planes.Count != 1 ||
            layout.Planes[0].Face != RafterRoofFace.Face0)
        {
            return false;
        }

        var keys = new HashSet<RoofGeneratedMemberKey>();
        var stationIndexes = new HashSet<int>();
        foreach (var rafter in layout.Rafters)
        {
            var planLength = rafter.PlanStart.DistanceTo(rafter.PlanEnd);
            var expectedTrueLength = planLength /
                Math.Cos(rafter.SlopeDegrees * Math.PI / 180d);
            if (rafter.Face != RafterRoofFace.Face0 ||
                rafter.StationIndex < 0 ||
                rafter.StationIndex >= layout.StationCount ||
                rafter.StationCount != layout.StationCount ||
                !stationIndexes.Add(rafter.StationIndex) ||
                !IsFinite(rafter.SlopeDegrees) ||
                Math.Abs(rafter.SlopeDegrees) >= 90d ||
                !IsFinite(planLength) ||
                planLength <= RoofRafterLayoutSolver.CoordinateToleranceMm ||
                !NearlyEqual(rafter.PlanLengthMm, planLength) ||
                !NearlyEqual(rafter.TrueLengthMm, expectedTrueLength) ||
                !keys.Add(rafter.LogicalKey))
            {
                return false;
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
