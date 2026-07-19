using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelFormatterTests
{
    [Fact]
    public void FormatDimensions_UsesCompactLowercaseXByDefault()
    {
        var result = TimberElementLabelFormatter.FormatDimensions(Data());

        Assert.Equal("80x160", result);
    }

    [Fact]
    public void Format_UsesSameDimensionFormatterInLabelContents()
    {
        var result = TimberElementLabelFormatter.Format(
            Data(),
            Measurement(Data(), cuttingLengthMm: 3400));

        Assert.Equal("K4\\P80x160\\P3400 mm", result);
    }

    [Fact]
    public void Format_UpdatesElementIdInContents()
    {
        var data = Data() with { ElementId = "K8" };

        var result = TimberElementLabelFormatter.Format(
            data,
            Measurement(data, cuttingLengthMm: 3400));

        Assert.Equal("K8\\P80x160\\P3400 mm", result);
    }

    [Fact]
    public void Format_UpdatesCuttingLengthInContents()
    {
        var data = Data();

        var result = TimberElementLabelFormatter.Format(
            data,
            Measurement(data, cuttingLengthMm: 3600));

        Assert.Equal("K4\\P80x160\\P3600 mm", result);
    }

    [Fact]
    public void FormatDimensions_CanUseAlternativeSeparatorFromOptions()
    {
        var result = TimberElementLabelFormatter.FormatDimensions(
            Data(),
            new TimberElementLabelFormatOptions { DimensionSeparator = "/" });

        Assert.Equal("80/160", result);
    }

    private static TimberElementData Data() => new()
    {
        ElementId = "K4",
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        Material = "Smrek C24",
    };

    private static TimberElementMeasurement Measurement(TimberElementData data, double cuttingLengthMm) =>
        new(
            data,
            PlanLengthMm: cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: 0);
}
