namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Undirected ridge axis tied to the owner Polyline's native vertex topology.
/// Opposite edges belong to the same family.
/// </summary>
public enum RoofRidgeEdgeFamily
{
    Undefined = 0,
    SourceEdge01 = 1,
    SourceEdge12 = 2,
}
