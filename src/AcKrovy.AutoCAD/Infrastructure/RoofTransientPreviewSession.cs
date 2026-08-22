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
        SimpleGableRoofGeometry geometry,
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
        SimpleGableRafterLayout layout,
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
        SimpleGableRoofGeometry geometry,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        return SimpleGableRoofWireframe.Create(geometry, sourceElevation)
            .Select(edge => new RoofPreviewSegment(
                MapPoint(edge.Segment.Start),
                MapPoint(edge.Segment.End),
                edge.Role == RoofDisplayEdgeRole.Ridge,
                edge.Role is RoofDisplayEdgeRole.Eave1 or
                    RoofDisplayEdgeRole.GableSlope10 or
                    RoofDisplayEdgeRole.GableSlope11 ? 1 : 0))
            .ToArray();
    }

    internal static IReadOnlyList<RoofPreviewSegment> MapRafterSegments(
        SimpleGableRafterLayout layout,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        return layout.Rafters
            .Select(rafter => new RoofPreviewSegment(
                new Point3d(rafter.PlanStart.X, rafter.PlanStart.Y, sourceElevation),
                new Point3d(rafter.PlanEnd.X, rafter.PlanEnd.Y, sourceElevation),
                IsRidge: false,
                FaceIndex: (int)rafter.Face))
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
        try
        {
            _document.Editor.UpdateScreen();
        }
        catch (Exception)
        {
            // Document destruction can make the editor unavailable during cleanup.
        }
    }

    private void AddGeometry(SimpleGableRoofGeometry geometry, double sourceElevation)
    {
        var transientManager = TransientManager.CurrentTransientManager;
        foreach (var segment in MapSegments(geometry, sourceElevation))
        {
            var drawable = new Line(segment.Start, segment.End)
            {
                ColorIndex = segment.IsRidge
                    ? RidgeColorIndex
                    : segment.FaceIndex == 1 ? Face1BoundaryColorIndex : FaceBoundaryColorIndex,
                LineWeight = segment.IsRidge
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

    private void AddRafters(SimpleGableRafterLayout layout, double sourceElevation)
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

    internal sealed record RoofPreviewSegment(
        Point3d Start,
        Point3d End,
        bool IsRidge,
        int FaceIndex);
}
