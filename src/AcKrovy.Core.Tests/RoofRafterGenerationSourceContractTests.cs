using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterGenerationSourceContractTests
{
    private static readonly string Workflow = Read("RoofRafterCommandWorkflow.cs");
    private static readonly string Creation = Read("TimberSourceLineCreationService.cs");
    private static readonly string Store = Read("RoofGeneratedTimberStore.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string Commands = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
    private static readonly string CommandCatalog = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Localization", "CommandUiCatalog.cs");

    [Fact]
    public void ExplicitCommandUsesOwnerResolverAndAcceptsDisplayChildren()
    {
        Assert.Contains("public const string RoofRafters = \"AK_ROOF_RAFTERS\"", CommandCatalog);
        Assert.Contains("AcKrovyCommandNames.RoofRafters", Commands);
        Assert.Contains("RoofRafterCommandWorkflow.Run", Commands);
        Assert.Contains("RoofOwnerSelectionResolver.Resolve(", Workflow);
        Assert.DoesNotContain("selected is Polyline", Workflow);
    }

    [Fact]
    public void SemanticRestoreAndStaleValidationAreReadOnlyBeforeDialog()
    {
        var selection = Segment(
            Workflow,
            "private static bool TrySelectCurrentRoof",
            "private static RoofRafterCreationResult TryCreateRafters");
        Assert.Contains("RoofDefinitionStore.Read(owner)", selection);
        Assert.Contains("RoofDefinitionPersistence.Restore(", selection);
        Assert.Contains("RoofDefinitionRestoreError.StaleFootprint", selection);
        Assert.Contains("OpenMode.ForRead", selection);
        Assert.DoesNotContain("OpenMode.ForWrite", selection);
        Assert.DoesNotContain("transaction.Commit", selection);
        Assert.True(
            Workflow.IndexOf("new RoofRafterWindow(", StringComparison.Ordinal) <
            Workflow.IndexOf("TryCreateRafters(", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstUseValuesComeFromCanonicalRafterDefaultsAndPreferences()
    {
        Assert.Contains("TimberElementDefaults.For(", Workflow);
        Assert.Contains("TimberElementType.Rafter", Workflow);
        Assert.Contains("RoofRafterPreferences.CreateFirstUse(canonicalRafterDefaults.Material)", Workflow);
        Assert.Contains("uiPreferences.AutomaticRafterPreferences", Workflow);
        Assert.Contains("defaultProfile", Workflow);
        Assert.Equal(80d, TimberElementDefaults.For(TimberElementType.Rafter).WidthMm);
        Assert.Equal(160d, TimberElementDefaults.For(TimberElementType.Rafter).HeightMm);
        Assert.Equal("Smrek C24", TimberElementDefaults.For(TimberElementType.Rafter).Material);
    }

    [Fact]
    public void CancelCannotEnterAnyWriteScopeOrRegisterRegApp()
    {
        var dialogResult = Workflow.IndexOf("AcApp.ShowModalWindow(dialog)", StringComparison.Ordinal);
        var create = Workflow.IndexOf("TryCreateRafters(", dialogResult, StringComparison.Ordinal);
        Assert.True(dialogResult >= 0 && create > dialogResult);
        Assert.DoesNotContain("EnsureRegAppRegistered", Workflow.Substring(0, create));
        Assert.DoesNotContain("OpenMode.ForWrite", Workflow.Substring(0, create));
        Assert.DoesNotContain("AppendEntity", Workflow.Substring(0, create));
    }

    [Fact]
    public void CanonicalSourceOnlyCreationReusesProductionMetadataLayerAndIdentity()
    {
        Assert.Contains("new Line(request.Start, request.End)", Creation);
        Assert.Contains("AutoCadTimberElementMetadataStore", Creation);
        Assert.Contains("metadataStore.Write(line, request.Data)", Creation);
        Assert.Contains("AutoCadTimberLayerService", Creation);
        Assert.Contains("ApplyLayerForTimberType", Creation);
        Assert.Contains("TimberElementItemIdentityService.SynchronizeElementIds", Creation);
        Assert.DoesNotContain("TimberAnnotationService", Creation);
        Assert.DoesNotContain("ElementLabelService", Creation);
        Assert.DoesNotContain("SlopeAnnotationService", Creation);
    }

    [Fact]
    public void EveryGeneratedSourceGetsBothIndependentMetadataContracts()
    {
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", Workflow);
        Assert.Contains("TimberSourceLineCreationService.Create(", Replacement);
        Assert.Contains("RoofGeneratedTimberStore.Write(", Replacement);
        Assert.Contains("RoofGeneratedTimberDataSchema.CurrentVersion", Replacement);
        Assert.Contains("RoofGeneratedTimberKind.Rafter", Replacement);
        Assert.Contains("rafter.Face", Replacement);
        Assert.Contains("rafter.StationIndex", Replacement);
        Assert.Contains("rafter.StationCount", Replacement);
        Assert.Contains("layout.RequestedMaximumSpacingMm", Replacement);
        Assert.Contains("layout.Signature", Replacement);
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_TIMBER", Store);
        Assert.Contains("ReadForeignXData", Store);
    }

    [Fact]
    public void ExistingSetDiscoveryIsReadOnlyAndReplacementIsSafelyDeferred()
    {
        var discovery = Segment(Store, "public static IReadOnlyList<ObjectId> FindByOwner", "private static List<TypedValue> ReadForeignXData");
        Assert.Contains("BlockTableRecord.ModelSpace", discovery);
        Assert.Contains("OpenMode.ForRead", discovery);
        Assert.DoesNotContain("OpenMode.ForWrite", discovery);
        Assert.DoesNotContain(".Erase(", discovery);
        Assert.Contains("Command_RoofRafters_ReplacementDeferred", Workflow);
        Assert.Contains("Command_RoofRafters_ExistingStale", Workflow);
        Assert.Contains("RoofGeneratedRafterSetService.IsGeneratedSetStale(", Workflow);
        Assert.Contains("RoofGeneratedTimberFreshness.IsLayoutCurrent(", Replacement);
        Assert.DoesNotContain(".Erase(", Workflow);
    }

    [Fact]
    public void DialogIsTheOnlyConfirmationAndCreationUsesOneCommit()
    {
        Assert.Contains("AcApp.ShowModalWindow(dialog)", Workflow);
        Assert.DoesNotContain("ConfirmYesNo", Workflow);
        Assert.DoesNotContain("ShowRafters", Workflow);
        Assert.Equal(1, Count(Workflow, "transaction.Commit();"));
    }

    [Fact]
    public void GeneratedSourceKeepsEaveToRidgeGeometryAndUsesCanonicalDownhillMetadata()
    {
        Assert.Contains("new Point3d(rafter.PlanStart.X", Replacement);
        Assert.Contains("new Point3d(rafter.PlanEnd.X", Replacement);
        Assert.Contains("IsSlopeDirectionReversed = true", Replacement);
        Assert.DoesNotContain("SlopeArrowService", Workflow + Replacement);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", Workflow + Replacement);
    }

    [Fact]
    public void Stage6DoesNotAlterRoofGroupOrIntroduceOtherTimberOrReactiveEntities()
    {
        var production = Workflow + Creation + Store + Replacement;
        Assert.DoesNotContain("RoofDisplayGroupService", production);
        Assert.DoesNotContain("BlockReference", production);
        Assert.DoesNotContain("Polyline3d", production);
        Assert.DoesNotContain("Solid3d", production);
        Assert.DoesNotContain("ObjectModified", production);
        Assert.DoesNotContain("CommandEnded", production);
        Assert.DoesNotContain("TimberElementType.WallPlate", production);
        Assert.DoesNotContain("TimberElementType.Purlin", production);
        Assert.DoesNotContain("TimberElementType.Post", production);
        Assert.DoesNotContain("TimberElementType.CollarTie", production);
        Assert.DoesNotContain("TimberElementType.Brace", production);
    }

    [Fact]
    public void StableSchemasRemainIndependent()
    {
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(2, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(1, TimberDrawingSettings.DrawingSettingsSchemaVersion);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
        {
            count++;
        }
        return count;
    }
}
