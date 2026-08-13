namespace AcKrovy.Core.Models.Roofs;

public enum RoofDefinitionDataDecodeError
{
    None = 0,
    MalformedPayload,
    UnsupportedFutureSchema,
    UnsupportedRoofKind,
    InvalidSlope,
    InvalidRidgeDirection,
    InvalidFootprintSignature,
}
