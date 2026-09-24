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

        // Source eave is the roof-local upper rafter face at the eave. Topology places
        // that face at Z = 0, so SourceEavePlane requires ReferenceLocalZMm = 0.
        // Non-zero LocalZ is ambiguous (legacy defective dialog state) and must not be
        // accepted or normalized here.
        if (referenceKind == RoofRelativeElevationReferenceKind.SourceEavePlane &&
            referenceLocalZMm != 0d)
        {
            return Invalid(RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ);
        }

        return new RoofRelativeElevationDatumValidationResult(
            true,
            new RoofRelativeElevationDatum(
                referenceKind,
                referenceRelativeElevationMm,
                referenceLocalZMm),
            RoofRelativeElevationDatumError.None);
    }

    /// <summary>
    /// True when a stored SourceEavePlane payload is schema-shaped but violates the
    /// LocalZ = 0 contract. Used by host load paths to fail closed without rewriting XData.
    /// </summary>
    public static bool IsInconsistentSourceEaveLocalZ(
        RoofRelativeElevationReferenceKind referenceKind,
        double referenceLocalZMm) =>
        referenceKind == RoofRelativeElevationReferenceKind.SourceEavePlane &&
        IsFinite(referenceLocalZMm) &&
        referenceLocalZMm != 0d;

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

    public static string FormatMetres(double relativeElevationMm) =>
        FormatMetres(relativeElevationMm, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats relative elevation as signed metres with exactly three decimals.
    /// After rounding, a zero magnitude is always <c>±0.000</c> / <c>±0,000</c>
    /// (never <c>+0.000</c> or <c>-0.000</c>). Uses the culture decimal separator.
    /// </summary>
    public static string FormatMetres(double relativeElevationMm, CultureInfo? culture)
    {
        if (!IsFinite(relativeElevationMm))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeElevationMm));
        }

        culture ??= CultureInfo.InvariantCulture;
        var metres = relativeElevationMm / 1000d;
        var rounded = Math.Round(metres, 3, MidpointRounding.AwayFromZero);
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        if (rounded == 0d)
        {
            return "±0" + separator + "000";
        }

        var magnitude = Math.Abs(rounded).ToString("0.000", culture);
        return (rounded > 0d ? "+" : "-") + magnitude;
    }

    /// <summary>Formats relative elevation including the metre unit for tooltip lines.</summary>
    public static string FormatMetresWithUnit(double relativeElevationMm, CultureInfo? culture)
    {
        culture ??= CultureInfo.InvariantCulture;
        return FormatMetres(relativeElevationMm, culture) + " m";
    }

    /// <summary>Parses editable architectural metres and returns millimetres.</summary>
    public static bool TryParseMetres(
        string? text,
        CultureInfo? culture,
        out double millimetres)
    {
        millimetres = 0d;
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith("±0", StringComparison.Ordinal))
        {
            var core = value;
            if (core.EndsWith(" m", StringComparison.OrdinalIgnoreCase))
            {
                core = core.Substring(0, core.Length - 2).TrimEnd();
            }

            if (string.Equals(core, "±0.000", StringComparison.Ordinal) ||
                string.Equals(core, "±0,000", StringComparison.Ordinal))
            {
                return true;
            }
        }

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
