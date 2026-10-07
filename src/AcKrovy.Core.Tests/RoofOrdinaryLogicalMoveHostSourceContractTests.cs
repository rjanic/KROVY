using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Routing guard only; native event order and undo require an AutoCAD HOST retest.</summary>
public sealed class RoofOrdinaryLogicalMoveHostSourceContractTests
{
    [Fact]
    public void MoveClaimsDirectPhysicalBeforeLegacyRecovery_AndPreservesAutoPlanDetach()
    {
        var lifecycle = Read("LiveGeometrySynchronizationService.cs");
        var logicalMove = lifecycle.IndexOf("RoofOrdinaryLogicalMoveService.Process(", StringComparison.Ordinal);
        var oldResize = lifecycle.IndexOf("RoofLiveResizeService.Process(", logicalMove, StringComparison.Ordinal);
        Assert.True(logicalMove >= 0 && oldResize > logicalMove);
        Assert.Contains("ids = ids.Where(id => !logicalMoveClaimedIds.Contains(id)).ToArray()",
            lifecycle[logicalMove..oldResize]);
        Assert.Contains("_ordinaryMoveSnapshot = RoofOrdinaryLogicalMoveService.Capture(_document)", lifecycle);
        Assert.DoesNotContain("_ordinaryMoveNativeOrder", lifecycle);

        var service = Read("RoofOrdinaryLogicalMoveService.cs");
        Assert.Contains("if (!member.Independent && plan.Initiator == RoofOrdinaryMoveInitiator.Plan2D)", service);
        Assert.Contains("RejectDirectPhysical(transaction, member, currentPlan, nativeSet)", service);
        Assert.Contains("RoofOrdinaryAuthorityTransitionRules.Decide(", service);
        Assert.Contains("editDecision == RoofOrdinaryEditDecision.RestoreDerivedPhysical", service);
        Assert.Contains("AcceptIndependentPlan(transaction, member, currentPlan, nativeSet)", service);
        Assert.Contains("RestorePhysicalToBaseline(transaction, member)", service);
        Assert.Contains("ROOF_ORDINARY_USER_GEOMETRY_AUTHORITY", service);
        Assert.DoesNotContain("RoofIndependentOrdinaryDetachService.TryDetach", service);
        Assert.DoesNotContain("ConfirmIndependentOrdinaryDetach", service);
    }

    private static string Read(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD",
            "Infrastructure", filename));
    }
}
