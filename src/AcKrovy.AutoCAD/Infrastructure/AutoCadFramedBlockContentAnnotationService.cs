using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production create path for one native G5 BlockContent MLeader.
/// Standalone create API — not wired into label production routing yet.
/// Geometry comes solely from <see cref="TimberFramedBlockContentLayoutCalculator"/>;
/// BTR identity from <see cref="AcKrovyFramedBlockContentDefinitionService"/>.
/// </summary>
internal static class AutoCadFramedBlockContentAnnotationService
{
    public static bool TryCreate(
        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentAnnotationRequest request,
        out AutoCadFramedBlockContentAnnotationResult result)
    {
        result = Create(database, transaction, request);
        return result.Succeeded;
    }

    public static AutoCadFramedBlockContentAnnotationResult Create(
        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentAnnotationRequest request)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);

        AutoCadFramedBlockContentAnnotationRequest normalized;
        try
        {
            normalized = request.Normalize();
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException or
                InvalidOperationException)
        {
            return AutoCadFramedBlockContentAnnotationResult.Fail(
                AutoCadFramedBlockContentAnnotationResultKind.InvalidRequest,
                request.StabilizationMode,
                exception.Message);
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, normalized.ItemTextStyleId) ||
            (normalized.Presentation ==
                TimberFramedBlockContentPresentation.Combined &&
             !AutoCadDatabaseIdentity.IsSame(
                 database,
                 normalized.DimensionTextStyleId)) ||
            !AutoCadDatabaseIdentity.IsSame(database, normalized.LayerId))
        {
            return AutoCadFramedBlockContentAnnotationResult.Fail(
                AutoCadFramedBlockContentAnnotationResultKind.DatabaseMismatch,
                normalized.StabilizationMode,
                "Request ObjectId belongs to a different database.");
        }

        TimberFramedBlockContentLayout layout;
        TimberFramedBlockContentDimensionColumnSide? columnSide = null;
        try
        {
            // Canonical create landing is +T; resolve column side from planned
            // knee → BlockPosition so Combined WIDTH/HEIGHT stay toward the knee.
            columnSide = ResolveCreateDimensionColumnSide(normalized);
            layout = TimberFramedBlockContentLayoutCalculator.Calculate(
                new TimberFramedBlockContentLayoutRequest(
                    normalized.AttachmentX,
                    normalized.AttachmentY,
                    normalized.ElementAxisRadians,
                    normalized.Side,
                    normalized.ContentKind,
                    normalized.FrameWidthMm,
                    normalized.FrameHeightMm,
                    normalized.AnnotationScaleDenominator,
                    normalized.ItemPaperHeightMm,
                    normalized.DimensionPaperHeightMm,
                    normalized.FirstSegmentLengthModelMm,
                    normalized.LandingLengthModelMm,
                    normalized.DimensionColumnEnvelopeWidthMm,
                    normalized.Presentation,
                    columnSide ??
                        TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException or
                InvalidOperationException)
        {
            return AutoCadFramedBlockContentAnnotationResult.Fail(
                AutoCadFramedBlockContentAnnotationResultKind.InvalidRequest,
                normalized.StabilizationMode,
                exception.Message);
        }

        var definitionRequest = new AutoCadFramedBlockContentRequest(
            normalized.ContentKind,
            normalized.Presentation,
            normalized.ItemTextStyleName,
            normalized.DimensionTextStyleName,
            normalized.ItemPaperHeightMm,
            normalized.DimensionPaperHeightMm,
            normalized.ItemTextStyleId,
            normalized.DimensionTextStyleId,
            normalized.ItemNoText,
            columnSide);
        var definition = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            definitionRequest);
        if (!definition.Succeeded ||
            definition.BlockTableRecordId is not ObjectId blockId ||
            blockId.IsNull)
        {
            return AutoCadFramedBlockContentAnnotationResult.Fail(
                AutoCadFramedBlockContentAnnotationResultKind.DefinitionFailed,
                normalized.StabilizationMode,
                definition.DiagnosticReason);
        }

        try
        {
            return CreateLeader(
                database,
                transaction,
                normalized,
                definition,
                blockId,
                layout);
        }
        catch (Exception exception) when (
            exception is AcadException or InvalidOperationException)
        {
            var detail = exception is AcadException acad
                ? $"{acad.ErrorStatus}: {acad.Message}"
                : exception.Message;
            return AutoCadFramedBlockContentAnnotationResult.Fail(
                AutoCadFramedBlockContentAnnotationResultKind.HostFailure,
                normalized.StabilizationMode,
                detail);
        }
    }

    /// <summary>
    /// Create-time content vector is landingEnd − knee in canonical local space
    /// (T = +X). Positive local X content → NegativeLocalX dimensions.
    /// </summary>
    private static TimberFramedBlockContentDimensionColumnSide?
        ResolveCreateDimensionColumnSide(
            AutoCadFramedBlockContentAnnotationRequest request)
    {
        if (request.Presentation != TimberFramedBlockContentPresentation.Combined)
        {
            return null;
        }

        // Canonical layout landing is along +T before TransformBy.
        return TimberFramedBlockContentDefinitionRules
            .ResolveDimensionColumnSideFromContentLocalX(
                request.LandingLengthModelMm);
    }

    private static AutoCadFramedBlockContentAnnotationResult CreateLeader(

        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentAnnotationRequest request,
        AutoCadFramedBlockContentResult definition,
        ObjectId blockId,
        TimberFramedBlockContentLayout layout)
    {
        var attachment = ToPoint(layout.AttachmentLocal);
        var knee = ToPoint(layout.KneeLocal);
        var landingEnd = ToPoint(layout.LandingEndLocal);
        var combined =
            request.Presentation == TimberFramedBlockContentPresentation.Combined;
        var blockScale = request.BlockScale;

        var styleId = combined
            ? AcKrovyMLeaderStyleService.EnsureCombinedFramed(
                database,
                transaction,
                updateExisting: false)
            : AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExisting: false);

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);

        // 1) Canonical horizontal MLeader (BlockRotation = 0).
        // G5C confirmed host contract: ConnectBase only — never flash
        // ConnectExtents (legacy ApplyCombinedBlockInstanceProperties does).
        // ConnectExtents + WIDTH/HEIGHT AttrDefs left of origin make AutoCAD
        // classify Left (knee −N) differently from Right; grip STRETCH then
        // reseats AttrRefs and drifts WIDTH/HEIGHT while ITEM_NO stays put.
        // DoglegDirection/BlockPosition come from layout landing (BlockPosition −
        // knee / ConnectBase), never LeaderKneeSide → ±T (cancelled rule).
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.MLeaderStyle = styleId;
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(blockScale);
        leader.BlockRotation = 0d;
        leader.EnableDogleg = true;
        leader.EnableLanding = true;
        leader.DoglegLength = layout.LandingLengthModelMm;
        leader.ExtendLeaderToText = false;
        leader.LandingGap = 0d;

        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(lineIndex, attachment);
        leader.AddLastVertex(lineIndex, knee);

        ApplyG5BlockInstanceLineProperties(
            leader,
            database,
            transaction,
            leaderIndex,
            lineIndex,
            combined,
            blockScale);

        // Canonical local T = +X (landingEnd − knee). Content dogleg from landing.
        ApplyCreateDogleg(
            leader,
            leaderIndex,
            knee,
            landingEnd,
            layout.LandingLengthModelMm);
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.EnableDogleg = true;
        leader.EnableLanding = true;
        leader.BlockScale = new Scale3d(blockScale);
        leader.BlockRotation = 0d;

        ApplyByLayer(leader, request.LayerId, database);
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);

        // 2) Reassert vertices after append, then SetBlockAttribute VALUE/height
        // only — never bake world Position/AlignmentPoint onto AttrRefs.
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);
        ApplyCreateDogleg(
            leader,
            leaderIndex,
            knee,
            landingEnd,
            layout.LandingLengthModelMm);
        var attributeTags = ApplyAttributeValues(
            transaction,
            leader,
            blockId,
            request);

        // 3) One final world rotation around attachment pivot (G5C order).
        var readable = layout.ReadableAngleRadians;
        if (Math.Abs(readable) > 1e-12d)
        {
            leader.TransformBy(
                Matrix3d.Rotation(readable, Vector3d.ZAxis, attachment));
        }

        // After TransformBy, sync DoglegDirection from BlockPosition − knee.
        // Mirror only when landing points toward attachment (bad LEFT).
        ApplyNormalizeDoglegFromLeader(leader, layout.LandingLengthModelMm);

        var beforeStabilizeAttachment = ReadAttachment(leader);
        var beforeStabilizeKnee = ReadKnee(leader);
        var beforeStabilizeLanding = leader.BlockPosition;

        ApplyStabilization(
            leader,
            attachment,
            request.StabilizationMode);

        var resolvedBlockName = definition.ResolvedBlockName;
        var resolvedBlockId = blockId;
        if (combined)
        {
            var attributeValues = CollectAttributeValues(request);
            if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryCorrectCombinedContentSide(
                        database,
                        transaction,
                        leader,
                        request.ContentKind,
                        request.ItemTextStyleName,
                        request.DimensionTextStyleName,
                        request.ItemTextStyleId,
                        request.DimensionTextStyleId,
                        request.ItemPaperHeightMm,
                        request.DimensionPaperHeightMm,
                        request.ItemNoText,
                        attributeValues,
                        out _,
                        out _,
                        out var correctedBlockId,
                        out var placement,
                        out var placementNote) ||
                !placement.Current.IsCorrect)
            {
                return AutoCadFramedBlockContentAnnotationResult.Fail(
                    AutoCadFramedBlockContentAnnotationResultKind.HostFailure,
                    request.StabilizationMode,
                    "Combined K→D→I column placement failed: " + placementNote);
            }

            resolvedBlockId = correctedBlockId;
            if (transaction.GetObject(correctedBlockId, OpenMode.ForRead, true) is
                BlockTableRecord correctedBlock)
            {
                resolvedBlockName = correctedBlock.Name;
            }

            // Reaffirm values/heights after optional BlockContentId swap.
            attributeTags = ApplyAttributeValues(
                transaction,
                leader,
                correctedBlockId,
                request);
        }

        var afterAttachment = ReadAttachment(leader);
        var afterKnee = ReadKnee(leader);
        var afterLanding = leader.BlockPosition;

        return new AutoCadFramedBlockContentAnnotationResult(
            AutoCadFramedBlockContentAnnotationResultKind.Created,
            leader.ObjectId,
            leader.ObjectId.Handle.ToString(),
            resolvedBlockName,
            resolvedBlockId,
            leader.ContentType,
            leader.GetLeaderIndexes().Cast<int>().Count(),
            CountVertices(leader),
            attributeTags,
            request.ItemAttributeBaselineHeightMm,
            request.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? request.DimensionAttributeBaselineHeightMm
                : double.NaN,
            request.ItemEffectiveModelHeightMm,
            request.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? request.DimensionEffectiveModelHeightMm
                : double.NaN,
            blockScale,
            afterAttachment,
            afterKnee,
            afterLanding,
            readable,
            layout.RowClearGapModelMm,
            request.StabilizationMode,
            beforeStabilizeAttachment.DistanceTo(afterAttachment),
            beforeStabilizeKnee.DistanceTo(afterKnee),
            beforeStabilizeLanding.DistanceTo(afterLanding),
            "Created one BlockContent MLeader.");
    }

    private static IReadOnlyList<(string Tag, string Text, double Height)>
        CollectAttributeValues(AutoCadFramedBlockContentAnnotationRequest request)
    {
        var values = new List<(string Tag, string Text, double Height)>
        {
            (
                TimberFramedBlockContentDefinitionRules.ItemNoTag,
                request.ItemNoText,
                request.ItemAttributeBaselineHeightMm),
        };
        if (request.Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            values.Add((
                TimberFramedBlockContentDefinitionRules.WidthTag,
                request.WidthText,
                request.DimensionAttributeBaselineHeightMm));
            values.Add((
                TimberFramedBlockContentDefinitionRules.HeightTag,
                request.HeightText,
                request.DimensionAttributeBaselineHeightMm));
        }

        return values;
    }

    private static IReadOnlyList<string> ApplyAttributeValues(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        AutoCadFramedBlockContentAnnotationRequest request)
    {
        var tags = new List<string>();
        var block = (BlockTableRecord)transaction.GetObject(
            blockId,
            OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            string? text = null;
            double height = double.NaN;
            if (string.Equals(
                    definition.Tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                text = request.ItemNoText;
                height = request.ItemAttributeBaselineHeightMm;
            }
            else if (
                request.Presentation ==
                    TimberFramedBlockContentPresentation.Combined &&
                string.Equals(
                    definition.Tag,
                    TimberFramedBlockContentDefinitionRules.WidthTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                text = request.WidthText;
                height = request.DimensionAttributeBaselineHeightMm;
            }
            else if (
                request.Presentation ==
                    TimberFramedBlockContentPresentation.Combined &&
                string.Equals(
                    definition.Tag,
                    TimberFramedBlockContentDefinitionRules.HeightTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                text = request.HeightText;
                height = request.DimensionAttributeBaselineHeightMm;
            }
            else
            {
                continue;
            }

            // G5C contract: SetAttributeFromBlock + TextString + Height only.
            // Do not assign Position/AlignmentPoint (would bake absolute space
            // and detach WIDTH/HEIGHT from BlockContent on grip edits).
            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = text;
            attribute.Height = height;
            // TextStyleId stays on AttrDef (P2 variant key). Do not mutate
            // shared BTR; per-instance TextStyleId is not relied upon.
            leader.SetBlockAttribute(definition.ObjectId, attribute);
            tags.Add(definition.Tag.ToUpperInvariant());
        }

        return tags;
    }

    /// <summary>
    /// Line/arrow cosmetics only. Never sets ConnectExtents, Side-based ±X
    /// dogleg, or DoglegLength from legacy CombinedFramedLandingDistanceMm.
    /// </summary>
    private static void ApplyG5BlockInstanceLineProperties(
        MLeader leader,
        Database database,
        Transaction transaction,
        int leaderIndex,
        int leaderLineIndex,
        bool combined,
        double presentationScaleFactor)
    {
        _ = leaderIndex;
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.LeaderLineType = LeaderType.StraightLeader;
        leader.LeaderLineColor = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        leader.LeaderLineTypeId = database.ByBlockLinetype;
        leader.LeaderLineWeight = LineWeight.ByBlock;
        leader.SetLeaderLineType(leaderLineIndex, LeaderType.StraightLeader);
        leader.SetLeaderLineColor(
            leaderLineIndex,
            AcColor.FromColorIndex(ColorMethod.ByBlock, 0));
        leader.SetLeaderLineTypeId(leaderLineIndex, database.ByBlockLinetype);
        leader.SetLeaderLineWeight(leaderLineIndex, LineWeight.ByBlock);

        if (combined)
        {
            leader.ArrowSymbolId = ObjectId.Null;
            leader.ArrowSize =
                TimberNativeLeaderStyleRules.CombinedFramedSettings.ArrowheadSize *
                presentationScaleFactor;
        }
        else
        {
            leader.ArrowSymbolId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
                database,
                transaction);
            leader.ArrowSize =
                TimberNativeLeaderStyleRules.FramedSettings.ArrowheadSize *
                presentationScaleFactor;
        }
    }

    /// <summary>
    /// Create-path dogleg from layout landing (BlockPosition − knee).
    /// Does not use LeaderKneeSide → ±T.
    /// </summary>
    private static void ApplyCreateDogleg(
        MLeader leader,
        int leaderIndex,
        Point3d knee,
        Point3d landingEnd,
        double doglegLengthMm)
    {
        var length = doglegLengthMm > 1e-9d
            ? doglegLengthMm
            : leader.DoglegLength;
        if (length <= 1e-9d)
        {
            length = knee.DistanceTo(landingEnd);
        }

        if (!TimberFramedBlockContentDoglegRules.TryResolveCreateDoglegGeometry(
                new TimberPlanarPoint(knee.X, knee.Y),
                new TimberPlanarPoint(landingEnd.X, landingEnd.Y),
                out var doglegDirection,
                out var blockPosition))
        {
            var fallback = ResolveCanonicalTangent(landingEnd, knee);
            leader.DoglegLength = length > 1e-9d ? length : 1d;
            leader.BlockPosition = landingEnd;
            leader.SetDogleg(leaderIndex, fallback);
            leader.BlockConnectionType = BlockConnectionType.ConnectBase;
            return;
        }

        if (length <= 1e-9d)
        {
            length = knee.DistanceTo(landingEnd);
        }

        var direction = new Vector3d(doglegDirection.X, doglegDirection.Y, 0d);
        leader.DoglegLength = length;
        leader.BlockPosition = new Point3d(blockPosition.X, blockPosition.Y, 0d);
        leader.SetDogleg(leaderIndex, direction);
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
    }

    /// <summary>
    /// Post-transform / refresh: sync DoglegDirection from BlockPosition − knee.
    /// Mirrors BlockPosition across knee only when landing points toward the
    /// attachment. Never rewrites BlockPosition as knee + dir × DoglegLength.
    /// </summary>
    private static void ApplyNormalizeDoglegFromLeader(
        MLeader leader,
        double? preferredDoglegLengthMm = null)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            return;
        }

        var leaderIndex = leaderIndexes[0];
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var attachment = ReadAttachment(leader);
        var knee = ReadKnee(leader);
        var blockPosition = leader.BlockPosition;
        var length = preferredDoglegLengthMm is > 1e-9d
            ? preferredDoglegLengthMm.Value
            : leader.DoglegLength;

        if (!TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                new TimberPlanarPoint(attachment.X, attachment.Y),
                new TimberPlanarPoint(knee.X, knee.Y),
                new TimberPlanarPoint(blockPosition.X, blockPosition.Y),
                out var doglegDirection,
                out var normalizedBlockPosition,
                out var mirrored))
        {
            return;
        }

        var direction = new Vector3d(doglegDirection.X, doglegDirection.Y, 0d);
        if (length > 1e-9d)
        {
            leader.DoglegLength = length;
        }

        leader.SetDogleg(leaderIndex, direction);
        if (mirrored)
        {
            leader.BlockPosition = new Point3d(
                normalizedBlockPosition.X,
                normalizedBlockPosition.Y,
                blockPosition.Z);
            // Changing BlockPosition can drag the knee under ConnectBase —
            // reassert the captured knee (API order B).
            leader.SetLastVertex(lineIndex, knee);
            leader.SetFirstVertex(lineIndex, attachment);
            leader.SetDogleg(leaderIndex, direction);
            if (length > 1e-9d)
            {
                leader.DoglegLength = length;
            }
        }

        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
    }

    /// <summary>
    /// Canonical annotation tangent from layout landing (+T). Falls back to +X.
    /// </summary>
    private static Vector3d ResolveCanonicalTangent(Point3d landingEnd, Point3d knee)
    {
        var landingDirection = landingEnd - knee;
        return landingDirection.Length > 1e-9d
            ? landingDirection.GetNormal()
            : Vector3d.XAxis;
    }

    private static void ApplyStabilization(
        MLeader leader,
        Point3d attachmentPivot,
        AutoCadFramedBlockContentStabilizationMode mode)
    {
        if (mode == AutoCadFramedBlockContentStabilizationMode.CreateOrderOnly)
        {
            return;
        }

        // B / C / D all start with a non-geometry graphics refresh.
        leader.RecordGraphicsModified(true);
        ApplyNormalizeDoglegFromLeader(leader);

        if (mode != AutoCadFramedBlockContentStabilizationMode.EpsilonRotate)
        {
            return;
        }

        // D: DEBUG/test-only ±1° around attachment — not production default.
        var eps = AutoCadFramedBlockContentAnnotationRequest.EpsilonRotateRadians;
        leader.TransformBy(
            Matrix3d.Rotation(eps, Vector3d.ZAxis, attachmentPivot));
        leader.TransformBy(
            Matrix3d.Rotation(-eps, Vector3d.ZAxis, attachmentPivot));
        ApplyNormalizeDoglegFromLeader(leader);
        leader.RecordGraphicsModified(true);
    }

    private static void ApplyByLayer(
        Entity entity,
        ObjectId layerId,
        Database database)
    {
        entity.LayerId = layerId;
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        entity.LinetypeId = database.ByLayerLinetype;
        entity.LinetypeScale = 1d;
        entity.LineWeight = LineWeight.ByLayer;
    }

    private static Point3d ToPoint(TimberPlanarPoint point) =>
        new(point.X, point.Y, 0d);

    private static int GetPrimaryLeaderLineIndex(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leaders.");
        }

        var lineIndexes = leader
            .GetLeaderLineIndexes(leaderIndexes[0])
            .Cast<int>()
            .ToArray();
        if (lineIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leader lines.");
        }

        return lineIndexes[0];
    }

    private static Point3d ReadAttachment(MLeader leader) =>
        leader.GetFirstVertex(GetPrimaryLeaderLineIndex(leader));

    private static Point3d ReadKnee(MLeader leader) =>
        leader.GetLastVertex(GetPrimaryLeaderLineIndex(leader));

    private static int CountVertices(MLeader leader)
    {
        var total = 0;
        foreach (int leaderIndex in leader.GetLeaderIndexes())
        {
            foreach (int lineIndex in leader.GetLeaderLineIndexes(leaderIndex))
            {
                total += leader.VerticesCount(lineIndex);
            }
        }

        return total;
    }
}
