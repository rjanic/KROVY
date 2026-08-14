using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayLifecycleTests
{
    [Fact]
    public void RotatedRoof_MapsToOneRidgeTwoEavesAndFourGableSlopes()
    {
        var geometry = Restore(Transform(Rectangle(), 37d, 250d, -900d), CreateDefinition(Rectangle()));
        var edges = SimpleGableRoofWireframe.Create(geometry, 0d);

        Assert.Single(edges, edge => edge.Role == RoofDisplayEdgeRole.Ridge);
        Assert.Equal(2, edges.Count(edge => edge.Role is RoofDisplayEdgeRole.Eave0 or RoofDisplayEdgeRole.Eave1));
        Assert.Equal(4, edges.Count(edge => edge.Role.ToString().StartsWith("GableSlope", StringComparison.Ordinal)));
        Assert.Equal(7, edges.Select(edge => UndirectedKey(edge.Segment)).Distinct().Count());
    }

    [Fact]
    public void SquareRotatedNinetyDegrees_KeepsSelectedRidgeFamilyInDisplay()
    {
        var square = Rectangle(8000d, 8000d);
        var rotated = Transform(square, 90d, 400d, 500d);
        var geometry = Restore(rotated, CreateDefinition(square));
        var ridge = SimpleGableRoofWireframe.Create(geometry, 0d)
            .Single(edge => edge.Role == RoofDisplayEdgeRole.Ridge)
            .Segment;
        var sourceEdge = EdgeDirection(rotated, 0);

        var ridgeX = ridge.End.X - ridge.Start.X;
        var ridgeY = ridge.End.Y - ridge.Start.Y;
        Assert.True(Math.Abs(ridgeX * sourceEdge.Y - ridgeY * sourceEdge.X) < 1e-6d);
    }

    [Theory]
    [InlineData(0d, 1250d, -750d)]
    [InlineData(30d, 0d, 0d)]
    public void RigidlyChangedSource_MakesPreviousDisplayStale(
        double angle,
        double moveX,
        double moveY)
    {
        const string owner = "A1";
        var original = Rectangle();
        var definition = CreateDefinition(original);
        var before = Edges(Restore(original, definition));
        var after = Edges(Restore(Transform(original, angle, moveX, moveY), definition));
        var beforeSignature = SimpleGableRoofWireframe.BuildGenerationSignature(before);
        var observations = Observe(owner, before, beforeSignature);

        var result = RoofDisplayValidator.Validate(
            owner,
            after,
            SimpleGableRoofWireframe.BuildGenerationSignature(after),
            observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.SignatureMismatch));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.GeometryMismatch));
    }

    [Fact]
    public void RegenerationTargetFromCurrentSource_ValidatesCurrentDeterministically()
    {
        const string owner = "A1";
        var original = Rectangle();
        var definition = CreateDefinition(original);
        var currentGeometry = Restore(Transform(original, 24d, 5000d, 2000d), definition);
        var currentEdges = Edges(currentGeometry);
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(currentEdges);
        var observations = Observe(owner, currentEdges, signature);

        var first = RoofDisplayValidator.Validate(owner, currentEdges, signature, observations);
        var second = RoofDisplayValidator.Validate(owner, currentEdges, signature, observations);

        Assert.Equal(first, second);
        Assert.True(first.IsCurrent);
    }

    [Theory]
    [InlineData(0d, 1250d, -750d)]
    [InlineData(30d, 0d, 0d)]
    [InlineData(90d, 400d, 500d)]
    public void WholeGroupRigidTransform_RemainsCurrentDespiteOriginalGenerationSignature(
        double angle,
        double moveX,
        double moveY)
    {
        const string owner = "A1";
        var original = angle == 90d ? Rectangle(8000d, 8000d) : Rectangle();
        var definition = CreateDefinition(original);
        var originalEdges = Edges(Restore(original, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var transformedEdges = Edges(Restore(Transform(original, angle, moveX, moveY), definition));
        var expectedSignature = SimpleGableRoofWireframe.BuildGenerationSignature(transformedEdges);
        var movedGroupObservations = Observe(owner, transformedEdges, originalSignature);

        var result = RoofDisplayValidator.Validate(
            owner,
            transformedEdges,
            expectedSignature,
            movedGroupObservations);

        Assert.True(result.IsCurrent);
        Assert.Equal(RoofDisplayValidationIssue.None, result.Issues);
    }

    [Fact]
    public void WholeGroupSingleNinety_ValidatesTransformedDisplayLinesAgainstRestoredOwner()
    {
        const string owner = "A1";
        var original = Rectangle(8000d, 8000d);
        var definition = CreateDefinition(original);
        var originalEdges = Edges(Restore(original, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var transformedSource = Transform(original, 90d, 400d, 500d);
        var observedEdges = Transform(originalEdges, 90d, 400d, 500d);
        var expectedEdges = Edges(Restore(transformedSource, definition));

        var result = RoofDisplayValidator.Validate(
            owner,
            expectedEdges,
            SimpleGableRoofWireframe.BuildGenerationSignature(expectedEdges),
            Observe(owner, observedEdges, originalSignature));

        Assert.True(result.IsCurrent, result.Issues.ToString());
    }

    [Fact]
    public void WholeGroupSequentialThirtyThenSixty_ValidatesLikeSingleNinety()
    {
        const string owner = "A1";
        var original = Rectangle(8000d, 8000d);
        var definition = CreateDefinition(original);
        var originalEdges = Edges(Restore(original, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var transformedSource = Transform(Transform(original, 30d, 0d, 0d), 60d, 0d, 0d);
        var observedEdges = Transform(Transform(originalEdges, 30d, 0d, 0d), 60d, 0d, 0d);
        var expectedEdges = Edges(Restore(transformedSource, definition));

        var result = RoofDisplayValidator.Validate(
            owner,
            expectedEdges,
            SimpleGableRoofWireframe.BuildGenerationSignature(expectedEdges),
            Observe(owner, observedEdges, originalSignature));

        Assert.True(result.IsCurrent, result.Issues.ToString());
    }

    [Fact]
    public void WholeGroupMeasuredSequentialResidue_DoesNotChangeSquareDisplayRoles()
    {
        const string owner = "A1";
        const double measuredHostMaxComponentDeltaMm = 2.9103830456733704e-11d;
        var original = Rectangle(8000d, 8000d);
        var definition = CreateDefinition(original);
        var originalEdges = Edges(Restore(original, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var idealNinety = Transform(original, 90d, 0d, 0d);
        var vertices = idealNinety.Vertices!.ToArray();
        vertices[2] = vertices[2] with
        {
            X = vertices[2].X - measuredHostMaxComponentDeltaMm,
        };
        var sequentialResidue = Input(vertices);
        var observedEdges = Transform(originalEdges, 90d, 0d, 0d);
        var expectedEdges = Edges(Restore(sequentialResidue, definition));

        var result = RoofDisplayValidator.Validate(
            owner,
            expectedEdges,
            SimpleGableRoofWireframe.BuildGenerationSignature(expectedEdges),
            Observe(owner, observedEdges, originalSignature));

        Assert.True(result.IsCurrent, result.Issues.ToString());
    }

    [Fact]
    public void WholeGroupRepeatedMoveAndRotate_RemainsCurrent()
    {
        const string owner = "A1";
        var original = Rectangle();
        var definition = CreateDefinition(original);
        var originalEdges = Edges(Restore(original, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var transformedSource = Transform(original, 0d, 1250d, -750d);
        var observedEdges = Transform(originalEdges, 0d, 1250d, -750d);
        transformedSource = Transform(transformedSource, 30d, 0d, 0d);
        observedEdges = Transform(observedEdges, 30d, 0d, 0d);
        transformedSource = Transform(transformedSource, -17d, 350d, 725d);
        observedEdges = Transform(observedEdges, -17d, 350d, 725d);
        var expectedEdges = Edges(Restore(transformedSource, definition));

        var result = RoofDisplayValidator.Validate(
            owner,
            expectedEdges,
            SimpleGableRoofWireframe.BuildGenerationSignature(expectedEdges),
            Observe(owner, observedEdges, originalSignature));

        Assert.True(result.IsCurrent, result.Issues.ToString());
    }

    [Fact]
    public void SquareMeasuredResidue_PreservesSelectedRidgeEdgeFamily()
    {
        const double measuredHostMaxComponentDeltaMm = 2.9103830456733704e-11d;
        var original = Rectangle(8000d, 8000d);
        var definition = CreateDefinition(original);
        var idealNinety = Transform(original, 90d, 0d, 0d);
        var vertices = idealNinety.Vertices!.ToArray();
        vertices[2] = vertices[2] with
        {
            X = vertices[2].X - measuredHostMaxComponentDeltaMm,
        };
        var current = Input(vertices);
        var geometry = Restore(current, definition);
        var selectedSourceFamily = EdgeDirection(current, 0);
        var otherSourceFamily = EdgeDirection(current, 1);
        var ridge = geometry.RidgeDirection;

        Assert.True(Math.Abs(ridge.X * selectedSourceFamily.Y - ridge.Y * selectedSourceFamily.X) < 1e-10d);
        Assert.True(Math.Abs(ridge.X * otherSourceFamily.X + ridge.Y * otherSourceFamily.Y) < 1e-10d);
    }

    [Fact]
    public void CopiedSemanticOwner_CanHaveIndependentDisplayOwnership()
    {
        var original = Rectangle();
        var definition = CreateDefinition(original);
        var copy = Transform(original, 0d, 25000d, 12000d);
        var originalEdges = Edges(Restore(original, definition));
        var copiedEdges = Edges(Restore(copy, definition));
        var originalSignature = SimpleGableRoofWireframe.BuildGenerationSignature(originalEdges);
        var copiedSignature = SimpleGableRoofWireframe.BuildGenerationSignature(copiedEdges);

        Assert.True(RoofDisplayValidator.Validate(
            "A1", originalEdges, originalSignature, Observe("A1", originalEdges, originalSignature)).IsCurrent);
        Assert.True(RoofDisplayValidator.Validate(
            "B2", copiedEdges, copiedSignature, Observe("B2", copiedEdges, copiedSignature)).IsCurrent);
        Assert.NotEqual(originalSignature, copiedSignature);
        Assert.NotEqual("A1", "B2");
    }

    [Fact]
    public void StretchStaleSemanticDefinition_ProducesNoGeometryForDisplayRegeneration()
    {
        var original = Rectangle();
        var definition = CreateDefinition(original);
        var stretched = Input([
            new(1000d, -2000d), new(11250d, -2000d),
            new(11250d, 4000d), new(1000d, 4000d)]);

        var result = RestoreResult(stretched, definition);

        Assert.False(result.IsValid);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, result.Error);
        Assert.Null(result.Geometry);
    }

    private static IReadOnlyList<RoofDisplayObservation> Observe(
        string owner,
        IReadOnlyList<RoofDisplayEdge> edges,
        string signature) =>
        edges.Select(edge => new RoofDisplayObservation(
            owner,
            new RoofDisplayData(1, owner, edge.Role, signature),
            RoofDisplayDataDecodeError.None,
            edge.Segment)).ToArray();

    private static IReadOnlyList<RoofDisplayEdge> Edges(SimpleGableRoofGeometry geometry) =>
        SimpleGableRoofWireframe.Create(geometry, 0d);

    private static string UndirectedKey(RoofSegment3D segment)
    {
        var first = $"{segment.Start.X:R},{segment.Start.Y:R},{segment.Start.Z:R}";
        var second = $"{segment.End.X:R},{segment.End.Y:R},{segment.End.Z:R}";
        return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
    }

    private static RoofDefinitionData CreateDefinition(RoofFootprintInput source)
    {
        var footprint = Validate(source);
        var geometry = Solve(source, footprint);
        return RoofDefinitionPersistence.Create(source, footprint, geometry);
    }

    private static SimpleGableRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData definition)
    {
        var result = RestoreResult(source, definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static RoofDefinitionRestoreResult RestoreResult(
        RoofFootprintInput source,
        RoofDefinitionData definition)
    {
        var validation = RoofFootprintValidator.Validate(source);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return RoofDefinitionPersistence.Restore(source, validation.Footprint!, definition);
    }

    private static SimpleGableRoofGeometry Solve(RoofFootprintInput source, RoofFootprint footprint)
    {
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(35d, EdgeDirection(source, 0))));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static RoofDirection2D EdgeDirection(RoofFootprintInput source, int edgeIndex)
    {
        var first = source.Vertices![edgeIndex];
        var second = source.Vertices[(edgeIndex + 1) % source.Vertices.Count];
        Assert.True(RoofDirection2D.TryCreate(second.X - first.X, second.Y - first.Y, out var direction));
        return direction;
    }

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var validation = RoofFootprintValidator.Validate(source);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static RoofFootprintInput Rectangle(double width = 10000d, double height = 6000d) =>
        Input([
            new(1000d, -2000d), new(1000d + width, -2000d),
            new(1000d + width, -2000d + height), new(1000d, -2000d + height)]);

    private static RoofFootprintInput Transform(
        RoofFootprintInput source,
        double angleDegrees,
        double translateX,
        double translateY)
    {
        var radians = angleDegrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return Input(source.Vertices!.Select(point => new RoofPoint2D(
            point.X * cosine - point.Y * sine + translateX,
            point.X * sine + point.Y * cosine + translateY)).ToArray());
    }

    private static IReadOnlyList<RoofDisplayEdge> Transform(
        IReadOnlyList<RoofDisplayEdge> edges,
        double angleDegrees,
        double translateX,
        double translateY)
    {
        var radians = angleDegrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        RoofPoint3D Point(RoofPoint3D point) => new(
            point.X * cosine - point.Y * sine + translateX,
            point.X * sine + point.Y * cosine + translateY,
            point.Z);
        return edges.Select(edge => edge with
        {
            Segment = new RoofSegment3D(Point(edge.Segment.Start), Point(edge.Segment.End)),
        }).ToArray();
    }

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> vertices) =>
        new(vertices, true, false, true);
}
