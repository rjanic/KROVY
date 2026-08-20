namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Secondary ownership for a manual Timber child attached to a roof (COPY/split).
/// v2 adds anchor generated-member key + relative segment geometry.
/// v3 adds the child origin (COPY vs split) so COPY clones can follow their anchor
/// during source resize without changing split/BREAK semantics.
/// </summary>
public sealed record RoofAttachedManualTimberData(
    int SchemaVersion,
    string RoofOwnerReference,
    string ChildIdentity,
    RoofTimberChildRole Role = RoofTimberChildRole.AttachedManual,
    RoofGeneratedMemberKey? AnchorGeneratedMemberKey = null,
    RoofAttachedManualRelativeSegment? RelativeSegment = null,
    RoofAttachedManualOrigin Origin = RoofAttachedManualOrigin.Split);
