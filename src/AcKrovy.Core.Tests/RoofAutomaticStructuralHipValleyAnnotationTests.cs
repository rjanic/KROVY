using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticStructuralHipValleyAnnotationTests
{
    private const double EqualPitchHipSlopeDegrees = 22.208d;

    [Fact]
    public void EqualPerpendicularFacesAt30Degrees_ProduceApproximately22_208MemberSlope()
    {
        Assert.True(RoofPhysicalStructuralFold.TryInclinationDegreesFromUpwardPlaneNormals(
            0d,
            -Math.Sin(Radians(30d)),
            Math.Cos(Radians(30d)),
            -Math.Sin(Radians(30d)),
            0d,
            Math.Cos(Radians(30d)),
            out var slope));

        Assert.Equal(EqualPitchHipSlopeDegrees, slope, 3);

        var plan = CreatePlan(Rectangle());
        Assert.All(
            plan.Items.Where(item => item.ElementType == TimberElementType.HipRafter),
            item =>
            {
                Assert.Equal(EqualPitchHipSlopeDegrees, item.TimberData.SlopeDegrees, 3);
                AssertArrowPointsDownhill(item);
            });
    }

    [Fact]
    public void ValleyEqualPitch_UsesSamePhysicalDownhillArrowRule()
    {
        var valley = Assert.Single(
            CreatePlan(LShape()).Items,
            item => item.ElementType == TimberElementType.ValleyRafter);
        Assert.True(valley.TimberData.SlopeDegrees > 0d);
        Assert.NotEqual(30d, valley.TimberData.SlopeDegrees);
        AssertArrowPointsDownhill(valley);
    }

    [Fact]
    public void EndpointReversal_PreservesMemberSlopeMagnitudeAndDownhillDisplay()
    {
        var plan = CreatePlan(LShape());
        foreach (var item in plan.Items)
        {
            var forward = item.Segment3D;
            var reversed = new RoofSegment3D(forward.End, forward.Start);
            Assert.Equal(
                forward.InclinationDegreesAboveHorizontal,
                reversed.InclinationDegreesAboveHorizontal,
                12);
            Assert.Equal(
                item.TimberData.SlopeDegrees,
                forward.InclinationDegreesAboveHorizontal,
                9);

            var forwardReversedFlag =
                TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(forward);
            var reversedReversedFlag =
                TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(reversed);
            Assert.Equal(item.TimberData.IsSlopeDirectionReversed, forwardReversedFlag);
            Assert.NotEqual(forwardReversedFlag, reversedReversedFlag);

            AssertArrowPointsDownhill(item);
            AssertArrowPointsDownhill(item with
            {
                Segment3D = reversed,
                TimberData = item.TimberData with
                {
                    IsSlopeDirectionReversed = reversedReversedFlag,
                },
            });

            var forwardDown = TimberSlopeDirectionRules.ResolveDownhillPlanDirection(forward);
            var reversedDown = TimberSlopeDirectionRules.ResolveDownhillPlanDirection(reversed);
            Assert.True(forwardDown.X * reversedDown.X + forwardDown.Y * reversedDown.Y > 0d);
        }

        Assert.True(RoofPhysicalStructuralFold.TryInclinationDegreesFromUpwardPlaneNormals(
            0d,
            -Math.Sin(Radians(30d)),
            Math.Cos(Radians(30d)),
            -Math.Sin(Radians(30d)),
            0d,
            Math.Cos(Radians(30d)),
            out var a));
        Assert.True(RoofPhysicalStructuralFold.TryInclinationDegreesFromUpwardPlaneNormals(
            -Math.Sin(Radians(30d)),
            0d,
            Math.Cos(Radians(30d)),
            0d,
            -Math.Sin(Radians(30d)),
            Math.Cos(Radians(30d)),
            out var b));
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void WindingReversal_PreservesMemberSlopeMagnitudeAndDownhillDisplay()
    {
        var forward = CreatePlan(LShape());
        var reversedPoints = LShape().Reverse().ToArray();
        var reversed = CreatePlan(reversedPoints);

        Assert.Equal(forward.Items.Count, reversed.Items.Count);
        Assert.Equal(
            forward.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v),
            reversed.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v));
        Assert.All(forward.Items, AssertArrowPointsDownhill);
        Assert.All(reversed.Items, AssertArrowPointsDownhill);
    }

    [Fact]
    public void CyclicFootprintStart_PreservesMemberSlopeMagnitudeAndDownhillDisplay()
    {
        var original = CreatePlan(LShape());
        var rotated = LShape().Skip(2).Concat(LShape().Take(2)).ToArray();
        var shifted = CreatePlan(rotated);

        Assert.Equal(original.Items.Count, shifted.Items.Count);
        Assert.Equal(
            original.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v),
            shifted.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v));
        Assert.All(original.Items, AssertArrowPointsDownhill);
        Assert.All(shifted.Items, AssertArrowPointsDownhill);
    }

    [Fact]
    public void MirroredRoof_PreservesMemberSlopeMagnitudeAndPhysicalDownhillDisplay()
    {
        var original = CreatePlan(LShape());
        var mirrored = CreatePlan(LShape().Select(point => new RoofPoint2D(-point.X, point.Y)).ToArray());

        Assert.Equal(original.Items.Count, mirrored.Items.Count);
        Assert.Equal(
            original.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v),
            mirrored.Items.Select(item => Round(item.TimberData.SlopeDegrees)).OrderBy(v => v));
        Assert.All(original.Items, AssertArrowPointsDownhill);
        Assert.All(mirrored.Items, AssertArrowPointsDownhill);
    }

    [Fact]
    public void UnequalAdjacentSlopes_UsePlaneIntersectionAndDownhillDirection()
    {
        Assert.True(RoofPhysicalStructuralFold.TryInclinationDegreesFromUpwardPlaneNormals(
            0d,
            -Math.Sin(Radians(20d)),
            Math.Cos(Radians(20d)),
            -Math.Sin(Radians(40d)),
            0d,
            Math.Cos(Radians(40d)),
            out var slope));

        Assert.True(slope > 0d);
        Assert.NotEqual(20d, slope, 3);
        Assert.NotEqual(40d, slope, 3);
        Assert.NotEqual(30d, slope, 3);

        var dx = Math.Sin(Radians(20d)) * Math.Cos(Radians(40d));
        var dy = Math.Cos(Radians(20d)) * Math.Sin(Radians(40d));
        var dz = Math.Sin(Radians(20d)) * Math.Sin(Radians(40d));
        var expected = RoofPhysicalStructuralFold.InclinationDegreesAboveHorizontal(dx, dy, dz);
        Assert.Equal(expected, slope, 12);

        // Synthetic member axis along the unequal-plane intersection: Start higher than End.
        var highToLow = new RoofSegment3D(
            new RoofPoint3D(0d, 0d, 1000d),
            new RoofPoint3D(dx * 1000d, dy * 1000d, 1000d - Math.Abs(dz) * 1000d));
        var lowToHigh = new RoofSegment3D(highToLow.End, highToLow.Start);
        Assert.False(TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(highToLow));
        Assert.True(TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(lowToHigh));
        AssertArrowPointsDownhill(new RoofAutomaticStructuralRafterPlanItem(
            new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 1, 2),
            TimberElementType.HipRafter,
            highToLow,
            TimberElementDefaults.For(TimberElementType.HipRafter) with
            {
                SlopeDegrees = slope,
                IsSlopeDirectionReversed =
                    TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(highToLow),
            }));
        AssertArrowPointsDownhill(new RoofAutomaticStructuralRafterPlanItem(
            new RoofStructuralLogicalKey(RoofStructuralRole.Valley, 1, 2),
            TimberElementType.ValleyRafter,
            lowToHigh,
            TimberElementDefaults.For(TimberElementType.ValleyRafter) with
            {
                SlopeDegrees = slope,
                IsSlopeDirectionReversed =
                    TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(lowToHigh),
            }));
    }

    [Fact]
    public void HipAndValley_UseSameFullLabelAnnotationStackAsOrdinaryRafter()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        Assert.Equal(TimberAnnotationMode.FullLabel, profile.DefaultAnnotationMode);

        var plan = RoofAutomaticStructuralRafterPlanner.Create(Resolve(LShape()), profile);
        Assert.All(plan.Items, item =>
        {
            Assert.Equal(TimberAnnotationMode.FullLabel, item.TimberData.AnnotationMode);
            var hipValleyPlan = TimberAnnotationRefreshPlanner.Create(item.TimberData, false);
            var ordinary = TimberElementDefaults.For(TimberElementType.Rafter, profile) with
            {
                SlopeDegrees = item.TimberData.SlopeDegrees,
            };
            var ordinaryPlan = TimberAnnotationRefreshPlanner.Create(ordinary, false);

            Assert.True(hipValleyPlan.EnsureLabel);
            Assert.True(hipValleyPlan.ReconcileSlopeArrow);
            Assert.True(hipValleyPlan.ReconcileSlopeAngleText);
            Assert.True(hipValleyPlan.ShouldSlopeArrowExist);
            Assert.True(hipValleyPlan.ShouldSlopeAngleTextExist);
            Assert.Equal(ordinaryPlan.EnsureLabel, hipValleyPlan.EnsureLabel);
            Assert.Equal(ordinaryPlan.ReconcileSlopeArrow, hipValleyPlan.ReconcileSlopeArrow);
            Assert.Equal(ordinaryPlan.ReconcileSlopeAngleText, hipValleyPlan.ReconcileSlopeAngleText);
            Assert.Equal(
                TimberSlopeAnnotationRules.ResolveDisplayAngleDegrees(
                    item.ElementType,
                    item.TimberData.SlopeDegrees),
                item.TimberData.SlopeDegrees);
        });
    }

    [Fact]
    public void DisplayedSlopeTextEqualsStoredPhysicalMemberSlope()
    {
        var plan = CreatePlan(LShape());
        Assert.All(plan.Items, item =>
        {
            Assert.Equal(
                item.TimberData.SlopeDegrees,
                TimberSlopeAnnotationRules.ResolveDisplayAngleDegrees(
                    item.ElementType,
                    item.TimberData.SlopeDegrees));
            Assert.NotEqual(0d, item.TimberData.SlopeDegrees);
            Assert.NotEqual(30d, item.TimberData.SlopeDegrees);
        });
    }

    [Fact]
    public void OrdinaryRafterSlopeRemainsFacePitchAndIsUnchangedByHipValleyRules()
    {
        var rafter = TimberElementDefaults.For(TimberElementType.Rafter);
        Assert.Equal(35d, rafter.SlopeDegrees);
        Assert.False(rafter.IsSlopeDirectionReversed);
        Assert.Equal(
            LengthCalculationMode.SlopeCorrected,
            TimberCalculator.ResolveLengthCalculationMode(rafter));

        var measurement = TimberCalculator.Measure(rafter, 3000d);
        Assert.Equal(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(3000d, 35d),
            measurement.ActualLengthMm,
            9);

        var hip = CreatePlan(Rectangle()).Items.First(item => item.ElementType == TimberElementType.HipRafter);
        Assert.Equal(LengthCalculationMode.PlanLength, hip.TimberData.LengthCalculationMode);
        Assert.Equal(
            hip.True3DLengthMm,
            TimberCalculator.Measure(hip.TimberData, hip.True3DLengthMm).ActualLengthMm,
            9);
        Assert.NotEqual(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(hip.True3DLengthMm, hip.TimberData.SlopeDegrees),
            TimberCalculator.Measure(hip.TimberData, hip.True3DLengthMm).ActualLengthMm);

        var replacement = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "RoofGeneratedRafterSetService.cs"));
        Assert.Contains("IsSlopeDirectionReversed = true", replacement);
        Assert.DoesNotContain(
            "TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay",
            replacement);
    }

    [Fact]
    public void SecondReconcile_UsesExistingSourceHandleUpsertPathWithoutDuplicateEngine()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "RoofAutomaticStructuralRafterMaterializationService.cs"));
        Assert.Contains("annotationTargets[existing.Id] = timberData", service);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements(", service);
        Assert.Contains("TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay", File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "AcKrovy.Core",
                "Services",
                "Roofs",
                "RoofAutomaticStructuralRafterPlanner.cs")));
        Assert.Equal(1, Count(service, "EnsureForCreatedElements("));
        Assert.DoesNotContain("DeleteDuplicates", service);
    }

    [Fact]
    public void TrueLengthEqualsSegmentLengthWithoutSecondSlopeCorrection()
    {
        var plan = CreatePlan(LShape());
        var hip = plan.Items.First(item => item.ElementType == TimberElementType.HipRafter);
        var valley = Assert.Single(plan.Items, item => item.ElementType == TimberElementType.ValleyRafter);

        foreach (var item in new[] { hip, valley })
        {
            var measurement = TimberCalculator.Measure(item.TimberData, item.True3DLengthMm);
            Assert.Equal(item.Segment3D.LengthMm, measurement.ActualLengthMm, 9);
            Assert.Equal(item.True3DLengthMm, measurement.ActualLengthMm, 9);
            Assert.Equal(LengthCalculationMode.PlanLength, item.TimberData.LengthCalculationMode);
        }
    }

    [Theory]
    [InlineData("L", 5, 1)]
    [InlineData("U", 6, 2)]
    [InlineData("T", 6, 2)]
    public void PhysicalFoldCountsRemainPublished(string name, int hips, int valleys)
    {
        var points = name switch
        {
            "L" => LShape(),
            "U" => Points((0, 0), (10000, 0), (10000, 9000), (7000, 9000), (7000, 3000), (3000, 3000), (3000, 9000), (0, 9000)),
            "T" => Points((0, 0), (10000, 0), (10000, 3000), (6500, 3000), (6500, 9000), (3500, 9000), (3500, 3000), (0, 3000)),
            _ => throw new InvalidOperationException(name),
        };
        var plan = CreatePlan(points);
        Assert.Equal(hips, plan.Items.Count(item => item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(valleys, plan.Items.Count(item => item.ElementType == TimberElementType.ValleyRafter));
    }

    [Fact]
    public void SchemasAndProductVersionRemainUnchanged()
    {
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofStructuralGeneratedDataSchema.CurrentVersion);
        Assert.Equal(1, RoofBoundaryIdentitySchema.CurrentVersion);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", props);
    }

    [Fact]
    public void ProfileAnnotationModeIsRespectedWithoutHipValleySpecialMode()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.DefaultAnnotationMode = TimberAnnotationMode.ItemNumberLeader;
        var plan = RoofAutomaticStructuralRafterPlanner.Create(Resolve(Rectangle()), profile);
        Assert.All(plan.Items, item =>
            Assert.Equal(TimberAnnotationMode.ItemNumberLeader, item.TimberData.AnnotationMode));
        Assert.DoesNotContain(
            plan.Items,
            item => item.TimberData.AnnotationMode == TimberAnnotationMode.NoAnnotations);
    }

    private static void AssertArrowPointsDownhill(RoofAutomaticStructuralRafterPlanItem item)
    {
        var segment = item.Segment3D;
        Assert.True(Math.Abs(segment.Start.Z - segment.End.Z) > 1e-9);
        Assert.Equal(
            TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(segment),
            item.TimberData.IsSlopeDirectionReversed);

        var midX = (segment.Start.X + segment.End.X) / 2d;
        var midY = (segment.Start.Y + segment.End.Y) / 2d;
        var arrow = TimberSlopeArrowCalculator.Calculate(
            segment.Start.X,
            segment.Start.Y,
            segment.End.X,
            segment.End.Y,
            midX,
            midY,
            item.TimberData.IsSlopeDirectionReversed);
        var downhill = TimberSlopeDirectionRules.ResolveDownhillPlanDirection(segment);
        var arrowX = arrow.TipX - arrow.TailX;
        var arrowY = arrow.TipY - arrow.TailY;
        Assert.True(
            arrowX * downhill.X + arrowY * downhill.Y > 0d,
            $"Arrow did not point downhill for {item.ElementType} {item.LogicalKey}.");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static RoofAutomaticStructuralRafterPlanResult CreatePlan(RoofPoint2D[] points) =>
        RoofAutomaticStructuralRafterPlanner.Create(Resolve(points));

    private static RoofStructuralEdgeResolutionResult Resolve(RoofPoint2D[] points)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var solved = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30),
            RoofKind.Hip));
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        return resolution;
    }

    private static RoofPoint2D[] Rectangle() =>
        Points((0, 0), (10000, 0), (10000, 6000), (0, 6000));

    private static RoofPoint2D[] LShape() =>
        Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000));

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();

    private static double Radians(double degrees) => degrees * Math.PI / 180d;

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
