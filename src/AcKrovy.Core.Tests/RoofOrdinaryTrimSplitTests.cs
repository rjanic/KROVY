using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryTrimSplitTests
{
    private static RoofSegment3D Axis(double a, double b) => new(new(1000, a, 0), new(1000, b, 0));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MiddleTrim_RecognizesDisconnectedPiecesWithEitherDirection(bool reverse)
    {
        var second = reverse ? Axis(3000, 2000) : Axis(2000, 3000);
        Assert.True(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), new[] { Axis(0, 1000), second }));
    }

    [Fact]
    public void SingleEndTrim_IsNotSplit() =>
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), new[] { Axis(0, 1000) }));

    [Theory]
    [InlineData(900, 2000)]
    [InlineData(1000, 2000)]
    [InlineData(2000, 4000)]
    [InlineData(2000, 2000)]
    public void OverlapTouchOutsideAndDegenerateClones_AreRejected(double a, double b) =>
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), new[] { Axis(0, 1000), Axis(a, b) }));

    [Fact]
    public void UnrelatedParallelLine_IsRejected() =>
        Assert.False(RoofOrdinaryTrimSplitRules.IsSplit(Axis(0, 3000), new[] {
            Axis(0, 1000), new RoofSegment3D(new(1001, 2000, 0), new(1001, 3000, 0)) }));

    [Fact]
    public void NativeSplit_IsClaimedBeforeDetachCanSynchronizeGroup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        var service = File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryGripLifecycleService.cs"));
        var live = File.ReadAllText(Path.Combine(root.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs"));
        Assert.True(service.IndexOf("ClaimSplitLine(transaction, item.Member", StringComparison.Ordinal) <
                    service.IndexOf("Accept(document, transaction, item.Member", StringComparison.Ordinal));
        Assert.Contains("NativeTrimAppendedIds.Add(entity.ObjectId)", live);
        Assert.Contains("appendedTimberIds.Where(id => !gripClaimedIds.Contains(id))", live);
        Assert.Contains("IndependentMemberId = Guid.NewGuid()", service);
        Assert.Contains("EraseSplitLines(transaction, splits.Values", service);
        Assert.Contains("CancelOrdinaryTrimSplit(e.GlobalCommandName)", live);
        Assert.Contains("NativeTrimAppendedIds.Clear()", service);
        Assert.Contains("VerifySplitPackages", service);
        Assert.Contains("affected.Concat(fragments)", service);
        Assert.Contains("ROOF_ORDINARY_TRIM_SPLIT", service);
    }
}
