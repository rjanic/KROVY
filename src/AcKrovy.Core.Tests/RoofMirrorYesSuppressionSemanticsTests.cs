using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofMirrorYesSuppressionSemanticsTests
{
    private static readonly RoofGeneratedMemberKey Key =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 10);

    [Fact]
    public void MirrorYesSuppression_IsPlainManualOverride_IdenticalToGeneratedErase()
    {
        var suppress = RoofGeneratedMemberOverride.Suppress(Key, "K-1");

        Assert.True(suppress.Suppressed);
        Assert.Equal(Key, suppress.Key);
        Assert.False(suppress.HasGeometryOverride);
    }

    [Fact]
    public void ResetEdits_ClearsSuppression_RestoringGeneratedSlot()
    {
        var set = new RoofManualOverrideSet(new[]
        {
            RoofGeneratedMemberOverride.Suppress(Key, "K-1"),
        });

        Assert.Equal(1, set.SuppressedCount);

        // AK_ROOF_RESET_EDITS clears overrides, which removes the suppression and lets
        // the canonical Generated slot regenerate. The mirrored AttachedManual child is
        // a separate Origin.Copy entity NOT represented in the override set, so it is
        // untouched by reset-edits (exactly like any other COPY child).
        var cleared = set.Clear();

        Assert.Equal(0, cleared.SuppressedCount);
        Assert.False(cleared.TryGet(Key, out _));
    }
}
