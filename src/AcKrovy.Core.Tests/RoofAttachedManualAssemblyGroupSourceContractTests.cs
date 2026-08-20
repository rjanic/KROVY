using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualAssemblyGroupSourceContractTests
{
    private static readonly string Group = Read("RoofDisplayGroupService.cs");
    private static readonly string Collector = Read("RoofAssemblyGroupMemberCollector.cs");
    private static readonly string Sync = Read("RoofAssemblyGroupSyncService.cs");
    private static readonly string Copy = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string Manual = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string ChildPolicy = Read("RoofSourceResizeChildPolicyService.cs");
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");

    [Fact]
    public void EnsureGroup_IncludesAttachedManualAndGeneratedViaCollector()
    {
        Assert.Contains("RoofAssemblyGroupMemberCollector.TryCollect", Group);
        Assert.Contains("RoofAttachedManualTimberStore.FindByOwner", Collector);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", Collector);
        Assert.DoesNotContain("RoofAttachedManualTimberStore", GeneratedStore);
    }

    [Fact]
    public void Copy_Split_AndResize_SyncRoofAssemblyGroup()
    {
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwnerReference", Copy);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwnerReference", Manual);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", ChildPolicy);
    }

    [Fact]
    public void GroupDebug_LinesPresent()
    {
        Assert.Contains("ROOF_GROUP_SYNC", Group + Sync);
    }

    [Fact]
    public void StructuralTopology_AllowsExtendedAssemblyMembership()
    {
        Assert.Contains("ContainsStructuralDisplayTopology", Group);
        Assert.Contains("ExpectedStructuralDisplayChildCount = 7", Group);
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
}
