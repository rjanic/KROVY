using System.Globalization;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ProductivityLocalizationTests
{
    private static readonly string[] CultureNames =
        ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];

    private static readonly string[] NewResourceKeys =
    [
        "Command_UnexpectedFailure",
        "SelectSimilarWindow_Title",
        "SelectSimilarWindow_ElementType",
        "SelectSimilarWindow_CuttingLength",
        "Command_SelectSimilar_PromptSeed",
        "Command_SelectSimilar_ResultFormat",
        "CsvExportWindow_Title",
        "CsvExportWindow_PickFirstFormat",
        "Command_ExportCsv_ResultFormat",
        "Csv_Header_ElementId",
        "Csv_Header_CuttingLengthMm",
        "Csv_Header_VolumeM3",
        "DiagnosticsWindow_Title",
        "DiagnosticsWindow_SettingsStates",
        "DiagnosticsWindow_CopySummary",
        "DiagnosticsWindow_StateMemoryOnly",
        "DiagnosticsEvent_SubjectApplicationLanguage",
        "DiagnosticsEvent_SubjectSettingsUiPreferences",
        "DiagnosticsEvent_SubjectLayerProfile",
        "DiagnosticsEvent_SubjectTimberDefaults",
        "DiagnosticsEvent_SubjectCustomElementDefinitions",
        "DiagnosticsEvent_ActionSaved",
        "Help_ProductivityCommands",
        "AkLabelResetAllConfirm_Confirm",
        "AkLabelResetAllProgress_Title",
        "AkLabelResetAllProgress_Status",
        "AkLabelResetAllProgress_CountFormat",
        "AkLabelResetAllProgress_ProcessedFormat",
        "AkLabelResetAllProgress_ElapsedFormat",
        "AkLabelResetAllProgress_EtaFormat",
    ];

    [Fact]
    public void ProductivityResources_ResolveInAllSixLanguagesWithoutRawKeys()
    {
        foreach (var cultureName in CultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var key in NewResourceKeys)
            {
                var value = UiStrings.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.NotEqual(key, value);
            }
        }
    }

    [Fact]
    public void AkLabelResetAllConfirmResources_ExistWithValidPlaceholdersInAllLanguages()
    {
        var confirmKeys = new[]
        {
            "Command_Labels_ResetAllTitle",
            "Command_Labels_ResetAllWarning",
            "AkLabelResetAllConfirm_Confirm",
            "Common_Cancel",
            "AkLabelResetAllProgress_Title",
            "AkLabelResetAllProgress_Status",
            "AkLabelResetAllProgress_CountFormat",
            "AkLabelResetAllProgress_ProcessedFormat",
            "AkLabelResetAllProgress_ElapsedFormat",
            "AkLabelResetAllProgress_EtaFormat",
        };

        string? skTitle = null;
        foreach (var cultureName in CultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var key in confirmKeys)
            {
                var value = UiStrings.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{cultureName}:{key}");
                Assert.NotEqual(key, value);
                Assert.DoesNotContain('\0', value);
            }

            var countFormat = UiStrings.GetString("AkLabelResetAllProgress_CountFormat", culture);
            var processedFormat = UiStrings.GetString("AkLabelResetAllProgress_ProcessedFormat", culture);
            var elapsedFormat = UiStrings.GetString("AkLabelResetAllProgress_ElapsedFormat", culture);
            var etaFormat = UiStrings.GetString("AkLabelResetAllProgress_EtaFormat", culture);

            Assert.Equal(
                "1 / 2",
                string.Format(culture, countFormat, 1, 2));
            Assert.Contains(
                "3",
                string.Format(culture, processedFormat, 3),
                StringComparison.Ordinal);
            Assert.Contains(
                "00:01",
                string.Format(culture, elapsedFormat, "00:01"),
                StringComparison.Ordinal);
            Assert.Contains(
                "00:02",
                string.Format(culture, etaFormat, "00:02"),
                StringComparison.Ordinal);

            var title = UiStrings.GetString("Command_Labels_ResetAllTitle", culture);
            if (cultureName.StartsWith("sk", StringComparison.OrdinalIgnoreCase))
            {
                skTitle = title;
            }
            else if (cultureName.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("Alle Beschriftungen zurücksetzen", title);
                Assert.Equal("Alle zurücksetzen", UiStrings.GetString("AkLabelResetAllConfirm_Confirm", culture));
                Assert.Equal("Abbrechen", UiStrings.GetString("Common_Cancel", culture));
                Assert.NotEqual(skTitle, title);
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(skTitle));
    }

    [Fact]
    public void CsvLocalizationProvider_UsesLocalizedHeadersAndTypedProviders()
    {
        foreach (var cultureName in CultureNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var localization = TimberCsvLocalizationProvider.Create(culture);

            Assert.Equal(
                UiStrings.GetString("Csv_Header_ElementId", culture),
                localization.Headers.ElementId);
            Assert.Equal(
                UiStrings.GetString("ElementType_Rafter", culture),
                localization.ElementTypeDisplay(
                    AcKrovy.Core.Models.TimberElementType.Rafter,
                    null));
            Assert.NotEqual(
                "Smrek C24",
                localization.MaterialDisplay("Smrek C24"));
        }
    }
}
