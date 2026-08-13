using System.Globalization;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Canonical closed polygon. Vertices omit a repeated closing point, are always
/// counter-clockwise and start at the lexicographically smallest (X, Y) point.
/// The final edge closes the loop back to vertex zero.
/// </summary>
public sealed class RoofFootprint
{
    internal RoofFootprint(IReadOnlyList<RoofPoint2D> canonicalVertices)
    {
        Vertices = canonicalVertices.ToArray();
        Edges = Enumerable.Range(0, Vertices.Count)
            .Select(index => new RoofEdge(
                index,
                Vertices[index],
                Vertices[(index + 1) % Vertices.Count]))
            .ToArray();
        Bounds = new RoofBoundingBox2D(
            Vertices.Min(vertex => vertex.X),
            Vertices.Min(vertex => vertex.Y),
            Vertices.Max(vertex => vertex.X),
            Vertices.Max(vertex => vertex.Y));
        SignedAreaMm2 = CalculateSignedArea(Vertices);
        AreaMm2 = Math.Abs(SignedAreaMm2);
        Centroid = CalculateCentroid(Vertices, SignedAreaMm2);
        Signature = string.Join(
            ";",
            Vertices.Select(vertex => string.Format(
                CultureInfo.InvariantCulture,
                "{0:R},{1:R}",
                vertex.X,
                vertex.Y)));
    }

    public IReadOnlyList<RoofPoint2D> Vertices { get; }

    public IReadOnlyList<RoofEdge> Edges { get; }

    public bool IsClosed => true;

    public RoofPolygonOrientation Orientation => RoofPolygonOrientation.CounterClockwise;

    public RoofBoundingBox2D Bounds { get; }

    public double SignedAreaMm2 { get; }

    public double AreaMm2 { get; }

    public RoofPoint2D Centroid { get; }

    /// <summary>Invariant deterministic signature of the canonical vertex sequence.</summary>
    public string Signature { get; }

    internal static double CalculateSignedArea(IReadOnlyList<RoofPoint2D> vertices)
    {
        var twiceArea = 0d;
        for (var index = 0; index < vertices.Count; index++)
        {
            var current = vertices[index];
            var next = vertices[(index + 1) % vertices.Count];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return twiceArea / 2d;
    }

    private static RoofPoint2D CalculateCentroid(
        IReadOnlyList<RoofPoint2D> vertices,
        double signedArea)
    {
        var weightedX = 0d;
        var weightedY = 0d;
        for (var index = 0; index < vertices.Count; index++)
        {
            var current = vertices[index];
            var next = vertices[(index + 1) % vertices.Count];
            var cross = current.X * next.Y - next.X * current.Y;
            weightedX += (current.X + next.X) * cross;
            weightedY += (current.Y + next.Y) * cross;
        }

        var divisor = 6d * signedArea;
        return new RoofPoint2D(weightedX / divisor, weightedY / divisor);
    }
}
