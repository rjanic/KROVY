using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadItemLeaderFrameOnlyBlockTests
{
    [Fact]
    public void Key_UsesImmutableValueEqualityWithoutTextStyle()
    {
        var reference = Key();

        Assert.Equal(reference, Key());
        Assert.NotEqual(reference, Key(frame: AutoCadItemLeaderBlockFrameKind.Slot));
        Assert.NotEqual(
            Key(frame: AutoCadItemLeaderBlockFrameKind.Slot),
            Key(
                frame: AutoCadItemLeaderBlockFrameKind.Slot,
                size: TimberItemLeaderBlockSize.Medium));
        Assert.Empty(typeof(AutoCadItemLeaderFrameOnlyBlockKey).GetConstructors());
    }

    [Fact]
    public void Key_RejectsInvalidGeometryVersionAndCircleMediumLarge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoCadItemLeaderFrameOnlyBlockKey.Create(
                AutoCadItemLeaderBlockFrameKind.Circle,
                TimberItemLeaderBlockSize.Small,
                version: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoCadItemLeaderFrameOnlyBlockKey.Create(
                AutoCadItemLeaderBlockFrameKind.Circle,
                TimberItemLeaderBlockSize.Medium));
    }

    [Theory]
    [InlineData("Circle", "Small", "AK_ITEM_FRAME_CIRCLE_S_G4")]
    [InlineData("Slot", "Small", "AK_ITEM_FRAME_SLOT_S_G4")]
    [InlineData("Slot", "Medium", "AK_ITEM_FRAME_SLOT_M_G4")]
    [InlineData("Slot", "Large", "AK_ITEM_FRAME_SLOT_L_G4")]
    [InlineData("Rectangle", "Small", "AK_ITEM_FRAME_RECT_S_G4")]
    [InlineData("Rectangle", "Large", "AK_ITEM_FRAME_RECT_L_G4")]
    public void Name_MatchesExpectedCanonicalPattern(
        string frameName,
        string sizeName,
        string expected)
    {
        var frame = Enum.Parse<AutoCadItemLeaderBlockFrameKind>(frameName);
        var size = Enum.Parse<TimberItemLeaderBlockSize>(sizeName);
        var key = Key(frame: frame, size: size);

        Assert.Equal(
            expected,
            AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(key));
        Assert.True(AutoCadItemLeaderFrameOnlyBlockNamePolicy.IsG4FrameOnlyName(expected));
        Assert.False(
            AutoCadItemLeaderFrameOnlyBlockNamePolicy.IsG4FrameOnlyName(
                "AK_ITEM_CIR_G3_CLASSIC"));
    }

    [Fact]
    public void FingerprintPayload_ExcludesTextStyleAndHeight()
    {
        var payload =
            AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateFingerprintPayload(Key());

        Assert.Equal("schema=4|geometry=4|frame=CIRCLE|size=S", payload);
        Assert.DoesNotContain("textStyle", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("height", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromDefinition_UsesResolveStyleAndSizeOnly()
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "VT1234");
        var key = AutoCadItemLeaderFrameOnlyBlockKey.FromDefinition(definition);

        Assert.Equal(AutoCadItemLeaderBlockFrameKind.Rectangle, key.FrameKind);
        Assert.Equal(definition.Size, key.FrameSize);
        Assert.Equal(4, key.Version);
    }

    private static AutoCadItemLeaderFrameOnlyBlockKey Key(
        AutoCadItemLeaderBlockFrameKind frame = AutoCadItemLeaderBlockFrameKind.Circle,
        TimberItemLeaderBlockSize size = TimberItemLeaderBlockSize.Small) =>
        AutoCadItemLeaderFrameOnlyBlockKey.Create(frame, size);
}
