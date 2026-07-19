namespace AcKrovy.Core.Models;

public sealed record TimberAnnotationRefreshPlan(
    bool EnsureLabel,
    bool ReconcileSlopeArrow,
    bool ShouldSlopeArrowExist,
    bool ReconcileSlopeAngleText,
    bool ShouldSlopeAngleTextExist);
