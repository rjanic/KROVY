using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementMeasurerTests
{
    [Fact]
    public void Measure_UsesNeutralSnapshotForOneElement()
    {
        var snapshot = Snapshot(LengthCalculationMode.PlanLength, planLengthMm: 3000);

        var measurement = TimberElementMeasurer.Measure(snapshot);

        Assert.Equal(3000, measurement.PlanLengthMm);
        Assert.Equal(3000, measurement.ActualLengthMm);
        Assert.Equal(3100, measurement.CuttingLengthMm);
        Assert.Equal(snapshot.Data, measurement.Data);
    }

    [Fact]
    public void MeasureAll_ReturnsMeasurementForEachSnapshot()
    {
        var snapshots = new[]
        {
            Snapshot(LengthCalculationMode.PlanLength, planLengthMm: 3000),
            Snapshot(LengthCalculationMode.PlanLength, planLengthMm: 4000),
        };

        var measurements = TimberElementMeasurer.MeasureAll(snapshots);

        Assert.Equal(2, measurements.Count);
        Assert.Equal(3000, measurements[0].PlanLengthMm);
        Assert.Equal(4000, measurements[1].PlanLengthMm);
    }

    [Fact]
    public void Measure_UsesAutomaticLengthMode()
    {
        var snapshot = new TimberElementSnapshot(
            new TimberElementData
            {
                ElementType = TimberElementType.Purlin,
                WidthMm = 100,
                HeightMm = 100,
                CuttingAllowanceMm = 0,
                LengthCalculationMode = LengthCalculationMode.AutoByElementType,
            },
            PlanLengthMm: 2500);

        var measurement = TimberElementMeasurer.Measure(snapshot);

        Assert.Equal(LengthCalculationMode.PlanLength, TimberCalculator.ResolveLengthCalculationMode(snapshot.Data));
        Assert.Equal(2500, measurement.ActualLengthMm);
    }

    [Fact]
    public void Measure_UsesSlopeCorrectedLength()
    {
        var snapshot = new TimberElementSnapshot(
            new TimberElementData
            {
                ElementType = TimberElementType.Rafter,
                WidthMm = 100,
                HeightMm = 100,
                SlopeDegrees = 60,
                CuttingAllowanceMm = 0,
                LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
            },
            PlanLengthMm: 1000);

        var measurement = TimberElementMeasurer.Measure(snapshot);

        Assert.Equal(2000, measurement.ActualLengthMm, precision: 6);
        Assert.Equal(2000, measurement.CuttingLengthMm, precision: 6);
    }

    [Fact]
    public void Measure_UsesManualLength()
    {
        var snapshot = new TimberElementSnapshot(
            new TimberElementData
            {
                ElementType = TimberElementType.Post,
                WidthMm = 100,
                HeightMm = 100,
                ManualLengthMm = 2800,
                CuttingAllowanceMm = 50,
                LengthCalculationMode = LengthCalculationMode.ManualLength,
            },
            PlanLengthMm: 1000);

        var measurement = TimberElementMeasurer.Measure(snapshot);

        Assert.Equal(1000, measurement.PlanLengthMm);
        Assert.Equal(2800, measurement.ActualLengthMm);
        Assert.Equal(2900, measurement.CuttingLengthMm);
    }

    [Fact]
    public void ReportBuilder_AggregatesMeasurementsFromNeutralSnapshots()
    {
        var data = new TimberElementData
        {
            ElementType = TimberElementType.Rafter,
            Material = "Smrek C24",
            WidthMm = 80,
            HeightMm = 160,
            CuttingAllowanceMm = 0,
            LengthCalculationMode = LengthCalculationMode.PlanLength,
        };
        var snapshots = new[]
        {
            new TimberElementSnapshot(data, PlanLengthMm: 5000),
            new TimberElementSnapshot(data, PlanLengthMm: 5000),
        };

        var report = TimberReportBuilder.Build(TimberElementMeasurer.MeasureAll(snapshots));

        var line = Assert.Single(report.Lines);
        Assert.Equal(2, line.Count);
        Assert.Equal(10000, line.TotalLengthMm);
        Assert.Equal(0.128, line.TotalVolumeM3, precision: 6);
        Assert.Equal(2, report.SourceElementCount);
        Assert.Equal(0.128, report.TotalVolumeM3, precision: 6);
    }

    [Fact]
    public void MeasureAll_ProvidesRecalculationInputsAndOutputs()
    {
        var snapshot = Snapshot(LengthCalculationMode.PlanLength, planLengthMm: 1234);

        var measurement = Assert.Single(TimberElementMeasurer.MeasureAll(new[] { snapshot }));

        Assert.Equal(snapshot.Data, measurement.Data);
        Assert.Equal(1234, measurement.PlanLengthMm);
        Assert.Equal(1234, measurement.ActualLengthMm);
        Assert.Equal(1400, measurement.CuttingLengthMm);
    }

    private static TimberElementSnapshot Snapshot(LengthCalculationMode mode, double planLengthMm) =>
        new(
            new TimberElementData
            {
                ElementId = "K1",
                ElementType = TimberElementType.Rafter,
                WidthMm = 80,
                HeightMm = 160,
                CuttingAllowanceMm = 100,
                LengthCalculationMode = mode,
                Material = "Smrek C24",
            },
            planLengthMm);
}
