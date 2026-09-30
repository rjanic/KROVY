using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validation and state mapping for the dedicated physical-elevation owner store.</summary>
public static class RoofPhysicalElevationRules
{
    public static RoofPhysicalElevationValidationResult Validate(
        int schemaVersion,
        RoofAbsoluteElevationInputMode inputMode,
        double enteredRelativeElevationMm,
        double resolvedEaveRelativeElevationMm,
        bool physical3DEnabled,
        RoofPhysicalDisplayVisibility displayVisibility =
            RoofPhysicalDisplayVisibility.Both,
        LowerEndCutMode lowerEndCutMode = LowerEndCutMode.Vertical,
        RidgeJoinMode ridgeJoinMode = RidgeJoinMode.Meet,
        double structuralWidthMm = RoofStructuralPhysicalSettings.DefaultWidthMm,
        RoofStructuralHeightMode structuralHeightMode = RoofStructuralHeightMode.Automatic,
        double structuralExplicitHeightMm = 0d)
    {
        if (schemaVersion is not (
                RoofPhysicalElevationSchema.Version1 or
                RoofPhysicalElevationSchema.Version2 or
                RoofPhysicalElevationSchema.Version3 or
                RoofPhysicalElevationSchema.CurrentVersion))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedSchemaVersion);
        }

        if (!Enum.IsDefined(typeof(RoofAbsoluteElevationInputMode), inputMode))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedInputMode);
        }

        if (!Enum.IsDefined(typeof(RoofPhysicalDisplayVisibility), displayVisibility))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedDisplayVisibility);
        }

        if (!Enum.IsDefined(typeof(LowerEndCutMode), lowerEndCutMode))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedLowerEndCutMode);
        }
        if (!Enum.IsDefined(typeof(RidgeJoinMode), ridgeJoinMode))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedRidgeJoinMode);
        }

        if (!IsFinite(structuralWidthMm) || structuralWidthMm <= 0d)
        {
            return Invalid(RoofPhysicalElevationError.InvalidStructuralWidth);
        }

        if (!Enum.IsDefined(typeof(RoofStructuralHeightMode), structuralHeightMode))
        {
            return Invalid(RoofPhysicalElevationError.UnsupportedStructuralHeightMode);
        }

        if (!IsFinite(structuralExplicitHeightMm) ||
            structuralExplicitHeightMm < 0d ||
            (structuralHeightMode == RoofStructuralHeightMode.Explicit &&
             structuralExplicitHeightMm == 0d))
        {
            return Invalid(RoofPhysicalElevationError.InvalidStructuralExplicitHeight);
        }

        if (!IsFinite(enteredRelativeElevationMm))
        {
            return Invalid(RoofPhysicalElevationError.InvalidEnteredRelativeElevation);
        }

        if (!IsFinite(resolvedEaveRelativeElevationMm))
        {
            return Invalid(RoofPhysicalElevationError.InvalidResolvedEaveRelativeElevation);
        }

        if (inputMode == RoofAbsoluteElevationInputMode.Eave &&
            enteredRelativeElevationMm != resolvedEaveRelativeElevationMm)
        {
            return Invalid(RoofPhysicalElevationError.InconsistentResolvedEave);
        }

        // Visibility only applies when physical 3D is enabled; otherwise force Both
        // as a harmless placeholder (legacy spatial wireframe has no 3D peers).
        var visibility = physical3DEnabled
            ? displayVisibility
            : RoofPhysicalDisplayVisibility.Both;

        return new RoofPhysicalElevationValidationResult(
            true,
            new RoofPhysicalElevationData(
                schemaVersion,
                inputMode,
                enteredRelativeElevationMm,
                resolvedEaveRelativeElevationMm,
                physical3DEnabled,
                visibility,
                lowerEndCutMode,
                ridgeJoinMode,
                structuralWidthMm,
                structuralHeightMode,
                structuralExplicitHeightMm),
            RoofPhysicalElevationError.None);
    }

    public static RoofPhysicalElevationData CreateFromState(RoofAbsoluteElevationState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var validated = Validate(
            RoofPhysicalElevationSchema.CurrentVersion,
            state.InputMode,
            state.EnteredRelativeElevationMm,
            state.ResolvedEaveRelativeElevationMm,
            state.Physical3DEnabled,
            state.DisplayVisibility,
            state.LowerEndCutMode,
            state.RidgeJoinMode,
            state.StructuralWidthMm,
            state.StructuralHeightMode,
            state.StructuralExplicitHeightMm);
        if (validated.Data is null)
        {
            throw new ArgumentException(
                "Invalid absolute elevation state: " + validated.Error,
                nameof(state));
        }

        return validated.Data;
    }

    public static RoofAbsoluteElevationState ToState(
        RoofPhysicalElevationData data,
        double riseMm)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (!IsFinite(riseMm) || riseMm < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(riseMm));
        }

        double eave;
        double ridge;
        double entered;
        if (data.InputMode == RoofAbsoluteElevationInputMode.Eave)
        {
            entered = data.EnteredRelativeElevationMm;
            eave = entered;
            ridge = eave + riseMm;
        }
        else
        {
            entered = data.EnteredRelativeElevationMm;
            ridge = entered;
            eave = ridge - riseMm;
        }

        var visibility = data.Physical3DEnabled
            ? data.DisplayVisibility
            : RoofPhysicalDisplayVisibility.Both;

        return new RoofAbsoluteElevationState(
            data.InputMode,
            entered,
            eave,
            ridge,
            riseMm,
            data.Physical3DEnabled,
            visibility,
            data.LowerEndCutMode,
            data.RidgeJoinMode,
            data.StructuralWidthMm,
            data.StructuralHeightMode,
            data.StructuralExplicitHeightMm);
    }

    public static RoofAbsoluteElevationState MissingStoreDefault(double riseMm) =>
        new(
            RoofAbsoluteElevationInputMode.Eave,
            EnteredRelativeElevationMm: 0d,
            ResolvedEaveRelativeElevationMm: 0d,
            ResolvedRidgeRelativeElevationMm: riseMm,
            RiseMm: riseMm,
            Physical3DEnabled: false,
            DisplayVisibility: RoofPhysicalDisplayVisibility.Both);

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

    private static RoofPhysicalElevationValidationResult Invalid(RoofPhysicalElevationError error) =>
        new(false, null, error);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
