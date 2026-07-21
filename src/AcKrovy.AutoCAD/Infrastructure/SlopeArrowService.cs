using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeArrowService
{
    public const string ArrowLayerName = "KROV_SKLON";

    private const int ArrowLayerColorIndex = 8;
    private const string HorizontalMarkerBlockName = "DECORAIR_ACADKROVY_HORIZONTAL_SLOPE_MARKER";
    private const string PostPerpendicularMarkerBlockName = "DECORAIR_ACADKROVY_POST_90_MARKER_V3";
    private const double HorizontalMarkerHalfLengthMm = 60d;
    private const double HorizontalMarkerHalfGapMm = 25d;

    public static bool UpsertForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        SlopeAnnotationGeometryData geometry)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity))
        {
            return false;
        }

        var sourceHandle = sourceEntity.Handle.ToString();
        var matchingGlyphs = ReadGlyphs(database, transaction)
            .Where(glyph => string.Equals(
                glyph.Data.SourceHandle,
                sourceHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var glyphKind = TimberSlopeAnnotationRules.ResolveGlyphKind(data.ElementType, data.SlopeDegrees);

        if (glyphKind == TimberSlopeGlyphKind.None)
        {
            DeleteGlyphs(transaction, matchingGlyphs.Select(glyph => glyph.Id));
            return false;
        }

        var matchingDesiredGlyphs = matchingGlyphs
            .Where(glyph => IsDesiredEntityType(
                database,
                transaction,
                glyph.Entity,
                glyphKind))
            .ToList();
        Entity glyph;
        var isCreated = matchingDesiredGlyphs.Count == 0;

        if (isCreated)
        {
            glyph = CreateGlyph(database, transaction, glyphKind, geometry.AnnotationPoint);
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(glyph);
            transaction.AddNewlyCreatedDBObject(glyph, true);
        }
        else
        {
            glyph = (Entity)transaction.GetObject(matchingDesiredGlyphs[0].Id, OpenMode.ForWrite);
        }

        if (glyph is Polyline arrow)
        {
            var placement = CalculatePlacement(geometry, data.IsSlopeDirectionReversed);
            ApplyArrowAppearance(database, transaction, arrow, placement.Geometry, placement.Elevation);
        }
        else if (glyph is BlockReference marker)
        {
            if (glyphKind == TimberSlopeGlyphKind.PostPerpendicularMarker)
            {
                ApplyPostPerpendicularMarkerAppearance(database, transaction, marker, geometry);
            }
            else
            {
                ApplyHorizontalMarkerAppearance(database, transaction, marker, geometry);
            }
        }

        SlopeArrowStore.Write(glyph, transaction, new SlopeArrowData { SourceHandle = sourceHandle });
        DeleteGlyphs(transaction, matchingGlyphs
            .Where(item => item.Id != glyph.ObjectId)
            .Select(item => item.Id));
        DeleteDuplicateArrowsForExistingSourceHandles(database, transaction);
        return isCreated;
    }

    internal static int DeleteArrowsForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        if (sourceHandles.Count == 0)
        {
            return 0;
        }

        var targetHandles = new HashSet<string>(sourceHandles, StringComparer.OrdinalIgnoreCase);
        var existingHandles = ReadTimberSourceHandles(database, transaction);
        var arrows = ReadGlyphs(database, transaction)
            .Where(arrow => targetHandles.Contains(arrow.Data.SourceHandle))
            .ToList();
        return DeleteArrowsSelectedByCleanupRules(transaction, arrows, existingHandles, deleteDuplicates: false);
    }

    internal static int DeleteForSourceHandle(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        return DeleteGlyphs(
            transaction,
            ReadGlyphs(database, transaction)
                .Where(arrow => string.Equals(
                    arrow.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
                .Select(arrow => arrow.Id));
    }

    internal static int DeleteInsertedArrowsWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> arrowIds)
    {
        if (arrowIds.Count == 0)
        {
            return 0;
        }

        var arrows = ReadGlyphs(database, transaction)
            .Where(arrow => arrowIds.Contains(arrow.Id))
            .ToList();
        return DeleteArrowsSelectedByCleanupRules(
            transaction,
            arrows,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    internal static int DeleteDuplicateArrowsForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        var arrows = ReadGlyphs(database, transaction);
        var existingHandles = ReadTimberSourceHandles(database, transaction);
        return DeleteArrowsSelectedByCleanupRules(transaction, arrows, existingHandles, deleteDuplicates: true);
    }

    private static ArrowPlacement CalculatePlacement(
        SlopeAnnotationGeometryData geometry,
        bool isReversed)
    {
        return new ArrowPlacement(
            TimberSlopeArrowCalculator.Calculate(
                geometry.Start.X,
                geometry.Start.Y,
                geometry.End.X,
                geometry.End.Y,
                geometry.AnnotationPoint.X,
                geometry.AnnotationPoint.Y,
                isReversed),
            geometry.AnnotationPoint.Z);
    }

    private static void ApplyArrowAppearance(
        Database database,
        Transaction transaction,
        Polyline arrow,
        TimberSlopeArrowPlacement placement,
        double elevation)
    {
        var points = new[]
        {
            new Point2d(placement.TailX, placement.TailY),
            new Point2d(placement.TipX, placement.TipY),
            new Point2d(placement.HeadLeftX, placement.HeadLeftY),
            new Point2d(placement.TipX, placement.TipY),
            new Point2d(placement.HeadRightX, placement.HeadRightY),
        };

        while (arrow.NumberOfVertices < points.Length)
        {
            var index = arrow.NumberOfVertices;
            arrow.AddVertexAt(index, points[index], 0d, 0d, 0d);
        }

        for (var index = 0; index < points.Length; index++)
        {
            arrow.SetPointAt(index, points[index]);
            arrow.SetBulgeAt(index, 0d);
            arrow.SetStartWidthAt(index, 0d);
            arrow.SetEndWidthAt(index, 0d);
        }

        while (arrow.NumberOfVertices > points.Length)
        {
            arrow.RemoveVertexAt(arrow.NumberOfVertices - 1);
        }

        arrow.Elevation = elevation;
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            arrow,
            ArrowLayerName,
            ArrowLayerColorIndex,
            isPlottable: false);
        arrow.LineWeight = LineWeight.ByLayer;
    }

    private static void ApplyHorizontalMarkerAppearance(
        Database database,
        Transaction transaction,
        BlockReference marker,
        SlopeAnnotationGeometryData geometry)
    {
        marker.Position = geometry.AnnotationPoint;
        marker.Rotation = Math.Atan2(
            geometry.End.Y - geometry.Start.Y,
            geometry.End.X - geometry.Start.X);
        marker.ScaleFactors = new Scale3d(1d);
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            marker,
            ArrowLayerName,
            ArrowLayerColorIndex,
            isPlottable: false);
        marker.LineWeight = LineWeight.ByLayer;
    }

    private static void ApplyPostPerpendicularMarkerAppearance(
        Database database,
        Transaction transaction,
        BlockReference marker,
        SlopeAnnotationGeometryData geometry)
    {
        var symbol = TimberPostAnnotationGeometryCalculator.Calculate(
            geometry.Start.X,
            geometry.Start.Y,
            geometry.End.X,
            geometry.End.Y,
            geometry.AnnotationPoint.X,
            geometry.AnnotationPoint.Y);
        marker.Position = new Point3d(
            symbol.Anchor.X,
            symbol.Anchor.Y,
            geometry.AnnotationPoint.Z);
        marker.Rotation = symbol.RotationRadians;
        marker.ScaleFactors = new Scale3d(1d);
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            marker,
            ArrowLayerName,
            ArrowLayerColorIndex,
            isPlottable: false);
        marker.LineWeight = LineWeight.ByLayer;
    }

    private static Entity CreateGlyph(
        Database database,
        Transaction transaction,
        TimberSlopeGlyphKind glyphKind,
        Point3d position) => glyphKind switch
        {
            TimberSlopeGlyphKind.DirectionalArrow => new Polyline(5),
            TimberSlopeGlyphKind.HorizontalMarker => new BlockReference(
                position,
                EnsureHorizontalMarkerBlock(database, transaction)),
            TimberSlopeGlyphKind.PostPerpendicularMarker => new BlockReference(
                position,
                EnsurePostPerpendicularMarkerBlock(database, transaction)),
            _ => throw new InvalidOperationException(UiStrings.ErrorUnsupportedSlopeGlyph),
        };

    private static ObjectId EnsureHorizontalMarkerBlock(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        if (blockTable.Has(HorizontalMarkerBlockName))
        {
            return blockTable[HorizontalMarkerBlockName];
        }

        blockTable.UpgradeOpen();
        var definition = new BlockTableRecord
        {
            Name = HorizontalMarkerBlockName,
            Origin = Point3d.Origin,
        };
        blockTable.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);

        AddHorizontalMarkerLine(database, transaction, definition, -HorizontalMarkerHalfGapMm);
        AddHorizontalMarkerLine(database, transaction, definition, HorizontalMarkerHalfGapMm);
        return definition.ObjectId;
    }

    private static void AddHorizontalMarkerLine(
        Database database,
        Transaction transaction,
        BlockTableRecord definition,
        double y)
    {
        var line = new Line(
            new Point3d(-HorizontalMarkerHalfLengthMm, y, 0d),
            new Point3d(HorizontalMarkerHalfLengthMm, y, 0d));
        line.SetDatabaseDefaults(database);
        line.Layer = "0";
        line.ColorIndex = 0;
        line.LineWeight = LineWeight.ByBlock;
        definition.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
    }

    private static ObjectId EnsurePostPerpendicularMarkerBlock(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        if (blockTable.Has(PostPerpendicularMarkerBlockName))
        {
            return blockTable[PostPerpendicularMarkerBlockName];
        }

        blockTable.UpgradeOpen();
        var definition = new BlockTableRecord
        {
            Name = PostPerpendicularMarkerBlockName,
            Origin = Point3d.Origin,
        };
        blockTable.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);

        var symbol = TimberPostAnnotationGeometryCalculator.CreateLocal();
        AddPostMarkerLine(database, transaction, definition, symbol.CapStart, symbol.CapEnd);
        AddPostMarkerLine(database, transaction, definition, symbol.Anchor, symbol.StemEnd);
        return definition.ObjectId;
    }

    private static void AddPostMarkerLine(
        Database database,
        Transaction transaction,
        BlockTableRecord definition,
        TimberSlopeAnnotationPoint start,
        TimberSlopeAnnotationPoint end)
    {
        var line = new Line(
            new Point3d(start.X, start.Y, 0d),
            new Point3d(end.X, end.Y, 0d));
        line.SetDatabaseDefaults(database);
        line.Layer = "0";
        line.ColorIndex = 0;
        line.LineWeight = LineWeight.ByBlock;
        definition.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
    }

    private static bool IsDesiredEntityType(
        Database database,
        Transaction transaction,
        Entity entity,
        TimberSlopeGlyphKind glyphKind) => glyphKind switch
        {
            TimberSlopeGlyphKind.DirectionalArrow => entity is Polyline,
            TimberSlopeGlyphKind.HorizontalMarker => IsBlockReferenceOf(
                database,
                transaction,
                entity,
                HorizontalMarkerBlockName),
            TimberSlopeGlyphKind.PostPerpendicularMarker => IsBlockReferenceOf(
                database,
                transaction,
                entity,
                PostPerpendicularMarkerBlockName),
            _ => false,
        };

    private static bool IsBlockReferenceOf(
        Database database,
        Transaction transaction,
        Entity entity,
        string blockName)
    {
        if (entity is not BlockReference blockReference)
        {
            return false;
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        return blockTable.Has(blockName) && blockReference.BlockTableRecord == blockTable[blockName];
    }

    private static IReadOnlyList<SlopeGlyphEntry> ReadGlyphs(
        Database database,
        Transaction transaction)
    {
        var arrows = new List<SlopeGlyphEntry>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var glyph,
                    database) ||
                glyph is null ||
                !SlopeArrowStore.TryRead(glyph, out var data) ||
                data is null)
            {
                continue;
            }

            arrows.Add(new SlopeGlyphEntry(id, glyph, data));
        }

        return arrows;
    }

    private static IReadOnlySet<string> ReadTimberSourceHandles(Database database, Transaction transaction)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) &&
                entity is not null)
            {
                handles.Add(entity.Handle.ToString());
            }
        }

        return handles;
    }

    private static int DeleteGlyphs(Transaction transaction, IEnumerable<ObjectId> ids)
    {
        var deleted = 0;
        foreach (var id in ids.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var glyph) ||
                glyph is null ||
                !SlopeArrowStore.TryRead(glyph, out _))
            {
                continue;
            }

            glyph.Erase();
            deleted++;
        }

        return deleted;
    }

    private static int DeleteArrowsSelectedByCleanupRules(
        Transaction transaction,
        IReadOnlyList<SlopeGlyphEntry> arrows,
        IReadOnlyCollection<string> existingTimberSourceHandles,
        bool deleteDuplicates)
    {
        var idsByKey = arrows.ToDictionary(arrow => arrow.Id.ToString(), arrow => arrow.Id);
        var candidates = arrows
            .Select(arrow => new TimberElementLabelCandidate
            {
                LabelKey = arrow.Id.ToString(),
                SourceHandle = arrow.Data.SourceHandle,
            })
            .ToList();
        var keys = deleteDuplicates
            ? TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
                candidates,
                existingTimberSourceHandles)
            : TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
                candidates,
                existingTimberSourceHandles);

        return DeleteGlyphs(
            transaction,
            keys.Where(idsByKey.ContainsKey).Select(key => idsByKey[key]));
    }

    private sealed record ArrowPlacement(TimberSlopeArrowPlacement Geometry, double Elevation);
    private sealed record SlopeGlyphEntry(ObjectId Id, Entity Entity, SlopeArrowData Data);
}
