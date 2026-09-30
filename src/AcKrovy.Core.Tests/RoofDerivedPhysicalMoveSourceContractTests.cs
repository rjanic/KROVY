using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Code-path guards. Native MOVE restoration still requires AutoCAD HOST proof.</summary>
public sealed class RoofDerivedPhysicalMoveSourceContractTests
{
    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName,
            "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Fact]
    public void NativeMove_IsClassifiedByOwnedSolidMetadata_ForLockedAndUnlockedRoofs()
    {
        var lifecycle = Read("RoofLiveResizeService.cs");
        var inspect = lifecycle.Substring(
            lifecycle.IndexOf("private static InspectionPlan Inspect(", StringComparison.Ordinal),
            lifecycle.IndexOf("private static bool HasErasedDerivedDisplay(", StringComparison.Ordinal) -
            lifecycle.IndexOf("private static InspectionPlan Inspect(", StringComparison.Ordinal));
        Assert.Contains("RoofGeneratedMemberEditCommandRules.IsMoveCommand(globalCommandName)", inspect);
        Assert.Contains("entity is Solid3d solid", inspect);
        Assert.Contains("RoofPhysical3DGeneratedStore.Read(solid).Data", inspect);
        Assert.Contains("physical.Role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid", inspect);
        Assert.Contains("physical.RoofOwnerReference", inspect);
        Assert.Contains("modifiedIds.Contains(ownerId)", inspect); // preserve whole-roof MOVE
        Assert.DoesNotContain("EditState == RoofEditState.Unlocked", inspect);
        Assert.DoesNotContain("EditState == RoofEditState.Locked", inspect);
    }

    [Fact]
    public void DirectPhysicalMove_RebuildsExactKeyFromAuthoritativePlan_NotMovedSolid()
    {
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var start = ordinary.IndexOf("public static bool TryRestoreMovedPhysicalMembersInTransaction(",
            StringComparison.Ordinal);
        var end = ordinary.IndexOf("public static bool TryVerifyRestoredPhysicalMembersInTransaction(",
            StringComparison.Ordinal);
        var restore = ordinary.Substring(start, end - start);
        Assert.Contains("physical.StructuralId", restore);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", restore);
        Assert.Contains("RoofGeneratedMemberKey.From(generated)", restore);
        Assert.Contains("TryReconcileModifiedMembersInTransaction(", restore);
        Assert.Contains("DetachMembersBeforeErase(", restore);
        Assert.DoesNotContain("TransformBy(", restore);
        Assert.DoesNotContain("line.UpgradeOpen()", restore);
        Assert.DoesNotContain("OpenMode.ForWrite, out var line", restore);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", restore);
    }

    [Fact]
    public void Recovery_IsTransactional_Canonical_AndIdempotentForRepeatedMove()
    {
        var lifecycle = Read("RoofLiveResizeService.cs");
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        Assert.Contains("TryRestoreMovedPhysicalMembersInTransaction(", lifecycle);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner(", lifecycle);
        Assert.Contains("TryVerifyMovedPhysicalStateInTransaction(", lifecycle);
        Assert.Contains("transaction.Commit()", lifecycle);
        Assert.Contains("TryFinalizeRestoredPhysicalGroup(", lifecycle);
        Assert.Contains("restoredIds.All(id => actual.Count(member => member == id) == 1)",
            lifecycle);
        Assert.Contains("actual.TryAdd(data.StructuralId, id)", ordinary);
        Assert.Contains("actual.Keys.ToHashSet().SetEquals(expected)", ordinary);
        Assert.Contains("!movedMemberIds.Add(physical.StructuralId)", ordinary);
    }

    [Fact]
    public void InternalRebuild_RemainsSuppressed_AndFailureIsExplicit()
    {
        var lifecycle = Read("LiveGeometrySynchronizationService.cs");
        var resize = Read("RoofLiveResizeService.cs");
        Assert.Contains("if (_modifiedIds.IsSuppressed)", lifecycle);
        Assert.Contains("using (_modifiedIds.Suppress())", lifecycle);
        Assert.Contains("RoofLiveResizeService.Process(", lifecycle);
        Assert.Contains("result=HardFailure", resize);
        Assert.Contains("Command_Roof_DerivedPhysicalMoveRecoveryFailed", resize);
        Assert.Contains("Command_Roof_DerivedPhysicalMoveRejected", resize);
    }
}
