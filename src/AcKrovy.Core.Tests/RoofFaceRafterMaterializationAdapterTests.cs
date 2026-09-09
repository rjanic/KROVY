using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofFaceRafterMaterializationAdapterTests
{
    [Theory]
    [InlineData(500d, 64)]
    [InlineData(900d, 36)]
    public void RectangleHip_AdaptsFaceLayoutIntoExistingMaterializationContract(
        double spacingMm,
        int expectedCount)
    {
        var geometry = SolveHip(Rectangle(), 30d);
        var faceLayout = CreateFaceLayout(geometry, spacingMm);
        Assert.Equal(expectedCount, faceLayout.Segments.Count);

        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            faceLayout,
            80d,
            out var layout));
        Assert.Equal(faceLayout.Signature, layout.Signature);
        Assert.Equal(faceLayout.Segments.Count, layout.Rafters.Count);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            layout.Signature,
            geometry.Signature));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature),
            layout.Signature));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            layout.Signature,
            layout.Signature));

        Assert.All(layout.Rafters, rafter =>
        {
            Assert.Equal(RafterRoofFace.Face0, rafter.Face);
            Assert.Equal(layout.StationCount, rafter.StationCount);
            var timber = TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                SlopeDegrees = rafter.SlopeDegrees,
            };
            Assert.Equal(
                rafter.TrueLengthMm,
                TimberCalculator.CalculateActualLengthMm(timber, rafter.PlanLengthMm),
                8);
        });
        Assert.Equal(
            layout.Rafters.Count,
            layout.Rafters.Select(item => item.LogicalKey).Distinct().Count());
    }

    [Theory]
    [InlineData("L", 70, 6)]
    [InlineData("U", 112, 12)]
    [InlineData("T", 88, 12)]
    public void ConcaveHipLayouts_PreserveOrdinaryCountsIncludingRidgeValley(
        string name,
        int expectedTotal,
        int expectedRidgeValley)
    {
        var polygon = name switch
        {
            "L" => LShape(),
            "U" => UShape(),
            _ => TShape(),
        };
        var geometry = SolveHip(polygon, 30d);
        var faceLayout = CreateFaceLayout(geometry, 500d);
        Assert.Equal(expectedTotal, faceLayout.Segments.Count);
        Assert.Equal(expectedRidgeValley, faceLayout.Segments.Count(IsRidgeValley));

        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            faceLayout,
            80d,
            out var layout));
        Assert.Equal(faceLayout.Signature, layout.Signature);
        Assert.Equal(expectedTotal, layout.Rafters.Count);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        Assert.All(
            faceLayout.Segments.Where(IsRidgeValley),
            segment => Assert.Contains(
                layout.Rafters,
                rafter => NearlyEqual(rafter.PlanStart, segment.PlanStart) &&
                          NearlyEqual(rafter.PlanEnd, segment.PlanEnd)));
    }

    [Fact]
    public void ConnectedRidgeAlignedSegmentsRemainUnchangedThroughAdapter()
    {
        var geometry = SolveHip(TShape(), 30d);
        var faceLayout = CreateFaceLayout(geometry, 900d);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            faceLayout,
            80d,
            out var layout));

        for (var index = 0; index < faceLayout.Segments.Count; index++)
        {
            var segment = faceLayout.Segments[index];
            var rafter = layout.Rafters[index];
            Assert.True(NearlyEqual(rafter.PlanStart, segment.PlanStart));
            Assert.True(NearlyEqual(rafter.PlanEnd, segment.PlanEnd));
            Assert.Equal(segment.StationDistanceMm, rafter.StationPositionMm, 8);
        }
    }

    [Fact]
    public void HipValidation_BuildsRequestAndAdaptedLayoutWithoutHostGeometry()
    {
        var geometry = SolveHip(Rectangle(), 45d);
        var validation = RoofRafterRequestValidator.Validate(
            geometry,
            100d,
            180d,
            500d,
            500d,
            "  Smrek C24  ");

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Request);
        Assert.NotNull(validation.Layout);
        Assert.Equal(64, validation.Layout!.Rafters.Count);
        Assert.Equal(100d, validation.Request!.WidthMm);
        Assert.Equal(180d, validation.Request.HeightMm);
        Assert.Equal(500d, validation.Request.MaximumSpacingMm);
        Assert.Equal("Smrek C24", validation.Request.Material);
        Assert.Equal(45d, validation.Request.RoofSlopeDegrees, 8);
        Assert.All(validation.Layout.Rafters, rafter => Assert.Equal(45d, rafter.SlopeDegrees, 8));
        Assert.Equal(
            CreateFaceLayout(geometry, 500d).Signature,
            validation.Layout.Signature);
        Assert.Equal(500d, validation.Layout.RequestedMaximumSpacingMm);
    }

    [Fact]
    public void InvalidAutomaticSpacingIsRejectedBeforeHipLayout()
    {
        var geometry = SolveHip(Rectangle(), 30d);
        var validation = RoofRafterRequestValidator.Validate(
            geometry,
            80d,
            160d,
            499d,
            500d,
            "Smrek C24");

        Assert.False(validation.IsValid);
        Assert.Equal(
            RoofRafterRequestValidationError.InvalidMaximumSpacing,
            validation.Error);
        Assert.Null(validation.Layout);
    }

    [Fact]
    public void AdapterRejectsEmptyOrSingleSegmentLayouts()
    {
        var geometry = SolveHip(Rectangle(), 30d);
        var empty = new RoofFaceRafterLayout(500d, Array.Empty<RoofFaceRafterSegment>(), "x");
        Assert.False(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            empty,
            80d,
            out _));
    }

    [Fact]
    public void ReplayPlannerMaterializesAllAdaptedHipRaftersCanonically()
    {
        var geometry = SolveHip(Rectangle(), 30d);
        var faceLayout = CreateFaceLayout(geometry, 500d);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            faceLayout,
            80d,
            out var layout));

        var plan = RoofGeneratedMemberReplayPlanner.Create(
            layout,
            0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            Array.Empty<RoofGeneratedMemberOverride>());

        Assert.True(plan.IsValid);
        Assert.Equal(64, plan.MaterializedCount);
        Assert.All(plan.Items, item =>
        {
            Assert.Equal(RoofGeneratedMemberReplayDisposition.Canonical, item.Disposition);
            Assert.True(item.Geometry.HasValue);
            var geometry3d = item.Geometry!.Value;
            Assert.Equal(item.Rafter.PlanStart.X, geometry3d.Start.X, 8);
            Assert.Equal(item.Rafter.PlanStart.Y, geometry3d.Start.Y, 8);
            Assert.Equal(item.Rafter.PlanEnd.X, geometry3d.End.X, 8);
            Assert.Equal(item.Rafter.PlanEnd.Y, geometry3d.End.Y, 8);
        });
    }

    [Fact]
    public void LHipPermanentCreatePath_ProducesSeventyValidUniqueSchemaOneCandidates()
    {
        const double spacingMm = 500d;
        var geometry = SolveHip(LShape(), 30d);
        var faceLayout = CreateFaceLayout(geometry, spacingMm);
        var validation = RoofRafterRequestValidator.Validate(
            geometry,
            100d,
            180d,
            spacingMm,
            spacingMm,
            "Smrek C24");

        Assert.True(validation.IsValid);
        var layout = Assert.IsType<RoofRafterLayout>(validation.Layout);
        Assert.Equal(70, faceLayout.Segments.Count);
        Assert.Equal(6, faceLayout.Segments.Count(IsRidgeValley));
        Assert.Equal(70, layout.Rafters.Count);
        Assert.Equal(faceLayout.Signature, layout.Signature);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        Assert.Equal(70, layout.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.Equal(70, layout.Rafters.Select(GeometryKey).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 70), layout.Rafters.Select(item => item.StationIndex));
        Assert.All(layout.Rafters, rafter =>
        {
            Assert.Equal(RafterRoofFace.Face0, rafter.Face);
            Assert.Equal(70, rafter.StationCount);
            Assert.True(double.IsFinite(rafter.PlanStart.X));
            Assert.True(double.IsFinite(rafter.PlanStart.Y));
            Assert.True(double.IsFinite(rafter.PlanEnd.X));
            Assert.True(double.IsFinite(rafter.PlanEnd.Y));
            Assert.True(double.IsFinite(rafter.PlanLengthMm));
            Assert.True(double.IsFinite(rafter.TrueLengthMm));
            Assert.True(rafter.PlanLengthMm > RoofFaceRafterLayoutService.CoordinateToleranceMm);
            Assert.True(rafter.TrueLengthMm > RoofFaceRafterLayoutService.CoordinateToleranceMm);
        });
        Assert.Equal(250d, layout.Rafters.Min(item => item.PlanLengthMm), 8);
        Assert.Equal(1500d, layout.Rafters.Max(item => item.PlanLengthMm), 8);

        var replay = RoofGeneratedMemberReplayPlanner.Create(
            layout,
            0d,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            Array.Empty<RoofGeneratedMemberOverride>());
        Assert.True(replay.IsValid);
        Assert.Equal(70, replay.MaterializedCount);
        Assert.All(replay.Items, item =>
        {
            Assert.Equal(RoofGeneratedMemberReplayDisposition.Canonical, item.Disposition);
            Assert.True(item.Geometry.HasValue);
        });

        foreach (var rafter in layout.Rafters)
        {
            var metadata = new RoofGeneratedTimberData(
                RoofGeneratedTimberDataSchema.CurrentVersion,
                "2912",
                RoofGeneratedTimberKind.Rafter,
                rafter.Face,
                rafter.StationIndex,
                rafter.StationCount,
                spacingMm,
                layout.Signature);
            Assert.True(RoofGeneratedTimberDataCodec.TryValidate(metadata, out var validationError));
            Assert.Equal(RoofGeneratedTimberDataDecodeError.None, validationError);

            var payload = RoofGeneratedTimberDataCodec.Encode(metadata);
            Assert.True(RoofGeneratedTimberDataCodec.TryDecode(
                payload,
                out var decoded,
                out var decodeError));
            Assert.Equal(RoofGeneratedTimberDataDecodeError.None, decodeError);
            Assert.Equal(metadata, decoded);
        }
    }

    private static bool IsRidgeValley(RoofFaceRafterSegment segment) =>
        (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge &&
         segment.EndBoundaryRole == RoofRafterBoundaryRole.Valley) ||
        (segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
         segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);

    private static bool NearlyEqual(RoofPoint2D first, RoofPoint2D second) =>
        Math.Abs(first.X - second.X) <= RoofFaceRafterLayoutService.CoordinateToleranceMm &&
        Math.Abs(first.Y - second.Y) <= RoofFaceRafterLayoutService.CoordinateToleranceMm;

    private static string GeometryKey(RoofRafterGeometry rafter)
    {
        var start = $"{rafter.PlanStart.X:R},{rafter.PlanStart.Y:R}";
        var end = $"{rafter.PlanEnd.X:R},{rafter.PlanEnd.Y:R}";
        return string.CompareOrdinal(start, end) <= 0
            ? start + ";" + end
            : end + ";" + start;
    }

    private static RoofFaceRafterLayout CreateFaceLayout(
        HipRoofGeometry geometry,
        double spacingMm)
    {
        var result = RoofFaceRafterLayoutService.Create(geometry.Topology, spacingMm);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFaceRafterLayout>(result.Layout);
    }

    private static HipRoofGeometry SolveHip(IReadOnlyList<RoofPoint2D> polygon, double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(polygon, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(solved.Geometry);
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];
}
