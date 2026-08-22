namespace AcKrovy.Core.Models.Roofs;

public enum SimpleGableRoofGeometryError
{
    None = 0,
    FootprintIsNotFourSided = 1,
    FootprintIsNotRectangular = 2,
    RidgeDirectionCannotBeResolved = 3,
    InvalidSlope = 4,
    DegenerateDimensions = 5,
    NonFiniteGeometry = 6,
    InvalidRoofKind = 7,
    InvalidEaveHeightDifference = 8,
}
