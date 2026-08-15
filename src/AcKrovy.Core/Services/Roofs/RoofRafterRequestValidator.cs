using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral validation and live-summary calculation for the rafter dialog.</summary>
public static class RoofRafterRequestValidator
{
    public static RoofRafterRequestValidationResult Validate(
        SimpleGableRoofGeometry geometry,
        double widthMm,
        double heightMm,
        double maximumSpacingMm,
        string? material)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!IsFinite(widthMm) || widthMm <= 0d)
        {
            return Invalid(RoofRafterRequestValidationError.InvalidWidth);
        }
        if (widthMm >= geometry.Ridge.LengthMm)
        {
            return Invalid(RoofRafterRequestValidationError.WidthDoesNotFitRoof);
        }
        if (!IsFinite(heightMm) || heightMm <= 0d)
        {
            return Invalid(RoofRafterRequestValidationError.InvalidHeight);
        }
        if (!IsFinite(maximumSpacingMm) || maximumSpacingMm <= 0d)
        {
            return Invalid(RoofRafterRequestValidationError.InvalidMaximumSpacing);
        }
        if (string.IsNullOrWhiteSpace(material))
        {
            return Invalid(RoofRafterRequestValidationError.InvalidMaterial);
        }

        var layoutResult = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(maximumSpacingMm, widthMm));
        if (!layoutResult.IsValid || layoutResult.Layout is null)
        {
            return Invalid(RoofRafterRequestValidationError.InvalidRoof);
        }

        return new RoofRafterRequestValidationResult(
            new RoofRafterCreationRequest(
                widthMm,
                heightMm,
                maximumSpacingMm,
                material!.Trim(),
                geometry.SlopeDegrees),
            layoutResult.Layout,
            RoofRafterRequestValidationError.None);
    }

    private static RoofRafterRequestValidationResult Invalid(
        RoofRafterRequestValidationError error) =>
        new(null, null, error);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
