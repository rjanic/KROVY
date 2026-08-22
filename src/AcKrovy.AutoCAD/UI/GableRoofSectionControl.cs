using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Native WPF technical section schematic for the shared gable roof dialog.</summary>
public sealed class GableRoofSectionControl : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(GableRoofSectionState),
        typeof(GableRoofSectionControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public GableRoofSectionState? State
    {
        get => (GableRoofSectionState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var state = State;
        if (state is null ||
            GableRoofSectionLayoutCalculator.Create(state, ActualWidth, ActualHeight) is not { } layout)
        {
            return;
        }

        var foreground = FindBrush("SettingsTextPrimaryBrush", Brushes.Black);
        var secondary = FindBrush("SettingsTextSecondaryBrush", Brushes.DimGray);
        var border = FindBrush("SettingsBorderBrush", Brushes.Gray);
        var blue = FrozenBrush(Color.FromRgb(39, 116, 196));
        var green = FrozenBrush(Color.FromRgb(43, 151, 96));
        var technical = FrozenBrush(Color.FromRgb(210, 145, 24));
        var datumPen = new Pen(border, 1.5d) { DashStyle = DashStyles.Dash };
        var referencePen = new Pen(secondary, 1.25d) { DashStyle = DashStyles.Dash };
        var dimensionPen = new Pen(secondary, 1.25d);

        var eaveA = PointOf(layout.EaveA);
        var ridge = PointOf(layout.Ridge);
        var eaveB = PointOf(layout.EaveB);
        var leftEave = state.IsMirrored ? eaveA : eaveB;
        var rightEave = state.IsMirrored ? eaveB : eaveA;
        var leftRun = state.IsMirrored ? state.RunAMm : state.RunBMm;
        var rightRun = state.IsMirrored ? state.RunBMm : state.RunAMm;
        var leftBrush = state.IsMirrored ? blue : green;
        var rightBrush = state.IsMirrored ? green : blue;
        var alphaAnnotation = CreateAngleAnnotation(eaveA, ridge);
        var betaAnnotation = CreateAngleAnnotation(eaveB, ridge);
        var dimensionY = Math.Min(
            ActualHeight - 42d,
            Math.Max(layout.Bottom, Math.Max(eaveA.Y, eaveB.Y)) + 58d);

        drawingContext.DrawLine(
            datumPen,
            new Point(Math.Max(12d, layout.Left - 24d), layout.DatumY),
            new Point(Math.Min(ActualWidth - 12d, layout.Right + 24d), layout.DatumY));
        drawingContext.DrawLine(
            referencePen,
            new Point(ridge.X, layout.Top - 14d),
            new Point(ridge.X, dimensionY + 8d));
        drawingContext.DrawLine(referencePen, eaveA, new Point(eaveA.X, dimensionY + 8d));
        drawingContext.DrawLine(referencePen, eaveB, new Point(eaveB.X, dimensionY + 8d));

        DrawHorizontalAngleReference(drawingContext, alphaAnnotation, dimensionPen);
        DrawHorizontalAngleReference(drawingContext, betaAnnotation, dimensionPen);

        drawingContext.DrawLine(new Pen(leftBrush, 12d), leftEave, ridge);
        drawingContext.DrawLine(new Pen(rightBrush, 12d), ridge, rightEave);
        drawingContext.DrawEllipse(technical, null, ridge, 6d, 6d);
        DrawSemanticAngleArc(
            drawingContext,
            alphaAnnotation.Anchor,
            ridge,
            state.AlphaDegrees,
            technical);
        DrawSemanticAngleArc(
            drawingContext,
            betaAnnotation.Anchor,
            ridge,
            state.BetaDegrees,
            technical);

        DrawHorizontalDimension(
            drawingContext,
            leftEave.X,
            ridge.X,
            dimensionY,
            FormatLength(leftRun, state.Culture),
            dimensionPen,
            technical,
            state.Culture);
        DrawHorizontalDimension(
            drawingContext,
            ridge.X,
            rightEave.X,
            dimensionY,
            FormatLength(rightRun, state.Culture),
            dimensionPen,
            technical,
            state.Culture);

        DrawText(
            drawingContext,
            $"α {FormatAngle(state.AlphaDegrees, state.Culture)}°",
            alphaAnnotation.LabelOrigin,
            technical,
            state.Culture,
            13d);
        DrawText(
            drawingContext,
            $"β {FormatAngle(state.BetaDegrees, state.Culture)}°",
            betaAnnotation.LabelOrigin,
            technical,
            state.Culture,
            13d);
        DrawText(
            drawingContext,
            state.EaveALabel,
            EaveLabelOrigin(eaveA, ridge),
            foreground,
            state.Culture,
            12d);
        DrawText(
            drawingContext,
            state.EaveBLabel,
            EaveLabelOrigin(eaveB, ridge),
            foreground,
            state.Culture,
            12d);
        DrawCenteredText(
            drawingContext,
            state.RidgeLabel,
            new Point(ridge.X, ridge.Y - 40d),
            foreground,
            state.Culture,
            13d);
        DrawText(
            drawingContext,
            $"{state.SpanLabel}: {FormatLength(state.SpanMm, state.Culture)}",
            new Point(layout.Left, 10d),
            secondary,
            state.Culture,
            12d);

        if (state.IsAsymmetric)
        {
            DrawVerticalDeltaDimension(
                drawingContext,
                layout,
                state,
                dimensionPen,
                technical);
        }
    }

    internal static GableRoofAngleAnnotation CreateAngleAnnotation(Point eave, Point ridge)
    {
        const double positionAlongSlope = 0.32d;
        var anchor = new Point(
            eave.X + (ridge.X - eave.X) * positionAlongSlope,
            eave.Y + (ridge.Y - eave.Y) * positionAlongSlope);
        var isLeftPlane = eave.X < ridge.X;
        var deltaX = ridge.X - eave.X;
        var deltaY = ridge.Y - eave.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var outwardX = (isLeftPlane ? deltaY : -deltaY) / length;
        var outwardY = (isLeftPlane ? -deltaX : deltaX) / length;
        var outsideAnchor = new Point(
            anchor.X + outwardX * 7d,
            anchor.Y + outwardY * 7d);
        return new GableRoofAngleAnnotation(
            outsideAnchor,
            anchor,
            new Point(outsideAnchor.X + (isLeftPlane ? -76d : 76d), outsideAnchor.Y),
            new Point(outsideAnchor.X + (isLeftPlane ? 34d : -34d), outsideAnchor.Y),
            new Point(outsideAnchor.X + (isLeftPlane ? -60d : 18d), outsideAnchor.Y - 34d));
    }

    private static void DrawHorizontalAngleReference(
        DrawingContext drawingContext,
        GableRoofAngleAnnotation annotation,
        Pen pen)
        => drawingContext.DrawLine(
            pen,
            annotation.ReferenceStart,
            annotation.ReferenceEnd);

    private static void DrawSemanticAngleArc(
        DrawingContext drawingContext,
        Point eave,
        Point ridge,
        double degrees,
        Brush brush)
    {
        if (eave.X < ridge.X)
        {
            DrawAngleArc(drawingContext, eave, 0d, -Radians(degrees), brush);
        }
        else
        {
            DrawAngleArc(
                drawingContext,
                eave,
                -Math.PI,
                -Math.PI + Radians(degrees),
                brush);
        }
    }

    private static void DrawAngleArc(
        DrawingContext drawingContext,
        Point center,
        double startAngle,
        double endAngle,
        Brush brush)
    {
        const double radius = 24d;
        const int segmentCount = 18;
        var previous = default(Point);
        var last = default(Point);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index <= segmentCount; index++)
            {
                var ratio = index / (double)segmentCount;
                var angle = startAngle + (endAngle - startAngle) * ratio;
                var point = new Point(
                    center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);
                if (index == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
                previous = last;
                last = point;
            }
        }
        geometry.Freeze();
        var pen = new Pen(brush, 1.5d);
        drawingContext.DrawGeometry(null, pen, geometry);
        DrawArcArrow(drawingContext, previous, last, pen);
    }

    private static void DrawArcArrow(
        DrawingContext drawingContext,
        Point previous,
        Point tip,
        Pen pen)
    {
        var deltaX = tip.X - previous.X;
        var deltaY = tip.Y - previous.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length <= double.Epsilon)
        {
            return;
        }

        var tangentX = deltaX / length;
        var tangentY = deltaY / length;
        var basePoint = new Point(tip.X - tangentX * 7d, tip.Y - tangentY * 7d);
        var normalX = -tangentY * 3d;
        var normalY = tangentX * 3d;
        drawingContext.DrawLine(
            pen,
            tip,
            new Point(basePoint.X + normalX, basePoint.Y + normalY));
        drawingContext.DrawLine(
            pen,
            tip,
            new Point(basePoint.X - normalX, basePoint.Y - normalY));
    }

    private void DrawHorizontalDimension(
        DrawingContext drawingContext,
        double startX,
        double endX,
        double y,
        string label,
        Pen pen,
        Brush textBrush,
        CultureInfo culture)
    {
        drawingContext.DrawLine(pen, new Point(startX, y), new Point(endX, y));
        DrawDimensionArrow(drawingContext, new Point(startX, y), 1d, pen.Brush);
        DrawDimensionArrow(drawingContext, new Point(endX, y), -1d, pen.Brush);
        DrawCenteredText(
            drawingContext,
            label,
            new Point((startX + endX) / 2d, y - 22d),
            textBrush,
            culture,
            12d);
    }

    private static void DrawDimensionArrow(
        DrawingContext drawingContext,
        Point tip,
        double inwardDirection,
        Brush brush)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, true, true);
            context.LineTo(new Point(tip.X + inwardDirection * 9d, tip.Y - 3d), true, false);
            context.LineTo(new Point(tip.X + inwardDirection * 9d, tip.Y + 3d), true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
    }

    private void DrawVerticalDeltaDimension(
        DrawingContext drawingContext,
        GableRoofSectionLayout layout,
        GableRoofSectionState state,
        Pen pen,
        Brush technical)
    {
        var eaveBIsLeft = layout.EaveB.X < layout.Ridge.X;
        var x = eaveBIsLeft
            ? Math.Max(24d, layout.Left - 42d)
            : Math.Min(ActualWidth - 24d, layout.Right + 42d);
        var first = new Point(x, layout.EaveA.Y);
        var second = new Point(x, layout.EaveB.Y);
        var geometrySideX = eaveBIsLeft ? layout.Left : layout.Right;
        drawingContext.DrawLine(
            pen,
            new Point(geometrySideX, first.Y),
            first);
        drawingContext.DrawLine(
            pen,
            new Point(geometrySideX, second.Y),
            second);
        drawingContext.DrawLine(pen, first, second);
        drawingContext.DrawLine(pen, new Point(x - 5d, first.Y), new Point(x + 5d, first.Y));
        drawingContext.DrawLine(pen, new Point(x - 5d, second.Y), new Point(x + 5d, second.Y));
        DrawVerticalCenteredText(
            drawingContext,
            $"ΔH {FormatSignedLength(state.EaveBElevationMm - state.EaveAElevationMm, state.Culture)}",
            new Point(x, (first.Y + second.Y) / 2d),
            eaveBIsLeft,
            technical,
            state.Culture,
            12d);
    }

    private void DrawVerticalCenteredText(
        DrawingContext drawingContext,
        string text,
        Point center,
        bool placeOnLeft,
        Brush brush,
        CultureInfo culture,
        double size)
    {
        var formatted = CreateText(text, brush, culture, size);
        drawingContext.PushTransform(new RotateTransform(placeOnLeft ? 90d : -90d, center.X, center.Y));
        drawingContext.DrawText(
            formatted,
            new Point(center.X - formatted.Width / 2d, center.Y + 9d));
        drawingContext.Pop();
    }

    private void DrawCenteredText(
        DrawingContext drawingContext,
        string text,
        Point centerTop,
        Brush brush,
        CultureInfo culture,
        double size)
    {
        var formatted = CreateText(text, brush, culture, size);
        drawingContext.DrawText(
            formatted,
            new Point(centerTop.X - formatted.Width / 2d, centerTop.Y));
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        Point origin,
        Brush brush,
        CultureInfo culture,
        double size) =>
        drawingContext.DrawText(CreateText(text, brush, culture, size), origin);

    private FormattedText CreateText(
        string text,
        Brush brush,
        CultureInfo culture,
        double size) =>
        new(
            text,
            culture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Point PointOf(GableRoofSectionPoint point) => new(point.X, point.Y);

    private static Point EaveLabelOrigin(Point eave, Point ridge) =>
        eave.X < ridge.X
            ? new Point(eave.X - 6d, eave.Y + 15d)
            : new Point(eave.X - 58d, eave.Y + 15d);

    private static double Radians(double degrees) => degrees * Math.PI / 180d;

    private static string FormatAngle(double value, CultureInfo culture) =>
        value.ToString("0.###", culture);

    private static string FormatLength(double value, CultureInfo culture) =>
        $"{value.ToString("0", culture)} mm";

    private static string FormatSignedLength(double value, CultureInfo culture) =>
        $"{value.ToString("+0;-0;0", culture)} mm";

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Brush FindBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}

internal readonly record struct GableRoofAngleAnnotation(
    Point Anchor,
    Point RoofCenterlinePoint,
    Point ReferenceStart,
    Point ReferenceEnd,
    Point LabelOrigin);
