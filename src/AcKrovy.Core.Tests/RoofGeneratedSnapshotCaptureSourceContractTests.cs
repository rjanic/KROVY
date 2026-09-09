using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedSnapshotCaptureSourceContractTests
{
    private static readonly string Snapshot = Read("RoofUnsupportedStretchRecoverySnapshotService.cs");
    private static readonly string Recovery = Read("RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Diag = Read("RoofGeneratedSnapshotDiag.cs");
    private static readonly string Rules = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofAssemblySnapshotCaptureRules.cs"));
    private static readonly string Codec = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionDataCodec.cs"));
    private static readonly string Resize = Read("RoofLiveResizeService.cs");

    [Fact]
    public void SnapshotCapture_UsesSharedEffectiveClosedEvaluation()
    {
        Assert.Contains("RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(", Snapshot);
        Assert.Contains("RoofFootprintValidator.IsEffectivelyClosed", Rules);
        Assert.DoesNotContain(
            "!input.IsClosed",
            Member(Snapshot, "private static bool TryBuildRoofSourceSnapshot", "private static bool TryBuildAssembly"));
        Assert.Contains("RoofSourceChangeKind.SupportedResize", Rules);
        Assert.DoesNotContain(
            "classification.Kind != RoofSourceChangeKind.RigidEquivalent",
            Member(Snapshot, "private static bool TryBuildRoofSourceSnapshot", "private static bool TryBuildAssembly"));
        Assert.Contains("ROOF_GENERATED_SNAPSHOT", Diag);
        Assert.Contains("ROOF_GENERATED_SNAPSHOT_LOOKUP", Diag);
    }

    [Fact]
    public void GeneratedOnlyRecovery_LooksUpByObjectIdThenHandle()
    {
        Assert.Contains("TryGet(ownerId, out var entry)", Recovery);
        Assert.Contains("TryGetByHandle(", Recovery);
        Assert.Contains("WriteLookup(", Recovery);
    }

    [Fact]
    public void SourceResizeStillOwnsWhenSourceModified()
    {
        Assert.Contains("resizeOwners.Contains(ownerId)", Resize);
        Assert.Contains("GeneratedMemberTamperOwnerIds", Resize);
        Assert.Contains("var sourceModified = modifiedIds.Contains(ownerId)", Resize);
    }

    [Fact]
    public void HipUnlocked_NoLongerRejectedByCodecDefaultEditStateGate()
    {
        Assert.DoesNotContain(
            "data.Kind == RoofKind.Hip && !HasDefaultEditState(data)",
            Codec);
        Assert.Contains("EditState is not (RoofEditState.Locked or RoofEditState.Unlocked)", Codec);
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
