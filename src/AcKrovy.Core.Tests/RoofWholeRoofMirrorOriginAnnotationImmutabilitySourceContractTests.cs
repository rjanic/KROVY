using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Whole-roof MIRROR must leave the original owner's annotation identity immutable.
/// AutoCAD may erase+recreate source MLeaders into ObjectAppended; EraseStale must
/// peer-gate consume so sole survivors are kept. New-owner Materialize must use
/// copySourcePreservation so ElementId fallback cannot reclaim original labels.
/// </summary>
public sealed class RoofWholeRoofMirrorOriginAnnotationImmutabilitySourceContractTests
{
    private static readonly string Rebind = ReadInfra("RoofWholeRoofCopyRebindService.cs");
    private static readonly string Live = ReadInfra("LiveGeometrySynchronizationService.cs");
    private static readonly string Materializer = ReadInfra("RoofGeneratedRafterSetService.cs");
    private static readonly string Structural = ReadInfra("RoofAutomaticStructuralRafterMaterializationService.cs");
    private static readonly string Created = ReadInfra("TimberCreatedElementAnnotationService.cs");
    private static readonly string Mirror = ReadInfra("RoofMirrorCloneDetachService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofMirrorAnnotationConsumeRules.cs");

    [Fact]
    public void CoreConsumeRules_Exist()
    {
        Assert.Contains("ShouldEraseAppendedAnnotationClone", Rules);
        Assert.Contains("hasLivingNonAppendedPeerSameRole", Rules);
    }

    [Fact]
    public void EraseStaleAnnotationClones_PeerGatesConsume()
    {
        var helper = Segment(
            Rebind,
            "private static int EraseStaleAnnotationClones(",
            "private static HashSet<string> CollectLivingNonAppendedAnnotationRoles(");
        Assert.Contains("CollectLivingNonAppendedAnnotationRoles(", helper);
        Assert.Contains("ShouldEraseAppendedAnnotationClone(true)", helper);
        Assert.Contains("SelectSoleSurvivorKeepIds(", Rebind);
        Assert.Contains("living-non-appended-peer", Rebind);
        Assert.Contains("sole-appended-survivor", Rebind);
        Assert.Contains("surplus-appended-duplicate", Rebind);
        Assert.Contains("ORIGIN_MIRROR_ANNOTATION_INVARIANT", Rebind);
        Assert.Contains("ROOF_MIRROR_ANNOTATION_CONSUME", Rebind);
    }

    [Fact]
    public void MemberMirrorCleanup_AlsoPeerGatesConsume()
    {
        var helper = Segment(
            Mirror,
            "private static void DeleteMirroredCloneAnnotations(",
            "private static void RefreshClonePresentation(");
        Assert.Contains("ShouldEraseAppendedAnnotationClone(true)", helper);
        Assert.Contains("CollectLivingNonAppendedAnnotationRolesForSource(", helper);
        Assert.Contains("SelectSoleSurvivorKeepIdsForSource(", helper);
    }

    [Fact]
    public void WholeRoofRebind_SuppressesAnnotationAppendQueues()
    {
        var refresh = Segment(
            Live,
            "private void RefreshCandidates(",
            "private static void RefreshTimberElements(");
        var rebindBlock = Segment(
            refresh,
            "if (nativeCopy || nativeMirror)",
            "RoofLiveResizeService.Process(");
        Assert.Contains("_appendedLabelIds.Suppress()", rebindBlock);
        Assert.Contains("_appendedSlopeArrowIds.Suppress()", rebindBlock);
        Assert.Contains("_appendedSlopeAngleTextIds.Suppress()", rebindBlock);
    }

    [Fact]
    public void RebindMaterialize_UsesCopySourcePreservationForAnnotations()
    {
        Assert.Contains("bool copySourcePreservation = false", Created);
        Assert.Contains("copySourcePreservation: copySourcePreservation", Created);
        Assert.Contains("copySourcePreservation: !syncAssemblyGroup", Materializer);
        Assert.Contains("copySourcePreservation: !syncAssemblyGroup", Structural);
    }

    [Fact]
    public void OriginInvariant_ExcludesAppendedMirrorClonesFromBaseline()
    {
        Assert.Contains("SelectOriginAnnotationKeysExcludingAppended(", Rebind);
        Assert.Contains("CaptureOriginAnnotationSnapshot(", Rebind);
        Assert.Contains("appendedAnnotationIds", Segment(
            Rebind,
            "originAnnotationBefore = CaptureOriginAnnotationSnapshot(",
            "var staleAnnotationClonesRemoved = EraseStaleAnnotationClones("));
        Assert.Contains("EmitOriginMirrorAnnotationInvariant(", Rebind);
        Assert.Contains("missingHandles=", Rebind);
        Assert.Contains("changedSourceHandles=", Rebind);
        Assert.Contains("identity={(exactIdentity ? \"preserved\"", Rebind);
        Assert.Contains("result={(ok ? \"ok\" : \"fail\")}", Rebind);
        // Production consume path must remain peer-gated; diagnostic-only change.
        Assert.Contains("ShouldEraseAppendedAnnotationClone(true)", Rebind);
        Assert.Contains("SelectSoleSurvivorKeepIds(", Rebind);
    }

    private static string ReadInfra(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found after start: {end}");
        return source[startIndex..endIndex];
    }
}
