using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberCsvFormatterTests
{
    [Fact]
    public void Individual_UsesBomSemicolonCrLfLocalizedHeadersAndDeterministicOrder()
    {
        var localization = Localization();
        var second = Measurement("K2", 5100, "poznámka");
        var first = Measurement("K1", 5000, "Unicode: ľščťž");

        var document = TimberCsvFormatter.Format(
            [second, first],
            TimberCsvExportMode.Individual,
            localization,
            CultureInfo.GetCultureInfo("sk-SK"));

        Assert.Equal(2, document.RowCount);
        Assert.StartsWith("Položka;Typ;Materiál;Šírka [mm]", document.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", document.Content.Replace("\r\n", string.Empty));
        Assert.True(document.Content.IndexOf("K1;", StringComparison.Ordinal) <
                    document.Content.IndexOf("K2;", StringComparison.Ordinal));
        var bytes = document.ToUtf8WithBom();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("ľščťž", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Formatter_QuotesSemicolonsQuotesAndNewlines()
    {
        var measurement = Measurement(
            "X1",
            5000,
            "line 1;\r\n\"line 2\"",
            TimberElementType.Custom,
            "Väzník; \"sever\"");

        var document = TimberCsvFormatter.Format(
            [measurement],
            TimberCsvExportMode.Individual,
            Localization(),
            CultureInfo.InvariantCulture);

        Assert.Contains("\"Väzník; \"\"sever\"\"\"", document.Content);
        Assert.Contains("\"line 1;\r\n\"\"line 2\"\"\"", document.Content);
    }

    [Fact]
    public void Summarized_ReusesReportGroupingTotalsAndQuantities()
    {
        var first = Measurement("K1", 5000, string.Empty);
        var second = Measurement("K1", 5000, string.Empty);

        var document = TimberCsvFormatter.Format(
            [first, second],
            TimberCsvExportMode.Summarized,
            Localization(),
            CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(2, document.SourceElementCount);
        Assert.Equal(1, document.RowCount);
        var dataLine = document.Content.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.Contains(";2;10;0.128;", dataLine);
    }

    [Fact]
    public void ActiveCulture_ControlsTypedDecimalFormatting()
    {
        var measurement = Measurement("K1", 5000, string.Empty) with
        {
            Data = Measurement("K1", 5000, string.Empty).Data with { WidthMm = 80.5 },
        };

        var document = TimberCsvFormatter.Format(
            [measurement],
            TimberCsvExportMode.Individual,
            Localization(),
            CultureInfo.GetCultureInfo("de-DE"));

        Assert.Contains(";80,5;160;5000;", document.Content);
    }

    [Fact]
    public void EmptyOptionalValues_ProduceEmptyCsvFields()
    {
        var measurement = Measurement("K1", 5000, string.Empty);

        var document = TimberCsvFormatter.Format(
            [measurement],
            TimberCsvExportMode.Individual,
            Localization(),
            CultureInfo.InvariantCulture);

        var dataLine = document.Content.Split(
            ["\r\n"],
            StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.EndsWith(";", dataLine, StringComparison.Ordinal);
    }

    private static TimberCsvLocalization Localization() =>
        new(
            new TimberCsvHeaders(
                "Položka",
                "Typ",
                "Materiál",
                "Šírka [mm]",
                "Výška [mm]",
                "Výrobná dĺžka [mm]",
                "Počet [ks]",
                "Celková dĺžka [m]",
                "Objem [m³]",
                "Poznámka"),
            (type, customName) => type == TimberElementType.Custom
                ? customName ?? "Vlastný prvok"
                : type.ToString(),
            material => material == "Smrek C24" ? "C24 – Smrek / Jedľa" : material);

    private static TimberElementMeasurement Measurement(
        string elementId,
        double cuttingLengthMm,
        string note,
        TimberElementType type = TimberElementType.Rafter,
        string? customName = null)
    {
        var data = new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = elementId,
            ElementType = type,
            CustomElementTypeId = type == TimberElementType.Custom ? "custom-a" : null,
            CustomElementTypeName = customName,
            CustomElementTypePrefix = type == TimberElementType.Custom ? "X" : null,
            WidthMm = 80,
            HeightMm = 160,
            Material = "Smrek C24",
            Note = note,
        };
        return new TimberElementMeasurement(
            data,
            cuttingLengthMm,
            cuttingLengthMm,
            cuttingLengthMm,
            80 * 160 * cuttingLengthMm / 1_000_000_000d);
    }
}
