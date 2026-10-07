using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Routing and geometry guards; native callbacks and DWG rollback require HOST execution.</summary>
public sealed class RoofOrdinaryExtendLifecycleTests
{
    [Theory]
    [InlineData("EXTEND")]
    [InlineData("_EXTEND")]
    [InlineData("_.EXTEND")]
    public void Extend_UsesSharedPlanFirstClaimAndSnapshot(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsMoveCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
    }

    [Theory]
    [InlineData(true, RoofOrdinaryGripChange.Start)]
    [InlineData(false, RoofOrdinaryGripChange.End)]
    public void FinalNativeLine_IdentifiesTheExtendedEndpoint(bool start, RoofOrdinaryGripChange expected)
    {
        var before = new RoofSegment3D(new(1000, 2000, 0), new(1000, 4000, 0));
        var final = start ? before with { Start = new(1000, 1300, 0) } : before with { End = new(1000, 4900, 0) };
        Assert.Equal(expected, RoofOrdinaryGripLifecycleRules.Classify(before, final));
        Assert.Equal(final, RoofOrdinaryGripLifecycleRules.Plan(final));
        Assert.Equal(RoofOrdinaryGripChange.None, RoofOrdinaryGripLifecycleRules.Classify(before, before));
    }

    [Fact]
    public void AcceptAndReject_ReuseSharedTransactionWithoutLegacyExtendPaths()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("ROOF_ORDINARY_EXTEND_LIFECYCLE", service);
        Assert.Contains("ROOF_ORDINARY_EXTEND_COMMAND_STATE", service);
        Assert.Contains("Accept(document, transaction, item.Member, item.Plan)", service);
        Assert.Contains("ConfirmDetach()", service);
        Assert.Contains("!a.Member.Independent && a.Change != RoofOrdinaryGripChange.None", service);
        Assert.Contains("useIndependentHorizontalFrame: true", service);
        Assert.Contains("RecalculateDesignations(document, transaction", service);
        Assert.Contains("foreach (var item in affected) Restore(transaction, item.Member)", service);
        Assert.Contains("RestoreGroup(transaction, snapshot, ownerMembers.Key)", service);
        Assert.Contains("Read(item.Member.PlanCopy)?.IndependentMemberId", service);
        Assert.True(live.IndexOf("RoofOrdinaryGripLifecycleService.Process(", StringComparison.Ordinal) <
                    live.IndexOf("RoofLiveResizeService.Process(", StringComparison.Ordinal));
        Assert.DoesNotContain("IntersectWith", service);
        Assert.DoesNotContain("ROOF_MANUAL_EDIT_ACCEPT", service);
        Assert.DoesNotContain("ROOF_MANUAL_EDIT_RECALC", service);
        Assert.DoesNotContain("HasGeometryOverride", service);
    }

    [Fact]
    public void CancelAndFail_RestorePendingGeometryBeforeClearingState()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        var cancel = service[service.IndexOf("public static void CancelExtend", StringComparison.Ordinal)..
            service.IndexOf("public static void CancelTrimSplit", StringComparison.Ordinal)];
        Assert.Contains("Restore(transaction, item.Member)", cancel);
        Assert.Contains("Verify(document, transaction, snapshot, affected, accepted: false)", cancel);
        Assert.DoesNotContain("Accept(document", cancel);
        Assert.DoesNotContain("RecalculateDesignations", cancel);
        foreach (var phase in new[] { "CommandCancelled", "CommandFailed" })
        {
            var start = live.IndexOf("private void " + phase, StringComparison.Ordinal);
            var method = live[start..];
            Assert.True(method.IndexOf("CancelOrdinaryTrimSplit(e.GlobalCommandName)", StringComparison.Ordinal) <
                        method.IndexOf("ClearPendingLiveGeometryState()", StringComparison.Ordinal));
            Assert.Contains("_ordinaryGripSnapshot?.Dispose()", method);
        }
        Assert.Contains("RoofOrdinaryGripLifecycleService.CancelExtend", live);
    }

    [Fact]
    public void RepeatedAndMultiMemberExtend_UseFreshContextAndProcessOnce()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        var begin = live[live.IndexOf("private void CommandWillStart", StringComparison.Ordinal)..
            live.IndexOf("private void CommandEnded", StringComparison.Ordinal)];
        Assert.True(begin.IndexOf("_ordinaryGripSnapshot?.Dispose()", StringComparison.Ordinal) <
                    begin.IndexOf("RoofOrdinaryGripLifecycleService.Capture", StringComparison.Ordinal));
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds", service);
        Assert.Contains("foreach (var member in ownerMembers)", service);
        Assert.Contains("change != RoofOrdinaryGripChange.None", service);
        Assert.Contains("_candidates.Clear()", service);
        Assert.Contains("_processed.Clear()", service);
        Assert.Contains("TraceCommandState(\"disposed\")", service);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
