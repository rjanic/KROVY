using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Infrastructure.IO;
using AcKrovy.Infrastructure.Settings;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// Príkazy ACAD KROVY. Prvky sa dajú označiť pred spustením príkazu
/// (PickFirst), alebo až po jeho spustení.
/// </summary>
public sealed class AcKrovyCommands
{
    [CommandMethod(AcKrovyCommandNames.Help, CommandFlags.Modal)]
    public void Help()
    {
        var editor = ActiveEditor();
        editor.WriteMessage(UiStrings.HelpCommandOverview);
    }

    [CommandMethod(AcKrovyCommandNames.Ribbon, CommandFlags.Modal)]
    public void ShowRibbon()
    {
        if (AcKrovyRibbon.EnsureCreated(activateTab: true))
        {
            ActiveEditor().WriteMessage(UiStrings.CommandRibbonReady);
            return;
        }

        AcKrovyRibbon.ScheduleCreation();
        ActiveEditor().WriteMessage(UiStrings.CommandRibbonPending);
    }

    [CommandMethod(AcKrovyCommandNames.Toolbar, CommandFlags.Modal)]
    public void ToggleClassicToolbar()
    {
        ClassicToolbarManager.Toggle();
        ActiveEditor().WriteMessage(ClassicToolbarManager.IsVisible
            ? UiStrings.CommandToolbarShown
            : UiStrings.CommandToolbarHidden);
    }

    [CommandMethod(AcKrovyCommandNames.ToolbarShow, CommandFlags.Modal)]
    public void ShowClassicToolbar()
    {
        ClassicToolbarManager.Show();
        ActiveEditor().WriteMessage(UiStrings.CommandToolbarShown);
    }

    [CommandMethod(AcKrovyCommandNames.ToolbarHide, CommandFlags.Modal)]
    public void HideClassicToolbar()
    {
        ClassicToolbarManager.Hide();
        ActiveEditor().WriteMessage(UiStrings.CommandToolbarHidden);
    }

    [CommandMethod(AcKrovyCommandNames.Settings, CommandFlags.Modal)]
    public void OpenSettings()
    {
        var document = ActiveDocument();
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var annotationScaleState = ReadAnnotationScaleSettingsState(document);
        LayerSettingsWindow? dialog = null;
        dialog = new LayerSettingsWindow(
            ElementLayerProfileStore.Load(),
            defaultProfile,
            AppLanguageService.CurrentLanguageCode,
            ReadAvailableLinetypeNames(document.Database),
            ReadAvailableLayerPresets(document.Database),
            request => ApplySettingsFromWindow(
                document,
                request,
                new WindowInteropHelper(dialog).Handle),
            annotationScaleState: annotationScaleState);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        AcApp.ShowModalWindow(dialog);
    }

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (System.Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static SettingsApplyResponse ApplySettingsFromWindow(
        Document document,
        SettingsApplyRequest request,
        IntPtr settingsWindowHandle)
    {
        var editor = document.Editor;
        var annotationSettings = request.AnnotationSettings;
        IReadOnlyList<ObjectId>? targetIds = null;
        if (annotationSettings?.ApplyScope ==
            TimberAnnotationSettingsApplyScope.SelectedElements)
        {
            SettingsSelectionResult selection;
            using (editor.StartUserInteraction(settingsWindowHandle))
            {
                selection = PromptForSettingsEntities(
                    editor,
                    UiStrings.GetString(
                        "Command_Settings_PromptApplyAnnotations",
                        AppLanguageService.CurrentUiCulture));
            }

            if (selection.Status != SettingsSelectionStatus.Selected)
            {
                return SettingsResponse(
                    document.Database,
                    selection.Status == SettingsSelectionStatus.Cancelled
                        ? "SettingsWindow_SelectionCancelled"
                        : "SettingsWindow_NoSmartElementsSelected",
                    StatusBannerSeverity.Information,
                    success: false,
                    profileAccepted: false);
            }

            targetIds = FilterSettingsTimberElementIds(
                document.Database,
                selection.Ids);
            if (targetIds.Count == 0)
            {
                return SettingsResponse(
                    document.Database,
                    "SettingsWindow_NoSmartElementsSelected",
                    StatusBannerSeverity.Information,
                    success: false,
                    profileAccepted: false);
            }
        }

        var persistedProfile = ElementLayerProfileStore.Load();
        var applyLayerProfileChange =
            annotationSettings is null &&
            request.LayerProfileChanged &&
            request.SaveMode is not
                (SettingsSaveMode.LanguageOnly or SettingsSaveMode.SelectedElements);
        var appliedProfile = applyLayerProfileChange
            ? request.Profile
            : persistedProfile;
        IReadOnlyList<string> createdLayerNames = [];
        if (applyLayerProfileChange)
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var resolution = TimberLayerService.ResolveNewElementsOnlyProfile(
                    document.Database,
                    transaction,
                    request.Profile,
                    persistedProfile,
                    request.LayerOverrideIntents);
                appliedProfile = resolution.Profile;
                createdLayerNames = resolution.CreatedLayerNames;
                transaction.Commit();
            }
        }

        try
        {
            if (applyLayerProfileChange)
            {
                ElementLayerProfileStore.Save(appliedProfile);
            }
            if (request.DefaultProfileChanged &&
                request.SaveMode != SettingsSaveMode.LanguageOnly)
            {
                TimberElementDefaultProfileStore.Save(request.DefaultProfile);
            }
        }
        catch (System.Exception ex)
        {
            return new SettingsApplyResponse(
                false,
                false,
                StatusBannerSeverity.Warning,
                "Command_Settings_SaveFailedFormat",
                [ex.Message],
                ReadAvailableLinetypeNames(document.Database),
                ReadAvailableLayerPresets(document.Database),
                appliedProfile);
        }

        if (request.SaveMode == SettingsSaveMode.LanguageOnly)
        {
            editor.WriteMessage(UiStrings.CommandSettingsSaved);
            return SettingsResponse(
                document.Database,
                "SettingsWindow_SettingsApplied",
                StatusBannerSeverity.Success);
        }

        if (request.SaveMode == SettingsSaveMode.NewElementsOnly)
        {
            editor.WriteMessage(UiStrings.CommandSettingsSaved);
            return createdLayerNames.Count == 0
                ? SettingsResponse(
                    document.Database,
                    "SettingsWindow_ProfileSavedNewOnly",
                    StatusBannerSeverity.Success,
                    appliedProfile: appliedProfile)
                : SettingsResponse(
                    document.Database,
                    createdLayerNames.Count == 1
                        ? "SettingsWindow_NewLayerCreatedFormat"
                        : "SettingsWindow_NewLayersCreatedFormat",
                    StatusBannerSeverity.Success,
                    [
                        createdLayerNames.Count == 1
                            ? createdLayerNames[0]
                            : createdLayerNames.Count,
                    ],
                    appliedProfile: appliedProfile);
        }

        if (annotationSettings is null)
        {
            return SettingsResponse(
                document.Database,
                "Command_Settings_SaveFailedFormat",
                StatusBannerSeverity.Warning,
                ["Annotation settings request is required for existing-element scopes."],
                success: false,
                profileAccepted: true,
                appliedProfile: appliedProfile);
        }

        SettingsDrawingApplyResult applyResult;
        try
        {
            using (document.LockDocument())
            {
                applyResult = ApplySettingsToExistingElements(
                    document,
                    request.DefaultProfile,
                    targetIds,
                    annotationSettings);
            }
        }
        catch (System.Exception ex)
        {
            return SettingsResponse(
                document.Database,
                "Command_Settings_SaveFailedFormat",
                StatusBannerSeverity.Warning,
                [ex.Message],
                success: false,
                profileAccepted: true,
                appliedProfile: appliedProfile);
        }
        if (createdLayerNames.Count > 0)
        {
            return SettingsResponse(
                document.Database,
                createdLayerNames.Count == 1
                    ? "SettingsWindow_NewLayerCreatedFormat"
                    : "SettingsWindow_NewLayersCreatedFormat",
                StatusBannerSeverity.Success,
                [
                    createdLayerNames.Count == 1
                        ? createdLayerNames[0]
                        : createdLayerNames.Count,
                ],
                appliedProfile: appliedProfile);
        }

        var resultKey = SettingsApplyDispatchRules.GetDrawingResultResourceKey(
            request.SaveMode,
            applyResult.Changed,
            applyResult.Eligible);
        return SettingsResponse(
            document.Database,
            resultKey,
            applyResult.Changed
                ? StatusBannerSeverity.Success
                : StatusBannerSeverity.Information,
            appliedProfile: appliedProfile);
    }

    private static SettingsApplyResponse SettingsResponse(
        Database database,
        string resourceKey,
        StatusBannerSeverity severity,
        object[]? resourceArguments = null,
        bool success = true,
        bool profileAccepted = true,
        ElementLayerProfile? appliedProfile = null) =>
        new(
            success,
            profileAccepted,
            severity,
            resourceKey,
            resourceArguments ?? [],
            ReadAvailableLinetypeNames(database),
            ReadAvailableLayerPresets(database),
            appliedProfile);

    [CommandMethod(AcKrovyCommandNames.Diagnostics, CommandFlags.Modal)]
    public void Diagnostics() =>
        CommandExecutionBoundary.Execute(
            AcKrovyCommandNames.Diagnostics,
            ShowDiagnostics);

    [CommandMethod(
        AcKrovyCommandNames.SelectSimilar,
        CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]
    public void SelectSimilar() =>
        CommandExecutionBoundary.Execute(
            AcKrovyCommandNames.SelectSimilar,
            SelectSimilarCore);

    [CommandMethod(
        AcKrovyCommandNames.ExportCsv,
        CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ExportCsv() =>
        CommandExecutionBoundary.Execute(
            AcKrovyCommandNames.ExportCsv,
            ExportCsvCore);

    [CommandMethod(AcKrovyCommandNames.ApplyLayers, CommandFlags.Modal)]
    public void ApplyLayers()
    {
        ApplyLayersToExistingElements(ActiveDocument(), ElementLayerProfileStore.Load());
    }

    [CommandMethod(AcKrovyCommandNames.Labels, CommandFlags.Modal)]
    public void UpdateAllLabels()
    {
        var document = ActiveDocument();
        var result = ElementLabelService.UpdateAll(document.Database, document.Editor);
        document.Editor.WriteMessage(UiStrings.Format(
            UiStrings.CommandLabelsUpdatedFormat,
            result.Processed,
            result.Created,
            result.Skipped));
    }

    [CommandMethod(AcKrovyCommandNames.LabelSelected, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void UpdateSelectedLabels()
    {
        var document = ActiveDocument();
        var ids = PromptForEntities(document.Editor, UiStrings.CommandLabelsPromptSelected);
        if (ids.Count == 0)
        {
            return;
        }

        var result = ElementLabelService.UpdateSelected(document.Database, document.Editor, ids);
        document.Editor.WriteMessage(UiStrings.Format(
            UiStrings.CommandLabelsUpdatedFormat,
            result.Processed,
            result.Created,
            result.Skipped));
    }

    [CommandMethod(AcKrovyCommandNames.LabelShow, CommandFlags.Modal)]
    public void ShowLabels() => SetLabelsVisibility(true);

    [CommandMethod(AcKrovyCommandNames.LabelHide, CommandFlags.Modal)]
    public void HideLabels() => SetLabelsVisibility(false);

    [CommandMethod(AcKrovyCommandNames.Assign, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Assign() => AssignWithPresetType(null);

    [CommandMethod(AcKrovyCommandNames.Rafter, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignRafter() => AssignWithPresetType(TimberElementType.Rafter);

    [CommandMethod(AcKrovyCommandNames.WallPlate, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignWallPlate() => AssignWithPresetType(TimberElementType.WallPlate);

    [CommandMethod(AcKrovyCommandNames.Purlin, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignPurlin() => AssignWithPresetType(TimberElementType.Purlin);

    [CommandMethod(AcKrovyCommandNames.Post, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignPost() => PostFootprintAssignmentWorkflow.Run(ActiveDocument());

    [CommandMethod(AcKrovyCommandNames.CollarTie, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignCollarTie() => AssignWithPresetType(TimberElementType.CollarTie);

    [CommandMethod(AcKrovyCommandNames.Brace, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignBrace() => AssignWithPresetType(TimberElementType.Brace);

    [CommandMethod(AcKrovyCommandNames.TieBeam, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignTieBeam() => AssignWithPresetType(TimberElementType.TieBeam);

    [CommandMethod(AcKrovyCommandNames.Custom, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignCustom()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var ids = PromptForEntities(
            editor,
            UiStrings.GetString("Command_Custom_PromptSelection", uiCulture));
        if (ids.Count == 0)
        {
            return;
        }

        IReadOnlyList<CustomElementDefinition> definitions;
        try
        {
            definitions = CustomElementDefinitionCatalogRules.Normalize(
                CustomElementDefinitionCatalogStore.Load()
                    .Concat(ReadCustomDefinitionsFromDrawing(document.Database)));
        }
        catch (ArgumentException ex)
        {
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Custom_AmbiguousDefinitionsFormat", uiCulture),
                ex.Message));
            return;
        }
        var definitionDialog = new CustomElementDefinitionWindow(definitions);
        if (AcApp.ShowModalWindow(definitionDialog) != true ||
            definitionDialog.SelectedDefinition is not { } definition)
        {
            return;
        }

        try
        {
            // Also imports self-contained definitions found only in the current
            // DWG, so they become reusable in another drawing on this PC.
            CustomElementDefinitionCatalogStore.Save(definitions.Append(definition));
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Custom_CatalogSaveFailedFormat", uiCulture),
                ex.Message));
            return;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var seedData = CustomElementDefinitionRules.Apply(
            TimberElementDefaults.For(TimberElementType.Custom, defaultProfile),
            definition);
        AssignSelectedElements(document, ids, seedData, defaultProfile);
    }

    private static IReadOnlyList<CustomElementDefinition> ReadCustomDefinitionsFromDrawing(
        Database database)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var definitions = new List<CustomElementDefinition>();
        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is Entity entity &&
                metadataStore.TryRead(entity, out var data) &&
                data is not null &&
                CustomElementDefinitionRules.TryFromElementData(data, out var definition) &&
                definition is not null)
            {
                definitions.Add(definition);
            }
        }

        transaction.Commit();
        return definitions;
    }

    [CommandMethod(AcKrovyCommandNames.Edit, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Edit()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var selection = ResolveEditSelection(
            document.Database,
            editor,
            UiStrings.GetString("Command_Edit_Prompt", uiCulture));
        var ids = selection.Ids;
        if (ids.Count == 0)
        {
            return;
        }

        using var readTransaction = document.Database.TransactionManager.StartTransaction();
        var readMetadataStore = new AutoCadTimberElementMetadataStore(readTransaction);
        var selectedData = ids
            .Select(id => readTransaction.GetObject(id, OpenMode.ForRead) as Entity)
            .Where(entity => entity is not null)
            .Select(entity => readMetadataStore.TryRead(entity!, out var data) ? data : null)
            .Where(data => data is not null)
            .Cast<TimberElementData>()
            .ToList();
        readTransaction.Commit();

        if (selectedData.Count == 0)
        {
            editor.WriteMessage(UiStrings.GetString("Command_Edit_NoData", uiCulture));
            return;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var dialog = new ElementEditWindow(
            selectedData[0],
            isNewAssignment: false,
            defaultProfile,
            cuttingAllowanceIsMixed: HasMixedCuttingAllowance(selectedData),
            slopeDirectionIsMixed: HasMixedSlopeDirection(selectedData),
            validationData: selectedData);
        dialog.CustomDefinitionNameChanged += name =>
        {
            if (selectedData.Count == 1)
            {
                dialog.Title = UiStrings.Format(
                    UiStrings.GetString("Command_Edit_TitleSingleFormat", uiCulture),
                    selectedData[0].ElementId,
                    name);
            }
        };
        dialog.Title = selectedData.Count == 1
            ? UiStrings.Format(
                UiStrings.GetString("Command_Edit_TitleSingleFormat", uiCulture),
                selectedData[0].ElementId,
                TimberElementDisplayNameProvider.GetDisplayName(
                    selectedData[0],
                    uiCulture))
            : UiStrings.Format(
                UiStrings.GetString("Command_Edit_TitleMultipleFormat", uiCulture),
                selectedData.Count);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        if (!TimberElementEditRules.HasRequestedChange(dialog.Patch) &&
            !dialog.UseDefaultCuttingAllowanceByType &&
            dialog.RenamedCustomDefinition is null)
        {
            editor.WriteMessage(UiStrings.GetString(
                "Command_Edit_NoChanges",
                uiCulture));
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction, editor);
        var changed = 0;
        var skipped = selection.RejectedImpliedItems;
        var changedIds = new List<ObjectId>();
        var previousElementIdById = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                !metadataStore.TryRead(entity, out var original) ||
                original is null)
            {
                skipped++;
                continue;
            }

            if (!TimberElementEditRules.TryCreateEffectiveChange(
                    original,
                    dialog.Patch,
                    dialog.UseDefaultCuttingAllowanceByType,
                    defaultProfile,
                    out var merged))
            {
                continue;
            }

            previousElementIdById.TryAdd(id, original.ElementId);
            metadataStore.Write(entity, merged);
            layerService.ApplyLayerForTimberType(entity, merged.ElementType, layerProfile);
            changedIds.Add(id);
            changed++;
        }

        if (dialog.RenamedCustomDefinition is { } renamedDefinition)
        {
            foreach (var id in DrawingScanner.FindAllTimberElements(
                         document.Database,
                         transaction,
                         metadataStore))
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    continue;
                }

                var renamedData = CustomElementDefinitionRenameRules.Apply(
                    data,
                    renamedDefinition);
                if (ReferenceEquals(renamedData, data))
                {
                    continue;
                }

                previousElementIdById.TryAdd(id, data.ElementId);
                metadataStore.Write(entity, renamedData);
                if (!changedIds.Contains(id))
                {
                    changedIds.Add(id);
                }
            }
        }

        if (changedIds.Count > 0)
        {
            UpdateLabelsForChangedEntities(
                document.Database,
                transaction,
                metadataStore,
                changedIds.ToList(),
                previousElementIdById,
                defaultProfile,
                presentationBatchContext);
            transaction.Commit();
        }

        if (dialog.RenamedCustomDefinition is { } catalogRename)
        {
            try
            {
                CustomElementDefinitionCatalogStore.Save(
                    CustomElementDefinitionCatalogRules.ApplyRename(
                        CustomElementDefinitionCatalogStore.Load(),
                        catalogRename));
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.GetString(
                        "Command_Custom_CatalogSaveFailedFormat",
                        uiCulture),
                    ex.Message));
            }
        }
        editor.WriteMessage(UiStrings.Format(
            UiStrings.GetString("Command_Edit_ResultFormat", uiCulture),
            changed,
            skipped));
    }

    [CommandMethod(AcKrovyCommandNames.FlipSlope, CommandFlags.Modal)]
    public void FlipSlopeDirection()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var selection = editor.GetEntity(UiStrings.CommandFlipSlopePrompt);
        if (selection.Status != PromptStatus.OK)
        {
            return;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                selection.ObjectId,
                OpenMode.ForRead,
                out var selectedEntity,
                document.Database) ||
            selectedEntity is null ||
            !SlopeAnnotationSourceResolver.TryResolveSourceId(
                document.Database,
                transaction,
                metadataStore,
                selectedEntity,
                out var sourceId) ||
            !AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                sourceId,
                OpenMode.ForWrite,
                out var sourceEntity,
                document.Database) ||
            sourceEntity is null ||
            !metadataStore.TryRead(sourceEntity, out var data) ||
            data is null)
        {
            editor.WriteMessage(UiStrings.CommandFlipSlopeNotTimberOrAnnotation);
            return;
        }

        if (data.ElementType == TimberElementType.Post)
        {
            editor.WriteMessage(UiStrings.CommandFlipSlopePostPerpendicular);
            return;
        }

        if (!TimberSlopeAnnotationRules.CanFlipDirection(data.ElementType, data.SlopeDegrees))
        {
            editor.WriteMessage(UiStrings.CommandFlipSlopeHorizontal);
            return;
        }

        var updated = data with
        {
            IsSlopeDirectionReversed = TimberSlopeAnnotationRules.ToggleDirection(
                data.IsSlopeDirectionReversed),
        };
        metadataStore.Write(sourceEntity, updated);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        if (updated.AnnotationMode == TimberAnnotationMode.NoAnnotations)
        {
            TimberAnnotationService.EnsureForElement(
                document.Database,
                transaction,
                sourceEntity,
                updated,
                presentationBatchContext);
        }
        else
        {
            SlopeAnnotationService.EnsureForElement(
                document.Database,
                transaction,
                sourceEntity,
                updated,
                presentationBatchContext.ResolveForElement(updated)
                    .AnnotationScaleContext);
        }
        transaction.Commit();

        editor.WriteMessage(updated.IsSlopeDirectionReversed
            ? UiStrings.CommandFlipSlopeResultReversed
            : UiStrings.CommandFlipSlopeResultNormal);
    }

    [CommandMethod(AcKrovyCommandNames.Inspect, CommandFlags.Modal)]
    public void Inspect()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var result = editor.GetEntity(UiStrings.CommandInspectPrompt);
        if (result.Status != PromptStatus.OK)
        {
            return;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (transaction.GetObject(result.ObjectId, OpenMode.ForRead) is not Entity entity ||
            !AutoCadEntityReader.TryReadTimberElement(entity, metadataStore, out var snapshot) ||
            snapshot is null)
        {
            editor.WriteMessage(UiStrings.CommandInspectNoData);
            return;
        }

        var data = snapshot.Data;
        var uiCulture = AppLanguageService.GetCultureInfo(AppLanguageService.CurrentLanguageCode);
        var elementTypeDisplayName = TimberElementDisplayNameProvider.GetDisplayName(
            data,
            uiCulture);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var annotationScaleService = AutoCadAnnotationScaleService.Create(
            document.Database,
            transaction,
            defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var measurement = TimberElementMeasurer.Measure(snapshot, roundingStepMm);
        var currentDefaultAllowance = defaultProfile.GetCuttingAllowanceMm(data.ElementType);
        var allowanceSource = Math.Abs(data.CuttingAllowanceMm - currentDefaultAllowance) < 0.000001
            ? UiStrings.CommandInspectAllowanceDefault
            : UiStrings.CommandInspectAllowanceIndividual;
        var isFootprintPost = !measurement.PlanLengthMm.HasValue;
        var message = isFootprintPost
            ? UiStrings.Format(
                UiStrings.CommandInspectFootprintSummaryFormat,
                data.ElementId,
                elementTypeDisplayName,
                data.WidthMm,
                data.HeightMm,
                measurement.ActualLengthMm / 1000d,
                measurement.CuttingLengthMm / 1000d,
                measurement.VolumeM3)
            : UiStrings.Format(
                UiStrings.CommandInspectSummaryFormat,
                data.ElementId,
                elementTypeDisplayName,
                data.WidthMm,
                data.HeightMm,
                measurement.PlanLengthMm!.Value / 1000d,
                measurement.ActualLengthMm / 1000d,
                measurement.CuttingLengthMm / 1000d,
                measurement.VolumeM3);
        var rows = new List<InspectInfoRow>
        {
            new(UiStrings.DialogInspectItem, data.ElementId),
            new(UiStrings.DialogInspectElementType, elementTypeDisplayName),
            new(
                UiStrings.DialogInspectMaterial,
                TimberMaterialDisplayNameProvider.GetDisplayName(data.Material, uiCulture)),
            new(UiStrings.DialogInspectWidth, $"{data.WidthMm:0} mm"),
            new(UiStrings.DialogInspectHeight, $"{data.HeightMm:0} mm"),
            new(UiStrings.DialogInspectSlope, $"{data.SlopeDegrees:0.###}°"),
            new(UiStrings.DialogInspectActualLength, $"{measurement.ActualLengthMm:0} mm"),
            new(UiStrings.DialogInspectCuttingAllowance, $"{data.CuttingAllowanceMm:0} mm ({allowanceSource})"),
            new(UiStrings.DialogInspectCuttingLength, $"{measurement.CuttingLengthMm:0} mm"),
            new(UiStrings.DialogInspectManualLengthMode, data.LengthCalculationMode == LengthCalculationMode.ManualLength
                ? UiStrings.GetString("Message_Yes", uiCulture)
                : UiStrings.GetString("Message_No", uiCulture)),
            new(UiStrings.DialogInspectCadHandle, entity.Handle.ToString()),
        };
        if (!isFootprintPost)
        {
            rows.Insert(6, new InspectInfoRow(
                UiStrings.DialogInspectSlopeDirection,
                data.IsSlopeDirectionReversed
                    ? UiStrings.GetString("Message_DirectionReversed", uiCulture)
                    : UiStrings.GetString("Message_DirectionNormal", uiCulture)));
            rows.Insert(7, new InspectInfoRow(
                UiStrings.DialogInspectPlanLength,
                $"{measurement.PlanLengthMm!.Value:0} mm"));
        }
        if (data.ManualLengthMm.HasValue)
        {
            rows.Add(new InspectInfoRow(UiStrings.DialogInspectManualLength, $"{data.ManualLengthMm.Value:0} mm"));
        }

        transaction.Commit();
        editor.WriteMessage(message);
        AcApp.ShowModalWindow(new InspectInfoWindow(rows));
    }

    [CommandMethod(AcKrovyCommandNames.Report, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ReportFromSelection()
    {
        var document = ActiveDocument();
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var ids = PromptForEntities(
            document.Editor,
            UiStrings.GetString("Command_Report_PromptSelection", uiCulture));
        InsertReport(document, ids);
    }

    [CommandMethod(AcKrovyCommandNames.ReportAll, CommandFlags.Modal)]
    public void ReportAll()
    {
        var document = ActiveDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var ids = DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore);
        transaction.Commit();
        InsertReport(document, ids);
    }

    [CommandMethod(AcKrovyCommandNames.Recalc, CommandFlags.Modal)]
    public void RecalculateAll()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var annotationScaleService = AutoCadAnnotationScaleService.Create(
            document.Database,
            transaction,
            defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var checkedCount = 0;
        var errors = 0;

        foreach (var id in DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !AutoCadEntityReader.TryReadTimberElement(entity, metadataStore, out var snapshot) ||
                snapshot is null)
            {
                continue;
            }

            try
            {
                _ = TimberElementMeasurer.Measure(snapshot, roundingStepMm);
                checkedCount++;
            }
            catch (System.Exception ex)
            {
                errors++;
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.CommandRecalcElementErrorFormat,
                    snapshot.Data.ElementId,
                    ex.Message));
            }
        }

        transaction.Commit();
        var labels = ElementLabelService.UpdateAll(document.Database, editor);
        editor.WriteMessage(UiStrings.Format(
            UiStrings.CommandRecalcResultFormat,
            checkedCount,
            errors,
            labels.Processed,
            labels.Skipped));
    }

    private static void AssignWithPresetType(TimberElementType? presetType)
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var message = presetType is null
            ? UiStrings.GetString("Command_Assign_Prompt", uiCulture)
            : UiStrings.Format(
                UiStrings.GetString("Command_Assign_PromptTypeFormat", uiCulture),
                TimberElementTypeDisplayNameProvider.GetDisplayName(presetType.Value, uiCulture));
        var ids = PromptForEntities(editor, message);
        if (ids.Count == 0)
        {
            return;
        }

        // Prednastavenie je iba štartovacia hodnota. V dialógu ho tesár/projektant vždy môže prepísať.
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var seedData = presetType is { } elementType
            ? TimberElementDefaults.For(elementType, defaultProfile)
            : TimberElementDefaults.For(TimberElementType.Rafter, defaultProfile);
        AssignSelectedElements(document, ids, seedData, defaultProfile);
    }

    private static void AssignSelectedElements(
        Document document,
        IReadOnlyList<ObjectId> ids,
        TimberElementData seedData,
        TimberElementDefaultProfile defaultProfile)
    {
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var dialog = new ElementEditWindow(seedData, isNewAssignment: true, defaultProfile);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        if (TryRunPostFootprintAssignment(document, dialog))
        {
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction, editor);
        var assigned = 0;
        var skipped = 0;
        var assignedIds = new List<ObjectId>();
        var previousElementIdById = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity || !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                skipped++;
                continue;
            }

            var hadExistingData = metadataStore.TryRead(entity, out var existing) && existing is not null;
            var original = hadExistingData
                ? existing!
                : seedData.ElementType == TimberElementType.Custom
                    ? seedData
                    : TimberElementDefaults.For(
                        dialog.SelectedElementType ?? seedData.ElementType,
                        defaultProfile);

            var patch = hadExistingData && !dialog.CuttingAllowanceWasEdited
                ? dialog.Patch with { CuttingAllowanceMm = null }
                : dialog.Patch;
            var merged = TimberElementPatcher.Apply(original, patch);
            if (seedData.ElementType == TimberElementType.Custom &&
                CustomElementDefinitionRules.TryFromElementData(seedData, out var definition) &&
                definition is not null)
            {
                merged = CustomElementDefinitionRules.Apply(merged, definition);
            }
            if (dialog.UseDefaultCuttingAllowanceByType)
            {
                merged = TimberElementDefaultApplicator.ApplyCuttingAllowance(merged, defaultProfile);
            }

            previousElementIdById[id] = original.ElementId;
            metadataStore.Write(entity, merged);
            layerService.ApplyLayerForTimberType(entity, merged.ElementType, layerProfile);
            assignedIds.Add(id);
            assigned++;
        }

        UpdateLabelsForChangedEntities(
            document.Database,
            transaction,
            metadataStore,
            assignedIds,
            previousElementIdById,
            defaultProfile,
            presentationBatchContext);

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(
            UiStrings.GetString("Command_Assign_ResultFormat", uiCulture),
            assigned,
            skipped));
    }

    [CommandMethod(AcKrovyCommandNames.Renumber, CommandFlags.Modal)]
    public void RenumberAllItems()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        if (!ConfirmRenumbering(editor, uiCulture))
        {
            return;
        }

        try
        {
            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var result = TimberElementRenumberingService.RenumberAll(
                document.Database,
                defaultProfile,
                defaultProfile.GetCuttingLengthRoundingStepMm());
            if (result.ProcessedElements == 0)
            {
                editor.WriteMessage(UiStrings.GetString("Command_Renumber_NoElements", uiCulture));
                return;
            }

            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Renumber_ResultFormat", uiCulture),
                result.ProcessedElements,
                result.UniqueItems,
                result.RenumberedElementTypes,
                result.ChangedElements));
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_Renumber_FailedFormat", uiCulture),
                ex.Message));
        }
    }

    private static bool ConfirmRenumbering(Editor editor, System.Globalization.CultureInfo uiCulture)
    {
        var yes = UiStrings.GetString("Message_Yes", uiCulture);
        var no = UiStrings.GetString("Message_No", uiCulture);
        var options = new PromptKeywordOptions(
            UiStrings.GetString("Command_Renumber_ConfirmPrompt", uiCulture))
        {
            AllowNone = true,
            AppendKeywordsToMessage = false,
        };
        options.Keywords.Add("Yes", yes, yes);
        if (RenumberConfirmationRules.SupportsSlovakAsciiYesAlias(uiCulture))
        {
            options.Keywords.Add(
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                RenumberConfirmationRules.SlovakAsciiYesKeyword,
                false,
                true);
        }
        options.Keywords.Add("No", no, no);
        options.Keywords.Default = "No";

        var response = editor.GetKeywords(options);
        return response.Status == PromptStatus.OK &&
               RenumberConfirmationRules.IsConfirmed(response.StringResult, yes, uiCulture);
    }

    private static bool TryRunPostFootprintAssignment(
        Document document,
        ElementEditWindow dialog)
    {
        if (dialog.SelectedElementType != TimberElementType.Post || dialog.Patch is null)
        {
            return false;
        }

        document.Editor.WriteMessage(UiStrings.CommandPostFootprintAssignRedirect);
        PostFootprintAssignmentWorkflow.Run(
            document,
            dialog.Patch,
            dialog.CuttingAllowanceWasEdited,
            dialog.UseDefaultCuttingAllowanceByType);
        return true;
    }

    private static bool HasMixedCuttingAllowance(IReadOnlyList<TimberElementData> selectedData)
    {
        if (selectedData.Count < 2)
        {
            return false;
        }

        var first = selectedData[0].CuttingAllowanceMm;
        return selectedData.Skip(1).Any(data => Math.Abs(data.CuttingAllowanceMm - first) > 0.000001);
    }

    private static bool HasMixedSlopeDirection(IReadOnlyList<TimberElementData> selectedData)
    {
        if (selectedData.Count < 2)
        {
            return false;
        }

        var first = selectedData[0].IsSlopeDirectionReversed;
        return selectedData.Skip(1).Any(data => data.IsSlopeDirectionReversed != first);
    }

    private static void UpdateLabelsForChangedEntities(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> changedIds,
        IReadOnlyDictionary<ObjectId, string> previousElementIdById,
        TimberElementDefaultProfile defaultProfile,
        AutoCadAnnotationPresentationBatchContext presentationBatchContext)
  {
        ArgumentNullException.ThrowIfNull(presentationBatchContext);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var synchronizedDataById = TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            changedIds,
            roundingStepMm);

        foreach (var id in changedIds.Distinct())
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !synchronizedDataById.TryGetValue(id, out var synchronizedData))
            {
                continue;
            }

            previousElementIdById.TryGetValue(id, out var previousElementId);
            TimberAnnotationService.EnsureForElement(
                database,
                transaction,
                entity,
                synchronizedData,
                presentationBatchContext,
                previousElementId,
                roundingStepMm);
        }
    }

    private static void ApplyLayersToExistingElements(Document document, ElementLayerProfile profile)
    {
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction, editor);
        var updated = 0;
        var skipped = 0;

        foreach (var id in DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore))
        {
            try
            {
                if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    skipped++;
                    continue;
                }

                var layerResult = layerService.ApplyLayerForTimberType(
                    entity,
                    data.ElementType,
                    profile,
                    CadLayerUpdateMode.UpdateExisting);
                if (layerResult.DrawingChanged)
                {
                    updated++;
                }
            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage(UiStrings.Format(UiStrings.CommandLayersElementSkippedFormat, ex.Message));
            }
        }

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(UiStrings.CommandLayersResultFormat, updated, skipped));
    }

    private static SettingsDrawingApplyResult ApplySettingsToExistingElements(
        Document document,
        TimberElementDefaultProfile defaultProfile,
        IReadOnlyList<ObjectId>? targetIds,
        TimberAnnotationSettingsRequest annotationSettings)
    {
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var applyAll = annotationSettings.ApplyScope ==
            TimberAnnotationSettingsApplyScope.AllElements;
        var drawingScaleStore = new AutoCadDrawingAnnotationScaleStore(
            document.Database,
            transaction);
        var hasDrawingScale = drawingScaleStore.TryRead(out var drawingScaleDenominator);
        var drawingScaleChanged = applyAll &&
            (!hasDrawingScale ||
             drawingScaleDenominator != annotationSettings.ScaleDenominator);
        if (applyAll)
        {
            drawingScaleStore.Write(annotationSettings.ScaleDenominator);
        }

        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var ids = targetIds is null
            ? DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore)
            : targetIds.Distinct().ToList();
        var updated = 0;
        var eligible = 0;
        var skipped = 0;
        var changedIds = new List<ObjectId>();
        var eligibleIds = new List<ObjectId>();
        var previousElementIdById = new Dictionary<ObjectId, string>();
        var annotationPatch = annotationSettings.CreateElementPatch();

        foreach (var id in ids)
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        document.Database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    skipped++;
                    continue;
                }

                eligible++;
                eligibleIds.Add(id);
                var updatedData = TimberAnnotationSettingsApplicator.Apply(
                    data,
                    annotationPatch);
                var metadataChanged = updatedData != data;
                if (metadataChanged)
                {
                    entity.UpgradeOpen();
                    previousElementIdById[id] = data.ElementId;
                    metadataStore.Write(entity, updatedData);
                    changedIds.Add(id);
                    updated++;
                }
            }
            catch (System.Exception ex)
            {
                throw new InvalidOperationException(
                    UiStrings.Format(
                        UiStrings.CommandSettingsApplyElementSkippedFormat,
                        ex.Message),
                    ex);
            }
        }

        var refreshIds = drawingScaleChanged ? eligibleIds : changedIds;
        UpdateLabelsForChangedEntities(
            document.Database,
            transaction,
            metadataStore,
            refreshIds,
            previousElementIdById,
            defaultProfile,
            presentationBatchContext);

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(UiStrings.CommandSettingsApplyResultFormat, updated, skipped));
        return new SettingsDrawingApplyResult(
            updated,
            skipped,
            eligible,
            drawingScaleChanged || changedIds.Count > 0);
    }

    private sealed record SettingsDrawingApplyResult(
        int Updated,
        int Skipped,
        int Eligible,
        bool Changed);

    private static AnnotationScaleSettingsState ReadAnnotationScaleSettingsState(
        Document document)
    {
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        var store = new AutoCadDrawingAnnotationScaleStore(
            document.Database,
            transaction);
        var hasDrawingOverride = store.TryRead(out var drawingDenominator);
        var effectiveDenominator = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingOverride,
            drawingDenominator).Denominator;
        return new AnnotationScaleSettingsState(
            hasDrawingOverride,
            hasDrawingOverride ? drawingDenominator : effectiveDenominator,
            effectiveDenominator);
    }

    private static IReadOnlyList<string> ReadAvailableLinetypeNames(Database database)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var service = new AutoCadTimberLayerService(database, transaction);
        return service.GetAvailableLinetypeNames();
    }

    private static IReadOnlyList<string> ReadAvailableLayerNames(Database database)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        return TimberLayerService.GetAvailableLayerNames(database, transaction);
    }

    private static IReadOnlyList<CadLayerPreset> ReadAvailableLayerPresets(Database database)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        return TimberLayerService.GetAvailableLayerPresets(database, transaction);
    }

    private static IReadOnlyList<string> ReadExistingLayerConflicts(
        Database database,
        ElementLayerProfile profile)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        return TimberLayerService.GetConflictingExistingLayerNames(
            database,
            transaction,
            profile);
    }

    private static void ShowDiagnostics()
    {
        _ = AppLanguageSettingsStore.Load();
        var preferences = SettingsUiPreferencesStore.Load();
        _ = ElementLayerProfileStore.Load();
        _ = TimberElementDefaultProfileStore.Load();
        _ = CustomElementDefinitionCatalogStore.Load();

        var culture = AppLanguageService.CurrentUiCulture;
        var hostVersion =
            typeof(AcApp).Assembly.GetName().Version?.ToString() ??
            UiStrings.GetString("Common_Unknown", culture);
        var informationRows = new[]
        {
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_ProductVersion", culture),
                ApplicationVersionProvider.DisplayVersion),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_MetadataSchema", culture),
                TimberElementDataSchema.CurrentVersion.ToString(culture)),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_LayerProfileSchema", culture),
                ElementLayerProfile.CurrentVersion.ToString(culture)),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_Host", culture),
                $"AutoCAD {hostVersion}"),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_Runtime", culture),
                RuntimeInformation.FrameworkDescription),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_Language", culture),
                $"{AppLanguageService.CurrentLanguageCode} ({culture.Name})"),
            new DiagnosticsInfoRow(
                UiStrings.GetString("DiagnosticsWindow_LogPath", culture),
                AcKrovyDiagnostics.LogDirectory),
        };

        var settingsRows = AcKrovyDiagnostics.Settings.GetStatuses()
            .Select(status => new DiagnosticsInfoRow(
                status.FileName,
                LocalizeSettingsState(status, culture)))
            .ToArray();
        var events = AcKrovyDiagnostics.Logger.GetRecentEvents(12)
            .Select(diagnosticEvent =>
                DiagnosticsRecentEventFormatter.Format(diagnosticEvent, culture))
            .ToArray();
        var summary = DiagnosticsSupportSummaryBuilder.Build(
            informationRows,
            settingsRows,
            events,
            UiStrings.GetString("DiagnosticsWindow_SettingsStates", culture),
            UiStrings.GetString("DiagnosticsWindow_RecentEvents", culture));
        AcApp.ShowModalWindow(new DiagnosticsWindow(
            informationRows,
            settingsRows,
            events,
            summary,
            AcKrovyDiagnostics.LogDirectory,
            preferences.Theme));
    }

    private static string LocalizeSettingsState(
        SettingsFileStatus status,
        System.Globalization.CultureInfo culture)
    {
        var stateKey = status.State switch
        {
            SettingsFileState.Missing => "DiagnosticsWindow_StateMissing",
            SettingsFileState.Loaded => "DiagnosticsWindow_StateLoaded",
            SettingsFileState.CorruptBackupCreated => "DiagnosticsWindow_StateRecovered",
            SettingsFileState.CorruptBackupFailed => "DiagnosticsWindow_StateMemoryOnly",
            SettingsFileState.SaveFailed => "DiagnosticsWindow_StateSaveFailed",
            _ => "Common_Unknown",
        };
        var state = UiStrings.GetString(stateKey, culture);
        return string.IsNullOrWhiteSpace(status.BackupFileName)
            ? state
            : $"{state} ({status.BackupFileName})";
    }

    private static void SelectSimilarCore()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var seedId = PromptForSeedEntity(editor);
        if (seedId.IsNull)
        {
            return;
        }

        TimberElementSnapshot? seed;
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            seed = AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    seedId,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) &&
                entity is not null &&
                AutoCadEntityReader.TryReadTimberElement(entity, metadataStore, out var snapshot)
                    ? snapshot
                    : null;
        }

        if (seed is null)
        {
            editor.WriteMessage(UiStrings.GetString("Command_SelectSimilar_InvalidSeed"));
            return;
        }

        var preferences = SettingsUiPreferencesStore.Load();
        var dialog = new SelectSimilarWindow(seed, preferences.Theme);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Criteria is null)
        {
            return;
        }

        var roundingStepMm = TimberElementDefaultProfileStore
            .Load()
            .GetCuttingLengthRoundingStepMm();
        IReadOnlyList<ObjectId> matches;
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            matches = DrawingScanner
                .ReadAllTimberElements(document.Database, transaction, metadataStore)
                .Where(item => TimberElementSimilarityFilter.Matches(
                    seed,
                    item.Snapshot,
                    dialog.Criteria,
                    roundingStepMm))
                .Select(item => item.ObjectId)
                .ToArray();
        }

        editor.SetImpliedSelection(matches.Count == 0
            ? Array.Empty<ObjectId>()
            : matches.ToArray());
        editor.WriteMessage(UiStrings.Format(
            UiStrings.GetString("Command_SelectSimilar_ResultFormat"),
            matches.Count));
    }

    private static ObjectId PromptForSeedEntity(Editor editor)
    {
        var implied = editor.SelectImplied();
        if (implied.Status == PromptStatus.OK &&
            implied.Value is not null &&
            implied.Value.Count > 0)
        {
            if (implied.Value.Count != 1)
            {
                editor.WriteMessage(UiStrings.GetString(
                    "Command_SelectSimilar_SelectOne"));
                return ObjectId.Null;
            }

            return implied.Value.GetObjectIds()[0];
        }

        var options = new PromptEntityOptions(
            UiStrings.GetString("Command_SelectSimilar_PromptSeed"));
        var result = editor.GetEntity(options);
        return result.Status == PromptStatus.OK
            ? result.ObjectId
            : ObjectId.Null;
    }

    private static void ExportCsvCore()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var pickFirst = editor.SelectImplied();
        var pickFirstIds = pickFirst.Status == PromptStatus.OK &&
            pickFirst.Value is not null
                ? pickFirst.Value.GetObjectIds()
                : Array.Empty<ObjectId>();
        var preferences = SettingsUiPreferencesStore.Load();
        var optionsDialog = new CsvExportWindow(
            pickFirstIds.Length,
            preferences.Theme);
        if (AcApp.ShowModalWindow(optionsDialog) != true)
        {
            return;
        }

        IReadOnlyList<ObjectId> ids;
        switch (optionsDialog.Source)
        {
            case CsvExportSource.PickFirst:
                ids = pickFirstIds;
                break;
            case CsvExportSource.ManualSelection:
                ids = PromptForManualEntities(
                    editor,
                    UiStrings.GetString("Command_ExportCsv_PromptSelection"));
                if (ids.Count == 0)
                {
                    return;
                }

                break;
            case CsvExportSource.ModelSpace:
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
                    ids = DrawingScanner.FindAllTimberElements(
                        document.Database,
                        transaction,
                        metadataStore);
                }

                break;
            default:
                throw new InvalidOperationException("Unsupported CSV export source.");
        }

        var measurements = ReadMeasurements(
            document.Database,
            ids,
            out var skippedCount);
        if (measurements.Count == 0)
        {
            editor.WriteMessage(UiStrings.GetString("Command_ExportCsv_NoValidElements"));
            return;
        }

        var culture = AppLanguageService.CurrentUiCulture;
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = UiStrings.GetString("CsvExport_SaveDialogTitle", culture),
            Filter = UiStrings.GetString("CsvExport_SaveDialogFilter", culture),
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true,
            FileName = $"ACAD_KROVY_{DateTime.Now:yyyyMMdd}.csv",
        };
        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        var csv = TimberCsvFormatter.Format(
            measurements,
            optionsDialog.Mode,
            TimberCsvLocalizationProvider.Create(culture),
            culture);
        try
        {
            SafeFileWriter.WriteAllBytes(saveDialog.FileName, csv.ToUtf8WithBom());
        }
        catch (System.Exception exception)
        {
            AcKrovyDiagnostics.Warning(
                "CsvExportWriteFailed",
                "CSV file write failed.",
                AcKrovyCommandNames.ExportCsv,
                exception);
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_ExportCsv_WriteFailedFormat"),
                exception.Message));
            return;
        }

        editor.WriteMessage(UiStrings.Format(
            UiStrings.GetString("Command_ExportCsv_ResultFormat"),
            saveDialog.FileName,
            csv.RowCount,
            skippedCount));
    }

    private static IReadOnlyList<TimberElementMeasurement> ReadMeasurements(
        Database database,
        IReadOnlyList<ObjectId> ids,
        out int skippedCount)
    {
        var measurements = new List<TimberElementMeasurement>();
        skippedCount = 0;
        var roundingStepMm = TimberElementDefaultProfileStore
            .Load()
            .GetCuttingLengthRoundingStepMm();
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var id in ids)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !AutoCadEntityReader.TryReadTimberElement(
                    entity,
                    metadataStore,
                    out var snapshot) ||
                snapshot is null)
            {
                skippedCount++;
                continue;
            }

            try
            {
                measurements.Add(TimberElementMeasurer.Measure(
                    snapshot,
                    roundingStepMm));
            }
            catch
            {
                skippedCount++;
            }
        }

        return measurements;
    }

    private static IReadOnlyList<ObjectId> PromptForManualEntities(
        Editor editor,
        string message)
    {
        var options = new PromptSelectionOptions
        {
            MessageForAdding = message,
            MessageForRemoval = UiStrings.CommandPromptRemoveSelection,
            AllowDuplicates = false,
        };
        var selection = editor.GetSelection(options);
        return selection.Status == PromptStatus.OK && selection.Value is not null
            ? selection.Value.GetObjectIds()
            : Array.Empty<ObjectId>();
    }

    private static void SetLabelsVisibility(bool visible)
    {
        var document = ActiveDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var changed = ElementLabelService.SetVisible(document.Database, transaction, visible);
        transaction.Commit();

        document.Editor.WriteMessage(changed
            ? visible
                ? UiStrings.CommandLabelsShown
                : UiStrings.CommandLabelsHidden
            : UiStrings.CommandLabelsLayerMissing);
    }

    private static void InsertReport(Document document, IReadOnlyList<ObjectId> ids)
    {
        var editor = document.Editor;
        var uiCulture = AppLanguageService.CurrentUiCulture;
        if (ids.Count == 0)
        {
            editor.WriteMessage(UiStrings.GetString("Command_Report_NoneFound", uiCulture));
            return;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var annotationScaleService = AutoCadAnnotationScaleService.Create(
            document.Database,
            transaction,
            defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        _ = TimberElementItemIdentityService.SynchronizeElementIds(
            document.Database,
            transaction,
            metadataStore,
            ids,
            roundingStepMm);
        var measurements = new List<TimberElementMeasurement>();
        var skipped = 0;

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !AutoCadEntityReader.TryReadTimberElement(entity, metadataStore, out var snapshot) ||
                snapshot is null)
            {
                skipped++;
                continue;
            }

            try
            {
                measurements.Add(TimberElementMeasurer.Measure(snapshot, roundingStepMm));
            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.GetString("Command_Report_ElementSkippedFormat", uiCulture),
                    snapshot.Data.ElementId,
                    ex.Message));
            }
        }

        if (measurements.Count == 0)
        {
            editor.WriteMessage(UiStrings.GetString("Command_Report_NoValidElements", uiCulture));
            return;
        }

        var pointResult = editor.GetPoint(
            UiStrings.GetString("Command_Report_PromptInsertionPoint", uiCulture));
        if (pointResult.Status != PromptStatus.OK)
        {
            return;
        }

        var report = TimberReportBuilder.Build(measurements);
        ReportTableWriter.Insert(
            document.Database,
            transaction,
            pointResult.Value,
            report,
            uiCulture);
        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(
            UiStrings.GetString("Command_Report_InsertedFormat", uiCulture),
            measurements.Count,
            skipped,
            report.TotalVolumeM3));
    }

    private static IReadOnlyList<ObjectId> PromptForEntities(Editor editor, string message)
    {
        // Umožní pracovný postup: najprv označiť prvky, potom kliknúť na ikonku/príkaz.
        var implied = editor.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value is not null && implied.Value.Count > 0)
        {
            var ids = implied.Value.GetObjectIds();
            editor.SetImpliedSelection(Array.Empty<ObjectId>());
            return ids;
        }

        var options = new PromptSelectionOptions
        {
            MessageForAdding = message,
            MessageForRemoval = UiStrings.CommandPromptRemoveSelection,
            AllowDuplicates = false,
        };

        var selection = editor.GetSelection(options);
        return selection.Status == PromptStatus.OK && selection.Value is not null
            ? selection.Value.GetObjectIds()
            : Array.Empty<ObjectId>();
    }

    private static EditSelectionResult ResolveEditSelection(
        Database database,
        Editor editor,
        string message)
    {
        var implied = editor.SelectImplied();
        var impliedIds = implied.Status == PromptStatus.OK &&
            implied.Value is not null
                ? implied.Value.GetObjectIds()
                : Array.Empty<ObjectId>();

        if (impliedIds.Length > 0)
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            var decision = TimberEditSelectionRules.Evaluate(
                impliedIds,
                id =>
                    AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) &&
                    entity is not null &&
                    AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) &&
                    metadataStore.TryRead(entity, out var data) &&
                    data is not null);

            if (decision.UseImpliedSelection)
            {
                return new EditSelectionResult(
                    decision.ValidItems,
                    decision.RejectedItems);
            }

            editor.SetImpliedSelection(Array.Empty<ObjectId>());
        }

        return new EditSelectionResult(
            PromptForManualEntities(editor, message),
            RejectedImpliedItems: 0);
    }

    private static SettingsSelectionResult PromptForSettingsEntities(
        Editor editor,
        string message)
    {
        var implied = editor.SelectImplied();
        if (implied.Status == PromptStatus.OK &&
            implied.Value is not null &&
            implied.Value.Count > 0)
        {
            var ids = implied.Value.GetObjectIds();
            editor.SetImpliedSelection(Array.Empty<ObjectId>());
            return new SettingsSelectionResult(
                SettingsSelectionStatus.Selected,
                ids);
        }

        var options = new PromptSelectionOptions
        {
            MessageForAdding = message,
            MessageForRemoval = UiStrings.CommandPromptRemoveSelection,
            AllowDuplicates = false,
        };
        var selection = editor.GetSelection(options);
        if (selection.Status == PromptStatus.Cancel)
        {
            return new SettingsSelectionResult(
                SettingsSelectionStatus.Cancelled,
                []);
        }

        return selection.Status == PromptStatus.OK &&
            selection.Value is not null &&
            selection.Value.Count > 0
                ? new SettingsSelectionResult(
                    SettingsSelectionStatus.Selected,
                    selection.Value.GetObjectIds())
                : new SettingsSelectionResult(
                    SettingsSelectionStatus.Empty,
                    []);
    }

    private static IReadOnlyList<ObjectId> FilterSettingsTimberElementIds(
        Database database,
        IEnumerable<ObjectId> candidateIds)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        return candidateIds
            .Distinct()
            .Where(id =>
                AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) &&
                entity is not null &&
                AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) &&
                metadataStore.TryRead(entity, out var data) &&
                data is not null)
            .ToList();
    }

    private enum SettingsSelectionStatus
    {
        Selected,
        Cancelled,
        Empty,
    }

    private sealed record SettingsSelectionResult(
        SettingsSelectionStatus Status,
        IReadOnlyList<ObjectId> Ids);

    private sealed record EditSelectionResult(
        IReadOnlyList<ObjectId> Ids,
        int RejectedImpliedItems);

    private static Document ActiveDocument() => AcApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException(UiStrings.ErrorNoActiveDrawing);

    private static Editor ActiveEditor() => ActiveDocument().Editor;
}
