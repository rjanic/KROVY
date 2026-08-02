using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberItemLeaderBlockDefinitionRules
{
    public const string AttributeTag = "ITEM_NO";
    public const double BlockScale = 1d;
    public const double PreviousCircleDiameterMm = 520d;
    public const double CircleDiameterMm = 400d;
    public const double FramedGeometryReductionFactor =
        CircleDiameterMm / PreviousCircleDiameterMm;
    public const double BaseFramedItemTextHeightAtScale50Mm =
        TimberItemNumberTypographyRules
            .BaseItemNumberTextHeightAtScale50Mm;
    // R4A3 selected linear frame variants from this reference width.
    // Keep it independent from the rendered ITEM_NO height so R4A4 changes
    // typography without changing any Slot/Rectangle geometry.
    public const double FramedGeometrySizingTextHeightMm = 175d;
    public const double PreviousHorizontalPaddingMm = 70d;
    public const double PreviousVerticalPaddingMm = 50d;
    public const double PreviousSmallFrameWidthMm = 600d;
    public const double PreviousMediumFrameWidthMm = 900d;
    public const double PreviousLargeFrameWidthMm = 1600d;
    public const double PreviousFrameHeightMm = 360d;
    public const double HorizontalPaddingMm =
        PreviousHorizontalPaddingMm * FramedGeometryReductionFactor;
    public const double VerticalPaddingMm =
        PreviousVerticalPaddingMm * FramedGeometryReductionFactor;
    public const double SmallFrameWidthMm =
        PreviousSmallFrameWidthMm * FramedGeometryReductionFactor;
    public const double MediumFrameWidthMm =
        PreviousMediumFrameWidthMm * FramedGeometryReductionFactor;
    public const double LargeFrameWidthMm =
        PreviousLargeFrameWidthMm * FramedGeometryReductionFactor;
    public const double FrameHeightMm =
        PreviousFrameHeightMm * FramedGeometryReductionFactor;
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

        if (normalizedStyle == ItemNumberLeaderStyle.Circle)
        {
            return Create(
                ItemNumberLeaderStyle.Circle,
                TimberItemLeaderBlockSize.Small,
                CircleDiameterMm,
                CircleDiameterMm);
        }

        var normalizedText = itemText?.Trim() ?? string.Empty;
        var estimatedTextWidth = Math.Max(
            FramedGeometrySizingTextHeightMm,
            normalizedText.Length *
            FramedGeometrySizingTextHeightMm *
            EstimatedCharacterWidthFactor);
        var requiredWidth = estimatedTextWidth + 2d * HorizontalPaddingMm;
        return ResolveLinearFrame(normalizedStyle, requiredWidth);
    }

    public static TimberItemLeaderTextFitResult EvaluateMeasuredTextWidth(
        ItemNumberLeaderStyle style,
        string itemText,
        double measuredTextWidthMm)
    {
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        if (normalizedStyle == ItemNumberLeaderStyle.Plain)
        {
            throw new ArgumentOutOfRangeException(
                nameof(style),
                "Plain item leaders do not use a block definition.");
        }
        if (string.IsNullOrWhiteSpace(itemText))
        {
            throw new ArgumentException(
                "A non-empty ITEM_NO token is required.",
                nameof(itemText));
        }
        if (double.IsNaN(measuredTextWidthMm) ||
            double.IsInfinity(measuredTextWidthMm) ||
            measuredTextWidthMm < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(measuredTextWidthMm),
                "Measured ITEM_NO width must be finite and non-negative.");
        }

        var candidates = normalizedStyle == ItemNumberLeaderStyle.Circle
            ? new[]
            {
                Create(
                    ItemNumberLeaderStyle.Circle,
                    TimberItemLeaderBlockSize.Small,
                    CircleDiameterMm,
                    CircleDiameterMm),
            }
            : new[]
            {
                Create(
                    normalizedStyle,
                    TimberItemLeaderBlockSize.Small,
                    SmallFrameWidthMm,
                    FrameHeightMm),
                Create(
                    normalizedStyle,
                    TimberItemLeaderBlockSize.Medium,
                    MediumFrameWidthMm,
                    FrameHeightMm),
                Create(
                    normalizedStyle,
                    TimberItemLeaderBlockSize.Large,
                    LargeFrameWidthMm,
                    FrameHeightMm),
            };

        foreach (var candidate in candidates)
        {
            var innerWidth = CalculateAvailableInnerWidthMm(candidate);
            if (measuredTextWidthMm <= innerWidth)
            {
                return new TimberItemLeaderTextFitResult(
                    true,
                    itemText,
                    candidate,
                    measuredTextWidthMm,
                    innerWidth,
                    HorizontalPaddingMm,
                    "Measured ITEM_NO width fits with the required horizontal padding.");
            }
        }

        var largest = candidates[candidates.Length - 1];
        var largestInnerWidth = CalculateAvailableInnerWidthMm(largest);
        return new TimberItemLeaderTextFitResult(
            false,
            itemText,
            largest,
            measuredTextWidthMm,
            largestInnerWidth,
            HorizontalPaddingMm,
            "Measured ITEM_NO width exceeds the largest permitted frame inner width.");
    }

    public static double CalculateAvailableInnerWidthMm(
        TimberItemLeaderBlockDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        return definition.WidthMm - 2d * HorizontalPaddingMm;
    }

    public static string GetBaseBlockName(ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) switch
        {
            ItemNumberLeaderStyle.Circle => "ACAD_KROVY_ITEM_CIRCLE",
            ItemNumberLeaderStyle.Slot => "ACAD_KROVY_ITEM_SLOT",
            ItemNumberLeaderStyle.Rectangle => "ACAD_KROVY_ITEM_RECTANGLE",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

    public static bool HasExpectedCircleDiameter(double diameterMm) =>
        Math.Abs(diameterMm - CircleDiameterMm) <= 0.001d;

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
            BaseFramedItemTextHeightAtScale50Mm);
    }

    public static bool HasExpectedFramedItemTextHeight(double textHeightMm) =>
        Math.Abs(
            textHeightMm -
            BaseFramedItemTextHeightAtScale50Mm) <= 0.001d;
}
