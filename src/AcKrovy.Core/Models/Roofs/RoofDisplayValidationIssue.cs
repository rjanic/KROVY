namespace AcKrovy.Core.Models.Roofs;

[Flags]
public enum RoofDisplayValidationIssue
{
    None = 0,
    MissingChild = 1 << 0,
    ExtraChild = 1 << 1,
    WrongOwner = 1 << 2,
    MalformedMetadata = 1 << 3,
    UnsupportedFutureSchema = 1 << 4,
    DuplicateRole = 1 << 5,
    MissingRole = 1 << 6,
    SignatureMismatch = 1 << 7,
    NonFiniteGeometry = 1 << 8,
    GeometryMismatch = 1 << 9,
    UnsupportedEntityType = 1 << 10,
}
