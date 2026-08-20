using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedCopyAttachedManualSourceContractTests
{
    private static readonly string Rehydration = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string AttachedStore = Read("RoofAttachedManualTimberStore.cs");
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");
    private static readonly string LiveResize = Read("RoofLiveResizeService.cs");
    private static readonly string Snapshot = Read("RoofUnsupportedStretchRecoverySnapshotService.cs");

    [Fact]
    public void UnlockedCopy_PromotesAttachedManual_AndClearsGeneratedOnly()
    {
        Assert.Contains("TryPromoteAttachedManualClone", Rehydration);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", Rehydration);
        Assert.Contains("RoofGeneratedTimberStore.TryClear(", Rehydration);
        Assert.Contains("detach-to-attached-manual", Rehydration);
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_ATTACHED_MANUAL", AttachedStore);
    }

    [Fact]
    public void UnlockedCopy_MarksCopyOrigin_SoCloneFollowsAnchorOnResize()
    {
        Assert.Contains("RoofAttachedManualOrigin.Copy", Rehydration);
    }

    [Fact]
    public void LockedCopy_ErasesClone_AndShowsLockedNotification()
    {
        Assert.Contains("TryEraseLockedCopyClone", Rehydration);
        Assert.Contains("RoofCopiedChildRollbackService.TryRollbackCopiedRoofChild", Rehydration);
        Assert.Contains("IsOwnerUnlocked", Rehydration);
        Assert.Contains("Command_Roof_LockedNotificationTitle", Rehydration);
        Assert.Contains("locked-copy-erased", Rehydration);
    }

    [Fact]
    public void GeneratedRecipeScan_IgnoresAttachedManualStore()
    {
        Assert.Contains("public static IReadOnlyList<ObjectId> FindByOwner", GeneratedStore);
        Assert.DoesNotContain("RoofAttachedManualTimberStore", GeneratedStore);
    }

    [Fact]
    public void LockProtection_ResolvesAttachedManualOwner()
    {
        Assert.Contains("RoofAttachedManualTimberStore.Read(entity)", LiveResize);
        Assert.Contains("RoofAttachedManualTimberStore.Read(selected)", Read("RoofOwnerSelectionResolver.cs"));
    }

    [Fact]
    public void RecoverySnapshot_IncludesAttachedManualTimberLines()
    {
        Assert.Contains("RoofAttachedManualTimberStore.FindByOwner", Snapshot);
    }

    [Fact]
    public void CopyInvariant_ReportsAttachedManualSeparatelyFromGeneratedRecipe()
    {
        Assert.Contains("generatedExpected=", Read("RoofGeneratedCopyLifecycleDiag.cs"));
        Assert.Contains("attachedManual=", Read("RoofGeneratedCopyLifecycleDiag.cs"));
        Assert.Contains("VerifyPostCopyInvariants", Rehydration);
    }

    [Fact]
    public void PostCopyVerify_RunsBeforeCommit()
    {
        var process = Segment(Rehydration, "if (wrote)", "catch (System.Exception");
        var verifyIndex = process.IndexOf("VerifyPostCopyInvariants", StringComparison.Ordinal);
        var commitIndex = process.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(verifyIndex >= 0 && commitIndex > verifyIndex);
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
