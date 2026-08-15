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
        Action<Line, Transaction, int>? writeSecondaryMetadata = null)
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
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var line = new Line(request.Start, request.End);
            var id = modelSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
            metadataStore.Write(line, request.Data);
            layerService.ApplyLayerForTimberType(
                line,
                request.Data.ElementType,
                layerProfile,
                CadLayerUpdateMode.PreserveExisting);
            writeSecondaryMetadata?.Invoke(line, transaction, index);
            createdIds.Add(id);
        }

        return TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            createdIds,
            defaultProfile.GetCuttingLengthRoundingStepMm());
    }
}

internal sealed record TimberSourceLineCreationRequest(
    Point3d Start,
    Point3d End,
    TimberElementData Data);
