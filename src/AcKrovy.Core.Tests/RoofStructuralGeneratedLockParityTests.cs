using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralGeneratedLockParityTests
{
    private static readonly string Root = FindRoot();

    [Theory]
    [InlineData(RoofStructuralRole.Hip, true)]
    [InlineData(RoofStructuralRole.Valley, true)]
    [InlineData(RoofStructuralRole.Ridge, false)]
    public void LockProtectedRolesMatchHipAndValleyOnly(
        RoofStructuralRole role,
        bool expected)
    {
        Assert.Equal(expected, RoofStructuralGeneratedLockRules.IsLockProtectedRole(role));
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("ROTATE")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("BREAK")]
    [InlineData("STRETCH")]
    [InlineData("ERASE")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("SCALE")]
    public void OrdinaryGeneratedEditVocabularyStillOwnsCommandSet(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(command));
    }

    [Fact]
    public void LiveResizeResolvesStructuralOwnersAndClassifiesModifications()
    {
        var live = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");

        Assert.Contains("RoofStructuralGeneratedStore.Read(entity)", live);
        Assert.Contains("RoofStructuralGeneratedLockRules.IsLockProtectedRole", live);
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", live);
        Assert.Contains("sourceStructural", live);
    }

    [Fact]
    public void RecoverySnapshotCapturesStructuralHipValleyWithOrdinaryTimber()
    {
        var snapshot = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoverySnapshotService.cs");
        var recovery = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofUnsupportedStretchRecoveryService.cs");
        var manual = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedMemberManualEditService.cs");

        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner", snapshot);
        Assert.Contains("structural-generated-metadata-mismatch", snapshot);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", recovery);
        Assert.Contains("allowErased: true", recovery);
        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", recovery);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", manual);
        Assert.Contains("TryIsLockProtectedStructuralTimber", manual);
        Assert.Contains("ROOF_STRUCT_EDIT_GUARD", manual);
        Assert.Contains("locked-generated-members-only", manual);
        Assert.Contains("structural-hip-valley-after-unlocked-accept", manual);
    }

    [Fact]
    public void ErasePreCommandMapIncludesStructuralHipValleyAsGeneratedTimber()
    {
        var erase = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofDisplayErasePreCommandMapService.cs");

        Assert.Contains("RoofStructuralGeneratedStore.Read(line)", erase);
        Assert.Contains("RoofEraseMappedKind.GeneratedTimber", erase);
        Assert.Contains("RoofStructuralGeneratedLockRules.IsLockProtectedRole", erase);
    }

    [Fact]
    public void OrdinaryGeneratedLockPathStillOwnsProcessOwnersRecovery()
    {
        var live = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");
        var manual = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedMemberManualEditService.cs");

        Assert.Contains("RoofGeneratedMemberManualEditService.ProcessOwners", live);
        Assert.Contains("TryRecoverGeneratedMembersOnly", manual);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", manual);
        Assert.Contains("Command_Roof_LockedNotificationTitle", manual);
    }

    [Fact]
    public void SupportedResizeStillMaterializesStructuralMembers()
    {
        var live = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");

        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction",
            live);
        Assert.Contains("forceRegenerateOnSourceResize: true", live);
        Assert.Contains("\"STRETCH\"", Read(
            "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs"));
        Assert.Contains("\"GRIP_STRETCH\"", Read(
            "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs"));
    }

    [Fact]
    public void VersionAndSchemasRemainUnchanged()
    {
        Assert.Equal(1, RoofStructuralGeneratedDataSchema.CurrentVersion);
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", Read("Directory.Build.props"));
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
