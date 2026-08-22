using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AsymmetricGableRoofFoundationTests
{
    [Fact]
    public void EqualSlopes_ReduceToCenteredSimpleGable()
    {
        var asymmetric = Solve(Rectangle(), 25d, 25d, 1d, 0d);
        var simple = SolveSimple(Rectangle(), 25d, 1d, 0d);

        Assert.Equal(3000d, asymmetric.Face0RunMm, 9);
        Assert.Equal(3000d, asymmetric.Face1RunMm, 9);
        Assert.Equal(simple.Ridge, asymmetric.Ridge);
        Assert.Equal(simple.RiseMm, asymmetric.RiseMm, 9);
    }

    [Fact]
    public void DifferentSlopes_DeriveOffCenterRidgeAndOneCommonHeight()
    {
        var geometry = Solve(Rectangle(), 20d, 35d, 1d, 0d);
        var expectedRun0 = 6000d * Tan(35d) / (Tan(20d) + Tan(35d));

        Assert.Equal(expectedRun0, geometry.Face0RunMm, 9);
        Assert.Equal(6000d, geometry.Face0RunMm + geometry.Face1RunMm, 9);
        Assert.Equal(geometry.Face0RunMm * Tan(20d), geometry.RiseMm, 9);
        Assert.Equal(geometry.Face1RunMm * Tan(35d), geometry.RiseMm, 9);
        Assert.InRange(geometry.Ridge.Start.Y, 0.000001d, 5999.999999d);
    }

    [Fact]
    public void ShallowAndSteepSlopes_StayFiniteAndInsideSpan()
    {
        var geometry = Solve(Rectangle(), 5d, 80d, 1d, 0d);

        Assert.True(double.IsFinite(geometry.RiseMm));
        Assert.True(double.IsFinite(geometry.Face0RunMm));
        Assert.True(double.IsFinite(geometry.Face1RunMm));
        Assert.InRange(geometry.Face0RunMm, 0.000001d, 5999.999999d);
        Assert.InRange(geometry.Face1RunMm, 0.000001d, 5999.999999d);
    }

    [Fact]
    public void RotatedRectangle_PreservesIntrinsicRunsAndRidgeLength()
    {
        const double angle = 37d * Math.PI / 180d;
        var axis = (X: Math.Cos(angle), Y: Math.Sin(angle));
        var rotated = Rectangle().Select(point => new RoofPoint2D(
            250d + point.X * axis.X - point.Y * axis.Y,
            -900d + point.X * axis.Y + point.Y * axis.X)).ToArray();

        var geometry = Solve(rotated, 20d, 35d, axis.X, axis.Y);
        var baseline = Solve(Rectangle(), 20d, 35d, 1d, 0d);

        Assert.Equal(baseline.Face0RunMm, geometry.Face0RunMm, 7);
        Assert.Equal(baseline.Face1RunMm, geometry.Face1RunMm, 7);
        Assert.Equal(10000d, geometry.RidgeLengthMm, 7);
    }

    [Fact]
    public void EquivalentRepresentations_KeepFaceIdentityRidgeAndGeneratedKeys()
    {
        var ccw = Rectangle();
        var cw = new[] { ccw[2], ccw[1], ccw[0], ccw[3] };
        var shifted = new[] { ccw[2], ccw[3], ccw[0], ccw[1] };
        var first = Solve(ccw, 20d, 35d, 1d, 0d);
        var second = Solve(cw, 20d, 35d, -1d, 0d);
        var third = Solve(shifted, 20d, 35d, 1d + 1e-12d, 1e-12d);

        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(first.Signature, third.Signature);
        Assert.Equal(Keys(first), Keys(second));
        Assert.Equal(Keys(first), Keys(third));
    }

    [Fact]
    public void GeneratedRafters_EndOnSameRidgeAndUseOwnFaceSlope()
    {
        var geometry = Solve(Rectangle(), 20d, 35d, 1d, 0d);
        var layout = Layout(geometry);

        foreach (var pair in layout.Rafters.GroupBy(rafter => rafter.StationIndex))
        {
            var face0 = Assert.Single(pair, rafter => rafter.Face == RafterRoofFace.Face0);
            var face1 = Assert.Single(pair, rafter => rafter.Face == RafterRoofFace.Face1);
            Assert.Equal(face0.PlanEnd, face1.PlanEnd);
            Assert.Equal(20d, face0.SlopeDegrees);
            Assert.Equal(35d, face1.SlopeDegrees);
            Assert.Equal(geometry.Face0RunMm / Math.Cos(20d * Math.PI / 180d),
                face0.PlanStart.DistanceTo(face0.PlanEnd) / Math.Cos(20d * Math.PI / 180d), 7);
            Assert.NotEqual(
                face0.PlanStart.DistanceTo(face0.PlanEnd) / Math.Cos(20d * Math.PI / 180d),
                face1.PlanStart.DistanceTo(face1.PlanEnd) / Math.Cos(35d * Math.PI / 180d));
        }
    }

    [Fact]
    public void Schema3_DecodesAsEqualSlopeSimpleGable()
    {
        const string payload = "3|SimpleGable|35|Edge01|4|CCW|10000|6000|Locked|";
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(RoofKind.SimpleGable, data!.Kind);
        Assert.Equal(35d, data.Face0SlopeDegrees);
        Assert.Equal(35d, data.EffectiveFace1SlopeDegrees);
        Assert.Equal(0d, data.EaveHeightDifferenceMm);
    }

    [Fact]
    public void Schema4_SimpleGableRoundTrip_RemainsEqualSlope()
    {
        var data = DefinitionData(
            RoofKind.SimpleGable,
            30d,
            30d,
            RoofDefinitionDataSchema.DualSlopeVersion);
        var decoded = RoundTrip(data);

        Assert.Equal(RoofKind.SimpleGable, decoded.Kind);
        Assert.Equal(decoded.Face0SlopeDegrees, decoded.EffectiveFace1SlopeDegrees);
        Assert.Equal(0d, decoded.EaveHeightDifferenceMm);
    }

    [Fact]
    public void Schema4_AsymmetricGableRoundTripsBothSlopes()
    {
        var decoded = RoundTrip(DefinitionData(
            RoofKind.AsymmetricGable,
            20d,
            35d,
            RoofDefinitionDataSchema.DualSlopeVersion));

        Assert.Equal(RoofKind.AsymmetricGable, decoded.Kind);
        Assert.Equal(20d, decoded.Face0SlopeDegrees);
        Assert.Equal(35d, decoded.EffectiveFace1SlopeDegrees);
        Assert.Equal(0d, decoded.EaveHeightDifferenceMm);
    }

    [Fact]
    public void Schema5ZeroDelta_ReproducesSchema4AsymmetricGeometry()
    {
        var source = Input(Rectangle());
        var footprint = Validate(source);
        var schema4 = RoundTrip(DefinitionData(
            RoofKind.AsymmetricGable,
            20d,
            35d,
            RoofDefinitionDataSchema.DualSlopeVersion));
        var schema5 = RoundTrip(DefinitionData(
            RoofKind.AsymmetricGable,
            20d,
            35d,
            RoofDefinitionDataSchema.CurrentVersion,
            0d));

        var restored4 = RoofDefinitionPersistence.Restore(source, footprint, schema4);
        var restored5 = RoofDefinitionPersistence.Restore(source, footprint, schema5);

        Assert.True(restored4.IsValid, restored4.Error.ToString());
        Assert.True(restored5.IsValid, restored5.Error.ToString());
        Assert.Equal(restored4.Geometry!.Signature, restored5.Geometry!.Signature);
        Assert.Equal(restored4.Geometry.Ridge, restored5.Geometry.Ridge);
    }

    [Theory]
    [InlineData(450d)]
    [InlineData(-325d)]
    [InlineData(0d)]
    public void Schema5_RoundTripPreservesSignedEaveHeightDifference(double deltaHeight)
    {
        var decoded = RoundTrip(DefinitionData(
            RoofKind.AsymmetricGable,
            20d,
            35d,
            RoofDefinitionDataSchema.CurrentVersion,
            deltaHeight));

        Assert.Equal(deltaHeight, decoded.EaveHeightDifferenceMm);
    }

    [Fact]
    public void Schema5_PayloadOrderIsStableAndExplicit()
    {
        var payload = RoofDefinitionDataCodec.Encode(DefinitionData(
            RoofKind.AsymmetricGable,
            20d,
            35d,
            RoofDefinitionDataSchema.CurrentVersion,
            450d));

        Assert.Equal(
            "5|AsymmetricGable|20|35|450|Edge01|4|CCW|10000|6000|Locked|",
            payload);
    }

    [Theory]
    [InlineData("5|AsymmetricGable|NaN|35|450|Edge01|4|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidSlope)]
    [InlineData("5|AsymmetricGable|20|35|NaN|Edge01|4|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidEaveHeightDifference)]
    public void Schema5_ReportsTheInvalidNumericField(
        string payload,
        RoofDefinitionDataDecodeError expectedError)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData("1|SimpleGable|35|1|0|0,0;10000,0;10000,6000;0,6000")]
    [InlineData("2|SimpleGable|35|Edge01|4|CCW|10000|6000")]
    [InlineData("3|SimpleGable|35|Edge01|4|CCW|10000|6000|Locked|")]
    public void Schemas1To3_DecodeWithZeroEaveHeightDifference(string payload)
    {
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(0d, data!.EaveHeightDifferenceMm);
    }

    [Fact]
    public void SignedEaveHeightDifference_MovesRidgeAndKeepsOneCommonElevation()
    {
        var zero = Solve(Rectangle(), 20d, 35d, 1d, 0d, 0d);
        var positive = Solve(Rectangle(), 20d, 35d, 1d, 0d, 450d);
        var negative = Solve(Rectangle(), 20d, 35d, 1d, 0d, -450d);

        Assert.True(positive.Face0RunMm > zero.Face0RunMm);
        Assert.True(negative.Face0RunMm < zero.Face0RunMm);
        Assert.Equal(
            positive.RiseMm,
            positive.EaveHeightDifferenceMm + positive.Face1RunMm * Tan(35d),
            8);
        Assert.Equal(
            negative.RiseMm,
            negative.EaveHeightDifferenceMm + negative.Face1RunMm * Tan(35d),
            8);
        Assert.Equal(450d, positive.Faces[1].Eave.Start.Z - positive.Faces[0].Eave.Start.Z, 9);
        Assert.Equal(-450d, negative.Faces[1].Eave.Start.Z - negative.Faces[0].Eave.Start.Z, 9);
    }

    [Fact]
    public void SaveReopenCodecAndRestore_PreserveSignedEaveHeightDifference()
    {
        var source = Input(Rectangle());
        var footprint = Validate(source);
        var original = Solve(Rectangle(), 20d, 35d, 1d, 0d, -325d);
        var savedPayload = RoofDefinitionDataCodec.Encode(
            RoofDefinitionPersistence.Create(source, footprint, original));

        Assert.True(RoofDefinitionDataCodec.TryDecode(
            savedPayload,
            out var reopenedData,
            out var decodeError), decodeError.ToString());
        var reopened = RoofDefinitionPersistence.Restore(source, footprint, reopenedData!);

        Assert.True(reopened.IsValid, reopened.Error.ToString());
        Assert.Equal(-325d, reopened.Geometry!.EaveHeightDifferenceMm);
        Assert.Equal(original.Signature, reopened.Geometry.Signature);
        Assert.Equal(original.Ridge, reopened.Geometry.Ridge);
    }

    [Fact]
    public void ImpossibleEaveHeightDifference_IsRejectedWithoutClamping()
    {
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            Validate(Input(Rectangle())),
            new RoofParameters(
                20d,
                direction,
                Face1SlopeDegrees: 35d,
                EaveHeightDifferenceMm: 10000d),
            RoofKind.AsymmetricGable));

        Assert.False(result.IsValid);
        Assert.Equal(SimpleGableRoofGeometryError.InvalidEaveHeightDifference, result.Error);
    }

    [Fact]
    public void RoofKindsHaveStableExplicitValuesAndSimpleGableRejectsUnequalSlopes()
    {
        Assert.Equal(1, (int)RoofKind.SimpleGable);
        Assert.Equal(2, (int)RoofKind.AsymmetricGable);
        Assert.False(RoofDefinitionDataCodec.TryValidate(
            DefinitionData(RoofKind.SimpleGable, 20d, 35d), out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidSlope, error);
    }

    [Fact]
    public void Resize_RecomputesRidgeFromNewSpanWhileSlopesStayConstant()
    {
        var source = Input(Rectangle());
        var footprint = Validate(source);
        var geometry = Solve(Rectangle(), 20d, 35d, 1d, 0d, 300d);
        var data = RoofDefinitionPersistence.Create(source, footprint, geometry);
        var resizedSource = Input([P(0, 0), P(10000, 0), P(10000, 9000), P(0, 9000)]);
        var restored = RoofDefinitionPersistence.Restore(resizedSource, Validate(resizedSource), data);

        Assert.True(restored.IsValid, restored.Error.ToString());
        Assert.Equal(20d, restored.Geometry!.Face0SlopeDegrees);
        Assert.Equal(35d, restored.Geometry.Face1SlopeDegrees);
        Assert.Equal(300d, restored.Geometry.EaveHeightDifferenceMm);
        Assert.Equal(9000d, restored.Geometry.Face0RunMm + restored.Geometry.Face1RunMm, 8);
        Assert.NotEqual(geometry.Face0RunMm, restored.Geometry.Face0RunMm);
    }

    [Fact]
    public void LifecycleSchemasRemainUnchanged()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
    }

    [Fact]
    public void CommonLifecycleServices_DoNotBranchOnAsymmetricGable()
    {
        var repository = RepositoryRoot();
        var names = new[]
        {
            "RoofAttachedManualLifecycleService.cs", "RoofMirrorCloneDetachService.cs",
            "RoofSourceResizeChildPolicyService.cs", "RoofDisplayGroupService.cs",
            "RoofGeneratedMemberManualEditService.cs", "LiveGeometrySynchronizationService.cs",
        };
        foreach (var name in names)
        {
            var path = Directory.EnumerateFiles(Path.Combine(repository, "src"), name, SearchOption.AllDirectories).Single();
            Assert.DoesNotContain("AsymmetricGable", File.ReadAllText(path));
        }
    }

    [Fact]
    public void CommandPreviewDisplayAndGeneration_DispatchOnlyAtGeometryBoundary()
    {
        var repository = RepositoryRoot();
        string Read(string relative) => File.ReadAllText(Path.Combine(repository, relative));
        var commands = Read("src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs");
        var catalog = Read("src/AcKrovy.Localization/CommandUiCatalog.cs");
        var workflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
        var preview = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofTransientPreviewSession.cs");
        var generation = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedRafterSetService.cs");

        Assert.Contains("AK_ROOF_ASYM", catalog);
        Assert.Contains("RoofKind.AsymmetricGable", commands);
        Assert.Contains("new GableRoofGeometryWindow(", workflow);
        Assert.Contains("Face1BoundaryColorIndex", preview);
        Assert.Contains("SlopeDegrees = rafter.SlopeDegrees", generation);
        Assert.DoesNotContain("AsymmetricGable", generation);
    }

    private static RoofDefinitionData DefinitionData(
        RoofKind kind,
        double slope0,
        double slope1,
        int schemaVersion = RoofDefinitionDataSchema.CurrentVersion,
        double eaveHeightDifferenceMm = 0d) =>
        new(schemaVersion, kind, slope0,
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(4, RoofPolygonOrientation.CounterClockwise, 10000d, 6000d),
            Face1SlopeDegrees: slope1,
            EaveHeightDifferenceMm: eaveHeightDifferenceMm);

    private static RoofDefinitionData RoundTrip(RoofDefinitionData data)
    {
        var payload = RoofDefinitionDataCodec.Encode(data);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        return decoded!;
    }

    private static IReadOnlyList<RoofGeneratedMemberKey> Keys(SimpleGableRoofGeometry geometry) =>
        Layout(geometry).Rafters.Select(RoofGeneratedMemberKey.From).ToArray();

    private static SimpleGableRafterLayout Layout(SimpleGableRoofGeometry geometry)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(geometry, new RafterLayoutParameters(1000d, 80d));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static SimpleGableRoofGeometry Solve(
        IReadOnlyList<RoofPoint2D> vertices,
        double slope0,
        double slope1,
        double x,
        double y,
        double eaveHeightDifferenceMm = 0d)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            Validate(Input(vertices)),
            new RoofParameters(
                slope0,
                direction,
                Face1SlopeDegrees: slope1,
                EaveHeightDifferenceMm: eaveHeightDifferenceMm),
            RoofKind.AsymmetricGable));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static SimpleGableRoofGeometry SolveSimple(
        IReadOnlyList<RoofPoint2D> vertices, double slope, double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            Validate(Input(vertices)), new RoofParameters(slope, direction))).Geometry!;
    }

    private static double Tan(double degrees) => Math.Tan(degrees * Math.PI / 180d);
    private static RoofPoint2D P(double x, double y) => new(x, y);
    private static RoofPoint2D[] Rectangle() => [P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000)];
    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> points) => new(points, true, false, true);
    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var result = RoofFootprintValidator.Validate(input);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

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
