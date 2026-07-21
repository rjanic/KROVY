namespace AcKrovy.Core.Models;

/// <summary>
/// Four ordered vertices of a prospective rectangular Post footprint. The model
/// describes geometry only; use the validator before treating it as a rectangle.
/// </summary>
public sealed class TimberRectangularFootprintGeometry
{
    public const int RequiredVertexCount = 4;

    public TimberRectangularFootprintGeometry(
        IReadOnlyList<TimberRectangularFootprintPoint> vertices)
    {
        if (vertices is null)
        {
            throw new ArgumentNullException(nameof(vertices));
        }

        if (vertices.Count != RequiredVertexCount)
        {
            throw new ArgumentException(
                $"A rectangular footprint requires exactly {RequiredVertexCount} vertices.",
                nameof(vertices));
        }

        Vertices = vertices.ToArray();
        Segments = Enumerable.Range(0, RequiredVertexCount)
            .Select(index =>
            {
                var start = Vertices[index];
                var end = Vertices[(index + 1) % RequiredVertexCount];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                return new TimberRectangularFootprintSegment(
                    index,
                    start,
                    end,
                    Math.Sqrt(dx * dx + dy * dy));
            })
            .ToArray();

        Center = new TimberRectangularFootprintPoint(
            Vertices.Average(vertex => vertex.X),
            Vertices.Average(vertex => vertex.Y));
        Bounds = new TimberRectangularFootprintBounds(
            Vertices.Min(vertex => vertex.X),
            Vertices.Min(vertex => vertex.Y),
            Vertices.Max(vertex => vertex.X),
            Vertices.Max(vertex => vertex.Y));
        SignedAreaMm2 = CalculateSignedArea(Vertices);
        AreaMm2 = Math.Abs(SignedAreaMm2);
    }

    public IReadOnlyList<TimberRectangularFootprintPoint> Vertices { get; }

    public IReadOnlyList<TimberRectangularFootprintSegment> Segments { get; }

    public TimberRectangularFootprintPoint Center { get; }

    public TimberRectangularFootprintBounds Bounds { get; }

    public double SignedAreaMm2 { get; }

    public double AreaMm2 { get; }

    private static double CalculateSignedArea(
        IReadOnlyList<TimberRectangularFootprintPoint> vertices)
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
}
