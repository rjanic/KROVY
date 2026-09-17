using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for whole-roof MIRROR (Erase source = No) reusing the
/// whole-roof COPY rebind pipeline. Member-only MIRROR AttachedManual policy must
/// remain intact when completeness is not matched.
/// </summary>
public sealed class RoofWholeRoofMirrorSourceContractTests
{
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Rebind = Read("RoofWholeRoofCopyRebindService.cs");
    private static readonly string Mirror = Read("RoofMirrorCloneDetachService.cs");
    private static readonly string Snapshot = Read("RoofGeneratedCopyPreCommandSnapshotService.cs");
    private static readonly string Diag = Read("RoofGeneratedCopyLifecycleDiag.cs");

    [Fact]
    public void CommandWillStart_CapturesPreCommandSnapshotForMirror()
    {
        var willStart = Segment(Live, "private void CommandWillStart(", "private void CommandEnded(");
        Assert.Contains("IsMirrorCommand(e.GlobalCommandName)", willStart);
        Assert.Contains("CaptureForCopy(_document)", willStart);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(e.GlobalCommandName)", willStart);
    }

    [Fact]
    public void CommandEnded_RunsWholeRoofRebindForMirror_BeforeLiveResizeAndMirrorDetach()
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
        Assert.True(rebind >= 0, "Whole-roof rebind call not found.");
        Assert.True(liveResize > rebind, "Whole-roof rebind must run before LiveResize.");
        Assert.Contains("nativeMirror", Live);
        Assert.Contains("IsMirrorCommand(globalCommandName)", Live);
        Assert.Contains("if (nativeCopy || nativeMirror)", Live);

        var mirror = Live.IndexOf("RoofMirrorCloneDetachService.Process(", StringComparison.Ordinal);
        Assert.True(mirror > rebind, "Whole-roof rebind must run before MirrorCloneDetach.");
    }

    [Fact]
    public void RebindService_AcceptsMirrorCommand_SharesCopyRebuildPipeline()
    {
        Assert.Contains("IsMirrorCommand(globalCommandName)", Rebind);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Rebind);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", Rebind);
        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            Rebind);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner(", Rebind);
        Assert.Contains("RegisterConsumedWholeRoofClones(", Rebind);
    }

    [Fact]
    public void MirrorDetach_SkipsConsumedWholeRoofClones_PreservesMemberOnlyPolicy()
    {
        var cloneLoop = Segment(
            Mirror,
            "foreach (var id in appendedTimberIds)",
            "// MIRROR Yes (Generated)");
        Assert.Contains("IsConsumedWholeRoofClone(", cloneLoop);
        Assert.Contains("continue;", cloneLoop);
        // Member-only Generated → AttachedManual policy remains for non-consumed clones.
        Assert.Contains("TryDetachAndPromote(", Mirror);
        Assert.Contains("roleAfter = \"attached-manual\"", Mirror);
        Assert.Contains("ROOF_MIRROR_TRACE", Mirror);
    }

    [Fact]
    public void SnapshotClear_HappensAfterMirrorDetach()
    {
        var refreshCandidates = Segment(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        var mirror = refreshCandidates.IndexOf(
            "RoofMirrorCloneDetachService.Process(",
            StringComparison.Ordinal);
        var clear = refreshCandidates.IndexOf(
            "RoofGeneratedCopyPreCommandSnapshotService.Clear()",
            StringComparison.Ordinal);
        Assert.True(mirror >= 0, "MirrorCloneDetach call not found in RefreshCandidates.");
        Assert.True(clear > mirror, "Snapshot clear must follow MirrorCloneDetach.");
        Assert.Contains("GetPreCommandStructuralGeneratedHandlesByOwner", Snapshot);
    }

    [Fact]
    public void RebindCollectOwners_FallsBackToClassifyWhenStrictHipRestoreRejectsOrientationFlip()
    {
        Assert.Contains("RoofDefinitionPersistence.Restore(", Rebind);
        Assert.Contains("RoofDefinitionPersistence.Classify(", Rebind);
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", Rebind);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", Rebind);
        var collect = Segment(
            Rebind,
            "private static IReadOnlyList<OwnerCandidate> CollectOwners(",
            "private static IReadOnlyList<AppendedGeneratedClone> CollectAppendedGeneratedClones(");
        var restore = collect.IndexOf("RoofDefinitionPersistence.Restore(", StringComparison.Ordinal);
        var classify = collect.IndexOf("RoofDefinitionPersistence.Classify(", StringComparison.Ordinal);
        Assert.True(restore >= 0 && classify > restore);
    }

    [Fact]
    public void Diagnostics_EmitWholeMirrorDetectAndRebind()
    {
        Assert.Contains("ROOF_WHOLE_MIRROR_DETECT", Diag);
        Assert.Contains("ROOF_WHOLE_MIRROR_REBIND", Diag);
        Assert.Contains("ROOF_WHOLE_MIRROR_STAGE", Diag);
        Assert.Contains("definition-equivalent-false", Rebind);
        Assert.Contains("isMirror", Rebind);
        Assert.Contains("isMirror);", Rebind);
    }

    [Fact]
    public void WholeRoofMirror_PersistsMirroredDefinition_BeforeFinalGroupSync()
    {
        var rebindPair = Segment(
            Rebind,
            "private static bool TryRebindPair(",
            "private static bool TryRebindAttachedManualClone(");
        var persist = rebindPair.IndexOf(
            "TryPersistMirroredOwnerDefinitionForGroupSync(",
            StringComparison.Ordinal);
        var finalSync = rebindPair.IndexOf(
            "if (!RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        Assert.True(persist >= 0, "Mirrored definition persist missing before group sync.");
        Assert.True(finalSync > persist, "Definition persist must precede final TrySyncForOwner.");

        Assert.Contains("RoofDefinitionPersistence.UpdateGeometry(", Rebind);
        Assert.Contains("RoofDefinitionStore.Write(", Rebind);
        Assert.Contains("RoofDefinitionPersistence.Restore(", Rebind);
        Assert.Contains("DissociateOwnerFromForeignGroups(", Read("RoofDisplayGroupService.cs"));
        // COPY path: Restore already Matches → early return, no Write.
        Assert.Contains("COPY / already-current definition", Rebind);
        // Never open or rewrite the original owner during persist.
        var persistMethod = Segment(
            Rebind,
            "private static bool TryPersistMirroredOwnerDefinitionForGroupSync(",
            "private static bool TryRebindAttachedManualClone(");
        Assert.DoesNotContain("pair.OldOwner", persistMethod);
        Assert.DoesNotContain("OldOwner", persistMethod);
    }

    [Fact]
    public void WholeRoofRebind_SkipsSelectabilityMembershipRepair_BeforeSingleFinalEnsureGroup()
    {
        var rebindPair = Segment(
            Rebind,
            "private static bool TryRebindPair(",
            "private static bool TryRebindAttachedManualClone(");
        Assert.Contains("repairMembership: false", rebindPair);
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner(", rebindPair);
        Assert.Equal(1, Count(rebindPair, "RoofAssemblyGroupSyncService.TrySyncForOwner("));
        var apply = rebindPair.IndexOf(
            "RoofDisplayGroupSelectabilityService.ApplyForOwner(",
            StringComparison.Ordinal);
        var sync = rebindPair.IndexOf(
            "if (!RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        Assert.True(apply >= 0 && sync > apply);
        Assert.Contains(
            "repairMembership: false",
            rebindPair[apply..Math.Min(rebindPair.Length, apply + 280)]);
    }

    [Fact]
    public void EnsureGroup_GuardsAppendAgainstDuplicateObjectIdSlots()
    {
        var group = Read("RoofDisplayGroupService.cs");
        var ensure = Segment(
            group,
            "public static void EnsureGroup(",
            "private static void VerifyGroupUndoInvariant(");
        Assert.Contains("var present = new HashSet<ObjectId>(group.GetAllEntityIds());", ensure);
        Assert.Contains("if (!present.Add(addId))", ensure);
        Assert.Contains("ensure-group-collapse-duplicate", ensure);
        Assert.Contains("CountDuplicates(afterAppend)", ensure);
    }

    [Fact]
    public void WholeRoofMirror_DoesNotInventSeparateRebuildArchitecture()
    {
        Assert.DoesNotContain("RoofWholeRoofMirrorRebindService", Live);
        Assert.DoesNotContain("class RoofWholeRoofMirror", Rebind);
        Assert.Contains("RoofWholeRoofCopyRebindService.Process(", Live);
    }

    [Fact]
    public void WholeRoofMirror_DefersIntermediateCanonicalGroupSync_UntilAssemblyComplete()
    {
        var materializer = Read("RoofGeneratedRafterSetService.cs");
        var structural = Read("RoofAutomaticStructuralRafterMaterializationService.cs");
        var rebindPair = Segment(
            Rebind,
            "private static bool TryRebindPair(",
            "private static bool TryRebindAttachedManualClone(");

        Assert.Contains("bool syncAssemblyGroup = true", materializer);
        Assert.Contains("if (!syncAssemblyGroup)", materializer);
        Assert.Contains("bool syncAssemblyGroup = true", structural);
        Assert.Contains("if (syncAssemblyGroup)", structural);

        var display = rebindPair.IndexOf("RoofDisplayService.Rebuild(", StringComparison.Ordinal);
        var materialize = rebindPair.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var rehome = rebindPair.IndexOf(
            "RoofBoundaryIdentityService.RehomeForCurrentSource(",
            StringComparison.Ordinal);
        var structuralCall = rebindPair.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var finalSync = rebindPair.IndexOf(
            "if (!RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        Assert.True(display >= 0 && materialize > display);
        Assert.True(rehome > materialize, "BoundaryIdentity re-home must follow ordinary Materialize.");
        Assert.True(structuralCall > rehome, "Structural materialize must follow BoundaryIdentity re-home.");
        Assert.True(finalSync > structuralCall && finalSync > structuralCall);
        Assert.Contains("syncAssemblyGroup: false", rebindPair);
        Assert.DoesNotContain("materialize-before-full-sync", rebindPair);
        Assert.Equal(1, Count(rebindPair, "RoofAssemblyGroupSyncService.TrySyncForOwner("));
        Assert.Contains("TryPersistMirroredOwnerDefinitionForGroupSync(", rebindPair);
        var persist = rebindPair.IndexOf(
            "TryPersistMirroredOwnerDefinitionForGroupSync(",
            StringComparison.Ordinal);
        Assert.True(persist > structuralCall && persist < finalSync);
    }

    [Fact]
    public void WholeRoofMirror_RehomesNewOwnerBoundaryIdentity_BeforeStructural_NeverTouchesOldOwner()
    {
        var rebindPair = Segment(
            Rebind,
            "private static bool TryRebindPair(",
            "private static bool TryRebindAttachedManualClone(");
        Assert.Contains("RoofBoundaryIdentityService.RehomeForCurrentSource(", rebindPair);
        Assert.Contains("newOwner.PolylineId", rebindPair);
        Assert.Contains("boundary-identity-", rebindPair);
        // Old owner polyline/handle must not be opened for BoundaryIdentity writes.
        Assert.DoesNotContain("pair.OldOwner", Segment(
            rebindPair,
            "RoofBoundaryIdentityService.RehomeForCurrentSource(",
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction("));
        var service = Read("RoofBoundaryIdentityService.cs");
        Assert.Contains("CurrentRawWindingMismatch", service);
        Assert.DoesNotContain("DefinitionsEquivalent", service);
    }

    [Fact]
    public void WholeRoofMirror_HostParityFixture_56Ordinary16Structural_IsCompleteAssembly()
    {
        Assert.True(AcKrovy.Core.Services.Roofs.RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(
            56,
            16,
            0,
            56,
            16,
            0));
        Assert.False(AcKrovy.Core.Services.Roofs.RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(
            56,
            16,
            0,
            56,
            8,
            0));
    }

    [Fact]
    public void MemberOnlyMirror_PolicyRemainsDetachPromote_WhenNotWholeRoofConsumed()
    {
        Assert.Contains("TryDetachAndPromote(", Mirror);
        Assert.Contains("IsConsumedWholeRoofClone(", Mirror);
        Assert.Contains("roleAfter = \"attached-manual\"", Mirror);
        Assert.DoesNotContain("syncAssemblyGroup: false", Mirror);
        Assert.DoesNotContain("RehomeForCurrentSource", Mirror);
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
