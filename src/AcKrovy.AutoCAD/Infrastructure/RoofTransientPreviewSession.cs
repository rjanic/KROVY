using AcKrovy.Core.Models.Roofs;
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

    internal static IReadOnlyList<RoofPreviewSegment> MapSegments(
        SimpleGableRoofGeometry geometry,
        double sourceElevation)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        var segments = new Dictionary<RoofPreviewSegmentKey, RoofPreviewSegment>();
        AddSegment(geometry.Ridge.Start, geometry.Ridge.End, isRidge: true);
        foreach (var face in geometry.Faces.OrderBy(face => face.Index))
        {
            for (var index = 0; index < face.BoundaryPoints.Count; index++)
            {
                AddSegment(
                    face.BoundaryPoints[index],
                    face.BoundaryPoints[(index + 1) % face.BoundaryPoints.Count],
                    isRidge: false);
            }
        }

        return segments.Values
            .OrderByDescending(segment => segment.IsRidge)
            .ThenBy(segment => segment.Start.X)
            .ThenBy(segment => segment.Start.Y)
            .ThenBy(segment => segment.Start.Z)
            .ThenBy(segment => segment.End.X)
            .ThenBy(segment => segment.End.Y)
            .ThenBy(segment => segment.End.Z)
            .ToArray();

        void AddSegment(RoofPoint3D first, RoofPoint3D second, bool isRidge)
        {
            var key = RoofPreviewSegmentKey.Create(first, second);
            var mapped = new RoofPreviewSegment(
                MapPoint(key.Start, sourceElevation),
                MapPoint(key.End, sourceElevation),
                isRidge);
            if (!segments.TryGetValue(key, out var existing) || isRidge && !existing.IsRidge)
            {
                segments[key] = mapped;
            }
        }
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
                ColorIndex = segment.IsRidge ? RidgeColorIndex : FaceBoundaryColorIndex,
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

    private void DocumentManager_DocumentToBeDestroyed(
        object sender,
        DocumentCollectionEventArgs e)
    {
        if (ReferenceEquals(e.Document, _document))
        {
            Dispose();
        }
    }

    private static Point3d MapPoint(RoofPoint3D point, double sourceElevation) =>
        new(point.X, point.Y, sourceElevation + point.Z);

    internal sealed record RoofPreviewSegment(Point3d Start, Point3d End, bool IsRidge);

    private readonly record struct RoofPreviewSegmentKey(RoofPoint3D Start, RoofPoint3D End)
    {
        public static RoofPreviewSegmentKey Create(RoofPoint3D first, RoofPoint3D second) =>
            Compare(first, second) <= 0
                ? new RoofPreviewSegmentKey(first, second)
                : new RoofPreviewSegmentKey(second, first);

        private static int Compare(RoofPoint3D first, RoofPoint3D second)
        {
            var x = first.X.CompareTo(second.X);
            if (x != 0)
            {
                return x;
            }

            var y = first.Y.CompareTo(second.Y);
            return y != 0 ? y : first.Z.CompareTo(second.Z);
        }
    }
}
