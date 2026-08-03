namespace AcKrovy.Core.Models;

/// <summary>
/// CAD-neutral annotation typography for the three independent text roles:
/// item code (K1, P8), dimensions (80/160) and numeric slope angle (35°).
/// Every role owns its text-style name and its paper height in millimetres.
/// </summary>
/// <remarks>
/// Schema 7 persists the six role members below. The write-only members at the
/// bottom of the record exist only so schema 6 payloads, which carried one
/// shared style name and the three legacy height keys, keep deserializing into
/// the role model. They have no getter, so they are never written back and a
/// schema 7 payload never mixes both shapes.
/// </remarks>
public sealed record TimberAnnotationTextSettings(
    string ItemCodeTextStyleName,
    string DimensionTextStyleName,
    string SlopeTextStyleName,
    double ItemCodePaperHeightMm,
    double DimensionPaperHeightMm,
    double SlopePaperHeightMm)
{
    /// <summary>
    /// Builds settings whose three roles share one text-style name. This is the
    /// schema 6 shape and stays the factory shape until per-role styles get a UI.
    /// </summary>
    public static TimberAnnotationTextSettings Shared(
        string textStyleName,
        double itemCodePaperHeightMm,
        double dimensionPaperHeightMm,
        double slopePaperHeightMm) =>
        new(
            textStyleName,
            textStyleName,
            textStyleName,
            itemCodePaperHeightMm,
            dimensionPaperHeightMm,
            slopePaperHeightMm);

    /// <summary>True when all three roles resolve to the same text-style name.</summary>
    public bool HasSharedTextStyleName =>
        string.Equals(
            ItemCodeTextStyleName,
            DimensionTextStyleName,
            StringComparison.Ordinal) &&
        string.Equals(
            ItemCodeTextStyleName,
            SlopeTextStyleName,
            StringComparison.Ordinal);

    public string GetTextStyleName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => ItemCodeTextStyleName,
            TimberAnnotationTextRole.Dimension => DimensionTextStyleName,
            TimberAnnotationTextRole.Slope => SlopeTextStyleName,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public double GetPaperHeightMm(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => ItemCodePaperHeightMm,
            TimberAnnotationTextRole.Dimension => DimensionPaperHeightMm,
            TimberAnnotationTextRole.Slope => SlopePaperHeightMm,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public TimberAnnotationTextSettings WithRole(
        TimberAnnotationTextRole role,
        string textStyleName,
        double paperHeightMm) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => this with
            {
                ItemCodeTextStyleName = textStyleName,
                ItemCodePaperHeightMm = paperHeightMm,
            },
            TimberAnnotationTextRole.Dimension => this with
            {
                DimensionTextStyleName = textStyleName,
                DimensionPaperHeightMm = paperHeightMm,
            },
            TimberAnnotationTextRole.Slope => this with
            {
                SlopeTextStyleName = textStyleName,
                SlopePaperHeightMm = paperHeightMm,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    /// <summary>Schema 6 shared style name. Fans out to all three roles.</summary>
    public string TextStyleName
    {
        init
        {
            ItemCodeTextStyleName = value;
            DimensionTextStyleName = value;
            SlopeTextStyleName = value;
        }
    }

    /// <summary>Schema 6 item-number height key.</summary>
    public double ItemNumberPaperHeightMm
    {
        init => ItemCodePaperHeightMm = value;
    }

    /// <summary>Schema 6 label-and-dimension height key.</summary>
    public double LabelAndDimensionPaperHeightMm
    {
        init => DimensionPaperHeightMm = value;
    }

    /// <summary>Schema 6 slope-angle height key.</summary>
    public double SlopeAnglePaperHeightMm
    {
        init => SlopePaperHeightMm = value;
    }
}
