using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
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
    [CommandMethod("AK_HELP", CommandFlags.Modal)]
    public void Help()
    {
        var editor = ActiveEditor();
        editor.WriteMessage(
            "\nACAD KROVY 0.10.0"
            + "\n\nPRVKY KROVU"
            + "\n  AK_KROKVA      – rýchlo priradí typ Krokva"
            + "\n  AK_POMURNICA   – rýchlo priradí typ Pomúrnica"
            + "\n  AK_VAZNICA     – rýchlo priradí typ Väznica"
            + "\n  AK_STLPIK      – rýchlo priradí typ Stĺpik"
            + "\n  AK_KLIESTINA   – rýchlo priradí typ Klieština / hambálok"
            + "\n  AK_VZPERA      – rýchlo priradí typ Vzpera"
            + "\n  AK_VAZNYTRAM   – rýchlo priradí typ Väzný trám"
            + "\n\nÚDAJE A VÝKAZY"
            + "\n  AK_ASSIGN      – priradí údaje vybraným čiaram/polyline"
            + "\n  AK_EDIT        – hromadne upraví zaškrtnuté hodnoty"
            + "\n  AK_INSPECT     – zobrazí údaje jedného prvku"
            + "\n  AK_REPORT      – vloží tabuľku z vybraných prvkov"
            + "\n  AK_REPORTALL   – vloží tabuľku zo všetkých prvkov"
            + "\n  AK_RECALC     – skontroluje prepočty všetkých prvkov"
            + "\n  AK_RIBBON      – zobrazí/obnoví kartu ACAD KROVY v Ribbóne"
            + "\n  AK_TOOLBAR     – zobrazí/skryje klasický plávajúci panel malých ikon"
            + "\n  AK_SETTINGS    – nastaví hladiny a farby jednotlivých typov prvkov"
            + "\n  AK_APPLYLAYERS – premietne aktuálne nastavenia hladín do výkresu"
            + "\n\nPOPISY VO VÝKRESE"
            + "\n  AK_LABELS      – vytvorí alebo obnoví popisy všetkých prvkov"
            + "\n  AK_LABELSELECTED – vytvorí alebo obnoví popisy označených prvkov"
            + "\n  AK_LABELSHOW   – zobrazí hladinu KROV_POPIS"
            + "\n  AK_LABELHIDE   – skryje hladinu KROV_POPIS"
            + "\n\nTip: pri priradení, úprave alebo zmene geometrie sa popis obnoví automaticky. AK_LABELS a AK_RECALC môžeš použiť na ručnú kontrolu.");
    }

    [CommandMethod("AK_RIBBON", CommandFlags.Modal)]
    public void ShowRibbon()
    {
        if (AcKrovyRibbon.EnsureCreated(activateTab: true))
        {
            ActiveEditor().WriteMessage("\nACAD KROVY: karta Ribbonu je pripravená.");
            return;
        }

        AcKrovyRibbon.ScheduleCreation();
        ActiveEditor().WriteMessage("\nACAD KROVY: Ribbon ešte nie je pripravený, karta sa pridá o chvíľu.");
    }

    [CommandMethod("AK_TOOLBAR", CommandFlags.Modal)]
    public void ToggleClassicToolbar()
    {
        ClassicToolbarManager.Toggle();
        ActiveEditor().WriteMessage(ClassicToolbarManager.IsVisible
            ? "\nACAD KROVY: klasický panel ikoniek je zobrazený."
            : "\nACAD KROVY: klasický panel ikoniek je skrytý.");
    }

    [CommandMethod("AK_TOOLBARSHOW", CommandFlags.Modal)]
    public void ShowClassicToolbar()
    {
        ClassicToolbarManager.Show();
        ActiveEditor().WriteMessage("\nACAD KROVY: klasický panel ikoniek je zobrazený.");
    }

    [CommandMethod("AK_TOOLBARHIDE", CommandFlags.Modal)]
    public void HideClassicToolbar()
    {
        ClassicToolbarManager.Hide();
        ActiveEditor().WriteMessage("\nACAD KROVY: klasický panel ikoniek je skrytý.");
    }

    [CommandMethod("AK_SETTINGS", CommandFlags.Modal)]
    public void OpenSettings()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var dialog = new LayerSettingsWindow(
            ElementLayerProfileStore.Load(),
            TimberElementDefaultProfileStore.Load());
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Profile is null || dialog.DefaultProfile is null)
        {
            return;
        }

        try
        {
            ElementLayerProfileStore.Save(dialog.Profile);
            TimberElementDefaultProfileStore.Save(dialog.DefaultProfile);
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nACAD KROVY: nepodarilo sa uložiť nastavenia: {ex.Message}");
            return;
        }

        editor.WriteMessage("\nACAD KROVY: nastavenia boli uložené.");
        switch (dialog.CuttingAllowanceApplyMode)
        {
            case CuttingAllowanceApplyMode.AllElements:
                ApplySettingsToExistingElements(document, dialog.Profile, dialog.DefaultProfile, null);
                break;
            case CuttingAllowanceApplyMode.SelectedElements:
                var ids = PromptForEntities(editor, "\nOznač prvky, na ktoré chceš aplikovať nové výrobné prídavky: ");
                if (ids.Count == 0)
                {
                    editor.WriteMessage("\nACAD KROVY: výber bol zrušený, existujúce prvky neboli zmenené.");
                    return;
                }

                ApplySettingsToExistingElements(document, dialog.Profile, dialog.DefaultProfile, ids);
                break;
            case CuttingAllowanceApplyMode.NewElementsOnly:
                if (dialog.ApplyToExistingElements)
                {
                    ApplyLayersToExistingElements(document, dialog.Profile);
                }

                break;
        }
    }

    [CommandMethod("AK_APPLYLAYERS", CommandFlags.Modal)]
    public void ApplyLayers()
    {
        ApplyLayersToExistingElements(ActiveDocument(), ElementLayerProfileStore.Load());
    }

    [CommandMethod("AK_LABELS", CommandFlags.Modal)]
    public void UpdateAllLabels()
    {
        var document = ActiveDocument();
        var result = ElementLabelService.UpdateAll(document.Database, document.Editor);
        document.Editor.WriteMessage(
            $"\nACAD KROVY: popisy obnovené: {result.Processed}. Nové: {result.Created}. Preskočené: {result.Skipped}.");
    }

    [CommandMethod("AK_LABELSELECTED", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void UpdateSelectedLabels()
    {
        var document = ActiveDocument();
        var ids = PromptForEntities(document.Editor, "\nOznač prvky, ktorým chceš vytvoriť alebo obnoviť popisy: ");
        if (ids.Count == 0)
        {
            return;
        }

        var result = ElementLabelService.UpdateSelected(document.Database, document.Editor, ids);
        document.Editor.WriteMessage(
            $"\nACAD KROVY: popisy obnovené: {result.Processed}. Nové: {result.Created}. Preskočené: {result.Skipped}.");
    }

    [CommandMethod("AK_LABELSHOW", CommandFlags.Modal)]
    public void ShowLabels() => SetLabelsVisibility(true);

    [CommandMethod("AK_LABELHIDE", CommandFlags.Modal)]
    public void HideLabels() => SetLabelsVisibility(false);

    [CommandMethod("AK_ASSIGN", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Assign() => AssignWithPresetType(null);

    [CommandMethod("AK_KROKVA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignRafter() => AssignWithPresetType(TimberElementType.Rafter);

    [CommandMethod("AK_POMURNICA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignWallPlate() => AssignWithPresetType(TimberElementType.WallPlate);

    [CommandMethod("AK_VAZNICA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignPurlin() => AssignWithPresetType(TimberElementType.Purlin);

    [CommandMethod("AK_STLPIK", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignPost() => AssignWithPresetType(TimberElementType.Post);

    [CommandMethod("AK_KLIESTINA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignCollarTie() => AssignWithPresetType(TimberElementType.CollarTie);

    [CommandMethod("AK_VZPERA", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignBrace() => AssignWithPresetType(TimberElementType.Brace);

    [CommandMethod("AK_VAZNYTRAM", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AssignTieBeam() => AssignWithPresetType(TimberElementType.TieBeam);

    [CommandMethod("AK_EDIT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Edit()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var ids = PromptForEntities(editor, "\nOznač inteligentné prvky krovu na úpravu: ");
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
            editor.WriteMessage("\nVybraný objekt nemá údaje ACAD KROVY. Najprv použi AK_ASSIGN alebo ikonku typu prvku.");
            return;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var dialog = new ElementEditWindow(
            selectedData[0],
            isNewAssignment: false,
            defaultProfile,
            cuttingAllowanceIsMixed: HasMixedCuttingAllowance(selectedData),
            slopeDirectionIsMixed: HasMixedSlopeDirection(selectedData));
        dialog.Title = selectedData.Count == 1
            ? $"ACAD KROVY – editácia prvku – {selectedData[0].ElementId} – {TimberElementLabels.ToSlovak(selectedData[0].ElementType)}"
            : $"ACAD KROVY – editácia {selectedData.Count} prvkov";
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
        editor.WriteMessage($"\nACAD KROVY: upravené {changed} prvky. Preskočené: {skipped}.");
    }

    [CommandMethod("AK_INSPECT", CommandFlags.Modal)]
    public void Inspect()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var result = editor.GetEntity("\nVyber prvok ACAD KROVY: ");
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
            editor.WriteMessage("\nTento objekt nemá údaje ACAD KROVY.");
            return;
        }

        var data = snapshot.Data;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var measurement = TimberElementMeasurer.Measure(snapshot, roundingStepMm);
        var currentDefaultAllowance = defaultProfile.GetCuttingAllowanceMm(data.ElementType);
        var allowanceSource = Math.Abs(data.CuttingAllowanceMm - currentDefaultAllowance) < 0.000001
            ? "aktuálny default podľa typu"
            : "individuálna hodnota prvku";
        var message =
            $"\n{data.ElementId} | {TimberElementLabels.ToSlovak(data.ElementType)}"
            + $"\n  Prierez: {data.WidthMm:0} × {data.HeightMm:0} mm"
            + $"\n  Pôdorysná dĺžka: {measurement.PlanLengthMm / 1000d:0.###} m"
            + $"\n  Skutočná dĺžka: {measurement.ActualLengthMm / 1000d:0.###} m"
            + $"\n  Rezná dĺžka: {measurement.CuttingLengthMm / 1000d:0.###} m"
            + $"\n  Kubatúra: {measurement.VolumeM3:0.0000} m³";
        var rows = new List<InspectInfoRow>
        {
            new("Označenie", data.ElementId),
            new("Typ prvku", TimberElementLabels.ToSlovak(data.ElementType)),
            new("Materiál", data.Material),
            new("Šírka", $"{data.WidthMm:0} mm"),
            new("Výška", $"{data.HeightMm:0} mm"),
            new("Sklon", $"{data.SlopeDegrees:0.###}°"),
            new("Smer spádu", data.IsSlopeDirectionReversed ? "Obrátený" : "Normálny"),
            new("Pôdorysná dĺžka", $"{measurement.PlanLengthMm:0} mm"),
            new("Skutočná dĺžka", $"{measurement.ActualLengthMm:0} mm"),
            new("Prídavok na prírez", $"{data.CuttingAllowanceMm:0} mm ({allowanceSource})"),
            new("Rezná dĺžka", $"{measurement.CuttingLengthMm:0} mm"),
            new("ManualLengthMode", data.LengthCalculationMode == LengthCalculationMode.ManualLength ? "Áno" : "Nie"),
            new("CAD Handle", entity.Handle.ToString()),
        };
        if (data.ManualLengthMm.HasValue)
        {
            rows.Add(new InspectInfoRow("Manuálna dĺžka", $"{data.ManualLengthMm.Value:0} mm"));
        }

        transaction.Commit();
        editor.WriteMessage(message);
        AcApp.ShowModalWindow(new InspectInfoWindow(rows));
    }

    [CommandMethod("AK_REPORT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ReportFromSelection()
    {
        var document = ActiveDocument();
        var ids = PromptForEntities(document.Editor, "\nOznač prvky pre výkaz: ");
        InsertReport(document, ids);
    }

    [CommandMethod("AK_REPORTALL", CommandFlags.Modal)]
    public void ReportAll()
    {
        var document = ActiveDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var ids = DrawingScanner.FindAllTimberElements(document.Database, transaction, metadataStore);
        transaction.Commit();
        InsertReport(document, ids);
    }

    [CommandMethod("AK_RECALC", CommandFlags.Modal)]
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
                editor.WriteMessage($"\nChyba v prvku {snapshot.Data.ElementId}: {ex.Message}");
            }
        }

        transaction.Commit();
        var labels = ElementLabelService.UpdateAll(document.Database, editor);
        editor.WriteMessage(
            $"\nACAD KROVY: prepočítaných {checkedCount} prvkov, chýb: {errors}. "
            + $"Popisy obnovené: {labels.Processed}. Preskočené: {labels.Skipped}.");
    }

    private static void AssignWithPresetType(TimberElementType? presetType)
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        var message = presetType is null
            ? "\nOznač čiary alebo polyline, ktorým chceš priradiť údaje: "
            : $"\nOznač čiary alebo polyline pre prvok {TimberElementLabels.ToSlovak(presetType.Value)}: ";
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
        editor.WriteMessage($"\nACAD KROVY: priradené údaje k {assigned} prvkom. Preskočené: {skipped}.");
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
                editor.WriteMessage($"\nPreskočený prvok pri nastavovaní hladiny: {ex.Message}");
            }
        }

        transaction.Commit();
        editor.WriteMessage($"\nACAD KROVY: prvky presunuté na hladiny: {updated}. Preskočené: {skipped}.");
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
                editor.WriteMessage($"\nPreskočený prvok pri aplikovaní nastavení: {ex.Message}");
            }
        }

        UpdateLabelsForChangedEntities(
            document.Database,
            transaction,
            metadataStore,
            changedIds,
            previousElementIdById);

        transaction.Commit();
        editor.WriteMessage(
            $"\nACAD KROVY: výrobné prídavky aplikované na {updated} prvkov. Preskočené: {skipped}.");
    }

    private static void SetLabelsVisibility(bool visible)
    {
        var document = ActiveDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var changed = ElementLabelService.SetVisible(document.Database, transaction, visible);
        transaction.Commit();

        document.Editor.WriteMessage(changed
            ? visible
                ? "\nACAD KROVY: popisy na hladine KROV_POPIS sú zobrazené."
                : "\nACAD KROVY: popisy na hladine KROV_POPIS sú skryté."
            : "\nACAD KROVY: hladina KROV_POPIS ešte vo výkrese neexistuje.");
    }

    private static void InsertReport(Document document, IReadOnlyList<ObjectId> ids)
    {
        var editor = document.Editor;
        if (ids.Count == 0)
        {
            editor.WriteMessage("\nNenašli sa žiadne prvky ACAD KROVY.");
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
                editor.WriteMessage($"\nPreskočený prvok {snapshot.Data.ElementId}: {ex.Message}");
            }
        }

        if (measurements.Count == 0)
        {
            editor.WriteMessage("\nVo výbere nie sú platné prvky ACAD KROVY.");
            return;
        }

        var pointResult = editor.GetPoint("\nZadaj miesto vloženia výkazu: ");
        if (pointResult.Status != PromptStatus.OK)
        {
            return;
        }

        var report = TimberReportBuilder.Build(measurements);
        ReportTableWriter.Insert(document.Database, transaction, pointResult.Value, report);
        transaction.Commit();
        editor.WriteMessage($"\nACAD KROVY: vložený výkaz z {measurements.Count} prvkov. Preskočené: {skipped}. Kubatúra: {report.TotalVolumeM3:0.0000} m³.");
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
            MessageForRemoval = "\nOdober z výberu: ",
            AllowDuplicates = false,
        };

        var selection = editor.GetSelection(options);
        return selection.Status == PromptStatus.OK && selection.Value is not null
            ? selection.Value.GetObjectIds()
            : Array.Empty<ObjectId>();
    }

    private static Document ActiveDocument() => AcApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Nie je otvorený žiadny výkres AutoCADu.");

    private static Editor ActiveEditor() => ActiveDocument().Editor;
}
