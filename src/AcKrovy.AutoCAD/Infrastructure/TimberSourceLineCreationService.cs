using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Canonical source-only intelligent timber creation path. It reuses production
/// defaults, timber XData, layer hydration and item identity without annotations.
/// </summary>
internal static class TimberSourceLineCreationService
{
    public static IReadOnlyDictionary<ObjectId, TimberElementData> Create(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<TimberSourceLineCreationRequest> requests,
        TimberElementDefaultProfile defaultProfile,
        ElementLayerProfile layerProfile,
        Func<Line, Transaction, int, IReadOnlyList<TypedValue>?>? writeSecondaryMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var layerService = new AutoCadTimberLayerService(database, transaction, editor);
        var createdIds = new List<ObjectId>(requests.Count);

        // For Generated timber, pre-compute the final ElementId before the single atomic
        // XData assignment so the SynchronizeElementIds pass (which runs over the whole
        // drawing) finds no change and therefore performs no second Entity.XData write.
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        IReadOnlyList<string>? finalElementIds = null;
        if (writeSecondaryMetadata is not null)
        {
            var newSnapshots = requests
                .Select(request => new TimberElementSnapshot(
                    request.Data,
                    request.Start.DistanceTo(request.End)))
                .ToList();
            finalElementIds = TimberElementItemIdentityService.ComputeFinalElementIds(
                database,
                transaction,
                metadataStore,
                newSnapshots,
                roundingStepMm);
        }

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var effectiveData = finalElementIds is not null
                ? request.Data with { ElementId = finalElementIds[index] }
                : request.Data;
            var line = new Line(request.Start, request.End);
            var id = modelSpace.AppendEntity(line);
            var secondarySection = writeSecondaryMetadata?.Invoke(line, transaction, index);
            if (secondarySection is null || secondarySection.Count == 0)
            {
                // Ordinary (non-Generated) timber: unchanged order.
                transaction.AddNewlyCreatedDBObject(line, true);
                metadataStore.Write(line, effectiveData);
            }
            else
            {
                // Generated timber: AppendEntity -> WriteAtomic -> AddNewlyCreatedDBObject
                // (probe timing T2). Writing XData BEFORE AddNewlyCreatedDBObject is the
                // timing proven to survive native STRETCH -> U -> REDO; the T3 order
                // (AddNewlyCreatedDBObject then XData) dropped the secondary RegApp.
                RoofGeneratedTimberStore.WriteAtomic(
                    line,
                    transaction,
                    effectiveData,
                    secondarySection);
                transaction.AddNewlyCreatedDBObject(line, true);
            }

            layerService.ApplyLayerForTimberType(
                line,
                effectiveData.ElementType,
                layerProfile,
                CadLayerUpdateMode.PreserveExisting);
            createdIds.Add(id);
        }

        var synchronizedDataById =
            TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            createdIds,
            roundingStepMm);
#if DEBUG
        RoofGeneratedPostAtomicWriteDiag.EmitSummary(createdIds.Count);
#endif
        return createdIds.ToDictionary(
            id => id,
            id => synchronizedDataById[id]);
    }
}

internal sealed record TimberSourceLineCreationRequest(
    Point3d Start,
    Point3d End,
    TimberElementData Data);
