using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the PRODUCT RULE:
/// AutoCAD MIRROR Yes of an AttachedManual Origin.Copy child transforms the SAME entity
/// IN PLACE (appended=0 / erased=0 / modified=1). KROVY must preserve the same child
/// identity and Origin.Copy, re-anchor + recompute RelativeSegment from FINAL mirrored
/// WCS geometry, reconcile generic Timber metadata, and refresh the canonical annotation
/// set — with NO Generated suppression and NO extra child.
/// </summary>
public sealed class RoofMirrorYesAttachedManualSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private static readonly string Detach = Read(Infra + "RoofMirrorCloneDetachService.cs");
    private static readonly string LiveSync = Read(Infra + "LiveGeometrySynchronizationService.cs");
    private static readonly string ManualEdit = Read(Infra + "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Lifecycle = Read(Infra + "RoofAttachedManualLifecycleService.cs");

    private const string InPlaceStart = "// MIRROR Yes (Generated): HOST-proven lifecycle.";
    private const string InPlaceEnd = "if (wrote || affectedOwners.Count > 0)";

    private static string InPlaceLoop => Segment(Detach, InPlaceStart, InPlaceEnd);
    private static string AttachedMethod => Segment(
        Detach,
        "private static bool TryReanchorInPlaceAttachedManual",
        "private static bool TryParseHandle");

    // 1-2: uses modified id (not appended clone); appended=0/erased=0/modified=1 accepted.
    [Fact]
    public void InPlace_AttachedManual_UsesModifiedId_NotAppendedClone()
    {
        Assert.Contains("foreach (var id in mirrorModifiedTimberIds)", InPlaceLoop);
        Assert.Contains("TryReanchorInPlaceAttachedManual", InPlaceLoop);
        Assert.DoesNotContain("foreach (var id in appendedTimberIds)", InPlaceLoop);
    }

    [Fact]
    public void InPlace_GateAcceptsModifiedWithoutAppendOrErase()
    {
        Assert.Contains("mirrorModifiedTimberIds.Count == 0", Detach);
        Assert.Contains("mirrorModifiedTimberIds = ids.Where", LiveSync);
    }

    // 3-5: same ObjectId/handle, same ChildIdentity, Origin.Copy retained.
    [Fact]
    public void InPlace_AttachedManual_PreservesSameEntityIdentity()
    {
        // No clone entity is created; the same handle is re-anchored and Origin stays Copy.
        Assert.DoesNotContain("AddNewlyCreatedDBObject", AttachedMethod);
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Detach);
    }

    [Fact]
    public void InPlace_AttachedManual_ChildIdentityPreserved_AsOwnHandle()
    {
        // TryPromoteFromMirroredGeometry writes CreateAnchoredData(owner, sameHandle, ...)
        // so ChildIdentity == the entity's own (unchanged) handle.
        Assert.Contains("cloneLine.Handle.ToString()", Detach);
        Assert.Contains("CreateAnchoredData(", Detach);
    }

    // 6-7: no Generated metadata, no suppression.
    [Fact]
    public void InPlace_AttachedManual_NoGeneratedMetadata_NoSuppression()
    {
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", AttachedMethod);
        Assert.DoesNotContain("TryWriteSuppressOverride", AttachedMethod);
        Assert.DoesNotContain("RoofGeneratedMemberOverride.Suppress", AttachedMethod);
    }

    // 8: AttachedManual count unchanged (no new child; same entity rewritten).
    [Fact]
    public void InPlace_AttachedManual_CountUnchanged()
    {
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
        Assert.Contains("WriteAnchored", Detach);
    }

    // 9-11: FINAL mirrored WCS geometry drives anchor + RelativeSegment.
    [Fact]
    public void InPlace_AttachedManual_UsesFinalMirroredWcsGeometry()
    {
        Assert.Contains("cloneLine.StartPoint", Detach);
        Assert.Contains("cloneLine.EndPoint", Detach);
        Assert.Contains("SelectNearestMirrorAnchor", Detach);
        Assert.Contains("CreateAnchoredData(", Detach);
    }

    [Fact]
    public void InPlace_AttachedManual_PersistsUpdatedAnchor()
    {
        Assert.Contains("RoofAttachedManualLifecycleService.WriteAnchored", Detach);
        Assert.Contains("newAnchorKey = rewritten.Data?.AnchorGeneratedMemberKey", AttachedMethod);
    }

    // 12: generic Timber metadata reconciled via the proven identity path.
    [Fact]
    public void InPlace_AttachedManual_ReconcilesGenericTimberMetadata()
    {
        Assert.Contains("RefreshClonePresentation(document, transaction, id)", InPlaceLoop);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Detach);
    }

    // 13-15: stale old-position annotation removed; canonical set recreated at final
    // geometry; canonical orientation reused (no mirror-specific angle algorithm).
    [Fact]
    public void InPlace_AttachedManual_RefreshesAnnotationFromFinalGeometry()
    {
        Assert.Contains("RefreshClonePresentation(document, transaction, id)", InPlaceLoop);
        Assert.Contains("attachedRoleAfter == \"attached-manual\"", InPlaceLoop);
    }

    [Fact]
    public void InPlace_AttachedManual_NoMidpointOrNearestAnnotationHeuristic()
    {
        // Deterministic source-handle / command-lifecycle identity only; never geometry
        // proximity. No midpoint/nearest annotation heuristic is introduced.
        Assert.DoesNotContain("DistanceTo", AttachedMethod);
        Assert.DoesNotContain("GetBoundingBox", AttachedMethod);
    }

    // 16: source resize dormancy/reactivation + footprint containment unchanged.
    [Fact]
    public void SourceResize_FootprintContainment_Unchanged()
    {
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", Lifecycle);
        Assert.Contains("sourceFootprintVertices", Read(Infra + "RoofSourceResizeChildPolicyService.cs"));
    }

    // 17: ERASE remains permanent Copy deletion.
    [Fact]
    public void Erase_PermanentCopyDeletion_Unchanged()
    {
        Assert.Contains("copy-delete", ManualEdit);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy &&", ManualEdit);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Split", ManualEdit);
    }

    // 18: U/REDO zero-DB contract unchanged.
    [Fact]
    public void ZeroDbUndoRedo_Unchanged()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        Assert.Contains("RoofUndoGuardDiag.Write", resize);
        Assert.Contains("action=skip-write", resize);
    }

    // 20: Generated MIRROR Yes unchanged.
    [Fact]
    public void GeneratedMirrorYes_Unchanged()
    {
        Assert.Contains("TryConvertInPlaceToAttachedManual", Detach);
        Assert.Contains("WriteInPlaceMirrorYesTrace", Detach);
        Assert.Contains("sourceRole=Generated", Detach);
    }

    // 21: MIRROR No AttachedManual re-init unchanged.
    [Fact]
    public void MirrorNoAttachedManual_Unchanged()
    {
        Assert.Contains("TryReinitializeAttachedManualClone", Detach);
        Assert.Contains("foreach (var id in appendedTimberIds)", Detach);
    }

    // 22: deterministic appended annotation cleanup unchanged.
    [Fact]
    public void MirrorNo_DeterministicAnnotationCleanup_Unchanged()
    {
        Assert.Contains("DeleteMirroredCloneAnnotations", Detach);
        Assert.Contains("appendedAnnotationIds", Detach);
    }

    // 24: Split lifecycle unchanged.
    [Fact]
    public void SplitLifecycle_Unchanged()
    {
        Assert.Contains("sourceRole = attachedSource.Origin", ManualEdit);
        Assert.Contains(": \"AttachedManual\"", ManualEdit);
    }

    // 25: T2 creation order unchanged (MIRROR service never appends T2 entities).
    [Fact]
    public void T2Order_Unchanged_NoAppendInMirrorYes()
    {
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", AttachedMethod);
    }

    // Diagnostic: compact one-line ROOF_MIRROR_YES_ATTACHED trace.
    [Fact]
    public void InPlace_AttachedManual_EmitsCompactDiagnostic()
    {
        Assert.Contains("ROOF_MIRROR_YES_ATTACHED", Detach);
        Assert.Contains("mode=in-place", Detach);
        Assert.Contains("sourceRole=AttachedManual", Detach);
        Assert.Contains("originBefore={originBefore}", Detach);
        Assert.Contains("originAfter={originAfter}", Detach);
        Assert.Contains("childIdentityPreserved=true", Detach);
        Assert.Contains("annotationRefresh=", Detach);
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
