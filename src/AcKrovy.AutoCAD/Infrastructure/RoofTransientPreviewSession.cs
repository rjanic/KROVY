using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Owns one non-database wireframe preview and deterministically removes every
/// transient drawable on dispose or when its document is being destroyed.
/// </summary>
internal sealed class RoofTransientPreviewSession : IDisposable
{
    internal const short RidgeColorIndex = 1;
    internal const short FaceBoundaryColorIndex = 4;
    internal const short Face1BoundaryColorIndex = 3;
    internal const short RafterColorIndex = 2;
    private const int TransientSubDrawingMode = 128;

    private readonly Document _document;
    private readonly IntegerCollection _viewportNumbers = new();
    private readonly List<Line> _drawables = [];
    private bool _disposed;

    private RoofTransientPreviewSession(Document document)
    {
        _document = document;
        AcApp.DocumentManager.DocumentToBeDestroyed += DocumentManager_DocumentToBeDestroyed;
    }

    public static RoofTransientPreviewSession Show(
        Document document,
        IRoofGeometry geometry,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        var session = new RoofTransientPreviewSession(document);
        try
        {
            session.AddGeometry(geometry, sourceElevation);
            document.Editor.UpdateScreen();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public static RoofTransientPreviewSession ShowRafters(
        Document document,
        RoofRafterLayout layout,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        var session = new RoofTransientPreviewSession(document);
        try
        {
            session.AddRafters(layout, sourceElevation);
            document.Editor.UpdateScreen();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal static IReadOnlyList<RoofPreviewSegment> MapSegments(
        IRoofGeometry geometry,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        if (geometry is HipRoofGeometry hip)
        {
            return MapTopologySegments(hip, sourceElevation)
                .Select(segment => new RoofPreviewSegment(
                    MapPoint(segment.Start),
                    MapPoint(segment.End),
                    segment.Kind == RoofTopologyEdgeKind.Ridge,
                    FaceIndex: 0,
                    TopologyKind: segment.Kind))
                .ToArray();
        }

        return RoofWireframe.Create(geometry, sourceElevation)
            .Select(edge => new RoofPreviewSegment(
                MapPoint(edge.Segment.Start),
                MapPoint(edge.Segment.End),
                edge.Role == RoofDisplayEdgeRole.Ridge,
                edge.Role is RoofDisplayEdgeRole.MonopitchDirection or
                    RoofDisplayEdgeRole.MonopitchDirectionWing0 or
                    RoofDisplayEdgeRole.MonopitchDirectionWing1 ? 2 :
                edge.Role is RoofDisplayEdgeRole.MonopitchHighEave ? 1 :
                edge.Role is RoofDisplayEdgeRole.Eave1 or
                    RoofDisplayEdgeRole.GableSlope10 or
                    RoofDisplayEdgeRole.GableSlope11 ? 1 : 0))
            .ToArray();
    }

    internal static IReadOnlyList<RoofTopologyPreviewSegment> MapTopologySegments(
        HipRoofGeometry geometry,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        return geometry.Topology.Edges
            .Where(edge => edge.Kind is RoofTopologyEdgeKind.Hip or
                RoofTopologyEdgeKind.Ridge or
                RoofTopologyEdgeKind.Valley)
            .Select(edge => new RoofTopologyPreviewSegment(
                AddElevation(geometry.Topology.Nodes[edge.StartNodeIndex], sourceElevation),
                AddElevation(geometry.Topology.Nodes[edge.EndNodeIndex], sourceElevation),
                edge.Kind))
            .ToArray();
    }

    internal static IReadOnlyList<RoofPreviewSegment> MapRafterSegments(
        RoofRafterLayout layout,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        return MapRafterPlanSegments(layout)
            .Select(segment => new RoofPreviewSegment(
                new Point3d(segment.Start.X, segment.Start.Y, sourceElevation),
                new Point3d(segment.End.X, segment.End.Y, sourceElevation),
                IsRidge: false,
                segment.FaceIndex))
            .ToArray();
    }

    internal static IReadOnlyList<RoofRafterPlanPreviewSegment> MapRafterPlanSegments(
        RoofRafterLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Rafters
            .Select(rafter => new RoofRafterPlanPreviewSegment(
                rafter.PlanStart,
                rafter.PlanEnd,
                (int)rafter.Face))
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AcApp.DocumentManager.DocumentToBeDestroyed -= DocumentManager_DocumentToBeDestroyed;
        var transientManager = TransientManager.CurrentTransientManager;
        var drawableCount = _drawables.Count;
        foreach (var drawable in _drawables)
        {
            try
            {
                transientManager.EraseTransient(drawable, _viewportNumbers);
            }
            catch (Exception)
            {
                // A closing document may already have invalidated graphics state.
                // The managed drawable still must be disposed below.
            }
            finally
            {
                drawable.Dispose();
            }
        }

        _drawables.Clear();
#if DEBUG
        if (drawableCount > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AK_ROOF_PREVIEW] cleanup erased={drawableCount} remaining={_drawables.Count}");
        }
#endif
        try
        {
            _document.Editor.UpdateScreen();
        }
        catch (Exception)
        {
            // Document destruction can make the editor unavailable during cleanup.
        }
    }

    private void AddGeometry(IRoofGeometry geometry, double sourceElevation)
    {
        var transientManager = TransientManager.CurrentTransientManager;
        var segments = MapSegments(geometry, sourceElevation);
#if DEBUG
        if (geometry is HipRoofGeometry)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AK_ROOF_HIP] preview-primitives={segments.Count} persistent-created=0");
        }
#endif
        foreach (var segment in segments)
        {
            var isRidge = segment.TopologyKind == RoofTopologyEdgeKind.Ridge || segment.IsRidge;
            var drawable = new Line(segment.Start, segment.End)
            {
                ColorIndex = segment.TopologyKind switch
                {
                    RoofTopologyEdgeKind.Ridge => RidgeColorIndex,
                    RoofTopologyEdgeKind.Valley => Face1BoundaryColorIndex,
                    RoofTopologyEdgeKind.Hip => FaceBoundaryColorIndex,
                    _ => segment.IsRidge
                        ? RidgeColorIndex
                        : segment.FaceIndex == 2 ? RidgeColorIndex
                        : segment.FaceIndex == 1 ? Face1BoundaryColorIndex : FaceBoundaryColorIndex,
                },
                LineWeight = isRidge
                    ? LineWeight.LineWeight050
                    : LineWeight.LineWeight025,
            };
            _drawables.Add(drawable);
            transientManager.AddTransient(
                drawable,
                TransientDrawingMode.DirectShortTerm,
                TransientSubDrawingMode,
                _viewportNumbers);
        }
    }

    private void AddRafters(RoofRafterLayout layout, double sourceElevation)
    {
        var transientManager = TransientManager.CurrentTransientManager;
        foreach (var segment in MapRafterSegments(layout, sourceElevation))
        {
            var drawable = new Line(segment.Start, segment.End)
            {
                ColorIndex = RafterColorIndex,
                LineWeight = LineWeight.LineWeight025,
            };
            _drawables.Add(drawable);
            transientManager.AddTransient(
                drawable,
                TransientDrawingMode.DirectShortTerm,
                TransientSubDrawingMode,
                _viewportNumbers);
        }
    }

    private void DocumentManager_DocumentToBeDestroyed(
        object sender,
        DocumentCollectionEventArgs e)
    {
        if (ReferenceEquals(e.Document, _document))
        {
            Dispose();
        }
    }

    private static Point3d MapPoint(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);

    private static RoofPoint3D AddElevation(RoofPoint3D point, double sourceElevation) =>
        new(point.X, point.Y, point.Z + sourceElevation);

    internal sealed record RoofPreviewSegment(
        Point3d Start,
        Point3d End,
        bool IsRidge,
        int FaceIndex,
        RoofTopologyEdgeKind? TopologyKind = null);

    internal sealed record RoofTopologyPreviewSegment(
        RoofPoint3D Start,
        RoofPoint3D End,
        RoofTopologyEdgeKind Kind);

    internal sealed record RoofRafterPlanPreviewSegment(
        RoofPoint2D Start,
        RoofPoint2D End,
        int FaceIndex);
}
