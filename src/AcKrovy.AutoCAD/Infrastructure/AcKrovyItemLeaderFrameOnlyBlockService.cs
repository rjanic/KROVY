using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadItemLeaderFrameOnlyBlockResultKind
{
    ReusedExisting,
    ReusedCollisionVariant,
    CreatedNew,
    CreatedCollisionVariant,
    InvalidRequest,
    DatabaseMismatch,
    ExistingDefinitionInvalid,
}

internal sealed record AutoCadItemLeaderFrameOnlyBlockResult
{
    public AutoCadItemLeaderFrameOnlyBlockResultKind Kind { get; }
    public AutoCadItemLeaderFrameOnlyBlockKey? VariantKey { get; }
    public string? CanonicalBlockName { get; }
    public string? ResolvedBlockName { get; }
    public ObjectId? BlockTableRecordId { get; }
    public AutoCadDatabaseIdentityToken? DatabaseIdentity { get; }
    public string Diagnostic { get; }

    public bool Succeeded =>
        Kind is
            AutoCadItemLeaderFrameOnlyBlockResultKind.ReusedExisting or
            AutoCadItemLeaderFrameOnlyBlockResultKind.ReusedCollisionVariant or
            AutoCadItemLeaderFrameOnlyBlockResultKind.CreatedNew or
            AutoCadItemLeaderFrameOnlyBlockResultKind.CreatedCollisionVariant;

    private AutoCadItemLeaderFrameOnlyBlockResult(
        AutoCadItemLeaderFrameOnlyBlockResultKind kind,
        AutoCadItemLeaderFrameOnlyBlockKey? variantKey,
        string? canonicalBlockName,
        string? resolvedBlockName,
        ObjectId? blockTableRecordId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string diagnostic)
    {
        Kind = kind;
        VariantKey = variantKey;
        CanonicalBlockName = canonicalBlockName;
        ResolvedBlockName = resolvedBlockName;
        BlockTableRecordId = blockTableRecordId;
        DatabaseIdentity = databaseIdentity;
        Diagnostic = diagnostic ?? string.Empty;
    }

    public static AutoCadItemLeaderFrameOnlyBlockResult Reused(
        AutoCadItemLeaderFrameOnlyBlockKey key,
        string canonicalName,
        string resolvedName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        bool collision,
        string diagnostic) =>
        new(
            collision
                ? AutoCadItemLeaderFrameOnlyBlockResultKind.ReusedCollisionVariant
                : AutoCadItemLeaderFrameOnlyBlockResultKind.ReusedExisting,
            key,
            canonicalName,
            resolvedName,
            blockId,
            databaseIdentity,
            diagnostic);

    public static AutoCadItemLeaderFrameOnlyBlockResult Created(
        AutoCadItemLeaderFrameOnlyBlockKey key,
        string canonicalName,
        string resolvedName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        bool collision,
        string diagnostic) =>
        new(
            collision
                ? AutoCadItemLeaderFrameOnlyBlockResultKind.CreatedCollisionVariant
                : AutoCadItemLeaderFrameOnlyBlockResultKind.CreatedNew,
            key,
            canonicalName,
            resolvedName,
            blockId,
            databaseIdentity,
            diagnostic);

    public static AutoCadItemLeaderFrameOnlyBlockResult InvalidRequest(
        AutoCadItemLeaderFrameOnlyBlockKey? key,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string diagnostic) =>
        new(
            AutoCadItemLeaderFrameOnlyBlockResultKind.InvalidRequest,
            key,
            key is null
                ? null
                : AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(key),
            null,
            null,
            databaseIdentity,
            diagnostic);

    public static AutoCadItemLeaderFrameOnlyBlockResult DatabaseMismatch(
        AutoCadItemLeaderFrameOnlyBlockKey? key,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string diagnostic) =>
        new(
            AutoCadItemLeaderFrameOnlyBlockResultKind.DatabaseMismatch,
            key,
            key is null
                ? null
                : AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(key),
            null,
            null,
            databaseIdentity,
            diagnostic);

    public static AutoCadItemLeaderFrameOnlyBlockResult ExistingDefinitionInvalid(
        AutoCadItemLeaderFrameOnlyBlockKey key,
        string canonicalName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string diagnostic) =>
        new(
            AutoCadItemLeaderFrameOnlyBlockResultKind.ExistingDefinitionInvalid,
            key,
            canonicalName,
            null,
            null,
            databaseIdentity,
            diagnostic);
}

/// <summary>
/// Ensures shared G4 frame-only block definitions (geometry, no AttrDef).
/// Never mutates G2/G3 definitions.
/// </summary>
internal static class AcKrovyItemLeaderFrameOnlyBlockService
{
    private const double GeometryTolerance = 0.001d;
    private const int MaximumCollisionAttempts = 64;

    public static AutoCadItemLeaderFrameOnlyBlockResult Ensure(
        Database database,
        Transaction transaction,
        ItemNumberLeaderStyle style,
        string itemText,
        AutoCadItemLeaderFrameOnlyBlockBatchCatalog? batchCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        if (ItemNumberLeaderStyleRules.Normalize(style) is
            ItemNumberLeaderStyle.Plain)
        {
            return AutoCadItemLeaderFrameOnlyBlockResult.InvalidRequest(
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Plain item leaders do not use a G4 frame-only block.");
        }

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);
        var key = AutoCadItemLeaderFrameOnlyBlockKey.FromDefinition(definition);
        return Ensure(database, transaction, definition, key, batchCatalog);
    }

    public static AutoCadItemLeaderFrameOnlyBlockResult Ensure(
        Database database,
        Transaction transaction,
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderFrameOnlyBlockKey key,
        AutoCadItemLeaderFrameOnlyBlockBatchCatalog? batchCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(key);

        var databaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database);
        var catalog = batchCatalog ??
            new AutoCadItemLeaderFrameOnlyBlockBatchCatalog(database);
        if (!ReferenceEquals(catalog.Database, database))
        {
            return AutoCadItemLeaderFrameOnlyBlockResult.DatabaseMismatch(
                key,
                databaseIdentity,
                "G4 frame batch catalog belongs to a different database.");
        }

        if (catalog.TryGet(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var canonicalName =
            AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(key);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);

        if (blockTable.Has(canonicalName))
        {
            var existingId = blockTable[canonicalName];
            if (ValidateFrameOnlyDefinition(
                    database,
                    transaction,
                    existingId,
                    definition,
                    out var reason))
            {
                var reused = AutoCadItemLeaderFrameOnlyBlockResult.Reused(
                    key,
                    canonicalName,
                    canonicalName,
                    existingId,
                    databaseIdentity,
                    collision: false,
                    "Reused matching G4 frame-only definition.");
                catalog.Add(key, existingId, canonicalName, collision: false);
                return reused;
            }

            // Occupied by invalid content: create collision name. Never mutate.
            for (var attempt = 1; attempt <= MaximumCollisionAttempts; attempt++)
            {
                var collisionName =
                    AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCollisionName(
                        key,
                        attempt);
                if (blockTable.Has(collisionName))
                {
                    var collisionId = blockTable[collisionName];
                    if (ValidateFrameOnlyDefinition(
                            database,
                            transaction,
                            collisionId,
                            definition,
                            out _))
                    {
                        var reusedCollision =
                            AutoCadItemLeaderFrameOnlyBlockResult.Reused(
                                key,
                                canonicalName,
                                collisionName,
                                collisionId,
                                databaseIdentity,
                                collision: true,
                                "Reused matching G4 frame-only collision definition.");
                        catalog.Add(
                            key,
                            collisionId,
                            collisionName,
                            collision: true);
                        return reusedCollision;
                    }

                    continue;
                }

                return CreateAndCache(
                    database,
                    transaction,
                    definition,
                    key,
                    canonicalName,
                    collisionName,
                    catalog,
                    collision: true);
            }

            return AutoCadItemLeaderFrameOnlyBlockResult.ExistingDefinitionInvalid(
                key,
                canonicalName,
                databaseIdentity,
                $"Canonical G4 frame definition was invalid ({reason}) and all " +
                    "deterministic collision names were occupied by invalid content.");
        }

        return CreateAndCache(
            database,
            transaction,
            definition,
            key,
            canonicalName,
            canonicalName,
            catalog,
            collision: false);
    }

    private static AutoCadItemLeaderFrameOnlyBlockResult CreateAndCache(
        Database database,
        Transaction transaction,
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderFrameOnlyBlockKey key,
        string canonicalName,
        string resolvedName,
        AutoCadItemLeaderFrameOnlyBlockBatchCatalog batchCatalog,
        bool collision)
    {
        var writableBlockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForWrite);
        var block = new BlockTableRecord
        {
            Name = resolvedName,
            Origin = Point3d.Origin,
            Annotative = AnnotativeStates.False,
            BlockScaling = BlockScaling.Uniform,
        };
        var blockId = writableBlockTable.Add(block);
        transaction.AddNewlyCreatedDBObject(block, true);

        AcKrovyItemLeaderBlockService.AddFrameGeometry(
            database,
            transaction,
            block,
            definition);

        if (!ValidateFrameOnlyDefinition(
                database,
                transaction,
                blockId,
                definition,
                out var validationReason))
        {
            throw new InvalidOperationException(
                "New G4 frame-only definition failed validation: " +
                validationReason);
        }

        batchCatalog.Add(key, blockId, resolvedName, collision);
        return AutoCadItemLeaderFrameOnlyBlockResult.Created(
            key,
            canonicalName,
            resolvedName,
            blockId,
            batchCatalog.DatabaseIdentity,
            collision,
            collision
                ? "Created a deterministic G4 frame collision variant without mutating the occupied canonical definition."
                : "Created and validated a new G4 frame-only block.");
    }

    internal static bool ValidateFrameOnlyDefinition(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        TimberItemLeaderBlockDefinition definition,
        out string reason)
    {
        reason = string.Empty;
        if (!AutoCadDatabaseIdentity.IsSame(database, blockId))
        {
            reason = "Block ObjectId does not belong to the current Database.";
            return false;
        }

        if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            reason = "Block definition is unavailable or erased.";
            return false;
        }

        if (block.IsDynamicBlock ||
            block.Annotative != AnnotativeStates.False ||
            block.BlockScaling != BlockScaling.Uniform ||
            block.Origin.DistanceTo(Point3d.Origin) > GeometryTolerance)
        {
            reason = "Block flags do not match the G4 frame-only contract.";
            return false;
        }

        var entities = new List<Entity>();
        foreach (ObjectId id in block)
        {
            if (id.IsValid &&
                transaction.GetObject(id, OpenMode.ForRead, true) is Entity entity &&
                !entity.IsErased)
            {
                entities.Add(entity);
            }
        }

        if (entities.OfType<AttributeDefinition>().Any())
        {
            reason = "G4 frame-only definitions must not contain AttributeDefinition.";
            return false;
        }

        var frames = entities.Where(entity => entity is not AttributeDefinition).ToArray();
        if (frames.Length != 1)
        {
            reason = "G4 frame-only definitions must contain exactly one frame entity.";
            return false;
        }

        var frame = frames[0];
        return definition.Style switch
        {
            ItemNumberLeaderStyle.Circle when frame is Circle circle =>
                Accept(
                    TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(
                        circle.Radius * 2d),
                    "Circle diameter mismatch.",
                    out reason),
            ItemNumberLeaderStyle.Slot when frame is Polyline slot =>
                Accept(
                    slot.Closed &&
                    slot.NumberOfVertices == 4 &&
                    HasExpectedExtents(slot, definition) &&
                    Math.Abs(slot.GetBulgeAt(1) - 1d) <= 1e-9 &&
                    Math.Abs(slot.GetBulgeAt(3) - 1d) <= 1e-9,
                    "Slot geometry mismatch.",
                    out reason),
            ItemNumberLeaderStyle.Rectangle when frame is Polyline rectangle =>
                Accept(
                    rectangle.Closed &&
                    rectangle.NumberOfVertices == 4 &&
                    HasExpectedExtents(rectangle, definition) &&
                    Enumerable.Range(0, 4).All(
                        index => Math.Abs(rectangle.GetBulgeAt(index)) <= 1e-9),
                    "Rectangle geometry mismatch.",
                    out reason),
            _ => Accept(false, "Unsupported or mismatched frame entity type.", out reason),
        };
    }

    private static bool Accept(bool ok, string failureReason, out string reason)
    {
        reason = ok ? string.Empty : failureReason;
        return ok;
    }

    private static bool HasExpectedExtents(
        Entity entity,
        TimberItemLeaderBlockDefinition definition)
    {
        var extents = entity.GeometricExtents;
        return
            Math.Abs(
                extents.MaxPoint.X -
                extents.MinPoint.X -
                definition.WidthMm) <= GeometryTolerance &&
            Math.Abs(
                extents.MaxPoint.Y -
                extents.MinPoint.Y -
                definition.HeightMm) <= GeometryTolerance;
    }
}

internal sealed class AutoCadItemLeaderFrameOnlyBlockBatchCatalog
{
    private readonly Dictionary<AutoCadItemLeaderFrameOnlyBlockKey, (
        ObjectId BlockId,
        string ResolvedName,
        bool Collision)> _index = new();

    public AutoCadItemLeaderFrameOnlyBlockBatchCatalog(Database database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        DatabaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database);
    }

    public Database Database { get; }
    public AutoCadDatabaseIdentityToken? DatabaseIdentity { get; }
    public int Count => _index.Count;

    public bool TryGet(
        AutoCadItemLeaderFrameOnlyBlockKey key,
        out AutoCadItemLeaderFrameOnlyBlockResult? result)
    {
        if (_index.TryGetValue(key, out var entry))
        {
            result = AutoCadItemLeaderFrameOnlyBlockResult.Reused(
                key,
                AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(key),
                entry.ResolvedName,
                entry.BlockId,
                DatabaseIdentity,
                entry.Collision,
                "Reused G4 frame-only definition from the current batch catalog.");
            return true;
        }

        result = null;
        return false;
    }

    public void Add(
        AutoCadItemLeaderFrameOnlyBlockKey key,
        ObjectId blockId,
        string resolvedName,
        bool collision) =>
        _index[key] = (blockId, resolvedName, collision);
}
