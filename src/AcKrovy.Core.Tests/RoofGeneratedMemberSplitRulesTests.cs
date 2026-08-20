using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberSplitRulesTests
{
    private const double Tolerance = RoofGeneratedMemberOverrideMath.LengthToleranceMm;

    [Fact]
    public void SnapshotHandle_RemainsGeneratedChild()
    {
        var snapshot = new[] { "2A1C", "2A1D" };
        Assert.True(RoofGeneratedMemberSplitRules.IsSnapshotGeneratedHandle("2A1C", snapshot));
        Assert.True(RoofGeneratedMemberSplitRules.IsSnapshotGeneratedHandle("2a1c", snapshot));
        Assert.False(RoofGeneratedMemberSplitRules.IsSnapshotGeneratedHandle("2A1E", snapshot));
        Assert.False(RoofGeneratedMemberSplitRules.IsSnapshotGeneratedHandle(" ", snapshot));
    }

    [Fact]
    public void MiddleTrimFragments_AreCollinearPiecesOfParent()
    {
        var parent = Horizontal(5000d);
        var generatedFragment = new RoofGeneratedMemberGeometry(
            parent.Start,
            new RoofPoint3D(1800d, 0d, 0d));
        var standaloneFragment = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(3200d, 0d, 0d),
            parent.End);

        Assert.True(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, generatedFragment, Tolerance));
        Assert.True(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, standaloneFragment, Tolerance));
        Assert.True(RoofGeneratedMemberSplitRules.PointOnSegment(parent, generatedFragment.End, Tolerance));
        Assert.True(RoofGeneratedMemberSplitRules.PointOnSegment(parent, standaloneFragment.Start, Tolerance));
    }

    [Fact]
    public void BreakFragments_AreCollinearPiecesOfParent()
    {
        var parent = Horizontal(4000d);
        var first = new RoofGeneratedMemberGeometry(parent.Start, new RoofPoint3D(1500d, 0d, 0d));
        var second = new RoofGeneratedMemberGeometry(new RoofPoint3D(1500d, 0d, 0d), parent.End);
        Assert.True(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, first, Tolerance));
        Assert.True(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, second, Tolerance));
    }

    [Fact]
    public void FullLengthOrOffAxis_IsNotASplitFragment()
    {
        var parent = Horizontal(4000d);
        Assert.False(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, parent, Tolerance));
        var offAxis = new RoofGeneratedMemberGeometry(
            parent.Start,
            new RoofPoint3D(1800d, 40d, 0d));
        Assert.False(RoofGeneratedMemberSplitRules.IsCollinearFragment(parent, offAxis, Tolerance));
    }

    [Fact]
    public void SplitGeneratedFragment_IsRepresentableAsEndpointOffset()
    {
        var canonical = Horizontal(5000d);
        var generatedFragment = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new RoofPoint3D(1800d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical,
            generatedFragment,
            new RoofPoint3D(0d, 0d, 1d),
            out var startDelta,
            out var endDelta,
            out _,
            out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(0d, startDelta, 6);
        Assert.Equal(-3200d, endDelta, 6);
    }

    [Fact]
    public void SplitCommands_AreAssemblySnapshotAndSupportedUnlocked()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsBreakCommand("BREAK"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSplitCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("BREAK"));
    }

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));
}
