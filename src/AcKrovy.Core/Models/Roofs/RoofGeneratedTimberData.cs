namespace AcKrovy.Core.Models.Roofs;

/// <summary>Secondary ownership metadata for an otherwise normal intelligent timber element.</summary>
public sealed record RoofGeneratedTimberData(
    int SchemaVersion,
    string RoofOwnerReference,
    RoofGeneratedTimberKind MemberKind,
    RafterRoofFace RoofFace,
    int StationIndex,
    int StationCount,
    double RequestedMaximumSpacingMm,
    string LayoutSignature);
