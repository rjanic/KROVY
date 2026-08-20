using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSplitEraseSourceContractTests
{
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");

    [Fact]
    public void EraseBranch_ClassifiesAttachedManualBeforeGeneratedKeyRequirement()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        var classifyIndex = erase.IndexOf("TryClassifyErasedCopyAttachedManual", StringComparison.Ordinal);
        var resolveIndex = erase.IndexOf("TryResolveErasedMemberKey", StringComparison.Ordinal);
        Assert.True(classifyIndex >= 0, "AttachedManual classifier not found in ERASE branch.");
        Assert.True(
            resolveIndex > classifyIndex,
            "AttachedManual classifier must run before Generated logical-key resolution.");
    }

    [Fact]
    public void SplitErase_IsPermanentDelete_NoSuppression_NoLogicalKey()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        var deleteBranch = Segment(
            erase,
            "TryClassifyErasedCopyAttachedManual",
            "TryResolveErasedMemberKey");
        Assert.Contains("split-delete", deleteBranch);
        Assert.Contains("DeleteAnnotationsForHandle", deleteBranch);
        Assert.DoesNotContain("Suppress", deleteBranch);
        Assert.DoesNotContain("logical-key-missing", deleteBranch);
    }

    [Fact]
    public void Classifier_AcceptsBothCopyAndSplitOrigin()
    {
        var method = Segment(
            ManualEdit,
            "private static bool TryClassifyErasedCopyAttachedManual",
            "private static bool TryIsLiveTimber");
        Assert.Contains("RoofAttachedManualTimberStore.Read", method);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Copy", method);
        Assert.Contains("Origin != RoofAttachedManualOrigin.Split", method);
        Assert.Contains("TryGetObjectAllowErased", method);
    }

    [Fact]
    public void SplitErase_EmitsAttachedManualEraseTrace()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        Assert.Contains("WriteAttachedManualErase", erase);
        Assert.Contains("ROOF_ATTACHED_MANUAL_ERASE", Diag);
        Assert.Contains("action=permanent-delete", Diag);
    }

    [Fact]
    public void GeneratedErase_SuppressionSemanticsUnchanged()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        Assert.Contains("RoofGeneratedMemberOverride.Suppress", erase);
        Assert.Contains("\"suppress\"", erase);
    }

    [Fact]
    public void CopyErase_StillPermanentDelete()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        Assert.Contains("copy-delete", erase);
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
