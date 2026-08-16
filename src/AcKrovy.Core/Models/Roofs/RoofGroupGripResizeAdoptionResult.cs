using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Which rectangular edge-family length changed under a supported GROUP grip adoption.
/// </summary>
public enum RoofGroupGripSideResizeKind
{
    None = 0,
    /// <summary>Length along the persisted ridge family changed (gable-end resize).</summary>
    GableEnd = 1,
    /// <summary>Length transverse to the ridge family changed (eave-side resize).</summary>
    EaveSide = 2,
}

/// <summary>
/// Portable result of attempting to interpret mutated roof display geometry as one
/// supported rectangular source side resize. Never invents geometry from loose cues.
/// </summary>
public sealed record RoofGroupGripResizeAdoptionResult(
    bool CanAdopt,
    IReadOnlyList<RoofPoint2D>? AdoptedVertices,
    RoofGroupGripSideResizeKind Kind,
    string RejectionReason)
{
    public static RoofGroupGripResizeAdoptionResult Reject(string reason) =>
        new(false, null, RoofGroupGripSideResizeKind.None, reason);
}
