using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Ensures immutable, database-local G5 BlockContent BTRs (Plain Combined,
/// Framed Combined, Framed ItemOnly). Existing definitions are inspected
/// ForRead only; mismatches never mutate shared content — collision names
/// are created instead.
/// </summary>
internal static class AcKrovyFramedBlockContentDefinitionService
{
    public static AutoCadFramedBlockContentResult Ensure(
        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);

        var databaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database);
        AutoCadFramedBlockContentRequest normalized;
        string rawKey;
        string canonicalName;
        TimberItemLeaderBlockDefinition? frameDefinition;
        try
        {
            normalized = request.Normalize();
            if (!AutoCadDatabaseIdentity.IsSame(database, normalized.ItemTextStyleId) ||
                (normalized.Presentation ==
                    TimberFramedBlockContentPresentation.Combined &&
                 !AutoCadDatabaseIdentity.IsSame(
                     database,
                     normalized.DimensionTextStyleId)))
            {
                return AutoCadFramedBlockContentResult.DatabaseMismatch(
                    null,
                    null,
                    databaseIdentity,
                    "TextStyle ObjectId belongs to a different database.");
            }

            frameDefinition = ResolveFrameDefinition(normalized);
            var sizeToken = TimberFramedBlockContentDefinitionRules.GetFrameSizeToken(
                normalized.ContentKind,
                frameDefinition?.Size);
            rawKey = TimberFramedBlockContentVariantRules.CreateRawKey(
                normalized.ContentKind,
                sizeToken,
                normalized.ItemTextStyleName,
                normalized.DimensionTextStyleName,
                normalized.ItemPaperHeightMm,
                normalized.DimensionPaperHeightMm,
                normalized.Presentation);
            canonicalName = AutoCadFramedBlockContentPolicy.CreateCanonicalName(rawKey);
            if (!AutoCadFramedBlockContentPolicy.IsProductionFamilyName(canonicalName))
            {
                return AutoCadFramedBlockContentResult.InvalidRequest(
                    rawKey,
                    canonicalName,
                    databaseIdentity,
                    "Generated block name is outside the AK_KROVY_FBC_ family.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                ArgumentOutOfRangeException)
        {
            return AutoCadFramedBlockContentResult.InvalidRequest(
                null,
                null,
                databaseIdentity,
                exception.Message);
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var canonicalReason = "Canonical name is unoccupied.";
        var matchedReason = string.Empty;
        var matchedId = ObjectId.Null;
        var decision = AutoCadFramedBlockContentPolicy.Select(
            rawKey,
            candidateName =>
            {
                if (!blockTable.Has(candidateName))
                {
                    return AutoCadFramedBlockContentCandidateState.Missing;
                }

                var candidateId = blockTable[candidateName];
                var matches = ValidateExistingDefinition(
                    database,
                    transaction,
                    candidateId,
                    normalized,
                    frameDefinition,
                    out var reason);
                if (string.Equals(
                        candidateName,
                        canonicalName,
                        StringComparison.Ordinal))
                {
                    canonicalReason = reason;
                }
                if (!matches)
                {
                    return AutoCadFramedBlockContentCandidateState.Invalid;
                }

                matchedId = candidateId;
                matchedReason = reason;
                return AutoCadFramedBlockContentCandidateState.Matching;
            });

        if (decision.Kind == AutoCadFramedBlockContentCollisionDecisionKind.Create)
        {
            return CreateDefinition(
                database,
                transaction,
                normalized,
                frameDefinition,
                rawKey,
                canonicalName,
                decision.CandidateName,
                decision.IsCollision);
        }

        if (decision.Kind == AutoCadFramedBlockContentCollisionDecisionKind.Reuse)
        {
            return AutoCadFramedBlockContentResult.Reused(
                rawKey,
                canonicalName,
                decision.CandidateName,
                matchedId,
                databaseIdentity,
                decision.IsCollision,
                decision.IsCollision
                    ? $"Canonical definition was invalid ({canonicalReason}); {matchedReason}"
                    : matchedReason);
        }

        return AutoCadFramedBlockContentResult.ExistingDefinitionInvalid(
            rawKey,
            canonicalName,
            databaseIdentity,
            $"Canonical definition was invalid ({canonicalReason}) and all " +
            "deterministic collision names were occupied by invalid content.");
    }

    internal static bool ValidateExistingDefinition(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        AutoCadFramedBlockContentRequest request,
        TimberItemLeaderBlockDefinition? frameDefinition,
        out string reason)
    {
        reason = "Existing definition matches the immutable FBC contract.";
        if (!AutoCadDatabaseIdentity.IsSame(database, blockId))
        {
            reason = "Block ObjectId does not belong to the current Database.";
            return false;
        }

        BlockTableRecord block;
        try
        {
            if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                    BlockTableRecord candidate ||
                candidate.IsErased)
            {
                reason = "Block definition is unavailable or erased.";
                return false;
            }

            block = candidate;
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            reason = $"Block definition could not be opened ForRead: {exception.Message}";
            return false;
        }

        if (block.IsDynamicBlock ||
            block.Annotative != AnnotativeStates.False ||
            block.BlockScaling != BlockScaling.Uniform ||
            Math.Abs(block.Origin.X) >
                TimberFramedBlockContentDefinitionRules.GeometryToleranceMm ||
            Math.Abs(block.Origin.Y) >
                TimberFramedBlockContentDefinitionRules.GeometryToleranceMm ||
            Math.Abs(block.Origin.Z) >
                TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            reason = "Block flags/origin do not match the immutable FBC contract.";
            return false;
        }

        var entities = new List<Entity>();
        foreach (ObjectId id in block)
        {
            try
            {
                if (id.IsValid &&
                    transaction.GetObject(id, OpenMode.ForRead, true) is Entity entity &&
                    !entity.IsErased)
                {
                    entities.Add(entity);
                }
            }
            catch (Exception exception) when (
                exception is AcadException or ObjectDisposedException)
            {
                // Skip unreadable members.
            }
        }

        var attributes = entities.OfType<AttributeDefinition>().ToArray();
        var frames = entities
            .Where(entity => entity is not AttributeDefinition)
            .ToArray();
        var expectedAttrCount =
            TimberFramedBlockContentDefinitionRules.ExpectedAttributeCount(
                request.Presentation);
        var expectedFrameCount =
            TimberFramedBlockContentDefinitionRules.ExpectedFrameEntityCount(
                request.ContentKind);
        if (attributes.Length != expectedAttrCount ||
            frames.Length != expectedFrameCount ||
            entities.Count != expectedAttrCount + expectedFrameCount)
        {
            reason =
                $"Entity inventory mismatch: attrs={attributes.Length} " +
                $"(expected {expectedAttrCount}), frames={frames.Length} " +
                $"(expected {expectedFrameCount}).";
            return false;
        }

        var expectedTags =
            TimberFramedBlockContentDefinitionRules.ExpectedAttributeTags(
                request.Presentation);
        foreach (var tag in expectedTags)
        {
            var matches = attributes.Where(attribute =>
                string.Equals(attribute.Tag, tag, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                reason = $"Expected exactly one AttrDef tag '{tag}'.";
                return false;
            }
        }

        if (attributes.Any(attribute =>
                !expectedTags.Contains(
                    attribute.Tag,
                    StringComparer.OrdinalIgnoreCase)))
        {
            reason = "Unexpected AttributeDefinition tag present.";
            return false;
        }

        var itemHeight =
            TimberFramedBlockContentDefinitionRules.CalculateBaselineItemModelHeightMm(
                request.ItemPaperHeightMm);
        var dimHeight =
            TimberFramedBlockContentDefinitionRules
                .CalculateBaselineDimensionModelHeightMm(
                    request.DimensionPaperHeightMm);
        var frameWidth = frameDefinition?.WidthMm ?? 0d;

        foreach (var attribute in attributes)
        {
            if (!ValidateAttributeContract(
                    database,
                    attribute,
                    request,
                    frameWidth,
                    itemHeight,
                    dimHeight,
                    out reason))
            {
                return false;
            }
        }

        if (expectedFrameCount == 1)
        {
            if (frameDefinition is null ||
                !HasExpectedGeometry(database, frames[0], frameDefinition))
            {
                reason = "Frame geometry does not match kind/size contract.";
                return false;
            }
        }

        return true;
    }

    private static AutoCadFramedBlockContentResult CreateDefinition(
        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentRequest request,
        TimberItemLeaderBlockDefinition? frameDefinition,
        string rawKey,
        string canonicalName,
        string resolvedName,
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

        if (frameDefinition is not null)
        {
            AcKrovyItemLeaderBlockService.AddFrameGeometry(
                database,
                transaction,
                block,
                frameDefinition);
        }

        var itemHeight =
            TimberFramedBlockContentDefinitionRules.CalculateBaselineItemModelHeightMm(
                request.ItemPaperHeightMm);
        AppendAttribute(
            database,
            transaction,
            block,
            TimberFramedBlockContentDefinitionRules.ItemNoTag,
            itemHeight,
            request.ItemTextStyleId,
            ToPoint3d(TimberFramedBlockContentDefinitionRules.ItemAttributeLocalPoint));

        if (request.Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            var dimHeight =
                TimberFramedBlockContentDefinitionRules
                    .CalculateBaselineDimensionModelHeightMm(
                        request.DimensionPaperHeightMm);
            var frameWidth = frameDefinition?.WidthMm ?? 0d;
            AppendAttribute(
                database,
                transaction,
                block,
                TimberFramedBlockContentDefinitionRules.WidthTag,
                dimHeight,
                request.DimensionTextStyleId,
                ToPoint3d(
                    TimberFramedBlockContentDefinitionRules.WidthAttributeLocalPoint(
                        request.ContentKind,
                        frameWidth,
                        request.DimensionPaperHeightMm)));
            AppendAttribute(
                database,
                transaction,
                block,
                TimberFramedBlockContentDefinitionRules.HeightTag,
                dimHeight,
                request.DimensionTextStyleId,
                ToPoint3d(
                    TimberFramedBlockContentDefinitionRules.HeightAttributeLocalPoint(
                        request.ContentKind,
                        frameWidth,
                        request.DimensionPaperHeightMm)));
        }

        if (!ValidateExistingDefinition(
                database,
                transaction,
                blockId,
                request,
                frameDefinition,
                out var validationReason))
        {
            throw new InvalidOperationException(
                "New immutable FBC definition failed read-only validation: " +
                validationReason);
        }

        return AutoCadFramedBlockContentResult.Created(
            rawKey,
            canonicalName,
            resolvedName,
            blockId,
            AutoCadDatabaseIdentity.TryGetIdentity(database),
            collision,
            collision
                ? "Created a deterministic collision FBC variant without mutating the occupied canonical definition."
                : "Created and validated a new immutable FBC block definition.");
    }

    private static void AppendAttribute(
        Database database,
        Transaction transaction,
        BlockTableRecord block,
        string tag,
        double height,
        ObjectId textStyleId,
        Point3d position)
    {
        var attribute = new AttributeDefinition();
        attribute.SetDatabaseDefaults(database);
        attribute.Tag = tag;
        attribute.Prompt = tag;
        attribute.TextString = string.Empty;
        attribute.Height = height;
        attribute.Position = position;
        attribute.HorizontalMode = TextHorizontalMode.TextCenter;
        attribute.VerticalMode = TextVerticalMode.TextVerticalMid;
        attribute.AlignmentPoint = position;
        attribute.Invisible = false;
        attribute.Constant = false;
        attribute.Preset = false;
        attribute.Verifiable = false;
        attribute.LockPositionInBlock = true;
        ApplyByBlock(database, attribute);
        attribute.TextStyleId = textStyleId;
        attribute.Height = height;
        block.AppendEntity(attribute);
        transaction.AddNewlyCreatedDBObject(attribute, true);
    }

    private static bool ValidateAttributeContract(
        Database database,
        AttributeDefinition attribute,
        AutoCadFramedBlockContentRequest request,
        double frameWidthMm,
        double itemHeight,
        double dimHeight,
        out string reason)
    {
        reason = "ok";
        if (attribute.IsMTextAttributeDefinition)
        {
            reason = $"AttrDef '{attribute.Tag}' must not be MText.";
            return false;
        }
        if (attribute.Constant ||
            attribute.Preset ||
            attribute.Invisible ||
            attribute.Verifiable ||
            !attribute.LockPositionInBlock)
        {
            reason = $"AttrDef '{attribute.Tag}' immutable flags mismatch.";
            return false;
        }
        if (!string.IsNullOrEmpty(attribute.TextString))
        {
            reason = $"AttrDef '{attribute.Tag}' default text must be empty.";
            return false;
        }
        if (attribute.HorizontalMode != TextHorizontalMode.TextCenter ||
            attribute.VerticalMode != TextVerticalMode.TextVerticalMid ||
            Math.Abs(attribute.Rotation) >
                TimberFramedBlockContentDefinitionRules.AttributeTolerance)
        {
            reason = $"AttrDef '{attribute.Tag}' alignment/rotation mismatch.";
            return false;
        }
        if (!HasByBlockAppearance(database, attribute))
        {
            reason = $"AttrDef '{attribute.Tag}' must use ByBlock appearance.";
            return false;
        }

        var tag = attribute.Tag;
        double expectedHeight;
        ObjectId expectedStyleId;
        Point3d expectedPosition;
        if (string.Equals(
                tag,
                TimberFramedBlockContentDefinitionRules.ItemNoTag,
                StringComparison.OrdinalIgnoreCase))
        {
            expectedHeight = itemHeight;
            expectedStyleId = request.ItemTextStyleId;
            expectedPosition = ToPoint3d(
                TimberFramedBlockContentDefinitionRules.ItemAttributeLocalPoint);
        }
        else if (string.Equals(
                     tag,
                     TimberFramedBlockContentDefinitionRules.WidthTag,
                     StringComparison.OrdinalIgnoreCase))
        {
            expectedHeight = dimHeight;
            expectedStyleId = request.DimensionTextStyleId;
            expectedPosition = ToPoint3d(
                TimberFramedBlockContentDefinitionRules.WidthAttributeLocalPoint(
                    request.ContentKind,
                    frameWidthMm,
                    request.DimensionPaperHeightMm));
        }
        else if (string.Equals(
                     tag,
                     TimberFramedBlockContentDefinitionRules.HeightTag,
                     StringComparison.OrdinalIgnoreCase))
        {
            expectedHeight = dimHeight;
            expectedStyleId = request.DimensionTextStyleId;
            expectedPosition = ToPoint3d(
                TimberFramedBlockContentDefinitionRules.HeightAttributeLocalPoint(
                    request.ContentKind,
                    frameWidthMm,
                    request.DimensionPaperHeightMm));
        }
        else
        {
            reason = $"Unexpected AttrDef tag '{tag}'.";
            return false;
        }

        if (attribute.TextStyleId != expectedStyleId)
        {
            reason = $"AttrDef '{tag}' TextStyleId mismatch.";
            return false;
        }
        if (Math.Abs(attribute.Height - expectedHeight) >
            TimberFramedBlockContentDefinitionRules.AttributeTolerance)
        {
            reason =
                $"AttrDef '{tag}' Height mismatch: expected {expectedHeight}, " +
                $"actual {attribute.Height}.";
            return false;
        }
        if (attribute.AlignmentPoint.DistanceTo(expectedPosition) >
                TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            reason = $"AttrDef '{tag}' AlignmentPoint mismatch.";
            return false;
        }

        return true;
    }

    private static TimberItemLeaderBlockDefinition? ResolveFrameDefinition(
        AutoCadFramedBlockContentRequest request)
    {
        if (request.ContentKind == TimberFramedBlockContentKind.Plain)
        {
            return null;
        }

        var style = TimberFramedBlockContentDefinitionRules.ToItemNumberLeaderStyle(
            request.ContentKind);
        return TimberItemLeaderBlockDefinitionRules.Resolve(
            style,
            request.ItemTextForFrameSizing);
    }

    private static bool HasExpectedGeometry(
        Database database,
        Entity frame,
        TimberItemLeaderBlockDefinition definition)
    {
        if (!HasByBlockAppearance(database, frame))
        {
            return false;
        }

        var tol = TimberFramedBlockContentDefinitionRules.GeometryToleranceMm;
        var attrTol = TimberFramedBlockContentDefinitionRules.AttributeTolerance;
        return definition.Style switch
        {
            ItemNumberLeaderStyle.Circle =>
                frame is Circle circle &&
                circle.Center.DistanceTo(Point3d.Origin) <= tol &&
                circle.Normal.IsParallelTo(Vector3d.ZAxis) &&
                Math.Abs(circle.Radius * 2d - definition.WidthMm) <= tol,
            ItemNumberLeaderStyle.Slot =>
                frame is Polyline slot &&
                slot.Closed &&
                slot.NumberOfVertices == 4 &&
                HasExpectedExtents(slot, definition) &&
                Math.Abs(slot.GetBulgeAt(0)) <= attrTol &&
                Math.Abs(slot.GetBulgeAt(1) - 1d) <= attrTol &&
                Math.Abs(slot.GetBulgeAt(2)) <= attrTol &&
                Math.Abs(slot.GetBulgeAt(3) - 1d) <= attrTol,
            ItemNumberLeaderStyle.Rectangle =>
                frame is Polyline rectangle &&
                rectangle.Closed &&
                rectangle.NumberOfVertices == 4 &&
                HasExpectedExtents(rectangle, definition) &&
                Enumerable.Range(0, 4).All(index =>
                    Math.Abs(rectangle.GetBulgeAt(index)) <= attrTol),
            _ => false,
        };
    }

    private static bool HasExpectedExtents(
        Entity entity,
        TimberItemLeaderBlockDefinition definition)
    {
        var extents = entity.GeometricExtents;
        var tol = TimberFramedBlockContentDefinitionRules.GeometryToleranceMm;
        return Math.Abs(
                extents.MaxPoint.X - extents.MinPoint.X - definition.WidthMm) <=
                tol &&
            Math.Abs(
                extents.MaxPoint.Y - extents.MinPoint.Y - definition.HeightMm) <=
                tol;
    }

    private static bool HasByBlockAppearance(Database database, Entity entity) =>
        entity.Color.ColorMethod == ColorMethod.ByBlock &&
        entity.LinetypeId == database.ByBlockLinetype &&
        entity.LineWeight == LineWeight.ByBlock &&
        string.Equals(entity.Layer, "0", StringComparison.Ordinal);

    private static void ApplyByBlock(Database database, Entity entity)
    {
        entity.SetDatabaseDefaults(database);
        entity.Layer = "0";
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        entity.LinetypeId = database.ByBlockLinetype;
        entity.LineWeight = LineWeight.ByBlock;
    }

    private static Point3d ToPoint3d(TimberPlanarPoint point) =>
        new(point.X, point.Y, 0d);
}
