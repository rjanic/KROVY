using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementItemNumberingTests
{
    [Fact]
    public void AssignElementIds_MatchingSignaturesShareElementId()
    {
        var result = Assign(
            Measurement("K1", TimberElementType.Rafter, 80, 160, 5000),
            Measurement("K1", TimberElementType.Rafter, 80, 160, 5000));

        Assert.Equal(new[] { "K1", "K1" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_SameTypeAndSectionDifferentLengthUseDifferentElementIds()
    {
        var result = Assign(
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W1", TimberElementType.Brace, 80, 120, 13000));

        Assert.Equal(new[] { "W1", "W2" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_ExistingMatchingSignatureReusesExistingElementId()
    {
        var result = Assign(
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W2", TimberElementType.Brace, 80, 120, 13000),
            Measurement("W1", TimberElementType.Brace, 80, 120, 13000));

        Assert.Equal(new[] { "W1", "W2", "W2" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_NewSignatureGetsNextFreeElementId()
    {
        var result = Assign(
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W2", TimberElementType.Brace, 80, 120, 13000),
            Measurement("W1", TimberElementType.Brace, 100, 120, 13000));

        Assert.Equal(new[] { "W1", "W2", "W3" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_CopyWithoutGeometryChangeSharesElementId()
    {
        var result = Assign(
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540));

        Assert.Equal(new[] { "W1", "W1" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_CopyWithChangedLengthGetsDifferentElementId()
    {
        var result = Assign(
            Measurement("W1", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W1", TimberElementType.Brace, 80, 120, 13000));

        Assert.Equal(new[] { "W1", "W2" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_DifferentPhysicalHandlesDoNotAffectItemGrouping()
    {
        var first = Measurement("K1", TimberElementType.Rafter, 80, 160, 5000);
        var second = Measurement("K1", TimberElementType.Rafter, 80, 160, 5000);

        var result = Assign(first, second);

        Assert.Equal(result[0].Signature, result[1].Signature);
        Assert.Equal(new[] { "K1", "K1" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_DifferentTypesNeverShareElementId()
    {
        var result = Assign(
            Measurement("K1", TimberElementType.Rafter, 80, 160, 5000),
            Measurement("W1", TimberElementType.Brace, 80, 160, 5000));

        Assert.Equal(new[] { "K1", "W1" }, result.Select(x => x.ElementId));
        Assert.NotEqual(result[0].Signature, result[1].Signature);
    }

    [Fact]
    public void AssignElementIds_ManualLengthMeasurementUsesManualCuttingLengthInSignature()
    {
        var result = Assign(
            Measurement("S1", TimberElementType.Post, 80, 160, 3000),
            Measurement("S1", TimberElementType.Post, 80, 160, 5000));

        Assert.Equal(new[] { "S1", "S2" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_ExistingCorrectElementIdsAreNotRenumbered()
    {
        var result = Assign(
            Measurement("W5", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W5", TimberElementType.Brace, 80, 120, 8540),
            Measurement("W8", TimberElementType.Brace, 80, 120, 13000));

        Assert.Equal(new[] { "W5", "W5", "W8" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_SameRoundedCuttingLengthSharesItemIdentity()
    {
        var first = CalculatedMeasurement("K1", TimberElementType.Rafter, 11021, 0);
        var second = CalculatedMeasurement("K1", TimberElementType.Rafter, 11085, 0);

        var result = Assign(first, second);

        Assert.Equal(11100, first.CuttingLengthMm);
        Assert.Equal(11100, second.CuttingLengthMm);
        Assert.Equal(new[] { "K1", "K1" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_DifferentRoundedCuttingLengthCreatesDifferentItemIdentity()
    {
        var first = CalculatedMeasurement("K1", TimberElementType.Rafter, 11100, 0);
        var second = CalculatedMeasurement("K1", TimberElementType.Rafter, 11101, 0);

        var result = Assign(first, second);

        Assert.Equal(11100, first.CuttingLengthMm);
        Assert.Equal(11200, second.CuttingLengthMm);
        Assert.Equal(new[] { "K1", "K2" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_IndividualAllowanceWithSameRoundedLengthKeepsSharedItemIdentity()
    {
        var first = CalculatedMeasurement("K1", TimberElementType.Rafter, 5000, 100);
        var second = CalculatedMeasurement("K1", TimberElementType.Rafter, 4975, 120);

        var result = Assign(first, second);

        Assert.Equal(5100, first.CuttingLengthMm);
        Assert.Equal(5100, second.CuttingLengthMm);
        Assert.Equal(result[0].Signature, result[1].Signature);
        Assert.Equal(new[] { "K1", "K1" }, result.Select(x => x.ElementId));
    }

    [Fact]
    public void AssignElementIds_IndividualAllowanceWithDifferentRoundedLengthSplitsItemIdentity()
    {
        var first = CalculatedMeasurement("K1", TimberElementType.Rafter, 5000, 100);
        var second = CalculatedMeasurement("K1", TimberElementType.Rafter, 5000, 300);

        var result = Assign(first, second);

        Assert.Equal(5100, first.CuttingLengthMm);
        Assert.Equal(5300, second.CuttingLengthMm);
        Assert.NotEqual(result[0].Signature, result[1].Signature);
        Assert.Equal(new[] { "K1", "K2" }, result.Select(x => x.ElementId));
    }

    private static IReadOnlyList<TimberElementItemAssignment> Assign(
        params TimberElementMeasurement[] measurements) =>
        TimberElementItemNumbering.AssignElementIds(measurements);

    private static TimberElementMeasurement CalculatedMeasurement(
        string elementId,
        TimberElementType type,
        double planLengthMm,
        double cuttingAllowanceMm)
    {
        var data = new TimberElementData
        {
            ElementId = elementId,
            ElementType = type,
            WidthMm = 80,
            HeightMm = 160,
            Material = "Smrek C24",
            CuttingAllowanceMm = cuttingAllowanceMm,
            LengthCalculationMode = LengthCalculationMode.PlanLength,
        };

        return TimberCalculator.Measure(data, planLengthMm);
    }

    private static TimberElementMeasurement Measurement(
        string elementId,
        TimberElementType type,
        double widthMm,
        double heightMm,
        double cuttingLengthMm)
    {
        var data = new TimberElementData
        {
            ElementId = elementId,
            ElementType = type,
            WidthMm = widthMm,
            HeightMm = heightMm,
            Material = "Smrek C24",
        };

        return new TimberElementMeasurement(
            data,
            PlanLengthMm: cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: widthMm * heightMm * cuttingLengthMm / 1_000_000_000d);
    }
}
