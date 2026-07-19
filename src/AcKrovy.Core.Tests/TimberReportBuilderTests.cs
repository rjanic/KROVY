using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberReportBuilderTests
{
    [Fact]
    public void Build_ReturnsEmptyReportForEmptyInput()
    {
        var report = TimberReportBuilder.Build(Array.Empty<TimberElementMeasurement>());

        Assert.Empty(report.Lines);
        Assert.Equal(0, report.SourceElementCount);
        Assert.Equal(0, report.TotalVolumeM3);
    }

    [Fact]
    public void Build_CreatesSingleLineForOneElement()
    {
        var measurement = Measurement(TimberElementType.Rafter, "Smrek C24", 80, 160, 5000, 0.064);

        var report = TimberReportBuilder.Build(new[] { measurement });

        var line = Assert.Single(report.Lines);
        Assert.Equal("K1", line.ElementId);
        Assert.Equal(TimberElementType.Rafter, line.ElementType);
        Assert.Equal("Smrek C24", line.Material);
        Assert.Equal(1, line.Count);
        Assert.Equal(5000, line.TotalLengthMm);
        Assert.Equal(0.064, line.TotalVolumeM3, precision: 6);
        Assert.Equal(1, report.SourceElementCount);
        Assert.Equal(0.064, report.TotalVolumeM3, precision: 6);
    }

    [Fact]
    public void Build_GroupsMatchingElements()
    {
        var first = Measurement(TimberElementType.Rafter, "Smrek C24", 80, 160, 5000, 0.064, "K1");
        var second = Measurement(TimberElementType.Rafter, "Smrek C24", 80, 160, 5000, 0.064, "K1");

        var report = TimberReportBuilder.Build(new[] { first, second });

        var line = Assert.Single(report.Lines);
        Assert.Equal(2, line.Count);
        Assert.Equal("K1", line.ElementId);
        Assert.Equal(10000, line.TotalLengthMm);
        Assert.Equal(0.128, line.TotalVolumeM3, precision: 6);
        Assert.Equal(2, report.SourceElementCount);
        Assert.Equal(0.128, report.TotalVolumeM3, precision: 6);
    }

    [Fact]
    public void Build_UsesExistingItemNumberForReportLine()
    {
        var first = Measurement(TimberElementType.Brace, "Smrek C24", 100, 140, 3000, 0.042, "V1");
        var second = Measurement(TimberElementType.Brace, "Smrek C24", 100, 140, 3000, 0.042, "V1");

        var report = TimberReportBuilder.Build(new[] { first, second });

        var line = Assert.Single(report.Lines);
        Assert.Equal("V1", line.ElementId);
        Assert.Equal(2, line.Count);
    }

    [Fact]
    public void Build_SeparatesDifferentElements()
    {
        var rafter = Measurement(TimberElementType.Rafter, "Smrek C24", 80, 160, 5000, 0.064);
        var purlin = Measurement(TimberElementType.Purlin, "Smrek C24", 160, 220, 4000, 0.1408);
        var differentLength = Measurement(TimberElementType.Rafter, "Smrek C24", 80, 160, 5100, 0.06528);

        var report = TimberReportBuilder.Build(new[] { purlin, differentLength, rafter });

        Assert.Equal(3, report.Lines.Count);
        Assert.Equal(3, report.SourceElementCount);
        Assert.Equal(0.27008, report.TotalVolumeM3, precision: 6);
        Assert.All(report.Lines, line => Assert.Equal(1, line.Count));
        Assert.Contains(report.Lines, line =>
            line.ElementType == TimberElementType.Rafter && line.CuttingLengthMm == 5000);
        Assert.Contains(report.Lines, line =>
            line.ElementType == TimberElementType.Rafter && line.CuttingLengthMm == 5100);
        Assert.Contains(report.Lines, line =>
            line.ElementType == TimberElementType.Purlin && line.CuttingLengthMm == 4000);
    }

    private static TimberElementMeasurement Measurement(
        TimberElementType type,
        string material,
        double widthMm,
        double heightMm,
        double cuttingLengthMm,
        double volumeM3,
        string elementId = "K1")
    {
        var data = new TimberElementData
        {
            ElementId = elementId,
            ElementType = type,
            Material = material,
            WidthMm = widthMm,
            HeightMm = heightMm,
        };

        return new TimberElementMeasurement(
            data,
            PlanLengthMm: cuttingLengthMm,
            ActualLengthMm: cuttingLengthMm,
            CuttingLengthMm: cuttingLengthMm,
            VolumeM3: volumeM3);
    }
}
