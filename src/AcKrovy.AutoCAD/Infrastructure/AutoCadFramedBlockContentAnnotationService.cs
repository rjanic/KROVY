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
/// Geometry comes solely from <see cref="TimberFramedBlockContentLayoutCalculator"/>;
/// BTR identity from <see cref="AcKrovyFramedBlockContentDefinitionService"/>.
/// Ownership metadata is written by the label production router after Create.
/// </summary>
internal static class AutoCadFramedBlockContentAnnotationService
{
#if DEBUG
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        R3CreatePresentationTrace> CreatePresentationTraces =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetCreatePresentationTrace(
        string leaderHandle,
        out R3CreatePresentationTrace trace) =>
        CreatePresentationTraces.TryGetValue(leaderHandle, out trace!);

    internal static void RecordProductionPresentationTrace(
        string leaderHandle,
        R3CreatePresentationTrace trace)
    {
        if (!string.IsNullOrWhiteSpace(leaderHandle))
        {
            CreatePresentationTraces[leaderHandle.Trim()] = trace;
        }
    }
#endif

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
            // R3 Combined: immutable R3_RIGHT / R3_LEFT content variant.
            // CREATE defaults to DesiredWorldSide → RIGHT (dims on landing).
            // Grip STRETCH may later swap LEFT without rewriting leader geometry.
            columnSide = normalized.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? TimberFramedCombinedG5ContentVariantRules.FromWorldSide(
                    TimberFramedCombinedG5CreatePlacementRules.DesiredWorldSide)
                : null;
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
                        TimberFramedBlockContentDefinitionRules
                            .DefaultCombinedDimensionColumnSide));
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
        // Native TransformBy rotates vertices + dogleg together. No K→D→I swap,
        // dogleg normalize rewrite, or full ApplyCanonicalWorldGeometry stack.
        var readable = layout.ReadableAngleRadians;
        if (Math.Abs(readable) > 1e-12d)
        {
            leader.TransformBy(
                Matrix3d.Rotation(readable, Vector3d.ZAxis, attachment));
        }

        var beforeStabilizeAttachment = ReadAttachment(leader);
        var beforeStabilizeKnee = ReadKnee(leader);
        var beforeStabilizeLanding = leader.BlockPosition;

        ApplyStabilization(
            leader,
            attachment,
            request.StabilizationMode);

        // 4) CREATE-only one-shot: after host/API recompute, force FINAL
        // attachment→knee first visible segment to 60° ±0.01° vs readable/source
        // axis. Never runs on grip / refresh / content edit.
        FinalizeCreateFirstSegmentSixtyDegrees(leader, layout);

        // 5) Layer C after TransformBy(readable): BlockRotation stays 0 except
        // the explicit visual-only 90°/180° reference half-turn below.
        // G5C host-proven contract — world orientation comes from TransformBy
        // alone. Setting BlockRotation = NormalizeReadable AFTER TransformBy
        // double-orients AttrDef local X vs landing and puts FRAME between knee
        // and dimensions (ZLÉ / WHITE left). Refresh paths that rebuild points
        // without TransformBy still apply Decide() onto BlockRotation.
        leader.BlockRotation = 0d;

        // 6) After CREATE geometry + presentation are final, re-resolve R3
        // content variant from FINAL world geometry — same helper as
        // post-knee-STRETCH grip. Early DesiredWorldSide pick may not match
        // post-finalize side; BTR swap preserves vertices.
        var resolvedBlockName = definition.ResolvedBlockName;
        var resolvedBlockId = blockId;
        var referencePresentationRevision = 0;
        if (combined)
        {
            EnsureCorrectR3ContentVariantAfterCreateFinalize(
                database,
                transaction,
                leader,
                request,
                ref resolvedBlockId,
                ref resolvedBlockName);
            attributeTags = ApplyAttributeValues(
                transaction,
                leader,
                resolvedBlockId,
                request);
            // Final-WCS authority: BlockRotation is relative to the implicit
            // TransformBy + live BTR basis, not an absolute world angle. Measure
            // that basis after the last variant/AttrRef mutation, then install
            // the requested half-turn as a relative correction.
            var blockRotationBefore = leader.BlockRotation;
            var blockNameBeforeCorrection = resolvedBlockName;
            var hasWorldBefore =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryResolveWorldContentXAxis(
                        transaction,
                        leader,
                        out var worldBefore,
                        out var worldBeforeNote);
            R3CreateReferencePresentationDecision? presentationDecision = null;
            if (hasWorldBefore)
            {
                presentationDecision =
                    TimberFramedBlockContentReadableOrientationRules
                        .ResolveCreateReferenceFinalWorldPresentation(
                            layout.RawAngleRadians,
                            worldBefore,
                            blockRotationBefore);
                leader.BlockRotation = presentationDecision.TargetBlockRotation;
                if (presentationDecision.AppliesHalfTurn)
                {
                    // A 180° content-basis turn reverses local ±X. Re-run the
                    // existing final-geometry R3 resolver so WIDTH/HEIGHT stay
                    // between knee and frame in WCS. The helper preserves all
                    // leader vertices, dogleg, BlockPosition, scale and BR.
                    EnsureCorrectR3ContentVariantAfterCreateFinalize(
                        database,
                        transaction,
                        leader,
                        request,
                        ref resolvedBlockId,
                        ref resolvedBlockName,
                        presentationDecision.FinalWorldPresentation);
                    // The shared swap helper restores the presentation it
                    // captured from AttrRef/BR. CREATE already owns the exact
                    // final-WCS target, so reassert that relative actuator after
                    // the content-only BTR swap.
                    leader.BlockRotation =
                        presentationDecision.TargetBlockRotation;
                }

                leader.RecordGraphicsModified(true);
            }

            var hasWorldAfter =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryResolveWorldContentXAxis(
                        transaction,
                        leader,
                        out var worldAfter,
                        out var worldAfterNote);
            var hasPlacementAfter =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryEvaluate(
                        transaction,
                        leader,
                        out var placementAfter,
                        out var pointsAfter,
                        out var placementAfterNote);
            var dimensionsCenterAfter = hasPlacementAfter
                ? new TimberPlanarPoint(
                    (pointsAfter.WidthAlignment.X + pointsAfter.HeightAlignment.X) /
                    2d,
                    (pointsAfter.WidthAlignment.Y + pointsAfter.HeightAlignment.Y) /
                    2d)
                : default;
            var towardKneeDotAfter = double.NaN;
            var hasTowardKneeDotAfter = hasPlacementAfter &&
                TimberFramedBlockContentDefinitionRules
                    .TryEvaluateDimensionsTowardKneeDot(
                        new TimberPlanarPoint(
                            pointsAfter.ItemAlignment.X,
                            pointsAfter.ItemAlignment.Y),
                        new TimberPlanarPoint(
                            pointsAfter.Knee.X,
                            pointsAfter.Knee.Y),
                        dimensionsCenterAfter,
                        out towardKneeDotAfter);
            if (presentationDecision?.AppliesReferenceRule == true &&
                hasWorldAfter &&
                hasPlacementAfter &&
                placementAfter.Current.IsCorrect &&
                Math.Abs(
                    TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                        worldAfter - presentationDecision.VerticalRuleOutput)) <=
                    TimberFramedBlockContentReadableOrientationRules
                        .AngleToleranceRadians)
            {
                referencePresentationRevision =
                    TimberFramedBlockContentReadableOrientationRules
                        .ReferencePresentationRevision;
            }
#if DEBUG
            var handle = leader.ObjectId.Handle.ToString();
            CreatePresentationTraces[handle] = new R3CreatePresentationTrace(
                SourceHandle: null,
                SourcePhysicalAxisAngle: layout.RawAngleRadians,
                VerticalRuleInput: presentationDecision?.VerticalRuleInput,
                VerticalRuleOutput: presentationDecision?.VerticalRuleOutput,
                TransformByAngle: readable,
                BlockRotationBefore: blockRotationBefore,
                BlockRotationRequested:
                    presentationDecision?.TargetBlockRotation ??
                    blockRotationBefore,
                BlockRotationAfter: leader.BlockRotation,
                FrameWorldOrientationBefore: hasWorldBefore ? worldBefore : null,
                FrameWorldOrientationAfter: hasWorldAfter ? worldAfter : null,
                ItemTextWorldAngle: TryReadAttributeRotationRadians(
                    transaction,
                    leader,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag),
                WidthTextWorldAngle: TryReadAttributeRotationRadians(
                    transaction,
                    leader,
                    TimberFramedBlockContentDefinitionRules.WidthTag),
                HeightTextWorldAngle: TryReadAttributeRotationRadians(
                    transaction,
                    leader,
                    TimberFramedBlockContentDefinitionRules.HeightTag),
                AppliedHalfTurn: presentationDecision?.AppliesHalfTurn ?? false,
                BlockNameBeforeCorrection: blockNameBeforeCorrection,
                BlockNameAfterCorrection: resolvedBlockName,
                ContentVariant: resolvedBlockName,
                DimensionsTowardKneeAfter:
                    hasPlacementAfter && placementAfter.Current.IsCorrect,
                DimensionsTowardKneeDot:
                    hasTowardKneeDotAfter ? towardKneeDotAfter : null,
                PresentationPath: "Create",
                PresentationOperationSequence:
                    "CreateLeader>TransformBy>Finalize60>R3Variant>AttrRefs>" +
                    "MeasureWorld>WorldDelta>R3Variant>ReassertBR>MeasureFinal",
                ReferenceRevisionBefore: 0,
                ReferenceRevisionAfter: referencePresentationRevision,
                MeasurementNote:
                    $"before={worldBeforeNote}; after={worldAfterNote}; " +
                    $"placement={placementAfterNote} " +
                    (hasPlacementAfter
                        ? placementAfter.Current.Reason
                        : "<unmeasured>"));
#endif
        }

        if (combined)
        {
            if (!AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(
                    transaction,
                    leader,
                    layout.RawAngleRadians,
                    referencePresentationRevision,
                    "Create",
                    out var wholeAnnotationOperation,
                    out var wholeAnnotationReason))
            {
                leader.Erase();
                throw new InvalidOperationException(
                    "Whole-MLeader vertical CREATE correction failed: " +
                    wholeAnnotationReason);
            }

            referencePresentationRevision =
                wholeAnnotationOperation.Decision.RevisionAfter;
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
            referencePresentationRevision,
            "Created one BlockContent MLeader.");
    }

    /// <summary>
    /// After CREATE 60° + straight-landing finalize: ensure R3_RIGHT/LEFT matches
    /// FINAL attachment→BlockPosition in raw ElementAxis (Start→End) basis.
    /// Reuses the same production helper as post-knee-STRETCH grip. Never rewrites
    /// leader vertices, dogleg, or BlockPosition (swap preserves geometry).
    /// </summary>
    private static void EnsureCorrectR3ContentVariantAfterCreateFinalize(
        Database database,
        Transaction transaction,
        MLeader leader,
        AutoCadFramedBlockContentAnnotationRequest request,
        ref ObjectId resolvedBlockId,
        ref string? resolvedBlockName,
        double? effectiveContentWorldAngleRadians = null)
    {
        var finalAttachment = ReadAttachment(leader);
        ResolveSourceBasisFromElementAxis(
            finalAttachment.X,
            finalAttachment.Y,
            request.ElementAxisRadians,
            out var startX,
            out var startY,
            out var endX,
            out var endY);

        var attributeValues = CollectAttributeValues(request);
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .EnsureCorrectR3ContentVariantFromFinalGeometry(
                    database,
                    transaction,
                    leader,
                    startX,
                    startY,
                    endX,
                    endY,
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
                    out var afterBlockId,
                    out _,
                    effectiveContentWorldAngleRadians))
        {
            return;
        }

        if (afterBlockId.IsNull || afterBlockId == resolvedBlockId)
        {
            return;
        }

        resolvedBlockId = afterBlockId;
        if (transaction.GetObject(afterBlockId, OpenMode.ForRead, true) is
            BlockTableRecord afterBlock)
        {
            resolvedBlockName = afterBlock.Name;
        }
    }

    /// <summary>
    /// Synthetic Start→End from raw element axis through attachment — same T/N
    /// basis ElementLabelService / grip use from the source Line/Polyline.
    /// </summary>
    private static void ResolveSourceBasisFromElementAxis(
        double attachmentX,
        double attachmentY,
        double elementAxisRadians,
        out double startX,
        out double startY,
        out double endX,
        out double endY)
    {
        var cos = Math.Cos(elementAxisRadians);
        var sin = Math.Sin(elementAxisRadians);
        const double span = 1000d;
        startX = attachmentX - (cos * span);
        startY = attachmentY - (sin * span);
        endX = attachmentX + (cos * span);
        endY = attachmentY + (sin * span);
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
    /// CREATE-only: reload FINAL world attachment/knee after insert + style +
    /// dogleg/landing/BlockPosition/TransformBy/stabilize, then correct the
    /// first visible leader segment to 60° when needed and always re-seat
    /// landing along readable +T from the final knee (same length). Keeping a
    /// stale BlockPosition after knee correction (or host dogleg rewrite) tilts
    /// the second segment and leaves WIDTH/HEIGHT beside the frame instead of
    /// on the landing axis.
    /// </summary>
    private static void FinalizeCreateFirstSegmentSixtyDegrees(
        MLeader leader,
        TimberFramedBlockContentLayout layout)
    {
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var leaderIndex = leader.GetLeaderIndexes().Cast<int>().First();
        var attachment = ReadAttachment(leader);
        var actualKnee = ReadKnee(leader);
        var preservedDoglegLength = leader.DoglegLength;
        var landingLength = preservedDoglegLength > 1e-9d
            ? preservedDoglegLength
            : layout.LandingLengthModelMm;

        if (!TimberFramedCombinedG5CreateFirstSegmentRules
                .TryResolveCreateFinalizationFromReadableAxis(
                    new TimberPlanarPoint(attachment.X, attachment.Y),
                    new TimberPlanarPoint(actualKnee.X, actualKnee.Y),
                    layout.ReadableAngleRadians,
                    layout.SideSign,
                    out var correctedKnee,
                    out _,
                    out var changed))
        {
            return;
        }

        var finalKnee = changed
            ? correctedKnee
            : new TimberPlanarPoint(actualKnee.X, actualKnee.Y);
        var correctedLanding =
            TimberFramedCombinedG5CreateFirstSegmentRules.BuildCorrectedLandingEnd(
                finalKnee,
                layout.ReadableAngleRadians,
                landingLength);

        // Skip host writes only when knee already 60° AND landing already on +T.
        if (!changed &&
            TimberFramedCombinedG5CreateFirstSegmentRules
                .TryMeasureLandingSegmentAngleToReadableDeg(
                    finalKnee,
                    new TimberPlanarPoint(
                        leader.BlockPosition.X,
                        leader.BlockPosition.Y),
                    layout.ReadableAngleRadians,
                    out var existingLandingAngle) &&
            TimberFramedCombinedG5CreateFirstSegmentRules
                .LandingSegmentIsStraightAlongReadable(existingLandingAngle))
        {
            return;
        }

        var knee = new Point3d(finalKnee.X, finalKnee.Y, actualKnee.Z);
        var landingEnd = new Point3d(
            correctedLanding.X,
            correctedLanding.Y,
            leader.BlockPosition.Z);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);

        // Second segment along readable +T from final knee — not knee→old BP.
        ApplyCreateDogleg(
            leader,
            leaderIndex,
            knee,
            landingEnd,
            landingLength);
        leader.BlockPosition = landingEnd;
        leader.SetLastVertex(lineIndex, knee);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.EnableDogleg = true;
        leader.EnableLanding = true;
        leader.ExtendLeaderToText = false;
        leader.RecordGraphicsModified(true);
    }

    /// <summary>
    /// Legacy helper retained for DEBUG/tests — not called from production CREATE.
    /// </summary>
    private static void ApplyCanonicalWorldGeometry(
        MLeader leader,
        TimberFramedBlockContentLayout layout)
    {
        var attachment = ToPoint(layout.AttachmentLocal);
        var readable = layout.ReadableAngleRadians;
        var cos = Math.Cos(readable);
        var sin = Math.Sin(readable);

        Point3d Rotate(TimberPlanarPoint point)
        {
            var dx = point.X - layout.AttachmentLocal.X;
            var dy = point.Y - layout.AttachmentLocal.Y;
            return new Point3d(
                attachment.X + (dx * cos) - (dy * sin),
                attachment.Y + (dx * sin) + (dy * cos),
                0d);
        }

        var knee = Rotate(layout.KneeLocal);
        var landingEnd = Rotate(layout.LandingEndLocal);
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var leaderIndex = leader.GetLeaderIndexes().Cast<int>().First();

        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);
        ApplyCreateDogleg(
            leader,
            leaderIndex,
            knee,
            landingEnd,
            layout.LandingLengthModelMm);
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.EnableDogleg = true;
        leader.EnableLanding = true;
        leader.BlockRotation = 0d;
        leader.ExtendLeaderToText = false;
        leader.LandingGap = 0d;
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

        // B / C: graphics refresh only — do not rewrite dogleg/landing after
        // create. Native TransformBy owns world geometry. EpsilonRotate (D)
        // remains DEBUG/test-only and re-syncs dogleg after the ±eps pair.
        leader.RecordGraphicsModified(true);

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

    internal static double? TryReadAttributeRotationRadians(
        Transaction transaction,
        MLeader leader,
        string tag)
    {
        if (leader.BlockContentId.IsNull ||
            transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord block)
        {
            return null;
        }

        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                !string.Equals(
                    definition.Tag,
                    tag,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            return TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                attribute.Rotation);
        }

        return null;
    }
}

#if DEBUG
internal sealed record R3CreatePresentationTrace(
    string? SourceHandle,
    double SourcePhysicalAxisAngle,
    double? VerticalRuleInput,
    double? VerticalRuleOutput,
    double TransformByAngle,
    double BlockRotationBefore,
    double BlockRotationRequested,
    double BlockRotationAfter,
    double? FrameWorldOrientationBefore,
    double? FrameWorldOrientationAfter,
    double? ItemTextWorldAngle,
    double? WidthTextWorldAngle,
    double? HeightTextWorldAngle,
    bool AppliedHalfTurn,
    string? BlockNameBeforeCorrection,
    string? BlockNameAfterCorrection,
    string? ContentVariant,
    bool DimensionsTowardKneeAfter,
    double? DimensionsTowardKneeDot,
    string PresentationPath,
    string PresentationOperationSequence,
    int ReferenceRevisionBefore,
    int ReferenceRevisionAfter,
    string MeasurementNote);
#endif
