using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Focused source-contract coverage for the PRODUCT RULE:
/// AutoCAD MIRROR Yes of an AttachedManual Origin.Split fragment uses the SAME in-place
/// callback form as Origin.Copy (appended=0 / erased=0 / modified=1). Because MIRROR Yes
/// means "erase original, keep mirrored copy", the surviving result must become
/// AttachedManual Origin.Copy — the same ownership model as the MIRROR No clone. ObjectId
/// / handle / ChildIdentity / owner / Role are preserved (same entity mutated in place),
/// while Origin, anchor, RelativeSegment and canonical annotations are rebuilt from FINAL
/// mirrored geometry. No Generated suppression, no extra child.
/// </summary>
public sealed class RoofMirrorYesSplitInPlaceSourceContractTests
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

    // 1-2: Split MIRROR Yes accepts modified-only event; appended=0/erased=0/modified=1.
    [Fact]
    public void SplitMirrorYes_AcceptsModifiedOnlyEvent()
    {
        Assert.Contains("foreach (var id in mirrorModifiedTimberIds)", InPlaceLoop);
        Assert.Contains("TryReanchorInPlaceAttachedManual", InPlaceLoop);
        Assert.Contains("mirrorModifiedTimberIds.Count == 0", Detach);
        Assert.Contains("mirrorModifiedTimberIds = ids.Where", LiveSync);
    }

    // 3-6: same ObjectId/handle/ChildIdentity; role remains AttachedManual.
    [Fact]
    public void SplitMirrorYes_PreservesSameEntity_NoNewChild()
    {
        Assert.DoesNotContain("AddNewlyCreatedDBObject", AttachedMethod);
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
        Assert.Contains("cloneLine.Handle.ToString()", Detach);
    }

    // 7: Origin changes Split -> Copy (guard accepts Split; promote writes Copy).
    [Fact]
    public void SplitMirrorYes_GuardAcceptsBothOrigins_PromotesToCopy()
    {
        Assert.Contains("attached.Data.Origin != RoofAttachedManualOrigin.Copy &&", AttachedMethod);
        Assert.Contains("attached.Data.Origin != RoofAttachedManualOrigin.Split", AttachedMethod);
        Assert.Contains("originBefore = attached.Data.Origin", AttachedMethod);
        Assert.Contains("originAfter = RoofAttachedManualOrigin.Copy", AttachedMethod);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Detach);
    }

    // 8-10: no Generated metadata, no suppression, count unchanged.
    [Fact]
    public void SplitMirrorYes_NoGeneratedMetadata_NoSuppression_CountUnchanged()
    {
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", AttachedMethod);
        Assert.DoesNotContain("TryWriteSuppressOverride", AttachedMethod);
        Assert.DoesNotContain("RoofGeneratedMemberOverride.Suppress", AttachedMethod);
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
    }

    // 11-13: FINAL mirrored WCS geometry drives anchor + RelativeSegment.
    [Fact]
    public void SplitMirrorYes_UsesFinalMirroredWcsGeometry_AndRecomputesAnchor()
    {
        Assert.Contains("cloneLine.StartPoint", Detach);
        Assert.Contains("cloneLine.EndPoint", Detach);
        Assert.Contains("SelectNearestMirrorAnchor", Detach);
        Assert.Contains("CreateAnchoredData(", Detach);
        Assert.Contains("WriteAnchored", Detach);
        Assert.Contains("newAnchorKey = rewritten.Data?.AnchorGeneratedMemberKey", AttachedMethod);
    }

    // 14-16: old Split annotation refreshed; one canonical set; canonical orientation.
    [Fact]
    public void SplitMirrorYes_RefreshesCanonicalAnnotation()
    {
        Assert.Contains("RefreshClonePresentation(document, transaction, id)", InPlaceLoop);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Detach);
        Assert.DoesNotContain("DistanceTo", AttachedMethod);
        Assert.DoesNotContain("GetBoundingBox", AttachedMethod);
    }

    // 17-19: result is Origin.Copy, so existing Copy lifecycle applies unchanged.
    [Fact]
    public void PostMirror_CopyLifecycleApplies()
    {
        // source resize containment/dormancy/reactivation (both origins).
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", Lifecycle);
        Assert.Contains("MakeCopyChildDormant", Lifecycle);
        // MOVE nearest-reanchor is Copy-only (Split child becomes Copy, so it qualifies).
        Assert.Contains("Origin == RoofAttachedManualOrigin.Copy", Lifecycle);
        // ERASE permanent Copy deletion.
        Assert.Contains("copy-delete", ManualEdit);
    }

    // 20: zero-DB U/REDO unchanged.
    [Fact]
    public void ZeroDbUndoRedo_Unchanged()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        Assert.Contains("RoofUndoGuardDiag.Write", resize);
        Assert.Contains("action=skip-write", resize);
    }

    // 21: existing Origin.Copy MIRROR Yes unchanged (same handler, Copy -> Copy no-op).
    [Fact]
    public void CopyMirrorYes_Unchanged()
    {
        Assert.Contains("originAfter = RoofAttachedManualOrigin.Copy", AttachedMethod);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Detach);
        Assert.Contains("WriteInPlaceMirrorYesAttachedTrace", Detach);
    }

    // 22: Generated MIRROR Yes unchanged.
    [Fact]
    public void GeneratedMirrorYes_Unchanged()
    {
        Assert.Contains("TryConvertInPlaceToAttachedManual", Detach);
        Assert.Contains("WriteInPlaceMirrorYesTrace", Detach);
        Assert.Contains("sourceRole=Generated", Detach);
    }

    // 23: Split MIRROR No -> Copy unchanged (clone branch accepts both origins).
    [Fact]
    public void SplitMirrorNo_Unchanged()
    {
        var cloneLoop = Segment(Detach, "foreach (var id in appendedTimberIds)", "// MIRROR Yes (Generated)");
        Assert.Contains("sourceOrigin != RoofAttachedManualOrigin.Copy &&", cloneLoop);
        Assert.Contains("sourceOrigin != RoofAttachedManualOrigin.Split", cloneLoop);
        Assert.Contains("isSplitSource", cloneLoop);
    }

    // 24: T2 unchanged (no T2 entity creation in the in-place path).
    [Fact]
    public void T2Order_Unchanged()
    {
        Assert.DoesNotContain("AppendEntity", AttachedMethod);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", AttachedMethod);
    }

    // Diagnostic: originBefore/originAfter transitions + result=ok on success.
    [Fact]
    public void SplitMirrorYes_EmitsOriginTransitionDiagnostic()
    {
        Assert.Contains("ROOF_MIRROR_YES_ATTACHED", Detach);
        Assert.Contains("originBefore={originBefore}", Detach);
        Assert.Contains("originAfter={originAfter}", Detach);
        Assert.Contains("childIdentityPreserved=true", Detach);
        Assert.Contains("result = \"ok\";", AttachedMethod);
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
