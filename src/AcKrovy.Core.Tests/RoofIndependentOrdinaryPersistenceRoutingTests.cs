using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Host routing/persistence guards; these do not prove native callback ordering or DWG writes.</summary>
public sealed class RoofIndependentOrdinaryPersistenceRoutingTests
{
    [Fact]
    public void EveryCreationWriterReachesSharedPersistenceWithinItsAcceptedTransaction()
    {
        var detach = Read("RoofIndependentOrdinaryDetachService.cs");
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        var copy = Read("RoofOrdinaryCopyLifecycleService.cs");
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, state)", detach);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, updated)", grip);
        Assert.Contains("Array.Empty<ObjectId>(), updated)", grip);
        Assert.Contains("Accept(document, transaction, fragment, plan)", grip);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Accept(document, transaction", copy);
        Assert.Contains("RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, current", detach);
        Assert.Contains("RoofOrdinaryPhysicalSectionFrameReader.TryCapture", detach);
    }

    [Theory]
    [InlineData("GRIP_STRETCH")]
    [InlineData("STRETCH")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    public void NativeGeometryCommandsUseCommonAcceptAndPersist(string command)
    {
        Assert.True(AcKrovy.Core.Services.Roofs.RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("Accept(document, transaction, item.Member, item.Plan)", grip);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist", grip);
    }

    [Fact]
    public void LegacyCaptureIsReadOnlyAndNeverUsesLiveAutoRebaseForIndependent()
    {
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        var capture = grip[grip.IndexOf("public static Snapshot Capture", StringComparison.Ordinal)..
            grip.IndexOf("public static IReadOnlyCollection<ObjectId> Process", StringComparison.Ordinal)];
        Assert.Contains("else if (independent is not null)", capture);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate", capture);
        Assert.DoesNotContain("PhysicalStateService.Persist", capture);
        var migration = Read("RoofIndependentOrdinaryPhysicalStateService.cs");
        Assert.Contains("HasRecord(line, transaction)", migration);
        Assert.Contains("paired.IndependentMemberId != identity.IndependentMemberId", migration);
        Assert.Contains("TryReadIndependent", migration);
        Assert.DoesNotContain("TryRecoverRecipe", migration);
        Assert.DoesNotContain("StationIndex ==", migration);
        Assert.DoesNotContain("TryRebase", migration);
        Assert.DoesNotContain("OpenMode.ForWrite", migration);
        Assert.DoesNotContain("GroupSync", migration);
        Assert.DoesNotContain("Guid.NewGuid", migration);
        Assert.DoesNotContain("TransferFromRoof", migration);
    }

    [Fact]
    public void PersistUsesExistingXrecordAndPreservesIdentityAndGroupAuthority()
    {
        var service = Read("RoofIndependentOrdinaryPhysicalStateService.cs");
        Assert.Contains("RoofOrdinaryPhysicalBuildStateStore.Write(line, transaction, complete!)", service);
        Assert.Contains("RoofIndependentOrdinaryTimberStore.Read(line) != identity", service);
        Assert.DoesNotContain("IndependentOrdinaryTimberStore.Write", service);
        Assert.DoesNotContain("Suppressed", service);
        Assert.DoesNotContain("ManualOverrides", service);
        Assert.DoesNotContain("AttachedManualTimberStore.Write", service);
        var collector = Read("RoofAssemblyGroupMemberCollector.cs");
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore", collector);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", collector);
    }

    [Fact]
    public void IndependentMoveUpdatesItsOwnAcceptedPlanStateAndDoesNotChangeIdentity()
    {
        var move = Read("RoofOrdinaryLogicalMoveService.cs");
        var start = move.IndexOf("private static void AcceptIndependentPlan", StringComparison.Ordinal);
        var end = move.IndexOf("private static void RestorePhysicalToBaseline", start, StringComparison.Ordinal);
        var accept = move[start..end];
        Assert.Contains("RoofOrdinaryPhysicalBuildStateRules.TryRebase", accept);
        Assert.Contains("RoofOrdinaryPhysicalSectionFrameReader.TryCapture", accept);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate", accept);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist", accept);
        Assert.DoesNotContain("Guid.NewGuid", accept);
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore.Write", accept);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName,"src","AcKrovy.AutoCAD","Infrastructure",file));
    }
}
