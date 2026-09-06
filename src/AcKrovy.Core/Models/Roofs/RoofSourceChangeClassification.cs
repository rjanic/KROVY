namespace AcKrovy.Core.Models.Roofs;

/// <summary>Pure result of classifying a current roof source against persisted data.</summary>
public sealed record RoofSourceChangeClassification(
    RoofSourceChangeKind Kind,
    IRoofGeometry? Geometry,
    RoofDefinitionRestoreError Error);
