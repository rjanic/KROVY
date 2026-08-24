using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Technical one-plane schematic matching the gable editor visual language.</summary>
public sealed class MonopitchRoofSectionControl : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(MonopitchRoofSectionState),
        typeof(MonopitchRoofSectionControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public MonopitchRoofSectionState? State
    {
        get => (MonopitchRoofSectionState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var state = State;
        if (state is null ||
            MonopitchRoofSectionLayoutCalculator.Create(
                state,
                ActualWidth,
                ActualHeight) is not { } layout)
        {
            return;
        }

        var foreground = FindBrush("SettingsTextPrimaryBrush", Brushes.Black);
        var secondary = FindBrush("SettingsTextSecondaryBrush", Brushes.DimGray);
        var border = FindBrush("SettingsBorderBrush", Brushes.Gray);
        var lowBrush = RoofSectionDiagramStyle.BlueRoofFaceBrush;
        var highBrush = RoofSectionDiagramStyle.GreenRoofFaceBrush;
        var roofBrush = RoofSectionDiagramStyle.GreenRoofFaceBrush;
        var technical = RoofSectionDiagramStyle.TechnicalBrush;
        var dimensionPen = RoofSectionDiagramStyle.CreateDimensionPen(secondary);
        var datumPen = RoofSectionDiagramStyle.CreateDatumPen(border);
        var referencePen = RoofSectionDiagramStyle.CreateReferencePen(secondary);
        var low = PointOf(layout.Low);
        var high = PointOf(layout.High);
        var highProjection = PointOf(layout.HighProjection);
        var angleAnnotation = CreateAngleAnnotation(low, high);
        var angleArcVertex = GableRoofSectionControl.CreateAngleArcVertex(
            angleAnnotation,
            low,
            high);

        drawingContext.DrawLine(
            datumPen,
            new Point(layout.Left - 24d, layout.DatumY),
            new Point(layout.Right + 24d, layout.DatumY));
        drawingContext.DrawLine(referencePen, low, highProjection);
        drawingContext.DrawLine(referencePen, high, highProjection);
        GableRoofSectionControl.DrawHorizontalAngleReference(
            drawingContext,
            angleAnnotation,
            dimensionPen);
        drawingContext.DrawLine(
            RoofSectionDiagramStyle.CreateRoofFacePen(roofBrush),
            low,
            high);
        drawingContext.DrawEllipse(lowBrush, null, low, 6d, 6d);
        drawingContext.DrawEllipse(highBrush, null, high, 6d, 6d);
        GableRoofSectionControl.DrawInteriorAngleArcTowardLowEave(
            drawingContext,
            angleArcVertex,
            low,
            state.SlopeDegrees,
            technical,
            RoofSectionDiagramStyle.AngleArcRadius);

        drawingContext.DrawLine(
            dimensionPen,
            new Point(layout.Left, layout.DimensionY),
            new Point(layout.Right, layout.DimensionY));
        DrawArrowHead(drawingContext, new Point(layout.Left, layout.DimensionY), 1d, secondary);
        DrawArrowHead(drawingContext, new Point(layout.Right, layout.DimensionY), -1d, secondary);
        DrawCenteredText(
            drawingContext,
            $"L = {state.SpanMm.ToString("0", state.Culture)} mm",
            new Point((layout.Left + layout.Right) / 2d, layout.DimensionY - 23d),
            technical,
            state.Culture,
            12d);

        var deltaX = state.IsMirrored
            ? Math.Max(24d, layout.Left - 42d)
            : Math.Min(ActualWidth - 24d, layout.Right + 42d);
        var geometrySideX = high.X;
        drawingContext.DrawLine(
            dimensionPen,
            new Point(geometrySideX, high.Y),
            new Point(deltaX, high.Y));
        drawingContext.DrawLine(
            dimensionPen,
            new Point(geometrySideX, low.Y),
            new Point(deltaX, low.Y));
        drawingContext.DrawLine(dimensionPen, new Point(deltaX, high.Y), new Point(deltaX, low.Y));
        drawingContext.DrawLine(dimensionPen, new Point(deltaX - 5d, high.Y), new Point(deltaX + 5d, high.Y));
        drawingContext.DrawLine(dimensionPen, new Point(deltaX - 5d, low.Y), new Point(deltaX + 5d, low.Y));
        DrawVerticalCenteredText(
            drawingContext,
            $"ΔH = {state.HeightDifferenceMm.ToString("0", state.Culture)} mm",
            new Point(deltaX, (high.Y + low.Y) / 2d),
            state.IsMirrored,
            technical,
            state.Culture,
            12d);

        DrawText(drawingContext, state.LowEaveLabel,
            new Point(low.X + (state.IsMirrored ? -62d : -8d), low.Y + 15d),
            foreground, state.Culture, 12d);
        DrawText(drawingContext, state.HighEaveLabel,
            new Point(high.X + (state.IsMirrored ? -8d : -64d), high.Y - 30d),
            foreground, state.Culture, 12d);
        DrawText(drawingContext,
            $"α {state.SlopeDegrees.ToString("0.###", state.Culture)}°",
            angleAnnotation.LabelOrigin,
            technical, state.Culture, 13d);
        DrawText(drawingContext, state.SpanLabel,
            new Point(layout.Left, 10d), secondary, state.Culture, 12d);
    }

    internal static GableRoofAngleAnnotation CreateAngleAnnotation(Point low, Point high)
    {
        var deltaX = high.X - low.X;
        var deltaY = high.Y - low.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var horizontalRoofwardProjection =
            RoofSectionDiagramStyle.AngleMaximumRoofwardReach * Math.Abs(deltaY) / length;
        var arrowRoofwardProjection =
            RoofSectionDiagramStyle.AngleArrowLength +
            RoofSectionDiagramStyle.AngleArrowNormalExtent;
        var outwardOffset =
            RoofSectionDiagramStyle.MonopitchAngleBaseOutwardOffset +
            Math.Max(horizontalRoofwardProjection, arrowRoofwardProjection);
        return GableRoofSectionControl.CreateAngleAnnotation(low, high, outwardOffset);
    }

    private static void DrawArrowHead(DrawingContext context, Point tip, double inward, Brush brush)
    {
        var geometry = new StreamGeometry();
        using (var target = geometry.Open())
        {
            target.BeginFigure(tip, true, true);
            target.LineTo(new Point(tip.X + inward * 9d, tip.Y - 3d), true, false);
            target.LineTo(new Point(tip.X + inward * 9d, tip.Y + 3d), true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawCenteredText(DrawingContext context, string text, Point center, Brush brush, CultureInfo culture, double size)
    {
        var formatted = CreateText(text, brush, culture, size);
        context.DrawText(formatted, new Point(center.X - formatted.Width / 2d, center.Y));
    }
    private void DrawVerticalCenteredText(
        DrawingContext context,
        string text,
        Point center,
        bool placeOnLeft,
        Brush brush,
        CultureInfo culture,
        double size)
    {
        var formatted = CreateText(text, brush, culture, size);
        context.PushTransform(new RotateTransform(placeOnLeft ? 90d : -90d, center.X, center.Y));
        context.DrawText(formatted, new Point(center.X - formatted.Width / 2d, center.Y + 9d));
        context.Pop();
    }
    private void DrawText(DrawingContext context, string text, Point origin, Brush brush, CultureInfo culture, double size) =>
        context.DrawText(CreateText(text, brush, culture, size), origin);
    private FormattedText CreateText(string text, Brush brush, CultureInfo culture, double size) =>
        new(text, culture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private static Point PointOf(MonopitchRoofSectionPoint point) => new(point.X, point.Y);
    private Brush FindBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
