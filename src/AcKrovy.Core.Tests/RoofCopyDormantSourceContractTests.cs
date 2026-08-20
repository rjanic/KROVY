using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCopyDormantSourceContractTests
{
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string Policy = Read("RoofSourceResizeChildPolicyService.cs");
    private static readonly string Scanner = Read("DrawingScanner.cs");
    private static readonly string RedoDiag = Read("RoofRedoStateDiag.cs");

    [Fact]
    public void ReplayAnchoredChildren_TracksReplayedDormantReactivated()
    {
        Assert.Contains("RoofCopyReplayResult", Lifecycle);
        Assert.Contains("int Replayed", Lifecycle);
        Assert.Contains("int Dormant", Lifecycle);
        Assert.Contains("int Reactivated", Lifecycle);
    }

    [Fact]
    public void AnchorMissing_MakesCopyChildDormant_NotErased()
    {
        var method = Segment(Lifecycle, "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner", "private static void MakeCopyChildDormant");
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", method);
        Assert.Contains("dormant++", method);
        // Reactivation restores visibility and replays geometry.
        Assert.Contains("var wasDormant = !childLine.Visible", method);
        Assert.Contains("childLine.Visible = true", method);
        Assert.Contains("reactivated++", method);
    }

    [Fact]
    public void MakeCopyChildDormant_HidesGeometryAndRemovesAnnotations()
    {
        var method = Segment(Lifecycle, "private static void MakeCopyChildDormant", "public static void RefreshModifiedAttachedManualRelatives");
        Assert.Contains("childLine.Visible = false", method);
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", method);
        // Entity is NOT erased, so XData survives SAVE/REOPEN.
        Assert.DoesNotContain("childLine.Erase()", method);
    }

    [Fact]
    public void Scanner_SkipsInvisibleTimberElements()
    {
        Assert.Contains("if (!entity.Visible)", Scanner);
    }

    [Fact]
    public void OwnershipInvariant_ExcludesValidAttachedManualChildren()
    {
        var invariant = Segment(RedoDiag, "public static void CaptureOwnershipInvariant", "editor.WriteMessage");
        Assert.Contains("RoofAttachedManualTimberStore.Read(member).Data is not null", invariant);
    }

    [Fact]
    public void PolicySummary_ReportsDormantAndReactivatedCounts()
    {
        Assert.Contains("attachedManualCopyDormant", Policy);
        Assert.Contains("attachedManualCopyReactivated", Policy);
        Assert.Contains("AttachedManualCopyDormant", Policy);
        Assert.Contains("AttachedManualCopyReactivated", Policy);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

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
