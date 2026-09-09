using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral validation and live-summary calculation for the rafter dialog.</summary>
public static class RoofRafterRequestValidator
{
    public static RoofRafterRequestValidationResult Validate(
        IRoofGeometry geometry,
        double widthMm,
        double heightMm,
        double maximumSpacingMm,
        double minimumAutomaticSpacingMm,
        string? material)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var inputError = ValidateAutomaticInputs(
            widthMm,
            heightMm,
            maximumSpacingMm,
            minimumAutomaticSpacingMm,
            material);
        if (inputError != RoofRafterRequestValidationError.None)
        {
            return Invalid(inputError);
        }

        if (geometry is HipRoofGeometry hip)
        {
            return ValidateHip(
                hip,
                widthMm,
                heightMm,
                maximumSpacingMm,
                material!.Trim());
        }

        var layoutResult = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(maximumSpacingMm, widthMm));
        if (!layoutResult.IsValid || layoutResult.Layout is null)
        {
            if (layoutResult.Error == RoofRafterLayoutError.InvalidRafterPlanWidth)
            {
                return Invalid(RoofRafterRequestValidationError.WidthDoesNotFitRoof);
            }
            return Invalid(RoofRafterRequestValidationError.InvalidRoof);
        }

        return new RoofRafterRequestValidationResult(
            new RoofRafterCreationRequest(
                widthMm,
                heightMm,
                maximumSpacingMm,
                material!.Trim(),
                geometry.PrimarySlopeDegrees),
            layoutResult.Layout,
            RoofRafterRequestValidationError.None);
    }

    public static RoofRafterRequestValidationResult ValidateHip(
        HipRoofGeometry geometry,
        double widthMm,
        double heightMm,
        double maximumSpacingMm,
        string material)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var faceResult = RoofFaceRafterLayoutService.Create(
            geometry.Topology,
            maximumSpacingMm);
        if (!faceResult.IsValid ||
            faceResult.Layout is null ||
            !RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
                geometry,
                faceResult.Layout,
                widthMm,
                out var layout) ||
            !RoofRafterMaterializationRules.IsConsistent(geometry, layout))
        {
            return Invalid(RoofRafterRequestValidationError.InvalidRoof);
        }

        return new RoofRafterRequestValidationResult(
            new RoofRafterCreationRequest(
                widthMm,
                heightMm,
                maximumSpacingMm,
                material,
                geometry.PrimarySlopeDegrees),
            layout,
            RoofRafterRequestValidationError.None);
    }

    public static RoofRafterRequestValidationError ValidateAutomaticInputs(
        double widthMm,
        double heightMm,
        double workingSpacingMm,
        double minimumAutomaticSpacingMm,
        string? material)
    {
        if (!IsFinite(widthMm) || widthMm <= 0d)
        {
            return RoofRafterRequestValidationError.InvalidWidth;
        }
        if (!IsFinite(heightMm) || heightMm <= 0d)
        {
            return RoofRafterRequestValidationError.InvalidHeight;
        }
        if (!RoofRafterSpacingRules.IsValidAutomaticWorkingSpacing(
                workingSpacingMm,
                minimumAutomaticSpacingMm))
        {
            return RoofRafterRequestValidationError.InvalidMaximumSpacing;
        }
        if (string.IsNullOrWhiteSpace(material))
        {
            return RoofRafterRequestValidationError.InvalidMaterial;
        }

        return RoofRafterRequestValidationError.None;
    }

    private static RoofRafterRequestValidationResult Invalid(
        RoofRafterRequestValidationError error) =>
        new(null, null, error);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
