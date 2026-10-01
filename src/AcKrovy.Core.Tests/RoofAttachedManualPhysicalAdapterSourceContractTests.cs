using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualPhysicalAdapterSourceContractTests
{
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Fact]
    public void Materialization_UsesSemanticModelAndCorePlanner_NoSolidGeometryAuthority()
    {
        var source = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        Assert.Contains("RoofAttachedManualPhysicalBuilder.TryAppend", source);
        Assert.Contains("RoofOrdinaryPhysicalReconciliationRules.TryPlan", source);
        Assert.Contains("CreateSolid(members[key])", source);
        Assert.Contains("member.PhysicalIdentity", source);
        Assert.Contains("RoofOrdinaryPhysicalReconciliationRules.IsCanonical", source);
        Assert.DoesNotContain("GeometricExtents", source);
        Assert.DoesNotContain("MassProperties", source);
        Assert.DoesNotContain("solid.Clone()", source);
    }

    [Fact]
    public void NativeCloneReconciliation_OwnsSemanticPhysicalAndGroupWritesInOneOuterTransaction()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        var start = source.IndexOf("private void ProcessNativeMemberClones", StringComparison.Ordinal);
        var end = source.IndexOf("private bool TryOpenNativeMemberOwner", start, StringComparison.Ordinal);
        var method = source[start..end];
        var transaction = method.IndexOf("using var transaction =", StringComparison.Ordinal);
        var semantic = method.IndexOf("RoofAttachedManualCopyCloneReinitializeService.Process", StringComparison.Ordinal);
        var physical = method.IndexOf("TryReconcileSemanticMembersInTransaction", StringComparison.Ordinal);
        var group = method.IndexOf("RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);
        var commit = method.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(transaction >= 0 && transaction < semantic && semantic < physical && physical < group && group < commit);
        Assert.Contains("propagateFailure: true", method);
        Assert.Contains("keys.Add(RoofAttachedManualIdentityRules.PhysicalKey(attached))", method);
        Assert.Contains("Native replacement lost its Generated logical key", method);
        Assert.Contains("Native ordinary result has no canonical AttachedManual semantic identity", method);
        Assert.Contains("RecoverNativeMemberFailure(snapshot, nativeAdded, ownedNativeAdded, affectedOwners", method);
    }

    [Fact]
    public void MirrorYes_OrdinaryReplacement_KeepsGeneratedKeyInSemanticOverride()
    {
        var source = Read("RoofMirrorCloneDetachService.cs");
        var start = source.IndexOf("private static bool TryRebindGeneratedReplacement", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool TrySuppressErasedGeneratedSource", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("RoofGeneratedMemberKey.From(generated)", method);
        Assert.Contains("RoofGeneratedMemberOverrideMath.TryClassify", method);
        Assert.Contains("overrides.Upsert(edit)", method);
        Assert.Contains("RoofGeneratedTimberStore.WriteAtomic", method);
        Assert.Contains("before.Timber.ElementId", method);
        Assert.DoesNotContain("Suppress(", method);
        Assert.DoesNotContain("CreateAnchoredData", method);
        Assert.Contains("sourcesByClone.Count(pair => pair.Value == sourceId) != 1", source);
    }

    [Fact]
    public void WholeRoofMirrorAndResize_RequireChangedSource_AndConsumeWholeRoofClonesFirst()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("owner is not null && !snapshot.IsSourceChanged(owner)", source);
        Assert.Contains("IsConsumedWholeRoofClone", source);
        var whole = source.IndexOf("RoofWholeRoofCopyRebindService.Process", StringComparison.Ordinal);
        var member = source.IndexOf("ProcessNativeMemberClones(globalCommandName", StringComparison.Ordinal);
        Assert.True(whole >= 0 && member > whole);
    }

    [Fact]
    public void AttachedEdit_RefreshesSemanticRelativesBeforePhysicalAndCollateralRecovery()
    {
        var source = Read("RoofGeneratedMemberManualEditService.cs");
        var start = source.IndexOf("var attachedChanged = new List<string>()", StringComparison.Ordinal);
        var end = source.IndexOf("var groupSynced", start, StringComparison.Ordinal);
        var method = source[start..end];
        var semantic = method.IndexOf("RefreshModifiedAttachedManualRelatives", StringComparison.Ordinal);
        var physical = method.IndexOf("TryReconcileSemanticMembersInTransaction", StringComparison.Ordinal);
        var collateral = method.IndexOf("TryRestoreStretchPhysicalInTransaction", StringComparison.Ordinal);
        Assert.True(semantic >= 0 && physical > semantic && collateral > physical);
        Assert.Contains("attachedChangedIds", method);
        Assert.Contains("before is not null", method);
    }

    [Theory]
    [InlineData("RoofLiveResizeService.cs", "RoofSourceResizeChildPolicyService.Apply")]
    [InlineData("RoofEditCommandWorkflow.cs", "RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner")]
    public void SourceReplay_PrecedesAttachedPhysicalRefresh_AndStructuralValidation(string file, string replayCall)
    {
        var source = Read(file);
        var replay = source.IndexOf(replayCall, StringComparison.Ordinal);
        var attached = source.IndexOf("TryReconcileAttachedAfterReplay", replay, StringComparison.Ordinal);
        var structural = source.IndexOf("RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction", attached, StringComparison.Ordinal);
        Assert.True(replay >= 0 && attached > replay && structural > attached);
    }

    [Fact]
    public void PhysicalKeyAudit_IsIndependentOfCanonicalGroup()
    {
        var source = Read("RoofLiveResizeService.cs");
        var start = source.IndexOf("internal static bool TryVerifyStretchPhysicalState", StringComparison.Ordinal);
        var end = source.IndexOf("private static void ApplyDerivedPhysicalMoveTampers", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("RoofOrdinaryPhysicalReconciliationRules.IsCanonical", method);
        Assert.Contains("model.Members.Select(member => member.PhysicalIdentity)", method);
        Assert.Contains("RoofAssemblyGroupMembershipRules.IsCanonicalMembership", method);
        Assert.Contains("DUPLICATE_PHYSICAL_KEY", Read("RoofPhysical3DHostDiagnostics.cs"));
    }

    [Fact]
    public void UndoRedoAndCancel_KeepExistingZeroDatabaseWriteGuards()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        var start = source.IndexOf("if (shouldIgnore)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("_ignoreCurrentCommand = false;", start, StringComparison.Ordinal);
        var branch = source[start..end];
        Assert.Contains("ClearPendingLiveGeometryState()", branch);
        Assert.DoesNotContain("ProcessNativeMemberClones", branch);
        Assert.DoesNotContain("StartTransaction()", branch);
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName)", source);
    }

    [Fact]
    public void Recovery_PreservesUnrelatedNativeClones_AndRestoresOwnedMetadataPhysicalAndGroup()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        var start = source.IndexOf("private void RecoverNativeMemberFailure", StringComparison.Ordinal);
        var end = source.IndexOf("private static void RefreshTimberElements", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("owners.Contains(reference)", method);
        Assert.Contains("ownedSourceHandles.Contains(source)", method);
        Assert.Contains("RoofGeneratedTimberStore.WriteAtomic", method);
        Assert.Contains("member.Attached", method);
        Assert.Contains("TryReconcileSemanticMembersInTransaction", method);
        Assert.Contains("TryVerifyStretchPhysicalState", method);
    }

    [Fact]
    public void AttachedIdentity_UpgradeOnOwnedWrite_PreserveOnEdit_CreateOnClone()
    {
        Assert.Contains("RoofAttachedManualIdentityRules.Upgrade(data)", Read("RoofAttachedManualTimberStore.cs"));
        Assert.Contains("RoofAttachedManualIdentityRules.Resolve(stored.Data)", Read("RoofAttachedManualLifecycleService.cs"));
        Assert.Contains("semanticIdentity ?? RoofAttachedManualIdentityRules.Create()", Read("RoofAttachedManualLifecycleService.cs"));
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", Read("RoofAttachedManualCopyCloneReinitializeService.cs"));
    }
}
