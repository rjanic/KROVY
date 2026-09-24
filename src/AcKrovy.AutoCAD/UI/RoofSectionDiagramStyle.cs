using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Shared visual constants for roof-section construction diagrams.</summary>
internal static class RoofSectionDiagramStyle
{
    public const double RoofFaceStrokeThickness = 12d;
    public const double DatumStrokeThickness = 1.5d;
    public const double ReferenceStrokeThickness = 1.25d;
    public const double DimensionStrokeThickness = 1.25d;
    public const double AngleStrokeThickness = 1.5d;
    public const double AngleArcRadius = 80d;
    public const double AngleArrowLength = 7d;
    public const double AngleArrowNormalExtent = 3d;
    public const double AngleMaximumRoofwardReach = 34d;
    public const double AngleSafeRoofClearance = 8d;
    public const double MonopitchAngleBaseOutwardOffset =
        RoofFaceStrokeThickness / 2d +
        AngleStrokeThickness / 2d +
        AngleSafeRoofClearance;

    /// <summary>Left-face oak timber (#B87535). Historical name kept for call sites.</summary>
    public static Brush BlueRoofFaceBrush { get; } =
        FrozenBrush(Color.FromRgb(184, 117, 53));

    /// <summary>Right-face walnut timber (#70401F). Historical name kept for call sites.</summary>
    public static Brush GreenRoofFaceBrush { get; } =
        FrozenBrush(Color.FromRgb(112, 64, 31));

    /// <summary>Dimension / angle annotation color (#7A4F2A).</summary>
    public static Brush TechnicalBrush { get; } =
        FrozenBrush(Color.FromRgb(122, 79, 42));

    /// <summary>Guide / construction lines (#A68B72).</summary>
    public static Brush GuideBrush { get; } =
        FrozenBrush(Color.FromRgb(166, 139, 114));

    /// <summary>Brass ridge marker (#D39A2C).</summary>
    public static Brush RidgeBrush { get; } =
        FrozenBrush(Color.FromRgb(211, 154, 44));

    public static Pen CreateRoofFacePen(Brush brush) =>
        new(brush, RoofFaceStrokeThickness);

    public static Pen CreateDatumPen(Brush brush) =>
        new(brush, DatumStrokeThickness) { DashStyle = DashStyles.Dash };

    public static Pen CreateReferencePen(Brush brush) =>
        new(brush, ReferenceStrokeThickness) { DashStyle = DashStyles.Dash };

    /// <summary>Technical member centerline (dash-dot), distinct from the reference datum dash.</summary>
    public const double MemberCenterAxisStrokeThickness = 1.1d;

    public static Brush MemberCenterAxisBrush { get; } =
        FrozenBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));

    public static Brush MemberCenterPointBrush { get; } =
        FrozenBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

    public const double MemberCenterPointRadiusPx = 2.0d;

    public static Pen CreateMemberCenterAxisPen(Brush? brush = null) =>
        new(brush ?? MemberCenterAxisBrush, MemberCenterAxisStrokeThickness)
        {
            DashStyle = DashStyles.DashDot,
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
        };

    public static Pen CreateDimensionPen(Brush brush) =>
        new(brush, DimensionStrokeThickness);

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
