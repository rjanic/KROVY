using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationRefreshPlanner
{
    public static TimberAnnotationRefreshPlan Create(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var shouldSlopeAnnotationExist = TimberSlopeArrowCalculator.ShouldDisplay(data.SlopeDegrees);
        return new TimberAnnotationRefreshPlan(
            EnsureLabel: true,
            ReconcileSlopeArrow: true,
            ShouldSlopeArrowExist: shouldSlopeAnnotationExist,
            ReconcileSlopeAngleText: true,
            ShouldSlopeAngleTextExist: shouldSlopeAnnotationExist);
    }
}
