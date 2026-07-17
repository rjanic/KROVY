using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
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
            "\nACAD KROVY 0.6.0"
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
            + "\n\nTip: pri priradení alebo úprave sa popis vytvorí automaticky. Po ručnej zmene dĺžky čiary použi AK_LABELS alebo AK_RECALC.");
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
        var dialog = new LayerSettingsWindow(ElementLayerProfileStore.Load());
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Profile is null)
        {
            return;
        }

        try
        {
            ElementLayerProfileStore.Save(dialog.Profile);
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nACAD KROVY: nepodarilo sa uložiť nastavenia hladín: {ex.Message}");
            return;
        }

        editor.WriteMessage("\nACAD KROVY: nastavenia hladín boli uložené.");
        if (dialog.ApplyToExistingElements)
        {
            ApplyLayersToExistingElements(document, dialog.Profile);
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
        var firstData = ids
            .Select(id => readTransaction.GetObject(id, OpenMode.ForRead) as Entity)
            .Where(entity => entity is not null)
            .Select(entity => ElementDataStore.TryRead(entity!, readTransaction, out var data) ? data : null)
            .FirstOrDefault(data => data is not null);
        readTransaction.Commit();

        if (firstData is null)
        {
            editor.WriteMessage("\nVybraný objekt nemá údaje ACAD KROVY. Najprv použi AK_ASSIGN alebo ikonku typu prvku.");
            return;
        }

        var dialog = new ElementEditWindow(firstData, isNewAssignment: false);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var changed = 0;
        var skipped = 0;

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                !ElementDataStore.TryRead(entity, transaction, out var original) ||
                original is null)
            {
                skipped++;
                continue;
            }

            var merged = TimberElementPatcher.Apply(original, dialog.Patch);
            ElementDataStore.Write(entity, transaction, merged);
            TimberLayerService.ApplyToEntity(document.Database, transaction, entity, merged.ElementType, layerProfile);
            ElementLabelService.UpsertForElement(document.Database, transaction, entity, merged);
            changed++;
        }

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
        if (transaction.GetObject(result.ObjectId, OpenMode.ForRead) is not Entity entity ||
            !AutoCadEntityReader.TryReadTimberElement(entity, transaction, out var snapshot) ||
            snapshot is null)
        {
            editor.WriteMessage("\nTento objekt nemá údaje ACAD KROVY.");
            return;
        }

        var data = snapshot.Data;
        var measurement = TimberElementMeasurer.Measure(snapshot);
        editor.WriteMessage(
            $"\n{data.ElementId} | {TimberElementLabels.ToSlovak(data.ElementType)}"
            + $"\n  Prierez: {data.WidthMm:0} × {data.HeightMm:0} mm"
            + $"\n  Pôdorysná dĺžka: {measurement.PlanLengthMm / 1000d:0.###} m"
            + $"\n  Skutočná dĺžka: {measurement.ActualLengthMm / 1000d:0.###} m"
            + $"\n  Rezná dĺžka: {measurement.CuttingLengthMm / 1000d:0.###} m"
            + $"\n  Kubatúra: {measurement.VolumeM3:0.0000} m³");
        transaction.Commit();
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
        var ids = DrawingScanner.FindAllTimberElements(document.Database, transaction);
        transaction.Commit();
        InsertReport(document, ids);
    }

    [CommandMethod("AK_RECALC", CommandFlags.Modal)]
    public void RecalculateAll()
    {
        var document = ActiveDocument();
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var checkedCount = 0;
        var errors = 0;

        foreach (var id in DrawingScanner.FindAllTimberElements(document.Database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !AutoCadEntityReader.TryReadTimberElement(entity, transaction, out var snapshot) ||
                snapshot is null)
            {
                continue;
            }

            try
            {
                _ = TimberElementMeasurer.Measure(snapshot);
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
        var seedData = presetType is { } elementType
            ? TimberElementDefaults.For(elementType)
            : new TimberElementData();
        var dialog = new ElementEditWindow(seedData, isNewAssignment: true);
        if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
        {
            return;
        }

        var layerProfile = ElementLayerProfileStore.Load();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var assigned = 0;
        var skipped = 0;
        var nextNumberByType = new Dictionary<TimberElementType, int>();

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity || !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                skipped++;
                continue;
            }

            var original = ElementDataStore.TryRead(entity, transaction, out var existing) && existing is not null
                ? existing
                : new TimberElementData();

            var merged = TimberElementPatcher.Apply(original, dialog.Patch);
            if (string.IsNullOrWhiteSpace(merged.ElementId))
            {
                if (!nextNumberByType.TryGetValue(merged.ElementType, out var number))
                {
                    number = ElementNumberingService.GetNextNumber(document.Database, transaction, merged.ElementType);
                }

                merged = merged with { ElementId = $"{TimberElementLabels.Prefix(merged.ElementType)}{number}" };
                nextNumberByType[merged.ElementType] = number + 1;
            }

            ElementDataStore.Write(entity, transaction, merged);
            TimberLayerService.ApplyToEntity(document.Database, transaction, entity, merged.ElementType, layerProfile);
            ElementLabelService.UpsertForElement(document.Database, transaction, entity, merged);
            assigned++;
        }

        transaction.Commit();
        editor.WriteMessage($"\nACAD KROVY: priradené údaje k {assigned} prvkom. Preskočené: {skipped}.");
    }

    private static void ApplyLayersToExistingElements(Document document, ElementLayerProfile profile)
    {
        var editor = document.Editor;
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var updated = 0;
        var skipped = 0;

        foreach (var id in DrawingScanner.FindAllTimberElements(document.Database, transaction))
        {
            try
            {
                if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !ElementDataStore.TryRead(entity, transaction, out var data) ||
                    data is null)
                {
                    skipped++;
                    continue;
                }

                TimberLayerService.ApplyToEntity(document.Database, transaction, entity, data.ElementType, profile);
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
        var measurements = new List<TimberElementMeasurement>();
        var skipped = 0;

        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !AutoCadEntityReader.TryReadTimberElement(entity, transaction, out var snapshot) ||
                snapshot is null)
            {
                skipped++;
                continue;
            }

            try
            {
                measurements.Add(TimberElementMeasurer.Measure(snapshot));
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
