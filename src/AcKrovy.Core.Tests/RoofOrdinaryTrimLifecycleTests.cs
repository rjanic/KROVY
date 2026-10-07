using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Portable routing guards. Native TRIM events and DWG effects require the HOST test.</summary>
public sealed class RoofOrdinaryTrimLifecycleTests
{
    [Theory]
    [InlineData("TRIM")]
    [InlineData("_TRIM")]
    [InlineData("_.TRIM")]
    public void Trim_UsesTheOrdinaryPlanFirstClaim(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
    }

    [Theory]
    [InlineData("MOVE")]
    public void OtherNativeEdits_KeepTheirExistingRoutes(string command)
    {
        Assert.False(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
    }

    [Fact]
    public void NativeTrim_CapturesFreshSessionAndClaimsBeforeLegacyRecovery()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var begin = live[live.IndexOf("private void CommandWillStart", StringComparison.Ordinal)..
            live.IndexOf("private void CommandEnded", StringComparison.Ordinal)];
        Assert.True(begin.IndexOf("_ordinaryGripSnapshot?.Dispose()", StringComparison.Ordinal) <
                    begin.IndexOf("RoofOrdinaryGripLifecycleService.Capture", StringComparison.Ordinal));
        Assert.Contains("_ordinaryGripSnapshot.BeginCommand(_document, e.GlobalCommandName)", begin);
        Assert.True(live.IndexOf("RoofOrdinaryGripLifecycleService.Process(", StringComparison.Ordinal) <
                    live.IndexOf("RoofLiveResizeService.Process(", StringComparison.Ordinal));
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds", service);
        Assert.Contains("_ordinaryGripSnapshot?.TraceCommandState(\"end\")", live);
        Assert.Contains("_ordinaryGripSnapshot?.TraceCommandState(\"cancel\")", live);
        Assert.Contains("_ordinaryGripSnapshot?.TraceCommandState(\"fail\")", live);
        Assert.Contains("_ordinaryGripSnapshot?.Dispose()", live);
        Assert.Contains("ROOF_ORDINARY_TRIM_COMMAND_STATE", service);
        Assert.Contains("ROOF_ORDINARY_TRIM_LIFECYCLE", service);
    }

    [Fact]
    public void Trim_ReusesPlanAuthorityForAcceptRejectAndIndependent()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("!a.Member.Independent && a.Change != RoofOrdinaryGripChange.None", service);
        Assert.Contains("ConfirmDetach()", service);
        Assert.Contains("foreach (var item in affected) Restore(transaction, item.Member)", service);
        Assert.Contains("RestoreGroup(transaction, snapshot, ownerMembers.Key)", service);
        Assert.Contains("Accept(document, transaction, item.Member, item.Plan)", service);
        Assert.Contains("useIndependentHorizontalFrame: true", service);
        Assert.Contains("RoofIndependentOrdinaryDetachService.TryDetach", service);
        Assert.Contains("RecalculateDesignations(document, transaction", service);
        Assert.Contains("RoofOrdinaryGripRollbackRefreshService.Schedule(", service);
        Assert.Contains("manualOverrideWritten=false terminalHandled=true", service);
        Assert.DoesNotContain("TryAcceptUnlockedEdits", service);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
