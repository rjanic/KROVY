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
        "Command_FlipSlope_PostPerpendicular",
        "Command_FlipSlope_ResultReversed", "Command_FlipSlope_ResultNormal", "Command_Inspect_Prompt",
        "Command_Inspect_NoData", "Command_Inspect_AllowanceDefault", "Command_Inspect_AllowanceIndividual",
        "Command_Inspect_SummaryFormat", "Command_Inspect_FootprintSummaryFormat",
        "Dialog_Inspect_Item", "Dialog_Inspect_ElementType",
        "Dialog_Inspect_Material", "Dialog_Inspect_Width", "Dialog_Inspect_Height", "Dialog_Inspect_Slope",
        "Dialog_Inspect_SlopeDirection", "Dialog_Inspect_PlanLength", "Dialog_Inspect_ActualLength",
        "Dialog_Inspect_CuttingAllowance", "Dialog_Inspect_CuttingLength", "Dialog_Inspect_ManualLengthMode",
        "Dialog_Inspect_CadHandle", "Dialog_Inspect_ManualLength", "Message_Yes", "Message_No",
        "Message_DirectionNormal", "Message_DirectionReversed", "Command_Report_PromptSelection",
        "Command_Report_NoneFound", "Command_Report_ElementSkippedFormat", "Command_Report_NoValidElements",
        "Command_Report_PromptInsertionPoint", "Command_Report_InsertedFormat", "Command_Recalc_ElementErrorFormat",
        "Command_Recalc_ResultFormat", "Command_Renumber_ConfirmPrompt", "Command_Renumber_NoElements",
        "Command_Renumber_ResultFormat", "Command_Renumber_FailedFormat",
        "Command_Assign_Prompt", "Command_Assign_PromptTypeFormat",
        "Command_Assign_ResultFormat", "Command_PostFootprint_EdgePrompt",
        "Command_PostFootprint_PolylineOnly", "Command_PostFootprint_InvalidGeometry",
        "Command_PostFootprint_AmbiguousPick", "Command_PostFootprint_PickTooFar",
        "Command_PostFootprint_AssignRedirect",
        "Command_PostFootprint_AssignedFormat", "Command_Layers_ElementSkippedFormat", "Command_Layers_ResultFormat",
        "Command_Settings_ApplyElementSkippedFormat", "Command_Settings_ApplyResultFormat", "Command_Labels_Shown",
        "Command_Labels_Hidden", "Command_Labels_LayerMissing", "Command_Prompt_RemoveSelection",
        "Warning_LiveRefreshSkippedFormat", "Dialog_Edit_FieldWidth", "Dialog_Edit_FieldHeight",
        "Dialog_Edit_FieldCuttingAllowance", "Dialog_Edit_FieldManualLength", "Dialog_Edit_WholeNonnegativeFormat",
        "Dialog_Edit_PositiveNumberFormat", "Dialog_Layers_ErrorFormat", "Dialog_Layers_DuplicateFormat",
        "Dialog_Settings_RoundingStepFormat", "Dialog_Settings_CuttingAllowanceFormat", "Error_LayerName_Empty",
        "Error_LayerName_TooLong", "Error_LayerName_InvalidCharacter",
    ];
    private static readonly string[] WpfUiResourceKeys =
    [
        "EditWindow_Title", "EditWindow_Heading", "EditWindow_ElementType", "EditWindow_WidthMm",
        "EditWindow_HeightMm", "EditWindow_SlopeDegrees", "EditWindow_SlopeDirection", "EditWindow_RoofPlane",
        "EditWindow_CuttingAllowanceMm", "EditWindow_UseDefaultByType", "EditWindow_LengthMode",
        "EditWindow_ManualLengthMm", "EditWindow_Material", "EditWindow_ChangeTooltip", "EditWindow_Cancel",
        "EditWindow_Apply", "EditWindow_SlopeDirectionNormal", "EditWindow_SlopeDirectionReversed",
        "EditWindow_SlopeDirectionMixedTooltip", "EditWindow_CuttingAllowanceMixedTooltip",
        "EditWindow_DefaultAllowanceTooltip", "SettingsWindow_Title", "SettingsWindow_Heading",
        "SettingsWindow_Description", "SettingsWindow_Layers_Tab", "SettingsWindow_Layers_ElementTypeColumn",
        "SettingsWindow_Layers_LayerNameColumn", "SettingsWindow_Layers_LayerColorColumn",
        "SettingsWindow_Manufacturing_Tab", "SettingsWindow_Manufacturing_Description",
        "SettingsWindow_Manufacturing_RoundingStep", "SettingsWindow_Manufacturing_ElementTypeColumn",
        "SettingsWindow_Manufacturing_DefaultAllowanceColumn", "SettingsWindow_Language_Tab",
        "SettingsWindow_Language_Description", "SettingsWindow_Language_Label", "SettingsWindow_RestoreDefaults",
        "SettingsWindow_Cancel", "SettingsWindow_Save", "SettingsWindow_SaveNewElementsOnly", "SettingsWindow_SaveApplySelection",
        "SettingsWindow_SaveApplyAll", "LayerColor_Red", "LayerColor_Yellow", "LayerColor_Green",
        "LayerColor_Cyan", "LayerColor_Blue", "LayerColor_Magenta", "LayerColor_Orange", "LayerColor_Gray",
        "LayerColor_LightGray", "InspectWindow_Title", "InspectWindow_Heading", "InspectWindow_Close",
    ];
    private static readonly string[] RibbonToolbarResourceKeys =
    [
        "Ribbon_Tab_Title", "Ribbon_Panel_Elements", "Ribbon_Panel_Data", "Ribbon_Panel_Reports",
        "Ribbon_Panel_Settings", "Ribbon_Panel_Labels", "Ribbon_Panel_Toolbar", "Toolbar_Title",
        "Toolbar_ContentTitle", "CommandUi_Rafter_Label", "CommandUi_Rafter_Tooltip",
        "CommandUi_WallPlate_Label", "CommandUi_WallPlate_Tooltip", "CommandUi_Purlin_Label",
        "CommandUi_Purlin_Tooltip", "CommandUi_Post_Label", "CommandUi_Post_Tooltip",
        "CommandUi_CollarTie_Label", "CommandUi_CollarTie_Tooltip", "CommandUi_Brace_Label",
        "CommandUi_Brace_Tooltip", "CommandUi_TieBeam_Label", "CommandUi_TieBeam_Tooltip",
        "CommandUi_Assign_Label", "CommandUi_Assign_Tooltip", "CommandUi_Edit_Label",
        "CommandUi_Edit_Tooltip", "CommandUi_Inspect_Label", "CommandUi_Inspect_Tooltip",
        "CommandUi_Recalc_Label", "CommandUi_Recalc_Tooltip", "CommandUi_Renumber_Label",
        "CommandUi_Renumber_Tooltip", "CommandUi_Report_Label",
        "CommandUi_Report_Tooltip", "CommandUi_ReportAll_Label", "CommandUi_ReportAll_Tooltip",
        "CommandUi_Settings_Label", "CommandUi_Settings_Tooltip", "CommandUi_Labels_Label",
        "CommandUi_Labels_Tooltip", "CommandUi_Toolbar_Label", "CommandUi_Toolbar_Tooltip",
    ];
    private static readonly string[] AdapterGuardResourceKeys =
    [
        "Error_NoActiveDrawing", "Error_UnsupportedTimberGeometry", "Error_LabelUnsupportedEntityType",
        "Error_XDataTooLargeFormat", "Error_SlopeAnnotationUnsupportedEntityType",
        "Error_UnsupportedSlopeGlyph", "Error_InvalidElementLayerFormat", "Error_InvalidAnnotationLayerFormat",
        "Error_InvalidSlopeDegrees", "Error_Renumber_EntityUnavailable",
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
        Assert.Equal(expectedDisplayName, TimberElementTypeDisplayNameProvider.GetDisplayName(
            type,
            CultureInfo.GetCultureInfo("sk-SK")));
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
        Assert.Equal(expectedDisplayName, LengthCalculationModeDisplayNameProvider.GetDisplayName(
            mode,
            CultureInfo.GetCultureInfo("sk-SK")));
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
            Assert.Equal(TimberElementDataSchema.LegacyImplicitVersion, data.SchemaVersion);
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
        InCulture("sk-SK", () =>
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
            return true;
        });
    }

    [Fact]
    public void FrenchUiCulture_UsesFrenchNeutralSatelliteResource()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var result = new
        {
            ElementType = TimberElementTypeDisplayNameProvider.GetDisplayName(TimberElementType.Rafter, culture),
            LengthMode = LengthCalculationModeDisplayNameProvider.GetDisplayName(LengthCalculationMode.SlopeCorrected, culture),
            ReportTitle = UiStrings.GetString("Report_Title", culture),
            ReportTotalFormat = UiStrings.GetString("Report_TotalFormat", culture),
            AssignResult = UiStrings.Format(UiStrings.GetString("Command_Assign_ResultFormat", culture), 3, 1),
        };

        Assert.Equal("Chevron", result.ElementType);
        Assert.Equal("Recalculer selon la pente", result.LengthMode);
        Assert.Equal("ACAD KROVY – liste de débit des bois", result.ReportTitle);
        Assert.Equal("Total : {0} éléments", result.ReportTotalFormat);
        Assert.Equal("\nACAD KROVY : données attribuées à 3 éléments. Ignorés : 1.", result.AssignResult);
    }

    [Fact]
    public void CommandAndMessageResourceKeys_AllExist()
    {
        Assert.Equal(93, CommandAndMessageResourceKeys.Length);
        Assert.All(CommandAndMessageResourceKeys, key =>
        {
            var value = UiStrings.GetString(key, CultureInfo.GetCultureInfo("sk-SK"));
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
        });
    }

    [Fact]
    public void WpfUiResourceKeys_ExistForAllSupportedCultures()
    {
        Assert.Equal(54, WpfUiResourceKeys.Length);

        foreach (var key in WpfUiResourceKeys)
        {
            Assert.All(CultureNames, cultureName =>
            {
                var localized = UiStrings.GetString(key, CultureInfo.GetCultureInfo(cultureName));
                Assert.False(string.IsNullOrWhiteSpace(localized));
                Assert.NotEqual(key, localized);
            });
        }
    }

    [Fact]
    public void UiStringBindingSource_RefreshesIndexerAndUsesSelectedCulture()
    {
        var source = new UiStringBindingSource();
        var changedProperties = new List<string?>();
        source.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        source.Culture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal("Données des éléments sélectionnés", source["EditWindow_Heading"]);
        Assert.Contains("Item[]", changedProperties);
    }

    [Theory]
    [InlineData(false, "Normal (début → fin)")]
    [InlineData(true, "Inversé (fin → début)")]
    public void SlopeDirectionProvider_LocalizesDisplayWithoutChangingTechnicalValue(
        bool isReversed,
        string expectedDisplay)
    {
        var originalValue = isReversed;

        var display = SlopeDirectionDisplayNameProvider.GetDisplayName(
            isReversed,
            CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(expectedDisplay, display);
        Assert.Equal(originalValue, isReversed);
    }

    [Fact]
    public void LayerColorProvider_LocalizesNamesWithoutChangingColorIndexes()
    {
        var colorIndexes = new[] { 1, 2, 3, 4, 5, 6, 30, 8, 9 };
        var expectedByCulture = new Dictionary<string, string[]>
        {
            ["sk-SK"] = ["Červená (1)", "Žltá (2)", "Zelená (3)", "Azúrová (4)", "Modrá (5)", "Purpurová (6)", "Oranžová (30)", "Sivá (8)", "Svetlosivá (9)"],
            ["cs-CZ"] = ["Červená (1)", "Žlutá (2)", "Zelená (3)", "Azurová (4)", "Modrá (5)", "Purpurová (6)", "Oranžová (30)", "Šedá (8)", "Světle šedá (9)"],
            ["en-US"] = ["Red (1)", "Yellow (2)", "Green (3)", "Cyan (4)", "Blue (5)", "Magenta (6)", "Orange (30)", "Gray (8)", "Light gray (9)"],
            ["de-DE"] = ["Rot (1)", "Gelb (2)", "Grün (3)", "Cyan (4)", "Blau (5)", "Magenta (6)", "Orange (30)", "Grau (8)", "Hellgrau (9)"],
            ["pl-PL"] = ["Czerwony (1)", "Żółty (2)", "Zielony (3)", "Cyjan (4)", "Niebieski (5)", "Magenta (6)", "Pomarańczowy (30)", "Szary (8)", "Jasnoszary (9)"],
            ["fr-FR"] = ["Rouge (1)", "Jaune (2)", "Vert (3)", "Cyan (4)", "Bleu (5)", "Magenta (6)", "Orange (30)", "Gris (8)", "Gris clair (9)"],
        };

        Assert.All(CultureNames, cultureName => Assert.Equal(
            expectedByCulture[cultureName],
            colorIndexes
                .Select(index => LayerColorDisplayNameProvider.GetDisplayName(
                    index,
                    CultureInfo.GetCultureInfo(cultureName)))
                .ToArray()));
        Assert.Equal([1, 2, 3, 4, 5, 6, 30, 8, 9], colorIndexes);
    }

    [Fact]
    public void RibbonToolbarResourceKeys_ExistForAllSupportedCultures()
    {
        Assert.Equal(43, RibbonToolbarResourceKeys.Length);

        foreach (var key in RibbonToolbarResourceKeys)
        {
            Assert.All(CultureNames, cultureName =>
            {
                var localized = UiStrings.GetString(key, CultureInfo.GetCultureInfo(cultureName));
                Assert.False(string.IsNullOrWhiteSpace(localized));
                Assert.NotEqual(key, localized);
            });
        }
    }

    [Fact]
    public void AllTechnicalCommandNames_RemainStableAndLanguageNeutral()
    {
        var expected = new[]
        {
            "AK_HELP", "AK_RIBBON", "AK_TOOLBAR", "AK_TOOLBARSHOW", "AK_TOOLBARHIDE", "AK_SETTINGS",
            "AK_APPLYLAYERS", "AK_LABELS", "AK_LABELSELECTED", "AK_LABELSHOW", "AK_LABELHIDE",
            "AK_ASSIGN", "AK_KROKVA", "AK_POMURNICA", "AK_VAZNICA", "AK_STLPIK", "AK_KLIESTINA",
            "AK_VZPERA", "AK_VAZNYTRAM", "AK_EDIT", "AK_FLIPSLOPE", "AK_INSPECT", "AK_REPORT",
            "AK_REPORTALL", "AK_RECALC", "AK_RENUMBER",
        };

        Assert.Equal(26, AcKrovyCommandNames.All.Count);
        Assert.Equal(expected, AcKrovyCommandNames.All);
        Assert.All(AcKrovyCommandNames.All, command => Assert.StartsWith("AK_", command, StringComparison.Ordinal));
    }

    [Fact]
    public void CommandUiCatalog_LocalizesDisplayAndPreservesCommandsControlIdsAndIcons()
    {
        var expected = new[]
        {
            ("AK_KROKVA", "DECORAIR_AK_RAFTER", "rafter", "Krokva"),
            ("AK_POMURNICA", "DECORAIR_AK_WALLPLATE", "wallplate", "Pomúrnica"),
            ("AK_VAZNICA", "DECORAIR_AK_PURLIN", "purlin", "Väznica"),
            ("AK_STLPIK", "DECORAIR_AK_POST", "post", "Stĺpik"),
            ("AK_KLIESTINA", "DECORAIR_AK_COLLARTIE", "collartie", "Klieština"),
            ("AK_VZPERA", "DECORAIR_AK_BRACE", "brace", "Vzpera"),
            ("AK_VAZNYTRAM", "DECORAIR_AK_TIEBEAM", "tiebeam", "Väzný trám"),
            ("AK_ASSIGN", "DECORAIR_AK_ASSIGN", "assign", "Priradiť údaje"),
            ("AK_EDIT", "DECORAIR_AK_EDIT", "edit", "Upraviť"),
            ("AK_INSPECT", "DECORAIR_AK_INSPECT", "inspect", "Skontrolovať"),
            ("AK_RECALC", "DECORAIR_AK_RECALC", "recalc", "Prepočítať"),
            ("AK_RENUMBER", "DECORAIR_AK_RENUMBER", "recalc", "Prečíslovať"),
            ("AK_REPORT", "DECORAIR_AK_REPORT", "report_selection", "Výkaz z výberu"),
            ("AK_REPORTALL", "DECORAIR_AK_REPORTALL", "report_all", "Výkaz všetkého"),
            ("AK_SETTINGS", "DECORAIR_AK_SETTINGS", "settings", "Nastavenia"),
            ("AK_LABELS", "DECORAIR_AK_LABELS", "labels", "Obnoviť popisy"),
            ("AK_TOOLBAR", "DECORAIR_AK_TOOLBAR", "toolbar", "Klasický panel"),
        };

        Assert.Equal("DECORAIR_ACAD_KROVY_TAB", CommandUiCatalog.RibbonTabId);
        Assert.Equal("AE3310A6-6077-4FB3-B9BE-D4A1DCC866C4", CommandUiCatalog.ClassicToolbarPaletteId);
        Assert.Equal(expected.Length, CommandUiCatalog.RibbonCommands.Count);

        for (var index = 0; index < expected.Length; index++)
        {
            var descriptor = CommandUiCatalog.RibbonCommands[index];
            var item = expected[index];
            Assert.Equal(item.Item1, descriptor.CommandName);
            Assert.Equal(item.Item2, descriptor.RibbonControlId);
            Assert.Equal(item.Item3, descriptor.IconKey);
            Assert.Equal(item.Item4, descriptor.GetLabel(CultureInfo.GetCultureInfo("sk-SK")));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.GetToolTip(CultureInfo.GetCultureInfo("sk-SK"))));
        }

        Assert.Equal(16, CommandUiCatalog.ClassicToolbarCommands.Count);
        Assert.DoesNotContain(CommandUiCatalog.Toolbar, CommandUiCatalog.ClassicToolbarCommands);
    }

    [Fact]
    public void ToolbarCommandMacros_RemainStableForAllCommands()
    {
        Assert.All(AcKrovyCommandNames.All, command =>
            Assert.Equal(command + " ", CommandMacroBuilder.Build(command)));
    }

    [Fact]
    public void AdapterGuardResourceKeys_ExistForAllSupportedCultures()
    {
        Assert.Equal(10, AdapterGuardResourceKeys.Length);

        foreach (var key in AdapterGuardResourceKeys)
        {
            Assert.All(CultureNames, cultureName =>
            {
                var localized = UiStrings.GetString(key, CultureInfo.GetCultureInfo(cultureName));
                Assert.False(string.IsNullOrWhiteSpace(localized));
                Assert.NotEqual(key, localized);
            });
        }
    }

    [Fact]
    public void AdapterGuardResourceFormats_AcceptExpectedTechnicalArguments()
    {
        var formats = new (string Format, object?[] Arguments)[]
        {
            (UiStrings.ErrorXDataTooLargeFormat, [4096]),
            (UiStrings.ErrorInvalidElementLayerFormat, [TimberElementType.Rafter, "chyba vrstvy"]),
            (UiStrings.ErrorInvalidAnnotationLayerFormat, ["chyba vrstvy"]),
        };

        Assert.All(formats, item =>
        {
            var formatted = UiStrings.Format(item.Format, item.Arguments);
            Assert.False(string.IsNullOrWhiteSpace(formatted));
            Assert.DoesNotContain("{0", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain("{1", formatted, StringComparison.Ordinal);
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

        Assert.All(CultureNames, cultureName =>
        {
            var help = UiStrings.GetString("Help_CommandOverview", CultureInfo.GetCultureInfo(cultureName));
            Assert.All(expectedCommandNames, commandName => Assert.Contains(commandName, help));
        });
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
            (UiStrings.CommandRenumberResultFormat, [5, 3, 2, 4]),
            (UiStrings.CommandRenumberFailedFormat, ["chyba"]),
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
