using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Classifies a coherent rigid planar translation of the complete SimpleGable roof
/// GROUP (1 source + 7 display) from a true pre-command baseline to post-grip geometry.
/// Does not invent side-resize. Does not mutate geometry.
/// </summary>
public static class RoofRigidGroupTransformRules
{
    public const double ToleranceMm = RoofGroupGripResizeAdoptionRules.GripAdoptionToleranceMm;

    public static RoofRigidGroupTransformResult TryClassifyTranslation(
        IReadOnlyList<RoofPoint2D> preCommandSourceVertices,
        IReadOnlyList<RoofPoint2D> currentSourceVertices,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> preCommandDisplay,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> currentDisplay,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> canonicalDisplayFromCurrentSource,
        double toleranceMm = ToleranceMm)
    {
        if (preCommandSourceVertices is null || preCommandSourceVertices.Count != 4 ||
            currentSourceVertices is null || currentSourceVertices.Count != 4)
        {
            return RoofRigidGroupTransformResult.Reject("source-vertex-count");
        }

        if (!RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(preCommandDisplay) ||
            !RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(currentDisplay) ||
            !RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(canonicalDisplayFromCurrentSource))
        {
            return RoofRigidGroupTransformResult.Reject("incomplete-display-roles");
        }

        if (!TryUniquePlanarTranslation(
                preCommandSourceVertices,
                currentSourceVertices,
                toleranceMm,
                out var dx,
                out var dy))
        {
            if (VerticesEqual(preCommandSourceVertices, currentSourceVertices, toleranceMm))
            {
                return RoofRigidGroupTransformResult.Reject("source-unchanged");
            }

            return RoofRigidGroupTransformResult.Reject("source-not-unique-translation");
        }

        if (Math.Abs(dx) <= toleranceMm && Math.Abs(dy) <= toleranceMm)
        {
            return RoofRigidGroupTransformResult.Reject("source-unchanged");
        }

        if (!SourceShapeRigidEquivalent(
                preCommandSourceVertices,
                currentSourceVertices,
                toleranceMm))
        {
            return RoofRigidGroupTransformResult.Reject("source-not-rigid-shape");
        }

        if (!TryUniqueDisplayTranslation(
                preCommandDisplay,
                currentDisplay,
                toleranceMm,
                out var displayDx,
                out var displayDy,
                out var displayDz))
        {
            return RoofRigidGroupTransformResult.Reject("display-not-unique-translation");
        }

        if (Math.Abs(displayDx - dx) > toleranceMm ||
            Math.Abs(displayDy - dy) > toleranceMm)
        {
            return RoofRigidGroupTransformResult.Reject("source-display-transform-mismatch");
        }

        if (!TransformedDisplayMatches(
                preCommandDisplay,
                currentDisplay,
                dx,
                dy,
                displayDz,
                toleranceMm))
        {
            return RoofRigidGroupTransformResult.Reject("transformed-display-mismatch");
        }

        if (!WireframesMatch(canonicalDisplayFromCurrentSource, currentDisplay, toleranceMm))
        {
            return RoofRigidGroupTransformResult.Reject("current-not-canonical-wireframe");
        }

        return RoofRigidGroupTransformResult.AcceptTranslation(dx, dy, displayDz);
    }

    private static bool TryUniquePlanarTranslation(
        IReadOnlyList<RoofPoint2D> before,
        IReadOnlyList<RoofPoint2D> after,
        double toleranceMm,
        out double dx,
        out double dy)
    {
        dx = after[0].X - before[0].X;
        dy = after[0].Y - before[0].Y;
        for (var i = 1; i < 4; i++)
        {
            var idx = after[i].X - before[i].X;
            var idy = after[i].Y - before[i].Y;
            if (Math.Abs(idx - dx) > toleranceMm || Math.Abs(idy - dy) > toleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SourceShapeRigidEquivalent(
        IReadOnlyList<RoofPoint2D> before,
        IReadOnlyList<RoofPoint2D> after,
        double toleranceMm)
    {
        for (var i = 0; i < 4; i++)
        {
            var j = (i + 1) % 4;
            var beforeLen = before[i].DistanceTo(before[j]);
            var afterLen = after[i].DistanceTo(after[j]);
            if (Math.Abs(beforeLen - afterLen) > toleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryUniqueDisplayTranslation(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> before,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> after,
        double toleranceMm,
        out double dx,
        out double dy,
        out double dz)
    {
        dx = dy = dz = 0d;
        var seeded = false;
        foreach (RoofDisplayEdgeRole role in Enum.GetValues(typeof(RoofDisplayEdgeRole)))
        {
            var b = before[role];
            var a = after[role];
            if (!TrySegmentTranslation(b, a, toleranceMm, out var sdx, out var sdy, out var sdz))
            {
                return false;
            }

            if (!seeded)
            {
                dx = sdx;
                dy = sdy;
                dz = sdz;
                seeded = true;
                continue;
            }

            if (Math.Abs(sdx - dx) > toleranceMm ||
                Math.Abs(sdy - dy) > toleranceMm ||
                Math.Abs(sdz - dz) > toleranceMm)
            {
                return false;
            }
        }

        return seeded;
    }

    private static bool TrySegmentTranslation(
        RoofSegment3D before,
        RoofSegment3D after,
        double toleranceMm,
        out double dx,
        out double dy,
        out double dz)
    {
        // Prefer matching orientation; allow reversed endpoints.
        if (TryEndpointPairTranslation(
                before.Start,
                before.End,
                after.Start,
                after.End,
                toleranceMm,
                out dx,
                out dy,
                out dz))
        {
            return true;
        }

        return TryEndpointPairTranslation(
            before.Start,
            before.End,
            after.End,
            after.Start,
            toleranceMm,
            out dx,
            out dy,
            out dz);
    }

    private static bool TryEndpointPairTranslation(
        RoofPoint3D beforeStart,
        RoofPoint3D beforeEnd,
        RoofPoint3D afterStart,
        RoofPoint3D afterEnd,
        double toleranceMm,
        out double dx,
        out double dy,
        out double dz)
    {
        dx = afterStart.X - beforeStart.X;
        dy = afterStart.Y - beforeStart.Y;
        dz = afterStart.Z - beforeStart.Z;
        var edx = afterEnd.X - beforeEnd.X;
        var edy = afterEnd.Y - beforeEnd.Y;
        var edz = afterEnd.Z - beforeEnd.Z;
        return Math.Abs(edx - dx) <= toleranceMm &&
               Math.Abs(edy - dy) <= toleranceMm &&
               Math.Abs(edz - dz) <= toleranceMm;
    }

    private static bool TransformedDisplayMatches(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> before,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> after,
        double dx,
        double dy,
        double dz,
        double toleranceMm)
    {
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var actual))
            {
                return false;
            }

            var expected = Translate(pair.Value, dx, dy, dz);
            if (!SegmentsEqual(expected, actual, toleranceMm))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WireframesMatch(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> canonical,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> observed,
        double toleranceMm)
    {
        foreach (var pair in canonical)
        {
            if (!observed.TryGetValue(pair.Key, out var actual) ||
                !SegmentsEqual(pair.Value, actual, toleranceMm))
            {
                return false;
            }
        }

        return true;
    }

    private static RoofSegment3D Translate(RoofSegment3D segment, double dx, double dy, double dz) =>
        new(
            new RoofPoint3D(segment.Start.X + dx, segment.Start.Y + dy, segment.Start.Z + dz),
            new RoofPoint3D(segment.End.X + dx, segment.End.Y + dy, segment.End.Z + dz));

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

    private static bool VerticesEqual(
        IReadOnlyList<RoofPoint2D> left,
        IReadOnlyList<RoofPoint2D> right,
        double toleranceMm)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].DistanceTo(right[i]) > toleranceMm)
            {
                return false;
            }
        }

        return true;
    }
}
