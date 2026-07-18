using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementMeasurer
{
    public static TimberElementMeasurement Measure(
        TimberElementSnapshot snapshot,
        double roundingIncrementMm = TimberCalculator.CuttingLengthRoundingIncrementMm)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return TimberCalculator.Measure(snapshot.Data, snapshot.PlanLengthMm, roundingIncrementMm);
    }

    public static IReadOnlyList<TimberElementMeasurement> MeasureAll(
        IEnumerable<TimberElementSnapshot> snapshots,
        double roundingIncrementMm = TimberCalculator.CuttingLengthRoundingIncrementMm)
    {
        if (snapshots is null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        return snapshots
            .Select(snapshot => Measure(snapshot, roundingIncrementMm))
            .ToList();
    }
}
