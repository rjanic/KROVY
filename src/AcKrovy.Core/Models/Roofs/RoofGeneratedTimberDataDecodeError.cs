namespace AcKrovy.Core.Models.Roofs;

public enum RoofGeneratedTimberDataDecodeError
{
    None = 0,
    MalformedPayload = 1,
    UnsupportedFutureSchema = 2,
    InvalidOwnerReference = 3,
    InvalidMemberKind = 4,
    InvalidRoofFace = 5,
    InvalidStation = 6,
    InvalidMaximumSpacing = 7,
    InvalidLayoutSignature = 8,
}
