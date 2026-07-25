using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AcKrovyItemLeaderBlockService
{
    public static ItemLeaderBlockReference Ensure(
        Database database,
        Transaction transaction,
        ItemNumberLeaderStyle style,
        string itemText)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        BlockTableRecord block;
        ObjectId blockId;
        if (blockTable.Has(definition.BlockName))
        {
            blockId = blockTable[definition.BlockName];
            block = (BlockTableRecord)transaction.GetObject(
                blockId,
                OpenMode.ForWrite);
            var existingAttributeId = FindItemNumberAttribute(block, transaction);
            if (!existingAttributeId.IsNull &&
                IsCompatibleDefinition(
                    database,
                    transaction,
                    block,
                    existingAttributeId,
                    definition))
            {
                return new ItemLeaderBlockReference(
                    blockId,
                    existingAttributeId,
                    definition);
            }

            EraseDefinitionContents(block, transaction);
            block.Annotative = AnnotativeStates.False;
            block.BlockScaling = BlockScaling.Uniform;
        }
        else
        {
            blockTable.UpgradeOpen();
            block = new BlockTableRecord
            {
                Name = definition.BlockName,
                Origin = Point3d.Origin,
                Annotative = AnnotativeStates.False,
                BlockScaling = BlockScaling.Uniform,
            };
            blockId = blockTable.Add(block);
            transaction.AddNewlyCreatedDBObject(block, true);
        }

        AddFrameGeometry(database, transaction, block, definition);
        var attributeId = AddItemNumberAttribute(
            database,
            transaction,
            block,
            definition.TextHeightMm);
        return new ItemLeaderBlockReference(blockId, attributeId, definition);
    }

    private static void AddFrameGeometry(
        Database database,
        Transaction transaction,
        BlockTableRecord block,
        TimberItemLeaderBlockDefinition definition)
    {
        switch (definition.Style)
        {
            case ItemNumberLeaderStyle.Circle:
                Append(
                    block,
                    transaction,
                    ApplyByBlock(
                        database,
                        new Circle(
                            Point3d.Origin,
                            Vector3d.ZAxis,
                            definition.WidthMm / 2d)));
                break;

            case ItemNumberLeaderStyle.Slot:
                var radius = definition.HeightMm / 2d;
                var straightHalfLength = definition.WidthMm / 2d - radius;
                var slot = new Polyline(4) { Closed = true };
                slot.AddVertexAt(
                    0,
                    new Point2d(-straightHalfLength, -radius),
                    0d,
                    0d,
                    0d);
                slot.AddVertexAt(
                    1,
                    new Point2d(straightHalfLength, -radius),
                    1d,
                    0d,
                    0d);
                slot.AddVertexAt(
                    2,
                    new Point2d(straightHalfLength, radius),
                    0d,
                    0d,
                    0d);
                slot.AddVertexAt(
                    3,
                    new Point2d(-straightHalfLength, radius),
                    1d,
                    0d,
                    0d);
                Append(block, transaction, ApplyByBlock(database, slot));
                break;

            case ItemNumberLeaderStyle.Rectangle:
                var halfWidth = definition.WidthMm / 2d;
                var halfHeight = definition.HeightMm / 2d;
                var rectangle = new Polyline(4) { Closed = true };
                rectangle.AddVertexAt(
                    0,
                    new Point2d(-halfWidth, -halfHeight),
                    0d,
                    0d,
                    0d);
                rectangle.AddVertexAt(
                    1,
                    new Point2d(halfWidth, -halfHeight),
                    0d,
                    0d,
                    0d);
                rectangle.AddVertexAt(
                    2,
                    new Point2d(halfWidth, halfHeight),
                    0d,
                    0d,
                    0d);
                rectangle.AddVertexAt(
                    3,
                    new Point2d(-halfWidth, halfHeight),
                    0d,
                    0d,
                    0d);
                Append(block, transaction, ApplyByBlock(database, rectangle));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(definition));
        }
    }

    private static ObjectId AddItemNumberAttribute(
        Database database,
        Transaction transaction,
        BlockTableRecord block,
        double textHeight)
    {
        var attribute = new AttributeDefinition();
        attribute.SetDatabaseDefaults(database);
        attribute.Tag = TimberItemLeaderBlockDefinitionRules.AttributeTag;
        attribute.Prompt = TimberItemLeaderBlockDefinitionRules.AttributeTag;
        attribute.TextString = string.Empty;
        attribute.Height = textHeight;
        attribute.Position = Point3d.Origin;
        attribute.HorizontalMode = TextHorizontalMode.TextCenter;
        attribute.VerticalMode = TextVerticalMode.TextVerticalMid;
        attribute.AlignmentPoint = Point3d.Origin;
        attribute.Invisible = false;
        attribute.Constant = false;
        attribute.Preset = false;
        attribute.Verifiable = false;
        attribute.LockPositionInBlock = true;
        ApplyByBlock(database, attribute);
        return Append(block, transaction, attribute);
    }

    private static T ApplyByBlock<T>(Database database, T entity)
        where T : Entity
    {
        entity.SetDatabaseDefaults(database);
        entity.Layer = "0";
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        entity.LinetypeId = database.ByBlockLinetype;
        entity.LineWeight = LineWeight.ByBlock;
        return entity;
    }

    private static ObjectId Append(
        BlockTableRecord block,
        Transaction transaction,
        Entity entity)
    {
        var id = block.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
        return id;
    }

    private static ObjectId FindItemNumberAttribute(
        BlockTableRecord block,
        Transaction transaction)
    {
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is
                    AttributeDefinition attribute &&
                !attribute.IsErased &&
                string.Equals(
                    attribute.Tag,
                    TimberItemLeaderBlockDefinitionRules.AttributeTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return ObjectId.Null;
    }

    private static bool IsCompatibleDefinition(
        Database database,
        Transaction transaction,
        BlockTableRecord block,
        ObjectId attributeId,
        TimberItemLeaderBlockDefinition definition)
    {
        if (block.IsDynamicBlock ||
            block.Annotative != AnnotativeStates.False ||
            transaction.GetObject(attributeId, OpenMode.ForRead) is not
                AttributeDefinition attribute ||
            !HasByBlockAppearance(database, attribute) ||
            Math.Abs(attribute.Height - definition.TextHeightMm) > 0.001d ||
            attribute.HorizontalMode != TextHorizontalMode.TextCenter ||
            attribute.VerticalMode != TextVerticalMode.TextVerticalMid)
        {
            return false;
        }

        var frames = block
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, true))
            .OfType<Entity>()
            .Where(entity => !entity.IsErased && entity is not AttributeDefinition)
            .ToArray();
        if (frames.Length != 1 || !HasByBlockAppearance(database, frames[0]))
        {
            return false;
        }

        return definition.Style switch
        {
            ItemNumberLeaderStyle.Circle =>
                frames[0] is Circle circle &&
                TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(
                    circle.Radius * 2d),
            ItemNumberLeaderStyle.Slot =>
                frames[0] is Polyline slot &&
                slot.Closed &&
                slot.NumberOfVertices == 4 &&
                HasExpectedExtents(slot, definition) &&
                Math.Abs(slot.GetBulgeAt(1) - 1d) <= 1e-9 &&
                Math.Abs(slot.GetBulgeAt(3) - 1d) <= 1e-9,
            ItemNumberLeaderStyle.Rectangle =>
                frames[0] is Polyline rectangle &&
                rectangle.Closed &&
                rectangle.NumberOfVertices == 4 &&
                HasExpectedExtents(rectangle, definition) &&
                Enumerable.Range(0, 4).All(
                    index => Math.Abs(rectangle.GetBulgeAt(index)) <= 1e-9),
            _ => false,
        };
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
                definition.WidthMm) <= 0.001d &&
            Math.Abs(
                extents.MaxPoint.Y -
                extents.MinPoint.Y -
                definition.HeightMm) <= 0.001d;
    }

    private static bool HasByBlockAppearance(Database database, Entity entity) =>
        entity.Color.ColorMethod == ColorMethod.ByBlock &&
        entity.LinetypeId == database.ByBlockLinetype &&
        entity.LineWeight == LineWeight.ByBlock &&
        string.Equals(entity.Layer, "0", StringComparison.Ordinal);

    private static void EraseDefinitionContents(
        BlockTableRecord block,
        Transaction transaction)
    {
        foreach (ObjectId id in block.Cast<ObjectId>().ToArray())
        {
            if (transaction.GetObject(id, OpenMode.ForWrite, true) is Entity entity &&
                !entity.IsErased)
            {
                entity.Erase();
            }
        }
    }
}

internal sealed record ItemLeaderBlockReference(
    ObjectId BlockId,
    ObjectId AttributeDefinitionId,
    TimberItemLeaderBlockDefinition Definition);
