using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofUnsupportedStretchRecoverySourceContractTests
{
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Snapshot = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoverySnapshotService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofUnsupportedStretchRecoveryRules.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryDiag.cs");
    private static readonly string Models = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Models", "Roofs", "RoofUnsupportedStretchRecoveryModels.cs");

    [Fact]
    public void Snapshot_CapturedAtCommandWillStart_ForStretchAndGripStretch()
    {
        Assert.Contains("CaptureForCommand", Live + Snapshot);
        Assert.Contains("IsRecoveryCommand", Live + Rules);
        var willStart = RoofUxSourceContractText.Member(
            Live,
            "private void CommandWillStart",
            "private void CommandEnded");
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.CaptureForCommand", willStart);
        Assert.True(
            willStart.IndexOf("CaptureForCommand", StringComparison.Ordinal) <
            willStart.IndexOf("TryBeginGroupedUndo", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_ClearedOnEndedCancelFailDispose()
    {
        Assert.Contains("Clear(\"CommandEnded\", e.GlobalCommandName)", Live);
        Assert.Contains("Clear(\"CommandCancelled\", e.GlobalCommandName)", Live);
        Assert.Contains("Clear(\"CommandFailed\", e.GlobalCommandName)", Live);
        Assert.Contains("Clear(\"dispose\")", Live);
        Assert.Contains("Clear(\"capture-start\", globalCommandName)", Snapshot);
        Assert.Contains("Clear(\"non-recovery-command\", e.GlobalCommandName)", Live);
    }

    [Fact]
    public void UnsupportedPath_PrefersAutoRecovery_ThenFallbackUseU()
    {
        var unsupported = RoofUxSourceContractText.Member(
            Resize,
            "if (plan.UnsupportedOwnerIds.Count > 0)",
            "IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds");
        Assert.Contains("TryRecoverUnsupportedOwners", unsupported);
        Assert.Contains("Command_Roof_UnsupportedStretchRecoveredNotificationTitle", unsupported);
        Assert.Contains("Command_Roof_UnsupportedStretchNotificationTitle", unsupported);
        Assert.Contains("RecoveredAll", unsupported);
        Assert.DoesNotContain("Command_Roof_DisplayTamperNotificationTitle", unsupported);
    }

    [Fact]
    public void Recovery_RestoresSameObjectId_WithoutDefinitionWrite_WithoutRafterReplace()
    {
        Assert.Contains("RestorePolylineGeometry", Recovery);
        Assert.Contains("RoofDisplayService.Rebuild", Recovery);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", Recovery);
        Assert.DoesNotContain("TryReplaceForSupportedResize(", Recovery);
        Assert.DoesNotContain("SendStringToExecute", Recovery + Resize + Live + Snapshot);
        Assert.DoesNotContain("SendStringToExecute(\"U\")", Recovery + Resize + Live);
        Assert.DoesNotContain("new Timer", Recovery + Snapshot);
        Assert.DoesNotContain("DatabaseReactor", Recovery + Snapshot + Resize);
        Assert.DoesNotContain("ObjectOverrule", Recovery + Snapshot + Resize);
        Assert.DoesNotContain("BeginDeepClone", Recovery + Snapshot + Resize);
        Assert.DoesNotContain("EndDeepClone", Recovery + Snapshot + Resize);
    }

    [Fact]
    public void SupportedResizeAndRigidAndDisplayTamper_DoNotCallRecovery()
    {
        var apply = RoofUxSourceContractText.Member(
            Resize,
            "private static void ApplyResizes",
            "private static ResizeApplyResult TryApplyResize");
        var rigid = RoofUxSourceContractText.Member(
            Resize,
            "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms",
            "private static IReadOnlyCollection<ObjectId> TryAdoptGroupGripResizes");
        var display = RoofUxSourceContractText.Member(
            Resize,
            "private static bool ApplyDisplayTampers",
            "private static bool TryApplyDisplayTamper");
        Assert.DoesNotContain("TryRecoverUnsupportedOwners", apply + rigid + display);
        Assert.DoesNotContain("RoofUnsupportedStretchRecoveryService", apply + rigid + display);
    }

    [Fact]
    public void RecoveryBatch_IsAllOrNothing()
    {
        var batch = RoofUxSourceContractText.Member(
            Resize,
            "private static UnsupportedRecoveryBatchResult TryRecoverUnsupportedOwners",
            "private static void ApplyResizes");
        Assert.Contains("All-or-nothing", batch);
        Assert.Contains("transaction.Commit()", batch);
        Assert.Contains("HardFailure", batch);
        Assert.Contains("Unavailable", batch);
    }

    [Fact]
    public void Snapshot_CapturesOwnedTimberAndAnnotations_NotPredictedSubset()
    {
        Assert.Contains("FindByOwner", Snapshot);
        Assert.Contains("TimberLines", Snapshot + Recovery);
        Assert.Contains("Annotations", Snapshot + Recovery);
        Assert.Contains("ALL owned generated members", Snapshot);
        Assert.DoesNotContain("TryReplaceForSupportedResize(", Recovery);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", Recovery);
    }

    [Fact]
    public void Recovery_RestoresTimberAndAnnotations_InPlace_ThenRebuildsDisplay()
    {
        Assert.Contains("TryRestoreTimberLines", Recovery);
        Assert.Contains("TryRestoreAnnotations", Recovery);
        Assert.Contains("TryProbeAssemblyMembers", Recovery);
        Assert.Contains("line.StartPoint", Recovery);
        Assert.Contains("line.EndPoint", Recovery);
        Assert.Contains("RoofDisplayService.Rebuild", Recovery);
        var rebuildIndex = Recovery.IndexOf("RoofDisplayService.Rebuild", StringComparison.Ordinal);
        var timberIndex = Recovery.IndexOf("TryRestoreTimberLines", StringComparison.Ordinal);
        Assert.True(timberIndex >= 0 && rebuildIndex > timberIndex);
    }

    [Fact]
    public void Snapshot_UsesSourceVertices_NotGroupDisplayBaseline()
    {
        Assert.Contains("owned generated", Snapshot);
        Assert.Contains("timber Lines", Snapshot);
        Assert.DoesNotContain("TryGetPreCommandDisplayByRole", Snapshot + Recovery);
        Assert.DoesNotContain("RoofGroupGripPreCommandBaselineService", Snapshot + Recovery);
        Assert.Contains("RoofDisplayStore.Read(entity).Exists", Snapshot);
    }

    [Fact]
    public void Capture_SkippedOnUndoRedo_AndClearedBeforeEachCapture()
    {
        var willStart = RoofUxSourceContractText.Member(
            Live,
            "private void CommandWillStart",
            "private void CommandEnded");
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", willStart);
        Assert.Contains("!isUndoRedo &&", willStart);
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.CaptureForCommand", willStart);
        Assert.Contains("Clear(\"capture-start\", globalCommandName)", Snapshot);
        Assert.True(
            willStart.IndexOf("isUndoRedo", StringComparison.Ordinal) <
            willStart.IndexOf("CaptureForCommand", StringComparison.Ordinal));
    }

    [Fact]
    public void Recovery_PreservesElementIdAndHandle_OnTimberProbeAndRestore()
    {
        Assert.Contains("data.ElementId, timber.ElementId", Recovery);
        Assert.Contains("timber.EntityHandle", Recovery);
        Assert.Contains("annotation.EntityHandle", Recovery);
        Assert.Contains("FindByOwner", Snapshot);
        Assert.Contains("Erase(false)", Recovery);
        Assert.DoesNotContain("entity.Erase();", Recovery);
        Assert.DoesNotContain("TryReplaceForSupportedResize(", Recovery);
    }

    [Fact]
    public void Batch_ProbeBeforeWrite_AbortWithoutCommitOnFailure()
    {
        var batch = RoofUxSourceContractText.Member(
            Resize,
            "private static UnsupportedRecoveryBatchResult TryRecoverUnsupportedOwners",
            "private static void ApplyResizes");
        Assert.Contains("StartTransaction()", batch);
        Assert.Contains("CanAttemptAssemblyRecovery", batch);
        Assert.Contains("transaction.Commit()", batch);
        Assert.Contains("// Abort:", batch);
        var commitIndex = batch.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        var abortIndex = batch.IndexOf("// Abort:", StringComparison.Ordinal);
        Assert.True(abortIndex >= 0 && commitIndex > abortIndex);
    }

    [Fact]
    public void Snapshot_ClearTracksNonRecoveryCommand_AndNestedLifecycleRisk()
    {
        Assert.Contains("Clear(\"non-recovery-command\"", Live + Snapshot);
        Assert.Contains("Clear(\"CommandEnded\"", Live);
        Assert.Contains("LastClearReason", Snapshot);
        Assert.Contains("LastClearCommand", Snapshot);
        Assert.Contains("GetCaptureSkips", Snapshot);
        Assert.Contains("unsupported-annotation-entity-type", Snapshot);
        // Capture runs on WillStart; Clear(CommandEnded) is in finally AFTER RefreshCandidates.
        var ended = RoofUxSourceContractText.Member(
            Live,
            "private void CommandEnded",
            "private void CommandCancelled");
        Assert.Contains("RefreshCandidates(", ended);
        Assert.Contains("Clear(\"CommandEnded\"", ended);
        Assert.True(
            ended.IndexOf("RefreshCandidates(", StringComparison.Ordinal) <
            ended.IndexOf("Clear(\"CommandEnded\"", StringComparison.Ordinal));
    }

    [Fact]
    public void RecoveryDiag_IsDebugOnly_AndMapsFallbackReasons()
    {
        var resize = Resize;
        Assert.Contains("TryProbeUnsupportedOwner", resize);
        Assert.Contains("no-command-snapshot", resize);
        Assert.Contains("owner-snapshot-missing", resize);
        Assert.Contains("snapshot-cleared-too-early", resize);
        Assert.Contains("multi-owner-all-or-nothing-rejection", resize);
        Assert.Contains("ambiguous-owner-match", resize);
        Assert.Contains("probe-validation-failure", resize);
        Assert.Contains("ROOF_UNSUPPORTED_STRETCH_RECOVERY_FALLBACK", Diag);
        Assert.Contains("ROOF_UNSUPPORTED_STRETCH_RECOVERY_PROBE", Diag);
        Assert.Contains("#if DEBUG", Diag);
        Assert.Contains("generated-timber-elementid-mismatch", Recovery);
        Assert.Contains("annotation-sourcehandle-mismatch", Recovery);
        Assert.Contains("post-restore-rigid-equivalent-failure", Recovery);
        Assert.Contains("roof-display-rebuild-failure", Recovery);
        Assert.Contains("restore-write-failure", Recovery);
    }

    [Fact]
    public void Recovery_MLeaderRestore_UsesLeaderLineIndexes_AndSafeDoglegOrder()
    {
        Assert.Contains("TryPrepareLiveMLeaderTopology", Recovery);
        Assert.Contains("RemoveLeader(", Recovery);
        Assert.Contains("RemoveLeaderLine(", Recovery);
        Assert.Contains("GetLeaderIndexes()", Recovery + Snapshot);
        Assert.Contains("GetLeaderLineIndexes(", Recovery + Snapshot);
        Assert.Contains("SetDogleg(leaderIndex", Recovery);
        Assert.Contains("SetLastVertex(lineIndex", Recovery);
        Assert.Contains("SetFirstVertex(lineIndex", Recovery);
        Assert.Contains("TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg", Recovery);
        Assert.Contains("IsRecoverableMLeaderTopology", Recovery + Rules);
        Assert.Contains("IsIndexOnlyTopologyDrift", Rules);
        Assert.DoesNotContain("SetFirstVertex(0,", Recovery);
        Assert.DoesNotContain("SetLastVertex(0,", Recovery);
        Assert.DoesNotContain("SetDogleg(0,", Recovery);
        Assert.DoesNotContain("Erase();", Recovery);
        Assert.Contains("Erase(false)", Recovery);
        Assert.Contains("MLEADER_WRITE_FAIL", Diag);
        Assert.Contains("MLEADER_TOPOLOGY", Diag);
        Assert.Contains("RebuildLeaderLine", Recovery);
        Assert.Contains("MLeaderLeaderIndex", Snapshot + Models);
        Assert.Contains("MLeaderEnableDogleg", Snapshot + Models);
    }

    [Fact]
    public void GeneratedOnlyStretch_RestoresMembers_WithoutUnsupportedSourcePath()
    {
        Assert.Contains("GeneratedMemberTamperOwnerIds", Resize);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Recovery + Resize);
        Assert.Contains("TryRecoverGeneratedMemberOwners", Resize);
        Assert.Contains("source-not-rigid-equivalent", Recovery);
        Assert.Contains("RigidEquivalent", Resize);
        Assert.DoesNotContain(
            "Command_Roof_UnsupportedStretchRecoveredNotificationTitle",
            RoofUxSourceContractText.Member(
                Resize,
                "TryRecoverGeneratedMemberOwners",
                "TryProbeUnsupportedOwner"));
        // Generated-only must not write RoofDefinition / SupportedResize replace.
        Assert.DoesNotContain("RoofDefinitionStore.Write(", Recovery);
        Assert.DoesNotContain("TryReplaceForSupportedResize(", Recovery);
    }

    [Fact]
    public void Recovery_DBTextAndPolyline_RemainDirectPropertyRestore()
    {
        Assert.Contains("dbText.Position", Recovery);
        Assert.Contains("dbText.AlignmentPoint", Recovery);
        Assert.Contains("TryRestorePolyline", Recovery);
        Assert.Contains("polyline.SetPointAt", Recovery);
    }

    [Fact]
    public void Localization_HasRecoveredAndFallbackKeys()
    {
        var path = Path.Combine(
            FindRepo(),
            "src", "AcKrovy.Localization", "Resources", "UiStrings.resx");
        var doc = System.Xml.Linq.XDocument.Load(path);
        string Value(string key) =>
            doc.Root!.Elements("data")
                .First(e => (string?)e.Attribute("name") == key)
                .Element("value")!.Value;
        Assert.Equal(
            "Nepodporovaná zmena strechy bola vrátená.",
            Value("Command_Roof_UnsupportedStretchRecoveredNotificationTitle"));
        Assert.Equal(
            "Obrys strechy musí zostať obdĺžnikový.",
            Value("Command_Roof_UnsupportedStretchRecoveredNotificationBody"));
        Assert.Equal(
            "Vráťte poslednú zmenu príkazom Späť (U).",
            Value("Command_Roof_UnsupportedStretchNotificationBody"));
    }

    private static string FindRepo()
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
