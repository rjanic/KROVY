using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSourceResizeChildPolicySourceContractTests
{
    private static readonly string LiveResize = Read("RoofLiveResizeService.cs");
    private static readonly string ChildPolicy = Read("RoofSourceResizeChildPolicyService.cs");
    private static readonly string RafterSet = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");

    [Fact]
    public void SupportedResize_ForcesGeneratedRegeneration()
    {
        Assert.Contains("forceRegenerateOnSourceResize: true", LiveResize);
        Assert.Contains("bool forceRegenerateOnSourceResize = false", RafterSet);
    }

    [Fact]
    public void SupportedResize_ReplaysCopyAndSplitChildren_WithDormancyNotDeletion()
    {
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", ChildPolicy);
        Assert.Contains("keep-in-place", ChildPolicy);
        Assert.Contains("delete-outside", ChildPolicy);
        Assert.Contains("ROOF_ATTACHED_MANUAL_RESIZE_POLICY", ChildPolicy);
        Assert.Contains("ROOF_ATTACHED_MANUAL_REMOVED", ChildPolicy);
        Assert.Contains("ROOF_SOURCE_RESIZE_CHILD_POLICY", ChildPolicy);
        Assert.Contains("RoofSourceResizeChildPolicyService.Apply", LiveResize);
        // COPY- and Split-origin children are anchor-replayed against their rebuilt
        // Generated anchor; both go dormant (never permanently deleted) when the exact
        // anchor temporarily disappears. Distinct Origin semantics, shared anchored
        // replay/dormancy lifecycle.
        Assert.Contains("ReplayAnchoredChildrenForOwner", ChildPolicy);
        Assert.Contains("RoofAttachedManualOrigin.Copy", ChildPolicy);
        Assert.Contains("RoofAttachedManualOrigin.Split", ChildPolicy);
        Assert.Contains("originFilter", ChildPolicy);
        Assert.DoesNotContain("TryMapSegment", ChildPolicy);
    }

    [Fact]
    public void Split_IsAnchorReplayed_OnSurvivingAnchor()
    {
        // Split/BREAK fragments are replayed against their exact rebuilt Generated anchor.
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", ChildPolicy);
        Assert.Contains("ReplayAnchoredChildrenForOwner", ChildPolicy);
    }

    [Fact]
    public void Split_ExcludedFromKeepDeletePolicy_SoTemporaryShrinkIsNotDeletion()
    {
        // Split must NOT be permanently deleted merely because the shrunk footprint no
        // longer contains it; it is handled by anchored dormancy instead.
        Assert.Contains("origin == RoofAttachedManualOrigin.Split", ChildPolicy);
        Assert.Contains("origin == RoofAttachedManualOrigin.Copy ||", ChildPolicy);
    }

    [Fact]
    public void Split_NoNearestStationRemap_DuringResize()
    {
        // Replay uses the EXACT persisted anchor (TryFindGeneratedAnchorLine), never a
        // nearest-station remap, during roof resize.
        Assert.DoesNotContain("SelectNearestAnchor", ChildPolicy);
    }

    [Fact]
    public void Split_Diagnostics_ReportedSeparately()
    {
        Assert.Contains("attachedManualSplitReplayed", ChildPolicy);
        Assert.Contains("attachedManualSplitDormant", ChildPolicy);
        Assert.Contains("attachedManualSplitReactivated", ChildPolicy);
    }

    [Fact]
    public void IncidentalChildStretch_SuppressedWhenSourceSupportedResize()
    {
        Assert.Contains("ShouldSuppressIncidentalChildManualStretch", LiveResize);
        Assert.Contains("ShouldSuppressIncidentalChildManualStretch", ManualEdit);
        Assert.Contains("SourceSupportedResizeOwnersThisCommand", LiveResize);
    }

    [Fact]
    public void AttachedManual_KeepDeleteEvaluatedFromCommandSnapshot_NotGeneratedRecipe()
    {
        Assert.Contains("RoofAttachedManualTimberStore.FindByOwner", ChildPolicy);
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.TryGet", ChildPolicy);
        Assert.DoesNotContain("RoofAttachedManualTimberStore", RafterSet);
    }

    [Fact]
    public void Deletion_RemovesAnnotationsMetadataAndGroupMembership()
    {
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", ChildPolicy);
        Assert.Contains("RoofAssemblyGroupSyncService.DetachMembersBeforeErase", ChildPolicy);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", ChildPolicy);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.TryClear", ChildPolicy);
    }

    [Fact]
    public void Deletion_DetachesFromGroupBeforeErase()
    {
        var policy = Segment(
            ChildPolicy,
            "private static (int Kept, int Deleted) ApplyAttachedManualResizePolicy",
            "return (kept, deleted);");
        var detachIndex = policy.IndexOf("DetachMembersBeforeErase", StringComparison.Ordinal);
        var eraseIndex = policy.IndexOf("line.Erase()", StringComparison.Ordinal);
        Assert.True(detachIndex >= 0, "DetachMembersBeforeErase call not found.");
        Assert.True(eraseIndex > detachIndex, "line.Erase() must run after group detach.");
    }

    [Fact]
    public void GeneratedReplacement_DetachesFromGroupBeforeErase()
    {
        var detachIndex = RafterSet.IndexOf("DetachMembersBeforeErase", StringComparison.Ordinal);
        var eraseIndex = RafterSet.IndexOf("entity.Erase()", StringComparison.Ordinal);
        Assert.True(detachIndex >= 0, "DetachMembersBeforeErase call not found in EraseGeneratedSet.");
        Assert.True(eraseIndex > detachIndex, "entity.Erase() must run after group detach.");
    }

    [Fact]
    public void UndoGuard_SkipsWriteOnUndoRedo()
    {
        Assert.Contains("RoofUndoGuardDiag.Write", LiveResize);
        Assert.Contains("ROOF_UNDO_GUARD", LiveResize);
        Assert.Contains("action=skip-write", LiveResize);
    }

    [Fact]
    public void CoreRules_DistinguishStretchFromRigidMoveRotate()
    {
        Assert.True(RoofSourceResizeChildPolicyRules.ShouldIgnoreIncidentalChildStretchOnSourceResize(
            true,
            "STRETCH"));
        Assert.False(RoofSourceResizeChildPolicyRules.ShouldIgnoreIncidentalChildStretchOnSourceResize(
            true,
            "MOVE"));
        Assert.False(RoofSourceResizeChildPolicyRules.ShouldIgnoreIncidentalChildStretchOnSourceResize(
            false,
            "STRETCH"));
    }

    [Fact]
    public void RigidTransform_NotBlockedByStretchSuppression()
    {
        Assert.Contains("isRigidRoofTransform", ManualEdit);
        Assert.Contains("MOVE", ManualEdit);
        Assert.Contains("ROTATE", ManualEdit);
    }

    private static string Read(string fileName)
    {
        if (fileName.Contains("PlacementRules", StringComparison.Ordinal))
        {
            return File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "src",
                "AcKrovy.Core",
                "Services",
                "Roofs",
                fileName));
        }

        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));
    }

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

    private static string Member(string source, string start, string end) => Segment(source, start, end);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
