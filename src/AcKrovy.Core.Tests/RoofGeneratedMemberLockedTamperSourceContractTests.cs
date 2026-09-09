using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberLockedTamperSourceContractTests
{
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Manual = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Recovery = Read("RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Snapshot = Read("RoofUnsupportedStretchRecoverySnapshotService.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Rules = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberLockedTamperRules.cs"));
    private static readonly string Persistence = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs"));

    [Fact]
    public void LockedAnnotationTamper_QueuesViaSameGeneratedOnlyRecovery()
    {
        Assert.Contains("LockedAnnotationTamper", Rules);
        Assert.Contains("ShouldDeferUnlockedAnnotationPresentationOnly(", Rules);
        Assert.Contains("ShouldDeferUnlockedAnnotationPresentationOnly(", Resize);
        Assert.Contains("ClassifyModifiedGeneratedChildren(", Resize);
        Assert.Contains("TryResolveAnnotationSourceHandle(", Resize);
        Assert.Contains("ElementLabelStore.TryRead(", Resize);
        Assert.Contains("SlopeArrowStore.TryRead(", Resize);
        Assert.Contains("SlopeAngleTextStore.TryRead(", Resize);
        Assert.Contains("PostFootprintPerpendicularAnnotationStore.TryRead(", Resize);
        Assert.Contains("TryRecoverGeneratedMembersOnly(", Manual);
        Assert.Contains("ROOF_ANNOTATION_OWNER_RESOLUTION", Read("RoofAnnotationOwnerResolutionDiag.cs"));
    }

    [Fact]
    public void ObjectModified_QueuesMLeaderIntoModifiedIdsForInspect()
    {
        var modified = Member(Live, "private void ObjectModified", "private void ObjectErased");
        Assert.Contains("_modifiedFramedLabelIds.TryAdd(entity.ObjectId)", modified);
        Assert.Contains("_modifiedIds.TryAdd(entity.ObjectId)", modified);
        // Historical HOST bug: MLeader early-return left Inspect without the annotation id.
        Assert.DoesNotContain("TraceQueueMLeader(\n                    _document,\n                    entity.ObjectId);\n#endif\n                    return;", NormalizeNewlines(modified));
        Assert.DoesNotContain("TraceQueueMLeader(\r\n                    _document,\r\n                    entity.ObjectId);\r\n#endif\r\n                    return;", modified);
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n");

    [Fact]
    public void LockedRecovery_StripsFramedLabelsFromPersistOffsets()
    {
        Assert.Contains("modifiedFramedLabelIds = modifiedFramedLabelIds", Live);
        Assert.Contains("!roofRelatedIds.Contains(id)", Live);
        Assert.Contains("PersistFramedManualOffsets cannot re-bake", Live);
    }

    [Fact]
    public void ChildOnlyTamper_DoesNotRequireRigidEquivalentWhenSourceUnmodified()
    {
        var inspect = Member(Resize, "private static InspectionPlan Inspect", "private static bool HasErasedGeneratedTimber");
        Assert.Contains("var sourceModified = modifiedIds.Contains(ownerId)", inspect);
        Assert.Contains("RoofSourceChangeKind.SupportedResize", inspect);
        Assert.Contains("!sourceModified", inspect);
    }

    [Fact]
    public void GeneratedOnlyRecovery_AllowsUnchangedSourceEvenIfClassifyIsNotRigid()
    {
        Assert.Contains("TryRecoverGeneratedMembersOnly(", Recovery);
        Assert.Contains("RestoredMatchesSnapshot(", Recovery);
        Assert.Contains("sourceUnchanged", Recovery);
    }

    [Fact]
    public void HipClassify_KeepsRigidEquivalentOnCompactDescriptorMatch()
    {
        var hipClassify = Member(
            Persistence,
            "private static RoofSourceChangeClassification ClassifyHip",
            "private static RoofDefinitionRestoreResult RestoreV2");
        Assert.Contains("RoofSourceChangeKind.RigidEquivalent", hipClassify);
        Assert.DoesNotContain("VertexCount == 4", hipClassify);
        Assert.Contains("HipGeneratedRelativeCoverageMismatch(", Resize);
    }

    [Fact]
    public void AssemblySnapshotStillCapturedForMoveRotateScale()
    {
        Assert.Contains("IsAssemblySnapshotCommand(e.GlobalCommandName)", Live);
        Assert.Contains("CaptureForCommand(", Snapshot);
        Assert.Contains("MOVE", File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs")));
    }

    [Fact]
    public void UndoRedoSuppressionUnchanged()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("return Array.Empty<ObjectId>()", Member(
            Resize,
            "public static IReadOnlyCollection<ObjectId> Process",
            "public static bool TryBeginGroupedUndo"));
    }

    private static string Member(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return source[from..to];
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
