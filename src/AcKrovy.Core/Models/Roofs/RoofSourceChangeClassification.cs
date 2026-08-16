namespace AcKrovy.Core.Models.Roofs;

/// <summary>Pure result of classifying a current SimpleGable source against persisted data.</summary>
public sealed record RoofSourceChangeClassification(
    RoofSourceChangeKind Kind,
    SimpleGableRoofGeometry? Geometry,
    RoofDefinitionRestoreError Error);
