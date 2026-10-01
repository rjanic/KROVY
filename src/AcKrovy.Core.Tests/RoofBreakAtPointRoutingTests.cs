using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofBreakAtPointRoutingTests
{
    [Theory]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData(" _breakatpoint ")]
    [InlineData("._BREAKATPOINT")]
    [InlineData("'_.breakatpoint")]
    public void BothBreakCommands_EnterTheEntireSharedSplitLifecycle(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsBreakCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command, RoofKind.Hip));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(command));
        Assert.True(LiveGeometryCommandRules.RequiresGroupedUndoMark(command));
        Assert.False(LiveGeometryCommandRules.IsCopySourcePreservingCommand(command));
    }

    [Theory]
    [InlineData("BREAKATPOINTX")]
    [InlineData("BREAKLINE")]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    [InlineData("UNDO")]
    [InlineData("")]
    public void BreakClassifier_RemainsExact(string command)
    {
        Assert.False(RoofGeneratedMemberEditCommandRules.IsBreakCommand(command));
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData(" _copy ")]
    [InlineData("'_.COPY")]
    [InlineData("MIRROR")]
    [InlineData(" _mirror ")]
    [InlineData("'_.MIRROR")]
    public void NativeCopyMirrorPrefixes_AlreadyReachTheirExistingRoutes(string command)
    {
        var copy = LiveGeometryCommandRules.NormalizeCommandName(command).Equals("COPY", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(copy, LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(command));
        Assert.Equal(!copy, RoofGeneratedMemberEditCommandRules.IsMirrorCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(LiveGeometryCommandRules.RequiresGroupedUndoMark(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(command));
    }
}
