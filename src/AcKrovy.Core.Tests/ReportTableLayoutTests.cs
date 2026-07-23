using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ReportTableLayoutTests
{
    private static readonly string[] CultureNames =
        ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];

    [Fact]
    public void ColumnWidths_UseStableCompactProportions()
    {
        Assert.Equal(
            ReportTableLayout.MinimumTypeColumnWidth,
            ReportTableLayout.ColumnWidths[ReportTableLayout.TypeColumn]);
        Assert.Equal(
            ReportTableLayout.MinimumMaterialColumnWidth,
            ReportTableLayout.ColumnWidths[ReportTableLayout.MaterialColumn]);
        Assert.Equal(20d, ReportTableLayout.ColumnWidths[ReportTableLayout.WidthColumn]);
        Assert.Equal(20d, ReportTableLayout.ColumnWidths[ReportTableLayout.HeightColumn]);
        Assert.Equal(27d, ReportTableLayout.ColumnWidths[ReportTableLayout.PieceLengthColumn]);
        Assert.Equal(18d, ReportTableLayout.ColumnWidths[ReportTableLayout.CountColumn]);
        Assert.Equal(30d, ReportTableLayout.ColumnWidths[ReportTableLayout.TotalLengthColumn]);
        Assert.Equal(28d, ReportTableLayout.ColumnWidths[ReportTableLayout.VolumeColumn]);
        Assert.Equal(
            ReportTableLayout.ColumnCount,
            ReportTableLayout.ColumnWidths.Count);
    }

    [Fact]
    public void TextColumns_HaveDedicatedMinimumWidths()
    {
        var typeWidth = ReportTableLayout.ColumnWidths[ReportTableLayout.TypeColumn];
        var materialWidth = ReportTableLayout.ColumnWidths[ReportTableLayout.MaterialColumn];

        Assert.True(typeWidth >= 40d);
        Assert.True(materialWidth >= 48d);
        Assert.True(materialWidth > typeWidth);
        Assert.True(materialWidth > 28d);
    }

    [Fact]
    public void LongestUnbreakableToken_DeterminesMinimumWidth()
    {
        var values = new[] { "short words", "Brettschichtholz" };

        var width = ReportTableLayout.CalculateMinimumTextColumnWidth(values, 0d);

        Assert.Equal(16, ReportTableLayout.GetLongestUnbreakableTokenLength(values));
        Assert.True(
            width >=
            (16 * ReportTableLayout.ConservativeCharacterWidth) +
            ReportTableLayout.HorizontalCellPadding);
    }

    [Fact]
    public void GermanWallPlate_FitsWithoutSplittingMauerlatte()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var displayName = TimberElementTypeDisplayNameProvider.GetDisplayName(
            TimberElementType.WallPlate,
            culture);
        var typeWidth = ReportTableLayout
            .GetColumnWidths([displayName], [])
            [ReportTableLayout.TypeColumn];

        Assert.Equal("Mauerlatte", displayName);
        Assert.True(HasUnbreakableTokenCapacity(displayName, typeWidth));
    }

    [Fact]
    public void LongCatalogMaterialTokens_FitWithoutSplitting()
    {
        var materialDisplayNames = GetAllLocalizedMaterialReportDisplayNames().ToArray();
        var materialWidth = ReportTableLayout
            .GetColumnWidths([], materialDisplayNames)
            [ReportTableLayout.MaterialColumn];

        Assert.Contains(
            materialDisplayNames,
            value => value.Contains("Brettschichtholz", StringComparison.Ordinal));
        Assert.Contains(
            materialDisplayNames,
            value => value.Contains("Sichtkantholz", StringComparison.Ordinal));
        Assert.All(
            materialDisplayNames,
            value => Assert.True(HasUnbreakableTokenCapacity(value, materialWidth)));
    }

    [Theory]
    [InlineData(
        "KVH C24 (Si) – Sichtkantholz",
        "KVH C24 (Si)\nSichtkantholz")]
    [InlineData(
        "C24 – Fichte / Tanne",
        "C24\nFichte / Tanne")]
    [InlineData(
        "BSH GL24h – Brettschichtholz",
        "BSH GL24h\nBrettschichtholz")]
    public void MaterialReportFormatter_CreatesExactTwoLinesWithoutSeparator(
        string displayName,
        string expected)
    {
        var formatted = ReportMaterialDisplayFormatter.FormatMaterialForReport(displayName);

        Assert.Equal(expected, formatted);
        Assert.Equal(2, formatted.Split('\n').Length);
        Assert.DoesNotContain(
            ReportMaterialDisplayFormatter.DescriptionSeparator,
            formatted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialReportFormatter_ValueWithoutSeparatorRemainsUnchanged()
    {
        const string legacyMaterial = "KVH SPECIAL";

        var formatted =
            ReportMaterialDisplayFormatter.FormatMaterialForReport(legacyMaterial);

        Assert.Equal(legacyMaterial, formatted);
    }

    [Fact]
    public void MaterialReportFormatter_DoesNotChangeCanonicalMaterial()
    {
        const string canonicalMaterial = TimberMaterialCatalog.KvhC24Si;
        var displayName = TimberMaterialDisplayNameProvider.GetDisplayName(
            canonicalMaterial,
            CultureInfo.GetCultureInfo("de-DE"));

        var formatted =
            ReportMaterialDisplayFormatter.FormatMaterialForReport(displayName);

        Assert.Equal(TimberMaterialCatalog.KvhC24Si, canonicalMaterial);
        Assert.Equal("KVH C24 (Si)\nSichtkantholz", formatted);
    }

    [Fact]
    public void UnknownLongReportMaterial_ExpandsOnlyMaterialColumn()
    {
        const string unknownLegacyMaterial = "ExtraordinarilyLongLegacyMaterialToken";
        var baseline = ReportTableLayout.ColumnWidths.ToArray();

        var widths = ReportTableLayout.GetColumnWidths(
            ["Pfette"],
            [unknownLegacyMaterial]);

        Assert.True(widths[ReportTableLayout.MaterialColumn] >
                    baseline[ReportTableLayout.MaterialColumn]);
        Assert.True(HasUnbreakableTokenCapacity(
            unknownLegacyMaterial,
            widths[ReportTableLayout.MaterialColumn]));
        for (var column = 0; column < ReportTableLayout.ColumnCount; column++)
        {
            if (column != ReportTableLayout.MaterialColumn)
            {
                Assert.Equal(baseline[column], widths[column]);
            }
        }
    }

    [Fact]
    public void ShortReportMaterials_CreateNarrowerMaterialColumn()
    {
        var shortMaterials = new[]
        {
            "C16\nFichte / Tanne",
            "KVH C24 (Si)\nSichtkantholz",
        };

        var shortWidth = GetMaterialColumnWidth(shortMaterials);
        var fullCatalogWidth = GetMaterialColumnWidth(
            GetAllLocalizedMaterialReportDisplayNames());

        Assert.True(shortWidth < fullCatalogWidth);
        Assert.True(shortWidth >= ReportTableLayout.MinimumMaterialColumnWidth);
    }

    [Fact]
    public void AddingAndRemovingLongMaterial_ExpandsAndShrinksMaterialColumn()
    {
        var shortMaterials = new[]
        {
            "C16\nFichte / Tanne",
            "KVH C24 (Si)\nSichtkantholz",
        };
        const string longMaterial =
            "KVH C24 (Si)\nkantownik do zastosowań widocznych";

        var shortWidthBefore = GetMaterialColumnWidth(shortMaterials);
        var expandedWidth = GetMaterialColumnWidth(
            shortMaterials.Append(longMaterial));
        var shortWidthAfter = GetMaterialColumnWidth(shortMaterials);

        Assert.True(expandedWidth > shortWidthBefore);
        Assert.Equal(shortWidthBefore, shortWidthAfter);
    }

    [Fact]
    public void MaterialWidth_UsesFormattedSegmentsFromCurrentReport()
    {
        const string displayName =
            "KVH C24 (Si) – kantownik do zastosowań widocznych";
        var formatted =
            ReportMaterialDisplayFormatter.FormatMaterialForReport(displayName);

        var width = GetMaterialColumnWidth([formatted]);
        var longestSegmentLength = formatted.Split('\n').Max(segment => segment.Length);
        var expectedMinimum =
            (longestSegmentLength * ReportTableLayout.ConservativeCharacterWidth) +
            ReportTableLayout.HorizontalCellPadding;

        Assert.Equal(2, formatted.Split('\n').Length);
        Assert.True(width >= expectedMinimum);
        Assert.True(HasUnbreakableTokenCapacity(formatted, width));
    }

    [Fact]
    public void TextColumnWidths_DoNotUseWholeCatalogAsGlobalMinimum()
    {
        Assert.Equal(
            ReportTableLayout.MinimumTypeColumnWidth,
            ReportTableLayout.ColumnWidths[ReportTableLayout.TypeColumn]);
        Assert.Equal(
            ReportTableLayout.MinimumMaterialColumnWidth,
            ReportTableLayout.ColumnWidths[ReportTableLayout.MaterialColumn]);

        var actualReportWidths = ReportTableLayout.GetColumnWidths(
            ["Sparren"],
            ["C16\nFichte / Tanne"]);
        var fullCatalogMaterialWidth = GetMaterialColumnWidth(
            GetAllLocalizedMaterialReportDisplayNames());

        Assert.True(
            actualReportWidths[ReportTableLayout.MaterialColumn] <
            fullCatalogMaterialWidth);
    }

    [Fact]
    public void TypeColumnWidth_UsesOnlyTypesInCurrentReport()
    {
        var shortTypeWidth = ReportTableLayout
            .GetColumnWidths(["Pfette"], [])
            [ReportTableLayout.TypeColumn];
        var longTypeWidth = ReportTableLayout
            .GetColumnWidths(["Zange / Kehlbalken"], [])
            [ReportTableLayout.TypeColumn];

        Assert.True(longTypeWidth > shortTypeWidth);
        Assert.True(HasUnbreakableTokenCapacity("Zange / Kehlbalken", longTypeWidth));
    }

    [Fact]
    public void NumericColumns_RemainCompact()
    {
        var numericColumns = new[]
        {
            ReportTableLayout.WidthColumn,
            ReportTableLayout.HeightColumn,
            ReportTableLayout.PieceLengthColumn,
            ReportTableLayout.CountColumn,
            ReportTableLayout.TotalLengthColumn,
            ReportTableLayout.VolumeColumn,
        };

        Assert.All(numericColumns, column =>
            Assert.InRange(ReportTableLayout.ColumnWidths[column], 18d, 30d));
    }

    [Fact]
    public void AllLocalizedTypesAndCatalogMaterials_FitPracticalTwoLineCapacity()
    {
        var typeDisplayNames = GetAllLocalizedTypeDisplayNames().ToArray();
        var materialDisplayNames =
            GetAllLocalizedMaterialReportDisplayNames().ToArray();
        var widths = ReportTableLayout.GetColumnWidths(
            typeDisplayNames,
            materialDisplayNames);

        Assert.All(
            typeDisplayNames,
            value => Assert.InRange(
                EstimateWrappedLines(
                    value,
                    widths[ReportTableLayout.TypeColumn]),
                1,
                2));
        Assert.All(
            materialDisplayNames,
            value => Assert.InRange(
                EstimateWrappedLines(
                    value,
                    widths[ReportTableLayout.MaterialColumn]),
                1,
                2));
    }

    [Fact]
    public void Layout_IsDeterministicAcrossCultures()
    {
        var expected = ReportTableLayout.ColumnWidths.ToArray();

        foreach (var cultureName in CultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            _ = Enum.GetValues<TimberElementType>()
                .Select(type => TimberElementTypeDisplayNameProvider.GetDisplayName(type, culture))
                .ToArray();
            _ = TimberMaterialCatalog.Items
                .Select(item => TimberMaterialDisplayNameProvider.GetDisplayName(
                    item.StoredValue,
                    culture))
                .ToArray();

            Assert.Equal(expected, ReportTableLayout.ColumnWidths);
        }
    }

    [Fact]
    public void MaximumDataRowHeight_IsBoundedForTwoTextLines()
    {
        Assert.Equal(12d, ReportTableLayout.MaximumDataRowHeight);
        Assert.True(
            ReportTableLayout.MaximumDataRowHeight >
            ReportTableLayout.DefaultRowHeight);
    }

    [Fact]
    public void LocalizedReportDisplay_DoesNotChangeReportData()
    {
        var data = new TimberElementData
        {
            ElementId = "K1",
            ElementType = TimberElementType.Rafter,
            WidthMm = 80,
            HeightMm = 160,
            Material = TimberMaterialCatalog.BshGl24h,
        };
        var measurement = new TimberElementMeasurement(
            data,
            PlanLengthMm: 4000,
            ActualLengthMm: 4000,
            CuttingLengthMm: 4200,
            VolumeM3: 0.05376);
        var report = TimberReportBuilder.Build([measurement]);
        var reportLine = Assert.Single(report.Lines);

        foreach (var cultureName in CultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            _ = TimberElementTypeDisplayNameProvider.GetDisplayName(
                reportLine.ElementType,
                culture);
            _ = TimberMaterialDisplayNameProvider.GetDisplayName(
                reportLine.Material,
                culture);
            _ = ReportMaterialDisplayFormatter.FormatMaterialForReport(
                TimberMaterialDisplayNameProvider.GetDisplayName(
                    reportLine.Material,
                    culture));

            Assert.Equal(TimberElementType.Rafter, reportLine.ElementType);
            Assert.Equal(TimberMaterialCatalog.BshGl24h, reportLine.Material);
        }
    }

    private static int EstimateWrappedLines(string value, double columnWidth)
    {
        var usableWidth = columnWidth - ReportTableLayout.HorizontalCellPadding;
        return value
            .Split('\n')
            .Sum(line => Math.Max(
                1,
                (int)Math.Ceiling(
                    line.Length *
                    ReportTableLayout.ConservativeCharacterWidth /
                    usableWidth)));
    }

    private static bool HasUnbreakableTokenCapacity(string value, double columnWidth)
    {
        var longestTokenLength = ReportTableLayout.GetLongestUnbreakableTokenLength([value]);
        var usableWidth = columnWidth - ReportTableLayout.HorizontalCellPadding;
        return usableWidth >=
            longestTokenLength * ReportTableLayout.ConservativeCharacterWidth;
    }

    private static double GetMaterialColumnWidth(IEnumerable<string> displayedMaterials) =>
        ReportTableLayout
            .GetColumnWidths([], displayedMaterials)
            [ReportTableLayout.MaterialColumn];

    private static IEnumerable<string> GetAllLocalizedTypeDisplayNames() =>
        CultureNames
            .Select(CultureInfo.GetCultureInfo)
            .SelectMany(culture => Enum.GetValues<TimberElementType>()
                .Select(type => TimberElementTypeDisplayNameProvider.GetDisplayName(
                    type,
                    culture)));

    private static IEnumerable<string> GetAllLocalizedMaterialReportDisplayNames() =>
        CultureNames
            .Select(CultureInfo.GetCultureInfo)
            .SelectMany(culture => TimberMaterialCatalog.Items
                .Select(item => ReportMaterialDisplayFormatter.FormatMaterialForReport(
                    TimberMaterialDisplayNameProvider.GetDisplayName(
                        item.StoredValue,
                        culture))));
}
