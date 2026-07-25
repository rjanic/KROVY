namespace AcKrovy.Core.Models;

public enum TimberNativeLeaderTextAttachment
{
    UnderlineBottomLine = 0,
}

public sealed record TimberNativeLeaderStyleSettings(
    string StyleName,
    string NoneArrowBlockName,
    bool UsesStraightLeader,
    bool LeaderColorIsByBlock,
    bool LeaderLinetypeIsByBlock,
    bool LeaderLineweightIsByBlock,
    double TextHeightMm,
    double Scale,
    bool UsesAnnotationScale,
    int FirstSegmentAngleDegrees,
    bool HasArrowhead,
    double ArrowheadSize,
    bool HasHorizontalLanding,
    double LandingDistance,
    bool ExtendsLeaderToText,
    TimberNativeLeaderTextAttachment LeftTextAttachment,
    TimberNativeLeaderTextAttachment RightTextAttachment);
