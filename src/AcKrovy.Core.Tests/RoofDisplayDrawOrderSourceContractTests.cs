using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayDrawOrderSourceContractTests
{
    private static readonly string Display = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    [Theory]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    public void SourceResizeCommandsReorderDisplayAfterRegeneration(string commandName)
    {
        Assert.Contains($"normalized.Equals(\"{commandName}\"", CommandRules);

        var commandEnded = RoofUxSourceContractText.Member(
            Live,
            "private void CommandEnded",
            "private void CommandCancelled");
        Assert.Contains("RefreshCandidates(", commandEnded);

        var resize = RoofUxSourceContractText.Member(
            Resize,
            "private static ResizeApplyResult TryApplyResize",
            "private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms");
        Assert.True(
            resize.IndexOf("RoofDisplayService.Rebuild(", StringComparison.Ordinal) <
            resize.IndexOf("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", StringComparison.Ordinal));
        Assert.True(
            resize.IndexOf("RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(", StringComparison.Ordinal) <
            resize.IndexOf("RoofDisplayService.EnsureAllDisplayBehindTimber(", StringComparison.Ordinal));
        Assert.Contains("RoofLiveResizeService.Process(", Live);
        Assert.Contains("TryApplyResize(document, transaction, ownerId, globalCommandName)", Resize);
    }

    [Fact]
    public void DrawOrderHelperMovesOnlyRoofDisplayLinesToBottom()
    {
        var helper = RoofUxSourceContractText.Member(
            Display,
            "public static void EnsureAllDisplayBehindTimber",
            "private static List<ObjectId> CollectDisplayIdsToErase(");

        Assert.Contains("record.Observation.IsNativeLine", helper);
        Assert.Contains("record.Id", helper);
        Assert.Contains("DrawOrderTable", helper);
        Assert.Contains("modelSpace.DrawOrderTableId", helper);
        Assert.Contains("drawOrderTable.MoveToBottom(displayIds)", helper);
        Assert.DoesNotContain("MoveToTop", helper);
        Assert.DoesNotContain("RoofStructuralGeneratedStore", helper);
        Assert.DoesNotContain("Annotation", helper, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrawOrderOperationDoesNotMoveOrRewriteTimberAnnotationsOrGeometry()
    {
        var helper = RoofUxSourceContractText.Member(
            Display,
            "public static void EnsureAllDisplayBehindTimber",
            "private static List<ObjectId> CollectDisplayIdsToErase(");

        Assert.DoesNotContain("RoofDisplayStore.Write", helper);
        Assert.DoesNotContain("TransformBy", helper);
        Assert.DoesNotContain("StartPoint =", helper);
        Assert.DoesNotContain("EndPoint =", helper);
        Assert.DoesNotContain("AppendEntity", helper);
        Assert.DoesNotContain("Erase", helper);
    }

    [Fact]
    public void RepeatedRebuildReusesCurrentDisplayOrErasesOldSetBeforeReplacement()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "public static void EnsureAllDisplayBehindTimber");
        var currentBranch = RoofUxSourceContractText.Member(
            rebuild,
            "if (inspection.Validation.IsCurrent)",
            "var eraseIds = CollectDisplayIdsToErase(");
        var replacementBranch = RoofUxSourceContractText.Member(
            rebuild,
            "var eraseIds = CollectDisplayIdsToErase(",
            "return true;");

        Assert.Contains("EnsureAllDisplayBehindTimber(database, transaction)", currentBranch);
        Assert.Contains("child.Erase()", replacementBranch);
        Assert.Contains("new List<ObjectId>(expectedEdges.Count)", replacementBranch);
        Assert.Contains("foreach (var edge in expectedEdges", replacementBranch);
        Assert.Contains("EnsureAllDisplayBehindTimber(database, transaction)", replacementBranch);
    }

    [Fact]
    public void GroupAndMetadataContractsRemainInTheirExistingDisplayLifecycle()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "public static void EnsureAllDisplayBehindTimber");
        var helper = RoofUxSourceContractText.Member(
            Display,
            "public static void EnsureAllDisplayBehindTimber",
            "private static List<ObjectId> CollectDisplayIdsToErase(");

        Assert.Contains("RoofDisplayGroupService.EnsureGroup(", rebuild);
        Assert.Contains("RoofDisplayStore.Write(", rebuild);
        Assert.DoesNotContain("RoofDisplayGroupService", helper);
        Assert.DoesNotContain("RoofDisplayStore.Write", helper);
        Assert.DoesNotContain("RoofDisplayDataSchema", helper);
    }
}
