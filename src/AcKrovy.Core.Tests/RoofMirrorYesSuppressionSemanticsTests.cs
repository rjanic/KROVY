using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofMirrorYesGeometryOverrideSemanticsTests
{
    private static readonly RoofGeneratedMemberKey Key = new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 10);
    private static readonly RoofGeneratedMemberGeometry Canonical = new(new(0, 0, 0), new(0, 3000, 0));
    private static readonly RoofGeneratedMemberGeometry Mirrored = new(new(1200, 0, 0), new(1200, -3000, 0));

    [Fact]
    public void MirrorYes_PersistsGeometryOverride_UnderOriginalKeyAndReservation()
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(Canonical, Mirrored, new(0, 0, 1), Key, "K-1", out var edit));
        Assert.NotNull(edit);
        Assert.False(edit!.Suppressed);
        Assert.Equal(Key, edit.Key);
        Assert.Equal("K-1", edit.ReservedElementId);
        Assert.True(edit.HasGeometryOverride);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(Canonical, new(0, 0, 1), edit, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(Mirrored, replayed));
    }

    [Fact]
    public void ResetEdits_RemovesMirroredGeometryOverride_RetainingTheGeneratedSlot()
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(Canonical, Mirrored, new(0, 0, 1), Key, "K-1", out var edit));
        var set = new RoofManualOverrideSet([edit!]);
        Assert.Equal(0, set.SuppressedCount);
        var cleared = set.Clear();
        Assert.False(cleared.TryGet(Key, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(Canonical, new(0, 0, 1), null, out var restored));
        Assert.Equal(Canonical, restored);
    }
}
