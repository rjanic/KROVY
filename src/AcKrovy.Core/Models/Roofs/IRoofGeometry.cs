namespace AcKrovy.Core.Models.Roofs;

/// <summary>Common CAD-neutral identity exposed at the roof-kind geometry boundary.</summary>
public interface IRoofGeometry
{
    RoofKind Kind { get; }

    /// <summary>
    /// Persistable orientation axis. For gable roofs this is the undirected ridge
    /// axis; for a monopitch roof it is the directed LOW-to-HIGH slope axis.
    /// </summary>
    RoofDirection2D OrientationDirection { get; }

    double PrimarySlopeDegrees { get; }

    string Signature { get; }
}
