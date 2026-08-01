namespace AcKrovy.Core.Models;

/// <summary>
/// CAD-neutral annotation typography persisted by stable text-style name.
/// Heights are expressed in millimetres on paper.
/// </summary>
public sealed record TimberAnnotationTextSettings(
    string TextStyleName,
    double LabelAndDimensionPaperHeightMm,
    double ItemNumberPaperHeightMm,
    double SlopeAnglePaperHeightMm);
