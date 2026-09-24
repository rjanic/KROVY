using System.Windows;
using System.Windows.Media;
using AcKrovy.Core.Models.Roofs;
using System.Linq;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using MediaPoint = System.Windows.Point;
using MediaSize = System.Windows.Size;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Native WPF conversion of krov.svg as one master section scene. Every semantic
/// element keeps its original SVG coordinates; production data supplies only the
/// documented dynamic visibility, member dimensions, position and seating changes.
/// </summary>
internal static class AutomaticPurlinSectionSvgTemplate
{
    internal const string RafterLeftId = "rafter-left";
    internal const string RafterLeftCorelId = "rafter-left_x002c_";
    internal const string RafterRightId = "rafter-right";
    internal const string WallLeftId = "wall-left";
    internal const string WallRightId = "wall-right";
    internal const string WallPlateLeftId = "wallplate-left";
    internal const string WallPlateRightId = "wallplate-right";
    internal const string PurlinLeftId = "purlin-left-1";
    internal const string PurlinRightId = "purlin-right-1";
    internal const string RidgePurlinId = "ridge-purlin";

    internal static readonly Rect MasterViewBox = new(0d, 0d, 11562.1457d, 4179.493d);

    private static readonly MediaBrush RafterLeftBrush = CreateBrush(0xE3, 0xAC, 0x7D);
    private static readonly MediaBrush RafterRightBrush = CreateBrush(0xBA, 0x82, 0x52);
    private static readonly MediaBrush TimberBrush = CreateBrush(0x87, 0x5D, 0x38);
    private static readonly MediaBrush WallBrush = CreateBrush(0x89, 0x89, 0x89);
    private static readonly MediaPen RafterOutlinePen = CreatePen(0x2B, 0x2A, 0x29, 1.25d);
    private static readonly MediaPen TimberOutlinePen = CreatePen(0x00, 0x00, 0x00, 0.9d);
    private static readonly MediaPen WallOutlinePen = CreatePen(0x2B, 0x2A, 0x29, 1.1d);

    // Original SVG paint order. Local path coordinates are deliberately not normalized.
    private static readonly IReadOnlyList<MasterElement> MasterElements =
    [
        Element(RafterRightId, "M11561.9977,3289.3138 l-5905.7403,-3289.1444 -332.8412,185.3726 332.8412,185.3727 5905.7403,3289.1443 0,-370.7451 Z", RafterRightBrush, RafterOutlinePen),
        Element(RafterLeftId, "M0.1479,3521.0295 l5656.1095,-3150.1148 -332.8412,-185.3727 -5323.2683,2964.7424 0,370.7451 Z", RafterLeftBrush, RafterOutlinePen),
        Element(RidgePurlinId, "M5509.8217,370.9146 l292.8715,0 0,415.2525 -292.8715,0 0,-415.2525 Z", TimberBrush, TimberOutlinePen),
        Element(PurlinLeftId, "M3341.456,1578.5647 l292.8714,0 0,415.2525 -292.8714,0 0,-415.2525 Z", TimberBrush, TimberOutlinePen),
        Element(PurlinRightId, "M7971.0587,1578.5647 l-292.8714,0 0,415.2525 292.8714,0 0,-415.2525 Z", TimberBrush, TimberOutlinePen),
        Element(WallPlateLeftId, "M596.3263,3105.9068 l366.6239,0 0,311.8737 -366.6239,0 0,-311.8737 Z", TimberBrush, TimberOutlinePen),
        Element(WallPlateRightId, "M10799.3987,3152.2501 l-366.6238,0 0,311.8736 366.6238,0 0,-311.8736 Z", TimberBrush, TimberOutlinePen),
        Element(WallLeftId, "M461.3265,3417.7806 l0,715.2214 636.6236,0 0,-715.2214 -134.9999,0 -366.6238,0 -134.9999,0 Z", WallBrush, WallOutlinePen),
        Element(WallRightId, "M10297.775,3464.1237 l0,715.2214 636.6236,0 0,-715.2214 -134.9999,0 -366.6238,0 -134.9999,0 Z", WallBrush, WallOutlinePen),
    ];

    internal static IReadOnlyList<string> ElementIds { get; } =
    [
        RafterLeftId, RafterRightId, WallLeftId, WallRightId,
        WallPlateLeftId, WallPlateRightId, PurlinLeftId, PurlinRightId,
        RidgePurlinId,
    ];

    internal static bool IsRafterLeftElement(string elementId) =>
        string.Equals(elementId, RafterLeftId, StringComparison.Ordinal) ||
        string.Equals(elementId, RafterLeftCorelId, StringComparison.Ordinal);

    internal static Rect GetMasterElementBounds(string elementId) =>
        FindElement(elementId).Geometry.Bounds;

    internal static Matrix CreateAspectPreservingViewportTransform(MediaSize viewport, double margin)
    {
        var availableWidth = Math.Max(1d, viewport.Width - 2d * margin);
        var availableHeight = Math.Max(1d, viewport.Height - 2d * margin);
        var scale = Math.Min(availableWidth / MasterViewBox.Width, availableHeight / MasterViewBox.Height);
        var offsetX = (viewport.Width - MasterViewBox.Width * scale) / 2d;
        var offsetY = (viewport.Height - MasterViewBox.Height * scale) / 2d;
        return new Matrix(scale, 0d, 0d, scale, offsetX, offsetY);
    }

    internal static string GetMemberTemplateElementId(AutomaticPurlinSectionMemberMm member) =>
        member.Role switch
        {
            RoofAutomaticPurlinGeneratorRole.WallPlate => member.Side == AutomaticPurlinSectionSide.Left
                ? WallPlateLeftId
                : WallPlateRightId,
            RoofAutomaticPurlinGeneratorRole.Ridge => RidgePurlinId,
            _ => member.Side == AutomaticPurlinSectionSide.Left ? PurlinLeftId : PurlinRightId,
        };

    internal static DrawingGroup CreateMasterScene(
        AutomaticPurlinSectionPresentation presentation,
        Func<double, double, MediaPoint> toView)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(toView);
        var drawing = new DrawingGroup();

        // Authoritative rafter bodies: physical BuildRafters corner polygons through toView.
        // Do not draw the unconstrained SVG master rafter paths (3-point upper-tip affine).
        // Paint order matches the original master: right rafter, then left.
        foreach (var side in new[]
                 {
                     AutomaticPurlinSectionSide.Right,
                     AutomaticPurlinSectionSide.Left,
                 })
        {
            var rafter = presentation.Rafters.FirstOrDefault(candidate => candidate.Side == side);
            if (rafter is not null)
            {
                AddPhysicalRafterDrawing(drawing, rafter, toView);
            }
        }

        foreach (var element in MasterElements)
        {
            if (element.Id is RafterLeftId or RafterRightId)
            {
                continue;
            }

            if (element.Id is WallLeftId or WallRightId)
            {
                var side = element.Id == WallLeftId
                    ? AutomaticPurlinSectionSide.Left
                    : AutomaticPurlinSectionSide.Right;
                var wall = presentation.Walls.FirstOrDefault(candidate => candidate.Side == side);
                var plate = presentation.Members.FirstOrDefault(candidate =>
                    candidate.Role == RoofAutomaticPurlinGeneratorRole.WallPlate && candidate.Side == side);
                if (wall is not null)
                {
                    AddDrawing(drawing, element, CreateWallTransform(element, wall, plate, toView));
                }

                continue;
            }

            foreach (var member in presentation.Members.Where(candidate =>
                         string.Equals(GetMemberTemplateElementId(candidate), element.Id, StringComparison.Ordinal)))
            {
                AddDrawing(drawing, element, CreateMemberTransform(element, member, toView));
            }
        }

        drawing.Freeze();
        return drawing;
    }

    /// <summary>
    /// Extracts the final view-space rafter polygon submitted to WPF by
    /// <see cref="CreateMasterScene"/> (independent of BuildRafters helpers).
    /// </summary>
    internal static bool TryGetRenderedRafterViewPolygon(
        DrawingGroup scene,
        AutomaticPurlinSectionSide side,
        out IReadOnlyList<MediaPoint> viewPoints)
    {
        viewPoints = Array.Empty<MediaPoint>();
        ArgumentNullException.ThrowIfNull(scene);
        if (side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
        {
            return false;
        }

        var expectedBrush = side == AutomaticPurlinSectionSide.Left
            ? RafterLeftBrush
            : RafterRightBrush;
        foreach (var child in scene.Children)
        {
            if (child is not GeometryDrawing geometryDrawing ||
                !ReferenceEquals(geometryDrawing.Brush, expectedBrush) ||
                geometryDrawing.Geometry is null)
            {
                continue;
            }

            if (!TryFlattenGeometryToViewPoints(geometryDrawing.Geometry, out var points) ||
                points.Count < 4)
            {
                continue;
            }

            viewPoints = points;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Model-space Z of the drawn rafter top-outer eave corners (BuildRafters upper face).
    /// </summary>
    internal static bool TryResolveVisualSourceEaveTopLocalZMm(
        AutomaticPurlinSectionPresentation presentation,
        out double localZMm)
    {
        localZMm = 0d;
        ArgumentNullException.ThrowIfNull(presentation);
        var left = presentation.Rafters.FirstOrDefault(r => r.Side == AutomaticPurlinSectionSide.Left);
        var right = presentation.Rafters.FirstOrDefault(r => r.Side == AutomaticPurlinSectionSide.Right);
        if (left is null || right is null ||
            left.Corners.Count < 2 || right.Corners.Count < 2)
        {
            return false;
        }

        // Corners[0]/[1] = upper face (eave → ridge). Eave tip = lower-Z upper corner.
        var leftEaveTop = LowerEndPoint(left.Corners[0], left.Corners[1]);
        var rightEaveTop = LowerEndPoint(right.Corners[0], right.Corners[1]);
        localZMm = (leftEaveTop.ZMm + rightEaveTop.ZMm) / 2d;
        return double.IsFinite(localZMm);
    }

    /// <summary>
    /// Interpolates seating along the drawn rafter thickness at <paramref name="xMm"/>.
    /// Fraction 0 = lower edge, 1 = upper edge. Drawn edges are BuildRafters polygons.
    /// </summary>
    internal static bool TryInterpolateVisualRafterSeatingLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double xMm,
        double seatingFraction,
        out double edgeLocalZMm,
        out double lowerLocalZMm)
    {
        edgeLocalZMm = 0d;
        lowerLocalZMm = 0d;
        if (side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right) ||
            !double.IsFinite(xMm) ||
            !double.IsFinite(seatingFraction))
        {
            return false;
        }

        if (!TryResolveBuildRaftersLowerUpperLocalZMm(
                rafters,
                side,
                xMm,
                out lowerLocalZMm,
                out var upperZ))
        {
            return false;
        }

        var p = Math.Clamp(seatingFraction, 0d, 1d);
        edgeLocalZMm = lowerLocalZMm + p * (upperZ - lowerLocalZMm);
        return double.IsFinite(edgeLocalZMm);
    }

    /// <summary>
    /// BuildRafters upper/lower edges in model mm (authoritative drawn rafter geometry).
    /// </summary>
    internal static bool TryResolveBuildRaftersLowerUpperLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double xMm,
        out double lowerLocalZMm,
        out double upperLocalZMm)
    {
        lowerLocalZMm = 0d;
        upperLocalZMm = 0d;
        ArgumentNullException.ThrowIfNull(rafters);
        if (side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right) ||
            !double.IsFinite(xMm))
        {
            return false;
        }

        var rafter = rafters.FirstOrDefault(candidate => candidate.Side == side);
        if (rafter is null || rafter.Corners.Count < 4)
        {
            return false;
        }

        return TryEdgeLocalZAtX(rafter.Corners[3], rafter.Corners[2], xMm, out lowerLocalZMm) &&
               TryEdgeLocalZAtX(rafter.Corners[0], rafter.Corners[1], xMm, out upperLocalZMm);
    }

    /// <summary>
    /// Final rendered rafter slope edges in model mm. Identical to BuildRafters after the
    /// physical-polygon renderer replaced the unconstrained SVG upper-tip affine.
    /// </summary>
    internal static bool TryResolveSvgMasterRafterLowerUpperLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double xMm,
        out double lowerLocalZMm,
        out double upperLocalZMm) =>
        TryResolveBuildRaftersLowerUpperLocalZMm(
            rafters,
            side,
            xMm,
            out lowerLocalZMm,
            out upperLocalZMm);

    /// <summary>
    /// Model-space Z of the drawn rafter lower/upper slope edges at <paramref name="xMm"/>.
    /// </summary>
    internal static bool TryResolveVisualRafterLowerUpperLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double xMm,
        out double lowerLocalZMm,
        out double upperLocalZMm) =>
        TryResolveBuildRaftersLowerUpperLocalZMm(
            rafters,
            side,
            xMm,
            out lowerLocalZMm,
            out upperLocalZMm);

    /// <summary>
    /// Strešná rovina for a WallPlate/Intermediate station: UPPER drawn rafter edge at
    /// member center X (not seating contact, not lower edge, not mid-thickness).
    /// </summary>
    internal static bool TryResolveVisualRoofPlaneLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double centerXMm,
        out double roofPlaneLocalZMm)
    {
        roofPlaneLocalZMm = 0d;
        if (!TryResolveBuildRaftersLowerUpperLocalZMm(
                rafters,
                side,
                centerXMm,
                out _,
                out var upperLocalZMm))
        {
            return false;
        }

        roofPlaneLocalZMm = upperLocalZMm;
        return true;
    }

    /// <summary>
    /// Strešná rovina for the ridge: intersection (apex) of the LEFT and RIGHT UPPER
    /// drawn rafter edges. Independent of ridge-purlin seating.
    /// </summary>
    internal static bool TryResolveVisualRidgeRoofPlaneLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        out double roofPlaneLocalZMm)
    {
        roofPlaneLocalZMm = 0d;
        ArgumentNullException.ThrowIfNull(rafters);
        var left = rafters.FirstOrDefault(r => r.Side == AutomaticPurlinSectionSide.Left);
        var right = rafters.FirstOrDefault(r => r.Side == AutomaticPurlinSectionSide.Right);
        if (left is null || right is null ||
            left.Corners.Count < 2 || right.Corners.Count < 2)
        {
            return false;
        }

        return TryIntersectLinesLocalZ(
            left.Corners[0],
            left.Corners[1],
            right.Corners[0],
            right.Corners[1],
            out roofPlaneLocalZMm);
    }

    private static AutomaticPurlinSectionPointMm LowerEndPoint(
        AutomaticPurlinSectionPointMm a,
        AutomaticPurlinSectionPointMm b) =>
        a.ZMm <= b.ZMm ? a : b;

    /// <summary>
    /// Visual rafter body corners: BuildRafters upper/lower slope edges with a PLUMB
    /// eave termination. Seating authority remains the BuildRafters slope edges;
    /// only the eave end face is changed from the perpendicular square cut.
    /// Order: upper0 → upper1 → lower1 → lower0 (same winding as BuildRafters).
    /// </summary>
    internal static bool TryCreatePlumbEaveRafterCorners(
        AutomaticPurlinSectionRafterMm rafter,
        out IReadOnlyList<AutomaticPurlinSectionPointMm> corners)
    {
        corners = Array.Empty<AutomaticPurlinSectionPointMm>();
        if (rafter.Corners.Count < 4)
        {
            return false;
        }

        var upper0 = rafter.Corners[0];
        var upper1 = rafter.Corners[1];
        var lower1 = rafter.Corners[2];
        var lower0 = rafter.Corners[3];

        // Eave tip = lower-Z end of the upper face (roof surface at the eave).
        var upperEaveIs0 = upper0.ZMm <= upper1.ZMm;
        var upperEave = upperEaveIs0 ? upper0 : upper1;
        if (!TryEdgeLocalZAtX(lower0, lower1, upperEave.XMm, out var lowerAtEaveX) ||
            !double.IsFinite(lowerAtEaveX))
        {
            return false;
        }

        var plumbLowerEave = new AutomaticPurlinSectionPointMm(upperEave.XMm, lowerAtEaveX);
        // Replace only the lower corner that sits under the upper eave tip.
        corners = upperEaveIs0
            ? [upper0, upper1, lower1, plumbLowerEave]
            : [upper0, upper1, plumbLowerEave, lower0];
        return true;
    }

    private static void AddPhysicalRafterDrawing(
        DrawingGroup group,
        AutomaticPurlinSectionRafterMm rafter,
        Func<double, double, MediaPoint> toView)
    {
        if (!TryCreatePlumbEaveRafterCorners(rafter, out var corners) ||
            corners.Count < 3)
        {
            return;
        }

        var figure = new PathFigure
        {
            StartPoint = toView(corners[0].XMm, corners[0].ZMm),
            IsClosed = true,
            IsFilled = true,
        };
        for (var i = 1; i < corners.Count; i++)
        {
            var corner = corners[i];
            figure.Segments.Add(new LineSegment(toView(corner.XMm, corner.ZMm), true));
        }

        figure.Freeze();
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        var brush = rafter.Side == AutomaticPurlinSectionSide.Left
            ? RafterLeftBrush
            : RafterRightBrush;
        group.Children.Add(new GeometryDrawing(brush, RafterOutlinePen, geometry));
    }

    private static bool TryFlattenGeometryToViewPoints(
        Geometry geometry,
        out List<MediaPoint> points)
    {
        points = [];
        var path = geometry as PathGeometry ?? PathGeometry.CreateFromGeometry(geometry);
        if (path is null || path.Figures.Count == 0)
        {
            return false;
        }

        foreach (var figure in path.Figures)
        {
            points.Add(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        points.Add(line.Point);
                        break;
                    case PolyLineSegment poly:
                        points.AddRange(poly.Points);
                        break;
                    default:
                        return false;
                }
            }
        }

        return points.Count >= 3;
    }

    private static bool TryIntersectLinesLocalZ(
        AutomaticPurlinSectionPointMm a0,
        AutomaticPurlinSectionPointMm a1,
        AutomaticPurlinSectionPointMm b0,
        AutomaticPurlinSectionPointMm b1,
        out double localZMm)
    {
        localZMm = 0d;
        var ax = a1.XMm - a0.XMm;
        var az = a1.ZMm - a0.ZMm;
        var bx = b1.XMm - b0.XMm;
        var bz = b1.ZMm - b0.ZMm;
        var denom = ax * bz - az * bx;
        if (Math.Abs(denom) <= 1e-9d)
        {
            // Parallel: fall back to average Z at mean X of the two ridge-side ends.
            localZMm = (a1.ZMm + b1.ZMm) / 2d;
            return double.IsFinite(localZMm);
        }

        var dx = b0.XMm - a0.XMm;
        var dz = b0.ZMm - a0.ZMm;
        var t = (dx * bz - dz * bx) / denom;
        localZMm = a0.ZMm + t * az;
        return double.IsFinite(localZMm);
    }

    private static bool TryEdgeLocalZAtX(
        AutomaticPurlinSectionPointMm start,
        AutomaticPurlinSectionPointMm end,
        double xMm,
        out double localZMm)
    {
        localZMm = 0d;
        var dx = end.XMm - start.XMm;
        if (Math.Abs(dx) <= 0.01d)
        {
            localZMm = (start.ZMm + end.ZMm) / 2d;
            return double.IsFinite(localZMm);
        }

        var t = (xMm - start.XMm) / dx;
        localZMm = start.ZMm + t * (end.ZMm - start.ZMm);
        return double.IsFinite(localZMm);
    }

    private static Matrix CreateMemberTransform(
        MasterElement element,
        AutomaticPurlinSectionMemberMm member,
        Func<double, double, MediaPoint> toView)
    {
        var left = member.CenterXMm - member.WidthMm / 2d;
        var bottom = member.CenterZMm - member.HeightMm / 2d;
        var topLeft = toView(left, member.MemberTopZMm);
        var bottomRight = toView(left + member.WidthMm, bottom);
        return MapBounds(element.Geometry.Bounds, new Rect(topLeft, bottomRight));
    }

    private static Matrix CreateWallTransform(
        MasterElement element,
        AutomaticPurlinSectionWallMm wall,
        AutomaticPurlinSectionMemberMm? plate,
        Func<double, double, MediaPoint> toView)
    {
        var sourcePlate = FindElement(wall.Side == AutomaticPurlinSectionSide.Left
            ? WallPlateLeftId
            : WallPlateRightId).Geometry.Bounds;
        var wallWidth = plate is null
            ? wall.WidthMm
            : plate.WidthMm * element.Geometry.Bounds.Width / sourcePlate.Width;
        var wallHeight = plate is null
            ? wall.TopZMm - wall.BottomZMm
            : plate.HeightMm * element.Geometry.Bounds.Height / sourcePlate.Height;
        var topLeft = toView(wall.CenterXMm - wallWidth / 2d, wall.TopZMm);
        var bottomRight = toView(wall.CenterXMm + wallWidth / 2d, wall.TopZMm - wallHeight);
        return MapBounds(element.Geometry.Bounds, new Rect(topLeft, bottomRight));
    }

    private static Matrix MapBounds(Rect source, Rect target)
    {
        var scaleX = target.Width / Math.Max(0.001d, source.Width);
        var scaleY = target.Height / Math.Max(0.001d, source.Height);
        return new Matrix(
            scaleX, 0d, 0d, scaleY,
            target.Left - source.Left * scaleX,
            target.Top - source.Top * scaleY);
    }

    private static void AddDrawing(DrawingGroup group, MasterElement element, Matrix transform)
    {
        var geometry = element.Geometry.Clone();
        geometry.Transform = new MatrixTransform(transform);
        geometry.Freeze();
        group.Children.Add(new GeometryDrawing(element.Fill, element.Pen, geometry));
    }

    private static MasterElement FindElement(string elementId) => MasterElements.First(element =>
        string.Equals(element.Id, elementId, StringComparison.Ordinal) ||
        IsRafterLeftElement(elementId) && element.Id == RafterLeftId);

    private static MasterElement Element(string id, string data, MediaBrush fill, MediaPen pen)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return new MasterElement(id, geometry, fill, pen);
    }

    private static MediaBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static MediaPen CreatePen(byte red, byte green, byte blue, double thickness)
    {
        var pen = new MediaPen(CreateBrush(red, green, blue), thickness)
        {
            LineJoin = PenLineJoin.Bevel,
            MiterLimit = 22.9256d,
        };
        pen.Freeze();
        return pen;
    }

    private sealed record MasterElement(string Id, Geometry Geometry, MediaBrush Fill, MediaPen Pen);
}
