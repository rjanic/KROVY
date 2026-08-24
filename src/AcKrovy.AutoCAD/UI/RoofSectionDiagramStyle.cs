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

    public static Brush BlueRoofFaceBrush { get; } =
        FrozenBrush(Color.FromRgb(39, 116, 196));

    public static Brush GreenRoofFaceBrush { get; } =
        FrozenBrush(Color.FromRgb(43, 151, 96));

    public static Brush TechnicalBrush { get; } =
        FrozenBrush(Color.FromRgb(210, 145, 24));

    public static Pen CreateRoofFacePen(Brush brush) =>
        new(brush, RoofFaceStrokeThickness);

    public static Pen CreateDatumPen(Brush brush) =>
        new(brush, DatumStrokeThickness) { DashStyle = DashStyles.Dash };

    public static Pen CreateReferencePen(Brush brush) =>
        new(brush, ReferenceStrokeThickness) { DashStyle = DashStyles.Dash };

    public static Pen CreateDimensionPen(Brush brush) =>
        new(brush, DimensionStrokeThickness);

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
