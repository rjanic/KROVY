namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Portable observation of one AutoCAD GROUP member for strict structural
/// display-erase eligibility after native GROUP COPY (stale owner metadata).
/// </summary>
public readonly record struct RoofDisplayForeignGroupMemberObservation(
    string MemberKey,
    RoofDisplayForeignGroupMemberKind Kind,
    bool HasReadableRoofDisplayMetadata,
    bool SchemaSupported,
    RoofDisplayEdgeRole? Role);

/// <summary>Structural kind of a roof GROUP member for erase eligibility.</summary>
public enum RoofDisplayForeignGroupMemberKind
{
    SourcePolyline = 0,
    DisplayLine = 1,
    Other = 2,
}
