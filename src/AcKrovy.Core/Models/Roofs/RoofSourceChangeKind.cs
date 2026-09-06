namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Classification of a live source footprint relative to a persisted roof definition.
/// </summary>
public enum RoofSourceChangeKind
{
    None = 0,
    RigidEquivalent = 1,
    SupportedResize = 2,
    Unsupported = 3,
    InvalidDefinition = 4,
}
