using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Native HOST execution is required in addition to these geometry/routing guards.</summary>
public sealed class RoofOrdinaryBreakLifecycleTests
{
    private static RoofSegment3D Axis(double a, double b) => new(new(1000, a, 0), new(1000, b, 0));

    [Theory]
    [InlineData("BREAK")]
    [InlineData("_.BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData("_BREAKATPOINT")]
    public void BreakAndBreakAtPoint_UseSharedNonRigidSplitLifecycle(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsMoveCommand(command));
    }

    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(1000, 1000, true)]
    [InlineData(1000, 2000, false)]
    [InlineData(1000, 2000, true)]
    public void FinalNativePieces_RecognizeGapAndZeroGapInEitherDirection(double end, double start, bool reverse)
    {
        var pieces = new[] { Axis(0, end), reverse ? Axis(3000, start) : Axis(start, 3000) };
        Assert.True(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), pieces, allowTouchingPieces: true));
        Assert.Equal(start > end,
            RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), pieces));
    }

    [Theory]
    [InlineData(999, 2000)]
    [InlineData(2000, 4000)]
    [InlineData(2000, 2000)]
    public void OverlapOutsideAndEmptyPieces_AreNotManufacturingSplits(double a, double b) =>
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000),
            new[] { Axis(0, 1000), Axis(a, b) }, allowTouchingPieces: true));

    [Fact]
    public void SingleResultAndUnchangedLine_AreNotSplits()
    {
        var source = Axis(0, 3000);
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(source, new[] { Axis(0, 1000) }, allowTouchingPieces: true));
        Assert.Equal(RoofOrdinaryGripChange.End, RoofOrdinaryGripLifecycleRules.Classify(source, Axis(0, 1000)));
        Assert.Equal(RoofOrdinaryGripChange.None, RoofOrdinaryGripLifecycleRules.Classify(source, source));
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(source, new[] {
            Axis(0, 1000), new RoofSegment3D(new(1001, 2000, 0), new(1001, 3000, 0)) }, allowTouchingPieces: true));
    }

    [Fact]
    public void AutoYesAndIndependentSingleAndSplit_ReuseAcceptedPackagesAndUniqueIdentities()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("!a.Member.Independent && a.Change != RoofOrdinaryGripChange.None", service);
        Assert.Contains("ConfirmDetach()", service);
        Assert.Contains("Accept(document, transaction, item.Member, item.Plan)", service);
        Assert.Contains("Accept(document, transaction, fragment, plan)", service);
        Assert.Contains("IndependentMemberId = Guid.NewGuid()", service);
        Assert.Contains("Read(item.Member.PlanCopy)?.IndependentMemberId", service);
        Assert.Contains("identities.Select(identity => identity!.IndependentMemberId).Distinct().Count() != lines.Length", service);
        Assert.Contains("Ordinary native split retained active Generated identity", service);
        Assert.Contains("useIndependentHorizontalFrame: true", service);
        Assert.Contains("RecalculateDesignations(document, transaction", service);
        Assert.Contains("affected.Concat(fragments)", service);
        Assert.Contains("ROOF_ORDINARY_BREAK_SPLIT", service);
        Assert.Contains("ROOF_ORDINARY_BREAK_LIFECYCLE", service);
        Assert.DoesNotContain("IntersectWith", service);
        Assert.DoesNotContain("ROOF_MANUAL_EDIT_ACCEPT", service);
        Assert.DoesNotContain("ROOF_MANUAL_EDIT_RECALC", service);
        Assert.DoesNotContain("HasGeometryOverride", service);
    }

    [Fact]
    public void RejectSingleOrSplit_RestoresSnapshotWithoutAnyIdentityOrDesignationAcceptance()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var reject = service[service.IndexOf("if (!accepted)", StringComparison.Ordinal)..
            service.IndexOf("// Claim every native clone", StringComparison.Ordinal)];
        Assert.Contains("EraseSplitLines(transaction, splits.Values.SelectMany(ids => ids))", reject);
        Assert.Contains("foreach (var item in affected) Restore(transaction, item.Member)", reject);
        Assert.Contains("RestoreGroup(transaction, snapshot, ownerMembers.Key)", reject);
        Assert.DoesNotContain("ClaimSplitLine", reject);
        Assert.DoesNotContain("Accept(document", reject);
        Assert.DoesNotContain("RecalculateDesignations", reject);
        Assert.Contains("Verify(document, transaction, snapshot, affected, accepted, designations)", service);
        Assert.Contains("var newIds = accepted ? string.Join", service);
        Assert.Contains("resultMemberCount={(accepted ? appended.Length + 1 : 1)}", service);
    }

    [Fact]
    public void AppendedNativeResults_AreCapturedAndClaimedBeforeGroupAndLegacyRecovery()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("IsSplitCommand(_currentGlobalCommandName)", live);
        Assert.Contains("NativeTrimAppendedIds.Add(entity.ObjectId)", live);
        Assert.Contains("IsSplitCommand(command)", service);
        Assert.True(service.IndexOf("ClaimSplitLine(transaction, item.Member", StringComparison.Ordinal) <
                    service.IndexOf("Accept(document, transaction, item.Member", StringComparison.Ordinal));
        Assert.True(live.IndexOf("RoofOrdinaryGripLifecycleService.Process(", StringComparison.Ordinal) <
                    live.IndexOf("RoofLiveResizeService.Process(", StringComparison.Ordinal));
        Assert.Contains("appendedTimberIds.Where(id => !gripClaimedIds.Contains(id))", live);
        Assert.Contains("claimed.UnionWith(splits.Values.SelectMany(ids => ids))", service);
    }

    [Fact]
    public void CancelFailAndRepeatedBreak_DisposeFreshCommandContextAndAppendedEvidence()
    {
        var service = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        var cancel = service[service.IndexOf("public static void CancelTrimSplit", StringComparison.Ordinal)..
            service.IndexOf("private static void TraceSplits", StringComparison.Ordinal)];
        Assert.Contains("IsSplitCommand(command)", cancel);
        Assert.Contains("EraseSplitLines(transaction, appended)", cancel);
        Assert.Contains("Restore(transaction, member)", cancel);
        Assert.Contains("Verify(document, transaction, snapshot, affected, accepted: false)", cancel);
        Assert.DoesNotContain("ClaimSplitLine", cancel);
        foreach (var phase in new[] { "CommandCancelled", "CommandFailed" })
        {
            var method = live[live.IndexOf("private void " + phase, StringComparison.Ordinal)..];
            Assert.True(method.IndexOf("CancelOrdinaryTrimSplit(e.GlobalCommandName)", StringComparison.Ordinal) <
                        method.IndexOf("ClearPendingLiveGeometryState()", StringComparison.Ordinal));
            Assert.Contains("_ordinaryGripSnapshot?.Dispose()", method);
        }
        var begin = live[live.IndexOf("private void CommandWillStart", StringComparison.Ordinal)..
            live.IndexOf("private void CommandEnded", StringComparison.Ordinal)];
        Assert.True(begin.IndexOf("_ordinaryGripSnapshot?.Dispose()", StringComparison.Ordinal) <
                    begin.IndexOf("RoofOrdinaryGripLifecycleService.Capture", StringComparison.Ordinal));
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds", service);
        Assert.Contains("ROOF_ORDINARY_BREAK_COMMAND_STATE", service);
        Assert.Contains("_candidates.Clear()", service);
        Assert.Contains("_processed.Clear()", service);
        Assert.Contains("NativeTrimAppendedIds.Clear()", service);
        Assert.Contains("TraceCommandState(\"disposed\")", service);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
