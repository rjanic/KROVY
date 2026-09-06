namespace AcKrovy.Core.Models.Roofs;

public enum RoofKind
{
    SimpleGable = 1,
    AsymmetricGable = 2,
    Monopitch = 3,
    // General uniform-pitch topology; persisted without a global ridge direction.
    Hip = 4,
}
