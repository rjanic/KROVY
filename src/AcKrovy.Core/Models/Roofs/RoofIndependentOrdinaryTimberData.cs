namespace AcKrovy.Core.Models.Roofs;

/// <summary>Member-owned Ordinary identity. Source fields record history only.</summary>
public sealed record RoofIndependentOrdinaryTimberData(
    int SchemaVersion,
    string IndependentMemberId,
    RoofIndependentOrdinaryOriginKind OriginKind,
    RoofIndependentOrdinaryEntityRole EntityRole,
    string? SourceRoofReference,
    RoofGeneratedMemberKey? SourceGeneratedMemberKey);

public static class RoofIndependentOrdinaryTimberDataSchema
{
    public const int CurrentVersion = 1;
}

public enum RoofIndependentOrdinaryOriginKind
{
    DetachedFromAuto = 1,
    CopiedFromAuto = 2,
    CopiedFromIndependent = 3,
    MirroredFromAuto = 4,
    MirroredFromIndependent = 5,
    Joined = 6,
}

public enum RoofIndependentOrdinaryEntityRole
{
    PlanLine = 1,
    PhysicalSolid = 2,
    Annotation = 3,
}
