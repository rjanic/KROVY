using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Ensures immutable, database-local framed ITEM_NO block variants. Existing
/// definitions are only inspected ForRead; a mismatching definition is never
/// repaired or repurposed.
/// </summary>
internal static class AcKrovyItemLeaderBlockVariantService
{
    private const double AttributeTolerance = 1e-9;
    private const double GeometryTolerance = 0.001d;

    public static AutoCadItemLeaderBlockVariantResult Ensure(
        Database database,
        Transaction transaction,
        AutoCadAnnotationPresentationContext presentationContext,
        ItemNumberLeaderStyle style,
        string itemText,
        AutoCadItemLeaderBlockVariantBatchCatalog batchCatalog)
    {
        ArgumentNullException.ThrowIfNull(presentationContext);
        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationContext.Database))
        {
            return AutoCadItemLeaderBlockVariantResult.DatabaseMismatch(
                null,
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Presentation context belongs to a different database.");
        }

        return EnsureResolved(
            database,
            transaction,
            style,
            itemText,
            presentationContext.ResolvedTextStyleName,
            presentationContext.ResolvedTextStyleId,
            presentationContext.EffectiveTextSettings.ItemNumberPaperHeightMm,
            batchCatalog);
    }

    internal static AutoCadItemLeaderBlockVariantResult EnsureResolved(
        Database database,
        Transaction transaction,
        ItemNumberLeaderStyle style,
        string itemText,
        string? resolvedCanonicalTextStyleName,
        ObjectId? resolvedTextStyleId,
        double itemNumberPaperHeightMm,
        AutoCadItemLeaderBlockVariantBatchCatalog batchCatalog)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(batchCatalog);
        var databaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database);

        if (!batchCatalog.IsBoundTo(database))
        {
            return AutoCadItemLeaderBlockVariantResult.DatabaseMismatch(
                null,
                null,
                databaseIdentity,
                "Batch catalog belongs to a different database.");
        }
        if (resolvedCanonicalTextStyleName is null ||
            resolvedTextStyleId is null)
        {
            return AutoCadItemLeaderBlockVariantResult.NoCompatibleTextStyle(
                databaseIdentity,
                "The Stage 2 resolution did not supply a compatible text style.");
        }
        if (!AutoCadDatabaseIdentity.IsSame(database, resolvedTextStyleId.Value))
        {
            return AutoCadItemLeaderBlockVariantResult.DatabaseMismatch(
                null,
                null,
                databaseIdentity,
                "Resolved text-style ObjectId belongs to a different database.");
        }

        TimberItemLeaderBlockDefinition definition;
        AutoCadItemLeaderBlockVariantKey key;
        string canonicalName;
        try
        {
            definition = TimberItemLeaderBlockDefinitionRules.Resolve(
                style,
                itemText);
            key = AutoCadItemLeaderBlockVariantKey.FromDefinition(
                definition,
                resolvedCanonicalTextStyleName,
                itemNumberPaperHeightMm);
            canonicalName =
                AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                null,
                null,
                databaseIdentity,
                exception.Message);
        }

        if (batchCatalog.TryGet(key, out var cached))
        {
            return cached!;
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var canonicalReason = "Canonical name is unoccupied.";
        var matchedReason = string.Empty;
        var matchedId = ObjectId.Null;
        var decision = AutoCadItemLeaderBlockVariantCollisionPolicy.Select(
            key,
            candidateName =>
            {
                if (!blockTable.Has(candidateName))
                {
                    return AutoCadItemLeaderBlockVariantCandidateState.Missing;
                }

                var candidateId = blockTable[candidateName];
                var matches = ValidateExistingDefinition(
                    database,
                    transaction,
                    candidateId,
                    definition,
                    key,
                    resolvedTextStyleId.Value,
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
                    return AutoCadItemLeaderBlockVariantCandidateState.Invalid;
                }

                matchedId = candidateId;
                matchedReason = reason;
                return AutoCadItemLeaderBlockVariantCandidateState.Matching;
            });

        if (decision.Kind ==
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Create)
        {
            return CreateAndCache(
                database,
                transaction,
                definition,
                key,
                canonicalName,
                decision.CandidateName,
                resolvedTextStyleId.Value,
                batchCatalog,
                decision.IsCollision);
        }
        if (decision.Kind ==
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Reuse)
        {
            batchCatalog.Add(
                key,
                matchedId,
                decision.CandidateName,
                decision.IsCollision);
            return AutoCadItemLeaderBlockVariantResult.Reused(
                key,
                canonicalName,
                decision.CandidateName,
                matchedId,
                batchCatalog.DatabaseIdentity,
                decision.IsCollision,
                decision.IsCollision
                    ? $"Canonical definition was invalid ({canonicalReason}); " +
                        matchedReason
                    : matchedReason);
        }

        return AutoCadItemLeaderBlockVariantResult.ExistingDefinitionInvalid(
            key,
            canonicalName,
            databaseIdentity,
            $"Canonical definition was invalid ({canonicalReason}) and all " +
                "deterministic collision names were occupied by invalid content.");
    }

    private static AutoCadItemLeaderBlockVariantResult CreateAndCache(
        Database database,
        Transaction transaction,
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalName,
        string resolvedName,
        ObjectId resolvedTextStyleId,
        AutoCadItemLeaderBlockVariantBatchCatalog batchCatalog,
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
        var baseHeight = TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            key.ItemNumberPaperHeightMm,
            key.BaseDenominator);
        AcKrovyItemLeaderBlockService.AddItemNumberAttribute(
            database,
            transaction,
            block,
            baseHeight,
            resolvedTextStyleId);

        if (!ValidateExistingDefinition(
                database,
                transaction,
                blockId,
                definition,
                key,
                resolvedTextStyleId,
                out var validationReason))
        {
            throw new InvalidOperationException(
                "New immutable variant failed read-only validation: " +
                validationReason);
        }

        batchCatalog.Add(key, blockId, resolvedName, collision);
        return AutoCadItemLeaderBlockVariantResult.Created(
            key,
            canonicalName,
            resolvedName,
            blockId,
            batchCatalog.DatabaseIdentity,
            collision,
            collision
                ? "Created a deterministic collision variant without mutating the occupied canonical definition."
                : "Created and validated a new immutable block variant.");
    }

    internal static bool ValidateExistingDefinition(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderBlockVariantKey key,
        ObjectId resolvedTextStyleId,
        out string reason)
    {
        var result = ValidateExistingDefinitionDetailed(
            database,
            transaction,
            blockId,
            definition,
            key,
            resolvedTextStyleId);
        reason = result.Reason;
        return result.IsValid;
    }

    internal static AutoCadItemLeaderBlockVariantDefinitionValidationResult
        ValidateExistingDefinitionDetailed(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        TimberItemLeaderBlockDefinition definition,
        AutoCadItemLeaderBlockVariantKey key,
        ObjectId resolvedTextStyleId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(key);

        var checks = new List<AutoCadItemLeaderBlockVariantValidationField>();
        var databaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database)
            ?.ToDiagnosticHex() ?? "<unavailable>";
        var diagnostic = EmptyDiagnostic(databaseIdentity);
        var blockDatabaseMatches = AutoCadDatabaseIdentity.IsSame(database, blockId);
        var expectedStyleDatabaseMatches =
            AutoCadDatabaseIdentity.IsSame(database, resolvedTextStyleId);
        checks.Add(Exact(
            "block ObjectId belongs to current Database",
            true,
            blockDatabaseMatches));
        checks.Add(Exact(
            "resolved TextStyleId belongs to current Database",
            true,
            expectedStyleDatabaseMatches));
        if (!blockDatabaseMatches || !expectedStyleDatabaseMatches)
        {
            var code = !expectedStyleDatabaseMatches
                ? AutoCadItemLeaderBlockVariantValidationReasonCode
                    .ItemNoTextStyleDatabaseMismatch
                : AutoCadItemLeaderBlockVariantValidationReasonCode.BlockUnavailable;
            return Invalid(
                code,
                "Block or resolved text-style ObjectId belongs to another database.",
                checks,
                diagnostic);
        }

        BlockTableRecord block;
        try
        {
            if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                    BlockTableRecord candidate ||
                candidate.IsErased)
            {
                return Invalid(
                    AutoCadItemLeaderBlockVariantValidationReasonCode
                        .BlockUnavailable,
                    "Block definition is unavailable or erased.",
                    checks,
                    diagnostic);
            }
            block = candidate;
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode.BlockUnavailable,
                $"Block definition could not be opened ForRead: {exception.Message}",
                checks,
                diagnostic);
        }

        diagnostic = diagnostic with
        {
            BlockName = block.Name,
            BlockHandle = ReadHandle(block.ObjectId),
            BlockObjectId = block.ObjectId.ToString(),
        };
        checks.Add(Exact("block IsDynamicBlock", false, block.IsDynamicBlock));
        checks.Add(Exact(
            "block Annotative",
            AnnotativeStates.False.ToString(),
            block.Annotative.ToString()));
        checks.Add(Exact(
            "block BlockScaling",
            BlockScaling.Uniform.ToString(),
            block.BlockScaling.ToString()));
        checks.Add(Number("block Origin.X", 0d, block.Origin.X, GeometryTolerance));
        checks.Add(Number("block Origin.Y", 0d, block.Origin.Y, GeometryTolerance));
        checks.Add(Number("block Origin.Z", 0d, block.Origin.Z, GeometryTolerance));
        if (checks.Any(check =>
                !check.Passed && check.PropertyName.StartsWith(
                    "block ",
                    StringComparison.Ordinal)))
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode.BlockFlagsMismatch,
                "Block flags do not match the immutable variant contract.",
                checks,
                diagnostic);
        }

        var entities = new List<Entity>();
        foreach (ObjectId id in block)
        {
            try
            {
                if (id.IsValid &&
                    transaction.GetObject(id, OpenMode.ForRead, true) is
                        Entity entity &&
                    !entity.IsErased)
                {
                    entities.Add(entity);
                }
            }
            catch (Exception exception) when (
                exception is AcadException or ObjectDisposedException)
            {
                // Invalid/erased direct members are excluded from the live contract.
            }
        }

        var attributes = entities.OfType<AttributeDefinition>().ToArray();
        var itemNumberAttributes = attributes.Where(attribute =>
            string.Equals(
                attribute.Tag,
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        var frames = entities
            .Where(entity => entity is not AttributeDefinition)
            .ToArray();
        diagnostic = diagnostic with
        {
            EntityCount = entities.Count,
            AttributeDefinitionCount = attributes.Length,
            FrameSignature = frames.Length == 1
                ? ReadFrameSignature(frames[0])
                : $"frame-count={frames.Length}",
        };
        checks.Add(Exact("ITEM_NO count", 1, itemNumberAttributes.Length));
        checks.Add(Exact("AttributeDefinition count", 1, attributes.Length));
        checks.Add(Exact("frame entity count", 1, frames.Length));
        var inventory =
            AutoCadItemLeaderBlockVariantInventoryValidationPolicy.Evaluate(
                attributes.Length,
                itemNumberAttributes.Length,
                frames.Length,
                attributes.Length == 1 ? attributes[0].Tag : null);
        if (!inventory.IsValid)
        {
            return Invalid(
                inventory.ReasonCode,
                inventory.Reason,
                checks,
                diagnostic);
        }

        var attribute = itemNumberAttributes[0];
        var styleIdValid = IsReadableObjectId(attribute.TextStyleId);
        var styleBelongsToDatabase = styleIdValid &&
            AutoCadDatabaseIdentity.IsSame(database, attribute.TextStyleId);
        TextStyleTableRecord? textStyle = null;
        if (styleBelongsToDatabase)
        {
            try
            {
                textStyle = transaction.GetObject(
                    attribute.TextStyleId,
                    OpenMode.ForRead,
                    true) as TextStyleTableRecord;
            }
            catch (Exception exception) when (
                exception is AcadException or ObjectDisposedException)
            {
                textStyle = null;
            }
        }
        var styleName = textStyle?.Name ?? "<unavailable>";
        var expectedHeight =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                key.ItemNumberPaperHeightMm,
                key.BaseDenominator);
        var snapshot = new AutoCadItemLeaderBlockVariantAttributeSnapshot(
            attribute.OwnerId == block.ObjectId,
            attribute.Tag,
            attribute.Prompt,
            attribute.TextString,
            attribute.Height,
            ReadObjectId(attribute.TextStyleId),
            styleIdValid && textStyle is not null,
            styleBelongsToDatabase,
            attribute.TextStyleId == resolvedTextStyleId,
            styleName,
            textStyle?.TextSize ?? double.NaN,
            ReadAnnotativeState(textStyle),
            attribute.Position.X,
            attribute.Position.Y,
            attribute.Position.Z,
            attribute.AlignmentPoint.X,
            attribute.AlignmentPoint.Y,
            attribute.AlignmentPoint.Z,
            attribute.Rotation,
            attribute.HorizontalMode.ToString(),
            attribute.VerticalMode.ToString(),
            attribute.LockPositionInBlock,
            attribute.Constant,
            attribute.Invisible,
            attribute.Preset,
            attribute.Verifiable,
            attribute.IsMTextAttributeDefinition,
            attribute.IsErased,
            HasByBlockAppearance(database, attribute));
        diagnostic = diagnostic with
        {
            AttributeHandle = ReadHandle(attribute.ObjectId),
            AttributeObjectId = attribute.ObjectId.ToString(),
            AttributeOwnerHandle = ReadHandle(attribute.OwnerId),
            AttributeOwnerName = ReadOwnerName(transaction, attribute.OwnerId),
            Attribute = snapshot,
        };
        var attributeValidation =
            AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Evaluate(
                snapshot,
                key.ResolvedCanonicalTextStyleName,
                expectedHeight);
        checks.AddRange(attributeValidation.Fields);
        if (!attributeValidation.IsValid)
        {
            return Invalid(
                attributeValidation.ReasonCode,
                attributeValidation.Reason,
                checks,
                diagnostic);
        }

        var geometryMatches = HasExpectedGeometry(database, frames[0], definition);
        checks.Add(Exact("frame geometry signature", true, geometryMatches));
        if (!geometryMatches)
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode
                    .FrameGeometryMismatch,
                "Frame geometry does not match frame kind, size, or geometry version.",
                checks,
                diagnostic);
        }

        return new AutoCadItemLeaderBlockVariantDefinitionValidationResult(
            true,
            AutoCadItemLeaderBlockVariantValidationReasonCode.Valid,
            Array.Empty<AutoCadItemLeaderBlockVariantValidationField>(),
            checks.AsReadOnly(),
            styleName,
            attribute.Height,
            diagnostic.FrameSignature,
            "Existing definition matches the complete immutable variant contract.",
            diagnostic);
    }

    private static AutoCadItemLeaderBlockVariantDefinitionValidationResult Invalid(
        AutoCadItemLeaderBlockVariantValidationReasonCode reasonCode,
        string reason,
        IReadOnlyList<AutoCadItemLeaderBlockVariantValidationField> checks,
        AutoCadItemLeaderBlockVariantDefinitionDiagnostic diagnostic) =>
        new(
            false,
            reasonCode,
            checks.Where(check => !check.Passed).ToArray(),
            checks.ToArray(),
            diagnostic.Attribute?.CanonicalTextStyleName,
            diagnostic.Attribute?.Height,
            diagnostic.FrameSignature,
            reason,
            diagnostic);

    private static AutoCadItemLeaderBlockVariantDefinitionDiagnostic
        EmptyDiagnostic(string databaseIdentity) =>
        new(
            "<unavailable>",
            "<unavailable>",
            "<unavailable>",
            databaseIdentity,
            0,
            0,
            "<unavailable>",
            "<unavailable>",
            "<unavailable>",
            "<unavailable>",
            null,
            "<unavailable>");

    private static AutoCadItemLeaderBlockVariantValidationField Exact<T>(
        string property,
        T expected,
        T actual) =>
        new(
            property,
            expected?.ToString() ?? "<null>",
            actual?.ToString() ?? "<null>",
            EqualityComparer<T>.Default.Equals(expected, actual),
            "exact");

    private static AutoCadItemLeaderBlockVariantValidationField Number(
        string property,
        double expected,
        double actual,
        double tolerance) =>
        new(
            property,
            AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(expected),
            AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(actual),
            double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(tolerance));

    private static bool IsReadableObjectId(ObjectId id)
    {
        try
        {
            return !id.IsNull && id.IsValid && !id.IsErased;
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static string ReadHandle(ObjectId id)
    {
        try
        {
            return id.IsNull ? "<null>" : id.Handle.ToString();
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadObjectId(ObjectId id)
    {
        try
        {
            return id.ToString();
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadOwnerName(
        Transaction transaction,
        ObjectId ownerId)
    {
        try
        {
            return transaction.GetObject(
                    ownerId,
                    OpenMode.ForRead,
                    true) is BlockTableRecord owner
                ? owner.Name
                : "<not-a-block-table-record>";
        }
        catch (Exception exception) when (
            exception is AcadException or ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadAnnotativeState(TextStyleTableRecord? textStyle)
    {
        if (textStyle is null)
        {
            return "<unavailable>";
        }
        try
        {
            return textStyle.Annotative.ToString();
        }
        catch (AcadException exception)
        {
            return $"NotApplicable/Unavailable ({exception.ErrorStatus})";
        }
        catch (ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadFrameSignature(Entity frame) => frame switch
    {
        Circle circle =>
            $"Circle(diameter={AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(circle.Radius * 2d)})",
        Polyline polyline =>
            $"Polyline(vertices={polyline.NumberOfVertices},closed={polyline.Closed})",
        _ => frame.GetRXClass()?.Name ?? frame.GetType().Name,
    };

    private static bool HasExpectedGeometry(
        Database database,
        Entity frame,
        TimberItemLeaderBlockDefinition definition)
    {
        if (!HasByBlockAppearance(database, frame))
        {
            return false;
        }

        return definition.Style switch
        {
            ItemNumberLeaderStyle.Circle =>
                frame is Circle circle &&
                circle.Center.DistanceTo(Point3d.Origin) <= GeometryTolerance &&
                circle.Normal.IsParallelTo(Vector3d.ZAxis) &&
                Math.Abs(circle.Radius * 2d - definition.WidthMm) <=
                    GeometryTolerance,
            ItemNumberLeaderStyle.Slot =>
                frame is Polyline slot &&
                slot.Closed &&
                slot.NumberOfVertices == 4 &&
                HasExpectedExtents(slot, definition) &&
                Math.Abs(slot.GetBulgeAt(0)) <= AttributeTolerance &&
                Math.Abs(slot.GetBulgeAt(1) - 1d) <= AttributeTolerance &&
                Math.Abs(slot.GetBulgeAt(2)) <= AttributeTolerance &&
                Math.Abs(slot.GetBulgeAt(3) - 1d) <= AttributeTolerance,
            ItemNumberLeaderStyle.Rectangle =>
                frame is Polyline rectangle &&
                rectangle.Closed &&
                rectangle.NumberOfVertices == 4 &&
                HasExpectedExtents(rectangle, definition) &&
                Enumerable.Range(0, 4).All(index =>
                    Math.Abs(rectangle.GetBulgeAt(index)) <= AttributeTolerance),
            _ => false,
        };
    }

    private static bool HasExpectedExtents(
        Entity entity,
        TimberItemLeaderBlockDefinition definition)
    {
        var extents = entity.GeometricExtents;
        return Math.Abs(
                extents.MaxPoint.X - extents.MinPoint.X - definition.WidthMm) <=
                GeometryTolerance &&
            Math.Abs(
                extents.MaxPoint.Y - extents.MinPoint.Y - definition.HeightMm) <=
                GeometryTolerance;
    }

    private static bool HasByBlockAppearance(Database database, Entity entity) =>
        entity.Color.ColorMethod == ColorMethod.ByBlock &&
        entity.LinetypeId == database.ByBlockLinetype &&
        entity.LineWeight == LineWeight.ByBlock &&
        string.Equals(entity.Layer, "0", StringComparison.Ordinal);
}
