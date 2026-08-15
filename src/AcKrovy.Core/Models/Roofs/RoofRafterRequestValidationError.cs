namespace AcKrovy.Core.Models.Roofs;

public enum RoofRafterRequestValidationError
{
    None = 0,
    InvalidWidth,
    WidthDoesNotFitRoof,
    InvalidHeight,
    InvalidMaximumSpacing,
    InvalidMaterial,
    InvalidRoof,
}
