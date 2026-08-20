namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Child line endpoints in anchor-generated-member local mm (U along member, V in-plane, W normal).
/// </summary>
public sealed record RoofAttachedManualRelativeSegment(
    double U0Mm,
    double V0Mm,
    double W0Mm,
    double U1Mm,
    double V1Mm,
    double W1Mm);
