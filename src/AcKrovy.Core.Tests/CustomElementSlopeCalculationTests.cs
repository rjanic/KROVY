using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CustomElementSlopeCalculationTests
{
    private const double PlanLengthMm = 4000d;
    private const double AllowanceMm = 100d;
    private const double RoundingStepMm = 100d;

    [Fact]
    public void CustomAtZeroDegrees_UsesPlanGeometryLength()
    {
        var measurement = Measure(CustomData(0d));

        Assert.Equal(PlanLengthMm, measurement.PlanLengthMm);
        Assert.Equal(PlanLengthMm, measurement.ActualLengthMm);
    }

    [Fact]
    public void CustomAtThirtyFiveDegrees_HasGreaterActualLength()
    {
        var level = Measure(CustomData(0d));
        var sloped = Measure(CustomData(35d));

        Assert.True(sloped.ActualLengthMm > level.ActualLengthMm);
    }

    [Fact]
    public void CustomAndRafter_UseSameSlopeCorrection()
    {
        var custom = Measure(CustomData(35d));
        var rafter = Measure(RafterData(35d));

        Assert.Equal(rafter.ActualLengthMm, custom.ActualLengthMm, precision: 9);
        Assert.Equal(rafter.CuttingLengthMm, custom.CuttingLengthMm);
    }

    [Fact]
    public void CuttingAllowanceAndRounding_AreAppliedAfterSlopeCorrection()
    {
        var data = CustomData(35d);

        var measurement = Measure(data);
        var expectedActual = TimberCalculator.CalculateSlopeCorrectedLengthMm(
            PlanLengthMm,
            35d);
        var expectedCutting = TimberCuttingLengthCalculator.Calculate(
            expectedActual,
            AllowanceMm,
            RoundingStepMm);

        Assert.Equal(expectedActual, measurement.ActualLengthMm, precision: 9);
        Assert.Equal(expectedCutting, measurement.CuttingLengthMm);
        Assert.Equal(5000d, measurement.CuttingLengthMm);
    }

    [Fact]
    public void EditFromZeroToThirtyFiveDegrees_ChangesCuttingLength()
    {
        var original = CustomData(0d);
        var edited = TimberElementPatcher.Apply(
            original,
            SlopePatch(35d));

        Assert.NotEqual(
            Measure(original).CuttingLengthMm,
            Measure(edited).CuttingLengthMm);
        Assert.Equal(5000d, Measure(edited).CuttingLengthMm);
    }

    [Fact]
    public void EditFromThirtyFiveToZeroDegrees_RestoresLevelCuttingLength()
    {
        var original = CustomData(35d);
        var edited = TimberElementPatcher.Apply(
            original,
            SlopePatch(0d));

        Assert.Equal(4100d, Measure(edited).CuttingLengthMm);
        Assert.NotEqual(
            Measure(original).CuttingLengthMm,
            Measure(edited).CuttingLengthMm);
    }

    [Fact]
    public void SlopeDerivedCuttingLength_ChangesSignature()
    {
        var level = Measure(CustomData(0d));
        var sloped = Measure(CustomData(35d));

        Assert.NotEqual(
            TimberElementSignature.FromMeasurement(level),
            TimberElementSignature.FromMeasurement(sloped));
    }

    [Fact]
    public void ItemSynchronization_SplitsChangedSlopeFromExistingSignature()
    {
        var unchangedLevel = Measure(CustomData(0d) with { ElementId = "PR1" });
        var changedSloped = Measure(CustomData(35d) with { ElementId = "PR1" });

        var assignments = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(
                unchangedLevel,
                IsChanged: false),
            new TimberElementItemNumberingCandidate(
                changedSloped,
                IsChanged: true),
        ]);

        Assert.Equal("PR1", assignments[0].ElementId);
        Assert.Equal("PR2", assignments[1].ElementId);
    }

    [Fact]
    public void SameCustomDefinitionWithDifferentSlopeLengths_GetsDifferentItems()
    {
        var assignments = TimberElementItemNumbering.AssignElementIds(
        [
            Measure(CustomData(0d) with { ElementId = string.Empty }),
            Measure(CustomData(35d) with { ElementId = string.Empty }),
        ]);

        Assert.Equal(["PR1", "PR2"], assignments.Select(item => item.ElementId));
    }

    [Fact]
    public void SlopeDirection_DoesNotChangeLengthOrSignature()
    {
        var normal = Measure(CustomData(35d) with
        {
            IsSlopeDirectionReversed = false,
        });
        var reversed = Measure(CustomData(35d) with
        {
            IsSlopeDirectionReversed = true,
        });

        Assert.Equal(normal.ActualLengthMm, reversed.ActualLengthMm);
        Assert.Equal(normal.CuttingLengthMm, reversed.CuttingLengthMm);
        Assert.Equal(
            TimberElementSignature.FromMeasurement(normal),
            TimberElementSignature.FromMeasurement(reversed));
    }

    [Fact]
    public void LiveGeometryRemeasure_UsesCurrentSlope()
    {
        var data = CustomData(35d);

        var before = TimberCalculator.Measure(
            data,
            planLengthMm: 4000d,
            roundingIncrementMm: RoundingStepMm);
        var after = TimberCalculator.Measure(
            data,
            planLengthMm: 4500d,
            roundingIncrementMm: RoundingStepMm);

        Assert.True(after.ActualLengthMm > before.ActualLengthMm);
        Assert.True(after.CuttingLengthMm > before.CuttingLengthMm);
        Assert.Equal(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(4500d, 35d),
            after.ActualLengthMm,
            precision: 9);
    }

    [Fact]
    public void CopyMetadata_PreservesSlopeAwareMeasurement()
    {
        var source = CustomData(35d);
        var copy = source with { ElementId = string.Empty };

        var sourceMeasurement = Measure(source);
        var copyMeasurement = Measure(copy);

        Assert.Equal(source.SlopeDegrees, copy.SlopeDegrees);
        Assert.Equal(sourceMeasurement.ActualLengthMm, copyMeasurement.ActualLengthMm);
        Assert.Equal(sourceMeasurement.CuttingLengthMm, copyMeasurement.CuttingLengthMm);
    }

    [Fact]
    public void ExplicitRenumber_OrdersCustomItemsBySlopeDerivedCuttingLength()
    {
        var sloped = Measure(CustomData(35d) with { ElementId = "PR8" });
        var level = Measure(CustomData(0d) with { ElementId = "PR9" });

        var assignments =
            TimberElementItemNumbering.RenumberElementIdsByCuttingLength(
                [sloped, level]);

        Assert.Equal("PR2", assignments[0].ElementId);
        Assert.Equal("PR1", assignments[1].ElementId);
        Assert.True(
            assignments[0].Measurement.CuttingLengthMm >
            assignments[1].Measurement.CuttingLengthMm);
    }

    [Fact]
    public void Report_UsesSlopeAwareCustomCuttingLength()
    {
        var level = Measure(CustomData(0d) with { ElementId = "PR1" });
        var sloped = Measure(CustomData(35d) with { ElementId = "PR2" });

        var report = TimberReportBuilder.Build([level, sloped]);

        Assert.Contains(report.Lines, line => line.CuttingLengthMm == 4100d);
        Assert.Contains(report.Lines, line => line.CuttingLengthMm == 5000d);
    }

    [Fact]
    public void BuiltInRafter_RemainsSlopeAware()
    {
        var level = Measure(RafterData(0d));
        var sloped = Measure(RafterData(35d));

        Assert.Equal(
            LengthCalculationMode.SlopeCorrected,
            TimberCalculator.ResolveLengthCalculationMode(RafterData(35d)));
        Assert.True(sloped.ActualLengthMm > level.ActualLengthMm);
    }

    private static TimberElementMeasurement Measure(TimberElementData data) =>
        TimberCalculator.Measure(
            data,
            PlanLengthMm,
            RoundingStepMm);

    private static TimberElementData CustomData(double slopeDegrees) =>
        CustomElementDefinitionRules.Apply(
            TimberElementDefaults.For(TimberElementType.Custom) with
            {
                WidthMm = 100d,
                HeightMm = 200d,
                SlopeDegrees = slopeDegrees,
                CuttingAllowanceMm = AllowanceMm,
                LengthCalculationMode = LengthCalculationMode.AutoByElementType,
            },
            new CustomElementDefinition(
                "definition-pr",
                "Prievlak",
                "PR"));

    private static TimberElementData RafterData(double slopeDegrees) =>
        TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            WidthMm = 100d,
            HeightMm = 200d,
            SlopeDegrees = slopeDegrees,
            CuttingAllowanceMm = AllowanceMm,
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        };

    private static TimberElementPatch SlopePatch(double slopeDegrees) =>
        new(
            ElementType: null,
            WidthMm: null,
            HeightMm: null,
            SlopeDegrees: slopeDegrees,
            RoofPlaneId: null,
            CuttingAllowanceMm: null,
            LengthCalculationMode: null,
            ManualLengthMm: null,
            Material: null,
            Note: null,
            IsSlopeDirectionReversed: null);
}
