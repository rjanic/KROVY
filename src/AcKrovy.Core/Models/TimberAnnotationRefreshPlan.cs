namespace AcKrovy.Core.Models;

public sealed record TimberAnnotationRefreshPlan(
    bool EnsureLabel,
    bool ReconcileSlopeArrow,
    bool ShouldSlopeArrowExist,
    bool ShouldHorizontalSlopeMarkerExist,
    bool ShouldPostPerpendicularMarkerExist,
    bool ReconcileSlopeAngleText,
    bool ShouldSlopeAngleTextExist);
