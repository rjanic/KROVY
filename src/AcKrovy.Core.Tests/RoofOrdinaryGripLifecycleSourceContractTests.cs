using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Routing guards only; native event sequence, BRep and rollback need HOST evidence.</summary>
public sealed class RoofOrdinaryGripLifecycleSourceContractTests
{
    [Fact]
    public void CommandBoundaries_DiscardLateNotificationsAndDisposeOrdinarySession()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var begin = live[live.IndexOf("private void CommandWillStart", StringComparison.Ordinal)..
            live.IndexOf("private void CommandEnded", StringComparison.Ordinal)];
        Assert.True(begin.IndexOf("ClearPendingLiveGeometryState()", StringComparison.Ordinal) <
            begin.IndexOf("RoofOrdinaryGripLifecycleService.Capture", StringComparison.Ordinal));
        Assert.Contains("BeginCommand(_document, e.GlobalCommandName)", begin);
        var end = live[live.IndexOf("private void CommandEnded", StringComparison.Ordinal)..
            live.IndexOf("private void CommandCancelled", StringComparison.Ordinal)];
        var cleanup = end[end.IndexOf("finally", StringComparison.Ordinal)..];
        Assert.Contains("ClearPendingLiveGeometryState()", cleanup);
        Assert.Contains("_ordinaryGripSnapshot?.Dispose()", cleanup);
        Assert.Contains("TraceCommandState(\"end\")", cleanup);
        Assert.Contains("TraceCommandState(\"cancel\")", live);
        Assert.Contains("TraceCommandState(\"fail\")", live);
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds", service);
        Assert.Contains("ROOF_ORDINARY_STRETCH_COMMAND_STATE", service);
        Assert.Contains("TraceCommandState(\"disposed\")", service);
        foreach (var field in new[] { "commandId=", "candidateCount=", "processedCount=", "pendingRollbackCount=", "pendingRefreshCount=" })
            Assert.Contains(field, service);
    }

    [Fact]
    public void GripFirstClaim_PrecedesLegacyResizeAndFiltersWholePackage()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var first = live.IndexOf("RoofOrdinaryGripLifecycleService.Process(", StringComparison.Ordinal);
        var legacy = live.IndexOf("RoofLiveResizeService.Process(", first, StringComparison.Ordinal);
        Assert.True(first >= 0 && legacy > first);
        Assert.Contains("_ordinaryGripSnapshot = RoofOrdinaryGripLifecycleService.Capture(_document)", live);
        Assert.Contains("IsOrdinaryPlanGeometryEditCommand", live);
        Assert.Contains("!gripClaimedIds.Contains(id)", live[first..legacy]);
        Assert.Contains("_ordinaryGripSnapshot?.Dispose()", live);
    }

    [Fact]
    public void GripYes_UsesFullBuilderAndDetach_WithoutLegacyGeometryOverrides()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("RoofOrdinaryPhysicalBuildStateRules.TryBuild", service);
        Assert.Contains("MaterializeOrdinaryMember(physical)", service);
        Assert.Contains("RoofIndependentOrdinaryDetachService.TryDetach", service);
        Assert.Contains("timber?.ElementId != member.ElementId", service);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist", service);
        Assert.Contains("RoofOrdinaryPhysicalBuildStateStore.Write", Read("RoofIndependentOrdinaryPhysicalStateService.cs"));
        Assert.DoesNotContain("TryAcceptUnlockedEdits", service);
        Assert.DoesNotContain("TryRecoverGeneratedMembersOnly", service);
        Assert.Contains("SynchronizeElementIdsDetailed", service);
        Assert.DoesNotContain("RenumberElementIdsByCuttingLength", service);
        Assert.DoesNotContain("replay.Upsert", service);
        Assert.DoesNotContain("HasGeometryOverride", service);
    }

    [Fact]
    public void AcceptedDesignationRecalc_IsAfterRebuildAndDetach_InTheAcceptanceTransaction()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var accepted = service.IndexOf("Accept(document, transaction, item.Member, item.Plan)", StringComparison.Ordinal);
        var recalc = service.IndexOf("designations = RecalculateDesignations", accepted, StringComparison.Ordinal);
        var verify = service.IndexOf("Verify(document, transaction, snapshot, affected, accepted, designations)", recalc, StringComparison.Ordinal);
        var commit = service.IndexOf("transaction.Commit()", verify, StringComparison.Ordinal);
        Assert.True(accepted >= 0 && recalc > accepted && verify > recalc && commit > verify);
        var noStart = service.IndexOf("if (!accepted)", StringComparison.Ordinal);
        var noEnd = service.IndexOf("else", noStart, StringComparison.Ordinal);
        Assert.DoesNotContain("RecalculateDesignations", service[noStart..noEnd]);
        Assert.Contains("assigned.ElementId : item.Member.ElementId", service);
        Assert.Contains("identity.IndependentMemberId !=", service);
        Assert.Contains("Read(item.Member.PlanCopy)?.IndependentMemberId", service);
        Assert.Contains("sync.PreviousElementIdById[id]", service);
        Assert.Contains("label.ElementId != data.ElementId", service);
        foreach (var legacy in new[] { "AlongMm", "LateralMm", "RotationRadians", "StartOffsetMm", "EndOffsetMm", "PhysicalReferenceSegment", "HasGeometryOverride" })
            Assert.DoesNotContain(legacy, service);
    }

    [Fact]
    public void SignatureComparison_UsesPreEditMeasurement_AndCurrentScannerWithoutProvenance()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var numbering = Read("TimberElementItemIdentityService.cs");
        Assert.Contains("TimberCalculator.Measure(timber, line.Length, roundingStep)", service);
        Assert.Contains("member => member.Signature", service);
        Assert.Contains("previousSignature != TimberElementSignature.FromMeasurement(entry.Measurement)", numbering);
        Assert.Contains("AssignElementIdsAfterGeometryEdit(candidates)", numbering);
        Assert.Contains("DrawingScanner.FindAllTimberElements", numbering);
        Assert.DoesNotContain("SourceGeneratedMemberKey", numbering);
        Assert.DoesNotContain("ManualOverrides", numbering);
    }

    [Fact]
    public void GripNoAndPhysicalGrip_RestoreCapturedEntitiesAndExactGroup_Terminally()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("entity.CopyFrom(saved.Copy)", service);
        Assert.Contains("RestoreGroup(transaction, snapshot, ownerMembers.Key)", service);
        Assert.Contains("group.GetAllEntityIds().ToHashSet().SetEquals(owner.GroupMembers)", service);
        Assert.Contains("RoofCommandLifecycleTerminalState.MarkHandled", service);
        Assert.Contains("if (physicalRejected)", service);
        Assert.Contains("RoofPhysical3DWarningService.Show()", service);
        Assert.Contains("manualOverrideWritten=false terminalHandled=true", service);
    }

    [Fact]
    public void ClassicStretch_UsesTheSharedOrdinaryPlanLifecycleBeforeLegacyRecovery()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        var rules = ReadCore("RoofGeneratedMemberEditCommandRules.cs");
        Assert.Contains("IsOrdinaryPlanGeometryEditCommand", service);
        Assert.Contains("IsOrdinaryPlanGeometryEditCommand", live);
        Assert.Contains("IsClassicStretch(globalCommandName) || IsGripStretchCommand(globalCommandName) ||", rules);
        Assert.Contains("ROOF_ORDINARY_STRETCH_LIFECYCLE", service);
        Assert.Contains("designationChanged=", service);
        Assert.Contains("manualOverrideWritten=false terminalHandled=true", service);
    }

    [Fact]
    public void GripNo_RebuildsImpliedSelectionAndRestoresItWithoutRegenAll()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var refresh = Read("RoofOrdinaryGripRollbackRefreshService.cs");
        var rollback = service.IndexOf("if (!accepted)", StringComparison.Ordinal);
        var accepted = service.IndexOf("else", rollback, StringComparison.Ordinal);
        Assert.True(rollback >= 0 && accepted > rollback);
        var rollbackPath = service[rollback..accepted];
        Assert.Contains("RoofOrdinaryGripRollbackRefreshService.Schedule", service);
        Assert.Contains("RoofOrdinaryGripRollbackRefreshService.SchedulePhysical3D", service);
        Assert.Contains("physicalRollbackMembers", service);
        var commit = service.IndexOf("transaction.Commit()", rollback, StringComparison.Ordinal);
        var schedule = service.IndexOf("RoofOrdinaryGripRollbackRefreshService.Schedule", commit, StringComparison.Ordinal);
        Assert.True(commit >= 0 && schedule > commit);
        Assert.Contains("QueueForGraphicsFlush", refresh);
        Assert.Contains("RecordGraphicsModified(true)", refresh);
        Assert.Contains("CaptureSelection(document.Editor)", refresh);
        Assert.Contains("Editor.Regen", refresh);
        Assert.Contains("deferredRegenScheduled", refresh);
        Assert.Contains("deferredRegenExecuted", refresh);
        Assert.Contains("commandActiveAtRegen", refresh);
        Assert.Contains("AcApp.Idle += idleHandler", refresh);
        Assert.Contains("AcApp.Idle -= idleHandler", refresh);
        Assert.Contains("selectionCleared=false", refresh);
        Assert.Contains("selectionRestored", refresh);
        Assert.Contains("selectionBefore=", refresh);
        Assert.Contains("UpdateScreen()", refresh);
        Assert.Contains("ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH", refresh);
        Assert.Contains("representation=", refresh);
        Assert.Contains("solid=", refresh);
        Assert.Contains("restoredEntityCount=", refresh);
        Assert.DoesNotContain("RegenType", refresh);
        Assert.DoesNotContain("RegenAll", refresh);
    }

    [Fact]
    public void IndependentGrip_HasNoPrompt_RefreshesAnnotationsAndVerifiesExclusion()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("!a.Member.Independent && a.Change != RoofOrdinaryGripChange.None", service);
        Assert.Contains("RoofIndependentOrdinaryEntityRole.Annotation", service);
        Assert.Contains("Independent Ordinary GRIP remains in roof GROUP", service);
        Assert.Contains("TimberAnnotationService.EnsureForElement", service);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    private static string ReadCore(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.Core", "Services", "Roofs", file));
    }
}
