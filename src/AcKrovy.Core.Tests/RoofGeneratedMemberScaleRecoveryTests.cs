using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberScaleRecoveryTests
{
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");

    [Theory]
    [InlineData("SCALE")]
    [InlineData("_SCALE")]
    [InlineData(".SCALE")]
    [InlineData("'_.SCALE")]
    public void ScaleSpellings_EnterSnapshotAndGeneratedMemberLifecycleButRemainUnsupported(
        string command)
    {
        Assert.Equal("SCALE", LiveGeometryCommandRules.NormalizeCommandName(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsScaleCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(LiveGeometryCommandRules.RequiresGroupedUndoMark(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand(command, RoofKind.SimpleGable));
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand(command, RoofKind.AsymmetricGable));
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand(command, RoofKind.Monopitch));
    }

    [Fact]
    public void ScaleRouting_CapturesBeforeMutationAndDispatchesGeneratedTamperToManualRecovery()
    {
        var willStart = RoofUxSourceContractText.Member(
            Live,
            "private void CommandWillStart",
            "private void CommandEnded");
        var process = RoofUxSourceContractText.Member(
            Resize,
            "public static IReadOnlyCollection<ObjectId> Process",
            "public static bool TryBeginGroupedUndo");

        Assert.Contains("IsAssemblySnapshotCommand(e.GlobalCommandName)", willStart);
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.CaptureForCommand", willStart);
        Assert.Contains("RequiresGroupedUndoMark(e.GlobalCommandName)", willStart);
        Assert.Contains("GeneratedMemberTamperOwnerIds", process);
        Assert.Contains("IsAssemblySnapshotCommand(globalCommandName)", process);
        Assert.Contains("RoofGeneratedMemberManualEditService.ProcessOwners", process);
    }

    [Fact]
    public void UnsupportedScale_RejectsThenRestoresWithoutChangingExistingOverrides()
    {
        var processOwner = RoofUxSourceContractText.Member(
            Manual,
            "private static OwnerEditOutcome ProcessOwner",
            "private static bool TryAcceptLockedRigidTranslation");
        var unsupportedStart = processOwner.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal);
        var acceptedStart = processOwner.IndexOf(
            "var accept = TryAcceptUnlockedEdits",
            unsupportedStart,
            StringComparison.Ordinal);
        var unsupported = processOwner[unsupportedStart..acceptedStart];

        Assert.Contains("TryRecoverGeneratedMembersOnly", unsupported);
        Assert.Contains("WriteUnlockedReject", unsupported);
        Assert.Contains("unsupported-scale", unsupported);
        Assert.DoesNotContain("TryAcceptUnlockedEdits", unsupported);
        Assert.DoesNotContain("RoofDefinitionStore.Write", unsupported + Recovery);
        Assert.DoesNotContain("ManualOverrides", unsupported + Recovery);
    }

    [Fact]
    public void GeneratedOnlyRecovery_RestoresLineAndAnnotationsOnExistingObjectIds()
    {
        var generatedOnly = RoofUxSourceContractText.Member(
            Recovery,
            "public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly",
            "public static bool TryNormalizeRigidTranslation");

        Assert.Contains("TryRestoreTimberLines", generatedOnly);
        Assert.Contains("TryRestoreAnnotations", generatedOnly);
        Assert.Contains("TryEraseUnsnapshotGeneratedDuplicates", generatedOnly);
        Assert.Contains("generated-only-ok", generatedOnly);
        Assert.DoesNotContain("RoofDefinitionStore.Write", generatedOnly);
        Assert.DoesNotContain("RoofAssemblyGroupSyncService", generatedOnly);
        Assert.DoesNotContain("Materialize", generatedOnly);
    }

    [Fact]
    public void SupportedAndExistingUnsupportedCommandsKeepTheirCurrentDisposition()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("MOVE"));
        Assert.True(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("GRIP_STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("BREAK", RoofKind.SimpleGable));
        Assert.True(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("BREAK", RoofKind.Monopitch));
    }

    [Fact]
    public void UndoRedoStillBypassesDatabaseWorkAndClearsRecoverySnapshot()
    {
        var ended = RoofUxSourceContractText.Member(
            Live,
            "private void CommandEnded",
            "private void CommandCancelled");

        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", ended);
        Assert.Contains("ClearPendingLiveGeometryState", ended);
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.Clear", ended);
        Assert.DoesNotContain("ProcessOwners", ended);
    }
}
