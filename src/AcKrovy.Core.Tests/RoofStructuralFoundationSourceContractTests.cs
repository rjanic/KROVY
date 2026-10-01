using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Adapter wiring checks; these do not claim AutoCAD HOST event validation.</summary>
public sealed class RoofStructuralFoundationSourceContractTests
{
    private static string Read(string name) => RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");

    [Fact]
    public void StructuralFirstClaim_IsAfterFullRoofAndBeforeOrdinaryAndGenericRecovery()
    {
        var tracker = Read("LiveGeometrySynchronizationService");
        var whole = tracker.IndexOf("RoofWholeRoofCopyRebindService.Process(", StringComparison.Ordinal);
        var structural = tracker.IndexOf("RoofStructuralNativeEditService.Process(", whole, StringComparison.Ordinal);
        var ordinary = tracker.IndexOf("ProcessNativeMemberClones(globalCommandName", structural, StringComparison.Ordinal);
        var generic = tracker.IndexOf("RoofLiveResizeService.Process(", ordinary, StringComparison.Ordinal);
        Assert.True(whole < structural && structural < ordinary && ordinary < generic);
        Assert.Contains("!structuralClaimedIds.Contains(id)", tracker);
        Assert.Contains("!claimedHandles.Contains(handle)", tracker);
        var router = Read("RoofStructuralNativeEditService");
        Assert.Contains("SourceGeometryMatchesSnapshot", router);
        Assert.Contains("Never claim an appended clone as its MOVE/ERASE source", router);
        Assert.Contains("RoofDisplayErasePreCommandMapService.TryResolve", router);
        Assert.Contains("mapped.StructuralData", router);
        Assert.Contains("mapped.PhysicalData", router);
        Assert.Contains("StructuralData: structural.Data", Read("RoofDisplayErasePreCommandMapService"));
        Assert.Contains("Ordinary Generated/AttachedManual rafters are intentionally ignored", router);
        Assert.Contains("RoofStructuralEditRules.IsStructuralHipValleyRole", router);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Read(entity).Data?.MemberKind == RoofGeneratedTimberKind.Rafter", router);
    }

    [Fact]
    public void LockedPlan2DStretch_DefersToLockedGeneratedMemberGuardWithoutStructuralGroupFailPath()
    {
        var rules = RoofUxSourceContractText.Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofStructuralEditRules.cs");
        Assert.Contains("existing Locked", rules);
        Assert.Contains("generated-member guard is the sole restore owner", rules);
        Assert.Contains("editState == RoofEditState.Locked", rules);
        Assert.Contains("RoofStructuralNativeAction.Unclaimed", rules);
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Unlocked));
        var manual = Read("RoofGeneratedMemberManualEditService");
        Assert.Contains("locked-generated-members-only", manual);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", manual);
        // Ordinary STRETCH / GRIP routing stays on the assembly-snapshot + ProcessOwners path.
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("GRIP_STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsClassicStretch("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGripStretchCommand("GRIP_STRETCH"));
        Assert.Contains("RoofGeneratedMemberManualEditService.ProcessOwners", Read("RoofLiveResizeService"));
    }

    [Fact]
    public void UnlockedRestorePlan_RebuildsDisplayBeforeGroupSyncAndForcesPlan2DZ0()
    {
        var router = Read("RoofStructuralNativeEditService");
        var restore = router.IndexOf("// RestorePlan / RejectClone: keep the canonical Plan2D source.", StringComparison.Ordinal);
        var z0Start = router.IndexOf("line.StartPoint = new(before.Start.X, before.Start.Y, 0);", restore, StringComparison.Ordinal);
        var z0End = router.IndexOf("line.EndPoint = new(before.End.X, before.End.Y, 0);", z0Start, StringComparison.Ordinal);
        var physical = router.IndexOf("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(", z0End, StringComparison.Ordinal);
        var display = router.IndexOf("RoofDisplayService.Rebuild(", physical, StringComparison.Ordinal);
        var group = router.IndexOf("TrySyncForOwner(document, transaction, ownerId)", display, StringComparison.Ordinal);
        Assert.True(restore >= 0 && z0Start > restore && z0End > z0Start && physical > z0End && display > physical && group > display);
        Assert.Contains("syncAssemblyGroup: false", router);
        Assert.Contains("CreateOwnedDisplayEdges(owner, geometry)", router);
        Assert.Contains("crossing selection", router);
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("MOVE", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("ERASE", false, RoofEditState.Unlocked));
    }

    [Fact]
    public void OrdinaryClonePath_StillOwnsOrdinaryRaftersAndDoesNotBecomeStructuralClaim()
    {
        var tracker = Read("LiveGeometrySynchronizationService");
        var ordinary = RoofUxSourceContractText.Member(tracker,
            "private void ProcessNativeMemberClones",
            "private bool TryOpenNativeMemberOwner");
        Assert.Contains("generated?.MemberKind == RoofGeneratedTimberKind.Rafter", ordinary);
        Assert.Contains("RoofGeneratedRafterCopyOwnershipRehydrationService.Process", ordinary);
        Assert.Contains("RoofMirrorCloneDetachService.Process", ordinary);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", ordinary);
        var router = Read("RoofStructuralNativeEditService");
        Assert.DoesNotContain("RoofGeneratedTimberKind.Rafter", router);
        Assert.DoesNotContain("RoofAttachedManualTimberStore", router);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("COPY"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("MIRROR"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("ARRAY"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("ARRAYRECT"));
        Assert.True(RoofStructuralEditRules.RequiresAssemblySnapshotCapture("ARRAY"));
        Assert.Contains("RequiresAssemblySnapshotCapture(e.GlobalCommandName)", tracker);
        Assert.Contains("RequiresAssemblySnapshotCapture(globalCommandName)", Read("RoofUnsupportedStretchRecoverySnapshotService"));
    }

    [Fact]
    public void SemanticWritePrecedesPhysicalAndCommittedClaimsProtectMixedOrdinaryFallback()
    {
        var router = Read("RoofStructuralNativeEditService");
        var store = router.IndexOf("RoofStructuralEditStateStore.Write(", StringComparison.Ordinal);
        var physical = router.IndexOf("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(", StringComparison.Ordinal);
        var commit = router.IndexOf("transaction.Commit();", physical, StringComparison.Ordinal);
        var claim = router.IndexOf("snapshot.ClaimStructural(", commit, StringComparison.Ordinal);
        var finalize = router.IndexOf("TryFinalizeStructuralMembers(", claim, StringComparison.Ordinal);
        Assert.True(store < physical && physical < commit && commit < claim && claim < finalize);
        Assert.Contains("Suppressed = true", router);
        Assert.Contains("TryAcceptMove", router);
        Assert.Contains("line.Erase(false)", router);
        Assert.Contains("RejectClone", router);
        Assert.Contains("eraseCloneIds", router);
        Assert.DoesNotContain("RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(", router);
        Assert.Contains("annotations[candidate.Id] = timber", router);
        var recovery = Read("RoofUnsupportedStretchRecoveryService");
        Assert.Contains("entry.IsStructuralClaimed(timber.EntityHandle)", recovery);
        Assert.Contains("IsClaimedStructural(ownerHandle, timber.EntityHandle)", recovery);
        Assert.Contains("IsClaimedStructural(ownerHandle, annotation.SourceHandle)", recovery);
        Assert.DoesNotContain("structural-hip-valley-before-unlocked-accept", Read("RoofGeneratedMemberManualEditService"));
    }

    [Fact]
    public void PhysicalRecovery_IsRoleBasedFreshBuilderAndNeverReadsEditedSolidGeometry()
    {
        var router = Read("RoofStructuralNativeEditService");
        Assert.Contains("physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid", router);
        Assert.Contains("RoofStructuralNativeAction.RebuildPhysical", router);
        Assert.Contains("RoofStructuralNativeAction.RejectClone", router);
        Assert.DoesNotContain("GeometricExtents", router);
        Assert.DoesNotContain("MassProperties", router);
        Assert.DoesNotContain("TransformBy", router);
        var physical = Read("RoofStructuralRafterSolidMaterializationService");
        Assert.Contains("RoofStructuralRafterPolyhedronService.TryBuild(request", physical);
        Assert.Contains("RoofStructuralEditRules.Place(body", physical);
        Assert.Contains("EraseStructuralRafterSolids", physical);
        Assert.Contains("DetachMembersBeforeErase", physical);
        Assert.Contains("!physicalKeys.Add(data.StructuralId)", router);
        Assert.Contains("physicalKeys.SetEquals(keys)", router);
    }

    [Fact]
    public void UndoRedo_ReturnsBeforeAnyDatabaseAccessOrLock()
    {
        var router = Read("RoofStructuralNativeEditService");
        Assert.True(router.IndexOf("IsUndoRedoCommand(commandName)", StringComparison.Ordinal) <
                    router.IndexOf("document.LockDocument()", StringComparison.Ordinal));
        var finalizer = RoofUxSourceContractText.Member(Read("RoofAssemblyGroupSyncService"),
            "public static bool TryFinalizeStructuralMembers", "public static bool TryFinalizeRestoredSources");
        Assert.True(finalizer.IndexOf("IsUndoRedoCommand(commandName)", StringComparison.Ordinal) <
                    finalizer.IndexOf("StartTransaction", StringComparison.Ordinal));
        Assert.DoesNotContain("SendStringToExecute", router + finalizer);
    }

    [Fact]
    public void OwnerPersistence_HasNoHandlesOrElementIdAndMissingReadDoesNotWrite()
    {
        var store = Read("RoofStructuralEditStateStore");
        var read = RoofUxSourceContractText.Member(store, "public static RoofStructuralEditState Read", "public static void Write");
        Assert.Contains("RoofStructuralEditState.Empty", read);
        Assert.DoesNotContain("CreateExtensionDictionary", read);
        Assert.DoesNotContain("ElementId", store);
        Assert.DoesNotContain("Handle", store);
        Assert.Contains("RoofStructuralEditRules.IsValid(state)", read);
        Assert.Contains("transaction.AddNewlyCreatedDBObject(record, true)", store);
        Assert.Contains("RoofStructuralEditRules.ApplyPlan", Read("RoofAutomaticStructuralRafterMaterializationService"));
    }

    [Fact]
    public void Group_UsesExactSlotsAndVerifiesClosedCommittedState()
    {
        var group = RoofUxSourceContractText.Member(Read("RoofDisplayGroupService"), "public static void EnsureGroup", "private static void VerifyGroupUndoInvariant");
        Assert.Contains("SurplusOrForeignMemberIndices", group);
        Assert.Contains("group.RemoveAt(index)", group);
        Assert.DoesNotContain("group.Remove(removeId)", group);
        Assert.Contains("!present.Add(addId)", group);
        Assert.Contains("throw new InvalidOperationException", group);
        var finalizer = RoofUxSourceContractText.Member(Read("RoofAssemblyGroupSyncService"),
            "public static bool TryFinalizeStructuralMembers", "public static bool TryFinalizeRestoredSources");
        Assert.Contains("pass < 2", finalizer);
        Assert.Contains("if (IsCurrent(document.Database, verify, ownerId)) return true", finalizer);
        Assert.Contains("structural-group-finalize-committed", finalizer);
    }

    [Fact]
    public void WholeRoofRebind_StillOwnsCompleteStructuralCloneSets()
    {
        var whole = Read("RoofWholeRoofCopyRebindService");
        Assert.Contains("CollectAppendedStructuralClones", whole);
        Assert.Contains("RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction", whole);
        var tracker = Read("LiveGeometrySynchronizationService");
        var wholeIdx = tracker.IndexOf("RoofWholeRoofCopyRebindService.Process(", StringComparison.Ordinal);
        var structuralIdx = tracker.IndexOf("RoofStructuralNativeEditService.Process(", wholeIdx, StringComparison.Ordinal);
        Assert.True(wholeIdx >= 0 && structuralIdx > wholeIdx);
    }
}
