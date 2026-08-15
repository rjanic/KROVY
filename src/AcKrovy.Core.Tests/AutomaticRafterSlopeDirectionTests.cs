using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AutomaticRafterSlopeDirectionTests
{
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string ArrowRenderer = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeArrowService.cs");
    private static readonly string LiveRefresh = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");

    [Fact]
    public void BothFacesResolveSemanticDirectionFromRidgeToEave()
    {
        var layout = Layout();

        foreach (var face in Enum.GetValues<RafterRoofFace>())
        {
            var rafter = layout.Rafters.First(item => item.Face == face);
            var arrow = Arrow(rafter.PlanStart, rafter.PlanEnd, isReversed: true);
            AssertPointsToward(arrow, from: rafter.PlanEnd, to: rafter.PlanStart);
        }
    }

    [Fact]
    public void FaceArrowsVisuallyDivergeAwayFromRidge()
    {
        var layout = Layout();
        var face0 = layout.Rafters.First(item => item.Face == RafterRoofFace.Face0);
        var face1 = layout.Rafters.First(item => item.Face == RafterRoofFace.Face1);
        var arrow0 = Arrow(face0.PlanStart, face0.PlanEnd, true);
        var arrow1 = Arrow(face1.PlanStart, face1.PlanEnd, true);
        var vector0 = (X: arrow0.TipX - arrow0.TailX, Y: arrow0.TipY - arrow0.TailY);
        var vector1 = (X: arrow1.TipX - arrow1.TailX, Y: arrow1.TipY - arrow1.TailY);

        Assert.True(vector0.X * vector1.X + vector0.Y * vector1.Y < 0d);
    }

    [Fact]
    public void MoveRefreshPreservesDownhillDirection()
    {
        var rafter = Layout().Rafters.First(item => item.Face == RafterRoofFace.Face0);
        var movedStart = new RoofPoint2D(rafter.PlanStart.X + 123, rafter.PlanStart.Y - 456);
        var movedEnd = new RoofPoint2D(rafter.PlanEnd.X + 123, rafter.PlanEnd.Y - 456);

        AssertPointsToward(Arrow(movedStart, movedEnd, true), movedEnd, movedStart);
        Assert.Contains("TimberAnnotationService.EnsureForElement", LiveRefresh);
        Assert.DoesNotContain("RoofGeneratedTimber", LiveRefresh);
    }

    [Fact]
    public void RotateRefreshPreservesDownhillDirection()
    {
        var rafter = Layout().Rafters.First(item => item.Face == RafterRoofFace.Face1);
        var rotatedStart = Rotate(rafter.PlanStart, 37);
        var rotatedEnd = Rotate(rafter.PlanEnd, 37);

        AssertPointsToward(Arrow(rotatedStart, rotatedEnd, true), rotatedEnd, rotatedStart);
    }

    [Fact]
    public void ReversalDoesNotChangeSlopeAwarePhysicalLength()
    {
        var normal = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SlopeDegrees = 44,
            IsSlopeDirectionReversed = false,
        };
        var reversed = normal with { IsSlopeDirectionReversed = true };

        var first = TimberElementMeasurer.Measure(new TimberElementSnapshot(normal, 4000));
        var second = TimberElementMeasurer.Measure(new TimberElementSnapshot(reversed, 4000));
        Assert.Equal(first.ActualLengthMm, second.ActualLengthMm);
        Assert.Equal(first.CuttingLengthMm, second.CuttingLengthMm);
    }

    [Fact]
    public void FlipSlopeUsesNormalCanonicalToggleForGeneratedRafter()
    {
        Assert.True(TimberSlopeAnnotationRules.CanFlipDirection(TimberElementType.Rafter, 44));
        Assert.False(TimberSlopeAnnotationRules.ToggleDirection(true));
        Assert.Contains("TimberSlopeAnnotationRules.ToggleDirection", RoofUxSourceContractText.Read(
            "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs"));
    }

    [Fact]
    public void CreationUsesNormalMetadataContractWithoutRendererBranchOrSchemaChange()
    {
        Assert.Contains("IsSlopeDirectionReversed = true", Workflow);
        Assert.Contains("new Point3d(rafter.PlanStart.X", Workflow);
        Assert.Contains("new Point3d(rafter.PlanEnd.X", Workflow);
        Assert.DoesNotContain("RoofGeneratedTimber", ArrowRenderer);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(2, RoofDefinitionDataSchema.CurrentVersion);
    }

    private static SimpleGableRafterLayout Layout()
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 8000), new(0, 8000)],
            true));
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var geometry = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(44, direction))).Geometry!;
        return SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900, 80)).Layout!;
    }

    private static TimberSlopeArrowPlacement Arrow(
        RoofPoint2D start,
        RoofPoint2D end,
        bool isReversed) =>
        TimberSlopeArrowCalculator.Calculate(
            start.X,
            start.Y,
            end.X,
            end.Y,
            (start.X + end.X) / 2d,
            (start.Y + end.Y) / 2d,
            isReversed);

    private static void AssertPointsToward(
        TimberSlopeArrowPlacement arrow,
        RoofPoint2D from,
        RoofPoint2D to)
    {
        var arrowX = arrow.TipX - arrow.TailX;
        var arrowY = arrow.TipY - arrow.TailY;
        var expectedX = to.X - from.X;
        var expectedY = to.Y - from.Y;
        Assert.True(arrowX * expectedX + arrowY * expectedY > 0d);
    }

    private static RoofPoint2D Rotate(RoofPoint2D point, double degrees)
    {
        var angle = degrees * Math.PI / 180d;
        return new RoofPoint2D(
            point.X * Math.Cos(angle) - point.Y * Math.Sin(angle),
            point.X * Math.Sin(angle) + point.Y * Math.Cos(angle));
    }
}
