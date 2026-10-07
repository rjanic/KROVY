using System;
using System.Collections.Generic;
using System.Linq;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validates disconnected surviving intervals of one Plan2D timber.</summary>
public static class RoofOrdinaryTrimSplitRules
{
    public static bool IsSplit(RoofSegment3D source, IReadOnlyList<RoofSegment3D> pieces,
        double toleranceMm = 0.001d, bool allowTouchingPieces = false)
    {
        if (pieces.Count < 2 || source.LengthMm <= toleranceMm) return false;
        var dx = source.End.X - source.Start.X;
        var dy = source.End.Y - source.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= toleranceMm || Math.Abs(source.Start.Z) > toleranceMm ||
            Math.Abs(source.End.Z) > toleranceMm) return false;
        var intervals = new List<(double Start, double End)>();
        foreach (var piece in pieces)
        {
            double Station(RoofPoint3D point) =>
                ((point.X - source.Start.X) * dx + (point.Y - source.Start.Y) * dy) / length;
            bool OnSource(RoofPoint3D point) => Math.Abs(point.Z) <= toleranceMm &&
                Math.Abs((point.X - source.Start.X) * dy - (point.Y - source.Start.Y) * dx) / length <= toleranceMm;
            if (!OnSource(piece.Start) || !OnSource(piece.End)) return false;
            var a = Station(piece.Start);
            var b = Station(piece.End);
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b)) return false;
            var start = Math.Min(a, b);
            var end = Math.Max(a, b);
            if (start < -toleranceMm || end > length + toleranceMm || end - start <= toleranceMm) return false;
            intervals.Add((start, end));
        }
        var sorted = intervals.OrderBy(interval => interval.Start).ToArray();
        // BREAKATPOINT's two native entities are separate timbers even at a shared endpoint.
        // TRIM keeps its stricter disconnected-interval contract. Overlap is never a split.
        return sorted.Zip(sorted.Skip(1), (a, b) => allowTouchingPieces
            ? b.Start - a.End >= -toleranceMm : b.Start - a.End > toleranceMm).All(valid => valid);
    }
}
