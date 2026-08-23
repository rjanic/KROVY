using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
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
        if (state is null || ActualWidth < 260d || ActualHeight < 220d ||
            state.SpanMm <= 0d || state.HeightDifferenceMm <= 0d)
        {
            return;
        }

        var foreground = FindBrush("SettingsTextPrimaryBrush", Brushes.Black);
        var secondary = FindBrush("SettingsTextSecondaryBrush", Brushes.DimGray);
        var border = FindBrush("SettingsBorderBrush", Brushes.Gray);
        var lowBrush = FrozenBrush(Color.FromRgb(39, 116, 196));
        var highBrush = FrozenBrush(Color.FromRgb(43, 151, 96));
        var technical = FrozenBrush(Color.FromRgb(210, 145, 24));
        var marginX = 90d;
        var top = 74d;
        var bottom = ActualHeight - 105d;
        var left = marginX;
        var right = ActualWidth - marginX;
        var availableHeight = Math.Max(60d, bottom - top);
        var risePixels = Math.Min(availableHeight, (right - left) *
            state.HeightDifferenceMm / state.SpanMm);
        var low = state.IsMirrored
            ? new Point(right, top + risePixels)
            : new Point(left, top + risePixels);
        var high = state.IsMirrored
            ? new Point(left, top)
            : new Point(right, top);
        var datumY = Math.Min(ActualHeight - 70d, low.Y + 38d);
        var dimensionY = Math.Min(ActualHeight - 32d, datumY + 30d);
        var dimensionPen = new Pen(secondary, 1.25d);
        var datumPen = new Pen(border, 1.5d) { DashStyle = DashStyles.Dash };

        drawingContext.DrawLine(datumPen, new Point(left - 24d, datumY), new Point(right + 24d, datumY));
        drawingContext.DrawLine(new Pen(technical, 12d), low, high);
        drawingContext.DrawEllipse(lowBrush, null, low, 6d, 6d);
        drawingContext.DrawEllipse(highBrush, null, high, 6d, 6d);
        DrawDirectionArrow(drawingContext, low, high, technical);
        DrawAngle(drawingContext, low, high, technical);

        drawingContext.DrawLine(dimensionPen, new Point(left, dimensionY), new Point(right, dimensionY));
        DrawArrowHead(drawingContext, new Point(left, dimensionY), 1d, secondary);
        DrawArrowHead(drawingContext, new Point(right, dimensionY), -1d, secondary);
        DrawCenteredText(
            drawingContext,
            $"L = {state.SpanMm.ToString("0", state.Culture)} mm",
            new Point((left + right) / 2d, dimensionY - 23d),
            technical,
            state.Culture,
            12d);

        var deltaX = state.IsMirrored
            ? Math.Max(24d, left - 42d)
            : Math.Min(ActualWidth - 24d, right + 42d);
        var geometrySideX = state.IsMirrored ? left : right;
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
            new Point(low.X + (state.IsMirrored ? -88d : 32d), low.Y - 34d),
            technical, state.Culture, 13d);
        DrawText(drawingContext, state.SpanLabel,
            new Point(left, 10d), secondary, state.Culture, 12d);
    }

    private static void DrawDirectionArrow(DrawingContext context, Point low, Point high, Brush brush)
    {
        var start = Lerp(low, high, 0.36d);
        var tip = Lerp(low, high, 0.66d);
        var pen = new Pen(brush, 2.2d);
        context.DrawLine(pen, start, tip);
        var dx = tip.X - start.X;
        var dy = tip.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var ux = dx / length;
        var uy = dy / length;
        context.DrawLine(pen, tip, new Point(tip.X - ux * 13d - uy * 6d, tip.Y - uy * 13d + ux * 6d));
        context.DrawLine(pen, tip, new Point(tip.X - ux * 13d + uy * 6d, tip.Y - uy * 13d - ux * 6d));
    }

    private static void DrawAngle(DrawingContext context, Point low, Point high, Brush brush)
    {
        var direction = high.X >= low.X ? 1d : -1d;
        context.DrawLine(new Pen(brush, 1.25d), low, new Point(low.X + direction * 58d, low.Y));
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
    private static Point Lerp(Point first, Point second, double ratio) =>
        new(first.X + (second.X - first.X) * ratio, first.Y + (second.Y - first.Y) * ratio);
    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    private Brush FindBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
