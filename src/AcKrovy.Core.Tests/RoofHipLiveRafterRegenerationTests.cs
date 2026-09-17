using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofHipLiveRafterRegenerationTests
{
    [Fact]
    public void LayoutSolverRoutesHipThroughFaceLayoutAdapter()
    {
        var before = SolveHip(Rectangle(10000, 6000), 30d);
        var face = CreateFaceLayout(before, 500d);
        var solved = RoofRafterLayoutSolver.Solve(
            before,
            new RafterLayoutParameters(500d, 80d, 1d));

        Assert.True(solved.IsValid);
        Assert.NotNull(solved.Layout);
        Assert.Equal(face.Signature, solved.Layout!.Signature);
        Assert.Equal(64, solved.Layout.Rafters.Count);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(before, solved.Layout));
        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            solved.Layout.Signature,
            before.Signature));
    }

    [Fact]
    public void RectangleStretch_10000x6000_to_12000x6000_At500_Replaces64With72()
    {
        var beforeGeometry = SolveHip(Rectangle(10000, 6000), 30d);
        var afterGeometry = SolveHip(Rectangle(12000, 6000), 30d);
        var recipe = new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24");

        var before = RoofRafterLayoutSolver.Solve(
            beforeGeometry,
            new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm, 1d));
        var after = RoofRafterLayoutSolver.Solve(
            afterGeometry,
            new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm, 1d));

        Assert.True(before.IsValid);
        Assert.True(after.IsValid);
        Assert.Equal(64, before.Layout!.Rafters.Count);
        Assert.Equal(72, after.Layout!.Rafters.Count);
        Assert.NotEqual(before.Layout.Signature, after.Layout.Signature);
        Assert.Equal(500d, after.Layout.RequestedMaximumSpacingMm);
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            before.Layout.Signature,
            before.Layout.Signature));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(after.Layout.Signature),
            after.Layout.Signature));
        Assert.False(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(before.Layout.Signature),
            after.Layout.Signature));
        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            after.Layout.Signature,
            afterGeometry.Signature));
        Assert.False(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            before.Layout.Signature,
            afterGeometry.Signature));
        Assert.All(after.Layout.Rafters, rafter => Assert.Equal(30d, rafter.SlopeDegrees, 8));
    }

    [Theory]
    [InlineData("L", 70)]
    [InlineData("U", 112)]
    [InlineData("T", 88)]
    public void ConcaveLayouts_RegenerateThroughSharedSolverWithStableCounts(
        string name,
        int expectedCount)
    {
        var polygon = name switch
        {
            "L" => LShape(),
            "U" => UShape(),
            _ => TShape(),
        };
        var geometry = SolveHip(polygon, 30d);
        var face = CreateFaceLayout(geometry, 500d);
        var solved = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(500d, 80d, 1d));

        Assert.Equal(expectedCount, face.Segments.Count);
        Assert.True(solved.IsValid);
        Assert.Equal(expectedCount, solved.Layout!.Rafters.Count);
        Assert.Equal(face.Signature, solved.Layout.Signature);
        Assert.Contains(
            face.Segments,
            segment =>
                (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge &&
                 segment.EndBoundaryRole == RoofRafterBoundaryRole.Valley) ||
                (segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
                 segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge));
    }

    [Fact]
    public void ConnectedRidgePhaseSurvivesSharedSolverRoundTrip()
    {
        var geometry = SolveHip(TShape(), 30d);
        var face = CreateFaceLayout(geometry, 900d);
        var solved = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 1d));

        Assert.True(solved.IsValid);
        Assert.Equal(face.Segments.Count, solved.Layout!.Rafters.Count);
        for (var index = 0; index < face.Segments.Count; index++)
        {
            Assert.Equal(
                face.Segments[index].StationDistanceMm,
                solved.Layout.Rafters[index].StationPositionMm,
                8);
            Assert.Equal(face.Segments[index].PlanStart.X, solved.Layout.Rafters[index].PlanStart.X, 8);
            Assert.Equal(face.Segments[index].PlanStart.Y, solved.Layout.Rafters[index].PlanStart.Y, 8);
            Assert.Equal(face.Segments[index].PlanEnd.X, solved.Layout.Rafters[index].PlanEnd.X, 8);
            Assert.Equal(face.Segments[index].PlanEnd.Y, solved.Layout.Rafters[index].PlanEnd.Y, 8);
        }
    }

    [Fact]
    public void RecipeUnificationRejectsInconsistentSpacingLikeGable()
    {
        var members = new[]
        {
            new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24"),
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
        };

        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(members, out _));
    }

    [Fact]
    public void SlopeChangeInvalidatesPreviousFreshnessAndRebuildsSlopeAwareLengths()
    {
        var footprint = Rectangle(10000, 6000);
        var at30 = SolveHip(footprint, 30d);
        var at45 = SolveHip(footprint, 45d);
        var recipe = new RafterLayoutParameters(500d, 80d, 1d);
        var layout30 = RoofRafterLayoutSolver.Solve(at30, recipe).Layout!;
        var layout45 = RoofRafterLayoutSolver.Solve(at45, recipe).Layout!;

        Assert.Equal(layout30.Rafters.Count, layout45.Rafters.Count);
        Assert.NotEqual(layout30.Signature, layout45.Signature);
        Assert.False(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            layout30.Signature,
            at45.Signature));
        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(
            layout45.Signature,
            at45.Signature));
        Assert.All(layout45.Rafters, rafter => Assert.Equal(45d, rafter.SlopeDegrees, 8));
        Assert.True(layout45.Rafters.Zip(layout30.Rafters, (a, b) => a.TrueLengthMm > b.TrueLengthMm).All(x => x));
    }

    [Fact]
    public void RoofEditHostRegression_RectanglePitch30To45RefreshesOrdinaryAndStructuralDesiredState()
    {
        var footprint = Rectangle(10000, 6000);
        var at30 = SolveHip(footprint, 30d);
        var at45 = SolveHip(footprint, 45d);
        var drawingParameters = new RafterLayoutParameters(
            MaximumSpacingMm: 900d,
            RafterPlanWidthMm: 80d,
            MinimumAutomaticLengthMm: 500d);

        var ordinary30 = RoofRafterLayoutSolver.Solve(at30, drawingParameters).Layout!;
        var ordinary45 = RoofRafterLayoutSolver.Solve(at45, drawingParameters).Layout!;
        Assert.Equal(32, ordinary30.Rafters.Count);
        Assert.NotEqual(ordinary30.Signature, ordinary45.Signature);
        Assert.All(ordinary45.Rafters, rafter =>
        {
            Assert.Equal(45d, rafter.SlopeDegrees, 8);
            Assert.True(RoofRafterLengthRules.MeetsMinimumTrueLength(
                rafter.TrueLengthMm,
                drawingParameters.MinimumAutomaticLengthMm));
        });

        var structural30 = CreateStructuralPlan(footprint, at30);
        var structural45 = CreateStructuralPlan(footprint, at45);
        Assert.Equal(4, structural30.Items.Count);
        Assert.Equal(4, structural45.Items.Count);
        Assert.All(structural45.Items, item =>
            Assert.Equal(TimberElementType.HipRafter, item.ElementType));
        Assert.DoesNotContain(
            structural45.Items,
            item => item.ElementType == TimberElementType.ValleyRafter);
        Assert.Equal(
            structural30.Items.Select(item => item.LogicalKey),
            structural45.Items.Select(item => item.LogicalKey));
        Assert.Equal(4, structural45.Items.Select(item => item.LogicalKey).Distinct().Count());

        var oldHighZ = structural30.Items.Max(HighZ);
        var newHighZ = structural45.Items.Max(HighZ);
        Assert.Equal(1732.0508075688772d, oldHighZ, 8);
        Assert.Equal(3000d, newHighZ, 8);
        Assert.DoesNotContain(
            structural45.Items.SelectMany(item =>
                new[] { item.Segment3D.Start.Z, item.Segment3D.End.Z }),
            z => Math.Abs(z - oldHighZ) <= 1e-6);

        Assert.All(structural45.Items, item =>
        {
            Assert.Equal(LengthCalculationMode.PlanLength, item.TimberData.LengthCalculationMode);
            Assert.Equal(item.True3DLengthMm, item.Segment3D.LengthMm, 8);
            Assert.Equal(
                item.Segment3D.InclinationDegreesAboveHorizontal,
                item.TimberData.SlopeDegrees,
                8);
            Assert.Equal(
                TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(item.Segment3D),
                item.TimberData.IsSlopeDirectionReversed);
        });
    }

    [Fact]
    public void SamePitchEditProducesTheSameUniqueOrdinaryAndStructuralDesiredSets()
    {
        var footprint = Rectangle(10000, 6000);
        var firstGeometry = SolveHip(footprint, 45d);
        var secondGeometry = SolveHip(footprint, 45d);
        var parameters = new RafterLayoutParameters(900d, 80d, 500d);
        var firstOrdinary = RoofRafterLayoutSolver.Solve(firstGeometry, parameters).Layout!;
        var secondOrdinary = RoofRafterLayoutSolver.Solve(secondGeometry, parameters).Layout!;
        var firstStructural = CreateStructuralPlan(footprint, firstGeometry);
        var secondStructural = CreateStructuralPlan(footprint, secondGeometry);

        Assert.Equal(firstOrdinary.Signature, secondOrdinary.Signature);
        Assert.Equal(
            firstOrdinary.Rafters.Select(item => item.LogicalKey),
            secondOrdinary.Rafters.Select(item => item.LogicalKey));
        Assert.Equal(
            firstStructural.Items.Select(DescribeStructural),
            secondStructural.Items.Select(DescribeStructural));
        Assert.Equal(
            firstStructural.Items.Count,
            firstStructural.Items.Select(item => item.LogicalKey).Distinct().Count());
    }

    [Fact]
    public void StationIdentityKeysRemainUniqueAcrossCountChangingResize()
    {
        var before = RoofRafterLayoutSolver.Solve(
            SolveHip(Rectangle(10000, 6000), 30d),
            new RafterLayoutParameters(500d, 80d, 1d)).Layout!;
        var after = RoofRafterLayoutSolver.Solve(
            SolveHip(Rectangle(12000, 6000), 30d),
            new RafterLayoutParameters(500d, 80d, 1d)).Layout!;

        Assert.Equal(64, before.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.Equal(72, after.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.Contains(after.Rafters, rafter => rafter.StationIndex >= 64);
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

    private static RoofAutomaticStructuralRafterPlanResult CreateStructuralPlan(
        RoofPoint2D[] footprint,
        HipRoofGeometry geometry)
    {
        var input = new RoofFootprintInput(footprint, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var plan = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(plan.IsValid, plan.Error.ToString());
        return plan;
    }

    private static double HighZ(RoofAutomaticStructuralRafterPlanItem item) =>
        Math.Max(item.Segment3D.Start.Z, item.Segment3D.End.Z);

    private static string DescribeStructural(RoofAutomaticStructuralRafterPlanItem item) =>
        string.Join(
            "|",
            item.LogicalKey,
            item.ElementType,
            item.Segment3D.Start,
            item.Segment3D.End,
            item.TimberData.SlopeDegrees.ToString("R"),
            item.TimberData.IsSlopeDirectionReversed);

    private static RoofPoint2D[] Rectangle(double length, double width) =>
        [new(0, 0), new(length, 0), new(length, width), new(0, width)];

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

public sealed class RoofHipLiveRafterRegenerationSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void HipResizeUsesGenericReplacementWithoutSecondEventSystem()
    {
        var liveResize = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");
        var replacement = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedRafterSetService.cs");
        var solver = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterLayoutSolver.cs");
        var edit = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofEditCommandWorkflow.cs");

        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", liveResize);
        Assert.Contains("forceRegenerateOnSourceResize: true", liveResize);
        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            liveResize);
        Assert.DoesNotContain("if (!isHip)", liveResize);
        Assert.DoesNotContain("HipGeneratedRafterReplacementService", liveResize + replacement);
        Assert.Contains("SolveHip(", solver);
        Assert.Contains("RoofFaceRafterLayoutService.Create", solver);
        Assert.Contains("RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout", solver);
        Assert.Contains("var layoutResult = RoofRafterLayoutSolver.Solve(", replacement);
        Assert.Contains("EraseGeneratedSet(", replacement);
        Assert.Contains("MaterializeCore(", replacement);
        Assert.Contains("TryRecoverRecipe(", replacement);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", edit);
        Assert.Contains("rebuildReason: \"roof-edit\"", edit);
        Assert.DoesNotContain("restored.Geometry is not HipRoofGeometry", edit);
    }

    [Fact]
    public void UndoRedoStillClearsPendingWithoutWriteTransaction()
    {
        var live = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "LiveGeometrySynchronizationService.cs");
        var ended = Segment(live, "private void CommandEnded", "private void CommandCancelled");
        var ignored = Segment(ended, "if (shouldIgnore)", "_ignoreCurrentCommand = false");
        Assert.Contains("isUndoRedo", ignored);
        Assert.Contains("ClearPendingLiveGeometryState()", ignored);
        Assert.DoesNotContain("StartTransaction(", ignored);
        Assert.DoesNotContain("TryReplaceForSupportedResize(", ignored);
    }

    private static string Segment(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, start);
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, end);
        return text[startIndex..endIndex];
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
