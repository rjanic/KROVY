using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryEraseRebuildTests
{
    [Theory]
    [InlineData(false, true, false, RoofOrdinaryEraseAction.DeleteCurrentAuto)]
    [InlineData(false, true, true, RoofOrdinaryEraseAction.DeleteCurrentAuto)]
    [InlineData(true, true, false, RoofOrdinaryEraseAction.DeleteIndependent)]
    [InlineData(true, true, true, RoofOrdinaryEraseAction.DeleteIndependent)]
    [InlineData(false, false, true, RoofOrdinaryEraseAction.RejectPhysicalOnly)]
    [InlineData(true, false, true, RoofOrdinaryEraseAction.RejectPhysicalOnly)]
    [InlineData(false, false, false, RoofOrdinaryEraseAction.None)]
    [InlineData(true, false, false, RoofOrdinaryEraseAction.None)]
    public void PlanEraseWins_IndependentAndDerivedAuthorityAreDistinct(bool independent, bool plan, bool solid, RoofOrdinaryEraseAction action) =>
        Assert.Equal(action, RoofOrdinaryEraseRules.Decide(independent, plan, solid));

    [Fact]
    public void RecipeAndLegacyOverrides_RoundTripInRoofDefinition_WithoutAnAutoInstance()
    {
        var legacy = Definition() with { SchemaVersion = RoofDefinitionDataSchema.EaveHeightVersion };
        var oldPayload = RoofDefinitionDataCodec.Encode(legacy);
        Assert.True(RoofDefinitionDataCodec.TryDecode(oldPayload, out var old, out _));
        Assert.Equal(5, old!.SchemaVersion);
        Assert.Null(old.OrdinaryRafterRecipe);
        var recipe = new RoofRafterGenerationRecipe(80, 160, 900, "C24 | A, žlté");
        var current = old with { SchemaVersion = RoofDefinitionDataSchema.CurrentVersion, OrdinaryRafterRecipe = recipe };
        var saved = RoofDefinitionDataCodec.Encode(current);
        Assert.True(RoofDefinitionDataCodec.TryDecode(saved, out var loaded, out _));
        Assert.Equal(recipe, loaded!.OrdinaryRafterRecipe);
        Assert.Equal(saved, RoofDefinitionDataCodec.Encode(loaded));
        Assert.Equal(current.RigidFootprint, loaded.RigidFootprint);
        Assert.Equal(current.Overrides, loaded.Overrides);
        Assert.Equal(oldPayload, RoofDefinitionDataCodec.Encode(old));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)]
    public void InvalidStoredRecipe_IsRejected(double width)
    {
        var data = Definition() with { OrdinaryRafterRecipe = new(width, 160, 900, "C24") };
        Assert.False(RoofDefinitionDataCodec.TryValidate(data, out _));
        Assert.Throws<ArgumentException>(() => RoofDefinitionDataCodec.Encode(data));
    }

    [Theory]
    [InlineData("80,160,NaN,QzI0")]
    [InlineData("80,160,900")]
    [InlineData("80,160,900,not-base64!")]
    public void MalformedRecipePayload_ReturnsFailureWithError(string recipe)
    {
        var legacy = RoofDefinitionDataCodec.Encode(Definition() with { SchemaVersion = RoofDefinitionDataSchema.EaveHeightVersion });
        var payload = $"6|{recipe}|{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(legacy))}";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Null(data);
        Assert.Equal(RoofDefinitionDataDecodeError.MalformedPayload, error);
    }

    [Fact]
    public void RebuildUsesCurrentRoofGeometry_RecreatesSuppressedSlot_AndPreservesRecipe()
    {
        var source = new RoofFootprintInput(new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }, true);
        var footprint = RoofFootprintValidator.Validate(source).Footprint!;
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var geometry = RoofGeometrySolver.Solve(new RoofDefinition(footprint, new RoofParameters(35, direction), RoofKind.SimpleGable)).Geometry!;
        var layout = RoofRafterLayoutSolver.Solve(geometry, new RafterLayoutParameters(900, 80)).Layout!;
        var recipe = new RoofRafterGenerationRecipe(80, 160, 900, "C24");
        var original = Definition() with { OrdinaryRafterRecipe = recipe,
            ManualOverrides = new[] { RoofGeneratedMemberOverride.Suppress(layout.Rafters[0].LogicalKey, "K4") } };
        var before = RoofDefinitionDataCodec.Encode(original);
        var current = RoofOrdinaryRebuildRules.Prepare(original);
        Assert.Empty(current.Overrides);
        Assert.Equal(recipe, current.OrdinaryRafterRecipe);
        var replay = RoofGeneratedMemberReplayPlanner.Create(layout, 0, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, current.Overrides);
        Assert.True(replay.IsValid);
        Assert.Equal(layout.Rafters.Count, replay.MaterializedCount);
        Assert.Equal(0, replay.SuppressedCount);
        Assert.All(replay.Items, item => Assert.Equal(RoofGeneratedMemberOverrideRules.CanonicalGeometry(item.Rafter, 0), item.Geometry));
        Assert.Equal(before, RoofDefinitionDataCodec.Encode(original));
        var changedGeometry = RoofGeometrySolver.Solve(new RoofDefinition(footprint, new RoofParameters(42, direction), RoofKind.SimpleGable)).Geometry!;
        var edited = RoofDefinitionPersistence.UpdateGeometry(current, source, changedGeometry);
        Assert.Equal(42, edited.SlopeDegrees);
        Assert.Equal(recipe, edited.OrdinaryRafterRecipe);
        Assert.Equal(recipe, RoofGeneratedMemberOverrideRules.PreserveEditState(edited with { OrdinaryRafterRecipe = null }, current).OrdinaryRafterRecipe);
    }

    [Fact]
    public void CurrentAdapterDoesNotSuppressEraseMirrorOrDetach_AndClaimsBeforeLegacyRecovery()
    {
        var erase = Read("RoofOrdinaryEraseLifecycleService.cs");
        foreach (var forbidden in new[] { "ConfirmAutomaticDetach", "TryDetach(", "TransferFromRoof", "Suppress(", "Suppressed = true", "AttachedManualTimberStore.Write" })
            Assert.DoesNotContain(forbidden, erase);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Restore", erase);
        Assert.Contains("RoofPhysical3DWarningService.Show()", erase);
        Assert.Contains("deleted.Any(id => !id.IsErased)", erase);
        Assert.Contains("transaction.Commit()", erase);
        Assert.Contains("ClaimOrdinary", erase);
        Assert.Contains("using var cleanup", erase);
        Assert.Contains("snapshot.IsOrdinaryClaimed(memberHandle)", Read("RoofUnsupportedStretchRecoveryService.cs"));
        var live = Read("LiveGeometrySynchronizationService.cs");
        var first = live.IndexOf("var ordinaryEraseClaimed", StringComparison.Ordinal);
        Assert.True(first > 0 && first < live.IndexOf("RoofStructuralNativeEditService.Process", first, StringComparison.Ordinal));
        Assert.True(first < live.IndexOf("RoofLiveResizeService.Process", first, StringComparison.Ordinal));
        Assert.Contains("_ordinaryEraseContext?.Dispose()", live);
        Assert.Contains("_ordinaryEraseContext = null", live);
        var mirror = Read("RoofOrdinaryCopyLifecycleService.cs");
        Assert.DoesNotContain("TryWriteSuppressOverride", mirror);
        Assert.Contains("suppressionWritten=False", mirror);
        Assert.DoesNotContain("Suppress(", Read("RoofIndependentOrdinaryDetachService.cs"));
        var generator = Read("RoofGeneratedRafterSetService.cs");
        Assert.Contains("OrdinaryRafterRecipe is { } savedRecipe", generator);
        Assert.Contains("rebuildReason != \"roof-edit\"", generator);
        Assert.Contains("FindByOwner", generator);
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore", generator);
        Assert.Contains("PrepareGeneratorDefinition(transaction, owner)", generator);
        var fallback = Read("RoofGeneratedMemberManualEditService.cs");
        var branch = fallback[fallback.IndexOf("if (isErase)", StringComparison.Ordinal)..fallback.IndexOf("else", fallback.IndexOf("if (isErase)", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.DoesNotContain("RoofGeneratedMemberOverride.Suppress(", branch);
    }

    private static RoofDefinitionData Definition() => new(RoofDefinitionDataSchema.CurrentVersion, RoofKind.SimpleGable, 35,
        RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
        RigidFootprint: new(4, RoofPolygonOrientation.CounterClockwise, 10000, 6000), EditState: RoofEditState.Unlocked);

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
