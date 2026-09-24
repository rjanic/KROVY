using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaPen = System.Windows.Media.Pen;
using MediaPoint = System.Windows.Point;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// WPF roof-section view driven by <see cref="AutomaticPurlinSectionPresentation"/>.
/// Fits model millimetres into the viewport and lays out labels in screen space.
/// </summary>
internal sealed class AutomaticPurlinRoofSectionView : FrameworkElement
{
    public static readonly DependencyProperty PresentationProperty =
        DependencyProperty.Register(
            nameof(Presentation),
            typeof(AutomaticPurlinSectionPresentation),
            typeof(AutomaticPurlinRoofSectionView),
            new FrameworkPropertyMetadata(
                AutomaticPurlinSectionPresentation.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MarginPx = 28d;
    private const double LabelPaddingPx = 4d;
    /// <summary>Base gap from member top to label bottom before the one-box lift.</summary>
    internal const double LabelBaseOffsetPx = 18d;
    private const double LaneStepPx = 56d;
    internal const double LabelWidthPx = 132d;
    internal const double LabelHeightPx = 54d;
    /// <summary>
    /// Preferred vertical gap from member top to label bottom: base offset plus one
    /// full label-box height so annotation boxes sit systematically higher.
    /// </summary>
    internal const double LabelOffsetPx = LabelBaseOffsetPx + LabelHeightPx;
    private const double LabelContentPadPx = 6d;
    private const double MinModelExtentMm = 1d;
    internal const double MaximumPreferredLeaderDistancePx = 140d;

    /// <summary>
    /// Interactive rafter dimension control — related to member labels but visually
    /// elevated with a warm wood/bronze accent so it reads as clickable.
    /// </summary>
    internal const string RafterAnnotationStyleFamily = "InteractiveRafterAnnotationButton";
    /// <summary>
    /// Preferred vertical gap from rafter underside to label top, locked to the
    /// historical compact box height so growing the button does not push it down.
    /// </summary>
    internal const double RafterLabelGapReferenceHeightPx = 36d;
    /// <summary>Preferred gap from rafter underside to the top of the rafter label box.</summary>
    internal const double RafterLabelBelowBoxMultiples = 3d;
    internal const double RafterLabelPreferredOffsetPx =
        RafterLabelBelowBoxMultiples * RafterLabelGapReferenceHeightPx;
    /// <summary>Three-line interactive box: caption, W×H, click hint.</summary>
    internal const double RafterLabelHeightPx = 52d;
    private const double RafterLabelPressOffsetPx = 1d;
    private const double RafterUpdateFlashDurationMs = 450d;

    private static readonly Typeface LabelTypeface = new(
        new MediaFontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly Typeface DetailTypeface = new(
        new MediaFontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly Typeface HintTypeface = new(
        new MediaFontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly MediaBrush LeaderBrush = CreateFrozenBrush(MediaColor.FromRgb(0x37, 0x41, 0x51));
    private static readonly MediaBrush LabelBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0x6E, 0xFF, 0xFF, 0xFF));
    private static readonly MediaBrush LabelBorderBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0x90, 0xD1, 0xD5, 0xDB));
    private static readonly MediaBrush LabelTextBrush = CreateFrozenBrush(MediaColor.FromRgb(0x1F, 0x29, 0x37));

    // Warm wood / bronze interactive rafter control (distinct from static labels).
    private static readonly MediaBrush RafterButtonBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xE8, 0xFB, 0xF5, 0xEE));
    private static readonly MediaBrush RafterButtonHoverBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xF2, 0xFF, 0xF8, 0xF0));
    private static readonly MediaBrush RafterButtonPressedBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xE0, 0xF3, 0xEA, 0xE0));
    private static readonly MediaBrush RafterButtonFlashBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xF0, 0xFF, 0xF4, 0xE8));
    private static readonly MediaBrush RafterButtonBorderBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0xB0, 0x7D, 0x4A));
    private static readonly MediaBrush RafterButtonHoverBorderBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0xC9, 0x92, 0x55));
    private static readonly MediaBrush RafterButtonPressedBorderBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x8F, 0x64, 0x38));
    private static readonly MediaBrush RafterButtonTopHighlightBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
    private static readonly MediaBrush RafterButtonBottomEdgeBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0x55, 0x6B, 0x45, 0x28));
    private static readonly MediaBrush RafterButtonShadowBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0x38, 0x4A, 0x32, 0x1C));
    private static readonly MediaBrush RafterHintTextBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x7A, 0x5A, 0x3A));
    private static readonly MediaBrush ReferencePlaneBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x6B, 0x7C, 0x8C));
    private static readonly MediaBrush ReferenceLabelBackgroundBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xDC, 0xF4, 0xF6, 0xF8));
    private static readonly MediaBrush ReferenceLabelBorderBrush =
        CreateFrozenBrush(MediaColor.FromArgb(0xB0, 0x6B, 0x7C, 0x8C));
    private static readonly MediaBrush ReferenceLabelTextBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x3A, 0x4A, 0x58));
    private const double ReferenceLabelWidthPx = 64d;
    private const double ReferenceLabelHeightPx = 20d;
    private const double ReferenceLineOverhangPx = 18d;

    private Rect _rafterLabelHitBounds = Rect.Empty;
    private bool _rafterLabelHovered;
    private bool _rafterLabelPressed;
    private bool _rafterDimensionFlashActive;
    private string? _lastRafterDimensionText;
    private System.Windows.Threading.DispatcherTimer? _rafterFlashTimer;

    public AutomaticPurlinSectionPresentation? Presentation
    {
        get => (AutomaticPurlinSectionPresentation?)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    internal event EventHandler? RafterLabelClicked;

    public AutomaticPurlinRoofSectionView()
    {
        Focusable = false;
        Cursor = System.Windows.Input.Cursors.Arrow;
        // Localized user tooltip only — never bind DEBUG / Editor diagnostics here.
        ToolTip = ResolveRafterUserToolTip();
        ToolTipService.AddToolTipOpeningHandler(
            this,
            AutomaticPurlinRoofSectionView_ToolTipOpening);
    }

    /// <summary>
    /// Resolves the user-facing rafter tooltip. Always localized; never a DEBUG token.
    /// </summary>
    internal static string ResolveRafterUserToolTip() =>
        UiStrings.GetString("AutomaticPurlin_SelectRafterHint");

    private void AutomaticPurlinRoofSectionView_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        // Show the localized tip only over the clickable rafter control — not the
        // whole section, and never as a vehicle for command-line diagnostics.
        if (_rafterLabelHitBounds.IsEmpty ||
            !IsMouseOver ||
            !_rafterLabelHitBounds.Contains(Mouse.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        var localized = ResolveRafterUserToolTip();
        if (ToolTip is not string text ||
            !string.Equals(text, localized, StringComparison.Ordinal) ||
            text.Contains("ROOF_PURLIN_RAFTER_PICK", StringComparison.Ordinal))
        {
            ToolTip = localized;
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != PresentationProperty)
        {
            return;
        }

        var next = e.NewValue as AutomaticPurlinSectionPresentation;
        var dimension = next?.RafterSectionDimensionText;
        if (!string.IsNullOrWhiteSpace(dimension) &&
            !string.Equals(dimension, _lastRafterDimensionText, StringComparison.Ordinal))
        {
            if (_lastRafterDimensionText is not null)
            {
                BeginRafterDimensionFlash();
            }

            _lastRafterDimensionText = dimension;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    /// <summary>
    /// When true, OnRender draws diagnostic seating overlays (BuildRafters edges,
    /// SVG-master edges, member contact corners). Default false — production look unchanged.
    /// </summary>
    internal static bool ShowSeatingDiagnostics { get; set; }

    private static readonly MediaBrush DiagBuildRaftersBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x00, 0x90, 0x00));
    private static readonly MediaBrush DiagSvgMasterBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0xC6, 0x28, 0x28));
    private static readonly MediaBrush DiagContactBrush =
        CreateFrozenBrush(MediaColor.FromRgb(0x15, 0x65, 0xC0));

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var presentation = Presentation ?? AutomaticPurlinSectionPresentation.Empty;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1d || height <= 1d)
        {
            _rafterLabelHitBounds = Rect.Empty;
            return;
        }

        drawingContext.DrawRectangle(
            MediaBrushes.Transparent,
            null,
            new Rect(0, 0, width, height));

        var transform = CreateModelToViewTransform(presentation, width, height);
        drawingContext.DrawDrawing(
            AutomaticPurlinSectionSvgTemplate.CreateMasterScene(presentation, transform.ToView));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawReferencePlane(drawingContext, presentation, transform, pixelsPerDip);
        DrawMemberCenterAxes(drawingContext, presentation, transform);
        if (ShowSeatingDiagnostics)
        {
            DrawSeatingDiagnostics(drawingContext, presentation, transform);
        }

        DrawRafterDimension(drawingContext, presentation, transform, pixelsPerDip);
        DrawLabels(drawingContext, presentation, transform, width, height, pixelsPerDip);
    }

    /// <summary>
    /// DEBUG overlay: green = BuildRafters edges, red = SVG-master edges after affine,
    /// blue dots = member outer-top corners. Does not alter production SVG fills.
    /// </summary>
    private static void DrawSeatingDiagnostics(
        DrawingContext dc,
        AutomaticPurlinSectionPresentation presentation,
        ModelViewTransform transform)
    {
        foreach (var side in new[]
                 {
                     AutomaticPurlinSectionSide.Left,
                     AutomaticPurlinSectionSide.Right,
                 })
        {
            var rafter = presentation.Rafters.FirstOrDefault(r => r.Side == side);
            if (rafter is null || rafter.Corners.Count < 4)
            {
                continue;
            }

            DrawModelEdge(
                dc,
                transform,
                DiagBuildRaftersBrush,
                rafter.Corners[0],
                rafter.Corners[1]);
            DrawModelEdge(
                dc,
                transform,
                DiagBuildRaftersBrush,
                rafter.Corners[3],
                rafter.Corners[2]);

            if (AutomaticPurlinSectionSvgTemplate.TryResolveSvgMasterRafterLowerUpperLocalZMm(
                    presentation.Rafters,
                    side,
                    rafter.Corners[0].XMm,
                    out _,
                    out _))
            {
                // Sample SVG edges along the rafter X span.
                var x0 = Math.Min(rafter.CenterLine.X1Mm, rafter.CenterLine.X2Mm);
                var x1 = Math.Max(rafter.CenterLine.X1Mm, rafter.CenterLine.X2Mm);
                MediaPoint? prevUpper = null;
                MediaPoint? prevLower = null;
                for (var i = 0; i <= 16; i++)
                {
                    var x = x0 + (x1 - x0) * i / 16d;
                    if (!AutomaticPurlinSectionSvgTemplate.TryResolveSvgMasterRafterLowerUpperLocalZMm(
                            presentation.Rafters,
                            side,
                            x,
                            out var lo,
                            out var hi))
                    {
                        continue;
                    }

                    var up = transform.ToView(x, hi);
                    var low = transform.ToView(x, lo);
                    if (prevUpper is not null)
                    {
                        var pen = new MediaPen(DiagSvgMasterBrush, 1.5);
                        pen.Freeze();
                        dc.DrawLine(pen, prevUpper.Value, up);
                        dc.DrawLine(pen, prevLower!.Value, low);
                    }

                    prevUpper = up;
                    prevLower = low;
                }
            }
        }

        foreach (var member in presentation.Members)
        {
            if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
            {
                continue;
            }

            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side,
                    member.WidthMm);
            var top = transform.ToView(contactX, member.MemberTopZMm);
            dc.DrawEllipse(DiagContactBrush, null, top, 4d, 4d);
        }
    }

    private static void DrawModelEdge(
        DrawingContext dc,
        ModelViewTransform transform,
        MediaBrush brush,
        AutomaticPurlinSectionPointMm a,
        AutomaticPurlinSectionPointMm b)
    {
        var pen = new MediaPen(brush, 1.5) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        dc.DrawLine(pen, transform.ToView(a.XMm, a.ZMm), transform.ToView(b.XMm, b.ZMm));
    }

    private static void DrawReferencePlane(
        DrawingContext dc,
        AutomaticPurlinSectionPresentation presentation,
        ModelViewTransform transform,
        double pixelsPerDip)
    {
        var plane = presentation.ReferencePlane;
        if (plane is null || string.IsNullOrWhiteSpace(plane.LabelText))
        {
            return;
        }

        var left = transform.ToView(presentation.MinXMm, plane.VisualLocalZMm);
        var right = transform.ToView(presentation.MaxXMm, plane.VisualLocalZMm);
        var y = left.Y;
        var x1 = Math.Min(left.X, right.X) - ReferenceLineOverhangPx;
        var x2 = Math.Max(left.X, right.X) + ReferenceLineOverhangPx;
        var pen = RoofSectionDiagramStyle.CreateDatumPen(ReferencePlaneBrush);
        pen.Freeze();
        dc.DrawLine(pen, new MediaPoint(x1, y), new MediaPoint(x2, y));

        var labelWidth = ReferenceLabelWidthPx;
        var labelHeight = ReferenceLabelHeightPx;
        var centerX = (x1 + x2) / 2d;
        var bounds = new Rect(
            centerX - labelWidth / 2d,
            y - labelHeight / 2d,
            labelWidth,
            labelHeight);
        var borderPen = new MediaPen(ReferenceLabelBorderBrush, 1.0);
        borderPen.Freeze();
        dc.DrawRoundedRectangle(ReferenceLabelBackgroundBrush, borderPen, bounds, 3, 3);
        DrawCenteredLabelText(
            dc,
            plane.LabelText,
            DetailTypeface,
            10d,
            ReferenceLabelTextBrush,
            bounds.X + 4d,
            bounds.Y + 2d,
            bounds.Width - 8d,
            pixelsPerDip);
    }

    /// <summary>
    /// Draws dash-dot center axes (3×W horizontal, 3×H vertical) and a center point
    /// for each wall-plate / ridge / intermediate timber member.
    /// </summary>
    private static void DrawMemberCenterAxes(
        DrawingContext dc,
        AutomaticPurlinSectionPresentation presentation,
        ModelViewTransform transform)
    {
        if (presentation.Members.Count == 0)
        {
            return;
        }

        var pen = RoofSectionDiagramStyle.CreateMemberCenterAxisPen();
        pen.Freeze();
        var pointBrush = RoofSectionDiagramStyle.MemberCenterPointBrush;
        var pointRadius = RoofSectionDiagramStyle.MemberCenterPointRadiusPx;

        foreach (var member in presentation.Members)
        {
            if (!AutomaticPurlinMemberCenterAxisGeometry.ShouldDrawAxes(member.Role))
            {
                continue;
            }

            var axes = AutomaticPurlinMemberCenterAxisGeometry.Create(member);
            if (axes.IsEmpty)
            {
                continue;
            }

            var h0 = transform.ToView(axes.HorizontalStartXMm, axes.HorizontalZMm);
            var h1 = transform.ToView(axes.HorizontalEndXMm, axes.HorizontalZMm);
            var v0 = transform.ToView(axes.VerticalXMm, axes.VerticalStartZMm);
            var v1 = transform.ToView(axes.VerticalXMm, axes.VerticalEndZMm);
            var center = transform.ToView(axes.CenterXMm, axes.CenterZMm);

            dc.DrawLine(pen, h0, h1);
            dc.DrawLine(pen, v0, v1);
            dc.DrawEllipse(
                pointBrush,
                null,
                center,
                pointRadius,
                pointRadius);
        }
    }

    private void DrawRafterDimension(
        DrawingContext dc,
        AutomaticPurlinSectionPresentation presentation,
        ModelViewTransform transform,
        double pixelsPerDip)
    {
        var rafter = presentation.Rafters.FirstOrDefault(candidate =>
            candidate.Side == AutomaticPurlinSectionSide.Left)
            ?? presentation.Rafters.FirstOrDefault();
        if (rafter is null || rafter.Corners.Count < 4 || presentation.RafterHeightMm <= 0d)
        {
            _rafterLabelHitBounds = Rect.Empty;
            return;
        }

        // BuildRafters: [0]→[1] upper edge, [3]→[2] lower edge along the slope.
        // Anchor on the lower edge mid-span so the label sits under the rafters.
        const double t = 0.42d;
        var lower = Lerp(rafter.Corners[3], rafter.Corners[2], t);
        var anchor = transform.ToView(lower.XMm, lower.ZMm);

        var boxWidth = LabelWidthPx;
        var boxHeight = RafterLabelHeightPx;
        var preferredTop = anchor.Y + RafterLabelPreferredOffsetPx;
        var bounds = new Rect(
            anchor.X - boxWidth / 2d,
            preferredTop,
            boxWidth,
            boxHeight);
        _rafterLabelHitBounds = bounds;

        var pressOffset = _rafterLabelPressed ? RafterLabelPressOffsetPx : 0d;
        var drawBounds = new Rect(
            bounds.X,
            bounds.Y + pressOffset,
            bounds.Width,
            bounds.Height);

        var leaderPen = new MediaPen(LeaderBrush, 1.0);
        leaderPen.Freeze();
        var leaderAttach = new MediaPoint(
            drawBounds.X + drawBounds.Width / 2d,
            drawBounds.Y);
        dc.DrawLine(leaderPen, anchor, leaderAttach);

        DrawInteractiveRafterButtonChrome(dc, drawBounds);

        var title = string.IsNullOrWhiteSpace(presentation.RafterCaptionText)
            ? presentation.RafterDimensionText
            : presentation.RafterCaptionText;
        var dimension = presentation.RafterSectionDimensionText;
        var hint = UiStrings.GetString("AutomaticPurlin_SelectRafterClickHint");
        var contentWidth = drawBounds.Width - 2d * LabelContentPadPx;
        const double titleSize = 11d;
        const double dimSize = 11d;
        const double hintSize = 8.5d;
        const double titleLine = 13d;
        const double dimLine = 13d;
        const double hintLine = 11d;
        var contentHeight = titleLine + dimLine + hintLine;
        var lineY = drawBounds.Y + Math.Max(
            LabelContentPadPx,
            (drawBounds.Height - contentHeight) / 2d);

        DrawCenteredLabelText(
            dc,
            title,
            LabelTypeface,
            titleSize,
            LabelTextBrush,
            drawBounds.X + LabelContentPadPx,
            lineY,
            contentWidth,
            pixelsPerDip);
        lineY += titleLine;
        DrawCenteredLabelText(
            dc,
            dimension,
            DetailTypeface,
            dimSize,
            LabelTextBrush,
            drawBounds.X + LabelContentPadPx,
            lineY,
            contentWidth,
            pixelsPerDip);
        lineY += dimLine;
        DrawCenteredLabelText(
            dc,
            hint,
            HintTypeface,
            hintSize,
            RafterHintTextBrush,
            drawBounds.X + LabelContentPadPx,
            lineY,
            contentWidth,
            pixelsPerDip);
    }

    private void DrawInteractiveRafterButtonChrome(DrawingContext dc, Rect bounds)
    {
        var shadowOffset = _rafterLabelPressed ? 0.5d : (_rafterLabelHovered ? 2.5d : 2d);
        var shadowRect = new Rect(
            bounds.X + 0.5d,
            bounds.Y + shadowOffset,
            bounds.Width,
            bounds.Height);
        dc.DrawRoundedRectangle(RafterButtonShadowBrush, null, shadowRect, 5, 5);

        MediaBrush background;
        MediaBrush borderBrush;
        double borderThickness;
        if (_rafterLabelPressed)
        {
            background = RafterButtonPressedBackgroundBrush;
            borderBrush = RafterButtonPressedBorderBrush;
            borderThickness = 1.5d;
        }
        else if (_rafterDimensionFlashActive)
        {
            background = RafterButtonFlashBackgroundBrush;
            borderBrush = RafterButtonHoverBorderBrush;
            borderThickness = 1.6d;
        }
        else if (_rafterLabelHovered)
        {
            background = RafterButtonHoverBackgroundBrush;
            borderBrush = RafterButtonHoverBorderBrush;
            borderThickness = 1.6d;
        }
        else
        {
            background = RafterButtonBackgroundBrush;
            borderBrush = RafterButtonBorderBrush;
            borderThickness = 1.35d;
        }

        var borderPen = new MediaPen(borderBrush, borderThickness);
        borderPen.Freeze();
        dc.DrawRoundedRectangle(background, borderPen, bounds, 5, 5);

        // Light top-edge highlight and slightly darker bottom edge (mild elevation).
        var topHighlight = new Rect(bounds.X + 2d, bounds.Y + 1.2d, bounds.Width - 4d, 1.2d);
        dc.DrawRoundedRectangle(RafterButtonTopHighlightBrush, null, topHighlight, 1, 1);
        var bottomEdge = new Rect(
            bounds.X + 2.5d,
            bounds.Y + bounds.Height - 2.2d,
            bounds.Width - 5d,
            1.1d);
        dc.DrawRoundedRectangle(RafterButtonBottomEdgeBrush, null, bottomEdge, 1, 1);
    }

    private void BeginRafterDimensionFlash()
    {
        _rafterFlashTimer?.Stop();
        _rafterDimensionFlashActive = true;
        InvalidateVisual();
        _rafterFlashTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RafterUpdateFlashDurationMs),
        };
        _rafterFlashTimer.Tick -= RafterFlashTimer_Tick;
        _rafterFlashTimer.Tick += RafterFlashTimer_Tick;
        _rafterFlashTimer.Start();
    }

    private void RafterFlashTimer_Tick(object? sender, EventArgs e)
    {
        _rafterFlashTimer?.Stop();
        if (!_rafterDimensionFlashActive)
        {
            return;
        }

        _rafterDimensionFlashActive = false;
        InvalidateVisual();
    }

    private static void DrawCenteredLabelText(
        DrawingContext dc,
        string text,
        Typeface typeface,
        double size,
        MediaBrush brush,
        double x,
        double y,
        double maxWidth,
        double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1d, maxWidth),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(formatted, new MediaPoint(x, y));
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = _rafterLabelHitBounds.Contains(e.GetPosition(this));
        if (hovered == _rafterLabelHovered)
        {
            Cursor = hovered ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            return;
        }

        _rafterLabelHovered = hovered;
        if (!hovered)
        {
            _rafterLabelPressed = false;
        }

        Cursor = hovered ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_rafterLabelHovered && !_rafterLabelPressed)
        {
            return;
        }

        _rafterLabelHovered = false;
        _rafterLabelPressed = false;
        Cursor = System.Windows.Input.Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_rafterLabelHitBounds.Contains(e.GetPosition(this)))
        {
            return;
        }

        e.Handled = true;
        _rafterLabelPressed = true;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasPressed = _rafterLabelPressed;
        var inside = _rafterLabelHitBounds.Contains(e.GetPosition(this));
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _rafterLabelPressed = false;
        InvalidateVisual();
        if (!wasPressed || !inside)
        {
            return;
        }

        e.Handled = true;
        RafterLabelClicked?.Invoke(this, EventArgs.Empty);
    }

    private static AutomaticPurlinSectionPointMm Lerp(
        AutomaticPurlinSectionPointMm a,
        AutomaticPurlinSectionPointMm b,
        double t) =>
        new(
            a.XMm + (b.XMm - a.XMm) * t,
            a.ZMm + (b.ZMm - a.ZMm) * t);

    private static void DrawLabels(
        DrawingContext dc,
        AutomaticPurlinSectionPresentation presentation,
        ModelViewTransform transform,
        double viewportWidth,
        double viewportHeight,
        double pixelsPerDip)
    {
        if (presentation.Members.Count == 0)
        {
            return;
        }

        var requests = new List<AutomaticPurlinSectionLabelLanes.LabelRequest>(
            presentation.Members.Count);
        var anchors = new Dictionary<string, MediaPoint>(StringComparer.Ordinal);
        foreach (var member in presentation.Members)
        {
            var top = transform.ToView(
                member.CenterXMm,
                member.CenterZMm + member.HeightMm / 2d);
            anchors[member.StableKey] = top;
            var preferBelow = member.Role == RoofAutomaticPurlinGeneratorRole.Ridge;
            requests.Add(AutomaticPurlinSectionLabelLanes.CreateCompactRequest(
                member.StableKey,
                top.X,
                top.Y,
                (int)member.Side,
                viewportWidth,
                viewportHeight,
                LabelWidthPx,
                LabelHeightPx,
                MarginPx,
                LabelOffsetPx,
                preferBelow));
        }

        var placements = AutomaticPurlinSectionLabelLanes.Assign(
            requests,
            LaneStepPx,
            LabelPaddingPx,
            lanesGrowNegativeY: true);

        var leaderPen = new MediaPen(LeaderBrush, 1.0);
        leaderPen.Freeze();
        var borderPen = new MediaPen(LabelBorderBrush, 1.0);
        borderPen.Freeze();

        var byKey = presentation.Members.ToDictionary(
            member => member.StableKey,
            StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            if (!byKey.TryGetValue(placement.Id, out var member) ||
                !anchors.TryGetValue(placement.Id, out var anchor))
            {
                continue;
            }

            var labelLeaderPoint = placement.PreferBelow
                ? new MediaPoint(placement.X + placement.Width / 2d, placement.Y)
                : new MediaPoint(
                    placement.X + placement.Width / 2d,
                    placement.Y + placement.Height);
            dc.DrawLine(leaderPen, anchor, labelLeaderPoint);

            var bounds = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
            dc.DrawRoundedRectangle(LabelBackgroundBrush, borderPen, bounds, 4, 4);

            var contentWidth = placement.Width - 2d * LabelContentPadPx;
            // Title + WxH + HH + SH — HH must render above SH.
            const double titleSize = 11d;
            const double dimSize = 11d;
            const double edgeSize = 9.5d;
            const double titleLine = 12d;
            const double dimLine = 12d;
            const double edgeLine = 11d;
            var contentHeight = titleLine + dimLine + edgeLine + edgeLine;
            var lineY = bounds.Y + Math.Max(
                LabelContentPadPx,
                (bounds.Height - contentHeight) / 2d);

            DrawLabelText(
                dc,
                member.Title,
                LabelTypeface,
                titleSize,
                bounds.X + LabelContentPadPx,
                lineY,
                contentWidth,
                pixelsPerDip);
            lineY += titleLine;
            DrawLabelText(
                dc,
                member.DimensionText,
                DetailTypeface,
                dimSize,
                bounds.X + LabelContentPadPx,
                lineY,
                contentWidth,
                pixelsPerDip);
            lineY += dimLine;
            DrawLabelText(
                dc,
                member.TopText,
                DetailTypeface,
                edgeSize,
                bounds.X + LabelContentPadPx,
                lineY,
                contentWidth,
                pixelsPerDip);
            lineY += edgeLine;
            DrawLabelText(
                dc,
                member.BottomText,
                DetailTypeface,
                edgeSize,
                bounds.X + LabelContentPadPx,
                lineY,
                contentWidth,
                pixelsPerDip);
        }
    }

    private static void DrawLabelText(
        DrawingContext dc,
        string text,
        Typeface typeface,
        double size,
        double x,
        double y,
        double maxWidth,
        double pixelsPerDip)
    {
        var formatted = CreateFormatted(text, typeface, size, pixelsPerDip, maxWidth);
        dc.DrawText(formatted, new MediaPoint(x, y));
    }

    private static FormattedText CreateFormatted(
        string text,
        Typeface typeface,
        double size,
        double pixelsPerDip,
        double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            size,
            LabelTextBrush,
            pixelsPerDip);
        formatted.MaxTextWidth = Math.Max(8d, maxWidth);
        formatted.TextAlignment = TextAlignment.Center;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    private static ModelViewTransform CreateModelToViewTransform(
        AutomaticPurlinSectionPresentation presentation,
        double viewportWidth,
        double viewportHeight)
    {
        var minX = presentation.MinXMm;
        var maxX = presentation.MaxXMm;
        var minZ = presentation.MinZMm;
        var maxZ = presentation.MaxZMm;
        var extentX = Math.Max(MinModelExtentMm, maxX - minX);
        var extentZ = Math.Max(MinModelExtentMm, maxZ - minZ);

        var availableWidth = Math.Max(1d, viewportWidth - 2d * MarginPx);
        var availableHeight = Math.Max(1d, viewportHeight - 2d * MarginPx);
        var scale = Math.Min(availableWidth / extentX, availableHeight / extentZ);
        var contentWidth = extentX * scale;
        var contentHeight = extentZ * scale;
        var offsetX = (viewportWidth - contentWidth) / 2d - minX * scale;
        // WPF Y grows downward; model Z grows upward.
        var offsetY = MarginPx + (availableHeight - contentHeight) / 2d + maxZ * scale;
        return new ModelViewTransform(scale, offsetX, offsetY);
    }

    private static MediaBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly record struct ModelViewTransform(double Scale, double OffsetX, double OffsetY)
    {
        internal MediaPoint ToView(double xMm, double zMm) =>
            new(xMm * Scale + OffsetX, OffsetY - zMm * Scale);
    }
}
