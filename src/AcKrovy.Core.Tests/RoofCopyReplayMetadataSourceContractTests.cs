using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCopyReplayMetadataSourceContractTests
{
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");

    [Fact]
    public void CopyRehydration_NeverWritesMalformedV1Child()
    {
        // A COPY clone must never become a v1 AttachedManual record (no anchor /
        // RelativeSegment), which replay would silently skip.
        Assert.DoesNotContain("new RoofAttachedManualTimberData(", Rehydration);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", Rehydration);
    }

    [Fact]
    public void CopyRehydration_Fallback_UsesOriginCopyAnchor_OrDetaches()
    {
        // When the association plan cannot resolve the source anchor, the fallback still
        // produces a proper Origin.Copy (via TryResolveCopyCloneAnchor) or detaches the
        // clone as generic timber — never a malformed child.
        Assert.Contains("TryResolveCopyCloneAnchor", Rehydration);
        Assert.Contains("RoofAttachedManualOrigin.Copy);", Rehydration);
        Assert.Contains("no-compatible-anchor", Rehydration);
    }

    [Fact]
    public void CopyRehydration_Fallback_ReusesProvenReanchorRule()
    {
        // The fallback resolves a compatible live Generated anchor via the existing
        // SelectNearestAnchor rule (no new anchor engine).
        Assert.Contains("SelectNearestAnchor", Rehydration);
        Assert.Contains("RoofAttachedManualReanchorRules.SelectNearestAnchor", Rehydration);
    }

    [Fact]
    public void Replay_NeverSilentlySkipsRecognizedOriginChild()
    {
        // ReplayAnchoredChildrenForOwner must not silently drop a recognized-origin child
        // that is missing anchor/relative — it is made dormant (hidden) and counted.
        var replay = Member(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static void MakeCopyChildDormant");
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", replay);
        Assert.Contains("dormant++;", replay);
        Assert.True(
            replay.IndexOf("AnchorGeneratedMemberKey is null", StringComparison.Ordinal) >
            replay.IndexOf("originFilter is not null", StringComparison.Ordinal),
            "origin filter must be evaluated before the missing-anchor dormancy fallback");
    }

    [Fact]
    public void Replay_DormancyRetainsMetadata_RemovesAnnotation()
    {
        var dormant = Member(
            Lifecycle,
            "private static void MakeCopyChildDormant",
            "public static void RefreshModifiedAttachedManualRelatives");
        Assert.Contains("childLine.Visible = false;", dormant);
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", dormant);
        Assert.DoesNotContain("childLine.Erase()", dormant);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            fileName));

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

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
