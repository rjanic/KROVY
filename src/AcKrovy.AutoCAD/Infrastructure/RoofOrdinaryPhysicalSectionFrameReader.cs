using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcBr = Autodesk.AutoCAD.BoundaryRepresentation;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Reads actual planar BRep section faces without modifying the drawing.
/// The hint disambiguates directed axis signs; dimensions and measured face planes
/// identify width and height, independently of the roof-plane normal.</summary>
internal static class RoofOrdinaryPhysicalSectionFrameReader
{
    private sealed record Face(Vector3d Normal, Point3d Center, IReadOnlyList<Point3d> Vertices);
    internal sealed record Measurement(RoofOrdinarySectionFrame Frame, Point3d UpperAxisPoint,
        IReadOnlyList<Point3d> TopVertices, double WidthMm, double HeightMm);

    /// <summary>Legacy member evidence comes from its own section face pairs,
    /// not a frame inferred from the current AUTO station or roof normal.</summary>
    internal static bool TryReadIndependent(Solid3d solid, RoofSegment3D plan,
        double widthMm, double heightMm, out Measurement? measured, out string reason)
    {
        measured = null;
        reason = "IndependentBRepSectionUnavailable";
        try
        {
            var path = new FullSubentityPath(new[] { solid.ObjectId }, new SubentityId(SubentityType.Null, IntPtr.Zero));
            using var brep = new AcBr.Brep(path);
            var polygons = brep.Faces.Select(f => f.Loops.SelectMany(loop =>
                loop.Vertices.Select(v => v.Point)).ToArray()).Where(p => p.Length >= 3).ToArray();
            var all = polygons.SelectMany(p => p).ToArray();
            if (all.Length == 0) { reason = "IndependentBRepEmpty"; return false; }
            var center = Mean(all);
            var faces = new List<Face>();
            foreach (var polygon in polygons)
            {
                var cross = new Vector3d();
                for (var i = 1; i + 1 < polygon.Length; i++)
                {
                    cross = (polygon[i] - polygon[0]).CrossProduct(polygon[i + 1] - polygon[0]);
                    if (cross.Length > 1e-8) break;
                }
                if (cross.Length <= 1e-8) continue;
                var normal = cross.GetNormal();
                var middle = Mean(polygon);
                if (normal.DotProduct(middle - center) < 0) normal = -normal;
                if (polygon.Any(p => Math.Abs((p - polygon[0]).DotProduct(normal)) > 0.001)) continue;
                faces.Add(new(normal, middle, polygon));
            }
            IEnumerable<Face> Pairs(double dimension) => faces.Where(plus => faces.Any(minus =>
                plus != minus && plus.Normal.DotProduct(minus.Normal) < -1 + 1e-8 &&
                Math.Abs(Math.Abs((plus.Center - minus.Center).DotProduct(plus.Normal)) - dimension) <= 0.01));
            var direction = new Vector3d(plan.End.X - plan.Start.X, plan.End.Y - plan.Start.Y, 0);
            if (direction.Length <= 1e-7) { reason = "IndependentPlanDegenerate"; return false; }
            var hints = new List<RoofOrdinarySectionFrame>();
            foreach (var height in Pairs(heightMm).Where(f => f.Normal.Z > 1e-9))
            foreach (var width in Pairs(widthMm))
            {
                if (Math.Abs(width.Normal.DotProduct(height.Normal)) > 1e-7) continue;
                var l = width.Normal.CrossProduct(height.Normal).GetNormal();
                var xy = new Vector3d(l.X, l.Y, 0);
                if (xy.Length <= 1e-9 || xy.GetNormal().DotProduct(direction.GetNormal()) < 1 - 1e-9) continue;
                var hint = new RoofOrdinarySectionFrame(Map(l), Map(width.Normal), Map(height.Normal));
                if (!hints.Any(h => h.LongitudinalAxis.DistanceTo(hint.LongitudinalAxis) < 1e-7 &&
                    h.WidthAxis.DistanceTo(hint.WidthAxis) < 1e-7 && h.HeightAxis.DistanceTo(hint.HeightAxis) < 1e-7)) hints.Add(hint);
            }
            if (hints.Count != 1)
            { reason = hints.Count == 0 ? "IndependentSectionOrPlanProjectionUnresolved" : "IndependentSectionFrameAmbiguous"; return false; }
            if (!TryRead(solid, hints[0], widthMm, heightMm, out measured)) return false;
            reason = "MeasuredIndependentSection";
            return true;
        }
        catch (Exception ex)
        { reason = "IndependentBRepException:" + (ex is Autodesk.AutoCAD.Runtime.Exception cad ? cad.ErrorStatus.ToString() : ex.GetType().Name); return false; }
    }

    internal static bool TryCapture(Solid3d solid, RoofOrdinaryPhysicalBuildState state,
        out RoofOrdinaryPhysicalBuildState? captured, RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        captured = null;
        var hint = state.SectionFrame;
        trace?.Add("frameHintSource", hint is null ? "roof_plane" : "stored_section_frame");
        if (hint is null)
        {
            if (!RoofOrdinaryPhysicalFrameRules.TryCreate(state, state.AcceptedPlanAxis,
                    state.Anchor.SourceFaceIndex, out var roofFrame) || roofFrame is null)
            { trace?.Fail("RoofOrdinaryPhysicalFrameRules.TryCreate:roof_frame_hint"); return false; }
            hint = new(roofFrame.LongitudinalAxis, roofFrame.SectionWidthAxis, roofFrame.SectionHeightAxis);
        }
        if (!TryRead(solid, hint, state.WidthMm, state.HeightMm, out var measured, trace) || measured is null) return false;
        captured = state with { Version = RoofOrdinaryPhysicalBuildState.CurrentVersion, SectionFrame = measured.Frame };
        return true;
    }

    internal static bool TryRead(Solid3d solid, RoofOrdinarySectionFrame hint,
        double widthMm, double heightMm, out Measurement? measured, RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        measured = null;
        try
        {
            if (!RoofOrdinarySectionTransportRules.IsValid(hint))
            { trace?.Fail("RoofOrdinarySectionTransportRules.IsValid:frame_hint"); return false; }
            trace?.Add("frameHint", $"L:{RoofOrdinaryPhysicalBuildStateTrace.Point(hint.LongitudinalAxis)},W:{RoofOrdinaryPhysicalBuildStateTrace.Point(hint.WidthAxis)},H:{RoofOrdinaryPhysicalBuildStateTrace.Point(hint.HeightAxis)}");
            var path = new FullSubentityPath(new[] { solid.ObjectId },
                new SubentityId(SubentityType.Null, IntPtr.Zero));
            using var brep = new AcBr.Brep(path);
            var polygons = brep.Faces.Select(f => f.Loops.SelectMany(loop =>
                loop.Vertices.Select(v => v.Point)).ToArray()).Where(p => p.Length >= 3).ToArray();
            var all = polygons.SelectMany(p => p).ToArray();
            if (all.Length == 0)
            { trace?.Fail("RoofOrdinaryPhysicalSectionFrameReader.TryRead:empty_BRep"); return false; }
            var center = Mean(all);
            var faces = new List<Face>();
            foreach (var polygon in polygons)
            {
                var middle = Mean(polygon);
                var cross = new Vector3d(0, 0, 0);
                for (var i = 1; i + 1 < polygon.Length; i++)
                {
                    cross = (polygon[i] - polygon[0]).CrossProduct(polygon[i + 1] - polygon[0]);
                    if (cross.Length > 1e-8) break;
                }
                if (cross.Length <= 1e-8) continue;
                var normal = cross.GetNormal();
                if (normal.DotProduct(middle - center) < 0) normal = -normal;
                if (polygon.Any(p => Math.Abs((p - polygon[0]).DotProduct(normal)) > 0.001)) continue;
                faces.Add(new(normal, middle, polygon));
            }
            var hintL = Map(hint.LongitudinalAxis);
            trace?.Add("brepPlanarFaces", faces.Count);
            trace?.Add("brepLongitudinalSideFaces", faces.Count(f => Math.Abs(f.Normal.DotProduct(hintL)) <= 1e-5));
            (Face Plus, Face Minus, double Dimension)? Pair(double dimension, Vector3d signHint)
            {
                var candidates = from plus in faces from minus in faces
                    where plus != minus && Math.Abs(plus.Normal.DotProduct(hintL)) <= 1e-5 &&
                        plus.Normal.DotProduct(minus.Normal) < -1 + 1e-8 &&
                        plus.Normal.DotProduct(signHint) > 0
                    let distance = Math.Abs((plus.Center - minus.Center).DotProduct(plus.Normal))
                    where Math.Abs(distance - dimension) <= 0.01
                    orderby plus.Normal.DotProduct(signHint) descending
                    select (plus, minus, distance);
                return candidates.Select(p => ((Face, Face, double)?)p).FirstOrDefault();
            }
            var height = Pair(heightMm, Map(hint.HeightAxis));
            var width = Pair(widthMm, Map(hint.WidthAxis));
            trace?.Add("heightFacePairResolved", height is not null);
            trace?.Add("widthFacePairResolved", width is not null);
            if (height is null || width is null)
            { trace?.Fail("RoofOrdinaryPhysicalSectionFrameReader.Pair:" + (height is null ? "height" : "width")); return false; }
            var h = height.Value.Plus.Normal;
            var w = width.Value.Plus.Normal;
            var l = w.CrossProduct(h).GetNormal();
            var actual = new RoofOrdinarySectionFrame(Map(l), Map(w), Map(h));
            trace?.Add("measuredFrame", $"L:{RoofOrdinaryPhysicalBuildStateTrace.Point(actual.LongitudinalAxis)},W:{RoofOrdinaryPhysicalBuildStateTrace.Point(actual.WidthAxis)},H:{RoofOrdinaryPhysicalBuildStateTrace.Point(actual.HeightAxis)}");
            trace?.Add("measuredAxisHintDot", l.DotProduct(hintL));
            if (!RoofOrdinarySectionTransportRules.IsValid(actual) || l.DotProduct(hintL) < 1 - 1e-7)
            { trace?.Fail("RoofOrdinaryPhysicalSectionFrameReader.TryRead:" +
                (!RoofOrdinarySectionTransportRules.IsValid(actual) ? "invalid_measured_frame" : "longitudinal_hint_mismatch")); return false; }
            var topCenter = height.Value.Plus.Center;
            var middleWidth = (width.Value.Plus.Center.GetAsVector().DotProduct(w) +
                width.Value.Minus.Center.GetAsVector().DotProduct(w)) / 2;
            var upperAxisPoint = topCenter + w * (middleWidth - topCenter.GetAsVector().DotProduct(w));
            measured = new(actual, upperAxisPoint, height.Value.Plus.Vertices, width.Value.Dimension, height.Value.Dimension);
            return true;
        }
        catch (Exception ex)
        {
            trace?.Add("brepException", ex is Autodesk.AutoCAD.Runtime.Exception cad ? cad.ErrorStatus.ToString() : ex.GetType().Name);
            trace?.Fail("RoofOrdinaryPhysicalSectionFrameReader.TryRead:exception:" + ex.GetType().Name);
            return false;
        }
    }

    private static Point3d Mean(IReadOnlyList<Point3d> p) => new(p.Average(v => v.X), p.Average(v => v.Y), p.Average(v => v.Z));
    private static Vector3d Map(RoofPoint3D p) => new(p.X, p.Y, p.Z);
    private static RoofPoint3D Map(Vector3d p) => new(p.X, p.Y, p.Z);
}
