using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral proof that Hip/Valley desired structural sets follow authoritative
/// roof source geometry across a SupportedResize-like footprint change. AutoCAD HOST
/// STRETCH/GRIP_STRETCH wiring is covered by live-resize source contracts.
/// </summary>
public sealed class RoofAutomaticStructuralLiveRegenerationTests
{
    [Fact]
    public void LShapeStretch_UpdatesHipAndValleyGeometryLengthSlopeAndDownhill()
    {
        var before = Plan(LShape(8000, 3000, 8000));
        var after = Plan(LShape(11000, 3000, 8000));

        Assert.Equal(
            before.Items.Count(item => item.ElementType == TimberElementType.HipRafter),
            after.Items.Count(item => item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(1, before.Items.Count(item => item.ElementType == TimberElementType.ValleyRafter));
        Assert.Equal(1, after.Items.Count(item => item.ElementType == TimberElementType.ValleyRafter));

        var beforeByKey = before.Items.ToDictionary(item => item.LogicalKey);
        var afterByKey = after.Items.ToDictionary(item => item.LogicalKey);
        Assert.Equal(beforeByKey.Keys.OrderBy(key => key.ToString()), afterByKey.Keys.OrderBy(key => key.ToString()));

        var changed = 0;
        foreach (var (key, beforeItem) in beforeByKey)
        {
            var afterItem = afterByKey[key];
            Assert.Equal(beforeItem.ElementType, afterItem.ElementType);
            Assert.Equal(LengthCalculationMode.PlanLength, afterItem.TimberData.LengthCalculationMode);
            Assert.Equal(
                afterItem.Segment3D.LengthMm,
                TimberCalculator.Measure(afterItem.TimberData, afterItem.True3DLengthMm).ActualLengthMm,
                9);
            Assert.Equal(
                afterItem.Segment3D.InclinationDegreesAboveHorizontal,
                afterItem.TimberData.SlopeDegrees,
                9);
            Assert.Equal(
                TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(afterItem.Segment3D),
                afterItem.TimberData.IsSlopeDirectionReversed);
            AssertArrowPointsDownhill(afterItem);

            if (Math.Abs(beforeItem.True3DLengthMm - afterItem.True3DLengthMm) > 1e-6 ||
                Math.Abs(beforeItem.TimberData.SlopeDegrees - afterItem.TimberData.SlopeDegrees) > 1e-6 ||
                !SamePoint(beforeItem.Segment3D.Start, afterItem.Segment3D.Start) ||
                !SamePoint(beforeItem.Segment3D.End, afterItem.Segment3D.End))
            {
                changed++;
            }
        }

        Assert.True(changed > 0, "Stretch must change at least one Hip/Valley member.");
    }

    [Fact]
    public void TopologyChangingStretch_CanDropAndAddLogicalStructuralMembers()
    {
        var before = Plan(LShape(8000, 3000, 8000));
        // Collapse the inner notch toward a near-rectangle path that changes fold set.
        var after = Plan(Points(
            (0, 0),
            (12000, 0),
            (12000, 8000),
            (0, 8000)));

        Assert.True(before.IsValid);
        Assert.True(after.IsValid);
        Assert.Contains(before.Items, item => item.ElementType == TimberElementType.ValleyRafter);
        Assert.DoesNotContain(after.Items, item => item.ElementType == TimberElementType.ValleyRafter);
        Assert.NotEqual(
            before.Items.Select(item => item.LogicalKey.ToString()).OrderBy(v => v),
            after.Items.Select(item => item.LogicalKey.ToString()).OrderBy(v => v));
        Assert.All(after.Items, AssertArrowPointsDownhill);
    }

    [Fact]
    public void SecondPlanOnSameGeometry_IsIdempotentDesiredSet()
    {
        var first = Plan(LShape(8000, 3000, 8000));
        var second = Plan(LShape(8000, 3000, 8000));
        Assert.Equal(
            first.Items.Select(Describe).OrderBy(v => v),
            second.Items.Select(Describe).OrderBy(v => v));
    }

    [Fact]
    public void OrdinaryRafterLivePathAndUndoGuardsRemainUnchanged()
    {
        var live = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs"));
        var resize = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs"));
        var replacement = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs"));

        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand", live);
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName)", resize);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", resize);
        Assert.Contains("IsSlopeDirectionReversed = true", replacement);
        Assert.DoesNotContain("TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay", replacement);
        Assert.DoesNotContain("RoofAutomaticPurlinMaterializationService", resize);
    }

    [Fact]
    public void ManualTimberAndSchemasRemainUnchangedByStructuralLivePath()
    {
        var resize = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs"));
        Assert.Contains("replayAttachedManualChildren: true", resize);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", Member(
            resize,
            "private static ResizeApplyResult TryApplyResize",
            "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms"));
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofStructuralGeneratedDataSchema.CurrentVersion);
        Assert.Equal(1, RoofBoundaryIdentitySchema.CurrentVersion);
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Directory.Build.props")));
    }

    private static RoofAutomaticStructuralRafterPlanResult Plan(RoofPoint2D[] points)
    {
        var resolution = Resolve(points);
        var plan = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(plan.IsValid, plan.Error.ToString());
        return plan;
    }

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

    private static void AssertArrowPointsDownhill(RoofAutomaticStructuralRafterPlanItem item)
    {
        var segment = item.Segment3D;
        Assert.True(Math.Abs(segment.Start.Z - segment.End.Z) > 1e-9);
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
        Assert.True((arrow.TipX - arrow.TailX) * downhill.X + (arrow.TipY - arrow.TailY) * downhill.Y > 0d);
    }

    private static bool SamePoint(RoofPoint3D a, RoofPoint3D b) =>
        Math.Abs(a.X - b.X) <= 1e-6 &&
        Math.Abs(a.Y - b.Y) <= 1e-6 &&
        Math.Abs(a.Z - b.Z) <= 1e-6;

    private static string Describe(RoofAutomaticStructuralRafterPlanItem item) =>
        string.Join(
            "|",
            item.LogicalKey,
            item.ElementType,
            item.True3DLengthMm.ToString("R"),
            item.TimberData.SlopeDegrees.ToString("R"),
            item.TimberData.IsSlopeDirectionReversed);

    private static RoofPoint2D[] LShape(double longA, double notch, double longB) =>
        Points((0, 0), (longA, 0), (longA, notch), (notch, notch), (notch, longB), (0, longB));

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, endMarker);
        return source[start..end];
    }

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
