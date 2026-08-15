namespace AcKrovy.Core.Models.Roofs;

public sealed record SimpleGableRafterLayout(
    double RequestedMaximumSpacingMm,
    double RafterPlanWidthMm,
    double RidgeLengthMm,
    double UsableCenterSpanMm,
    int IntervalCount,
    int StationCount,
    double ActualSpacingMm,
    IReadOnlyList<SimpleGableRafter> Rafters,
    string Signature);
