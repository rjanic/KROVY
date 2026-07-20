using System.Globalization;
using System.Resources;

namespace AcKrovy.Localization;

public static class UiStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "AcKrovy.Localization.Resources.UiStrings",
        typeof(UiStrings).Assembly);

    public static string ReportTitle => GetString("Report_Title");
    public static string ReportColumnItem => GetString("Report_Column_Item");
    public static string ReportColumnType => GetString("Report_Column_Type");
    public static string ReportColumnMaterial => GetString("Report_Column_Material");
    public static string ReportColumnWidthMm => GetString("Report_Column_WidthMm");
    public static string ReportColumnHeightMm => GetString("Report_Column_HeightMm");
    public static string ReportColumnPieceLengthM => GetString("Report_Column_PieceLengthM");
    public static string ReportColumnCount => GetString("Report_Column_Count");
    public static string ReportColumnTotalLengthM => GetString("Report_Column_TotalLengthM");
    public static string ReportColumnVolumeM3 => GetString("Report_Column_VolumeM3");
    public static string ReportTotalFormat => GetString("Report_TotalFormat");
    public static string MessageDialogTitle => GetString("Message_DialogTitle");
    public static string MessagePluginLoaded => GetString("Message_PluginLoaded");
    public static string HelpCommandOverview => GetString("Help_CommandOverview");
    public static string CommandRibbonReady => GetString("Command_Ribbon_Ready");
    public static string CommandRibbonPending => GetString("Command_Ribbon_Pending");
    public static string CommandToolbarShown => GetString("Command_Toolbar_Shown");
    public static string CommandToolbarHidden => GetString("Command_Toolbar_Hidden");
    public static string CommandSettingsSaveFailedFormat => GetString("Command_Settings_SaveFailedFormat");
    public static string CommandSettingsSaved => GetString("Command_Settings_Saved");
    public static string CommandSettingsPromptApplyAllowances => GetString("Command_Settings_PromptApplyAllowances");
    public static string CommandSettingsSelectionCancelled => GetString("Command_Settings_SelectionCancelled");
    public static string CommandLabelsPromptSelected => GetString("Command_Labels_PromptSelected");
    public static string CommandLabelsUpdatedFormat => GetString("Command_Labels_UpdatedFormat");
    public static string CommandLabelsRefreshFailedFormat => GetString("Command_Labels_RefreshFailedFormat");
    public static string CommandEditPrompt => GetString("Command_Edit_Prompt");
    public static string CommandEditNoData => GetString("Command_Edit_NoData");
    public static string CommandEditTitleSingleFormat => GetString("Command_Edit_TitleSingleFormat");
    public static string CommandEditTitleMultipleFormat => GetString("Command_Edit_TitleMultipleFormat");
    public static string CommandEditResultFormat => GetString("Command_Edit_ResultFormat");
    public static string CommandFlipSlopePrompt => GetString("Command_FlipSlope_Prompt");
    public static string CommandFlipSlopeNotTimberOrAnnotation => GetString("Command_FlipSlope_NotTimberOrAnnotation");
    public static string CommandFlipSlopeHorizontal => GetString("Command_FlipSlope_Horizontal");
    public static string CommandFlipSlopeResultReversed => GetString("Command_FlipSlope_ResultReversed");
    public static string CommandFlipSlopeResultNormal => GetString("Command_FlipSlope_ResultNormal");
    public static string CommandInspectPrompt => GetString("Command_Inspect_Prompt");
    public static string CommandInspectNoData => GetString("Command_Inspect_NoData");
    public static string CommandInspectAllowanceDefault => GetString("Command_Inspect_AllowanceDefault");
    public static string CommandInspectAllowanceIndividual => GetString("Command_Inspect_AllowanceIndividual");
    public static string CommandInspectSummaryFormat => GetString("Command_Inspect_SummaryFormat");
    public static string DialogInspectItem => GetString("Dialog_Inspect_Item");
    public static string DialogInspectElementType => GetString("Dialog_Inspect_ElementType");
    public static string DialogInspectMaterial => GetString("Dialog_Inspect_Material");
    public static string DialogInspectWidth => GetString("Dialog_Inspect_Width");
    public static string DialogInspectHeight => GetString("Dialog_Inspect_Height");
    public static string DialogInspectSlope => GetString("Dialog_Inspect_Slope");
    public static string DialogInspectSlopeDirection => GetString("Dialog_Inspect_SlopeDirection");
    public static string DialogInspectPlanLength => GetString("Dialog_Inspect_PlanLength");
    public static string DialogInspectActualLength => GetString("Dialog_Inspect_ActualLength");
    public static string DialogInspectCuttingAllowance => GetString("Dialog_Inspect_CuttingAllowance");
    public static string DialogInspectCuttingLength => GetString("Dialog_Inspect_CuttingLength");
    public static string DialogInspectManualLengthMode => GetString("Dialog_Inspect_ManualLengthMode");
    public static string DialogInspectCadHandle => GetString("Dialog_Inspect_CadHandle");
    public static string DialogInspectManualLength => GetString("Dialog_Inspect_ManualLength");
    public static string MessageYes => GetString("Message_Yes");
    public static string MessageNo => GetString("Message_No");
    public static string MessageDirectionNormal => GetString("Message_DirectionNormal");
    public static string MessageDirectionReversed => GetString("Message_DirectionReversed");
    public static string CommandReportPromptSelection => GetString("Command_Report_PromptSelection");
    public static string CommandReportNoneFound => GetString("Command_Report_NoneFound");
    public static string CommandReportElementSkippedFormat => GetString("Command_Report_ElementSkippedFormat");
    public static string CommandReportNoValidElements => GetString("Command_Report_NoValidElements");
    public static string CommandReportPromptInsertionPoint => GetString("Command_Report_PromptInsertionPoint");
    public static string CommandReportInsertedFormat => GetString("Command_Report_InsertedFormat");
    public static string CommandRecalcElementErrorFormat => GetString("Command_Recalc_ElementErrorFormat");
    public static string CommandRecalcResultFormat => GetString("Command_Recalc_ResultFormat");
    public static string CommandAssignPrompt => GetString("Command_Assign_Prompt");
    public static string CommandAssignPromptTypeFormat => GetString("Command_Assign_PromptTypeFormat");
    public static string CommandAssignResultFormat => GetString("Command_Assign_ResultFormat");
    public static string CommandLayersElementSkippedFormat => GetString("Command_Layers_ElementSkippedFormat");
    public static string CommandLayersResultFormat => GetString("Command_Layers_ResultFormat");
    public static string CommandSettingsApplyElementSkippedFormat => GetString("Command_Settings_ApplyElementSkippedFormat");
    public static string CommandSettingsApplyResultFormat => GetString("Command_Settings_ApplyResultFormat");
    public static string CommandLabelsShown => GetString("Command_Labels_Shown");
    public static string CommandLabelsHidden => GetString("Command_Labels_Hidden");
    public static string CommandLabelsLayerMissing => GetString("Command_Labels_LayerMissing");
    public static string CommandPromptRemoveSelection => GetString("Command_Prompt_RemoveSelection");
    public static string WarningLiveRefreshSkippedFormat => GetString("Warning_LiveRefreshSkippedFormat");
    public static string DialogEditFieldWidth => GetString("Dialog_Edit_FieldWidth");
    public static string DialogEditFieldHeight => GetString("Dialog_Edit_FieldHeight");
    public static string DialogEditFieldCuttingAllowance => GetString("Dialog_Edit_FieldCuttingAllowance");
    public static string DialogEditFieldManualLength => GetString("Dialog_Edit_FieldManualLength");
    public static string DialogEditWholeNonnegativeFormat => GetString("Dialog_Edit_WholeNonnegativeFormat");
    public static string DialogEditPositiveNumberFormat => GetString("Dialog_Edit_PositiveNumberFormat");
    public static string DialogLayersErrorFormat => GetString("Dialog_Layers_ErrorFormat");
    public static string DialogLayersDuplicateFormat => GetString("Dialog_Layers_DuplicateFormat");
    public static string DialogSettingsRoundingStepFormat => GetString("Dialog_Settings_RoundingStepFormat");
    public static string DialogSettingsCuttingAllowanceFormat => GetString("Dialog_Settings_CuttingAllowanceFormat");
    public static string ErrorLayerNameEmpty => GetString("Error_LayerName_Empty");
    public static string ErrorLayerNameTooLong => GetString("Error_LayerName_TooLong");
    public static string ErrorLayerNameInvalidCharacter => GetString("Error_LayerName_InvalidCharacter");

    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    public static string GetString(string resourceKey, CultureInfo? culture = null) =>
        ResourceManager.GetString(resourceKey, culture ?? CultureInfo.CurrentUICulture) ?? resourceKey;
}
