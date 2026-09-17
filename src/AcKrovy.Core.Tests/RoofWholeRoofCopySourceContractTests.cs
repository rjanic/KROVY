using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofWholeRoofCopySourceContractTests
{
    private static readonly string Rebind = Read("RoofWholeRoofCopyRebindService.cs");
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string Reinit = Read("RoofAttachedManualCopyCloneReinitializeService.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Snapshot = Read("RoofGeneratedCopyPreCommandSnapshotService.cs");
    private static readonly string Diag = Read("RoofGeneratedCopyLifecycleDiag.cs");
    private static readonly string Identity = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofWholeRoofCopyIdentityRules.cs");

    [Fact]
    public void WholeRoofBranch_RunsInsideGenuineCopyOrMirrorCommand_ZeroDBAccessOnUndoRedo()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Rebind);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Rebind);
        Assert.Contains("IsMirrorCommand(globalCommandName)", Rebind);
        Assert.DoesNotContain("new Timer", Rebind);
        Assert.DoesNotContain("DatabaseReactor", Rebind);
        Assert.DoesNotContain("ObjectOverrule", Rebind);
    }

    [Fact]
    public void WholeRoofDetection_IsPayloadAndEventBased_NeverSpatial()
    {
        Assert.Contains("RoofWholeRoofCopyIdentityRules", Rebind);
        Assert.Contains("DefinitionsEquivalent", Rebind);
        Assert.Contains("IsCompleteAssemblyClone", Rebind);
        Assert.Contains("ClassifyPairing", Rebind);
        Assert.DoesNotContain("TryMatchCompleteSet", Rebind);
        Assert.DoesNotContain("GetClosestPointTo", Rebind);
        Assert.DoesNotContain("GetBoundingBox", Rebind);
        Assert.DoesNotContain("Extents", Rebind);
    }

    [Fact]
    public void PreCommandSnapshot_CapturesOwnersAndOwnedRoleHandles()
    {
        Assert.Contains("GetPreCommandOwnerHandles", Snapshot);
        Assert.Contains("GetPreCommandGeneratedHandlesByOwner", Snapshot);
        Assert.Contains("GetPreCommandAttachedManualHandlesByOwner", Snapshot);
        Assert.Contains("GetPreCommandDisplayHandlesByOwner", Snapshot);
        Assert.Contains("RoofAttachedManualTimberStore.FindByOwner", Snapshot);
        Assert.Contains("RoofDisplayStore.Read", Snapshot);
    }

    [Fact]
    public void GeneratedRebuild_RoutesThroughSharedPipeline_NoDirectGeneratedXDataWrite()
    {
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", Rebind);
        Assert.Contains("RoofRafterLayoutSolver.Solve(", Rebind);
        Assert.Contains("RoofGeneratedRafterSetService.TryRecoverRecipe(", Rebind);
        Assert.Contains("RoofGeneratedRafterSetService.CollectReservedElementIds(", Rebind);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver.Solve(", Rebind);
        Assert.DoesNotContain("unsupported-generated-roof-kind", Rebind);
        Assert.DoesNotContain("new RoofGeneratedTimberData(", Rebind);
        Assert.DoesNotContain("RoofGeneratedTimberStore.WriteAtomic(", Rebind);
        Assert.DoesNotContain("TimberSourceLineCreationService", Rebind);
    }

    [Fact]
    public void HipWholeRoofCopy_RebuildsStructuralSetThroughAuthoritativeReconcile()
    {
        Assert.Contains("CollectAppendedStructuralClones(", Rebind);
        Assert.Contains("GetPreCommandStructuralGeneratedHandlesByOwner(", Rebind);
        Assert.Contains("pair.StructuralClones", Rebind);
        Assert.Contains("RoofStructuralGeneratedStore.Read(", Rebind);
        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            Rebind);
        Assert.Contains("structuralRebuilt = structural.Actual", Rebind);
        Assert.Contains("RoofDisplayService.EnsureAllDisplayBehindTimber(", Rebind);
        Assert.DoesNotContain("new RoofStructuralGeneratedData(", Rebind);
        Assert.DoesNotContain("RoofStructuralGeneratedStore.WriteAtomic(", Rebind);
    }

    [Fact]
    public void WholeRoofCompleteness_IncludesStructuralGeneratedChildren()
    {
        Assert.Contains("GetPreCommandStructuralGeneratedHandlesByOwner", Snapshot);
        Assert.Contains("StructuralGeneratedHandlesByOwner", Snapshot);
        Assert.Contains("preStructural.Count", Rebind);
        Assert.Contains("appendedStructural.Count", Rebind);
        Assert.Contains("clone.Data.RoofOwnerReference", Rebind);
    }

    [Fact]
    public void AttachedManualRebind_UsesLogicalAnchorKey_NeverNearestStationGuessing()
    {
        Assert.Contains("TryFindGeneratedAnchorLine(", Rebind);
        Assert.Contains("CreateAnchoredData(", Rebind);
        Assert.Contains("WriteAnchored(", Rebind);
        Assert.Contains("AnchorGeneratedMemberKey", Rebind);
        Assert.DoesNotContain("SelectNearestMirrorAnchor", Rebind);
        Assert.DoesNotContain("SelectNearestAnchor", Rebind);
    }

    [Fact]
    public void ConsumedWholeRoofClones_NeverEnterPerRafterDetach()
    {
        Assert.Contains("RegisterConsumedWholeRoofClones(", Rebind);
        Assert.Contains("IsConsumedWholeRoofClone(", Snapshot);
        Assert.Contains("IsConsumedWholeRoofClone(", Rehydration);
        Assert.Contains("IsConsumedWholeRoofClone(", Reinit);
        Assert.Contains("IsConsumedWholeRoofClone(", Read("RoofMirrorCloneDetachService.cs"));
        Assert.Contains("IsConsumedWholeRoofKey(", Rehydration);
    }

    [Fact]
    public void ConsumedWholeRoofClones_AreExcludedFromGeometryAssociationObservations()
    {
        var observations = Segment(
            Rehydration,
            "private static IReadOnlyList<RoofGeneratedRafterGeometryObservation> CollectObservations(",
            "private static IReadOnlyCollection<string> CollectAppendedMemberKeys(");
        Assert.Contains("IsConsumedWholeRoofClone(", observations);
        Assert.Contains("continue;", observations);
    }

    [Fact]
    public void ConsumedRegistration_HappensBeforeAnyRebindWork()
    {
        var registration = Rebind.IndexOf("RegisterConsumedWholeRoofClones(", StringComparison.Ordinal);
        var rebindWork = Rebind.IndexOf("TryRebindPair(", StringComparison.Ordinal);
        Assert.True(registration >= 0, "RegisterConsumedWholeRoofClones not found.");
        Assert.True(rebindWork > registration, "Consumed registration must precede rebind work.");
    }

    [Fact]
    public void WholeRoofBranch_RunsBeforeLiveResizeAndPerRafterCloneServices()
    {
        var refreshCandidates = Segment(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        var rebind = refreshCandidates.IndexOf(
            "RoofWholeRoofCopyRebindService.Process(",
            StringComparison.Ordinal);
        var liveResize = refreshCandidates.IndexOf(
            "RoofLiveResizeService.Process(",
            StringComparison.Ordinal);
        Assert.True(rebind >= 0, "Whole-roof rebind call not found before RefreshTimberElements.");
        Assert.True(liveResize > rebind, "Whole-roof rebind must run before LiveResize.");

        var reinit = Live.IndexOf("RoofAttachedManualCopyCloneReinitializeService.Process(", StringComparison.Ordinal);
        var rehydration = Live.IndexOf("RoofGeneratedRafterCopyOwnershipRehydrationService.Process(", StringComparison.Ordinal);
        var rebindAny = Live.IndexOf("RoofWholeRoofCopyRebindService.Process(", StringComparison.Ordinal);
        Assert.True(rebindAny >= 0, "Whole-roof rebind call not found in LiveGeometrySynchronizationService.");
        Assert.True(reinit > rebindAny, "Whole-roof rebind must run before AttachedManual clone re-init.");
        Assert.True(rehydration > reinit, "reinit must run before COPY rehydration.");
    }

    [Fact]
    public void DisplayAnnotationsAndGroup_AreRebuiltForNewOwner()
    {
        Assert.Contains("RoofDisplayService.Rebuild(", Rebind);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner(", Rebind);
        Assert.Contains("RoofUnlockIndicatorService.Sync(", Rebind);
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner(", Rebind);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Rebind);
        Assert.Contains("if (!RoofAssemblyGroupSyncService.TrySyncForOwner(", Rebind);
    }

    [Fact]
    public void WholeRoofRebind_RebuildsDisplayBeforeMaterialize_DefersIntermediateGroupSync()
    {
        var rebindPair = Segment(
            Rebind,
            "private static bool TryRebindPair(",
            "private static bool TryRebindAttachedManualClone(");
        var display = rebindPair.IndexOf("RoofDisplayService.Rebuild(", StringComparison.Ordinal);
        var materialize = rebindPair.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var structural = rebindPair.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var finalSync = rebindPair.IndexOf(
            "if (!RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        Assert.True(display >= 0, "Display Rebuild not found in TryRebindPair.");
        Assert.True(materialize > display, "Display Rebuild must precede ordinary Materialize.");
        Assert.True(structural > materialize, "Structural materialize must follow ordinary Materialize.");
        Assert.True(finalSync > structural, "Final canonical group sync must follow complete rebuild.");
        Assert.Contains("syncAssemblyGroup: false", rebindPair);
        Assert.Equal(2, Count(rebindPair, "syncAssemblyGroup: false"));
        Assert.Contains("RoofBoundaryIdentityService.RehomeForCurrentSource(", rebindPair);
        var rehome = rebindPair.IndexOf(
            "RoofBoundaryIdentityService.RehomeForCurrentSource(",
            StringComparison.Ordinal);
        Assert.True(rehome > materialize && structural > rehome);
    }

    [Fact]
    public void WholeRoofInvariant_UsesNewOwnerExpectationAndFailedRebindCannotPass()
    {
        Assert.Contains("RegisterWholeRoofCopyExpectation(", Rebind);
        Assert.Contains("MarkWholeRoofCopyRebindSucceeded(", Rebind);
        Assert.Contains("HasWholeRoofCopyExpectations", Rehydration);
        Assert.Contains("TryGetWholeRoofCopyExpectedLogicalKeys(", Rehydration);
        Assert.Contains("expectedKeys.Count == actualKeys.Count", Rehydration);
        Assert.Contains("wholeCopyRebindSucceeded", Rehydration);
        Assert.Contains("wholeCopyRebind", Diag);
    }

    [Fact]
    public void TemporaryCloneOrphanAnnotations_AreDeletedAfterCloneErase_BeforeMaterialize()
    {
        var erase = Rebind.IndexOf("EraseGeneratedClones(", StringComparison.Ordinal);
        var delete = Rebind.IndexOf("DeleteForMissingSourceHandles(", StringComparison.Ordinal);
        var materialize = Rebind.IndexOf("RoofGeneratedRafterSetService.Materialize(", StringComparison.Ordinal);
        Assert.True(erase >= 0, "EraseGeneratedClones call not found.");
        Assert.True(delete >= 0, "DeleteForMissingSourceHandles call not found.");
        Assert.True(delete > erase, "Orphan annotation deletion must follow the temporary clone erase.");
        Assert.True(materialize > delete, "Materialize must follow the orphan annotation deletion.");
    }

    [Fact]
    public void TemporaryCloneOrphanDeletion_TargetsThisPairsCloneHandles_NeverSpatial()
    {
        Assert.Contains("DeleteForMissingSourceHandles(", Rebind);
        Assert.Contains("pair.GeneratedClones", Rebind);
        Assert.Contains("clone.Handle", Rebind);
        Assert.Contains("CountAnnotationsBoundToHandles", Rebind);
        Assert.DoesNotContain("GetClosestPointTo", Rebind);
        Assert.DoesNotContain("GetBoundingBox", Rebind);
        Assert.DoesNotContain("Extents", Rebind);
    }

    [Fact]
    public void Diagnostics_ReportDetectionAndRebindRouting()
    {
        Assert.Contains("ROOF_WHOLE_COPY_DETECT", Diag);
        Assert.Contains("ROOF_WHOLE_COPY_REBIND", Diag);
        Assert.Contains("ROOF_WHOLE_MIRROR_DETECT", Diag);
        Assert.Contains("ROOF_WHOLE_MIRROR_REBIND", Diag);
        Assert.Contains("ROOF_WHOLE_MIRROR_STAGE", Diag);
        Assert.Contains("WriteWholeCopyDetect", Diag);
        Assert.Contains("WriteWholeCopyRebind", Diag);
        Assert.Contains("WriteWholeMirrorStage", Diag);
        Assert.Contains("generatedClones", Diag);
        Assert.Contains("generatedRebuilt", Diag);
        Assert.Contains("attachedManualRebound", Diag);
        Assert.Contains("bool isMirror = false", Diag);
    }

    [Fact]
    public void IdentityRules_AreCadNeutral_NoAutodeskDependency()
    {
        Assert.Contains("public static bool DefinitionsEquivalent", Identity);
        Assert.Contains("public static bool RigidFootprintsEquivalent", Identity);
        Assert.Contains("public static bool IsCompleteAssemblyClone", Identity);
        Assert.Contains("public enum RoofWholeRoofCopyPairing", Identity);
        Assert.DoesNotContain("Autodesk", Identity);
        Assert.DoesNotContain("ObjectId", Identity);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static int Count(string source, string token)
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
