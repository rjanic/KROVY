using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationRefreshPlanner
{
    public static TimberAnnotationRefreshPlan Create(
        TimberElementData data,
        bool isRectangularFootprintPost = false)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (isRectangularFootprintPost)
        {
            return new TimberAnnotationRefreshPlan(
                EnsureLabel: false,
                ReconcileSlopeArrow: false,
                ShouldSlopeArrowExist: false,
                ShouldHorizontalSlopeMarkerExist: false,
                ShouldPostPerpendicularMarkerExist: false,
                ReconcileSlopeAngleText: false,
                ShouldSlopeAngleTextExist: false);
        }

        var glyphKind = TimberSlopeAnnotationRules.ResolveGlyphKind(data.ElementType, data.SlopeDegrees);
        return new TimberAnnotationRefreshPlan(
            EnsureLabel: true,
            ReconcileSlopeArrow: true,
            ShouldSlopeArrowExist: glyphKind == TimberSlopeGlyphKind.DirectionalArrow,
            ShouldHorizontalSlopeMarkerExist: glyphKind == TimberSlopeGlyphKind.HorizontalMarker,
            ShouldPostPerpendicularMarkerExist: glyphKind == TimberSlopeGlyphKind.PostPerpendicularMarker,
            ReconcileSlopeAngleText: true,
            ShouldSlopeAngleTextExist: TimberSlopeAnnotationRules.ShouldDisplayAngleText(
                data.ElementType,
                data.SlopeDegrees));
    }
}
