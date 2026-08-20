using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofHybridEditSourceContractTests
{
    private static readonly string Commands = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
    private static readonly string Catalog = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Localization", "CommandUiCatalog.cs");
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofEditStateCommandWorkflow.cs");
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Indicator = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnlockIndicatorService.cs");
    private static readonly string Replacement = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Group = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    [Fact]
    public void Commands_AreRegisteredWithExistingBoundary()
    {
        Assert.Contains("public const string RoofUnlock = \"AK_ROOF_UNLOCK\"", Catalog);
        Assert.Contains("public const string RoofLock = \"AK_ROOF_LOCK\"", Catalog);
        Assert.Contains("public const string RoofResetEdits = \"AK_ROOF_RESET_EDITS\"", Catalog);
        Assert.Contains("RoofEditStateCommandWorkflow.Unlock", Commands);
        Assert.Contains("RoofEditStateCommandWorkflow.Lock", Commands);
        Assert.Contains("RoofEditStateCommandWorkflow.ResetEdits", Commands);
        Assert.Contains("CommandExecutionBoundary.Execute", Commands);
    }

    [Fact]
    public void UnlockDoesNotRegenerateGeometry()
    {
        var setState = RoofUxSourceContractText.Member(
            Workflow,
            "private static void SetEditState",
            "private static bool TrySelectOwner");
        Assert.Contains("RoofGeneratedMemberOverrideRules.WithEditState", setState);
        Assert.Contains("RoofUnlockIndicatorService.Sync", setState);
        Assert.DoesNotContain("TryReplaceForSupportedResize", setState);
        Assert.DoesNotContain("Materialize(", setState);
    }

    [Fact]
    public void SupportedEditCommands_IncludeClassicStretchWhenUnlocked()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("MOVE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("ROTATE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("ERASE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("GRIP_STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsClassicStretch("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("BREAK"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("SCALE"));
        Assert.Contains("IsClassicStretch(globalCommandName)", Manual);
    }

    [Fact]
    public void UndoRedo_BypassesWrites_AndJoinsNativeUndoGroup()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Live);
        Assert.Contains("IsGeneratedTimberEditCommand(globalCommandName)", CommandRules);
        Assert.Contains("RequiresGroupedUndoMark(e.GlobalCommandName)", Live);
        Assert.DoesNotContain("SendStringToExecute", Manual + Workflow + Indicator + Resize);
        Assert.DoesNotContain("new Timer", Manual + Workflow + Indicator);
        Assert.DoesNotContain("ObjectOverrule", Manual + Workflow + Indicator);
        Assert.DoesNotContain("DatabaseReactor", Manual + Workflow + Indicator);
    }

    [Fact]
    public void LockedPath_ReusesGeneratedOnlyRecovery()
    {
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.Contains("TryUnEraseAndRestore", Manual);
        Assert.Contains("Command_Roof_LockedNotificationTitle", Manual);
        Assert.Contains("Command_Roof_UnsupportedMemberEditNotificationTitle", Manual);
        Assert.DoesNotContain("SendStringToExecute(\"U\")", Manual);
    }

    [Fact]
    public void Acceptance_KeepsLiveLine_AndWritesOnlyEditState()
    {
        Assert.Contains("OpenMode.ForWrite", Manual);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, updated)", Manual);
        Assert.DoesNotContain("entity.Erase();", Manual);
        Assert.DoesNotContain("TryReplaceForSupportedResize", Manual);
        Assert.Contains("TimberAnnotationService.EnsureForElement", Manual);
        Assert.Contains("TryRefreshAcceptedMemberAnnotations", Manual);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", Manual);
        Assert.Contains("PreserveEditState", Resize);
        Assert.Contains("TryApplyToLayout", Replacement);
    }

    [Fact]
    public void UnlockedTrim_ClassifiesAgainstAcceptedBaseline_AndComposesOffsets()
    {
        Assert.Contains("IsEndpointTrimOrExtendCommand", Manual);
        Assert.Contains("TryClassifyCollinearEndpointEdit", Manual);
        Assert.Contains("ComposeEndpointOffsets", Manual);
        Assert.Contains("ApplyAcceptedLineGeometry", Manual);
        Assert.Contains("ROOF_MANUAL_EDIT_REJECT", Diag);
        Assert.Contains("ROOF_MANUAL_EDIT_ANNOTATION_FAIL", Diag);
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        Assert.Contains("TryClassifyCollinearEndpointEdit", accept);
        Assert.Contains("TryRefreshAcceptedMemberAnnotations", accept);
        Assert.Contains("TryRecalculateAcceptedMembers", accept);
        Assert.Contains("IsEndpointTrimOrExtendCommand", accept);
        Assert.Contains("RoofEditState.Unlocked", accept);
        Assert.DoesNotContain("TryReplaceForSupportedResize", accept);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", accept);
        Assert.DoesNotContain("RecalculateAll()", accept);
        Assert.DoesNotContain("SendStringToExecute", accept);
        Assert.True(
            accept.IndexOf("ApplyAcceptedLineGeometry", StringComparison.Ordinal) <
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal));
        Assert.True(
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal) <
            accept.LastIndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal));
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.Contains("WriteUnlockedReject", Manual);
        Assert.True(
            Manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) >= 0 &&
            Manual.IndexOf("TryClassifyCollinearEndpointEdit", StringComparison.Ordinal) >
            Manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal));
        Assert.True(
            Manual.IndexOf("WriteUnlockedReject", StringComparison.Ordinal) <
            Manual.LastIndexOf("OwnerEditOutcome.UnsupportedRecovered", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlockIndicator_UsesNonPlotUiLayer_AndIsNotGroupMember()
    {
        Assert.Contains("internal const string LayerName = \"KROV_ROOF_UI\"", Indicator);
        Assert.Contains("internal const string BlockName = \"KROV_ROOF_UNLOCK_ICON\"", Indicator);
        Assert.Contains("new BlockReference(insertion, blockId)", Indicator);
        Assert.Contains("isPlottable: false", Indicator);
        Assert.Contains("new Transparency(70)", Indicator);
        Assert.DoesNotContain("EnsureGroup", Indicator);
        Assert.Contains("ExpectedMemberCount = 8", Group);
        Assert.Equal(RoofEditState.Locked, default(RoofEditState));
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Fact]
    public void CopyRebuildsUnlockIndicators_WithoutNewReactor()
    {
        Assert.Contains("RebuildUnlockedOwners", Live + Indicator);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Live);
        Assert.DoesNotContain("CommandEnded +=", Indicator);
        Assert.DoesNotContain("ObjectModified +=", Indicator);
    }
}
