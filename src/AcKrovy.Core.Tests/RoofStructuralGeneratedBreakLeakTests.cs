using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// BREAK of StructuralGenerated Hip/Valley clones a Line that inherits structural
/// metadata. Recovery must restore snapshot geometry AND erase unsnapshot clones so
/// structural count / GROUP membership match the pre-command set exactly.
/// </summary>
public sealed class RoofStructuralGeneratedBreakLeakTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void LockedAndUnlockedRecoveryEraseUnsnapshotStructuralDuplicates()
    {
        var recovery = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoveryService.cs");

        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", recovery);
        Assert.Contains("TryEraseUnsnapshotGeneratedDuplicates", recovery);

        // Locked generated-only recovery and Hip/Valley-only restore both erase extras.
        Assert.Contains("TryRecoverGeneratedMembersOnly", recovery);
        var generatedOnly = Segment(
            recovery,
            "public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly",
            "public static bool TryNormalizeRigidTranslation");
        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", generatedOnly);

        var hipValleyOnly = Segment(
            recovery,
            "public static bool TryRestoreStructuralHipValleyMembersOnly",
            "public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly");
        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", hipValleyOnly);

        var unerase = Segment(
            recovery,
            "public static bool TryUnEraseAndRestore",
            "private static bool TryProbeAssemblyMembers");
        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", unerase);
    }

    [Fact]
    public void UnsnapshotStructuralEraseUsesOwnerMetadataAndSnapshotHandlesNotLayer()
    {
        var recovery = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoveryService.cs");
        var erase = Segment(
            recovery,
            "private static bool TryEraseUnsnapshotStructuralDuplicates",
            "private static bool TryRestoreAnnotations");

        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", erase);
        Assert.Contains("RoofStructuralGeneratedLockRules.IsLockProtectedRole", erase);
        Assert.Contains("RoofOwnerReference", erase);
        Assert.Contains("snapshotHandles.Contains", erase);
        Assert.Contains("line.Erase(true)", erase);
        Assert.Contains("ElementLabelService.DeleteForSourceHandle", erase);
        Assert.DoesNotContain("Layer", erase);
        Assert.DoesNotContain("Color", erase);
        Assert.DoesNotContain("Linetype", erase);
    }

    [Fact]
    public void BreakIsInGeneratedEditVocabularyAndUsesSnapshotRestore()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsBreakCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("BREAK"));

        var manual = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("TryRecoverGeneratedMembersOnly", manual);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", manual);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", manual);
    }

    [Fact]
    public void OrdinaryGeneratedUnsnapshotErasePathRemainsIntact()
    {
        var recovery = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoveryService.cs");
        var generatedErase = Segment(
            recovery,
            "private static bool TryEraseUnsnapshotGeneratedDuplicates",
            "private static bool TryEraseUnsnapshotStructuralDuplicates");

        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", generatedErase);
        Assert.Contains("line.Erase(true)", generatedErase);
        Assert.DoesNotContain("RoofStructuralGeneratedStore", generatedErase);
    }

    [Fact]
    public void SnapshotStillCapturesStructuralMembersForExactMembership()
    {
        var snapshot = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoverySnapshotService.cs");
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", snapshot);
        Assert.Contains("structural-generated-metadata-mismatch", snapshot);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, true)]
    [InlineData(RoofStructuralRole.Valley, true)]
    [InlineData(RoofStructuralRole.Ridge, false)]
    public void OnlyHipAndValleyAreLockProtectedStructuralRoles(
        RoofStructuralRole role,
        bool expected)
    {
        Assert.Equal(expected, RoofStructuralGeneratedLockRules.IsLockProtectedRole(role));
    }

    [Fact]
    public void VersionAndSchemasRemainUnchanged()
    {
        Assert.Equal(1, RoofStructuralGeneratedDataSchema.CurrentVersion);
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", Read("Directory.Build.props"));
    }

    private static string Segment(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker after start: {endMarker}");
        return source[start..end];
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
