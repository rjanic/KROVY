using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source contracts for AK_ROOF_RAFTERS CREATE vs EDIT routing, recipe recovery,
/// and edited-recipe replacement. HOST behavior remains separate.
/// </summary>
public sealed class RoofRafterEditModeSourceContractTests
{
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string Replacement = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Window = Read(
        "src", "AcKrovy.AutoCAD", "UI", "RoofRafterWindow.xaml.cs");

    [Fact]
    public void ExistingGeneratedSet_EntersEditInsteadOfReplacementDeferred()
    {
        var run = Member(Workflow, "public static void Run(", "private static bool TryRecoverExistingRecipe(");
        Assert.Contains("var isEdit = selectedRoof.ExistingGeneratedRafterCount > 0", run);
        Assert.Contains("TryRecoverExistingRecipe(", run);
        Assert.Contains("TryReplaceRafters(", run);
        Assert.Contains("TryCreateRafters(", run);
        Assert.DoesNotContain("Command_RoofRafters_ReplacementDeferred", run);
        Assert.Contains("Command_RoofRafters_RecipeAmbiguous", run);
        Assert.Contains("new RoofRafterWindow(", run);
    }

    [Fact]
    public void RecoveredRecipe_ContainsSpacingWidthHeightMaterial()
    {
        Assert.Contains("TryRecoverRecipe(", Replacement);
        Assert.Contains("timber.WidthMm", Replacement);
        Assert.Contains("timber.HeightMm", Replacement);
        Assert.Contains("generated.Data.RequestedMaximumSpacingMm", Replacement);
        Assert.Contains("timber.Material", Replacement);
        Assert.Contains("RoofRafterGenerationRecipeRules.TryUnify(", Replacement);

        var recipe = new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24");
        Assert.True(RoofRafterGenerationRecipeRules.IsValid(recipe));
        Assert.Equal(900d, recipe.MaximumSpacingMm);
        Assert.Equal(80d, recipe.WidthMm);
        Assert.Equal(160d, recipe.HeightMm);
        Assert.Equal("Smrek C24", recipe.Material);
    }

    [Fact]
    public void DialogState_IsInitializedFromRecoveredRecipeInEdit()
    {
        var editBranch = Member(
            Workflow,
            "if (isEdit)",
            "else");
        Assert.Contains("TryRecoverExistingRecipe(", editBranch);
        Assert.Contains("new RoofRafterPreferences(", editBranch);
        Assert.Contains("recoveredRecipe.WidthMm", editBranch);
        Assert.Contains("recoveredRecipe.HeightMm", editBranch);
        Assert.Contains("recoveredRecipe.MaximumSpacingMm", editBranch);
        Assert.Contains("recoveredRecipe.Material", editBranch);
        Assert.DoesNotContain("TimberElementDefaults.For(", editBranch);
        Assert.DoesNotContain("AutomaticRafterPreferences", editBranch);
        Assert.DoesNotContain("DefaultAutomaticSpacingMm", editBranch);

        Assert.Contains("WidthTextBox.Text = FormatInput(preferences.WidthMm)", Window);
        Assert.Contains("HeightTextBox.Text = FormatInput(preferences.HeightMm)", Window);
        Assert.Contains("MaximumSpacingTextBox.Text = FormatInput(preferences.MaximumSpacingMm)", Window);
    }

    [Fact]
    public void CreateMode_StillUsesGlobalDefaultsAndRememberedPreferences()
    {
        var createBranch = Member(
            Workflow,
            "var canonicalRafterDefaults = TimberElementDefaults.For(",
            "var dialog = new RoofRafterWindow(");
        Assert.Contains("TimberElementDefaults.For(", createBranch);
        Assert.Contains("TimberElementType.Rafter", createBranch);
        Assert.Contains("AutomaticRafterPreferences", createBranch);
        Assert.Contains("DefaultAutomaticSpacingMm", createBranch);
        Assert.Contains("RoofRafterPreferences.CreateFirstUse(", createBranch);
    }

    [Fact]
    public void EditedRecipeReplacement_AcceptsValidatedRecipeNotRecoveredAuthority()
    {
        Assert.Contains("public static ReplacementOutcome TryReplaceWithEditedRecipe(", Replacement);
        Assert.Contains("RoofRafterGenerationRecipe editedRecipe", Replacement);
        Assert.Contains("RoofRafterGenerationRecipeRules.IsValid(editedRecipe)", Replacement);
        Assert.Contains("rebuildReason: \"rafter-edit\"", Replacement);

        var replace = Member(
            Workflow,
            "private static RoofRafterCreationResult TryReplaceRafters(",
            "private static RoofRafterCreationResult TryCreateRafters(");
        Assert.Contains("TryReplaceWithEditedRecipe(", replace);
        Assert.Contains("new RoofRafterGenerationRecipe(", replace);
        Assert.Contains("request.WidthMm", replace);
        Assert.Contains("request.HeightMm", replace);
        Assert.Contains("request.MaximumSpacingMm", replace);
        Assert.Contains("request.Material", replace);
        Assert.DoesNotContain("Command_RoofRafters_ReplacementDeferred", replace);
        Assert.Contains("Command_RoofRafters_RecipeAmbiguous", replace);
    }

    [Fact]
    public void CreateGuard_StillDefersUnsafeDuplicateCreation()
    {
        var create = Member(
            Workflow,
            "private static RoofRafterCreationResult TryCreateRafters(",
            "private static bool IsGeneratedSetStale(");
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", create);
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", create);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", create);
        Assert.DoesNotContain("TryReplaceWithEditedRecipe(", create);
    }

    [Fact]
    public void DimensionsMaterialAndSpacingEdits_ShareReservedIdReplayPath()
    {
        var edited = Member(
            Replacement,
            "public static ReplacementOutcome TryReplaceWithEditedRecipe(",
            "private static bool TryPrepareExistingOrdinarySet(");
        Assert.Contains("ReplacePreparedSetWithRecipe(", edited);

        var shared = Member(
            Replacement,
            "private static ReplacementOutcome ReplacePreparedSetWithRecipe(",
            "public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize(");
        Assert.Contains("CollectReservedElementIds(", shared);
        Assert.Contains("RoofGeneratedMemberReplayPlanner.Create(", shared);
        Assert.Contains("EraseGeneratedSet(", shared);
        Assert.Contains("MaterializeCore(", shared);
        Assert.Contains("reservedElementIds", shared);
    }

    [Fact]
    public void AmbiguousRecipe_DoesNotMutateDrawing()
    {
        var run = Member(Workflow, "public static void Run(", "private static bool TryRecoverExistingRecipe(");
        Assert.True(
            run.IndexOf("Command_RoofRafters_RecipeAmbiguous", StringComparison.Ordinal) <
            run.IndexOf("new RoofRafterWindow(", StringComparison.Ordinal));

        var edited = Member(
            Replacement,
            "public static ReplacementOutcome TryReplaceWithEditedRecipe(",
            "private static bool TryPrepareExistingOrdinarySet(");
        Assert.True(
            edited.IndexOf("TryRecoverRecipe(", StringComparison.Ordinal) <
            edited.IndexOf("ReplacePreparedSetWithRecipe(", StringComparison.Ordinal));
        Assert.Contains("SkippedAmbiguousRecipe", edited);

        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
                new RoofRafterGenerationRecipe(100d, 160d, 900d, "Smrek C24"),
            ],
            out _));
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
                new RoofRafterGenerationRecipe(80d, 200d, 900d, "Smrek C24"),
            ],
            out _));
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "KVH C24 NSi"),
            ],
            out _));
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
                new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24"),
            ],
            out _));
    }

    [Fact]
    public void HipOrdering_OrdinaryThenStructuralThenCommit()
    {
        var replace = Member(
            Workflow,
            "private static RoofRafterCreationResult TryReplaceRafters(",
            "private static RoofRafterCreationResult TryCreateRafters(");
        AssertOrdered(
            replace,
            "TryReplaceWithEditedRecipe(",
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            "transaction.Commit();");
        Assert.Equal(1, Count(replace, "transaction.Commit();"));
    }

    [Fact]
    public void ReplacementPath_DoesNotTouchPurlinsWallPlatesOrManualStores()
    {
        var replace = Member(
            Workflow,
            "private static RoofRafterCreationResult TryReplaceRafters(",
            "private static RoofRafterCreationResult TryCreateRafters(");
        Assert.DoesNotContain("RoofAutomaticPurlinGeneratedStore", replace);
        Assert.DoesNotContain("TimberElementType.WallPlate", replace);
        Assert.DoesNotContain("TimberElementType.Purlin", replace);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Clear", replace);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", replace);

        var shared = Member(
            Replacement,
            "private static ReplacementOutcome ReplacePreparedSetWithRecipe(",
            "public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize(");
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", Replacement);
        Assert.DoesNotContain("RoofAutomaticPurlinGeneratedStore", shared);
        Assert.DoesNotContain("RoofStructuralGeneratedStore.Clear", shared);
        Assert.DoesNotContain("RoofAttachedManualTimberStore", shared);
    }

    [Fact]
    public void SharedReplacement_StillSyncsCanonicalGroup()
    {
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Replacement);
        Assert.Contains("DetachMembersBeforeErase", Replacement);
        Assert.Contains("RoofGeneratedMemberKey", Replacement);
    }

    [Fact]
    public void UnchangedEditApply_StillForcesEditedRecipeReplacement()
    {
        // Idempotent identity: reserved ElementIds + replay; work still goes through
        // the forced edited-recipe replacement path (no freshness short-circuit).
        var edited = Member(
            Replacement,
            "public static ReplacementOutcome TryReplaceWithEditedRecipe(",
            "private static bool TryPrepareExistingOrdinarySet(");
        Assert.DoesNotContain("IsGeneratedSetStale", edited);
        Assert.DoesNotContain("forceRegenerateOnSourceResize", edited);
        Assert.Contains("ReplacePreparedSetWithRecipe(", edited);
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing marker: {marker}");
            Assert.True(index > previous, $"Out of order: {marker}");
            previous = index;
        }
    }

    private static string Read(params string[] parts) =>
        RoofUxSourceContractText.Read(parts);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }

        return count;
    }
}
