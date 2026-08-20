using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCopiedChildRollbackSourceContractTests
{
    private static readonly string Rollback = Read("RoofCopiedChildRollbackService.cs");
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string TimberAnnotations = Read("TimberAnnotationService.cs");

    [Fact]
    public void LockedCopy_UsesRollback_ToRemoveCloneLineAndAnnotations()
    {
        Assert.Contains("TryRollbackCopiedRoofChild", Rehydration);
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", Rollback);
        Assert.Contains("entity.Erase()", Rollback);
        Assert.Contains("locked-copy-erased", Rehydration);
    }

    [Fact]
    public void AttachedManualFailure_RollsBackAndStillCommitsCleanup()
    {
        var process = Segment(Rehydration, "if (!TryPromoteAttachedManualClone", "private static bool IsOwnerUnlocked");
        Assert.Contains("TryRollbackCopiedRoofChild", process);
        Assert.Contains("attached-manual-rollback", process);
        Assert.Contains("return true;", process);
    }

    [Fact]
    public void EnsureAttachedManualPresentation_DoesNotTreatEnsureForElementFalseAsFailure()
    {
        var presentation = Member(Rehydration, "private static bool EnsureAttachedManualPresentation", "private static bool TryOpenGeneratedLine");
        Assert.Contains("_ = TimberAnnotationService.EnsureForElement", presentation);
        Assert.DoesNotContain("return TimberAnnotationService.EnsureForElement", presentation);
        Assert.Contains("return true;", presentation);
    }

    [Fact]
    public void DeleteForSourceHandle_CoversLabelSlopeAndFootprint()
    {
        var method = Member(TimberAnnotations, "public static void DeleteForSourceHandle", "public static void DeleteForMissingSourceHandles");
        Assert.Contains("ElementLabelService.DeleteForSourceHandle", method);
        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle", method);
        Assert.Contains("PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle", method);
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

    private static string Member(string source, string start, string end) => Segment(source, start, end);
}
