namespace AcKrovy.Core.Models.Roofs;

public enum RoofValidationError
{
    None = 0,
    OpenLoop = 1,
    UnsupportedCurvedSegment = 2,
    NonPlanar = 3,
    FewerThanThreeUniqueVertices = 4,
    NonFiniteCoordinate = 5,
    DuplicateConsecutiveVertex = 6,
    ZeroLengthEdge = 7,
    SelfIntersection = 8,
    DegenerateArea = 9,
    RedundantCollinearVertex = 10,
}
