namespace AcKrovy.Core.Models.Roofs;

public enum RoofTopologyError
{
    None,
    InvalidFootprint,
    InvalidSlope,
    DegenerateDimensions,
    NonFiniteGeometry,
    ConcaveWavefrontNotImplemented,
    NumericallyUnresolvedTopology,
}

/// <summary>Failure never exposes partial topology. Footprint errors retain the existing validator detail.</summary>
public sealed record RoofTopologyResult(
    bool IsValid,
    RoofTopology? Topology,
    RoofTopologyError Error,
    RoofValidationError FootprintError = RoofValidationError.None);
