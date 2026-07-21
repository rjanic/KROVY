namespace AcKrovy.Core.Models;

public enum TimberRectangularFootprintValidationError
{
    None = 0,
    InvalidVertexCount = 1,
    NonFiniteCoordinate = 2,
    ZeroLengthEdge = 3,
    DegenerateArea = 4,
    AdjacentEdgesNotPerpendicular = 5,
    OppositeEdgesNotParallel = 6,
    OppositeEdgesDifferentLength = 7,
}
