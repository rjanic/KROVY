using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofUnlockIndicatorSourceContractTests
{
    private static readonly string Indicator = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnlockIndicatorService.cs");
    private static readonly string Store = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnlockIndicatorStore.cs");
    private static readonly string Resolver = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofEditStateCommandWorkflow.cs");
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");

    [Fact]
    public void Indicator_IsSingleBlockReference_NotModelSpacePolylines()
    {
        Assert.Contains("internal const string BlockName = \"KROV_ROOF_UNLOCK_ICON\"", Indicator);
        Assert.Contains("new BlockReference(insertion, blockId)", Indicator);
        Assert.Contains("EnsureBlockDefinition", Indicator);
        Assert.Contains("blockTable.Has(BlockName)", Indicator);
        Assert.DoesNotContain("modelSpace.AppendEntity(polyline)", Indicator);
        Assert.DoesNotContain("modelSpace.AppendEntity(body)", Indicator);
        Assert.Contains("definition.AppendEntity(polyline)", Indicator);
    }

    [Fact]
    public void BlockDefinition_IsReused_AndOriginIsIconCenter()
    {
        Assert.Contains("Origin = Point3d.Origin", Indicator);
        Assert.Contains("internal const double IconCenterX = 0.50d", Indicator);
        Assert.Contains("internal const double IconCenterY = 0.485d", Indicator);
        Assert.Contains("units[i].X - IconCenterX", Indicator);
        Assert.Contains("units[i].Y - IconCenterY", Indicator);
        Assert.Contains("origin.X + (IconCenterX * size)", Indicator);
        Assert.Contains("origin.Y + (IconCenterY * size)", Indicator);
        Assert.Contains("ScaleFactors = new Scale3d(size)", Indicator);
    }

    [Fact]
    public void LayerPlotTransparency_AndNotGroupOrTimber()
    {
        Assert.Contains("internal const string LayerName = \"KROV_ROOF_UI\"", Indicator);
        Assert.Contains("isPlottable: false", Indicator);
        Assert.Contains("new Transparency(70)", Indicator);
        Assert.DoesNotContain("EnsureGroup", Indicator);
        Assert.Contains("ExpectedMemberCount = 8", Group);
        Assert.DoesNotContain("ElementDataStore.Write", Indicator);
        Assert.DoesNotContain("TimberElementData", Indicator);
        Assert.DoesNotContain("RoofGeneratedTimberStore", Indicator + Store);
        Assert.Contains("Not timber, display, or report metadata", Store);
    }

    [Fact]
    public void Sync_ErasesLegacyOwnerEntities_ThenCreatesAtMostOneReference()
    {
        var sync = RoofUxSourceContractText.Member(
            Indicator,
            "public static void Sync(",
            "public static bool RebuildUnlockedOwners");
        Assert.True(
            sync.IndexOf("EraseExisting(database, transaction, ownerReference)", StringComparison.Ordinal) <
            sync.IndexOf("CreateSymbol", StringComparison.Ordinal));
        Assert.Contains("EditState != RoofEditState.Unlocked", sync);
        Assert.Contains("CreateSymbol(database, transaction, origin, size, ownerReference)", sync);
        Assert.Contains("RoofUnlockIndicatorStore.TryReadOwnerReference(entity)", Indicator);
    }

    [Fact]
    public void LockUnlockCopyResize_KeepDerivedUiLifecycle()
    {
        Assert.Contains("RoofUnlockIndicatorService.Sync", Workflow);
        Assert.Contains("RoofUnlockIndicatorService.Sync", Resize);
        Assert.Contains("RebuildUnlockedOwners", Live + Indicator);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Live);
        Assert.DoesNotContain("CommandEnded +=", Indicator);
        Assert.DoesNotContain("ObjectModified +=", Indicator);
        Assert.DoesNotContain("ObjectOverrule", Indicator);
        Assert.DoesNotContain("SendStringToExecute", Indicator);
    }

    [Fact]
    public void OwnerResolution_UsesStoreLink_NotBlockName()
    {
        Assert.Contains("TryResolveUnlockIndicatorOwner", Resolver);
        Assert.Contains("RoofUnlockIndicatorStore.TryReadOwnerReference(selected)", Resolver);
        Assert.DoesNotContain("KROV_ROOF_UNLOCK_ICON", Resolver);
        Assert.DoesNotContain("BlockName", Resolver);
        Assert.Contains("selectedThroughDisplayChild: true", Resolver);
    }

    [Fact]
    public void UnlockedReject_EmitsDebugReasonOnEveryUnsupportedRecovery()
    {
        Assert.Contains("ROOF_MANUAL_EDIT_REJECT", Diag);
        Assert.Contains("WriteUnlockedReject", Manual);
        var process = RoofUxSourceContractText.Member(
            Manual,
            "private static OwnerEditOutcome ProcessOwner(",
            "private static bool TryAcceptUnlockedEdits");
        Assert.Equal(3, CountOccurrences(process, "OwnerEditOutcome.UnsupportedRecovered"));
        Assert.Equal(3, CountOccurrences(process, "WriteUnlockedReject"));
        Assert.Contains("TryClassifyCollinearEndpointEdit", Manual);
        Assert.Contains("ComposeEndpointOffsets", Manual);
        Assert.Contains("IsEndpointTrimOrExtendCommand", Manual);
        Assert.True(
            process.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            process.IndexOf("var accept = TryAcceptUnlockedEdits", StringComparison.Ordinal));
    }

    [Fact]
    public void SchemaAndPackageRemainUnchangedForIcon()
    {
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(RoofEditState.Locked, default(RoofEditState));
        Assert.DoesNotContain("SchemaVersion", Store);
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
