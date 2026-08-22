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
    public void WholeRoofBranch_RunsOnlyInsideGenuineCopyCommand_ZeroDBAccessOnUndoRedo()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Rebind);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Rebind);
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
        Assert.Contains("RoofGeneratedRafterSetService.TryRecoverRecipe(", Rebind);
        Assert.Contains("RoofGeneratedRafterSetService.CollectReservedElementIds(", Rebind);
        Assert.DoesNotContain("new RoofGeneratedTimberData(", Rebind);
        Assert.DoesNotContain("RoofGeneratedTimberStore.WriteAtomic(", Rebind);
        Assert.DoesNotContain("TimberSourceLineCreationService", Rebind);
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
        Assert.Contains("IsConsumedWholeRoofKey(", Rehydration);
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
    public void WholeRoofBranch_RunsBeforePerRafterCloneServices()
    {
        var rebind = Live.IndexOf("RoofWholeRoofCopyRebindService.Process(", StringComparison.Ordinal);
        var reinit = Live.IndexOf("RoofAttachedManualCopyCloneReinitializeService.Process(", StringComparison.Ordinal);
        var rehydration = Live.IndexOf("RoofGeneratedRafterCopyOwnershipRehydrationService.Process(", StringComparison.Ordinal);
        Assert.True(rebind >= 0, "Whole-roof rebind call not found in LiveGeometrySynchronizationService.");
        Assert.True(reinit > rebind, "Whole-roof rebind must run before AttachedManual clone re-init.");
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
        Assert.Contains("WriteWholeCopyDetect", Diag);
        Assert.Contains("WriteWholeCopyRebind", Diag);
        Assert.Contains("generatedClones", Diag);
        Assert.Contains("generatedRebuilt", Diag);
        Assert.Contains("attachedManualRebound", Diag);
    }

    [Fact]
    public void IdentityRules_AreCadNeutral_NoAutodeskDependency()
    {
        Assert.Contains("public static bool DefinitionsEquivalent", Identity);
        Assert.Contains("public static bool IsCompleteAssemblyClone", Identity);
        Assert.Contains("public enum RoofWholeRoofCopyPairing", Identity);
        Assert.DoesNotContain("Autodesk", Identity);
        Assert.DoesNotContain("ObjectId", Identity);
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);
}
