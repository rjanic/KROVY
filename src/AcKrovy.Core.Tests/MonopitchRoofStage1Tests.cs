using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRoofStage1Tests
{
    [Fact]
    public void RoofKindAndSchema5Payload_HaveStableIdentity()
    {
        Assert.Equal(3, (int)RoofKind.Monopitch);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        var source = Source();
        var geometry = Solve(source, 30d, Direction(0d, 1d));
        var payload = RoofDefinitionDataCodec.Encode(
            RoofDefinitionPersistence.Create(source, Validate(source), geometry));

        Assert.Equal("5|Monopitch|30|30|3464.1016151377544|Edge12|4|CCW|10000|6000|Locked|", payload);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(RoofKind.Monopitch, decoded!.Kind);
    }

    [Fact]
    public void LowToHighDirection_IsSnappedToDirectedFootprintAxis()
    {
        var geometry = Solve(Source(), 25d, Direction(1e-12d, 1d));

        Assert.Equal(0d, geometry.LowToHighDirection.X, 10);
        Assert.Equal(1d, geometry.LowToHighDirection.Y, 10);
        Assert.All(geometry.LowEave is { } low
            ? new[] { low.Start.Z, low.End.Z }
            : Array.Empty<double>(), value => Assert.Equal(0d, value));
        Assert.All(new[] { geometry.HighEave.Start.Z, geometry.HighEave.End.Z },
            value => Assert.Equal(geometry.EaveHeightDifferenceMm, value, 9));
    }

    [Fact]
    public void Span_IsProjectionInLowToHighDirection()
    {
        Assert.Equal(6000d, Solve(Source(), 30d, Direction(0d, 1d)).SpanMm, 9);
        Assert.Equal(10000d, Solve(Source(), 30d, Direction(1d, 0d)).SpanMm, 9);
    }

    [Theory]
    [InlineData(6000d, 30d)]
    [InlineData(10000d, 12.5d)]
    [InlineData(2500d, 67d)]
    public void SlopeAndHeightConversions_AreInverseWithoutDisplayRounding(
        double span,
        double slope)
    {
        Assert.True(MonopitchRoofMath.TryCalculateHeightDifferenceMm(span, slope, out var height));
        Assert.Equal(span * Math.Tan(slope * Math.PI / 180d), height, 10);
        Assert.True(MonopitchRoofMath.TryCalculateSlopeDegrees(span, height, out var restored));
        Assert.Equal(slope, restored, 10);
    }

    [Theory]
    [InlineData(0d, 30d)]
    [InlineData(6000d, 0d)]
    [InlineData(6000d, 90d)]
    [InlineData(double.NaN, 30d)]
    public void InvalidOrImpossibleMath_IsRejected(double span, double slope)
    {
        Assert.False(MonopitchRoofMath.TryCalculateHeightDifferenceMm(span, slope, out _));
    }

    [Fact]
    public void Mirror_SwapsLowHighAndPreservesMagnitudeAndFootprint()
    {
        var footprint = Validate(Source());
        var originalDefinition = Definition(footprint, 30d, Direction(0d, 1d));
        var original = Solve(originalDefinition);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(originalDefinition);
        var mirrored = Solve(mirroredDefinition);

        Assert.Same(footprint, mirroredDefinition.Footprint);
        Assert.Equal(original.SlopeDegrees, mirrored.SlopeDegrees);
        Assert.Equal(original.EaveHeightDifferenceMm, mirrored.EaveHeightDifferenceMm, 9);
        Assert.Equal(-original.LowToHighDirection.X, mirrored.LowToHighDirection.X, 10);
        Assert.Equal(-original.LowToHighDirection.Y, mirrored.LowToHighDirection.Y, 10);
        Assert.True(
            SamePlanPoint(original.LowEave.Start, mirrored.HighEave.Start) ||
            SamePlanPoint(original.LowEave.Start, mirrored.HighEave.End));
        Assert.True(
            SamePlanPoint(original.LowEave.End, mirrored.HighEave.Start) ||
            SamePlanPoint(original.LowEave.End, mirrored.HighEave.End));
    }

    [Fact]
    public void MirrorTwice_ReturnsCanonicalEquivalentOriginal()
    {
        var definition = Definition(Validate(Source()), 37d, Direction(1d, 0d));
        var twice = MonopitchRoofDefinitionRules.Mirror(
            MonopitchRoofDefinitionRules.Mirror(definition));

        Assert.Equal(Solve(definition).Signature, Solve(twice).Signature);
    }

    [Fact]
    public void Persistence_RoundTripsDirectionAndRecomputesDerivedHeightOnResize()
    {
        var source = Source();
        var original = Solve(source, 30d, Direction(0d, -1d));
        var data = RoofDefinitionPersistence.Create(source, Validate(source), original);
        Assert.True(data.EaveHeightDifferenceMm < 0d);

        var reopened = Restore(source, data);
        Assert.Equal(original.Signature, reopened.Signature);
        var resized = new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 9000), new(0, 9000)],
            true,
            false,
            true);
        var resizedGeometry = Restore(resized, data);

        Assert.Equal(9000d, resizedGeometry.SpanMm, 9);
        Assert.Equal(30d, resizedGeometry.SlopeDegrees);
        Assert.Equal(9000d * Math.Tan(Math.PI / 6d), resizedGeometry.EaveHeightDifferenceMm, 8);
        Assert.Equal(0d, resizedGeometry.LowToHighDirection.X, 10);
        Assert.Equal(-1d, resizedGeometry.LowToHighDirection.Y, 10);
    }

    [Fact]
    public void Wireframe_ContainsOnePlaneBoundaryAndDirectedArrow()
    {
        var edges = RoofWireframe.Create(Solve(Source(), 20d, Direction(0d, 1d)), 125d);

        Assert.Equal(MonopitchRoofWireframe.EdgeCount, edges.Count);
        Assert.Contains(edges, edge => edge.Role == RoofDisplayEdgeRole.MonopitchLowEave);
        Assert.Contains(edges, edge => edge.Role == RoofDisplayEdgeRole.MonopitchHighEave);
        Assert.Contains(edges, edge => edge.Role == RoofDisplayEdgeRole.MonopitchDirection);
        Assert.Equal(edges.Count, edges.Select(edge => edge.Role).Distinct().Count());
        Assert.False(string.IsNullOrWhiteSpace(RoofWireframe.BuildGenerationSignature(edges)));
        var displayPayload = RoofDisplayDataCodec.Encode(new RoofDisplayData(
            RoofDisplayDataSchema.CurrentVersion,
            "AB12",
            RoofDisplayEdgeRole.MonopitchDirection,
            "signature"));
        Assert.True(RoofDisplayDataCodec.TryDecode(
            displayPayload,
            out var displayData,
            out var displayError));
        Assert.Equal(RoofDisplayDataDecodeError.None, displayError);
        Assert.Equal(RoofDisplayEdgeRole.MonopitchDirection, displayData!.Role);
    }

    [Fact]
    public void ExistingGableDispatch_RemainsBitEquivalentToDedicatedSolver()
    {
        var footprint = Validate(Source());
        var definition = new RoofDefinition(
            footprint,
            new RoofParameters(35d, Direction(1d, 0d)),
            RoofKind.SimpleGable);
        var dedicated = SimpleGableRoofGeometrySolver.Solve(definition);
        var dispatched = RoofGeometrySolver.Solve(definition);

        Assert.True(dedicated.IsValid);
        Assert.True(dispatched.IsValid);
        Assert.Equal(
            dedicated.Geometry!.Signature,
            Assert.IsType<SimpleGableRoofGeometry>(dispatched.Geometry).Signature);
    }

    [Fact]
    public void ExistingAsymmetricGableDispatch_RemainsBitEquivalentToDedicatedSolver()
    {
        var footprint = Validate(Source());
        var definition = new RoofDefinition(
            footprint,
            new RoofParameters(
                22d,
                Direction(1d, 0d),
                Face1SlopeDegrees: 37d,
                EaveHeightDifferenceMm: 450d),
            RoofKind.AsymmetricGable);
        var dedicated = SimpleGableRoofGeometrySolver.Solve(definition);
        var dispatched = RoofGeometrySolver.Solve(definition);

        Assert.True(dedicated.IsValid);
        Assert.True(dispatched.IsValid);
        Assert.Equal(
            dedicated.Geometry!.Signature,
            Assert.IsType<SimpleGableRoofGeometry>(dispatched.Geometry).Signature);
    }

    [Fact]
    public void SharedRigidTranslationLifecycle_AcceptsMonopitchRoleTopology()
    {
        const double dx = 725d;
        const double dy = -1320d;
        var source = Source();
        var geometry = Solve(source, 30d, Direction(0d, 1d));
        var before = RoofWireframe.Create(geometry, 0d)
            .ToDictionary(edge => edge.Role, edge => edge.Segment);
        var after = before.ToDictionary(
            pair => pair.Key,
            pair => Translate(pair.Value, dx, dy));
        var movedVertices = source.Vertices!
            .Select(point => new RoofPoint2D(point.X + dx, point.Y + dy))
            .ToArray();

        Assert.True(RoofGroupGripNativeObservationRules.IsCompleteSevenRoles(before));
        var result = RoofRigidGroupTransformRules.TryClassifyTranslation(
            source.Vertices!,
            movedVertices,
            before,
            after,
            after);

        Assert.True(result.IsAccepted, result.RejectionReason);
        Assert.Equal(dx, result.DeltaX, 9);
        Assert.Equal(dy, result.DeltaY, 9);
    }

    [Fact]
    public void CommonLifecycleServices_DoNotBranchOnMonopitch()
    {
        var root = RepositoryRoot();
        var names = new[]
        {
            "RoofAttachedManualLifecycleService.cs",
            "RoofMirrorCloneDetachService.cs",
            "RoofSourceResizeChildPolicyService.cs",
            "RoofDisplayGroupService.cs",
            "LiveGeometrySynchronizationService.cs",
        };
        foreach (var name in names)
        {
            var path = Directory.EnumerateFiles(Path.Combine(root, "src"), name, SearchOption.AllDirectories).Single();
            Assert.DoesNotContain("RoofKind.Monopitch", File.ReadAllText(path));
        }
    }

    private static RoofDefinition Definition(
        RoofFootprint footprint,
        double slope,
        RoofDirection2D direction) =>
        new(footprint, new RoofParameters(slope, SlopeDirection: direction), RoofKind.Monopitch);

    private static MonopitchRoofGeometry Solve(
        RoofFootprintInput source,
        double slope,
        RoofDirection2D direction) =>
        Solve(Definition(Validate(source), slope, direction));

    private static MonopitchRoofGeometry Solve(RoofDefinition definition)
    {
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static MonopitchRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var result = RoofDefinitionPersistence.Restore(source, Validate(source), data);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }

    private static bool SamePlanPoint(RoofPoint3D left, RoofPoint3D right) =>
        Math.Abs(left.X - right.X) <= 1e-9 &&
        Math.Abs(left.Y - right.Y) <= 1e-9;

    private static RoofSegment3D Translate(RoofSegment3D segment, double dx, double dy) =>
        new(
            new RoofPoint3D(segment.Start.X + dx, segment.Start.Y + dy, segment.Start.Z),
            new RoofPoint3D(segment.End.X + dx, segment.End.Y + dy, segment.End.Z));

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofFootprintInput Source() => new(
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)],
        true,
        false,
        true);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
