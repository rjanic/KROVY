using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Production-compatible gable adapter over the shared bounded-plane rafter engine.
/// Preserves the existing output model, ordering and RAFTER_LAYOUT_V1 signature.
/// </summary>
public static class SimpleGableRafterLayoutSolver
{
    public const double CoordinateToleranceMm = RoofRafterLayoutSolver.CoordinateToleranceMm;

    public static SimpleGableRafterLayoutResult Solve(
        SimpleGableRoofGeometry geometry,
        RafterLayoutParameters parameters)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var shared = RoofRafterLayoutSolver.Solve(geometry, parameters);
        if (!shared.IsValid || shared.Layout is null)
        {
            return Invalid(Map(shared.Error));
        }

        var layout = shared.Layout;
        var rafters = layout.Rafters.Select(rafter => new SimpleGableRafter(
            rafter.Face,
            rafter.StationIndex,
            rafter.StationCount,
            rafter.StationFraction,
            rafter.PlanStart,
            rafter.PlanEnd,
            rafter.SlopeDegrees)).ToArray();
        return new SimpleGableRafterLayoutResult(
            true,
            new SimpleGableRafterLayout(
                layout.RequestedMaximumSpacingMm,
                layout.RafterPlanWidthMm,
                layout.StationSpanMm,
                layout.UsableCenterSpanMm,
                layout.IntervalCount,
                layout.StationCount,
                layout.ActualSpacingMm,
                rafters,
                layout.Signature),
            SimpleGableRafterLayoutError.None);
    }

    private static SimpleGableRafterLayoutError Map(RoofRafterLayoutError error) => error switch
    {
        RoofRafterLayoutError.InvalidMaximumSpacing =>
            SimpleGableRafterLayoutError.InvalidMaximumSpacing,
        RoofRafterLayoutError.InvalidRafterPlanWidth =>
            SimpleGableRafterLayoutError.InvalidRafterPlanWidth,
        RoofRafterLayoutError.TooManyStations =>
            SimpleGableRafterLayoutError.TooManyStations,
        _ => SimpleGableRafterLayoutError.InvalidRoofGeometry,
    };

    private static SimpleGableRafterLayoutResult Invalid(
        SimpleGableRafterLayoutError error) =>
        new(false, null, error);
}
