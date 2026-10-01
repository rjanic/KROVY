using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofMirrorCloneDetachSourceContractTests
{
    private static readonly string CommandRules = Read(
        "src/AcKrovy.Core/Services/Roofs/RoofGeneratedMemberEditCommandRules.cs");
    private static readonly string LiveRules = Read(
        "src/AcKrovy.Core/Services/LiveGeometryCommandRules.cs");
    private static readonly string Reanchor = Read(
        "src/AcKrovy.Core/Services/Roofs/RoofAttachedManualReanchorRules.cs");
    private static readonly string Detach = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofMirrorCloneDetachService.cs");
    private static readonly string LiveSync = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/LiveGeometrySynchronizationService.cs");

    [Fact]
    public void MirrorCommand_IsClassified()
    {
        Assert.Contains("IsMirrorCommand", CommandRules);
        Assert.Contains("\"MIRROR\"", CommandRules);
    }

    [Fact]
    public void Mirror_JoinsGroupedUndoMark()
    {
        Assert.Contains("IsMirrorCommand(globalCommandName)", LiveRules);
    }

    [Fact]
    public void MirrorClone_IsDetachedAndPromoted_NotLeftGenerated()
    {
        Assert.Contains("RoofGeneratedTimberStore.TryClear", Detach);
        Assert.Contains("RoofGeneratedTimberStore.Read(cloneLine).Data is not null", Detach);
        Assert.Contains("RoofAttachedManualLifecycleService.WriteAnchored", Detach);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Detach);
    }

    [Fact]
    public void MirrorClone_UsesFaceAwareNearestAnchor()
    {
        Assert.Contains("SelectNearestMirrorAnchor", Detach);
        Assert.Contains("SelectNearestMirrorAnchor", Reanchor);
        Assert.Contains("relative.U1Mm <= relative.U0Mm", Reanchor);
    }

    [Fact]
    public void MirrorClone_NoCompatibleAnchor_FallsBackToGenericTimber_NoFabricatedOwnership()
    {
        Assert.Contains("no-compatible-anchor", Detach);
        Assert.Contains("generic-timber", Detach);
    }

    [Fact]
    public void Mirror_EmitsCompactDiagnostics()
    {
        Assert.Contains("ROOF_MIRROR_TRACE", Detach);
        Assert.Contains("ROOF_MIRROR_INVARIANT", Detach);
        Assert.Contains("duplicateKeyCount", Detach);
        Assert.Contains("uniqueStations", Detach);
        Assert.Contains("annotationRefresh", Detach);
        Assert.Contains("annotationReady=true", Detach);
    }

    [Fact]
    public void Mirror_ImmediatelyMaterializesAnnotation_AfterPromotion()
    {
        Assert.Contains("RefreshClonePresentation", Detach);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Detach);
        // Refresh runs only for the successfully promoted AttachedManual role.
        Assert.Contains("roleAfter == \"attached-manual\"", Detach);
    }

    [Fact]
    public void Mirror_RefreshRunsBeforeGroupSync()
    {
        var refresh = Detach.IndexOf("RefreshClonePresentation(", StringComparison.Ordinal);
        var groupSync = Detach.IndexOf(
            "RoofAssemblyGroupSyncService.TrySyncForOwnerReference(",
            StringComparison.Ordinal);
        Assert.True(refresh >= 0, "refresh call not found");
        Assert.True(groupSync > refresh, "annotation refresh must run before group sync");
    }

    [Fact]
    public void Mirror_IsWiredIntoLiveGeometryCommandEnd()
    {
        Assert.Contains("RoofMirrorCloneDetachService.Process(", LiveSync);
    }

    [Fact]
    public void Mirror_DoesNotClassifyAsAssemblySnapshotOrGeneratedEdit()
    {
        // MIRROR must NOT route through the STRETCH/ERASE member-edit override path.
        var snapshot = Segment(CommandRules, "IsAssemblySnapshotCommand", "IsGeneratedTimberEditCommand");
        Assert.DoesNotContain("\"MIRROR\"", snapshot);
    }

    [Fact]
    public void Mirror_OfAttachedManualClone_IsReinitialized()
    {
        Assert.Contains("RoofAttachedManualTimberStore.Read", Detach);
        Assert.Contains("TryReinitializeAttachedManualClone", Detach);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy", Detach);
    }

    [Fact]
    public void Mirror_SharedPromoteHelper_UsedByBothPaths()
    {
        Assert.Contains("TryPromoteFromMirroredGeometry", Detach);
        // Both the Generated detach path and the AttachedManual reinit path promote
        // through the same geometry-driven helper.
        var promote = Detach.IndexOf("TryPromoteFromMirroredGeometry(", StringComparison.Ordinal);
        Assert.True(promote >= 0, "shared promote helper not found");
        Assert.True(
            Detach.IndexOf("TryPromoteFromMirroredGeometry(", promote + 1, StringComparison.Ordinal) > promote,
            "promote helper must be invoked from more than one path");
    }

    [Fact]
    public void Mirror_OfAttachedManualClone_EmitsSourceRoleTrace()
    {
        Assert.Contains("sourceRole=AttachedManual", Detach);
        Assert.Contains("source={source}", Detach);
    }

    [Fact]
    public void MirrorYes_OrdinaryErasedSource_IsReboundBeforeLegacySuppression()
    {
        Assert.Contains("nativeSnapshot?.Members.Values.Any", Detach);
        Assert.Contains("member.Generated?.MemberKind == RoofGeneratedTimberKind.Rafter", Detach);
        Assert.Contains("sourcesByClone.TryGetValue(id, out var sourceId)", Detach);
        Assert.Contains("TryRebindGeneratedReplacement(document, transaction, cloneLine, before", Detach);
    }

    [Fact]
    public void MirrorYes_ResolvesErasedKeyViaOpenErasedAccess()
    {
        Assert.Contains("TryGetObjectAllowErased", Detach);
        Assert.Contains("GetObjectId(false, new Handle", Detach);
    }

    [Fact]
    public void MirrorYes_NonGeneratedSource_NoSuppression()
    {
        // A non-Generated (generic/AttachedManual) erased source must not produce an
        // override: the Generated read guard short-circuits before any override write.
        var suppress = Segment(
            Detach,
            "private static bool TrySuppressErasedGeneratedSource",
            "private static bool TryDetachAndPromote");
        // The Generated-read guard short-circuits BEFORE any override write.
        var guard = suppress.IndexOf("generated is null", StringComparison.Ordinal);
        var suppressWrite = suppress.IndexOf(
            "RoofGeneratedMemberOverride.Suppress", StringComparison.Ordinal);
        Assert.True(guard >= 0, "Generated guard not found");
        Assert.True(guard < suppressWrite, "Generated guard must precede the Suppress write");
    }

    [Fact]
    public void MirrorYes_OrdinaryRebind_ReportsSameKeyWithoutSuppression()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("ROOF_MIRROR_YES", rebind);
        Assert.Contains("suppression=false roleAfter=generated sameMemberKey=true", rebind);
    }

    [Fact]
    public void MirrorYes_InvariantReportsSuppressedCount()
    {
        Assert.Contains("suppressed={suppressed}", Detach);
        Assert.Contains("SuppressedCount", Detach);
    }

    [Fact]
    public void Mirror_ReceivesErasedSourceHandles()
    {
        // The Process call in LiveGeometrySynchronizationService passes erased handles.
        Assert.Contains("erasedSourceHandles", LiveSync);
    }

    [Fact]
    public void MirrorYes_InPlace_AcceptsModifiedCandidate_WithoutAppendOrErase()
    {
        // MIRROR Yes is a THIRD lifecycle: SAME entity modified in place (no appended
        // clone, no erased source). The gate must not short-circuit on those alone.
        Assert.Contains("mirrorModifiedTimberIds", Detach);
        Assert.Contains("mirrorModifiedTimberIds.Count == 0", Detach);
        Assert.Contains("TryRebindGeneratedReplacement", Detach);
    }

    private const string InPlaceStart = "// MIRROR Yes (Generated): HOST-proven lifecycle.";
    // End token spans the full in-place loop (through its group sync), which the first
    // inner #if DEBUG (around WriteInPlaceMirrorYesTrace) would otherwise truncate.
    private const string InPlaceEnd = "if (wrote || affectedOwners.Count > 0)";

    [Fact]
    public void MirrorYes_InPlace_DoesNotRequireObjectAppendedOrErased()
    {
        // The in-place loop iterates mirrorModifiedTimberIds (raw modified), not the
        // appended clone list, and must not require ObjectErased source recovery.
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        Assert.Contains("foreach (var id in mirrorModifiedTimberIds)", inPlace);
        Assert.DoesNotContain("TrySuppressErasedGeneratedSource", inPlace);
        Assert.DoesNotContain("TryGetObjectAllowErased", inPlace);
    }

    [Fact]
    public void MirrorYes_InPlace_RoutesModifiedIdsBeforeRoofFilter()
    {
        // Raw MIRROR modified ids are captured in LiveGeometrySynchronizationService
        // BEFORE roof-related candidate filtering drops them, then passed to the service.
        Assert.Contains("mirrorModifiedTimberIds = ids.Where", LiveSync);
        Assert.Contains("IsMirrorCommand(globalCommandName)", LiveSync);
        Assert.Contains("mirrorModifiedTimberIds ?? Array.Empty<ObjectId>()", LiveSync);
    }

    [Fact]
    public void MirrorYes_InPlace_ExcludesAppendedClones()
    {
        // A MIRROR No clone is also recorded in _modifiedIds; it must NOT be routed to
        // the in-place branch (it is already handled by the clone branch).
        Assert.Contains("!appendedSet.Contains(id)", LiveSync);
        Assert.Contains("appendedTimberIds.Contains(id)", Detach);
    }

    [Fact]
    public void MirrorYes_InPlace_CapturesOriginalKeyBeforeSemanticRebind()
    {
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        var keyRead = inPlace.IndexOf("RoofGeneratedMemberKey.From(inPlaceGenerated)", StringComparison.Ordinal);
        var rebind = inPlace.IndexOf("TryRebindGeneratedReplacement(", StringComparison.Ordinal);
        Assert.True(keyRead >= 0 && rebind > keyRead);
        Assert.Contains("beforeGenerated", inPlace);
    }

    [Fact]
    public void MirrorYes_InPlace_ExistingGeometryOverrideWritten_WithoutSuppression()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("RoofGeneratedMemberOverrideMath.TryClassify", rebind);
        Assert.Contains("overrides.Upsert(edit)", rebind);
        Assert.Contains("RoofDefinitionStore.Write", rebind);
        Assert.DoesNotContain("Suppress(", rebind);
    }

    [Fact]
    public void MirrorYes_InPlace_SameEntity_KeepsGeneratedMetadata()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("RoofGeneratedTimberStore.WriteAtomic", rebind);
        Assert.Contains("RoofGeneratedTimberStore.BuildSection(replacement, transaction, generated)", rebind);
        Assert.DoesNotContain("CreateAnchoredData", rebind);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", rebind);
    }

    [Fact]
    public void MirrorYes_InPlace_OrdinaryDoesNotSearchForNewAnchor()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("RoofGeneratedMemberKey.From(generated)", rebind);
        Assert.DoesNotContain("SelectNearestMirrorAnchor", rebind);
        Assert.DoesNotContain("TryPromoteFromMirroredGeometry", rebind);
    }

    [Fact]
    public void MirrorYes_InPlace_UsesFinalMirroredPlanGeometry()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("ToRoof(replacement.StartPoint)", rebind);
        Assert.Contains("ToRoof(replacement.EndPoint)", rebind);
        Assert.Contains("RoofGeneratedMemberOverrideMath.TryClassify(canonical, observed", rebind);
    }

    [Fact]
    public void MirrorYes_InPlace_RefreshesAcceptedGeneratedAnnotation()
    {
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        var ordinary = inPlace[inPlace.IndexOf("if (inPlaceGenerated.MemberKind == RoofGeneratedTimberKind.Rafter)", StringComparison.Ordinal)..];
        Assert.Contains("if (changed)", ordinary);
        Assert.Contains("RefreshClonePresentation(document, transaction, id)", ordinary);
    }

    [Fact]
    public void MirrorYes_InPlace_ReportsSemanticRebindSuccess()
    {
        var rebind = Segment(Detach, "private static bool TryRebindGeneratedReplacement", "private static bool TrySuppressErasedGeneratedSource");
        Assert.Contains("action=mirror-rebind generatedKey=", rebind);
        Assert.Contains("sameMemberKey=true result=ok", rebind);
    }

    [Fact]
    public void MirrorYes_InPlace_GroupSyncAfterSemanticReconciliation()
    {
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwnerReference", inPlace);
    }

    [Fact]
    public void MirrorYes_InPlace_NoCloneCreatedForYesPath()
    {
        // MIRROR Yes must not fabricate a clone: the in-place branch contains no
        // AppendEntity / AddNewlyCreatedDBObject and no ObjectAppended-driven loop.
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        Assert.DoesNotContain("AppendEntity", inPlace);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", inPlace);
    }

    [Fact]
    public void MirrorNo_Branch_Unchanged()
    {
        // MIRROR No (appended clone) paths must remain intact alongside the in-place one.
        Assert.Contains("TryDetachAndPromote", Detach);
        Assert.Contains("TryReinitializeAttachedManualClone", Detach);
        Assert.Contains("foreach (var id in appendedTimberIds)", Detach);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
