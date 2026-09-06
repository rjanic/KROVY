using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofLiveResizeSourceContractTests
{
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Display = Read("RoofDisplayService.cs");
    private static readonly string Persistence = File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs"));
    private static readonly string Policy = File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.Core", "Services", "Roofs", "RoofSourceChangeEditStatePolicy.cs"));
    private static readonly string Coordinator = File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.Core", "Services", "LiveGeometryRefreshCoordinator.cs"));
    private static readonly string Diagnostics = Read("RoofRedoStateDiag.cs");
    private static readonly string Recovery = Read("RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Snapshot = Read("RoofUnsupportedStretchRecoverySnapshotService.cs");
    private static readonly string RecoveryRules = File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.Core", "Services", "Roofs", "RoofUnsupportedStretchRecoveryRules.cs"));

    [Fact]
    public void ObjectModifiedOnlyQueuesAndCommandEndOwnsTheRefresh()
    {
        var modified = Member(Live, "private void ObjectModified", "private void ObjectErased");
        var ended = Member(Live, "private void CommandEnded", "private void CommandCancelled");
        var refresh = Member(Live, "private void RefreshCandidates", "private static void RefreshTimberElements");

        Assert.Contains("_modifiedIds.TryAdd(entity.ObjectId)", modified);
        Assert.DoesNotContain("RoofLiveResizeService.Process", modified);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", modified);
        Assert.DoesNotContain("StartTransaction", modified);
        Assert.DoesNotContain("LockDocument", modified);
        Assert.Contains("RefreshCandidates(", ended);
        Assert.Contains("RoofLiveResizeService.Process(", refresh);
    }

    [Fact]
    public void PendingOwnersAreDeduplicatedAndCancelledOrFailedCommandsNeverRefresh()
    {
        Assert.Contains("HashSet<TCandidate>", Coordinator);
        Assert.Contains("return _candidates.Add(candidate)", Coordinator);
        var cancelled = Member(Live, "private void CommandCancelled", "private void CommandFailed");
        var failed = Member(Live, "private void CommandFailed", "private void ClearPendingLiveGeometryState");
        Assert.Contains("ClearPendingLiveGeometryState()", cancelled);
        Assert.Contains("ClearPendingLiveGeometryState()", failed);
        Assert.DoesNotContain("RefreshCandidates(", cancelled + failed);
        Assert.DoesNotContain("RoofLiveResizeService.Process(", cancelled + failed);
    }

    [Fact]
    public void OnlyAModifiedPersistedPolylineEntersTheSourceResizeClassification()
    {
        var inspect = Member(Resize, "private static InspectionPlan Inspect", "private static bool HasErasedGeneratedTimber");
        Assert.Contains("entity is not Polyline polyline", inspect);
        Assert.Contains("RoofDefinitionStore.Read(polyline).Data", inspect);
        Assert.Contains("treatHipDisplayDriftAsResize: true", inspect);
        Assert.Contains("RoofDisplayStore.Read(entity).Exists", inspect);
        Assert.Contains("displayTamperCandidates.Add", inspect);
        Assert.DoesNotContain("treatHipDisplayDriftAsResize: true).Kind", Member(
            Resize,
            "private static bool TryApplyDisplayTamper",
            "private static RoofSourceChangeClassification ClassifyOwner"));
    }

    [Fact]
    public void HipUsesCurrentSourceAndStoredSlopeThenUpdatesOnlyAtTheExistingApplyBoundary()
    {
        Assert.Contains("return ClassifyHip(source, footprint, data)", Persistence);
        var hipClassify = Member(Persistence, "private static RoofSourceChangeClassification ClassifyHip", "private static RoofDefinitionRestoreResult RestoreV2");
        Assert.Contains("new RoofParameters(data.Face0SlopeDegrees)", hipClassify);
        Assert.Contains("RoofKind.Hip", hipClassify);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", hipClassify);

        var apply = Member(Resize, "private static ResizeApplyResult TryApplyResize", "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms");
        Assert.Contains("RoofDefinitionPersistence.UpdateGeometry(", apply);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, updated)", apply);
        Assert.Contains("RoofWireframe.Create(", apply);
        Assert.Contains("RoofDisplayService.Rebuild(", apply);
        Assert.Contains("if (!isHip)", apply);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", apply);
        Assert.Contains("RoofSourceResizeChildPolicyService.Apply(", apply);
    }

    [Fact]
    public void CompactDescriptorCollisionFallsBackToFullDisplayValidation()
    {
        var classify = Member(Resize, "private static RoofSourceChangeClassification ClassifyOwner", "private static bool TryInvokeUndoMark");
        Assert.Contains("geometric.Geometry is HipRoofGeometry hipGeometry", classify);
        Assert.Contains("RoofWireframe.BuildGenerationSignature", classify);
        Assert.Contains("RoofDisplayService.Inspect(", classify);
        Assert.Contains("!display.Validation.IsCurrent", classify);
        Assert.Contains("Kind = RoofSourceChangeKind.SupportedResize", classify);
    }

    [Fact]
    public void LockedHipCanRefreshWhileAllExistingNonHipLockRulesRemain()
    {
        Assert.Contains("roofKind == RoofKind.Hip", Policy);
        Assert.Contains(": EffectiveKind(editState, geometricKind)", Policy);
        var classify = Member(Resize, "private static RoofSourceChangeClassification ClassifyOwner", "private static bool TryInvokeUndoMark");
        Assert.Contains("stored.Data.Kind", classify);
        Assert.Contains("stored.Data.EditState", classify);
    }

    [Fact]
    public void HipInvalidFinalGeometryUsesEstablishedAtomicAssemblyRecovery()
    {
        Assert.DoesNotContain("UnsupportedHipOwnerIds", Resize);
        Assert.Contains("TryRecoverUnsupportedOwners(", Resize);
        Assert.Contains("input.Vertices.Count < 3", Snapshot);
        Assert.Contains("snapshot.Vertices.Count < 3", RecoveryRules);
        Assert.Contains("owner.NumberOfVertices == vertices.Count", Recovery);
        Assert.Contains("for (var i = 0; i < vertices.Count; i++)", Recovery);
        Assert.Contains("RoofDisplayService.Rebuild(", Recovery);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", Recovery);
    }

    [Fact]
    public void DisplayRebuildReplacesVariableRoleSetsAndKeepsOneCanonicalGroup()
    {
        Assert.Contains("CollectDisplayIdsToErase", Display);
        Assert.Contains("DetachMembersBeforeErase", Display);
        Assert.Contains("child.Erase()", Display);
        Assert.Contains("new List<ObjectId>(expectedEdges.Count)", Display);
        Assert.Contains("foreach (var edge in expectedEdges", Display);
        Assert.Contains("RoofDisplayGroupService.EnsureGroup(", Display);
        Assert.Contains("DissociateOwnerFromForeignGroups", File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs")));
    }

    [Fact]
    public void UndoRedoGuardStillPrecedesEveryDatabaseWrite()
    {
        var ended = Member(Live, "private void CommandEnded", "private void CommandCancelled");
        var ignored = Member(ended, "if (shouldIgnore)", "_ignoreCurrentCommand = false");
        Assert.Contains("isUndoRedo", ignored);
        Assert.Contains("ClearPendingLiveGeometryState()", ignored);
        Assert.DoesNotContain("StartTransaction(", ignored);
        Assert.DoesNotContain("LockDocument(", ignored);
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("return Array.Empty<ObjectId>()", Resize);
    }

    [Fact]
    public void DebugEvidenceIsCompactAndExcludedFromRelease()
    {
        Assert.Contains("#if DEBUG", Diagnostics);
        Assert.Contains("ROOF_HIP_LIVE_RESIZE", Diagnostics);
        Assert.Contains("vertices=", Diagnostics);
        Assert.Contains("ridge=", Diagnostics);
        Assert.Contains("hip=", Diagnostics);
        Assert.Contains("valley=", Diagnostics);
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", file));

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token not found: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
