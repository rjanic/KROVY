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

        var glyphKind = TimberSlopeAnnotationRules.ResolveGlyphKind(data.SlopeDegrees);
        return new TimberAnnotationRefreshPlan(
            EnsureLabel: true,
            ReconcileSlopeArrow: true,
            ShouldSlopeArrowExist: glyphKind == TimberSlopeGlyphKind.DirectionalArrow,
            ShouldHorizontalSlopeMarkerExist: glyphKind == TimberSlopeGlyphKind.HorizontalMarker,
            ReconcileSlopeAngleText: true,
            ShouldSlopeAngleTextExist: TimberSlopeAnnotationRules.ShouldDisplayAngleText(data.SlopeDegrees));
    }
}
