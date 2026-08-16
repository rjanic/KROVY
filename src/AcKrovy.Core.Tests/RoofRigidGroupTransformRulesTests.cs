using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRigidGroupTransformRulesTests
{
    private const double Dx = 889.152d;
    private const double Dy = 2559.704d;

    [Fact]
    public void UniformTranslation_SourceAndAllSevenDisplay_IsAccepted()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));
        var canonical = currentDisplay;

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            canonical);

        Assert.True(result.IsAccepted, result.RejectionReason);
        Assert.Equal("Translation", result.TransformKind);
        Assert.Equal(Dx, result.DeltaX, 3);
        Assert.Equal(Dy, result.DeltaY, 3);
    }

    [Fact]
    public void RotatedRoof_UniformTranslation_IsAccepted()
    {
        var original = Transform(Rectangle(), 30d, 400d, -250d);
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            currentDisplay);

        Assert.True(result.IsAccepted, result.RejectionReason);
        Assert.Equal("Translation", result.TransformKind);
    }

    [Fact]
    public void SourceUnchanged_DisplayMutated_IsRejected()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(Translate(original, Dx, Dy), data));

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            original.Vertices!,
            preDisplay,
            currentDisplay,
            currentDisplay);

        Assert.False(result.IsAccepted);
        Assert.Equal("source-unchanged", result.RejectionReason);
    }

    [Fact]
    public void SingleDisplayLineMutation_IsRejected()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));
        var ridge = currentDisplay[RoofDisplayEdgeRole.Ridge];
        currentDisplay[RoofDisplayEdgeRole.Ridge] = new RoofSegment3D(
            new RoofPoint3D(ridge.Start.X + 500d, ridge.Start.Y, ridge.Start.Z),
            ridge.End);

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            ToMap(Wireframe(moved, data)));

        Assert.False(result.IsAccepted);
        Assert.Contains(
            result.RejectionReason,
            new[]
            {
                "display-not-unique-translation",
                "transformed-display-mismatch",
                "current-not-canonical-wireframe",
            });
    }

    [Fact]
    public void IncompleteGroup_IsRejected()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));
        currentDisplay.Remove(RoofDisplayEdgeRole.GableSlope11);

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            ToMap(Wireframe(moved, data)));

        Assert.False(result.IsAccepted);
        Assert.Equal("incomplete-display-roles", result.RejectionReason);
    }

    [Fact]
    public void DifferentTransformsAmongMembers_IsRejected()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));
        // Translate only ridge by a different delta.
        var ridge = preDisplay[RoofDisplayEdgeRole.Ridge];
        currentDisplay[RoofDisplayEdgeRole.Ridge] = new RoofSegment3D(
            new RoofPoint3D(ridge.Start.X + Dx + 100d, ridge.Start.Y + Dy, ridge.Start.Z),
            new RoofPoint3D(ridge.End.X + Dx + 100d, ridge.End.Y + Dy, ridge.End.Z));

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            ToMap(Wireframe(moved, data)));

        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void NonRigidSourceChange_IsRejected()
    {
        var original = Rectangle();
        var resized = StretchGableEnd(original, 2000d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(resized, data));

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            resized.Vertices!,
            preDisplay,
            currentDisplay,
            currentDisplay);

        Assert.False(result.IsAccepted);
        Assert.Contains(
            result.RejectionReason,
            new[] { "source-not-unique-translation", "source-not-rigid-shape" });
    }

    [Fact]
    public void CanonicalWireframeMismatch_IsRejected()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var preDisplay = ToMap(Wireframe(original, data));
        var currentDisplay = ToMap(Wireframe(moved, data));
        var wrongCanonical = ToMap(Wireframe(original, data));

        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            original.Vertices!,
            moved.Vertices!,
            preDisplay,
            currentDisplay,
            wrongCanonical);

        Assert.False(result.IsAccepted);
        Assert.Equal("current-not-canonical-wireframe", result.RejectionReason);
    }

    [Fact]
    public void PreCommandDelta_MeansTimingCaseC_IsInvalid()
    {
        var original = Rectangle();
        var moved = Translate(original, Dx, Dy);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var pre = ToMap(Wireframe(original, data));
        var observed = ToMap(Wireframe(moved, data));

        Assert.True(
            RoofGroupGripNativeObservationRules.HasMeaningfulDeltaFromExpected(
                pre,
                observed,
                RoofRigidGroupTransformRules.ToleranceMm));
    }

    [Fact]
    public void SupportedSideResize_StillAdoptsViaExistingRules()
    {
        var original = Rectangle();
        var resized = StretchGableEnd(original, 2000d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var result = RoofGroupGripResizeAdoptionRules.TryDeriveSupportedSideResize(
            original.Vertices!,
            data,
            ToMap(Wireframe(original, data)),
            ToMap(Wireframe(resized, data)));
        Assert.True(result.CanAdopt, result.RejectionReason);
        Assert.Equal(RoofGroupGripSideResizeKind.GableEnd, result.Kind);
    }

    private static Dictionary<RoofDisplayEdgeRole, RoofSegment3D> ToMap(
        IReadOnlyList<RoofDisplayEdge> edges) =>
        edges.ToDictionary(edge => edge.Role, edge => edge.Segment);

    private static IReadOnlyList<RoofDisplayEdge> Wireframe(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        SimpleGableRoofWireframe.Create(Restore(source, data), 0d);

    private static RoofDefinitionData Create(
        RoofFootprintInput source,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var footprint = Validate(source);
        var direction = EdgeDirection(
            source.Vertices!,
            family == RoofRidgeEdgeFamily.SourceEdge01 ? 0 : 1);
        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, direction)));
        Assert.True(solved.IsValid);
        return RoofDefinitionPersistence.Create(source, footprint, solved.Geometry!);
    }

    private static SimpleGableRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var result = RoofDefinitionPersistence.Restore(source, Validate(source), data);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDirection2D EdgeDirection(IReadOnlyList<RoofPoint2D> vertices, int edgeIndex)
    {
        var first = vertices[edgeIndex];
        var second = vertices[(edgeIndex + 1) % vertices.Count];
        Assert.True(RoofDirection2D.TryCreate(second.X - first.X, second.Y - first.Y, out var direction));
        return direction;
    }

    private static RoofFootprintInput Rectangle(double width = 10000d, double depth = 6000d) =>
        new(
            new[]
            {
                new RoofPoint2D(0d, 0d),
                new RoofPoint2D(width, 0d),
                new RoofPoint2D(width, depth),
                new RoofPoint2D(0d, depth),
            },
            true,
            false,
            true);

    private static RoofFootprintInput Translate(RoofFootprintInput source, double dx, double dy) =>
        new(
            source.Vertices!.Select(v => new RoofPoint2D(v.X + dx, v.Y + dy)).ToArray(),
            true,
            false,
            true);

    private static RoofFootprintInput StretchGableEnd(RoofFootprintInput source, double delta)
    {
        var v = source.Vertices!.ToArray();
        return new RoofFootprintInput(
            new[]
            {
                v[0],
                new RoofPoint2D(v[1].X + delta, v[1].Y),
                new RoofPoint2D(v[2].X + delta, v[2].Y),
                v[3],
            },
            true,
            false,
            true);
    }

    private static RoofFootprintInput Transform(
        RoofFootprintInput source,
        double degrees,
        double dx,
        double dy)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new RoofFootprintInput(
            source.Vertices!
                .Select(v => new RoofPoint2D(
                    v.X * cos - v.Y * sin + dx,
                    v.X * sin + v.Y * cos + dy))
                .ToArray(),
            true,
            false,
            true);
    }
}
