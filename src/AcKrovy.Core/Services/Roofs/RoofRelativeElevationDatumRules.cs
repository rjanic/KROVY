using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral validation, conversion, and display rules for relative elevation.</summary>
public static class RoofRelativeElevationDatumRules
{
    public static RoofRelativeElevationDatumValidationResult Validate(
        int schemaVersion,
        RoofRelativeElevationReferenceKind referenceKind,
        double referenceRelativeElevationMm,
        double referenceLocalZMm)
    {
        if (schemaVersion != RoofRelativeElevationDatumSchema.CurrentVersion)
        {
            return Invalid(RoofRelativeElevationDatumError.UnsupportedSchemaVersion);
        }

        if (!Enum.IsDefined(typeof(RoofRelativeElevationReferenceKind), referenceKind))
        {
            return Invalid(RoofRelativeElevationDatumError.UnsupportedReferenceKind);
        }

        if (!IsFinite(referenceRelativeElevationMm))
        {
            return Invalid(RoofRelativeElevationDatumError.InvalidReferenceRelativeElevation);
        }

        if (!IsFinite(referenceLocalZMm))
        {
            return Invalid(RoofRelativeElevationDatumError.InvalidReferenceLocalZ);
        }

        return new RoofRelativeElevationDatumValidationResult(
            true,
            new RoofRelativeElevationDatum(
                referenceKind,
                referenceRelativeElevationMm,
                referenceLocalZMm),
            RoofRelativeElevationDatumError.None);
    }

    public static double ToRelativeElevationMm(
        RoofRelativeElevationDatum datum,
        double localZMm)
    {
        if (datum is null)
        {
            throw new ArgumentNullException(nameof(datum));
        }
        var validated = Validate(
            RoofRelativeElevationDatumSchema.CurrentVersion,
            datum.ReferenceKind,
            datum.ReferenceRelativeElevationMm,
            datum.ReferenceLocalZMm);
        if (!validated.IsValid || !IsFinite(localZMm))
        {
            throw new ArgumentOutOfRangeException(nameof(localZMm));
        }

        return datum.ReferenceRelativeElevationMm + (localZMm - datum.ReferenceLocalZMm);
    }

    public static double ToLocalZMm(
        RoofRelativeElevationDatum datum,
        double relativeElevationMm)
    {
        if (datum is null)
        {
            throw new ArgumentNullException(nameof(datum));
        }
        if (!IsFinite(relativeElevationMm))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeElevationMm));
        }

        _ = ToRelativeElevationMm(datum, datum.ReferenceLocalZMm);
        return datum.ReferenceLocalZMm +
               (relativeElevationMm - datum.ReferenceRelativeElevationMm);
    }

    public static string FormatMetres(double relativeElevationMm)
    {
        if (!IsFinite(relativeElevationMm))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeElevationMm));
        }

        var metres = relativeElevationMm / 1000d;
        if (Math.Abs(metres) < 0.0005d)
        {
            return "±0.000";
        }

        return metres.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);
    }

    /// <summary>Parses editable architectural metres and returns millimetres.</summary>
    public static bool TryParseMetres(
        string? text,
        CultureInfo? culture,
        out double millimetres)
    {
        millimetres = 0d;
        var value = (text ?? string.Empty).Trim();
        if (string.Equals(value, "±0.000", StringComparison.Ordinal) ||
            string.Equals(value, "±0,000", StringComparison.Ordinal))
        {
            return true;
        }

        var style = NumberStyles.Float;
        if ((!double.TryParse(value, style, culture ?? CultureInfo.CurrentCulture, out var metres) &&
             !double.TryParse(value, style, CultureInfo.InvariantCulture, out metres)) ||
            !IsFinite(metres))
        {
            return false;
        }

        millimetres = metres * 1000d;
        return IsFinite(millimetres);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofRelativeElevationDatumValidationResult Invalid(
        RoofRelativeElevationDatumError error) => new(false, null, error);
}
