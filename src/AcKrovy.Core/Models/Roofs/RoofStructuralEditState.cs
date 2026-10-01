namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Owner-local semantic overrides. The boundary-pair key retains its source fold;
/// placement is a planar translation of that fold's canonical member, never a Z edit.
/// Suppression survives deletion of the reference entity. No entity identity is stored.
/// </summary>
public sealed record RoofStructuralMemberEdit(
    RoofStructuralLogicalKey LogicalKey, double OffsetXmm, double OffsetYmm, bool Suppressed);

public sealed record RoofStructuralEditState(int SchemaVersion, IReadOnlyList<RoofStructuralMemberEdit> Members)
{
    public static RoofStructuralEditState Empty => new(1, Array.Empty<RoofStructuralMemberEdit>());
}
