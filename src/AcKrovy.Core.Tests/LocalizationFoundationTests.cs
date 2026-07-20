using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class LocalizationCultureCollection
{
    public const string CollectionName = "Localization culture tests";
}

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class LocalizationFoundationTests
{
    private static readonly string[] CultureNames = ["sk-SK", "cs-CZ", "en-US", "de-DE", "pl-PL", "fr-FR"];
    private static readonly string[] CommandAndMessageResourceKeys =
    [
        "Message_DialogTitle", "Message_PluginLoaded", "Help_CommandOverview",
        "Command_Ribbon_Ready", "Command_Ribbon_Pending", "Command_Toolbar_Shown", "Command_Toolbar_Hidden",
        "Command_Settings_SaveFailedFormat", "Command_Settings_Saved", "Command_Settings_PromptApplyAllowances",
        "Command_Settings_SelectionCancelled", "Command_Labels_PromptSelected", "Command_Labels_UpdatedFormat",
        "Command_Labels_RefreshFailedFormat", "Command_Edit_Prompt", "Command_Edit_NoData",
        "Command_Edit_TitleSingleFormat", "Command_Edit_TitleMultipleFormat", "Command_Edit_ResultFormat",
        "Command_FlipSlope_Prompt", "Command_FlipSlope_NotTimberOrAnnotation", "Command_FlipSlope_Horizontal",
        "Command_FlipSlope_ResultReversed", "Command_FlipSlope_ResultNormal", "Command_Inspect_Prompt",
        "Command_Inspect_NoData", "Command_Inspect_AllowanceDefault", "Command_Inspect_AllowanceIndividual",
        "Command_Inspect_SummaryFormat", "Dialog_Inspect_Item", "Dialog_Inspect_ElementType",
        "Dialog_Inspect_Material", "Dialog_Inspect_Width", "Dialog_Inspect_Height", "Dialog_Inspect_Slope",
        "Dialog_Inspect_SlopeDirection", "Dialog_Inspect_PlanLength", "Dialog_Inspect_ActualLength",
        "Dialog_Inspect_CuttingAllowance", "Dialog_Inspect_CuttingLength", "Dialog_Inspect_ManualLengthMode",
        "Dialog_Inspect_CadHandle", "Dialog_Inspect_ManualLength", "Message_Yes", "Message_No",
        "Message_DirectionNormal", "Message_DirectionReversed", "Command_Report_PromptSelection",
        "Command_Report_NoneFound", "Command_Report_ElementSkippedFormat", "Command_Report_NoValidElements",
        "Command_Report_PromptInsertionPoint", "Command_Report_InsertedFormat", "Command_Recalc_ElementErrorFormat",
        "Command_Recalc_ResultFormat", "Command_Assign_Prompt", "Command_Assign_PromptTypeFormat",
        "Command_Assign_ResultFormat", "Command_Layers_ElementSkippedFormat", "Command_Layers_ResultFormat",
        "Command_Settings_ApplyElementSkippedFormat", "Command_Settings_ApplyResultFormat", "Command_Labels_Shown",
        "Command_Labels_Hidden", "Command_Labels_LayerMissing", "Command_Prompt_RemoveSelection",
        "Warning_LiveRefreshSkippedFormat", "Dialog_Edit_FieldWidth", "Dialog_Edit_FieldHeight",
        "Dialog_Edit_FieldCuttingAllowance", "Dialog_Edit_FieldManualLength", "Dialog_Edit_WholeNonnegativeFormat",
        "Dialog_Edit_PositiveNumberFormat", "Dialog_Layers_ErrorFormat", "Dialog_Layers_DuplicateFormat",
        "Dialog_Settings_RoundingStepFormat", "Dialog_Settings_CuttingAllowanceFormat", "Error_LayerName_Empty",
        "Error_LayerName_TooLong", "Error_LayerName_InvalidCharacter",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData(TimberElementType.Rafter, "Krokva")]
    [InlineData(TimberElementType.WallPlate, "Pomúrnica")]
    [InlineData(TimberElementType.Purlin, "Väznica")]
    [InlineData(TimberElementType.Post, "Stĺpik")]
    [InlineData(TimberElementType.CollarTie, "Klieština / hambálok")]
    [InlineData(TimberElementType.Brace, "Vzpera")]
    [InlineData(TimberElementType.TieBeam, "Väzný trám")]
    public void ElementTypeProvider_SeparatesDisplayNameFromTechnicalEnum(
        TimberElementType type,
        string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, TimberElementTypeDisplayNameProvider.GetDisplayName(type));
        Assert.NotEqual(expectedDisplayName, type.ToString());
    }

    [Theory]
    [InlineData(LengthCalculationMode.AutoByElementType, "Automaticky podľa typu")]
    [InlineData(LengthCalculationMode.PlanLength, "Pôdorysná dĺžka")]
    [InlineData(LengthCalculationMode.SlopeCorrected, "Prepočítať podľa sklonu")]
    [InlineData(LengthCalculationMode.ManualLength, "Ručne zadaná dĺžka")]
    public void LengthModeProvider_ReturnsResourceDisplayName(
        LengthCalculationMode mode,
        string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, LengthCalculationModeDisplayNameProvider.GetDisplayName(mode));
    }

    [Fact]
    public void MetadataSerialization_IsIdenticalAcrossUiCultures()
    {
        var data = SampleData();

        var serialized = CultureNames
            .Select(culture => InCulture(culture, () => JsonSerializer.Serialize(data, JsonOptions)))
            .ToList();

        Assert.Single(serialized.Distinct(StringComparer.Ordinal));
        Assert.Contains("\"ElementType\":\"Rafter\"", serialized[0]);
        Assert.Contains("\"LengthCalculationMode\":\"SlopeCorrected\"", serialized[0]);
        Assert.DoesNotContain("Krokva", serialized[0], StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityAndSignature_AreIdenticalAcrossUiCultures()
    {
        var data = SampleData();
        var measurement = new TimberElementMeasurement(data, 4000, 4800, 5000, 0.064);

        var results = CultureNames
            .Select(culture => InCulture(culture, () => new
            {
                ElementId = TimberElementIdentityRules.CreateElementId(data.ElementType, 12),
                Signature = TimberElementSignature.FromMeasurement(measurement),
            }))
            .ToList();

        Assert.All(results, result => Assert.Equal("K12", result.ElementId));
        Assert.Single(results.Select(result => result.Signature).Distinct());
    }

    [Fact]
    public void LegacyMetadata_LoadsIdenticallyAcrossUiCultures()
    {
        const string json = """
            {
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 90,
              "HeightMm": 170,
              "SlopeDegrees": 37,
              "RoofPlaneId": "R3",
              "CuttingAllowanceMm": 120,
              "LengthCalculationMode": "SlopeCorrected",
              "Material": "Smrek C24"
            }
            """;

        var loaded = CultureNames
            .Select(culture => InCulture(culture, () =>
            {
                var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);
                return TimberElementDataVersioning.Normalize(Assert.IsType<TimberElementData>(data));
            }))
            .ToList();

        Assert.All(loaded, data =>
        {
            Assert.Equal(TimberElementDataSchema.CurrentVersion, data.SchemaVersion);
            Assert.Equal("K9", data.ElementId);
            Assert.Equal(TimberElementType.Rafter, data.ElementType);
            Assert.Equal(LengthCalculationMode.SlopeCorrected, data.LengthCalculationMode);
        });
        Assert.Single(loaded.Distinct());
    }

    [Fact]
    public void DefaultCadLayerNames_AreIdenticalAcrossUiCultures()
    {
        var layerNames = CultureNames
            .Select(culture => InCulture(culture, () => Enum
                .GetValues<TimberElementType>()
                .Select(type => ElementLayerProfile.CreateDefault().GetStyle(type).LayerName)
                .ToArray()))
            .ToList();

        Assert.All(layerNames, names => Assert.Equal(
            ["KROKVA", "POMURNICA", "VAZNICA", "STLPIK", "KLIESTINA", "VZPERA", "VAZNY_TRAM"],
            names));
    }

    [Fact]
    public void ReportData_IsIdenticalAcrossUiCultures()
    {
        var data = SampleData();
        var measurement = new TimberElementMeasurement(data, 4000, 4800, 5000, 0.064);

        var reports = CultureNames
            .Select(culture => InCulture(culture, () => TimberReportBuilder.Build([measurement])))
            .ToList();

        Assert.All(reports, report =>
        {
            var line = Assert.Single(report.Lines);
            Assert.Equal("K1", line.ElementId);
            Assert.Equal(TimberElementType.Rafter, line.ElementType);
            Assert.Equal("Smrek C24", line.Material);
            Assert.Equal(0.064, report.TotalVolumeM3, 6);
        });
    }

    [Fact]
    public void PilotReportResources_PreserveCurrentSlovakText()
    {
        Assert.Equal("ACAD KROVY – výkaz reziva", UiStrings.ReportTitle);
        Assert.Equal("Položka", UiStrings.ReportColumnItem);
        Assert.Equal("Typ", UiStrings.ReportColumnType);
        Assert.Equal("Materiál", UiStrings.ReportColumnMaterial);
        Assert.Equal("Šírka [mm]", UiStrings.ReportColumnWidthMm);
        Assert.Equal("Výška [mm]", UiStrings.ReportColumnHeightMm);
        Assert.Equal("Dĺžka kusu [m]", UiStrings.ReportColumnPieceLengthM);
        Assert.Equal("Počet", UiStrings.ReportColumnCount);
        Assert.Equal("Celková dĺžka [m]", UiStrings.ReportColumnTotalLengthM);
        Assert.Equal("Kubatúra [m³]", UiStrings.ReportColumnVolumeM3);
        Assert.Equal("Spolu: {0} prvkov", UiStrings.ReportTotalFormat);
    }

    [Fact]
    public void FrenchUiCulture_UsesDefaultSlovakResourceFallback()
    {
        var result = InCulture("fr-FR", () => new
        {
            ElementType = TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter),
            LengthMode = LengthCalculationModeDisplayNameProvider.GetDisplayName(LengthCalculationMode.SlopeCorrected),
            ReportTitle = UiStrings.ReportTitle,
            ReportTotalFormat = UiStrings.ReportTotalFormat,
            AssignResult = UiStrings.Format(UiStrings.CommandAssignResultFormat, 3, 1),
        });

        Assert.Equal("Krokva", result.ElementType);
        Assert.Equal("Prepočítať podľa sklonu", result.LengthMode);
        Assert.Equal("ACAD KROVY – výkaz reziva", result.ReportTitle);
        Assert.Equal("Spolu: {0} prvkov", result.ReportTotalFormat);
        Assert.Equal("\nACAD KROVY: priradené údaje k 3 prvkom. Preskočené: 1.", result.AssignResult);
    }

    [Fact]
    public void CommandAndMessageResourceKeys_AllExist()
    {
        Assert.Equal(80, CommandAndMessageResourceKeys.Length);
        Assert.All(CommandAndMessageResourceKeys, key =>
        {
            var value = UiStrings.GetString(key, CultureInfo.GetCultureInfo("sk-SK"));
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
        });
    }

    [Fact]
    public void HelpResource_PreservesStableTechnicalCommandNames()
    {
        var expectedCommandNames = new[]
        {
            "AK_KROKVA", "AK_POMURNICA", "AK_VAZNICA", "AK_STLPIK", "AK_KLIESTINA", "AK_VZPERA",
            "AK_VAZNYTRAM", "AK_ASSIGN", "AK_EDIT", "AK_FLIPSLOPE", "AK_INSPECT", "AK_REPORT",
            "AK_REPORTALL", "AK_RECALC", "AK_RIBBON", "AK_TOOLBAR", "AK_SETTINGS", "AK_APPLYLAYERS",
            "AK_LABELS", "AK_LABELSELECTED", "AK_LABELSHOW", "AK_LABELHIDE",
        };

        Assert.All(expectedCommandNames, commandName => Assert.Contains(commandName, UiStrings.HelpCommandOverview));
    }

    [Fact]
    public void CommandAndMessageFormats_AcceptExpectedArguments()
    {
        var formats = new (string Format, object?[] Arguments)[]
        {
            (UiStrings.CommandSettingsSaveFailedFormat, ["chyba"]),
            (UiStrings.CommandLabelsUpdatedFormat, [1, 2, 3]),
            (UiStrings.CommandLabelsRefreshFailedFormat, ["K1", "chyba"]),
            (UiStrings.CommandEditTitleSingleFormat, ["K1", "Krokva"]),
            (UiStrings.CommandEditTitleMultipleFormat, [5]),
            (UiStrings.CommandEditResultFormat, [4, 1]),
            (UiStrings.CommandInspectSummaryFormat, ["K1", "Krokva", 80d, 160d, 4d, 4.5d, 4.7d, 0.1d]),
            (UiStrings.CommandReportElementSkippedFormat, ["K1", "chyba"]),
            (UiStrings.CommandReportInsertedFormat, [5, 1, 0.1234d]),
            (UiStrings.CommandRecalcElementErrorFormat, ["K1", "chyba"]),
            (UiStrings.CommandRecalcResultFormat, [5, 0, 5, 0]),
            (UiStrings.CommandAssignPromptTypeFormat, ["Krokva"]),
            (UiStrings.CommandAssignResultFormat, [5, 0]),
            (UiStrings.CommandLayersElementSkippedFormat, ["chyba"]),
            (UiStrings.CommandLayersResultFormat, [5, 0]),
            (UiStrings.CommandSettingsApplyElementSkippedFormat, ["chyba"]),
            (UiStrings.CommandSettingsApplyResultFormat, [5, 0]),
            (UiStrings.WarningLiveRefreshSkippedFormat, ["chyba"]),
            (UiStrings.DialogEditWholeNonnegativeFormat, ["prídavok", 1000d]),
            (UiStrings.DialogEditPositiveNumberFormat, ["šírka"]),
            (UiStrings.DialogLayersErrorFormat, ["Krokva", "chyba"]),
            (UiStrings.DialogLayersDuplicateFormat, ["KROKVA"]),
            (UiStrings.DialogSettingsRoundingStepFormat, [1000d]),
            (UiStrings.DialogSettingsCuttingAllowanceFormat, ["Krokva", 1000d]),
        };

        Assert.All(formats, item =>
        {
            var formatted = UiStrings.Format(item.Format, item.Arguments);
            Assert.False(string.IsNullOrWhiteSpace(formatted));
            Assert.DoesNotContain("{0", formatted, StringComparison.Ordinal);
        });
    }

    private static TimberElementData SampleData() => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementId = "K1",
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 200,
        LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
        Material = "Smrek C24",
        Note = "poznámka",
    };

    private static T InCulture<T>(string cultureName, Func<T> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
