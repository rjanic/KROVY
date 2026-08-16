using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Derives a unique supported rectangular side resize from GROUP GRIP_STRETCH display
/// mutation while the semantic source remains RigidEquivalent.
/// Expected display comes from the unchanged source; observed display is post-grip.
/// Adoption is erase/write of source vertices only when the mapping is unique.
/// </summary>
public static class RoofGroupGripResizeAdoptionRules
{
    /// <summary>
    /// Planar grip noise tolerance for comparing expected vs observed display and
    /// deciding which rectangle edge-family length changed.
    /// </summary>
    public const double GripAdoptionToleranceMm = 0.01d;

    public static RoofGroupGripResizeAdoptionResult TryDeriveSupportedSideResize(
        IReadOnlyList<RoofPoint2D> currentSourceVertices,
        RoofDefinitionData definition,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> expectedDisplay,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> observedDisplay)
    {
        if (currentSourceVertices is null || currentSourceVertices.Count != 4)
        {
            return RoofGroupGripResizeAdoptionResult.Reject("source-vertex-count");
        }

        if (definition is null ||
            definition.SchemaVersion != RoofDefinitionDataSchema.CurrentVersion ||
            definition.RigidFootprint is null ||
            definition.RidgeEdgeFamily is not (
                RoofRidgeEdgeFamily.SourceEdge01 or RoofRidgeEdgeFamily.SourceEdge12))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("definition");
        }

        if (!HasAllRoles(expectedDisplay) || !HasAllRoles(observedDisplay))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("missing-display-roles");
        }

        if (!TryBuildRectangleFromEaves(
                observedDisplay[RoofDisplayEdgeRole.Eave0],
                observedDisplay[RoofDisplayEdgeRole.Eave1],
                out var observedCorners))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("observed-eaves-not-rectangle");
        }

        if (!TryMatchSourceVertexOrder(
                currentSourceVertices,
                observedCorners,
                out var orderedCandidate))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("ambiguous-corner-order");
        }

        if (!TryClassifySideResize(
                currentSourceVertices,
                orderedCandidate,
                definition.RidgeEdgeFamily!.Value,
                out var kind))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("not-unique-side-resize");
        }

        var adoptedInput = new RoofFootprintInput(orderedCandidate, true, false, true);
        var validation = RoofFootprintValidator.Validate(adoptedInput);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return RoofGroupGripResizeAdoptionResult.Reject("adopted-footprint-invalid");
        }

        var classification = RoofDefinitionPersistence.Classify(
            adoptedInput,
            validation.Footprint,
            definition);
        if (classification.Kind != RoofSourceChangeKind.SupportedResize ||
            classification.Geometry is null)
        {
            return RoofGroupGripResizeAdoptionResult.Reject("not-supported-resize");
        }

        if (Math.Abs(classification.Geometry.SlopeDegrees - definition.SlopeDegrees) >
            GripAdoptionToleranceMm)
        {
            return RoofGroupGripResizeAdoptionResult.Reject("slope-changed");
        }

        var expectedAdopted = SimpleGableRoofWireframe.Create(
            classification.Geometry,
            ResolveCommonElevation(expectedDisplay));
        if (!WireframesMatch(expectedAdopted, observedDisplay))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("observed-not-wireframe-of-adopted");
        }

        // Final authority: adopted vertices must differ from current (real resize).
        if (VerticesEqual(currentSourceVertices, orderedCandidate))
        {
            return RoofGroupGripResizeAdoptionResult.Reject("no-geometry-change");
        }

        return new RoofGroupGripResizeAdoptionResult(
            true,
            orderedCandidate,
            kind,
            string.Empty);
    }

    private static bool HasAllRoles(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D>? display)
    {
        if (display is null || display.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            return false;
        }

        foreach (RoofDisplayEdgeRole role in Enum.GetValues(typeof(RoofDisplayEdgeRole)))
        {
            if (!display.ContainsKey(role))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildRectangleFromEaves(
        RoofSegment3D eave0,
        RoofSegment3D eave1,
        out IReadOnlyList<RoofPoint2D> corners)
    {
        corners = Array.Empty<RoofPoint2D>();
        var a0 = To2D(eave0.Start);
        var a1 = To2D(eave0.End);
        var b0 = To2D(eave1.Start);
        var b1 = To2D(eave1.End);
        var eave0Axis = Between(a0, a1);
        var eave1Axis = Between(b0, b1);
        var len0 = Length(eave0Axis);
        var len1 = Length(eave1Axis);
        if (len0 <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm ||
            len1 <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm ||
            Math.Abs(len0 - len1) > GripAdoptionToleranceMm)
        {
            return false;
        }

        if (!AreParallel(eave0Axis, eave1Axis))
        {
            return false;
        }

        // Orient eave1 the same direction as eave0 so corners wind consistently.
        if (Dot(eave0Axis, eave1Axis) < 0d)
        {
            (b0, b1) = (b1, b0);
        }

        var candidate = new[] { a0, a1, b1, b0 };
        var input = new RoofFootprintInput(candidate, true, false, true);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return false;
        }

        // Keep raw eave-derived corners (not canonicalized footprint order) so
        // source vertex indexing can be recovered uniquely.
        corners = candidate;
        return true;
    }

    private static bool TryMatchSourceVertexOrder(
        IReadOnlyList<RoofPoint2D> source,
        IReadOnlyList<RoofPoint2D> candidate,
        out IReadOnlyList<RoofPoint2D> ordered)
    {
        ordered = Array.Empty<RoofPoint2D>();
        double? bestScore = null;
        List<RoofPoint2D>? best = null;
        var ambiguous = false;

        foreach (var reversed in new[] { false, true })
        {
            var ring = reversed
                ? new[] { candidate[0], candidate[3], candidate[2], candidate[1] }
                : candidate.ToArray();
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var rotated = new RoofPoint2D[4];
                for (var i = 0; i < 4; i++)
                {
                    rotated[i] = ring[(i + rotation) % 4];
                }

                var score = 0d;
                for (var i = 0; i < 4; i++)
                {
                    score += DistanceSquared(source[i], rotated[i]);
                }

                if (bestScore is null || score + GripAdoptionToleranceMm * GripAdoptionToleranceMm < bestScore)
                {
                    bestScore = score;
                    best = rotated.ToList();
                    ambiguous = false;
                }
                else if (best is not null &&
                         Math.Abs(score - bestScore.Value) <=
                         GripAdoptionToleranceMm * GripAdoptionToleranceMm)
                {
                    if (!VerticesEqual(best, rotated))
                    {
                        ambiguous = true;
                    }
                }
            }
        }

        if (ambiguous || best is null)
        {
            return false;
        }

        ordered = best;
        return true;
    }

    private static bool TryClassifySideResize(
        IReadOnlyList<RoofPoint2D> current,
        IReadOnlyList<RoofPoint2D> adopted,
        RoofRidgeEdgeFamily ridgeFamily,
        out RoofGroupGripSideResizeKind kind)
    {
        kind = RoofGroupGripSideResizeKind.None;
        var current01 = current[0].DistanceTo(current[1]);
        var current12 = current[1].DistanceTo(current[2]);
        var adopted01 = adopted[0].DistanceTo(adopted[1]);
        var adopted12 = adopted[1].DistanceTo(adopted[2]);
        var edge01Changed = Math.Abs(current01 - adopted01) > GripAdoptionToleranceMm;
        var edge12Changed = Math.Abs(current12 - adopted12) > GripAdoptionToleranceMm;
        if (edge01Changed == edge12Changed)
        {
            return false;
        }

        var ridgeIsEdge01 = ridgeFamily == RoofRidgeEdgeFamily.SourceEdge01;
        if (edge01Changed)
        {
            kind = ridgeIsEdge01
                ? RoofGroupGripSideResizeKind.GableEnd
                : RoofGroupGripSideResizeKind.EaveSide;
        }
        else
        {
            kind = ridgeIsEdge01
                ? RoofGroupGripSideResizeKind.EaveSide
                : RoofGroupGripSideResizeKind.GableEnd;
        }

        return kind != RoofGroupGripSideResizeKind.None;
    }

    private static bool WireframesMatch(
        IReadOnlyList<RoofDisplayEdge> expectedAdopted,
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> observed)
    {
        foreach (var edge in expectedAdopted)
        {
            if (!observed.TryGetValue(edge.Role, out var actual) ||
                !SegmentsEqual(edge.Segment, actual))
            {
                return false;
            }
        }

        return true;
    }

    private static double ResolveCommonElevation(
        IReadOnlyDictionary<RoofDisplayEdgeRole, RoofSegment3D> expectedDisplay)
    {
        var eave = expectedDisplay[RoofDisplayEdgeRole.Eave0];
        return (eave.Start.Z + eave.End.Z) / 2d;
    }

    private static bool SegmentsEqual(RoofSegment3D first, RoofSegment3D second) =>
        PointsEqual(first.Start, second.Start) && PointsEqual(first.End, second.End) ||
        PointsEqual(first.Start, second.End) && PointsEqual(first.End, second.Start);

    private static bool PointsEqual(RoofPoint3D first, RoofPoint3D second) =>
        Math.Abs(first.X - second.X) <= GripAdoptionToleranceMm &&
        Math.Abs(first.Y - second.Y) <= GripAdoptionToleranceMm &&
        Math.Abs(first.Z - second.Z) <= GripAdoptionToleranceMm;

    private static bool VerticesEqual(
        IReadOnlyList<RoofPoint2D> left,
        IReadOnlyList<RoofPoint2D> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].DistanceTo(right[i]) > GripAdoptionToleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    private static RoofPoint2D To2D(RoofPoint3D point) => new(point.X, point.Y);

    private static (double X, double Y) Between(RoofPoint2D start, RoofPoint2D end) =>
        (end.X - start.X, end.Y - start.Y);

    private static double Length((double X, double Y) vector) =>
        Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double Dot((double X, double Y) first, (double X, double Y) second) =>
        first.X * second.X + first.Y * second.Y;

    private static bool AreParallel((double X, double Y) first, (double X, double Y) second)
    {
        var cross = first.X * second.Y - first.Y * second.X;
        var scale = Length(first) * Length(second);
        return scale > 0d && Math.Abs(cross) <= GripAdoptionToleranceMm * scale;
    }

    private static double DistanceSquared(RoofPoint2D first, RoofPoint2D second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
