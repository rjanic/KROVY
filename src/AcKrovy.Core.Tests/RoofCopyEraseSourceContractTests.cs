using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCopyEraseSourceContractTests
{
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");

    [Fact]
    public void EraseBranch_ClassifiesCopyAttachedManualBeforeGeneratedKeyRequirement()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        var classifyIndex = erase.IndexOf("TryClassifyErasedCopyAttachedManual", StringComparison.Ordinal);
        var resolveIndex = erase.IndexOf("TryResolveErasedMemberKey", StringComparison.Ordinal);
        Assert.True(classifyIndex >= 0, "Copy classifier not found in ERASE branch.");
        Assert.True(resolveIndex > classifyIndex, "Copy classifier must run before Generated logical-key resolution.");
    }

    [Fact]
    public void CopyErase_IsPermanentDelete_WithoutSuppressionOverride()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        Assert.Contains("copy-delete", erase);
        Assert.Contains("DeleteAnnotationsForHandle", erase);
        // The COPY delete branch must not create a suppression override.
        var copyBranch = Segment(erase, "TryClassifyErasedCopyAttachedManual", "TryResolveErasedMemberKey");
        Assert.DoesNotContain("Suppress", copyBranch);
        Assert.Contains("DeleteAnnotationsForHandle", copyBranch);
    }

    [Fact]
    public void GeneratedErase_SuppressionSemanticsUnchanged()
    {
        var erase = Segment(ManualEdit, "if (isErase)", "else");
        Assert.Contains("RoofGeneratedMemberOverride.Suppress", erase);
        Assert.Contains("\"suppress\"", erase);
    }

    [Fact]
    public void Classifier_AcceptsCopyAndSplitOrigin()
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
