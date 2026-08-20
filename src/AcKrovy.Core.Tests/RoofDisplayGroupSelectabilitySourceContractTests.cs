using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayGroupSelectabilitySourceContractTests
{
    private static readonly string Group = Read("RoofDisplayGroupService.cs");
    private static readonly string Selectability = Read("RoofDisplayGroupSelectabilityService.cs");
    private static readonly string EditState = Read("RoofEditStateCommandWorkflow.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");

    [Fact]
    public void EnsureGroup_AppliesEditStateToGroupSelectable()
    {
        Assert.Contains("RoofDisplayGroupSelectabilityRules.ShouldEnableGroupSelection", Group);
        Assert.Contains("group.Selectable = groupSelectable", Group);
        Assert.DoesNotContain("group.Selectable = true;", Group);
    }

    [Fact]
    public void LockUnlock_SyncGroupSelectabilityWithoutDissolvingGroup()
    {
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner", EditState);
        Assert.Contains("TryOpenCanonicalGroup", Selectability + Group);
        Assert.DoesNotContain("group.Clear()", EditState);
    }

    [Fact]
    public void Unlock_Diagnostics_RecordBeforeAfterAndPickstyle()
    {
        Assert.Contains("selectableReadBefore", Selectability);
        Assert.Contains("selectableWritten", Selectability);
        Assert.Contains("selectableReadAfter", Selectability);
        Assert.Contains("groupObjectId", Selectability);
        Assert.Contains("GetSystemVariable(\"PICKSTYLE\")", Selectability);
    }

    [Fact]
    public void DuplicateSelectableRoofGroups_AreEnumeratedAndPruned()
    {
        Assert.Contains("CollectGroupsContainingCanonicalMembers", Group + Selectability);
        Assert.Contains("PruneStaleRoofGroupsContainingCanonicalMembers", Group + Selectability);
        Assert.Contains("ROOF_GROUP_MEMBER_GROUP", Selectability);
        Assert.Contains("IsKrovyOwnedDuplicateRoofGroup", Group);
    }

    [Fact]
    public void GeneratedOrAttachedSelection_EmitsGroupMembershipDiagnostics()
    {
        var resolver = Read("RoofOwnerSelectionResolver.cs");
        Assert.Contains("WriteGroupMembershipDiagnostics", resolver);
    }

    [Fact]
    public void DocumentOpen_ReconcilesGroupSelectability()
    {
        Assert.Contains("ReconcileAllRoofOwners", Live);
        Assert.Contains("TryReconcileRoofGroupSelectabilityOnce", Live);
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
