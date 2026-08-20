using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Reconstructible unlocked-roof padlock as one BlockReference on a non-plot UI layer.
/// Not a GROUP member and not timber/report metadata.
/// </summary>
internal static class RoofUnlockIndicatorService
{
    internal const string LayerName = "KROV_ROOF_UI";
    internal const string BlockName = "KROV_ROOF_UNLOCK_ICON";
    internal const int LayerColorIndex = 8;
    internal const double IconCenterX = 0.50d;
    internal const double IconCenterY = 0.485d;
    private const double BaseSizeMm = 350d;

    private static readonly Point2d[] BodyUnits =
    [
        new(0.20, 0.05),
        new(0.80, 0.05),
        new(0.80, 0.62),
        new(0.20, 0.62),
    ];

    private static readonly Point2d[] ShackleUnits =
    [
        new(0.30, 0.62),
        new(0.30, 0.92),
        new(0.70, 0.92),
        new(0.70, 0.78),
    ];

    public static void Sync(
        Database database,
        Transaction transaction,
        Polyline owner)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        var ownerReference = owner.Handle.ToString();
        EraseExisting(database, transaction, ownerReference);
        var stored = RoofDefinitionStore.Read(owner);
        if (stored.Data is null || stored.Data.EditState != RoofEditState.Unlocked)
        {
            return;
        }

        var origin = ResolveOrigin(owner);
        var size = ResolveSize(database, transaction);
        CreateSymbol(database, transaction, origin, size, ownerReference);
    }

    public static bool RebuildUnlockedOwners(Database database, Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        var hadIndicators = EraseExisting(database, transaction, ownerReference: null);
        var ownerIds = new List<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var polyline,
                    database) ||
                polyline is null ||
                RoofDefinitionStore.Read(polyline).Data is null)
            {
                continue;
            }

            ownerIds.Add(id);
        }

        var wrote = hadIndicators;
        foreach (var ownerId in ownerIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    database) ||
                owner is null ||
                RoofDefinitionStore.Read(owner).Data?.EditState != RoofEditState.Unlocked)
            {
                continue;
            }

            Sync(database, transaction, owner);
            wrote = true;
        }

        return wrote;
    }

    public static void SyncOwnerId(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            return;
        }

        Sync(database, transaction, owner);
    }

    private static bool EraseExisting(
        Database database,
        Transaction transaction,
        string? ownerReference)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var erase = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            var storedOwner = RoofUnlockIndicatorStore.TryReadOwnerReference(entity);
            if (string.IsNullOrWhiteSpace(storedOwner))
            {
                continue;
            }

            if (ownerReference is null ||
                string.Equals(storedOwner, ownerReference, StringComparison.OrdinalIgnoreCase))
            {
                erase.Add(id);
            }
        }

        foreach (var id in erase)
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) &&
                entity is not null &&
                !entity.IsErased)
            {
                entity.Erase();
            }
        }

        return erase.Count > 0;
    }

    private static Point3d ResolveOrigin(Polyline owner)
    {
        var elevation = RoofPolylineExtractor.GetSourceElevation(owner);
        if (owner.NumberOfVertices < 1)
        {
            return new Point3d(0d, 0d, elevation);
        }

        var first = owner.GetPoint3dAt(0);
        var second = owner.GetPoint3dAt(Math.Min(1, owner.NumberOfVertices - 1));

        var centroidX = 0d;
        var centroidY = 0d;
        var count = Math.Min(owner.NumberOfVertices, 4);
        for (var i = 0; i < count; i++)
        {
            var p = owner.GetPoint3dAt(i);
            centroidX += p.X;
            centroidY += p.Y;
        }

        centroidX /= count;
        centroidY /= count;
        var dx = first.X - centroidX;
        var dy = first.Y - centroidY;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 1e-6d)
        {
            dx = second.X - first.X;
            dy = second.Y - first.Y;
            length = Math.Sqrt(dx * dx + dy * dy);
        }

        if (length <= 1e-6d)
        {
            return new Point3d(first.X, first.Y, elevation);
        }

        dx /= length;
        dy /= length;
        var size = 400d;
        return new Point3d(first.X + dx * size, first.Y + dy * size, elevation);
    }

    private static double ResolveSize(Database database, Transaction transaction)
    {
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var scale = AutoCadAnnotationScaleService.Create(database, transaction, defaultProfile);
        return scale.Context.ScaleLength(BaseSizeMm);
    }

    private static void CreateSymbol(
        Database database,
        Transaction transaction,
        Point3d origin,
        double size,
        string ownerReference)
    {
        var blockId = EnsureBlockDefinition(database, transaction);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var insertion = new Point3d(
            origin.X + (IconCenterX * size),
            origin.Y + (IconCenterY * size),
            origin.Z);
        var reference = new BlockReference(insertion, blockId)
        {
            ScaleFactors = new Scale3d(size),
            Rotation = 0d,
        };
        reference.SetDatabaseDefaults(database);
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            reference,
            LayerName,
            LayerColorIndex,
            isPlottable: false);
        reference.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        reference.Transparency = new Transparency(70);
        modelSpace.AppendEntity(reference);
        transaction.AddNewlyCreatedDBObject(reference, true);
        RoofUnlockIndicatorStore.Write(reference, transaction, ownerReference);
    }

    private static ObjectId EnsureBlockDefinition(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        if (blockTable.Has(BlockName))
        {
            return blockTable[BlockName];
        }

        blockTable.UpgradeOpen();
        var definition = new BlockTableRecord
        {
            Name = BlockName,
            Origin = Point3d.Origin,
        };
        var definitionId = blockTable.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);
        AppendDefinitionPolyline(database, transaction, definition, BodyUnits, closed: true);
        AppendDefinitionPolyline(database, transaction, definition, ShackleUnits, closed: false);
        return definitionId;
    }

    private static void AppendDefinitionPolyline(
        Database database,
        Transaction transaction,
        BlockTableRecord definition,
        IReadOnlyList<Point2d> units,
        bool closed)
    {
        var polyline = new Polyline(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            polyline.AddVertexAt(
                i,
                new Point2d(units[i].X - IconCenterX, units[i].Y - IconCenterY),
                0d,
                0d,
                0d);
        }

        polyline.Closed = closed;
        polyline.Elevation = 0d;
        polyline.ConstantWidth = 0.04d;
        polyline.SetDatabaseDefaults(database);
        polyline.Layer = "0";
        polyline.Color = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        polyline.LineWeight = LineWeight.ByBlock;
        definition.AppendEntity(polyline);
        transaction.AddNewlyCreatedDBObject(polyline, true);
    }
}
