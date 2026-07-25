using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberItemLeaderBlockDefinitionRules
{
    public const string AttributeTag = "ITEM_NO";
    public const double HorizontalPaddingMm = 70d;
    public const double VerticalPaddingMm = 50d;
    public const double SmallCircleDiameterMm = 520d;
    public const double MediumCircleDiameterMm = 760d;
    public const double LargeCircleDiameterMm = 1800d;
    public const double SmallFrameWidthMm = 600d;
    public const double MediumFrameWidthMm = 900d;
    public const double LargeFrameWidthMm = 1600d;
    public const double FrameHeightMm = 360d;
    public const double EstimatedCharacterWidthFactor =
        TimberItemLeaderLayoutCalculator.EstimatedCharacterWidthFactor;

    public static TimberItemLeaderBlockDefinition Resolve(
        ItemNumberLeaderStyle style,
        string itemText)
    {
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        if (normalizedStyle == ItemNumberLeaderStyle.Plain)
        {
            throw new ArgumentOutOfRangeException(
                nameof(style),
                "Plain item leaders do not use a block definition.");
        }

        var normalizedText = itemText?.Trim() ?? string.Empty;
        var estimatedTextWidth = Math.Max(
            TimberMainAnnotationTextRules.TextHeightMm,
            normalizedText.Length *
            TimberMainAnnotationTextRules.TextHeightMm *
            EstimatedCharacterWidthFactor);
        var requiredWidth = estimatedTextWidth + 2d * HorizontalPaddingMm;
        var requiredHeight =
            TimberMainAnnotationTextRules.TextHeightMm +
            2d * VerticalPaddingMm;

        return normalizedStyle == ItemNumberLeaderStyle.Circle
            ? ResolveCircle(requiredWidth, requiredHeight)
            : ResolveLinearFrame(normalizedStyle, requiredWidth);
    }

    public static string GetBaseBlockName(ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) switch
        {
            ItemNumberLeaderStyle.Circle => "ACAD_KROVY_ITEM_CIRCLE",
            ItemNumberLeaderStyle.Slot => "ACAD_KROVY_ITEM_SLOT",
            ItemNumberLeaderStyle.Rectangle => "ACAD_KROVY_ITEM_RECTANGLE",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

    private static TimberItemLeaderBlockDefinition ResolveCircle(
        double requiredWidth,
        double requiredHeight)
    {
        var requiredDiameter = Math.Sqrt(
            requiredWidth * requiredWidth +
            requiredHeight * requiredHeight);
        var (size, diameter) = requiredDiameter switch
        {
            <= SmallCircleDiameterMm => (
                TimberItemLeaderBlockSize.Small,
                SmallCircleDiameterMm),
            <= MediumCircleDiameterMm => (
                TimberItemLeaderBlockSize.Medium,
                MediumCircleDiameterMm),
            _ => (
                TimberItemLeaderBlockSize.Large,
                LargeCircleDiameterMm),
        };
        return Create(
            ItemNumberLeaderStyle.Circle,
            size,
            diameter,
            diameter);
    }

    private static TimberItemLeaderBlockDefinition ResolveLinearFrame(
        ItemNumberLeaderStyle style,
        double requiredWidth)
    {
        var (size, width) = requiredWidth switch
        {
            <= SmallFrameWidthMm => (
                TimberItemLeaderBlockSize.Small,
                SmallFrameWidthMm),
            <= MediumFrameWidthMm => (
                TimberItemLeaderBlockSize.Medium,
                MediumFrameWidthMm),
            _ => (
                TimberItemLeaderBlockSize.Large,
                LargeFrameWidthMm),
        };
        return Create(style, size, width, FrameHeightMm);
    }

    private static TimberItemLeaderBlockDefinition Create(
        ItemNumberLeaderStyle style,
        TimberItemLeaderBlockSize size,
        double width,
        double height)
    {
        var suffix = size switch
        {
            TimberItemLeaderBlockSize.Small => string.Empty,
            TimberItemLeaderBlockSize.Medium => "_M",
            TimberItemLeaderBlockSize.Large => "_L",
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };
        return new TimberItemLeaderBlockDefinition(
            style,
            size,
            GetBaseBlockName(style) + suffix,
            width,
            height,
            TimberMainAnnotationTextRules.TextHeightMm);
    }
}
