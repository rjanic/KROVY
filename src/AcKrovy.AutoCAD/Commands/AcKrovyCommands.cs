using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

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
        var editor = document.Editor;
        var dialog = new LayerSettingsWindow(
            ElementLayerProfileStore.Load(),
            TimberElementDefaultProfileStore.Load(),
            AppLanguageService.CurrentLanguageCode);
        if (AcApp.ShowModalWindow(dialog) != true)
        {
            return;
        }

        if (!SettingsWindowActionRules.AppliesElementSettings(dialog.SaveMode))
        {
            try
            {
                AppLanguageSettingsStore.Save(CreateLanguageSettings(dialog.LanguageCode));
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage(UiStrings.Format(UiStrings.CommandSettingsSaveFailedFormat, ex.Message));
                return;
            }

            ApplySelectedLanguage(dialog.LanguageCode);
            editor.WriteMessage(UiStrings.CommandSettingsSaved);
            return;
        }

        if (dialog.Profile is null || dialog.DefaultProfile is null)
        {
            return;
        }

        try
        {
            ElementLayerProfileStore.Save(dialog.Profile);
            TimberElementDefaultProfileStore.Save(dialog.DefaultProfile);
            AppLanguageSettingsStore.Save(CreateLanguageSettings(dialog.LanguageCode));
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage(UiStrings.Format(UiStrings.CommandSettingsSaveFailedFormat, ex.Message));
            return;
        }

        ApplySelectedLanguage(dialog.LanguageCode);

        editor.WriteMessage(UiStrings.CommandSettingsSaved);
        switch (dialog.SaveMode)
        {
            case SettingsSaveMode.AllElements:
                ApplySettingsToExistingElements(document, dialog.Profile, dialog.DefaultProfile, null);
                break;
            case SettingsSaveMode.SelectedElements:
                var ids = PromptForEntities(editor, UiStrings.CommandSettingsPromptApplyAllowances);
                if (ids.Count == 0)
                {
                    editor.WriteMessage(UiStrings.CommandSettingsSelectionCancelled);
                    return;
                }

                ApplySettingsToExistingElements(document, dialog.Profile, dialog.DefaultProfile, ids);
                break;
            case SettingsSaveMode.NewElementsOnly:
                if (dialog.ApplyToExistingElements)
                {
                    ApplyLayersToExistingElements(document, dialog.Profile);
                }

                break;
        }
    }

    private static AppLanguageSettings CreateLanguageSettings(string languageCode) => new()
    {
        LanguageCode = languageCode,
    };

    private static void ApplySelectedLanguage(string languageCode)
    {
        var languageChanged = !string.Equals(
            AppLanguageService.CurrentLanguageCode,
            languageCode,
            StringComparison.Ordinal);
        if (!languageChanged)
        {
            return;
        }

        AppLanguageService.Apply(languageCode);
        if (!AcKrovyRibbon.RebuildLocalizedUi(activateTab: false))
        {
            AcKrovyRibbon.ScheduleCreation();
        }

        ClassicToolbarManager.RefreshLocalizedContent();
    }

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
    public void AssignPost() => AssignWithPresetType(TimberElementType.Post);

    [CommandMethod(AcKrovyCommandNames.CollarTie, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignCollarTie() => AssignWithPresetType(TimberElementType.CollarTie);

    [CommandMethod(AcKrovyCommandNames.Brace, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignBrace() => AssignWithPresetType(TimberElementType.Brace);

    [CommandMethod(AcKrovyCommandNames.TieBeam, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignTieBeam() => AssignWithPresetType(TimberElementType.TieBeam);

    [CommandMethod(AcKrovyCommandNames.Edit, CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Edit()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var ids = PromptForEntities(editor, UiStrings.CommandEditPrompt);
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
            editor.WriteMessage(UiStrings.CommandEditNoData);
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
        dialog.Title = selectedData.Count == 1
            ? UiStrings.Format(
                UiStrings.CommandEditTitleSingleFormat,
                selectedData[0].ElementId,
                TimberElementTypeDisplayNameProvider.GetDisplayName(selectedData[0].ElementType))
            : UiStrings.Format(UiStrings.CommandEditTitleMultipleFormat, selectedData.Count);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction);
        var changed = 0;
        var skipped = 0;
        var changedIds = new List<ObjectId>();
        var previousElementIdById = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                !metadataStore.TryRead(entity, out var original) ||
                original is null)
            {
                skipped++;
                continue;
            }

            var merged = TimberElementPatcher.Apply(original, dialog.Patch);
            if (dialog.UseDefaultCuttingAllowanceByType)
            {
                merged = TimberElementDefaultApplicator.ApplyCuttingAllowance(merged, defaultProfile);
            }

            previousElementIdById[id] = original.ElementId;
            metadataStore.Write(entity, merged);
            layerService.ApplyLayerForTimberType(entity, merged.ElementType, layerProfile);
            changedIds.Add(id);
            changed++;
        }

        UpdateLabelsForChangedEntities(document.Database, transaction, metadataStore, changedIds, previousElementIdById);

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(UiStrings.CommandEditResultFormat, changed, skipped));
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

        if (!TimberSlopeAnnotationRules.CanFlipDirection(data.SlopeDegrees))
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
        SlopeAnnotationService.EnsureForElement(
            document.Database,
            transaction,
            sourceEntity,
            updated);
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
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var measurement = TimberElementMeasurer.Measure(snapshot, roundingStepMm);
        var currentDefaultAllowance = defaultProfile.GetCuttingAllowanceMm(data.ElementType);
        var allowanceSource = Math.Abs(data.CuttingAllowanceMm - currentDefaultAllowance) < 0.000001
            ? UiStrings.CommandInspectAllowanceDefault
            : UiStrings.CommandInspectAllowanceIndividual;
        var message = UiStrings.Format(
            UiStrings.CommandInspectSummaryFormat,
            data.ElementId,
            TimberElementTypeDisplayNameProvider.GetDisplayName(data.ElementType),
            data.WidthMm,
            data.HeightMm,
            measurement.PlanLengthMm / 1000d,
            measurement.ActualLengthMm / 1000d,
            measurement.CuttingLengthMm / 1000d,
            measurement.VolumeM3);
        var rows = new List<InspectInfoRow>
        {
            new(UiStrings.DialogInspectItem, data.ElementId),
            new(UiStrings.DialogInspectElementType, TimberElementTypeDisplayNameProvider.GetDisplayName(data.ElementType)),
            new(UiStrings.DialogInspectMaterial, data.Material),
            new(UiStrings.DialogInspectWidth, $"{data.WidthMm:0} mm"),
            new(UiStrings.DialogInspectHeight, $"{data.HeightMm:0} mm"),
            new(UiStrings.DialogInspectSlope, $"{data.SlopeDegrees:0.###}°"),
            new(UiStrings.DialogInspectSlopeDirection, data.IsSlopeDirectionReversed
                ? UiStrings.MessageDirectionReversed
                : UiStrings.MessageDirectionNormal),
            new(UiStrings.DialogInspectPlanLength, $"{measurement.PlanLengthMm:0} mm"),
            new(UiStrings.DialogInspectActualLength, $"{measurement.ActualLengthMm:0} mm"),
            new(UiStrings.DialogInspectCuttingAllowance, $"{data.CuttingAllowanceMm:0} mm ({allowanceSource})"),
            new(UiStrings.DialogInspectCuttingLength, $"{measurement.CuttingLengthMm:0} mm"),
            new(UiStrings.DialogInspectManualLengthMode, data.LengthCalculationMode == LengthCalculationMode.ManualLength
                ? UiStrings.MessageYes
                : UiStrings.MessageNo),
            new(UiStrings.DialogInspectCadHandle, entity.Handle.ToString()),
        };
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
        var ids = PromptForEntities(document.Editor, UiStrings.CommandReportPromptSelection);
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
        var message = presetType is null
            ? UiStrings.CommandAssignPrompt
            : UiStrings.Format(
                UiStrings.CommandAssignPromptTypeFormat,
                TimberElementTypeDisplayNameProvider.GetDisplayName(presetType.Value));
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
        var dialog = new ElementEditWindow(seedData, isNewAssignment: true, defaultProfile);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction);
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
                : TimberElementDefaults.For(dialog.SelectedElementType ?? seedData.ElementType, defaultProfile);

            var patch = hadExistingData && !dialog.CuttingAllowanceWasEdited
                ? dialog.Patch with { CuttingAllowanceMm = null }
                : dialog.Patch;
            var merged = TimberElementPatcher.Apply(original, patch);
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

        UpdateLabelsForChangedEntities(document.Database, transaction, metadataStore, assignedIds, previousElementIdById);

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(UiStrings.CommandAssignResultFormat, assigned, skipped));
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
        IReadOnlyDictionary<ObjectId, string> previousElementIdById)
    {
        var defaultProfile = TimberElementDefaultProfileStore.Load();
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
                previousElementId,
                roundingStepMm);
        }
    }

    private static void ApplyLayersToExistingElements(Document document, ElementLayerProfile profile)
    {
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction);
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

                layerService.ApplyLayerForTimberType(entity, data.ElementType, profile);
                updated++;
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

    private static void ApplySettingsToExistingElements(
        Document document,
        ElementLayerProfile layerProfile,
        TimberElementDefaultProfile defaultProfile,
        IReadOnlyList<ObjectId>? targetIds)
    {
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction);
        var ids = targetIds is null
            ? DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore)
            : targetIds.Distinct().ToList();
        var updated = 0;
        var skipped = 0;
        var changedIds = new List<ObjectId>();
        var previousElementIdById = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
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

                var updatedData = TimberElementDefaultApplicator.ApplyCuttingAllowance(data, defaultProfile);
                previousElementIdById[id] = data.ElementId;
                metadataStore.Write(entity, updatedData);
                layerService.ApplyLayerForTimberType(entity, updatedData.ElementType, layerProfile);
                changedIds.Add(id);
                updated++;
            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.CommandSettingsApplyElementSkippedFormat,
                    ex.Message));
            }
        }

        UpdateLabelsForChangedEntities(
            document.Database,
            transaction,
            metadataStore,
            changedIds,
            previousElementIdById);

        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(UiStrings.CommandSettingsApplyResultFormat, updated, skipped));
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
        if (ids.Count == 0)
        {
            editor.WriteMessage(UiStrings.CommandReportNoneFound);
            return;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var defaultProfile = TimberElementDefaultProfileStore.Load();
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
                    UiStrings.CommandReportElementSkippedFormat,
                    snapshot.Data.ElementId,
                    ex.Message));
            }
        }

        if (measurements.Count == 0)
        {
            editor.WriteMessage(UiStrings.CommandReportNoValidElements);
            return;
        }

        var pointResult = editor.GetPoint(UiStrings.CommandReportPromptInsertionPoint);
        if (pointResult.Status != PromptStatus.OK)
        {
            return;
        }

        var report = TimberReportBuilder.Build(measurements);
        ReportTableWriter.Insert(document.Database, transaction, pointResult.Value, report);
        transaction.Commit();
        editor.WriteMessage(UiStrings.Format(
            UiStrings.CommandReportInsertedFormat,
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

    private static Document ActiveDocument() => AcApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException(UiStrings.ErrorNoActiveDrawing);

    private static Editor ActiveEditor() => ActiveDocument().Editor;
}
