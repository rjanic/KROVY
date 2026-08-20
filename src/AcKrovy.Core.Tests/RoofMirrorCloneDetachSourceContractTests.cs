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
    public void MirrorYes_SuppressesErasedGeneratedSource()
    {
        Assert.Contains("TrySuppressErasedGeneratedSource", Detach);
        Assert.Contains("RoofGeneratedMemberOverride.Suppress", Detach);
        Assert.Contains("RoofGeneratedMemberOverrideRules.WithEditState", Detach);
        Assert.Contains("RoofDefinitionStore.Write", Detach);
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
    public void MirrorYes_EmitsSuppressionTrace()
    {
        Assert.Contains("ROOF_MIRROR_YES", Detach);
        Assert.Contains("suppression=true", Detach);
        Assert.Contains("sourceRole=Generated", Detach);
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
        Assert.Contains("TryConvertInPlaceToAttachedManual", Detach);
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
    public void MirrorYes_InPlace_CapturesOwnerKey_BeforeGeneratedClear()
    {
        // owner/key must be captured from the live Generated entity BEFORE its Generated
        // XData is cleared by TryConvertInPlaceToAttachedManual.
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        var keyRead = inPlace.IndexOf("RoofGeneratedMemberKey.From(inPlaceGenerated)", StringComparison.Ordinal);
        var convert = inPlace.IndexOf("TryConvertInPlaceToAttachedManual(", StringComparison.Ordinal);
        Assert.True(keyRead >= 0, "owner/key capture not found in in-place branch");
        Assert.True(convert > keyRead, "owner/key must be captured before Generated XData is cleared");
    }

    [Fact]
    public void MirrorYes_InPlace_SuppressOverrideWritten()
    {
        Assert.Contains("TryWriteSuppressOverride", Detach);
        Assert.Contains("RoofGeneratedMemberOverride.Suppress(key, elementId)", Detach);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, updated)", Detach);
    }

    [Fact]
    public void MirrorYes_InPlace_SameEntityConverted_ChildIdentityIsHandle()
    {
        // The SAME entity H becomes AttachedManual Origin.Copy with ChildIdentity = its
        // own handle, anchored from its final mirrored WCS. No clone entity is created.
        Assert.Contains("RoofAttachedManualOrigin.Copy", Detach);
        Assert.Contains("cloneLine.Handle.ToString()", Detach);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", Detach);
    }

    [Fact]
    public void MirrorYes_InPlace_SelfExcludedFromAnchorCandidates()
    {
        // Clearing Generated XData before anchor discovery removes H from FindByOwner,
        // so H can never select itself as its own anchor.
        var convert = Segment(
            Detach,
            "private static bool TryConvertInPlaceToAttachedManual",
            "private static bool TryReinitializeAttachedManualClone");
        var clear = convert.IndexOf("RoofGeneratedTimberStore.TryClear", StringComparison.Ordinal);
        var promote = convert.IndexOf("TryPromoteFromMirroredGeometry", StringComparison.Ordinal);
        Assert.True(clear >= 0, "Generated XData clear not found in in-place convert");
        Assert.True(promote > clear, "anchor discovery must run AFTER Generated XData is cleared (self-exclusion)");
    }

    [Fact]
    public void MirrorYes_InPlace_UsesFinalMirroredWcsGeometry()
    {
        // The mirrored entity's final WCS Start/End are authoritative; anchor + relative
        // segment derive from them via the shared geometry-driven promote helper.
        Assert.Contains("cloneLine.StartPoint", Detach);
        Assert.Contains("cloneLine.EndPoint", Detach);
        Assert.Contains("SelectNearestMirrorAnchor", Detach);
    }

    [Fact]
    public void MirrorYes_InPlace_RefreshSameHandleAnnotation()
    {
        // After conversion, the SAME handle H annotation is refreshed (no duplicate set,
        // no stale old-position annotation) via the proven AttachedManual presentation
        // pipeline.
        var inPlace = Segment(Detach, InPlaceStart, InPlaceEnd);
        Assert.Contains("RefreshClonePresentation(document, transaction, id)", inPlace);
        Assert.Contains("inPlaceRoleAfter == \"attached-manual\"", inPlace);
    }

    [Fact]
    public void MirrorYes_InPlace_EmitsModeInPlaceSuccessDiagnostic()
    {
        Assert.Contains("mode=in-place", Detach);
        Assert.Contains("WriteInPlaceMirrorYesTrace", Detach);
        Assert.Contains("sourceRole=Generated", Detach);
        Assert.Contains("annotationRefresh=", Detach);
    }

    [Fact]
    public void MirrorYes_InPlace_GroupSyncAfterConversion()
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
