namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// How an AttachedManual roof timber child was created. Both origins replay against
/// their exact generated anchor during source SupportedResize; Origin distinguishes
/// their command/edit semantics.
/// </summary>
public enum RoofAttachedManualOrigin
{
    Split = 0,
    Copy = 1,
}
