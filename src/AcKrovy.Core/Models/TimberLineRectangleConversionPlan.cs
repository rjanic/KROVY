namespace AcKrovy.Core.Models;

/// <summary>CAD-neutral specification for replacing four source lines with one LWPOLYLINE.</summary>
public sealed record TimberLineRectangleConversionPlan(
    IReadOnlyList<TimberRectangularFootprintPoint> Vertices,
    IReadOnlyList<string> SourceEdgeKeys,
    bool IsClosed,
    IReadOnlyList<double> Bulges,
    int WidthEdgeIndex)
{
    public static TimberLineRectangleConversionPlan FromDiscovery(
        TimberLineRectangleDiscoveryResult discovery)
    {
        if (discovery is null)
        {
            throw new ArgumentNullException(nameof(discovery));
        }
        if (discovery.Status != TimberLineRectangleDiscoveryStatus.Success ||
            discovery.Geometry is null ||
            discovery.OrderedEdgeKeys.Count != TimberRectangularFootprintGeometry.RequiredVertexCount)
        {
            throw new ArgumentException("A successful four-edge discovery is required.", nameof(discovery));
        }

        return new TimberLineRectangleConversionPlan(
            discovery.Geometry.Vertices.ToArray(),
            discovery.OrderedEdgeKeys.ToArray(),
            IsClosed: true,
            Enumerable.Repeat(0d, TimberRectangularFootprintGeometry.RequiredVertexCount).ToArray(),
            TimberLineRectangleDiscoveryResult.SelectedWidthEdgeIndex);
    }
}
