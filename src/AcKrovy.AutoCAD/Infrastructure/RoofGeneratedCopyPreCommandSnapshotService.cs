using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Pre-command generated-timber handles for native COPY (individual-member detach).
/// </summary>
internal static class RoofGeneratedCopyPreCommandSnapshotService
{
    private static readonly object Gate = new();
    private static HashSet<string> _generatedHandles = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, HashSet<string>> _logicalKeysByOwner = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
        lock (Gate)
        {
            _generatedHandles.Clear();
            _logicalKeysByOwner.Clear();
        }
    }

    public static void CaptureForCopy(Document document)
    {
        Clear();
        ArgumentNullException.ThrowIfNull(document);
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keysByOwner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Polyline owner ||
                owner.IsErased ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                continue;
            }

            var ownerHandle = owner.Handle.ToString();
            foreach (var timberId in RoofGeneratedTimberStore.FindByOwner(
                         document.Database,
                         transaction,
                         ownerHandle))
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        timberId,
                        OpenMode.ForRead,
                        out var line,
                        document.Database) ||
                    line is null)
                {
                    continue;
                }

                var generated = RoofGeneratedTimberStore.Read(line);
                if (generated.Data is null ||
                    generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
                    !metadataStore.TryRead(line, out var timber) ||
                    timber is null ||
                    timber.ElementType != TimberElementType.Rafter)
                {
                    continue;
                }

                var handle = line.Handle.ToString();
                handles.Add(handle);
                if (!keysByOwner.TryGetValue(ownerHandle, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    keysByOwner[ownerHandle] = keys;
                }

                keys.Add(RoofGeneratedRafterCopyDetachRules.FormatLogicalKey(
                    generated.Data.RoofFace,
                    generated.Data.StationIndex));
            }
        }

        transaction.Commit();
        lock (Gate)
        {
            _generatedHandles = handles;
            _logicalKeysByOwner = keysByOwner;
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandGeneratedHandles()
    {
        lock (Gate)
        {
            return _generatedHandles.ToArray();
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetPreCommandLogicalKeysByOwner()
    {
        lock (Gate)
        {
            return _logicalKeysByOwner.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
