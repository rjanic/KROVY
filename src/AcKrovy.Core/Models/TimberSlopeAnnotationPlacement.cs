namespace AcKrovy.Core.Models;

public sealed record TimberSlopeAnnotationLongitudinalInterval(double MinimumMm, double MaximumMm);

public sealed record TimberSlopeAnnotationPlacement(double AnchorDistanceMm, bool UsesPreferredPosition);
