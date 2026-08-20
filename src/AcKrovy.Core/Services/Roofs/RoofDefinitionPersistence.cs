using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Creates and lazily reconstructs neutral persisted roof definitions.</summary>
public static class RoofDefinitionPersistence
{
    public static RoofDefinitionData Create(
        RoofFootprintInput source,
        RoofFootprint footprint,
        SimpleGableRoofGeometry geometry)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!TryReadSourceTopology(source, out var topology) ||
            !TryResolveRidgeEdgeFamily(
                topology,
                geometry.RidgeDirection,
                out var edgeFamily))
        {
            throw new ArgumentException(
                "Source topology cannot represent the solved ridge axis.",
                nameof(source));
        }

        var data = new RoofDefinitionData(
            RoofDefinitionDataSchema.CurrentVersion,
            RoofKind.SimpleGable,
            geometry.SlopeDegrees,
            RidgeEdgeFamily: edgeFamily,
            RigidFootprint: topology.Descriptor);
        _ = RoofDefinitionDataCodec.Encode(data);
        return data;
    }

    public static RoofDefinitionRestoreResult Restore(
        RoofFootprintInput source,
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var classified = Classify(source, footprint, data);
        return classified.Geometry is not null
            ? new RoofDefinitionRestoreResult(
                true,
                classified.Geometry,
                RoofDefinitionRestoreError.None)
            : Invalid(classified.Error);
    }

    /// <summary>
    /// Classifies the current source against persisted SimpleGable data without writing.
    /// Rigid MOVE/ROTATE and supported rectangular STRETCH both restore geometry;
    /// unsupported source shapes stay stale and produce no invented roof.
    /// </summary>
    public static RoofSourceChangeClassification Classify(
        RoofFootprintInput source,
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!RoofDefinitionDataCodec.TryValidate(data, out _))
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.InvalidDefinition,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        return data.SchemaVersion switch
        {
            RoofDefinitionDataSchema.LegacyAbsoluteVersion =>
                ClassifyV1(footprint, data),
            RoofDefinitionDataSchema.TopologyVersion or
            RoofDefinitionDataSchema.CurrentVersion =>
                ClassifyV2(source, footprint, data),
            _ => new RoofSourceChangeClassification(
                RoofSourceChangeKind.InvalidDefinition,
                null,
                RoofDefinitionRestoreError.InvalidDefinition),
        };
    }

    private static RoofDefinitionRestoreResult RestoreV1(
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (!string.Equals(
                footprint.Signature,
                data.FootprintSignature,
                StringComparison.Ordinal))
        {
            return Invalid(RoofDefinitionRestoreError.StaleFootprint);
        }

        if (!RoofDirection2D.TryCreate(
                data.RidgeDirectionX!.Value,
                data.RidgeDirectionY!.Value,
                out var direction))
        {
            return Invalid(RoofDefinitionRestoreError.InvalidDefinition);
        }

        return Solve(footprint, data.SlopeDegrees, direction);
    }

    private static RoofSourceChangeClassification ClassifyV1(
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        var restored = RestoreV1(footprint, data);
        return restored.IsValid
            ? new RoofSourceChangeClassification(
                RoofSourceChangeKind.RigidEquivalent,
                restored.Geometry,
                RoofDefinitionRestoreError.None)
            : new RoofSourceChangeClassification(
                restored.Error == RoofDefinitionRestoreError.InvalidDefinition
                    ? RoofSourceChangeKind.InvalidDefinition
                    : RoofSourceChangeKind.Unsupported,
                null,
                restored.Error);
    }

    private static RoofSourceChangeClassification ClassifyV2(
        RoofFootprintInput source,
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        var restored = RestoreV2(source, footprint, data);
        if (!restored.IsValid || restored.Geometry is null)
        {
            return new RoofSourceChangeClassification(
                restored.Error == RoofDefinitionRestoreError.InvalidDefinition
                    ? RoofSourceChangeKind.InvalidDefinition
                    : RoofSourceChangeKind.Unsupported,
                null,
                restored.Error);
        }

        if (!TryReadSourceTopology(source, out var topology))
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.Unsupported,
                null,
                RoofDefinitionRestoreError.StaleFootprint);
        }

        // Pure native winding reversal (AutoCAD STRETCH/GROUP) keeps edge-length pairs
        // swapped but is still RigidEquivalent — not a supported resize write.
        var kind = Matches(topology.Descriptor, data.RigidFootprint!) ||
                   MatchesOrientationFlippedRigid(topology.Descriptor, data.RigidFootprint!)
            ? RoofSourceChangeKind.RigidEquivalent
            : RoofSourceChangeKind.SupportedResize;
        return new RoofSourceChangeClassification(
            kind,
            restored.Geometry,
            RoofDefinitionRestoreError.None);
    }

    private static RoofDefinitionRestoreResult RestoreV2(
        RoofFootprintInput source,
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (data.RigidFootprint is null ||
            data.RidgeEdgeFamily is not (
                RoofRidgeEdgeFamily.SourceEdge01 or RoofRidgeEdgeFamily.SourceEdge12))
        {
            return Invalid(RoofDefinitionRestoreError.InvalidDefinition);
        }

        if (!TryResolveSourceTopologyForRestore(
                source,
                data.RigidFootprint,
                data.RidgeEdgeFamily.Value,
                out var topology,
                out var ridgeFamily,
                out _))
        {
            return Invalid(RoofDefinitionRestoreError.StaleFootprint);
        }

        var edge = ridgeFamily == RoofRidgeEdgeFamily.SourceEdge01
            ? topology.Edge01
            : topology.Edge12;
        if (!RoofDirection2D.TryCreate(edge.X, edge.Y, out var direction))
        {
            return Invalid(RoofDefinitionRestoreError.InvalidDefinition);
        }

        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(data.SlopeDegrees, direction)));
        return solved.IsValid && solved.Geometry is not null
            ? new RoofDefinitionRestoreResult(
                true,
                solved.Geometry,
                RoofDefinitionRestoreError.None)
            : Invalid(RoofDefinitionRestoreError.StaleFootprint);
    }

    /// <summary>
    /// Resolves native source topology for V2 restore. AutoCAD GROUP/STRETCH can reverse
    /// polyline winding while preserving the rectangle; that is not Unsupported.
    /// Opposite winding remaps SourceEdge01 ↔ SourceEdge12 on the reversed ring.
    /// </summary>
    private static bool TryResolveSourceTopologyForRestore(
        RoofFootprintInput source,
        RoofRigidFootprintDescriptor persisted,
        RoofRidgeEdgeFamily persistedFamily,
        out SourceTopology topology,
        out RoofRidgeEdgeFamily ridgeFamily,
        out string resolvePath)
    {
        topology = default;
        ridgeFamily = RoofRidgeEdgeFamily.Undefined;
        resolvePath = "none";
        if (persistedFamily is not (
                RoofRidgeEdgeFamily.SourceEdge01 or RoofRidgeEdgeFamily.SourceEdge12) ||
            !TryReadSourceTopology(source, out topology))
        {
            return false;
        }

        if (MatchesTopology(topology.Descriptor, persisted))
        {
            ridgeFamily = persistedFamily;
            resolvePath = "native";
            return true;
        }

        if (!TryReverseClosedRectangleVertices(source, out var reversed) ||
            !TryReadSourceTopology(reversed, out var reversedTopology) ||
            !MatchesTopology(reversedTopology.Descriptor, persisted))
        {
            resolvePath = "orientation-mismatch";
            return false;
        }

        // Full vertex-list reverse restores the persisted winding while keeping the
        // same Edge01/Edge12 length families (rectangle). Do not swap ridge family.
        topology = reversedTopology;
        ridgeFamily = persistedFamily;
        resolvePath = "orientation-flipped";
        return true;
    }

    private static bool TryReverseClosedRectangleVertices(
        RoofFootprintInput source,
        out RoofFootprintInput reversed)
    {
        reversed = source;
        if (source.Vertices is null || source.Vertices.Count < 4)
        {
            return false;
        }

        var vertices = source.Vertices.ToList();
        if (vertices.Count > 1 &&
            vertices[0].DistanceTo(vertices[vertices.Count - 1]) <=
                RoofFootprintValidator.ClosingPointToleranceMm)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count != 4)
        {
            return false;
        }

        vertices.Reverse();
        reversed = new RoofFootprintInput(
            vertices,
            source.IsClosed,
            source.HasCurvedSegments,
            source.IsPlanar);
        return true;
    }

    private static bool MatchesOrientationFlippedRigid(
        RoofRigidFootprintDescriptor current,
        RoofRigidFootprintDescriptor persisted) =>
        current.VertexCount == persisted.VertexCount &&
        current.SourceOrientation != persisted.SourceOrientation &&
        current.SourceOrientation != RoofPolygonOrientation.Undefined &&
        persisted.SourceOrientation != RoofPolygonOrientation.Undefined &&
        Math.Abs(current.Edge01LengthMm - persisted.Edge01LengthMm) <=
            SimpleGableRoofGeometryTolerance.LengthTolerance(
                current.Edge01LengthMm,
                persisted.Edge01LengthMm) &&
        Math.Abs(current.Edge12LengthMm - persisted.Edge12LengthMm) <=
            SimpleGableRoofGeometryTolerance.LengthTolerance(
                current.Edge12LengthMm,
                persisted.Edge12LengthMm);

    /// <summary>
    /// Public diagnostic breakdown for HOST A/B original-vs-copy resize classification.
    /// Read-only. Does not mutate.
    /// </summary>
    public static string ExplainClassify(
        RoofFootprintInput source,
        RoofFootprint footprint,
        RoofDefinitionData data)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var classification = Classify(source, footprint, data);
        var hasTopology = TryReadSourceTopology(source, out var topology);
        var persisted = data.RigidFootprint;
        var resolvePath = "n/a";
        if (persisted is not null &&
            data.RidgeEdgeFamily is RoofRidgeEdgeFamily family)
        {
            _ = TryResolveSourceTopologyForRestore(
                source,
                persisted,
                family,
                out _,
                out _,
                out resolvePath);
        }

        var currentOrient = hasTopology
            ? topology.Descriptor.SourceOrientation.ToString()
            : "<none>";
        var persistedOrient = persisted?.SourceOrientation.ToString() ?? "<none>";
        var edge01 = hasTopology ? topology.Descriptor.Edge01LengthMm : double.NaN;
        var edge12 = hasTopology ? topology.Descriptor.Edge12LengthMm : double.NaN;
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "result={0} error={1} schema={2} ridgeFamily={3} resolvePath={4} " +
            "orient={5}/{6} edge01={7:0.###}/{8:0.###} edge12={9:0.###}/{10:0.###}",
            classification.Kind,
            classification.Error,
            data.SchemaVersion,
            data.RidgeEdgeFamily,
            resolvePath,
            currentOrient,
            persistedOrient,
            edge01,
            persisted?.Edge01LengthMm,
            edge12,
            persisted?.Edge12LengthMm);
    }

    private static RoofDefinitionRestoreResult Solve(
        RoofFootprint footprint,
        double slopeDegrees,
        RoofDirection2D direction)
    {
        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slopeDegrees, direction)));
        return solved.IsValid && solved.Geometry is not null
            ? new RoofDefinitionRestoreResult(
                true,
                solved.Geometry,
                RoofDefinitionRestoreError.None)
            : Invalid(RoofDefinitionRestoreError.InvalidDefinition);
    }

    private static bool TryReadSourceTopology(
        RoofFootprintInput source,
        out SourceTopology topology)
    {
        topology = default;
        if (source.Vertices is null || source.Vertices.Count < 4 ||
            source.Vertices.Any(point => !IsFinite(point.X) || !IsFinite(point.Y)))
        {
            return false;
        }

        var vertices = source.Vertices.ToList();
        if (vertices.Count > 1 &&
            vertices[0].DistanceTo(vertices[vertices.Count - 1]) <=
                RoofFootprintValidator.ClosingPointToleranceMm)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count != 4)
        {
            return false;
        }

        var signedArea = RoofFootprint.CalculateSignedArea(vertices);
        if (!IsFinite(signedArea) || Math.Abs(signedArea) < RoofFootprintValidator.MinimumAreaMm2)
        {
            return false;
        }

        var edge01 = Between(vertices[0], vertices[1]);
        var edge12 = Between(vertices[1], vertices[2]);
        var edge01Length = Length(edge01);
        var edge12Length = Length(edge12);
        if (!IsFinite(edge01Length) || !IsFinite(edge12Length) ||
            edge01Length <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm ||
            edge12Length <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm)
        {
            return false;
        }

        topology = new SourceTopology(
            edge01,
            edge12,
            new RoofRigidFootprintDescriptor(
                vertices.Count,
                signedArea > 0d
                    ? RoofPolygonOrientation.CounterClockwise
                    : RoofPolygonOrientation.Clockwise,
                edge01Length,
                edge12Length));
        return true;
    }

    private static bool TryResolveRidgeEdgeFamily(
        SourceTopology topology,
        RoofDirection2D ridgeDirection,
        out RoofRidgeEdgeFamily edgeFamily)
    {
        edgeFamily = RoofRidgeEdgeFamily.Undefined;
        var ridge = new Vector2(ridgeDirection.X, ridgeDirection.Y);
        var firstCross = Math.Abs(Cross(ridge, topology.Edge01) /
                                  topology.Descriptor.Edge01LengthMm);
        var secondCross = Math.Abs(Cross(ridge, topology.Edge12) /
                                   topology.Descriptor.Edge12LengthMm);
        if (firstCross <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            edgeFamily = RoofRidgeEdgeFamily.SourceEdge01;
            return true;
        }

        if (secondCross <= SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            edgeFamily = RoofRidgeEdgeFamily.SourceEdge12;
            return true;
        }

        return false;
    }

    private static bool MatchesTopology(
        RoofRigidFootprintDescriptor current,
        RoofRigidFootprintDescriptor persisted) =>
        current.VertexCount == persisted.VertexCount &&
        current.SourceOrientation == persisted.SourceOrientation;

    private static bool Matches(
        RoofRigidFootprintDescriptor current,
        RoofRigidFootprintDescriptor persisted) =>
        MatchesTopology(current, persisted) &&
        Math.Abs(current.Edge01LengthMm - persisted.Edge01LengthMm) <=
            SimpleGableRoofGeometryTolerance.LengthTolerance(
                current.Edge01LengthMm,
                persisted.Edge01LengthMm) &&
        Math.Abs(current.Edge12LengthMm - persisted.Edge12LengthMm) <=
            SimpleGableRoofGeometryTolerance.LengthTolerance(
                current.Edge12LengthMm,
                persisted.Edge12LengthMm);

    private static Vector2 Between(RoofPoint2D start, RoofPoint2D end) =>
        new(end.X - start.X, end.Y - start.Y);

    private static double Length(Vector2 vector) =>
        Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofDefinitionRestoreResult Invalid(RoofDefinitionRestoreError error) =>
        new(false, null, error);

    private readonly record struct Vector2(double X, double Y);

    private readonly record struct SourceTopology(
        Vector2 Edge01,
        Vector2 Edge12,
        RoofRigidFootprintDescriptor Descriptor);
}
