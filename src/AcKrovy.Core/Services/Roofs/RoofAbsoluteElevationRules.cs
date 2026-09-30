using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral absolute eave/ridge elevation conversion without display rounding drift.</summary>
public static class RoofAbsoluteElevationRules
{
    public static double RiseMm(double halfRoofWidthMm, double pitchDegrees)
    {
        if (!IsFinite(halfRoofWidthMm) || halfRoofWidthMm < 0d ||
            !IsFinite(pitchDegrees) ||
            pitchDegrees <= 0d ||
            pitchDegrees >= 90d)
        {
            return double.NaN;
        }

        return halfRoofWidthMm * Math.Tan(pitchDegrees * Math.PI / 180d);
    }

    public static RoofAbsoluteElevationState CreateDefault(
        double halfRoofWidthMm,
        double pitchDegrees,
        bool physical3DEnabled = false) =>
        FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            enteredRelativeElevationMm: 0d,
            halfRoofWidthMm,
            pitchDegrees,
            physical3DEnabled,
            physical3DEnabled
                ? RoofPhysicalDisplayVisibility.Both
                : RoofPhysicalDisplayVisibility.Both);

    public static RoofAbsoluteElevationState FromEntered(
        RoofAbsoluteElevationInputMode mode,
        double enteredRelativeElevationMm,
        double halfRoofWidthMm,
        double pitchDegrees,
        bool physical3DEnabled,
        RoofPhysicalDisplayVisibility displayVisibility =
            RoofPhysicalDisplayVisibility.Both)
    {
        if (!IsFinite(enteredRelativeElevationMm))
        {
            throw new ArgumentOutOfRangeException(nameof(enteredRelativeElevationMm));
        }

        var rise = RiseMm(halfRoofWidthMm, pitchDegrees);
        if (!IsFinite(rise))
        {
            throw new ArgumentOutOfRangeException(nameof(pitchDegrees));
        }

        double eave;
        double ridge;
        if (mode == RoofAbsoluteElevationInputMode.Eave)
        {
            eave = enteredRelativeElevationMm;
            ridge = eave + rise;
        }
        else
        {
            ridge = enteredRelativeElevationMm;
            eave = ridge - rise;
        }

        return new RoofAbsoluteElevationState(
            mode,
            enteredRelativeElevationMm,
            eave,
            ridge,
            rise,
            physical3DEnabled,
            physical3DEnabled ? displayVisibility : RoofPhysicalDisplayVisibility.Both);
    }

    /// <summary>
    /// Switches input mode while preserving physical eave/ridge elevations (no geometry move).
    /// </summary>
    public static RoofAbsoluteElevationState SwitchMode(
        RoofAbsoluteElevationState current,
        RoofAbsoluteElevationInputMode nextMode)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (current.InputMode == nextMode)
        {
            return current;
        }

        var entered = nextMode == RoofAbsoluteElevationInputMode.Eave
            ? current.ResolvedEaveRelativeElevationMm
            : current.ResolvedRidgeRelativeElevationMm;
        return new RoofAbsoluteElevationState(
            nextMode,
            entered,
            current.ResolvedEaveRelativeElevationMm,
            current.ResolvedRidgeRelativeElevationMm,
            current.RiseMm,
            current.Physical3DEnabled,
            current.DisplayVisibility,
            current.LowerEndCutMode,
            current.RidgeJoinMode,
            current.StructuralWidthMm,
            current.StructuralHeightMode,
            current.StructuralExplicitHeightMm);
    }

    /// <summary>
    /// Recalculates rise and the non-anchored elevation after pitch or width changes,
    /// preserving the entered value of the selected mode.
    /// </summary>
    public static RoofAbsoluteElevationState RecalculateForGeometry(
        RoofAbsoluteElevationState current,
        double halfRoofWidthMm,
        double pitchDegrees) =>
        FromEntered(
            current.InputMode,
            current.EnteredRelativeElevationMm,
            halfRoofWidthMm,
            pitchDegrees,
            current.Physical3DEnabled,
            current.DisplayVisibility) with
        {
            LowerEndCutMode = current.LowerEndCutMode,
            RidgeJoinMode = current.RidgeJoinMode,
            StructuralWidthMm = current.StructuralWidthMm,
            StructuralHeightMode = current.StructuralHeightMode,
            StructuralExplicitHeightMm = current.StructuralExplicitHeightMm,
        };

    public static RoofAbsoluteElevationState WithPhysical3DEnabled(
        RoofAbsoluteElevationState current,
        bool enabled)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (current.Physical3DEnabled == enabled)
        {
            return current;
        }

        return current with
        {
            Physical3DEnabled = enabled,
            DisplayVisibility = enabled
                ? (current.DisplayVisibility == RoofPhysicalDisplayVisibility.Both
                    ? RoofPhysicalDisplayVisibility.Both
                    : current.DisplayVisibility)
                : RoofPhysicalDisplayVisibility.Both,
        };
    }

    public static RoofAbsoluteElevationState WithDisplayVisibility(
        RoofAbsoluteElevationState current,
        RoofPhysicalDisplayVisibility visibility)
    {
        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        return current.DisplayVisibility == visibility
            ? current
            : current with { DisplayVisibility = visibility };
    }

    public static string FormatMetres(double relativeElevationMm, CultureInfo? culture) =>
        RoofRelativeElevationDatumRules.FormatMetres(relativeElevationMm, culture);

    public static string FormatMetresWithUnit(double relativeElevationMm, CultureInfo? culture) =>
        RoofRelativeElevationDatumRules.FormatMetresWithUnit(relativeElevationMm, culture);

    public static bool TryParseMetres(
        string? text,
        CultureInfo? culture,
        out double millimetres) =>
        RoofRelativeElevationDatumRules.TryParseMetres(text, culture, out millimetres);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
