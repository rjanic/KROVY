using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryEditPreviewTests
{
    [Theory]
    [InlineData(RoofKind.SimpleGable, 35d, false)]
    [InlineData(RoofKind.SimpleGable, 42d, true)]
    [InlineData(RoofKind.Hip, 35d, false)]
    [InlineData(RoofKind.Hip, 42d, true)]
    public void ErasedAutoStation_ReturnsInPreviewAndApply_WithoutUsingLiveInventory(
        RoofKind kind, double slope, bool eraseAll)
    {
        var source = new RoofFootprintInput(new RoofPoint2D[]
        { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }, true);
        var footprint = RoofFootprintValidator.Validate(source).Footprint!;
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var geometry = RoofGeometrySolver.Solve(new RoofDefinition(footprint,
            new RoofParameters(slope, kind == RoofKind.Hip ? null : direction), kind)).Geometry!;
        var recipe = new RoofRafterGenerationRecipe(80, 160, 900, "C24");
        var definition = RoofDefinitionPersistence.Create(source, footprint, geometry) with { OrdinaryRafterRecipe = recipe };
        var parameters = new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm, MinimumAutomaticLengthMm: 200);
        var layout = RoofRafterLayoutSolver.Solve(geometry, parameters).Layout!;
        var created = RoofOrdinaryRebuildRules.CreateReplayPlan(layout, definition);
        Assert.True(created.IsValid);
        Assert.NotEmpty(created.Items);
        var missingKey = created.Items[created.Items.Count / 2].Rafter.LogicalKey;
        var liveAuto = created.Items.ToDictionary(item => item.Rafter.LogicalKey, item => item.Geometry!.Value);
        if (eraseAll) liveAuto.Clear(); else Assert.True(liveAuto.Remove(missingKey));
        Assert.False(liveAuto.ContainsKey(missingKey));
        var independent = new RoofGeneratedMemberGeometry(new(25000, 16000, 0), new(28000, 18000, 0));
        var before = RoofDefinitionDataCodec.Encode(definition);

        // Production preview and Apply share this generator plan. Neither takes liveAuto
        // nor the Independent inventory as an input; even an empty live set is irrelevant.
        var preview = RoofOrdinaryRebuildRules.CreateReplayPlan(layout, definition);
        Assert.Equal(created.Items.Count, preview.MaterializedCount);
        Assert.Contains(preview.Items, item => item.Rafter.LogicalKey == missingKey && item.Geometry.HasValue);
        Assert.False(liveAuto.ContainsKey(missingKey)); // preview is read-only
        var applied = RoofOrdinaryRebuildRules.CreateReplayPlan(layout, definition);
        var rebuilt = applied.Items.Where(item => item.Geometry.HasValue)
            .ToDictionary(item => item.Rafter.LogicalKey, item => item.Geometry!.Value);
        Assert.Equal(preview.Items.Select(item => (item.Rafter.LogicalKey, item.Geometry)),
            applied.Items.Select(item => (item.Rafter.LogicalKey, item.Geometry)));
        Assert.Equal(created.Items.Count, rebuilt.Count);
        Assert.Equal(created.Items.Single(item => item.Rafter.LogicalKey == missingKey).Geometry, rebuilt[missingKey]);
        Assert.DoesNotContain(preview.Items, item => item.Geometry == independent);
        Assert.Equal(before, RoofDefinitionDataCodec.Encode(definition));
        Assert.Equal(0, preview.SuppressedCount);
        Assert.Empty(definition.Overrides);
    }

    [Fact]
    public void PreviewUsesPreparedLegacyDefinition_WithoutPersistingExclusionOrMutatingIt()
    {
        var source = new RoofFootprintInput(new RoofPoint2D[]
        { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }, true);
        var footprint = RoofFootprintValidator.Validate(source).Footprint!;
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var geometry = RoofGeometrySolver.Solve(new RoofDefinition(footprint, new RoofParameters(35, direction), RoofKind.SimpleGable)).Geometry!;
        var layout = RoofRafterLayoutSolver.Solve(geometry, new RafterLayoutParameters(900, 80)).Layout!;
        var definition = RoofDefinitionPersistence.Create(source, footprint, geometry) with
        { ManualOverrides = new[] { RoofGeneratedMemberOverride.Suppress(layout.Rafters[0].LogicalKey, "K4") } };
        var before = RoofDefinitionDataCodec.Encode(definition);
        var preview = RoofOrdinaryRebuildRules.CreateReplayPlan(layout, definition);
        Assert.Equal(layout.Rafters.Count, preview.MaterializedCount);
        Assert.Equal(0, preview.SuppressedCount);
        Assert.Equal(before, RoofDefinitionDataCodec.Encode(definition));
    }

    [Fact]
    public void AdapterPreviewAndApplyShareGeneratorInputs_AndPreviewOnlyAddsTransientDrawables()
    {
        var edit = Read("RoofEditCommandWorkflow.cs");
        var preview = edit[edit.IndexOf("private static void ShowPreview(", StringComparison.Ordinal)..
            edit.IndexOf("private static bool TryPromptOrientationDirection", StringComparison.Ordinal)];
        Assert.Contains("OpenMode.ForRead", preview);
        Assert.Contains("TryResolveGeneratorRecipe", preview);
        Assert.Contains("CreateGeneratorLayout", preview);
        Assert.Contains("RoofOrdinaryRebuildRules.CreateReplayPlan", preview);
        Assert.Contains("sourceElevation, ordinaryPlan)", preview);
        foreach (var forbidden in new[] { "OpenMode.ForWrite", "RoofDefinitionStore.Write", "transaction.Commit", "TryReplace", "RoofIndependentOrdinaryTimberStore", "FindByOwner" })
            Assert.DoesNotContain(forbidden, preview);
        var generator = Read("RoofGeneratedRafterSetService.cs");
        var recipe = generator[generator.IndexOf("internal static bool TryResolveGeneratorRecipe", StringComparison.Ordinal)..
            generator.IndexOf("internal static RoofRafterLayoutResult CreateGeneratorLayout", StringComparison.Ordinal)];
        Assert.True(recipe.IndexOf("OrdinaryRafterRecipe", StringComparison.Ordinal) < recipe.IndexOf("FindByOwner", StringComparison.Ordinal));
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore", recipe);
        var apply = generator[generator.IndexOf("private static ReplacementOutcome ReplacePreparedSetWithRecipe(", StringComparison.Ordinal)..
            generator.IndexOf("public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize(", StringComparison.Ordinal)];
        Assert.Contains("CreateGeneratorLayout", apply);
        Assert.Contains("RoofOrdinaryRebuildRules.CreateReplayPlan", apply);
        var drawables = Read("RoofTransientPreviewSession.cs");
        Assert.Contains("session.AddGeneratedRafters(ordinaryPlan)", drawables);
        Assert.Contains("foreach (var item in plan.Items)", drawables);
        Assert.Contains("MapGeneratedRafterSegments(plan)", drawables);
        Assert.Contains("MapPoint(segment.Start), MapPoint(segment.End)", drawables);
        Assert.DoesNotContain("AppendEntity", drawables);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", drawables);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
