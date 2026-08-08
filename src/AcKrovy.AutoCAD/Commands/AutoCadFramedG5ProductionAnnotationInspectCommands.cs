#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG inspect for production framed Combined G5 annotations.
/// </summary>
public static class AutoCadFramedG5ProductionAnnotationInspectCommands
{
    [CommandMethod("AK_DEV_G5_PRODUCTION_ANNOTATION_INSPECT")]
    public static void Inspect()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var selection = editor.GetSelection();
        if (selection.Status != PromptStatus.OK)
        {
            editor.WriteMessage("\nNo selection.");
            return;
        }

        using var documentLock = document.LockDocument();
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        foreach (SelectedObject selected in selection.Value)
        {
            if (selected is null ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    selected.ObjectId,
                    OpenMode.ForRead,
                    out var entity) ||
                entity is null)
            {
                continue;
            }

            WriteEntity(editor, transaction, entity);
        }

        transaction.Commit();
    }

    private static void WriteEntity(
        Editor editor,
        Transaction transaction,
        Entity entity)
    {
        ElementLabelStore.TryRead(entity, out var data);
        var sourceHandle = data?.SourceHandle ?? "<none>";
        var role = data?.ComponentRole.ToString() ?? "<none>";
        var generation = data?.RendererGeneration?.ToString() ?? "<none>";
        var entityCount = 0;
        if (!string.IsNullOrWhiteSpace(data?.SourceHandle) &&
            data.ComponentRole ==
                AutoCadFramedBlockContentProductionPolicy.CombinedRole)
        {
            entityCount = CountOwnedForRole(
                transaction,
                entity.Database,
                data.SourceHandle,
                data.ComponentRole);
        }

        var mleader = entity as MLeader;
        var contentType = mleader?.ContentType.ToString() ?? entity.GetType().Name;
        var blockVariant = "<n/a>";
        var contentVariant = "<n/a>";
        var rendererRevision = "<n/a>";
        if (mleader is not null &&
            mleader.ContentType == ContentType.BlockContent &&
            !mleader.BlockContentId.IsNull &&
            transaction.GetObject(mleader.BlockContentId, OpenMode.ForRead, true) is
                BlockTableRecord block)
        {
            blockVariant = block.Name;
            if (TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                    block.Name,
                    out var r3Parse))
            {
                rendererRevision = TimberFramedBlockContentVariantRules.FamilyRevisionToken;
                if (r3Parse.ContentVariantSide is
                    TimberFramedBlockContentDimensionColumnSide side)
                {
                    contentVariant =
                        TimberFramedCombinedG5ContentVariantRules.ToContentVariantToken(
                            side);
                }
                else if (r3Parse.IsCombined)
                {
                    contentVariant = "LEGACY_SIDELESS";
                }
                else
                {
                    contentVariant = "ITEM";
                }
            }
            else if (TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                         block.Name,
                         out _))
            {
                rendererRevision = "R2";
            }
        }

        var legacyOwned = 0;
        if (!string.IsNullOrWhiteSpace(data?.SourceHandle))
        {
            legacyOwned = CountLegacyOwned(transaction, entity.Database, data.SourceHandle);
        }

        editor.WriteMessage(
            $"\n=== AK_DEV_G5_PRODUCTION_ANNOTATION_INSPECT ===" +
            $"\nSourceHandle={sourceHandle}" +
            $"\nrole={role}" +
            $"\nRendererGeneration={generation}" +
            $"\nRendererRevision={rendererRevision}" +
            $"\nContentVariant={contentVariant}" +
            $"\nEntityCount={entityCount}" +
            $"\nEntityCountForRole={entityCount}" +
            $"\nMLeaderHandle={entity.Handle}" +
            $"\nContentType={contentType}" +
            $"\nBlockVariant={blockVariant}" +
            $"\nLegacyOwnedPartCount={legacyOwned}" +
            $"\nDesiredWorldSide={TimberFramedCombinedG5CreatePlacementRules.DesiredWorldSide}" +
            $"\nPlacementRotationRadians={data?.PlacementRotationRadians?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "<n/a>"}");
        WriteSourceRotationRebuildDiagnostics(editor, sourceHandle);

        if (mleader is null ||
            mleader.ContentType != ContentType.BlockContent)
        {
            return;
        }

        if (!TryReadLeaderGeometry(
                mleader,
                out var attachment,
                out var knee,
                out var blockPosition))
        {
            editor.WriteMessage("\nLeaderGeometry=<unreadable>");
            return;
        }

        var frameCenter = blockPosition;
        editor.WriteMessage(
            $"\nAttachment=({Fmt(attachment.X)},{Fmt(attachment.Y)})" +
            $"\nLeaderVertex0=({Fmt(attachment.X)},{Fmt(attachment.Y)})" +
            $"\nLeaderVertex1=({Fmt(knee.X)},{Fmt(knee.Y)})" +
            $"\nFirstLeaderVertex=({Fmt(attachment.X)},{Fmt(attachment.Y)})" +
            $"\nKnee=({Fmt(knee.X)},{Fmt(knee.Y)})" +
            $"\nKneeWorld=({Fmt(knee.X)},{Fmt(knee.Y)})" +
            $"\nBlockPosition=({Fmt(blockPosition.X)},{Fmt(blockPosition.Y)})" +
            $"\nFrameCenter=({Fmt(frameCenter.X)},{Fmt(frameCenter.Y)})" +
            $"\nFrameCenterWorld=({Fmt(frameCenter.X)},{Fmt(frameCenter.Y)})");

        WriteAttributeSnapshot(editor, transaction, mleader);
        WriteDimensionsTowardKneeDiagnostics(
            editor,
            transaction,
            mleader,
            knee,
            frameCenter);

        if (!string.IsNullOrWhiteSpace(data?.SourceHandle) &&
            TryFindSourceEntity(
                transaction,
                entity.Database,
                data.SourceHandle,
                out var source) &&
            TryGetSourceEndpoints(source, out var start, out var end))
        {
            var sourceAngleDeg =
                Math.Atan2(end.Y - start.Y, end.X - start.X) * 180d / Math.PI;
            var rawAxis = Math.Atan2(end.Y - start.Y, end.X - start.X);
            editor.WriteMessage(
                $"\nSourceAngleDeg={Fmt(sourceAngleDeg)}" +
                $"\nRequestedSide={TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(rawAxis)}");

            if (TimberFramedCombinedG5CreatePlacementRules.TryMeasureSignedSide(
                    attachment.X,
                    attachment.Y,
                    blockPosition.X,
                    blockPosition.Y,
                    start.X,
                    start.Y,
                    end.X,
                    end.Y,
                    out var signedSide,
                    out var worldSide))
            {
                editor.WriteMessage(
                    $"\nActualWorldSide={worldSide}" +
                    $"\nSignedSide={Fmt(signedSide)}");
            }

            if (TimberFramedCombinedG5CreatePlacementRules
                    .TryMeasureFirstSegmentAngleDeg(
                        attachment.X,
                        attachment.Y,
                        knee.X,
                        knee.Y,
                        start.X,
                        start.Y,
                        end.X,
                        end.Y,
                        out var angleDeg))
            {
                editor.WriteMessage(
                    $"\nActualFirstSegmentAngleToSourceDeg={Fmt(angleDeg)}");
            }

            var decision =
                TimberFramedBlockContentReadableOrientationRules.Decide(rawAxis);
            var itemAttrRot = TryReadAttributeRotationRadians(
                transaction,
                mleader,
                TimberFramedBlockContentDefinitionRules.ItemNoTag);
            var widthAttrRot = TryReadAttributeRotationRadians(
                transaction,
                mleader,
                TimberFramedBlockContentDefinitionRules.WidthTag);
            var heightAttrRot = TryReadAttributeRotationRadians(
                transaction,
                mleader,
                TimberFramedBlockContentDefinitionRules.HeightTag);
            var hasMeasuredWorld =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryResolveWorldContentXAxis(
                        transaction,
                        mleader,
                        out var measuredPresentation,
                        out var measuredNote);
            var landingPhysical = Math.Atan2(
                blockPosition.Y - knee.Y,
                blockPosition.X - knee.X);
            var gripDecision = hasMeasuredWorld
                ? TimberFramedBlockContentGripPresentationRules
                    .ResolveFinalContentPresentation(
                        measuredPresentation,
                        mleader.BlockRotation,
                        landingPhysical,
                        (data.R3ReferencePresentationRevision ?? 0) >=
                            TimberFramedBlockContentReadableOrientationRules
                                .ReferencePresentationRevision)
                : null;
            var expectedPresentation = gripDecision?.FinalWorldPresentationAngle ??
                decision.PresentationAngle;
            var gripDeltaDeg =
                hasMeasuredWorld
                    ? TimberFramedBlockContentGripPresentationRules
                        .PresentationDeltaRadians(
                            expectedPresentation,
                            measuredPresentation) *
                      180d /
                      Math.PI
                    : double.NaN;
            var measuredWorldDeg = measuredPresentation * 180d / Math.PI;
            var hasRefreshTrace =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryGetRefreshPresentationTrace(
                        data.SourceHandle,
                        out var refreshTrace);
            var hasCreateTrace =
                AutoCadFramedBlockContentAnnotationService
                    .TryGetCreatePresentationTrace(
                        entity.Handle.ToString(),
                        out var createTrace);
            editor.WriteMessage(
                $"\nSourceAxisWorldAngleDeg={Fmt(sourceAngleDeg)}" +
                $"\nLandingWorldAngleDeg={Fmt(landingPhysical * 180d / Math.PI)}" +
                $"\nSourcePhysicalAxisAngleDeg={Fmt(decision.PhysicalAxisAngle * 180d / Math.PI)}" +
                $"\nVerticalRuleInputDeg=" +
                (hasCreateTrace && createTrace.VerticalRuleInput is double verticalInput
                    ? Fmt(verticalInput * 180d / Math.PI)
                    : "<not-called-in-this-session>") +
                $"\nVerticalRuleOutputDeg=" +
                (hasCreateTrace && createTrace.VerticalRuleOutput is double verticalOutput
                    ? Fmt(verticalOutput * 180d / Math.PI)
                    : "<not-called-in-this-session>") +
                $"\nTransformByAngleDeg=" +
                (hasCreateTrace && !double.IsNaN(createTrace.TransformByAngle)
                    ? Fmt(createTrace.TransformByAngle * 180d / Math.PI)
                    : "<not-applied-on-this-path>") +
                $"\nBlockRotationBeforeDeg=" +
                (hasCreateTrace
                    ? Fmt(createTrace.BlockRotationBefore * 180d / Math.PI)
                    : "<trace-unavailable>") +
                $"\nBlockRotationRequestedDeg=" +
                (hasCreateTrace
                    ? Fmt(createTrace.BlockRotationRequested * 180d / Math.PI)
                    : "<trace-unavailable>") +
                $"\nBlockRotationAfterDeg=" +
                Fmt(mleader.BlockRotation * 180d / Math.PI) +
                $"\nPhysicalAxisAngleDeg={Fmt(decision.PhysicalAxisAngle * 180d / Math.PI)}" +
                $"\nPresentationAngleDeg={Fmt(expectedPresentation * 180d / Math.PI)}" +
                $"\nReadableFlip={gripDecision?.ReadableFlip ?? decision.ReadableFlip}" +
                $"\nIncomingLandingSide={gripDecision?.IncomingLandingSide ?? decision.IncomingLandingSide}" +
                $"\nMLeaderBlockRotationDeg={Fmt(mleader.BlockRotation * 180d / Math.PI)}" +
                $"\nBlockRotationDeg={Fmt(mleader.BlockRotation * 180d / Math.PI)}" +
                $"\nBlockContentBaseTransformAngleDeg={Fmt((gripDecision?.ExistingContentBaseAngle ?? double.NaN) * 180d / Math.PI)}" +
                $"\nFrameWorldOrientationDeg={(hasMeasuredWorld ? Fmt(measuredWorldDeg) : "<n/a>")}" +
                $"\nItemTextWorldAngleDeg=" +
                (itemAttrRot is double itemWorld
                    ? Fmt(itemWorld * 180d / Math.PI)
                    : "<n/a>") +
                $"\nWidthTextWorldAngleDeg=" +
                (widthAttrRot is double widthWorld
                    ? Fmt(widthWorld * 180d / Math.PI)
                    : "<n/a>") +
                $"\nHeightTextWorldAngleDeg=" +
                (heightAttrRot is double heightWorld
                    ? Fmt(heightWorld * 180d / Math.PI)
                    : "<n/a>") +
                $"\nCreateVerticalRuleCalled={hasCreateTrace}" +
                $"\nCreateAppliedHalfTurn=" +
                (hasCreateTrace ? createTrace.AppliedHalfTurn.ToString() : "<n/a>") +
                $"\nCreateBlockRotationBeforeDeg=" +
                (hasCreateTrace
                    ? Fmt(createTrace.BlockRotationBefore * 180d / Math.PI)
                    : "<n/a>") +
                $"\nCreateBlockRotationAfterDeg=" +
                (hasCreateTrace
                    ? Fmt(createTrace.BlockRotationAfter * 180d / Math.PI)
                    : "<n/a>") +
                $"\nCreateFrameWorldOrientationBeforeDeg=" +
                (hasCreateTrace && createTrace.FrameWorldOrientationBefore is double frameBefore
                    ? Fmt(frameBefore * 180d / Math.PI)
                    : "<n/a>") +
                $"\nCreateFrameWorldOrientationAfterDeg=" +
                (hasCreateTrace && createTrace.FrameWorldOrientationAfter is double frameAfter
                    ? Fmt(frameAfter * 180d / Math.PI)
                    : "<n/a>") +
                $"\nCreatePresentationMeasurement=" +
                (hasCreateTrace ? createTrace.MeasurementNote : "<n/a>") +
                $"\nPresentationPath=" +
                (hasCreateTrace
                    ? createTrace.PresentationPath
                    : hasRefreshTrace
                        ? refreshTrace.SourceRotationChanged
                            ? "SourceRotation"
                            : "Refresh"
                        : "Grip/Unknown") +
                $"\nPresentationOperationSequence=" +
                (hasCreateTrace
                    ? createTrace.PresentationOperationSequence
                    : "<trace-unavailable>") +
                $"\nReferencePresentationRevisionBefore=" +
                (hasCreateTrace
                    ? createTrace.ReferenceRevisionBefore.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "<n/a>") +
                $"\nReferencePresentationRevisionAfter=" +
                (hasCreateTrace
                    ? createTrace.ReferenceRevisionAfter.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : data.R3ReferencePresentationRevision?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) ?? "<n/a>") +
                $"\nPresentationOrigin=" +
                (hasRefreshTrace
                    ? refreshTrace.SourceRotationChanged
                        ? "SourceRotation"
                        : "Refresh"
                    : "Create/Grip") +
                $"\nPresentationBeforeRefreshDeg=" +
                (hasRefreshTrace
                    ? Fmt(refreshTrace.PresentationBeforeRefresh * 180d / Math.PI)
                    : "<n/a>") +
                $"\nPresentationAfterRefreshDeg=" +
                (hasRefreshTrace
                    ? Fmt(refreshTrace.PresentationAfterRefresh * 180d / Math.PI)
                    : "<n/a>") +
                $"\nPresentationRefreshDeltaDeg=" +
                (hasRefreshTrace
                    ? Fmt(refreshTrace.PresentationRefreshDelta * 180d / Math.PI)
                    : "<n/a>") +
                $"\nItemAttrRefRotationDeg={Fmt((itemAttrRot ?? double.NaN) * 180d / Math.PI)}" +
                $"\nWidthAttrRefRotationDeg={Fmt((widthAttrRot ?? double.NaN) * 180d / Math.PI)}" +
                $"\nHeightAttrRefRotationDeg={Fmt((heightAttrRot ?? double.NaN) * 180d / Math.PI)}" +
                $"\nPresentationAngleBeforeGrip={Fmt(expectedPresentation * 180d / Math.PI)}" +
                $"\nPresentationAngleAfterGrip={(hasMeasuredWorld ? Fmt(measuredWorldDeg) : "<n/a>")}" +
                $"\nGripPresentationDeltaDeg={Fmt(gripDeltaDeg)}" +
                $"\nGripPresentationDecision=" +
                (gripDecision is null
                    ? "UNRESOLVED:" + measuredNote
                    : $"CURRENT_WORLD={Fmt(gripDecision.CurrentWorldPresentationAngle * 180d / Math.PI)}; " +
                      $"BASE={Fmt(gripDecision.ExistingContentBaseAngle * 180d / Math.PI)}; " +
                      $"DELTA={Fmt(gripDecision.BlockRotationCorrection * 180d / Math.PI)}; " +
                      $"TARGET_BR={Fmt(gripDecision.TargetBlockRotation * 180d / Math.PI)}; " +
                      $"FINAL_WORLD={Fmt(gripDecision.FinalWorldPresentationAngle * 180d / Math.PI)}"));
            WriteWholeAnnotationHalfTurnDiagnostics(
                editor,
                mleader,
                data,
                rawAxis,
                attachment);
        }
        else
        {
            editor.WriteMessage("\nSourceGeometry=<unresolved>");
            editor.WriteMessage(
                "\nWholeAnnotationHalfTurnRequired=<n/a>" +
                "\nWholeAnnotationHalfTurnApplied=<n/a>" +
                "\nWholeAnnotationHalfTurnStateBefore=<n/a>" +
                "\nWholeAnnotationHalfTurnStateAfter=<n/a>" +
                "\nWholeAnnotationTransformAppliedThisOperation=<n/a>" +
                "\nWholeAnnotationRotationDeg=<n/a>" +
                "\nAttachmentBefore=<n/a>" +
                "\nAttachmentAfter=<n/a>" +
                "\nAttachmentDelta=<n/a>" +
                "\nPresentationLifecyclePath=<n/a>");
        }
    }

    private static void WriteSourceRotationRebuildDiagnostics(
        Editor editor,
        string sourceHandle)
    {
        if (!ElementLabelService.TryGetSourceRotationRebuildTrace(
                sourceHandle,
                out var trace))
        {
            editor.WriteMessage(
                "\nSourceAxisBeforeDeg=<trace-unavailable>" +
                "\nSourceAxisAfterDeg=<trace-unavailable>" +
                "\nSourceAxisDeltaDeg=<trace-unavailable>" +
                "\nPhysicalSourceAxisBeforeDeg=<trace-unavailable>" +
                "\nPhysicalSourceAxisAfterDeg=<trace-unavailable>" +
                "\nPhysicalSourceAxisDeltaDeg=<trace-unavailable>" +
                "\nSourceAxisSemantics=PhysicalStartToEnd" +
                "\nSourceRotationDetected=<trace-unavailable>" +
                "\nAnnotationRebuildRequired=<trace-unavailable>" +
                "\nAnnotationRebuilt=<trace-unavailable>" +
                "\nAnnotationRecreated=<trace-unavailable>" +
                "\nSameAnnotationHandle=<trace-unavailable>" +
                "\nOldAnnotationHandle=<trace-unavailable>" +
                "\nNewAnnotationHandle=<trace-unavailable>" +
                "\nRebuildReason=<trace-unavailable>");
            return;
        }

        var sameHandle = string.Equals(
            trace.OldAnnotationHandle,
            trace.NewAnnotationHandle,
            StringComparison.OrdinalIgnoreCase);
        editor.WriteMessage(
            $"\nSourceAxisBeforeDeg={Fmt(trace.SourceAxisBeforeRadians * 180d / Math.PI)}" +
            $"\nSourceAxisAfterDeg={Fmt(trace.SourceAxisAfterRadians * 180d / Math.PI)}" +
            $"\nSourceAxisDeltaDeg={Fmt(trace.SourceAxisDeltaRadians * 180d / Math.PI)}" +
            $"\nPhysicalSourceAxisBeforeDeg={Fmt(trace.SourceAxisBeforeRadians * 180d / Math.PI)}" +
            $"\nPhysicalSourceAxisAfterDeg={Fmt(trace.SourceAxisAfterRadians * 180d / Math.PI)}" +
            $"\nPhysicalSourceAxisDeltaDeg={Fmt(trace.SourceAxisDeltaRadians * 180d / Math.PI)}" +
            $"\nSourceAxisSemantics=PhysicalStartToEnd" +
            $"\nSourceRotationDetected={trace.SourceRotationDetected}" +
            $"\nAnnotationRebuildRequired={trace.AnnotationRebuildRequired}" +
            $"\nAnnotationRebuilt={trace.AnnotationRebuilt}" +
            $"\nAnnotationRecreated={trace.AnnotationRebuilt}" +
            $"\nSameAnnotationHandle={sameHandle}" +
            $"\nOldAnnotationHandle={trace.OldAnnotationHandle}" +
            $"\nNewAnnotationHandle={trace.NewAnnotationHandle ?? "<none>"}" +
            $"\nRebuildReason={trace.RebuildReason}");
    }

    private static void WriteWholeAnnotationHalfTurnDiagnostics(
        Editor editor,
        MLeader leader,
        ElementLabelData data,
        double sourcePhysicalAxisRadians,
        Point3d currentAttachment)
    {
        var required =
            TimberFramedBlockContentWholeAnnotationHalfTurnRules
                .RequiresWholeAnnotationHalfTurn(sourcePhysicalAxisRadians);
        var applied =
            TimberFramedBlockContentWholeAnnotationHalfTurnRules
                .IsWholeAnnotationHalfTurnApplied(
                    data.R3ReferencePresentationRevision ?? 0);
        var hasTrace = AutoCadWholeMLeaderHalfTurnService.TryGetLatestTrace(
            leader.ObjectId.Handle.ToString(),
            out var trace);
        editor.WriteMessage(
            $"\nWholeAnnotationHalfTurnRequired={required}" +
            $"\nWholeAnnotationHalfTurnApplied={applied}" +
            "\nWholeAnnotationHalfTurnStateBefore=" +
            (hasTrace ? trace.AppliedBefore.ToString() : applied.ToString()) +
            "\nWholeAnnotationHalfTurnStateAfter=" +
            (hasTrace ? trace.AppliedAfter.ToString() : applied.ToString()) +
            "\nWholeAnnotationTransformAppliedThisOperation=" +
            (hasTrace
                ? trace.TransformAppliedThisOperation.ToString()
                : "<trace-unavailable>") +
            "\nWholeAnnotationRotationDeg=" +
            (hasTrace
                ? Fmt(trace.RotationRadians * 180d / Math.PI)
                : "<trace-unavailable>") +
            "\nAttachmentBefore=" +
            (hasTrace ? Fmt(trace.AttachmentBefore) : Fmt(currentAttachment)) +
            "\nAttachmentAfter=" +
            (hasTrace ? Fmt(trace.AttachmentAfter) : Fmt(currentAttachment)) +
            "\nAttachmentDelta=" +
            (hasTrace ? Fmt(trace.AttachmentDelta) : "<trace-unavailable>") +
            "\nPresentationLifecyclePath=" +
            (hasTrace ? trace.LifecyclePath : "<trace-unavailable>"));
    }

    private static double? TryReadAttributeRotationRadians(
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
                definition.IsErased)
            {
                continue;
            }

            if (!string.Equals(
                    definition.Tag,
                    tag,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            return attribute?.Rotation;
        }

        return null;
    }

    private static void WriteAttributeSnapshot(
        Editor editor,
        Transaction transaction,
        MLeader leader)
    {
        if (leader.BlockContentId.IsNull ||
            transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord block)
        {
            return;
        }

        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            var tag = definition.Tag?.ToUpperInvariant() ?? string.Empty;
            if (tag is not (
                TimberFramedBlockContentDefinitionRules.ItemNoTag or
                TimberFramedBlockContentDefinitionRules.WidthTag or
                TimberFramedBlockContentDefinitionRules.HeightTag))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            var text = attribute?.TextString ?? "<missing>";
            editor.WriteMessage($"\n{tag}={text}");
        }
    }

    private static void WriteDimensionsTowardKneeDiagnostics(
        Editor editor,
        Transaction transaction,
        MLeader leader,
        Point3d knee,
        Point3d frameCenter)
    {
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryReadWorldAttributePoints(
                    transaction,
                    leader,
                    out var points,
                    out _))
        {
            editor.WriteMessage("\nDimensionsAnchorWorld=<unreadable>");
            editor.WriteMessage("\nDimensionsTowardKneeDot=<n/a>");
            return;
        }

        var dims = new TimberPlanarPoint(
            (points.WidthAlignment.X + points.HeightAlignment.X) * 0.5d,
            (points.WidthAlignment.Y + points.HeightAlignment.Y) * 0.5d);
        editor.WriteMessage(
            $"\nDimensionsAnchorWorld=({Fmt(dims.X)},{Fmt(dims.Y)})");
        if (TimberFramedBlockContentDefinitionRules.TryEvaluateDimensionsTowardKneeDot(
                new TimberPlanarPoint(frameCenter.X, frameCenter.Y),
                new TimberPlanarPoint(knee.X, knee.Y),
                dims,
                out var towardKneeDot))
        {
            editor.WriteMessage(
                $"\nDimensionsTowardKneeDot={Fmt(towardKneeDot)}" +
                (towardKneeDot > 0d ? " (PASS)" : " (FAIL)"));
        }
        else
        {
            editor.WriteMessage("\nDimensionsTowardKneeDot=<n/a>");
        }
    }

    private static bool TryReadLeaderGeometry(
        MLeader leader,
        out Point3d attachment,
        out Point3d knee,
        out Point3d blockPosition)
    {
        attachment = Point3d.Origin;
        knee = Point3d.Origin;
        blockPosition = leader.BlockPosition;
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length == 0)
            {
                return false;
            }

            var lineIndexes = leader
                .GetLeaderLineIndexes(leaderIndexes[0])
                .Cast<int>()
                .ToArray();
            if (lineIndexes.Length == 0)
            {
                return false;
            }

            attachment = leader.GetFirstVertex(lineIndexes[0]);
            knee = leader.GetLastVertex(lineIndexes[0]);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static bool TryFindSourceEntity(
        Transaction transaction,
        Database database,
        string sourceHandle,
        out Entity source)
    {
        source = null!;
        if (!TryParseHandleObjectId(database, sourceHandle, out var id) ||
            id.IsNull)
        {
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                id,
                OpenMode.ForRead,
                out var entity,
                database) ||
            entity is null)
        {
            return false;
        }

        source = entity;
        return true;
    }

    private static bool TryParseHandleObjectId(
        Database database,
        string handleText,
        out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        if (string.IsNullOrWhiteSpace(handleText))
        {
            return false;
        }

        try
        {
            var hex = handleText.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex[2..];
            }

            if (!long.TryParse(
                    hex,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            objectId = database.GetObjectId(false, new Handle(value), 0);
            return !objectId.IsNull;
        }
        catch (System.Exception)
        {
            objectId = ObjectId.Null;
            return false;
        }
    }

    private static bool TryGetSourceEndpoints(
        Entity source,
        out Point3d start,
        out Point3d end)
    {
        switch (source)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                return true;
            case Polyline polyline:
                start = polyline.StartPoint;
                end = polyline.EndPoint;
                return true;
            default:
                start = Point3d.Origin;
                end = Point3d.Origin;
                return false;
        }
    }

    private static string Fmt(double value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fmt(Point3d point) =>
        $"({Fmt(point.X)},{Fmt(point.Y)},{Fmt(point.Z)})";

    private static int CountOwnedForRole(
        Transaction transaction,
        Database database,
        string sourceHandle,
        TimberMainAnnotationComponentRole role)
    {
        var count = 0;
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            if (string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) &&
                data.ComponentRole == role)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountLegacyOwned(
        Transaction transaction,
        Database database,
        string sourceHandle)
    {
        var count = 0;
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AutoCadFramedG4CompositePolicy.IsG4CompositeRole(data.ComponentRole) ||
                data.ComponentRole == TimberMainAnnotationComponentRole.Primary)
            {
                count++;
            }
        }

        return count;
    }
}
#endif
