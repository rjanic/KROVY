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
