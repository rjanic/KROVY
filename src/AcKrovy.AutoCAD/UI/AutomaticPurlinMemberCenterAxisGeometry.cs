using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Model-space center axes for rectangular purlin / wall-plate members in the
/// WPF roof section. Lengths are 3× width (horizontal) and 3× height (vertical),
/// centered on the member geometric center.
/// </summary>
internal static class AutomaticPurlinMemberCenterAxisGeometry
{
    internal const double AxisLengthFactor = 3d;
    internal const double HalfAxisLengthFactor = AxisLengthFactor / 2d;

    internal static bool ShouldDrawAxes(RoofAutomaticPurlinGeneratorRole role) =>
        role is RoofAutomaticPurlinGeneratorRole.WallPlate
            or RoofAutomaticPurlinGeneratorRole.Ridge
            or RoofAutomaticPurlinGeneratorRole.Intermediate;

    internal static AutomaticPurlinMemberCenterAxesMm Create(
        double centerXMm,
        double centerZMm,
        double widthMm,
        double heightMm)
    {
        if (!double.IsFinite(centerXMm) ||
            !double.IsFinite(centerZMm) ||
            !double.IsFinite(widthMm) ||
            !double.IsFinite(heightMm) ||
            widthMm <= 0d ||
            heightMm <= 0d)
        {
            return AutomaticPurlinMemberCenterAxesMm.Empty;
        }

        var halfHorizontal = HalfAxisLengthFactor * widthMm;
        var halfVertical = HalfAxisLengthFactor * heightMm;
        return new AutomaticPurlinMemberCenterAxesMm(
            CenterXMm: centerXMm,
            CenterZMm: centerZMm,
            HorizontalStartXMm: centerXMm - halfHorizontal,
            HorizontalEndXMm: centerXMm + halfHorizontal,
            HorizontalZMm: centerZMm,
            VerticalXMm: centerXMm,
            VerticalStartZMm: centerZMm - halfVertical,
            VerticalEndZMm: centerZMm + halfVertical,
            HorizontalLengthMm: AxisLengthFactor * widthMm,
            VerticalLengthMm: AxisLengthFactor * heightMm);
    }

    internal static AutomaticPurlinMemberCenterAxesMm Create(
        AutomaticPurlinSectionMemberMm member) =>
        Create(member.CenterXMm, member.CenterZMm, member.WidthMm, member.HeightMm);
}

internal readonly record struct AutomaticPurlinMemberCenterAxesMm(
    double CenterXMm,
    double CenterZMm,
    double HorizontalStartXMm,
    double HorizontalEndXMm,
    double HorizontalZMm,
    double VerticalXMm,
    double VerticalStartZMm,
    double VerticalEndZMm,
    double HorizontalLengthMm,
    double VerticalLengthMm)
{
    internal static AutomaticPurlinMemberCenterAxesMm Empty { get; } = default;

    internal bool IsEmpty =>
        HorizontalLengthMm <= 0d || VerticalLengthMm <= 0d;
}
