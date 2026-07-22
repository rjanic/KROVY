using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class PostFootprintAssignmentWorkflow
{
    public static bool Run(
        Document document,
        TimberElementPatch? requestedPatch = null,
        bool cuttingAllowanceWasEdited = false,
        bool useDefaultCuttingAllowanceByType = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!PostFootprintSelectionService.TryPrompt(document, out var selection) || selection is null)
        {
            return false;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        TimberElementData? existingData;
        using (var readTransaction = document.Database.TransactionManager.StartTransaction())
        {
            var readMetadataStore = new AutoCadTimberElementMetadataStore(readTransaction);
            var entity = readTransaction.GetObject(selection.SelectedEntityId, OpenMode.ForRead) as Entity;
            existingData = !selection.RequiresLineConversion &&
                entity is not null && readMetadataStore.TryRead(entity, out var data)
                ? data
                : null;
            readTransaction.Commit();
        }

        var postDefaults = TimberElementDefaults.For(TimberElementType.Post, defaultProfile) with
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = TimberPostFootprintAssignmentRules.DefaultManualLengthMm,
        };
        var source = TimberPostFootprintAssignmentRules.CreateMetadata(
            existingData ?? postDefaults,
            selection.Dimensions);
        var patch = requestedPatch;
        if (patch is null)
        {
            var dialog = new ElementEditWindow(source, isNewAssignment: true, defaultProfile);
            if (AcApp.ShowModalWindow(dialog) != true || dialog.Patch is null)
            {
                return false;
            }

            patch = dialog.Patch;
            cuttingAllowanceWasEdited = dialog.CuttingAllowanceWasEdited;
            useDefaultCuttingAllowanceByType = dialog.UseDefaultCuttingAllowanceByType;
        }

        var effectivePatch = existingData is not null && !cuttingAllowanceWasEdited
            ? patch with { CuttingAllowanceMm = null }
            : patch;
        var merged = TimberElementPatcher.Apply(source, effectivePatch);
        if (useDefaultCuttingAllowanceByType)
        {
            merged = TimberElementDefaultApplicator.ApplyCuttingAllowance(merged, defaultProfile);
        }

        merged = TimberPostFootprintAssignmentRules.CreateMetadata(merged, selection.Dimensions);

        using var transaction = document.Database.TransactionManager.StartTransaction();
        Polyline polyline;
        if (selection.RequiresLineConversion)
        {
            var conversion = PostFootprintLineConversionService.CreatePolyline(
                document.Database,
                transaction,
                selection);
            polyline = conversion.Polyline;
            merged = TimberPostFootprintAssignmentRules.CreateMetadata(merged, conversion.Dimensions);
        }
        else if (transaction.GetObject(selection.SelectedEntityId, OpenMode.ForWrite) is Polyline existingPolyline)
        {
            polyline = existingPolyline;
        }
        else
        {
            return false;
        }

        var targetId = polyline.ObjectId;

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        metadataStore.Write(polyline, merged);
        var layerService = new AutoCadTimberLayerService(document.Database, transaction);
        layerService.ApplyLayerForTimberType(
            polyline,
            TimberElementType.Post,
            ElementLayerProfileStore.Load());
        var synchronized = TimberElementItemIdentityService.SynchronizeElementIds(
            document.Database,
            transaction,
            metadataStore,
            new[] { targetId },
            defaultProfile.GetCuttingLengthRoundingStepMm());
        var assigned = synchronized.TryGetValue(targetId, out var finalData)
            ? finalData
            : merged;
        TimberAnnotationService.EnsureForElement(
            document.Database,
            transaction,
            polyline,
            assigned,
            roundingStepMm: defaultProfile.GetCuttingLengthRoundingStepMm());
        if (selection.RequiresLineConversion)
        {
            PostFootprintLineConversionService.EraseSourceLines(
                transaction,
                selection.OrderedSourceLineIds);
        }

        transaction.Commit();

        document.Editor.WriteMessage(UiStrings.Format(
            UiStrings.CommandPostFootprintAssignedFormat,
            assigned.WidthMm,
            assigned.HeightMm,
            assigned.ManualLengthMm ?? TimberPostFootprintAssignmentRules.DefaultManualLengthMm));
        return true;
    }
}
