using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class RuntimeLocalizationCultureTests
{
    [Theory]
    [InlineData("sk", "Krokva", "Položka", "ACAD KROVY – výkaz reziva", "Väzný trám")]
    [InlineData("cs", "Krokev", "Položka", "ACAD KROVY – výkaz řeziva", "Vazný trám")]
    [InlineData("en", "Rafter", "Item", "ACAD KROVY – timber schedule", "Tie beam")]
    [InlineData("de", "Sparren", "Position", "ACAD KROVY – Holzliste", "Bundbalken")]
    [InlineData("pl", "Krokiew", "Pozycja", "ACAD KROVY – zestawienie drewna", "Belka wiązarowa")]
    [InlineData("fr", "Chevron", "Repère", "ACAD KROVY – liste de débit des bois", "Entrait")]
    public void ActiveAppLanguage_WinsOverDifferentCommandThreadCulture(
        string languageCode,
        string expectedRafter,
        string expectedItemHeader,
        string expectedReportTitle,
        string expectedTieBeam)
    {
        var previousLanguage = AppLanguageService.CurrentLanguageCode;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var technicalCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = technicalCulture;
            AppLanguageService.Apply(languageCode);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(
                string.Equals(languageCode, "en", StringComparison.Ordinal) ? "sk-SK" : "en-US");

            var activeCulture = AppLanguageService.CurrentUiCulture;

            Assert.Equal(expectedRafter, TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter));
            Assert.Equal(expectedTieBeam, TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.TieBeam));
            Assert.Equal(expectedReportTitle, UiStrings.ReportTitle);
            Assert.Equal(expectedItemHeader, UiStrings.ReportColumnItem);
            Assert.Equal(technicalCulture.Name, CultureInfo.CurrentCulture.Name);

            var reportKeys = new[]
            {
                "Report_Column_Item",
                "Report_Column_Type",
                "Report_Column_Material",
                "Report_Column_WidthMm",
                "Report_Column_HeightMm",
                "Report_Column_PieceLengthM",
                "Report_Column_Count",
                "Report_Column_TotalLengthM",
                "Report_Column_VolumeM3",
                "Report_TotalFormat",
            };
            var runtimeReportValues = new[]
            {
                UiStrings.ReportColumnItem,
                UiStrings.ReportColumnType,
                UiStrings.ReportColumnMaterial,
                UiStrings.ReportColumnWidthMm,
                UiStrings.ReportColumnHeightMm,
                UiStrings.ReportColumnPieceLengthM,
                UiStrings.ReportColumnCount,
                UiStrings.ReportColumnTotalLengthM,
                UiStrings.ReportColumnVolumeM3,
                UiStrings.ReportTotalFormat,
            };
            Assert.Equal(
                reportKeys.Select(key => UiStrings.GetString(key, activeCulture)),
                runtimeReportValues);

            var commandPrompt = UiStrings.Format(
                UiStrings.CommandAssignPromptTypeFormat,
                TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.TieBeam));
            var expectedPrompt = UiStrings.Format(
                UiStrings.GetString("Command_Assign_PromptTypeFormat", activeCulture),
                TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.TieBeam, activeCulture));
            Assert.Equal(expectedPrompt, commandPrompt);
            Assert.Contains(expectedTieBeam, commandPrompt, StringComparison.Ordinal);
            Assert.Equal(
                UiStrings.Format(
                    UiStrings.GetString("Command_Assign_ResultFormat", activeCulture),
                    1,
                    0),
                UiStrings.Format(UiStrings.CommandAssignResultFormat, 1, 0));
        }
        finally
        {
            AppLanguageService.Apply(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }
}
