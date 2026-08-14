namespace AcKrovy.Core.Models.Roofs;

public enum RoofDisplayDataDecodeError
{
    None = 0,
    MalformedPayload = 1,
    UnsupportedFutureSchema = 2,
    InvalidOwnerReference = 3,
    InvalidRole = 4,
    InvalidGenerationSignature = 5,
}
