namespace AcKrovy.Core.Models.Roofs;

public sealed record RafterLayoutParameters(
    double MaximumSpacingMm,
    double RafterPlanWidthMm,
    double MinimumAutomaticLengthMm = Services.Roofs.RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm);
