using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberNativeLeaderStyleRules
{
    private const double LandingDistanceTolerance = 1e-9d;

    public static TimberNativeLeaderStyleSettings Settings { get; } = new(
        StyleName: "ACAD_KROVY_LEADER",
        NoneArrowBlockName: "_None",
        UsesStraightLeader: true,
        LeaderColorIsByBlock: true,
        LeaderLinetypeIsByBlock: true,
        LeaderLineweightIsByBlock: true,
        TextHeightMm: TimberMainAnnotationTextRules.TextHeightMm,
        Scale: TimberMainAnnotationTextRules.LeaderScale,
        UsesAnnotationScale: TimberMainAnnotationTextRules.UsesAnnotationScale,
        FirstSegmentAngleDegrees: 60,
        HasArrowhead: false,
        ArrowheadSize: 0.08d,
        HasHorizontalLanding: true,
        LandingDistance: 0d,
        ExtendsLeaderToText: false,
        LeftTextAttachment: TimberNativeLeaderTextAttachment.UnderlineBottomLine,
        RightTextAttachment: TimberNativeLeaderTextAttachment.UnderlineBottomLine);

    public static TimberNativeLeaderStyleSettings FramedSettings { get; } =
        Settings with
        {
            StyleName = "ACAD_KROVY_FRAMED_LEADER",
            UsesStraightLeader = false,
            FirstSegmentAngleDegrees = 40,
        };

    public static bool UsesDedicatedStyle(TimberAnnotationMode mode) =>
        TimberAnnotationModeRules.Normalize(mode) is
            TimberAnnotationMode.ItemNumberLeader or
            TimberAnnotationMode.DimensionsLeader;

    public static bool RequiresExplicitDoglegDirection =>
        Settings.HasHorizontalLanding &&
        Settings.LandingDistance > LandingDistanceTolerance;

    public static bool UsesSplineLeader(ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) != ItemNumberLeaderStyle.Plain;

    public static bool UsesInsertionPointBlockAttachment(ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) != ItemNumberLeaderStyle.Plain;

    public static TimberNativeLeaderTextAttachment GetTextAttachment(
        TimberLeaderHorizontalSide contentSide) =>
        contentSide == TimberLeaderHorizontalSide.Left
            ? Settings.RightTextAttachment
            : Settings.LeftTextAttachment;
}
