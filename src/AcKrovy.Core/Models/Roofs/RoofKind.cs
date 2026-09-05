namespace AcKrovy.Core.Models.Roofs;

public enum RoofKind
{
    SimpleGable = 1,
    AsymmetricGable = 2,
    Monopitch = 3,
    // Geometry-only: deliberately not accepted by the persisted roof codec yet.
    Hip = 4,
}
