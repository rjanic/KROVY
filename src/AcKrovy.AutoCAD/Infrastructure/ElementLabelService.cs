using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Vytvára a obnovuje hlavné MText/MLeader anotácie krovu. Anotácia je
/// samostatný objekt na hladine KROV_POPIS, ale obsahuje XData väzbu na prvok.
/// Pri WBLOCK je preto potrebné vybrať aj popisy; keď sa prenesie iba krov,
/// príkaz AK_LABELS popisy v cieľovom DWG bezpečne dopočíta znovu.
/// </summary>
internal static partial class ElementLabelService
{
    public const string LabelLayerName = "KROV_POPIS";

    private const short LabelLayerColorIndex = 8;
    private const double DefaultTextHeightMm = TimberMainAnnotationTextRules.TextHeightMm;
    private const double PlacementToleranceMm = 0.001d;

    public static bool UpsertForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        TimberAnnotationScaleContext annotationScaleContext,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm,
        bool copySourcePreservation = false,
        AutoCadAnnotationPresentationContext? presentationContext = null,
        AutoCadItemLeaderBlockVariantBatchCatalog? variantBatchCatalog = null,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(annotationScaleContext);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity) || string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(data, AutoCadEntityHelpers.GetPlanLengthMm(sourceEntity), roundingStepMm);
        var normalizedMode =
            TimberAnnotationModeRules.Normalize(data.AnnotationMode);
        var labelText = TimberMainAnnotationFormatter.Format(data, measurement);
        if (normalizedMode ==
            TimberAnnotationMode.DimensionsWithItemNumber)
        {
            labelText = TimberElementLabelFormatter.FormatStackedDimensions(data);
            var framedPlacement = CalculateShortLeaderPlacement(
                sourceEntity,
                data.ElementId,
                data.ItemNumberLeaderStyle,
                annotationScaleContext.ScaleFactor);
            return UpsertCombinedLeader(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                framedPlacement,
                labelText,
                copySourcePreservation,
                annotationScaleContext: annotationScaleContext,
                presentationContext: presentationContext,
                variantBatchCatalog: variantBatchCatalog,
                variantResultObserver: variantResultObserver);
        }

        if (TimberAnnotationModeRules.GetRepresentation(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle) !=
            TimberMainAnnotationRepresentation.FullLabel)
        {
            var leaderPlacement = CalculateShortLeaderPlacement(
                sourceEntity,
                labelText,
                data.AnnotationMode == TimberAnnotationMode.ItemNumberLeader
                    ? data.ItemNumberLeaderStyle
                    : null,
                annotationScaleContext.ScaleFactor,
                preferredSideOverride: TimberLeaderHorizontalSide.Right,
                standaloneNativeOrientation: true);
            return UpsertLeader(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                leaderPlacement,
                labelText,
                copySourcePreservation,
                annotationScaleContext: annotationScaleContext,
                baseTextHeightMm:
                    normalizedMode ==
                        TimberAnnotationMode.DimensionsLeader
                        ? TimberDimensionTypographyRules
                            .BaseDimensionTextHeightAtScale50Mm
                        : normalizedMode ==
                                TimberAnnotationMode.ItemNumberLeader &&
                            ItemNumberLeaderStyleRules.Normalize(
                                data.ItemNumberLeaderStyle) ==
                                ItemNumberLeaderStyle.Plain
                            ? TimberItemNumberTypographyRules
                                .BaseItemNumberTextHeightAtScale50Mm
                            : DefaultTextHeightMm,
                presentationContext: presentationContext,
                variantBatchCatalog: variantBatchCatalog,
                variantResultObserver: variantResultObserver);
        }

        if (!AutoCadFullLabelPresentationPolicy.TryPrepare(
                database,
                presentationContext,
                out var fullLabelPresentation,
                out var fullLabelDiagnostic) ||
            fullLabelPresentation is null)
        {
            AcKrovyDiagnostics.Warning(
                "FullLabelPresentation",
                fullLabelDiagnostic);
            return false;
        }

        var textHeightMm = fullLabelPresentation.ModelHeightMm;
        var placement = CalculatePlacement(sourceEntity, textHeightMm);
        return UpsertLabel(
            database,
            transaction,
            sourceEntity,
            data,
            previousElementId,
            placement,
            labelText,
            AttachmentPoint.MiddleCenter,
            textHeightMm,
            lineSpacingFactor: null,
            copySourcePreservation,
            resolvedTextStyleId: fullLabelPresentation.TextStyleId);
    }

    public static bool UpsertForPostFootprint(
        Database database,
        Transaction transaction,
        Polyline sourcePolyline,
        TimberElementData data,
        TimberRectangularFootprintGeometry geometry,
        TimberAnnotationScaleContext annotationScaleContext,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm,
        bool copySourcePreservation = false,
        AutoCadAnnotationPresentationContext? presentationContext = null,
        AutoCadItemLeaderBlockVariantBatchCatalog? variantBatchCatalog = null,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourcePolyline);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(annotationScaleContext);

        if (string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(
            data,
            planLengthMm: null,
            roundingIncrementMm: roundingStepMm);
        var normalizedMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode);
        var presentationScaleFactor =
            annotationScaleContext.ScaleFactor;
        var labelText = normalizedMode == TimberAnnotationMode.FullLabel
            ? TimberPostFootprintLabelFormatter.Format(data, measurement.ActualLengthMm)
            : TimberMainAnnotationFormatter.Format(data, measurement);
        if (normalizedMode == TimberAnnotationMode.DimensionsWithItemNumber)
        {
            labelText = TimberElementLabelFormatter.FormatStackedDimensions(data);
            var leaderPlacement = TimberLeaderPlacementCalculator.CalculatePost(geometry.Bounds);
            var framedPlacement = CalculateShortLeaderPlacement(
                leaderPlacement,
                sourcePolyline.Elevation,
                data.ElementId,
                data.ItemNumberLeaderStyle,
                presentationScaleFactor: presentationScaleFactor);
            return UpsertCombinedLeader(
                database,
                transaction,
                sourcePolyline,
                data,
                previousElementId,
                framedPlacement,
                labelText,
                copySourcePreservation,
                annotationScaleContext: annotationScaleContext,
                presentationContext: presentationContext,
                variantBatchCatalog: variantBatchCatalog,
                variantResultObserver: variantResultObserver);
        }

        if (TimberAnnotationModeRules.GetRepresentation(
                normalizedMode,
                data.ItemNumberLeaderStyle) !=
            TimberMainAnnotationRepresentation.FullLabel)
        {
            var leaderPlacement = TimberLeaderPlacementCalculator.CalculatePost(geometry.Bounds);
            var itemPlacement = CalculateShortLeaderPlacement(
                leaderPlacement,
                sourcePolyline.Elevation,
                labelText,
                normalizedMode == TimberAnnotationMode.ItemNumberLeader
                    ? data.ItemNumberLeaderStyle
                    : null,
                preferredSide: TimberLeaderHorizontalSide.Right,
                presentationScaleFactor: presentationScaleFactor,
                usePlainItemNumberPlacement:
                    normalizedMode == TimberAnnotationMode.ItemNumberLeader &&
                    ItemNumberLeaderStyleRules.Normalize(
                        data.ItemNumberLeaderStyle) ==
                    ItemNumberLeaderStyle.Plain,
                standaloneNativeOrientation: true);
            return UpsertLeader(
                database,
                transaction,
                sourcePolyline,
                data,
                previousElementId,
                itemPlacement,
                labelText,
                copySourcePreservation,
                annotationScaleContext,
                baseTextHeightMm:
                    normalizedMode == TimberAnnotationMode.DimensionsLeader
                        ? TimberDimensionTypographyRules
                            .BaseDimensionTextHeightAtScale50Mm
                        : normalizedMode == TimberAnnotationMode.ItemNumberLeader &&
                          ItemNumberLeaderStyleRules.Normalize(
                              data.ItemNumberLeaderStyle) ==
                          ItemNumberLeaderStyle.Plain
                            ? TimberItemNumberTypographyRules
                                .BaseItemNumberTextHeightAtScale50Mm
                            : DefaultTextHeightMm,
                presentationContext: presentationContext,
                variantBatchCatalog: variantBatchCatalog,
                variantResultObserver: variantResultObserver);
        }

        if (!AutoCadFullLabelPresentationPolicy.TryPrepare(
                database,
                presentationContext,
                out var fullLabelPresentation,
                out var fullLabelDiagnostic) ||
            fullLabelPresentation is null)
        {
            AcKrovyDiagnostics.Warning(
                "FullLabelPresentation",
                fullLabelDiagnostic);
            return false;
        }

        var footprintPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(
            geometry.Bounds,
            TimberPostFootprintLabelPlacementCalculator.VerticalGapMm *
            presentationScaleFactor);
        var elevation = sourcePolyline.GetPoint3dAt(0).Z;
        var placement = new LabelPlacement(
            new Point3d(footprintPlacement.AnchorX, footprintPlacement.AnchorY, elevation),
            footprintPlacement.RotationRadians);
        var fullLabelTextHeightMm = fullLabelPresentation.ModelHeightMm;

        return UpsertLabel(
            database,
            transaction,
            sourcePolyline,
            data,
            previousElementId,
            placement,
            labelText,
            AttachmentPoint.BottomCenter,
            fullLabelTextHeightMm,
            TimberPostFootprintLabelPlacementCalculator.LineSpacingFactor,
            copySourcePreservation,
            resolvedTextStyleId: fullLabelPresentation.TextStyleId);
    }

    private static bool UpsertLabel(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LabelPlacement placement,
        string labelText,
        AttachmentPoint attachment,
        double textHeightMm,
        double? lineSpacingFactor,
        bool copySourcePreservation,
        TimberAnnotationMode annotationMode =
            TimberAnnotationMode.FullLabel,
        TimberMainAnnotationComponentRole componentRole =
            TimberMainAnnotationComponentRole.Primary,
        bool preserveCompositeSiblings = false,
        double? envelopeWidthMm = null,
        double? envelopeHeightMm = null,
        ObjectId? resolvedTextStyleId = null)
    {
        var sourceHandle = sourceEntity.Handle.ToString();
        var existingLabelId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            TimberMainAnnotationRepresentation.FullLabel,
            componentRole,
            preserveCompositeSiblings,
            allowElementIdFallback: !copySourcePreservation,
            out var obsoleteLabelIds);
        var isCreated = existingLabelId.IsNull;

        MText label;
        if (isCreated)
        {
            label = new MText();
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(label);
            transaction.AddNewlyCreatedDBObject(label, true);
        }
        else
        {
            label = (MText)transaction.GetObject(existingLabelId, OpenMode.ForWrite);
        }

        ApplyLabelAppearance(
            database,
            transaction,
            label,
            placement,
            labelText,
            attachment,
            textHeightMm,
            lineSpacingFactor,
            updateExistingLayer: !copySourcePreservation,
            resolvedTextStyleId: resolvedTextStyleId);
        ElementLabelStore.Write(label, transaction, new ElementLabelData
        {
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = TimberAnnotationModeRules.Normalize(annotationMode),
            ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(
                data.ItemNumberLeaderStyle),
            Contents = labelText,
            TextX = placement.Location.X,
            TextY = placement.Location.Y,
            RotationRadians = placement.RotationRadians,
            ComponentRole = componentRole,
            EnvelopeWidthMm = envelopeWidthMm,
            EnvelopeHeightMm = envelopeHeightMm,
        });
        DeleteObsoleteLabels(transaction, obsoleteLabelIds, label.ObjectId);
        if (!copySourcePreservation)
        {
            DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        }

        return isCreated;
    }

    private static bool UpsertLeader(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LeaderPlacement placement,
        string contents,
        bool copySourcePreservation,
        TimberAnnotationScaleContext annotationScaleContext,
        TimberMainAnnotationComponentRole componentRole =
            TimberMainAnnotationComponentRole.Primary,
        TimberMainAnnotationRepresentation? representationOverride = null,
        bool preserveCompositeSiblings = false,
        Action<LeaderPlacement>? effectivePlacementObserver = null,
        bool scaleNativePresentation = true,
        double baseTextHeightMm = DefaultTextHeightMm,
        AutoCadAnnotationPresentationContext? presentationContext = null,
        AutoCadItemLeaderBlockVariantBatchCatalog? variantBatchCatalog = null,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver = null,
        AutoCadPlainItemLeaderPresentationPreparation? preparedPlainItemPresentation = null,
        double? combinedLandingDistanceMm = null)
    {
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        var sourceHandle = sourceEntity.Handle.ToString();
        var desiredRepresentation = representationOverride ??
            TimberAnnotationModeRules.GetRepresentation(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle);

        var existingId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            desiredRepresentation,
            componentRole,
            preserveCompositeSiblings,
            allowElementIdFallback: !copySourcePreservation,
            out var obsoleteIds);
        var existingData = existingId.IsNull
            ? null
            : ReadLabels(database, transaction)
                .FirstOrDefault(annotation => annotation.Id == existingId)
                ?.Data;
        var automaticPlacement = placement;
        var manualOffset = TimberFramedLeaderManualOffset.Zero;
        if (existingData is not null &&
            desiredRepresentation == TimberMainAnnotationRepresentation.BlockLeader &&
            transaction.GetObject(existingId, OpenMode.ForRead, false) is MLeader framedLeader)
        {
            manualOffset = TimberFramedLeaderManualOffsetCalculator.Capture(
                new TimberFramedLeaderManualOffset(
                    existingData.LocalManualOffsetAlongAxisMm ?? 0d,
                    existingData.LocalManualOffsetNormalAxisMm ?? 0d),
                existingData.TextX ?? framedLeader.BlockPosition.X,
                existingData.TextY ?? framedLeader.BlockPosition.Y,
                framedLeader.BlockPosition.X,
                framedLeader.BlockPosition.Y,
                existingData.PlacementRotationRadians ??
                    existingData.RotationRadians ??
                    placement.RotationRadians);
            var manualPosition = TimberFramedLeaderManualOffsetCalculator.Apply(
                manualOffset,
                placement.TextLocation.X,
                placement.TextLocation.Y,
                placement.RotationRadians);
            var manualDelta = new Vector3d(
                manualPosition.X - placement.TextLocation.X,
                manualPosition.Y - placement.TextLocation.Y,
                0d);
            placement = placement with
            {
                TextLocation = new Point3d(
                    manualPosition.X,
                    manualPosition.Y,
                    placement.TextLocation.Z),
                // BlockContent is evaluated from the terminal leader vertex.
                // Move that vertex by the same
                // manual delta; changing BlockPosition alone is discarded by
                // AutoCAD during MLeader evaluation.
                Knee = placement.Knee + manualDelta,
            };
        }
        if (existingData is not null &&
            TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
                TimberAnnotationMode.DimensionsWithItemNumber &&
            componentRole == TimberMainAnnotationComponentRole.FramedItem &&
            TryCreateDoglegDirection(
                existingData.CombinedDoglegDirectionX,
                existingData.CombinedDoglegDirectionY,
                out var persistedDoglegDirection))
        {
            placement = placement with
            {
                Knee = placement.TextLocation -
                    persistedDoglegDirection * (
                        placement.EnvelopeWidthMm / 2d +
                        TimberItemLeaderLayoutCalculator
                            .CombinedFramedLandingDistanceMm *
                        annotationScaleContext.ScaleFactor),
                DoglegDirection = persistedDoglegDirection,
            };
        }
        effectivePlacementObserver?.Invoke(placement);
        var geometryMatches = existingData is not null &&
            LeaderGeometryMatches(existingData, data.AnnotationMode, placement);

        // Standalone framed ItemOnly → one native BlockContent MLeader.
        // Replaces the former G4 multi-entity composite (leader+frame+DBText).
        if (desiredRepresentation ==
                TimberMainAnnotationRepresentation.BlockLeader &&
            AutoCadStandaloneFramedItemOnlyProductionPolicy.UsesStandaloneFramedItemOnly(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle,
                componentRole))
        {
            return UpsertStandaloneFramedItemOnlyLeader(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                placement,
                automaticPlacement,
                manualOffset,
                contents,
                copySourcePreservation,
                annotationScaleContext,
                presentationContext,
                existingId,
                obsoleteIds,
                variantResultObserver);
        }

        // Legacy G4 composite path retained only for non-standalone roles; CREATE
        // for ItemNumberLeader framed never enters here after the gate above.
        if (desiredRepresentation ==
                TimberMainAnnotationRepresentation.BlockLeader &&
            AutoCadFramedG4CompositePolicy.UsesG4Composite(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle,
                componentRole))
        {
            if (presentationContext is null)
            {
                variantResultObserver?.Invoke(
                    AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                        null,
                        null,
                        AutoCadDatabaseIdentity.TryGetIdentity(database),
                        "G4 framed renderer requires a presentation context."));
#if DEBUG
                AutoCadFramedG4HostDiagnostics.Fail(
                    "F.01",
                    "G4 framed renderer requires a presentation context.",
                    sourceId: sourceEntity.ObjectId,
                    sourceHandle: sourceHandle);
#endif
                return false;
            }

            var existingGroupId = existingData?.AnnotationGroupId ??
                ReadExistingG4AnnotationGroupId(
                    database,
                    transaction,
                    sourceHandle);
            var g4Preparation = AutoCadFramedG4CompositeService.TryPrepare(
                database,
                transaction,
                data,
                contents,
                annotationScaleContext,
                presentationContext,
                new AutoCadItemLeaderFrameOnlyBlockBatchCatalog(database),
                combinedFramed:
                    TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
                        TimberAnnotationMode.DimensionsWithItemNumber &&
                    componentRole ==
                        TimberMainAnnotationComponentRole.FramedItem,
                existingGroupId,
                out var frameResult);
            if (g4Preparation is null)
            {
                variantResultObserver?.Invoke(
                    AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                        null,
                        frameResult.CanonicalBlockName,
                        frameResult.DatabaseIdentity,
                        frameResult.Diagnostic));
#if DEBUG
                AutoCadFramedG4HostDiagnostics.Fail(
                    "F.04",
                    frameResult.Diagnostic ?? "TryPrepare returned null",
                    sourceId: sourceEntity.ObjectId,
                    sourceHandle: sourceHandle,
                    frameBlockName: frameResult.ResolvedBlockName ??
                        frameResult.CanonicalBlockName,
                    frameBlockId: frameResult.BlockTableRecordId);
#endif
                return false;
            }

            var created = AutoCadFramedG4CompositeService.TryUpsert(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                placement,
                automaticPlacement,
                manualOffset,
                contents,
                copySourcePreservation,
                g4Preparation,
                out _);
            // FindExistingLabelId still keys on FramedItem/Primary BlockLeader.
            // G4 composites use Circle* roles, so a legacy framed MLeader can be
            // selected as existingId and would otherwise survive beside G4.
            if (!existingId.IsNull)
            {
                EraseLegacyFramedLeaderBesideG4(transaction, existingId);
            }

            DeleteObsoleteLabels(transaction, obsoleteIds, ObjectId.Null);
            if (!copySourcePreservation)
            {
                DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
                DeleteOwnedAnnotationOwnershipViolations(
                    database,
                    transaction,
                    sourceHandle,
                    data.ElementId);
            }

            return created;
        }

        AutoCadFramedItemLeaderPreparation? framedPreparation = null;

        AutoCadPlainItemLeaderPresentationPreparation? plainItemPreparation = null;
        AutoCadDimensionsLeaderPresentationPreparation? dimensionsLeaderPreparation =
            null;
        var normalizedAnnotationMode =
            TimberAnnotationModeRules.Normalize(data.AnnotationMode);
        var usesPlainItemPresentation =
            desiredRepresentation == TimberMainAnnotationRepresentation.Leader &&
            ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) ==
                ItemNumberLeaderStyle.Plain &&
            ((normalizedAnnotationMode == TimberAnnotationMode.ItemNumberLeader &&
              componentRole == TimberMainAnnotationComponentRole.Primary) ||
             (normalizedAnnotationMode ==
                  TimberAnnotationMode.DimensionsWithItemNumber &&
              componentRole == TimberMainAnnotationComponentRole.FramedItem));
        if (usesPlainItemPresentation)
        {
            if (preparedPlainItemPresentation is not null)
            {
                plainItemPreparation = preparedPlainItemPresentation;
            }
            else if (!AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(
                         database,
                         presentationContext,
                         out plainItemPreparation,
                         out var plainItemDiagnostic) ||
                     plainItemPreparation is null)
            {
                AcKrovyDiagnostics.Warning(
                    "PlainItemLeaderPresentation",
                    plainItemDiagnostic);
                return false;
            }
        }

        var usesDimensionsLeaderPresentation =
            desiredRepresentation == TimberMainAnnotationRepresentation.Leader &&
            normalizedAnnotationMode == TimberAnnotationMode.DimensionsLeader &&
            componentRole == TimberMainAnnotationComponentRole.Primary;
        if (usesDimensionsLeaderPresentation)
        {
            if (!AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(
                    database,
                    presentationContext,
                    out dimensionsLeaderPreparation,
                    out var dimensionsLeaderDiagnostic) ||
                dimensionsLeaderPreparation is null)
            {
                AcKrovyDiagnostics.Warning(
                    "DimensionsLeaderPresentation",
                    dimensionsLeaderDiagnostic);
                return false;
            }
        }

        MLeader leader;
        var isCreated = existingId.IsNull;
        var metadataWritten = false;
        // Standalone Plain / Dimensions: existing valid owner → keep entity and
        // refresh in place even when live placement drifted from canonical
        // (annotation grip). Combined Plain keeps geometryMatches gate unchanged.
        // Source timber MOVE/STRETCH/ROTATE rebuilds CREATE canonical geometry;
        // unchanged Automatic*/axis is content-only (preserves grip).
        var preserveStandaloneNativePlacement =
            combinedLandingDistanceMm is null &&
            existingData is not null &&
            !TimberAnnotationModeRules.RequiresLeaderRecreation(
                existingData.AnnotationMode,
                existingData.ItemNumberLeaderStyle,
                data.AnnotationMode,
                placement.ItemStyle ?? ItemNumberLeaderStyle.Plain);
        var standaloneSourceSync =
            preserveStandaloneNativePlacement && existingData is not null
                ? TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
                    existingData.AutomaticTextX,
                    existingData.AutomaticTextY,
                    existingData.RotationRadians,
                    automaticPlacement.TextLocation.X,
                    automaticPlacement.TextLocation.Y,
                    placement.RotationRadians)
                : new TimberStandaloneNativeLeaderSourceSyncDecision(
                    SourceGeometryChanged: false,
                    RequiresCanonicalRebuild: false,
                    RequiresOrientationSync: false,
                    OrientationDeltaRadians: 0d);
        if (!isCreated &&
            geometryMatches &&
            desiredRepresentation ==
                TimberMainAnnotationRepresentation.BlockLeader &&
            framedPreparation is not null &&
            transaction.GetObject(existingId, OpenMode.ForWrite, false) is
                MLeader existingBlockLeader &&
            TryUpdateBlockLeader(
                database,
                transaction,
                existingBlockLeader,
                placement,
                contents,
                updateExistingDefinitions: !copySourcePreservation,
                combinedFramed:
                    TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
                        TimberAnnotationMode.DimensionsWithItemNumber &&
                    componentRole ==
                        TimberMainAnnotationComponentRole.FramedItem,
                framedPreparation))
        {
            leader = existingBlockLeader;
        }
        else if (!isCreated &&
            (geometryMatches || preserveStandaloneNativePlacement) &&
            desiredRepresentation !=
                TimberMainAnnotationRepresentation.BlockLeader &&
            transaction.GetObject(existingId, OpenMode.ForWrite, false) is MLeader existingLeader &&
            TryUpdateNativeLeader(
                database,
                transaction,
                existingLeader,
                placement,
                contents,
                updateExistingDefinitions: !copySourcePreservation,
                annotationScaleContext,
                scaleNativePresentation,
                baseTextHeightMm,
                plainItemPreparation,
                combinedLandingDistanceMm,
                dimensionsLeaderPreparation,
                standaloneSourceSync))
        {
            leader = existingLeader;
            if (preserveStandaloneNativePlacement)
            {
                placement = CaptureLiveNativeLeaderPlacement(existingLeader, placement);
            }
        }
        else
        {
            Entity? replacedAnnotation = null;
            if (!existingId.IsNull &&
                transaction.GetObject(existingId, OpenMode.ForWrite, false) is Entity existing)
            {
                replacedAnnotation = existing;
            }

            leader = desiredRepresentation == TimberMainAnnotationRepresentation.BlockLeader
                ? CreateBlockMLeader(
                    database,
                    transaction,
                    placement,
                    contents,
                    updateExistingDefinitions: !copySourcePreservation,
                    combinedFramed:
                        TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
                            TimberAnnotationMode.DimensionsWithItemNumber &&
                        componentRole == TimberMainAnnotationComponentRole.FramedItem,
                    framedPreparation ?? throw new InvalidOperationException(
                        "A framed block leader requires a prepared immutable variant."))
                : CreateNativeMLeader(
                    database,
                    transaction,
                    placement,
                    contents,
                    updateExistingDefinitions: !copySourcePreservation,
                    annotationScaleContext,
                    scaleNativePresentation,
                    baseTextHeightMm,
                    plainItemPreparation,
                    combinedLandingDistanceMm,
                    dimensionsLeaderPreparation);
            WriteLeaderMetadata(
                leader,
                transaction,
                data,
                sourceHandle,
                placement,
                automaticPlacement,
                manualOffset,
                contents,
                componentRole);
            metadataWritten = true;

            if (replacedAnnotation is not null)
            {
                EraseMainAnnotation(transaction, replacedAnnotation);
            }
        }

        if (!metadataWritten)
        {
            WriteLeaderMetadata(
                leader,
                transaction,
                data,
                sourceHandle,
                placement,
                automaticPlacement,
                manualOffset,
                contents,
                componentRole);
        }

        DeleteObsoleteLabels(transaction, obsoleteIds, leader.ObjectId);
        if (!copySourcePreservation)
        {
            DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        }
        return isCreated;
    }

    private static bool UpsertCombinedLeader(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LeaderPlacement framedPlacement,
        string dimensionsContents,
        bool copySourcePreservation,
        TimberAnnotationScaleContext annotationScaleContext,
        AutoCadAnnotationPresentationContext? presentationContext,
        AutoCadItemLeaderBlockVariantBatchCatalog? variantBatchCatalog,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver)
    {
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        var presentationScaleFactor =
            annotationScaleContext.ScaleFactor;

        // Framed Combined → one G5 BlockContent MLeader (not G4 composite).
        if (AutoCadFramedBlockContentProductionPolicy.UsesG5CombinedFramed(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle))
        {
            if (presentationContext is null)
            {
                AcKrovyDiagnostics.Warning(
                    "G5CombinedFramed",
                    "G5 Combined framed requires a presentation context.");
                return false;
            }

            // G5 Combined create uses local-basis DefaultCreateSide (Right),
            // never World-X Start→End from CalculateLeaderPlacement.
            // Refresh of an existing MLeader preserves live geometry in place.
            var g5CreatePlacement = framedPlacement with
            {
                Side = TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide,
            };
            var combinedFramedPlacement = ApplyCombinedLandingDistance(
                g5CreatePlacement,
                presentationScaleFactor);
            var g5Ok = UpsertG5CombinedFramedLeader(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                combinedFramedPlacement,
                copySourcePreservation,
                annotationScaleContext,
                presentationContext);
            DeleteUnexpectedCompositeComponents(
                database,
                transaction,
                sourceEntity.Handle.ToString());
            if (!copySourcePreservation)
            {
                DeleteOwnedAnnotationOwnershipViolations(
                    database,
                    transaction,
                    sourceEntity.Handle.ToString(),
                    data.ElementId,
                    deleteStaleElementIds: true);
            }

            return g5Ok;
        }

        if (!AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(
                database,
                presentationContext,
                out var dimensionsPresentation,
                out var dimensionsDiagnostic) ||
            dimensionsPresentation is null)
        {
            AcKrovyDiagnostics.Warning(
                "CombinedDimensionsPresentation",
                dimensionsDiagnostic);
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.01",
                $"CombinedDimensionsPresentation failed: {dimensionsDiagnostic}",
                sourceId: sourceEntity.ObjectId,
                sourceHandle: sourceEntity.Handle.ToString());
#endif
            return false;
        }

        var dimensionTextHeightMm = dimensionsPresentation.ModelHeightMm;
        var dimensionEnvelopeHeightMm =
            TimberCombinedDimensionTypographyRules.CalculateEnvelopeHeightMm(
                presentationScaleFactor);
        var dimensionEnvelopeWidthMm =
            TimberCombinedDimensionTypographyRules.CalculateEnvelopeWidthMm(
                dimensionsContents,
                presentationScaleFactor);
        var combinedPlainPlacement = ApplyCombinedLandingDistance(
            framedPlacement,
            presentationScaleFactor);
        var effectiveFramedPlacement = combinedPlainPlacement;
        var isCombinedPlainItem =
            ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) ==
                ItemNumberLeaderStyle.Plain;
        var combinedLandingDistanceMm = isCombinedPlainItem
            ? TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
              presentationScaleFactor
            : (double?)null;
        AutoCadPlainItemLeaderPresentationPreparation? plainItemPreparation = null;
        if (isCombinedPlainItem)
        {
            if (!AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(
                    database,
                    presentationContext,
                    out plainItemPreparation,
                    out var plainItemDiagnostic) ||
                plainItemPreparation is null)
            {
                AcKrovyDiagnostics.Warning(
                    "PlainItemLeaderPresentation",
                    plainItemDiagnostic);
                return false;
            }
        }

        AutoCadItemLeaderBlockVariantResult? framedVariantResult = null;
        var framedCreated = UpsertLeader(
            database,
            transaction,
            sourceEntity,
            data,
            previousElementId,
            combinedPlainPlacement,
            data.ElementId,
            copySourcePreservation,
            annotationScaleContext: annotationScaleContext,
            componentRole: TimberMainAnnotationComponentRole.FramedItem,
            representationOverride: isCombinedPlainItem
                ? TimberMainAnnotationRepresentation.Leader
                : TimberMainAnnotationRepresentation.BlockLeader,
            preserveCompositeSiblings: true,
            effectivePlacementObserver: placement =>
                effectiveFramedPlacement = placement,
            baseTextHeightMm:
                TimberItemNumberTypographyRules
                    .BaseItemNumberTextHeightAtScale50Mm,
            presentationContext: presentationContext,
            variantBatchCatalog: variantBatchCatalog,
            variantResultObserver: result =>
            {
                framedVariantResult = result;
                variantResultObserver?.Invoke(result);
            },
            preparedPlainItemPresentation: plainItemPreparation,
            combinedLandingDistanceMm: combinedLandingDistanceMm);
        if (framedVariantResult is { Succeeded: false })
        {
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.03",
                $"framedVariantResult.Succeeded=false; " +
                $"Reason={framedVariantResult.DiagnosticReason}",
                sourceId: sourceEntity.ObjectId,
                sourceHandle: sourceEntity.Handle.ToString());
#endif
            return false;
        }

        var framedOk = framedCreated;
        var dimensionsPlacement = CalculateCombinedDimensionsTextPlacement(
            database,
            transaction,
            sourceEntity.Handle.ToString(),
            effectiveFramedPlacement,
            dimensionEnvelopeWidthMm,
            dimensionTextHeightMm,
            presentationScaleFactor);
        var primaryCreated = UpsertLabel(
            database,
            transaction,
            sourceEntity,
            data,
            previousElementId,
            dimensionsPlacement,
            dimensionsContents,
            AttachmentPoint.MiddleCenter,
            dimensionTextHeightMm,
            lineSpacingFactor: null,
            copySourcePreservation: copySourcePreservation,
            annotationMode: TimberAnnotationMode.DimensionsWithItemNumber,
            componentRole: TimberMainAnnotationComponentRole.Primary,
            preserveCompositeSiblings: true,
            envelopeWidthMm: dimensionEnvelopeWidthMm,
            envelopeHeightMm: dimensionEnvelopeHeightMm,
            resolvedTextStyleId: dimensionsPresentation.TextStyleId);
        DeleteUnexpectedCompositeComponents(
            database,
            transaction,
            sourceEntity.Handle.ToString());
        if (!copySourcePreservation)
        {
            // After framed + Primary upsert: drop previous-type ElementId
            // leftovers (K→KL / K→VT) that role/group dedupe alone can miss.
            DeleteOwnedAnnotationOwnershipViolations(
                database,
                transaction,
                sourceEntity.Handle.ToString(),
                data.ElementId,
                deleteStaleElementIds: true);
        }

        return primaryCreated || framedOk;
    }

    private static bool UpsertStandaloneFramedItemOnlyLeader(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        bool copySourcePreservation,
        TimberAnnotationScaleContext annotationScaleContext,
        AutoCadAnnotationPresentationContext? presentationContext,
        ObjectId existingId,
        IReadOnlyList<ObjectId> obsoleteIds,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver)
    {
        if (presentationContext is null)
        {
            variantResultObserver?.Invoke(
                AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                    null,
                    null,
                    AutoCadDatabaseIdentity.TryGetIdentity(database),
                    "Standalone framed ItemOnly requires a presentation context."));
            return false;
        }

        var sourceHandle = sourceEntity.Handle.ToString();
        EraseLegacyG4FramedItemOnlyForSource(database, transaction, sourceHandle);

        var style = ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle);
        TimberFramedBlockContentKind contentKind;
        try
        {
            contentKind =
                TimberFramedBlockContentDefinitionRules.FromItemNumberLeaderStyle(
                    style);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            AcKrovyDiagnostics.Warning(
                "StandaloneFramedItemOnly",
                exception.Message);
            return false;
        }

        var itemCode = presentationContext.FramedItemCodeText;
        if (itemCode.ResolvedTextStyleId is not ObjectId textStyleId ||
            itemCode.ResolvedTextStyleName is null)
        {
            AcKrovyDiagnostics.Warning(
                "StandaloneFramedItemOnly",
                "Resolved item text style is required.");
            return false;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            AcKrovyDiagnostics.Warning(
                "StandaloneFramedItemOnly",
                "Item text-style ObjectId belongs to a different database.");
            return false;
        }

        var physicalAxis = TryGetSourceElementAxisRadians(sourceEntity, out var axis)
            ? axis
            : placement.RotationRadians;
        var scale = annotationScaleContext.ScaleFactor;
        var canonicalLayout =
            TimberItemLeaderLayoutCalculator.CalculateStandaloneNativeFramedItem(
                new TimberLeaderPlacement(
                    placement.Anchor.X,
                    placement.Anchor.Y,
                    placement.TextLocation.X,
                    placement.TextLocation.Y,
                    0d),
                contents,
                style,
                scale);
        var definitionRequest = new AutoCadFramedBlockContentRequest(
            contentKind,
            TimberFramedBlockContentPresentation.ItemOnly,
            itemCode.ResolvedTextStyleName,
            itemCode.ResolvedTextStyleName,
            itemCode.PaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            textStyleId,
            ObjectId.Null,
            contents);
        var layerId = EnsureLabelLayer(database, transaction, updateExisting: true);

        ObjectId ownerId = ObjectId.Null;
        // Source MOVE/STRETCH/ROTATE: TryUpdateInPlace returns false so we erase
        // and fall through to Create — absolute BlockRotation on a fresh MLeader
        // (in-place rewrite cannot clear AutoCAD ROTATE TransformBy residue when
        // framed BlockRotation is π-invariant under the readable 180° fold).
        var createUsesCanonicalManualOffset = false;
        if (!existingId.IsNull &&
            AutoCadObjectIdAccess.TryGetObject<MLeader>(
                transaction,
                existingId,
                OpenMode.ForWrite,
                out var existingLeader,
                database) &&
            existingLeader is not null &&
            !existingLeader.IsErased &&
            existingLeader.ContentType == ContentType.BlockContent &&
            ElementLabelStore.TryRead(existingLeader, out var existingMeta) &&
            existingMeta is not null &&
            AutoCadStandaloneFramedItemOnlyAnnotationService
                .IsStandaloneFramedItemOnlyOwner(existingMeta))
        {
            var sourceSync = TimberStandaloneNativeLeaderSourceSyncRules.Evaluate(
                existingMeta.AutomaticTextX,
                existingMeta.AutomaticTextY,
                existingMeta.RotationRadians,
                automaticPlacement.TextLocation.X,
                automaticPlacement.TextLocation.Y,
                physicalAxis);
            if (!sourceSync.RequiresCanonicalRebuild &&
                AutoCadStandaloneFramedItemOnlyAnnotationService.TryUpdateInPlace(
                    database,
                    transaction,
                    existingLeader,
                    definitionRequest,
                    canonicalLayout,
                    physicalAxis,
                    scale,
                    sourceSync))
            {
                WriteStandaloneFramedItemOnlyMetadata(
                    existingLeader,
                    transaction,
                    data,
                    sourceHandle,
                    placement,
                    automaticPlacement,
                    manualOffset,
                    contents,
                    physicalAxis);
                ownerId = existingLeader.ObjectId;
            }
            else
            {
                createUsesCanonicalManualOffset = sourceSync.RequiresCanonicalRebuild;
                EraseMainAnnotation(transaction, existingLeader);
            }
        }
        else if (!existingId.IsNull)
        {
            // Legacy Primary / BlockLeader framed leftover beside the new owner.
            EraseLegacyFramedLeaderBesideG4(transaction, existingId);
        }

        if (ownerId.IsNull)
        {
            var created = AutoCadStandaloneFramedItemOnlyAnnotationService.Create(
                database,
                transaction,
                definitionRequest,
                canonicalLayout,
                physicalAxis,
                scale,
                layerId);
            if (!created.Succeeded ||
                !AutoCadObjectIdAccess.TryGetObject<MLeader>(
                    transaction,
                    created.LeaderId,
                    OpenMode.ForWrite,
                    out var leader,
                    database) ||
                leader is null)
            {
                AcKrovyDiagnostics.Warning(
                    "StandaloneFramedItemOnly",
                    created.Diagnostic ?? "Create failed.");
                return false;
            }

            WriteStandaloneFramedItemOnlyMetadata(
                leader,
                transaction,
                data,
                sourceHandle,
                placement,
                automaticPlacement,
                createUsesCanonicalManualOffset
                    ? TimberFramedLeaderManualOffset.Zero
                    : manualOffset,
                contents,
                physicalAxis);
            ownerId = leader.ObjectId;
        }

        DeleteObsoleteLabels(transaction, obsoleteIds, ownerId);
        if (!copySourcePreservation)
        {
            DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
            DeleteOwnedAnnotationOwnershipViolations(
                database,
                transaction,
                sourceHandle,
                data.ElementId);
        }

        _ = previousElementId;
        return !ownerId.IsNull;
    }

    private static void WriteStandaloneFramedItemOnlyMetadata(
        MLeader leader,
        Transaction transaction,
        TimberElementData data,
        string sourceHandle,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        double physicalAxisRadians)
    {
        var lineIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        var attachment = placement.Anchor;
        if (lineIndexes.Length == 1)
        {
            var lines = leader.GetLeaderLineIndexes(lineIndexes[0]).Cast<int>().ToArray();
            if (lines.Length == 1)
            {
                attachment = leader.GetFirstVertex(lines[0]);
            }
        }

        var block = leader.BlockPosition;
        ElementLabelStore.Write(leader, transaction, new ElementLabelData
        {
            SchemaVersion =
                AutoCadStandaloneFramedItemOnlyAnnotationService
                    .LabelMetadataSchemaVersion,
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode),
            ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(
                data.ItemNumberLeaderStyle),
            Contents = contents,
            AnchorX = attachment.X,
            AnchorY = attachment.Y,
            TextX = block.X,
            TextY = block.Y,
            RotationRadians =
                TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    physicalAxisRadians),
            EnvelopeWidthMm = placement.EnvelopeWidthMm,
            EnvelopeHeightMm = placement.EnvelopeHeightMm,
            AutomaticTextX = automaticPlacement.TextLocation.X,
            AutomaticTextY = automaticPlacement.TextLocation.Y,
            LocalManualOffsetAlongAxisMm = manualOffset.AlongAxisMm,
            LocalManualOffsetNormalAxisMm = manualOffset.NormalAxisMm,
            PlacementRotationRadians =
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveFramedItemOnlyBlockRotationRadians(physicalAxisRadians),
            ComponentRole =
                AutoCadStandaloneFramedItemOnlyAnnotationService.OwnerRole,
            RendererGeneration =
                AutoCadStandaloneFramedItemOnlyAnnotationService.RendererGeneration,
        });
    }

    private static void EraseLegacyG4FramedItemOnlyForSource(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (var entry in ReadAnnotationEntities(database, transaction))
        {
            if (!string.Equals(
                    entry.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var role = entry.Data.ComponentRole;
            var isG4 = AutoCadFramedG4CompositePolicy.IsG4CompositeRole(role);
            if (!isG4)
            {
                continue;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForWrite,
                    out var entity) &&
                entity is not null &&
                !entity.IsErased)
            {
                EraseMainAnnotation(transaction, entity);
            }
        }
    }

    private static bool UpsertG5CombinedFramedLeader(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LeaderPlacement framedPlacement,
        bool copySourcePreservation,
        TimberAnnotationScaleContext annotationScaleContext,
        AutoCadAnnotationPresentationContext presentationContext)
    {
        var sourceHandle = sourceEntity.Handle.ToString();
        // Canonical create/refresh geometry always starts from automatic source
        // placement. Manual offset must never move Anchor/attachment.
        var automaticPlacement = framedPlacement;
        var placement = framedPlacement;
        var existingId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            TimberMainAnnotationRepresentation.BlockLeader,
            AutoCadFramedBlockContentProductionPolicy.CombinedRole,
            preserveCompositeSiblings: true,
            allowElementIdFallback: !copySourcePreservation,
            out var obsoleteIds);

        // Prefer an existing production G5 Combined MLeader for in-place update.
        ObjectId existingG5Id = ObjectId.Null;
        if (!existingId.IsNull &&
            AutoCadObjectIdAccess.TryGetObject<MLeader>(
                transaction,
                existingId,
                OpenMode.ForRead,
                out var existingLeader,
                database) &&
            existingLeader is not null &&
            ElementLabelStore.TryRead(existingLeader, out var existingData) &&
            existingData is not null &&
            AutoCadFramedBlockContentProductionPolicy.IsG5CombinedMetadata(
                existingData))
        {
            existingG5Id = existingId;
            // Existing placement has priority: never move Anchor via manual offset.
            // Content-only refresh reads live attachment/knee/BlockPosition instead.
            if (!TimberFramedCombinedG5RefreshPlacementRules.ManualOffsetMayMoveAnchor &&
                TryCreateDoglegDirection(
                    existingData.CombinedDoglegDirectionX,
                    existingData.CombinedDoglegDirectionY,
                    out var persistedDogleg))
            {
                placement = placement with
                {
                    DoglegDirection = persistedDogleg,
                };
            }
        }
        else if (!existingId.IsNull)
        {
            // Legacy FramedItem BlockLeader beside G5 path — erase before create.
            EraseMainAnnotation(
                transaction,
                (Entity)transaction.GetObject(existingId, OpenMode.ForWrite));
        }

        // Create and geometry rebuild share the same canonical attachment inputs.
        if (!TryBuildG5CombinedRequest(
                database,
                transaction,
                sourceEntity,
                data,
                automaticPlacement,
                annotationScaleContext,
                presentationContext,
                out var request,
                out var frameDefinition,
                out var diagnostic))
        {
            AcKrovyDiagnostics.Warning("G5CombinedFramed", diagnostic);
            return false;
        }

        ObjectId leaderId;
        TimberFramedCombinedG5SourceRotationRebuildDecision?
            sourceRotationRebuildDecision = null;
        if (!existingG5Id.IsNull &&
            TryUpdateG5CombinedInPlace(
                database,
                transaction,
                existingG5Id,
                request,
                data,
                sourceHandle,
                sourceEntity,
                placement,
                automaticPlacement,
                frameDefinition,
                out leaderId,
                out sourceRotationRebuildDecision))
        {
            DeleteObsoleteLabels(transaction, obsoleteIds, leaderId);
            DeleteOwnedG4CombinedPartsForSourceHandle(
                database,
                transaction,
                sourceHandle,
                keepLeaderId: leaderId);
            return true;
        }

        if (!existingG5Id.IsNull)
        {
            EraseMainAnnotation(
                transaction,
                (Entity)transaction.GetObject(existingG5Id, OpenMode.ForWrite));
        }

        DeleteOwnedG4CombinedPartsForSourceHandle(
            database,
            transaction,
            sourceHandle,
            keepLeaderId: ObjectId.Null);

        var created = AutoCadFramedBlockContentAnnotationService.Create(
            database,
            transaction,
            request);
        if (!created.Succeeded ||
            created.LeaderId is not ObjectId createdId ||
            createdId.IsNull ||
            transaction.GetObject(createdId, OpenMode.ForWrite) is not MLeader leader)
        {
            AcKrovyDiagnostics.Warning(
                "G5CombinedFramed",
                created.DiagnosticReason ?? "G5 Combined create failed.");
            return false;
        }

        WriteG5CombinedMetadata(
            leader,
            transaction,
            data,
            sourceHandle,
            automaticPlacement,
            automaticPlacement,
            request.ElementAxisRadians,
            frameDefinition,
            created.ReferencePresentationRevision);
#if DEBUG
        if (sourceRotationRebuildDecision?.AnnotationRebuildRequired == true)
        {
            CompleteSourceRotationRebuildTrace(
                sourceHandle,
                leader.Handle.ToString());
        }
#endif
        DeleteObsoleteLabels(transaction, obsoleteIds, createdId);
        DeleteOwnedG4CombinedPartsForSourceHandle(
            database,
            transaction,
            sourceHandle,
            keepLeaderId: createdId);
        return true;
    }

    private static bool TryUpdateG5CombinedInPlace(
        Database database,
        Transaction transaction,
        ObjectId existingId,
        AutoCadFramedBlockContentAnnotationRequest request,
        TimberElementData data,
        string sourceHandle,
        Entity sourceEntity,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        TimberItemLeaderBlockDefinition? frameDefinition,
        out ObjectId leaderId,
        out TimberFramedCombinedG5SourceRotationRebuildDecision?
            sourceRotationRebuildDecision)
    {
        _ = database;
        leaderId = ObjectId.Null;
        sourceRotationRebuildDecision = null;
        if (transaction.GetObject(existingId, OpenMode.ForWrite, false) is not
                MLeader leader ||
            leader.IsErased ||
            leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            return false;
        }

        if (transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord existingBlock ||
            !AutoCadFramedBlockContentPolicy.IsProductionFamilyName(existingBlock.Name))
        {
            return false;
        }

        ElementLabelData? existingLabelData = null;
        _ = ElementLabelStore.TryRead(leader, out existingLabelData);
        var newPlacementRotation = automaticPlacement.RotationRadians;
        var oldPlacementRotation =
            existingLabelData?.PlacementRotationRadians ??
            newPlacementRotation;
        sourceRotationRebuildDecision =
            TimberFramedCombinedG5SourceRotationRebuildRules
                .DecideFromPersistedMetadata(
                    existingLabelData?.RotationRadians,
                    existingLabelData?.PlacementRotationRadians,
                    request.ElementAxisRadians);
        if (sourceRotationRebuildDecision.AnnotationRebuildRequired)
        {
#if DEBUG
            RecordSourceRotationRebuildPending(
                sourceHandle,
                sourceRotationRebuildDecision,
                leader.Handle.ToString());
#endif
            // Host-proven lifecycle: never repair a source-rotated production
            // R3 Combined MLeader in place. The caller erases this exact owned
            // entity and continues through the canonical production CREATE.
            return false;
        }

        var attachment = ReadG5Attachment(leader);
        // Preserve live placement unless source attachment or scale actually changed.
        if (!TimberFramedCombinedG5RefreshPlacementRules.ShouldPreserveExistingPlacement(
                attachment.X,
                attachment.Y,
                automaticPlacement.Anchor.X,
                automaticPlacement.Anchor.Y,
                leader.BlockScale.X,
                request.BlockScale))
        {
            return false;
        }

        var blockId = leader.BlockContentId;

        // World presentation is authoritative. BlockRotation is only relative
        // to the implicit CREATE TransformBy basis and cannot be captured as an
        // absolute replacement value.
        var blockRotationBeforeRefresh = leader.BlockRotation;
        var presentationMeasured =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var livePresentation,
                    out _);
        if (!presentationMeasured)
        {
            livePresentation =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .CaptureBlockContentPresentationRadians(transaction, leader);
        }

        var refreshPresentation =
            TimberFramedCombinedG5SourceRotationRules.ResolveRefreshPresentationRadians(
                oldPlacementRotation,
                newPlacementRotation,
                livePresentation);
        // Content-only refresh: pick R3 kind + RIGHT/LEFT for the live relative
        // side. Never re-apply CREATE 60°. Preserve leader vertices + user placement.
        if (!TrySyncG5CombinedContentVariant(
                database,
                transaction,
                leader,
                sourceEntity,
                request,
                refreshPresentation))
        {
            return false;
        }

        ApplyG5CombinedAttributeValues(
            transaction,
            leader,
            leader.BlockContentId.IsNull ? blockId : leader.BlockContentId,
            request);

        // BTR/AttrRef update may alter the measured world basis. Correct only
        // by desiredWorld-currentWorld in relative BlockRotation space. For an
        // ordinary "Obnoviť popisy" refresh this produces world delta 0°.
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryRestoreBlockContentPresentationAfterRefresh(
                    transaction,
                    leader,
                    oldPlacementRotation,
                    newPlacementRotation,
                    livePresentation,
                    out var refreshDecision,
                    out _))
        {
            // Valid production R3 has measurable AttrRef geometry. Fail closed
            // without converting a world angle into absolute BlockRotation.
            var estimatedWorldAfterContent =
                TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    livePresentation +
                    TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                        leader.BlockRotation - blockRotationBeforeRefresh));
            var fallbackDecision =
                TimberFramedCombinedG5RefreshPlacementRules
                    .ResolveContentOnlyRefreshPresentation(
                        oldPlacementRotation,
                        newPlacementRotation,
                        livePresentation,
                        estimatedWorldAfterContent,
                        leader.BlockRotation);
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .PreserveBlockContentPresentationRotation(
                    transaction,
                    leader,
                    fallbackDecision.TargetBlockRotation);
        }
#if DEBUG
        else
        {
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .RecordRefreshPresentationTrace(sourceHandle, refreshDecision);
        }
#endif
        var referencePresentationRevision =
            ApplyG5CombinedReferencePresentationAfterRefresh(
            database,
            transaction,
            leader,
            sourceEntity,
            request,
            sourceHandle,
            existingLabelData?.R3ReferencePresentationRevision ?? 0);
        if (AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(
                transaction,
                leader,
                request.ElementAxisRadians,
                referencePresentationRevision,
                "Refresh",
                out var wholeAnnotationOperation,
                out var wholeAnnotationReason))
        {
            referencePresentationRevision =
                wholeAnnotationOperation.Decision.RevisionAfter;
        }
        else
        {
            AcKrovyDiagnostics.Warning(
                "G5CombinedWholeAnnotationHalfTurn",
                wholeAnnotationReason);
        }

        WriteG5CombinedMetadata(
            leader,
            transaction,
            data,
            sourceHandle,
            placement,
            automaticPlacement,
            request.ElementAxisRadians,
            frameDefinition,
            referencePresentationRevision);
#if DEBUG
        RecordSourceRotationNoRebuildTrace(
            sourceHandle,
            sourceRotationRebuildDecision,
            leader.Handle.ToString());
#endif
        leaderId = existingId;
        return true;
    }

    /// <summary>
    /// Idempotent adoption of the two CREATE reference directions by an
    /// already-existing production R3 entity. The ordinary refresh-preservation
    /// step above remains unchanged; this runs afterward only for exact source
    /// 90°/180° and applies a measured WCS delta plus the existing R3 side sync.
    /// No leader geometry or user placement is written.
    /// </summary>
    internal static int ApplyG5CombinedReferencePresentationAfterRefresh(
        Database database,
        Transaction transaction,
        MLeader leader,
        Entity sourceEntity,
        AutoCadFramedBlockContentAnnotationRequest request,
        string sourceHandle,
        int currentRevision)
    {
        var blockRotationBefore = leader.BlockRotation;
        var hasWorldBefore =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var currentWorld,
                    out var worldBeforeNote);
        if (!TimberFramedBlockContentReadableOrientationRules
                .ShouldAdoptReferencePresentation(
                    request.ElementAxisRadians,
                    currentRevision))
        {
#if DEBUG
            RecordG5CombinedReferencePresentationTrace(
                transaction,
                leader,
                request,
                sourceHandle,
                hasWorldBefore ? currentWorld : null,
                decision: null,
                blockRotationBefore,
                currentRevision,
                currentRevision,
                "TryUpdateG5CombinedInPlace>RefreshPreservation>" +
                "ReferenceAdoptionSkipped",
                worldBeforeNote);
#endif
            return currentRevision;
        }

        if (!hasWorldBefore)
        {
#if DEBUG
            RecordG5CombinedReferencePresentationTrace(
                transaction,
                leader,
                request,
                sourceHandle,
                worldBefore: null,
                decision: null,
                blockRotationBefore,
                currentRevision,
                currentRevision,
                "TryUpdateG5CombinedInPlace>RefreshPreservation>" +
                "ReferenceMeasureFailed",
                worldBeforeNote);
#endif
            return currentRevision;
        }

        var decision =
            TimberFramedBlockContentReadableOrientationRules
                .ResolveCreateReferenceFinalWorldPresentation(
                    request.ElementAxisRadians,
                    currentWorld,
                    leader.BlockRotation);
        if (!decision.AppliesReferenceRule)
        {
            return currentRevision;
        }

        if (decision.AppliesHalfTurn)
        {
            leader.BlockRotation = decision.TargetBlockRotation;
            _ = TrySyncG5CombinedContentVariant(
                database,
                transaction,
                leader,
                sourceEntity,
                request,
                decision.VerticalRuleOutput);

            // A BTR swap may restore its captured presentation. Re-measure and
            // install only the remaining relative WCS delta to the fixed target.
            var hasFinalWorld =
                AutoCadFramedBlockContentDimensionColumnPlacementService
                    .TryResolveWorldContentXAxis(
                        transaction,
                        leader,
                        out var worldAfterVariant,
                        out _);
            if (hasFinalWorld)
            {
                // Do not invoke the reference rule a second time. Install only
                // the remaining WCS delta to its one original desired angle.
                var remainingDelta =
                    TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                        decision.VerticalRuleOutput - worldAfterVariant);
                leader.BlockRotation =
                    TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                        leader.BlockRotation + remainingDelta);
            }

            leader.RecordGraphicsModified(true);
        }
        var hasPlacement =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryEvaluate(
                    transaction,
                    leader,
                    out var placement,
                    out _,
                    out _);
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var finalWorld,
                    out _) ||
            !hasPlacement ||
            !placement.Current.IsCorrect ||
            Math.Abs(
                TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                    finalWorld - decision.VerticalRuleOutput)) >
                TimberFramedBlockContentReadableOrientationRules
                    .AngleToleranceRadians)
        {
#if DEBUG
            RecordG5CombinedReferencePresentationTrace(
                transaction,
                leader,
                request,
                sourceHandle,
                currentWorld,
                decision,
                blockRotationBefore,
                currentRevision,
                currentRevision,
                "TryUpdateG5CombinedInPlace>RefreshPreservation>MeasureWorld>" +
                "WorldDelta>R3Variant>ReassertBR>FinalVerificationFailed",
                "final world/toward-knee verification failed");
#endif
            return currentRevision;
        }

        var adoptedRevision = TimberFramedBlockContentReadableOrientationRules
            .ReferencePresentationRevision;
#if DEBUG
        RecordG5CombinedReferencePresentationTrace(
            transaction,
            leader,
            request,
            sourceHandle,
            currentWorld,
            decision,
            blockRotationBefore,
            currentRevision,
            adoptedRevision,
            "TryUpdateG5CombinedInPlace>RefreshPreservation>MeasureWorld>" +
            "WorldDelta>R3Variant>ReassertBR>MeasureFinal>WriteRevision",
            "existing R3 reference presentation adopted");
#endif
        return adoptedRevision;
    }

#if DEBUG
    private static void RecordG5CombinedReferencePresentationTrace(
        Transaction transaction,
        MLeader leader,
        AutoCadFramedBlockContentAnnotationRequest request,
        string sourceHandle,
        double? worldBefore,
        R3CreateReferencePresentationDecision? decision,
        double blockRotationBefore,
        int revisionBefore,
        int revisionAfter,
        string operationSequence,
        string note)
    {
        var hasWorldAfter =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var worldAfter,
                    out var worldAfterNote);
        var hasPlacement =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryEvaluate(
                    transaction,
                    leader,
                    out var placement,
                    out var points,
                    out var placementNote);
        var dimensionCenter = hasPlacement
            ? new TimberPlanarPoint(
                (points.WidthAlignment.X + points.HeightAlignment.X) / 2d,
                (points.WidthAlignment.Y + points.HeightAlignment.Y) / 2d)
            : default;
        var towardKneeDot = double.NaN;
        var hasDot = hasPlacement &&
            TimberFramedBlockContentDefinitionRules
                .TryEvaluateDimensionsTowardKneeDot(
                    new TimberPlanarPoint(
                        points.ItemAlignment.X,
                        points.ItemAlignment.Y),
                    new TimberPlanarPoint(points.Knee.X, points.Knee.Y),
                    dimensionCenter,
                    out towardKneeDot);
        string? blockName = null;
        if (!leader.BlockContentId.IsNull &&
            transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead,
                true) is BlockTableRecord block)
        {
            blockName = block.Name;
        }

        AutoCadFramedBlockContentAnnotationService
            .RecordProductionPresentationTrace(
                leader.ObjectId.Handle.ToString(),
                new R3CreatePresentationTrace(
                    SourceHandle: sourceHandle,
                    SourcePhysicalAxisAngle: request.ElementAxisRadians,
                    VerticalRuleInput: decision?.VerticalRuleInput,
                    VerticalRuleOutput: decision?.VerticalRuleOutput,
                    TransformByAngle: double.NaN,
                    BlockRotationBefore: blockRotationBefore,
                    BlockRotationRequested:
                        decision?.TargetBlockRotation ?? leader.BlockRotation,
                    BlockRotationAfter: leader.BlockRotation,
                    FrameWorldOrientationBefore: worldBefore,
                    FrameWorldOrientationAfter:
                        hasWorldAfter ? worldAfter : null,
                    ItemTextWorldAngle:
                        AutoCadFramedBlockContentAnnotationService
                            .TryReadAttributeRotationRadians(
                                transaction,
                                leader,
                                TimberFramedBlockContentDefinitionRules.ItemNoTag),
                    WidthTextWorldAngle:
                        AutoCadFramedBlockContentAnnotationService
                            .TryReadAttributeRotationRadians(
                                transaction,
                                leader,
                                TimberFramedBlockContentDefinitionRules.WidthTag),
                    HeightTextWorldAngle:
                        AutoCadFramedBlockContentAnnotationService
                            .TryReadAttributeRotationRadians(
                                transaction,
                                leader,
                                TimberFramedBlockContentDefinitionRules.HeightTag),
                    AppliedHalfTurn: revisionAfter > revisionBefore,
                    BlockNameBeforeCorrection: blockName,
                    BlockNameAfterCorrection: blockName,
                    ContentVariant: blockName,
                    DimensionsTowardKneeAfter:
                        hasPlacement && placement.Current.IsCorrect,
                    DimensionsTowardKneeDot: hasDot ? towardKneeDot : null,
                    PresentationPath:
                        revisionAfter > revisionBefore
                            ? "ExistingUpdate"
                            : "Refresh",
                    PresentationOperationSequence: operationSequence,
                    ReferenceRevisionBefore: revisionBefore,
                    ReferenceRevisionAfter: revisionAfter,
                    MeasurementNote:
                        $"{note}; worldAfter={worldAfterNote}; " +
                        $"placement={placementNote}"));
    }
#endif

    private static bool TrySyncG5CombinedContentVariant(
        Database database,
        Transaction transaction,
        MLeader leader,
        Entity sourceEntity,
        AutoCadFramedBlockContentAnnotationRequest request,
        double effectiveContentWorldAngleRadians)
    {
        if (leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            return true;
        }

        Point3d start;
        Point3d end;
        switch (sourceEntity)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                break;
            case Polyline polyline:
                start = polyline.StartPoint;
                end = polyline.EndPoint;
                break;
            default:
                return true;
        }

        if (transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased ||
            !TimberFramedBlockContentVariantRules.IsProductionR3Combined(block.Name))
        {
            return true;
        }

        var values = new List<(string Tag, string Text, double Height)>
        {
            (
                TimberFramedBlockContentDefinitionRules.ItemNoTag,
                request.ItemNoText,
                request.ItemAttributeBaselineHeightMm),
            (
                TimberFramedBlockContentDefinitionRules.WidthTag,
                request.WidthText,
                request.DimensionAttributeBaselineHeightMm),
            (
                TimberFramedBlockContentDefinitionRules.HeightTag,
                request.HeightText,
                request.DimensionAttributeBaselineHeightMm),
        };

        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .EnsureCorrectR3ContentVariantFromFinalGeometry(
                    database,
                    transaction,
                    leader,
                    start.X,
                    start.Y,
                    end.X,
                    end.Y,
                    request.ContentKind,
                    request.ItemTextStyleName,
                    request.DimensionTextStyleName,
                    request.ItemTextStyleId,
                    request.DimensionTextStyleId,
                    request.ItemPaperHeightMm,
                    request.DimensionPaperHeightMm,
                    request.ItemNoText,
                    values,
                    out _,
                    out _,
                    out var afterBlockContentId,
                    out var note,
                    effectiveContentWorldAngleRadians))
        {
            AcKrovyDiagnostics.Warning(
                "G5CombinedR3ContentIdentity",
                note);
            return false;
        }

        if (afterBlockContentId.IsNull ||
            transaction.GetObject(afterBlockContentId, OpenMode.ForRead, true) is not
                BlockTableRecord afterBlock ||
            afterBlock.IsErased ||
            !TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                afterBlock.Name,
                out var afterParse) ||
            !TimberFramedCombinedG5ContentVariantRules.IsContentKindMatch(
                afterParse.ContentKind,
                request.ContentKind))
        {
            AcKrovyDiagnostics.Warning(
                "G5CombinedR3ContentIdentity",
                "final physical BTR content kind disagrees with requested style");
            return false;
        }

        return true;
    }

    private static void ApplyG5CombinedAttributeValues(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        AutoCadFramedBlockContentAnnotationRequest request)
    {
        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
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
            else if (string.Equals(
                         definition.Tag,
                         TimberFramedBlockContentDefinitionRules.WidthTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                text = request.WidthText;
                height = request.DimensionAttributeBaselineHeightMm;
            }
            else if (string.Equals(
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

            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = text;
            attribute.Height = height;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }
    }

    private static bool TryBuildG5CombinedRequest(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        LeaderPlacement placement,
        TimberAnnotationScaleContext annotationScaleContext,
        AutoCadAnnotationPresentationContext presentationContext,
        out AutoCadFramedBlockContentAnnotationRequest request,
        out TimberItemLeaderBlockDefinition? frameDefinition,
        out string diagnostic)
    {
        request = null!;
        frameDefinition = null;
        diagnostic = string.Empty;

        var itemRole = presentationContext.FramedItemCodeText;
        var dimRole = presentationContext.DimensionText;
        if (itemRole.ResolvedTextStyleId is not ObjectId itemStyleId ||
            itemStyleId.IsNull ||
            dimRole.ResolvedTextStyleId is not ObjectId dimStyleId ||
            dimStyleId.IsNull ||
            string.IsNullOrWhiteSpace(itemRole.ResolvedTextStyleName) ||
            string.IsNullOrWhiteSpace(dimRole.ResolvedTextStyleName))
        {
            diagnostic =
                "G5 Combined requires resolved item and dimension text styles.";
            return false;
        }

        TimberFramedBlockContentKind contentKind;
        try
        {
            contentKind =
                TimberFramedBlockContentDefinitionRules.FromItemNumberLeaderStyle(
                    data.ItemNumberLeaderStyle);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            diagnostic = exception.Message;
            return false;
        }

        frameDefinition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle),
            data.ElementId);
        var scale = annotationScaleContext.ScaleFactor;
        var denom = annotationScaleContext.Denominator;
        var frameWidth = frameDefinition.WidthMm * scale;
        var frameHeight = frameDefinition.HeightMm * scale;
        var envelope =
            TimberFramedBlockContentDefinitionRules
                .CalculateReferenceDimensionEnvelopeWidthMm(dimRole.PaperHeightMm) *
            scale;
        var firstSegment =
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm * scale;
        var landing =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scale;
        var layerId = EnsureLabelLayer(database, transaction, updateExisting: true);

        // Raw Start→End axis (not the already-readable placement rotation) so
        // readability flip can mirror layout Side and keep ActualWorldSide=Right.
        var rawAxisRadians = TryGetSourceElementAxisRadians(
            sourceEntity,
            out var sourceAxis)
            ? sourceAxis
            : placement.RotationRadians;
        var createLayoutSide =
            TimberFramedCombinedG5CreatePlacementRules.ResolveCreateLayoutSide(
                rawAxisRadians);
        request = new AutoCadFramedBlockContentAnnotationRequest(
            placement.Anchor.X,
            placement.Anchor.Y,
            rawAxisRadians,
            createLayoutSide,
            contentKind,
            TimberFramedBlockContentPresentation.Combined,
            frameWidth,
            frameHeight,
            envelope,
            denom,
            itemRole.PaperHeightMm,
            dimRole.PaperHeightMm,
            itemRole.ResolvedTextStyleName!,
            dimRole.ResolvedTextStyleName!,
            itemStyleId,
            dimStyleId,
            data.ElementId,
            $"{data.WidthMm:0}",
            $"{data.HeightMm:0}",
            firstSegment,
            landing,
            layerId,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh);
        return true;
    }

    private static bool TryGetSourceElementAxisRadians(
        Entity sourceEntity,
        out double axisRadians)
    {
        axisRadians = 0d;
        Point3d start;
        Point3d end;
        switch (sourceEntity)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                break;
            case Polyline polyline:
                start = polyline.StartPoint;
                end = polyline.EndPoint;
                break;
            default:
                return false;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <= PlacementToleranceMm)
        {
            return false;
        }

        axisRadians = Math.Atan2(dy, dx);
        return true;
    }

    private static void WriteG5CombinedMetadata(
        MLeader leader,
        Transaction transaction,
        TimberElementData data,
        string sourceHandle,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        double sourcePhysicalAxisRadians,
        TimberItemLeaderBlockDefinition? frameDefinition,
        int referencePresentationRevision)
    {
        // Persist live MLeader geometry so the next refresh Capture residual is
        // zero unless the user actually moved BlockPosition.
        var liveAttachment = ReadG5Attachment(leader);
        var liveKnee = ReadG5Knee(leader);
        var liveBlockPosition = leader.BlockPosition;
        var placementRotation = placement.RotationRadians;
        var sourcePhysicalAxis =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourcePhysicalAxisRadians);
        var dogleg = placement.DoglegDirection;
        if (dogleg is null &&
            liveKnee.DistanceTo(liveBlockPosition) > 1e-9d)
        {
            dogleg = (liveBlockPosition - liveKnee).GetNormal();
        }

        ElementLabelStore.Write(leader, transaction, new ElementLabelData
        {
            SchemaVersion =
                AutoCadFramedBlockContentProductionPolicy.LabelMetadataSchemaVersion,
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode),
            ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(
                data.ItemNumberLeaderStyle),
            Contents = data.ElementId,
            AnchorX = liveAttachment.X,
            AnchorY = liveAttachment.Y,
            TextX = liveBlockPosition.X,
            TextY = liveBlockPosition.Y,
            // Existing field, G5-specific semantics: physical Start→End source
            // direction. PlacementRotationRadians remains the readable layout
            // angle. Keeping both like-for-like prevents ±90° readability flips
            // from masquerading as ~180° source rotations.
            RotationRadians = sourcePhysicalAxis,
            EnvelopeWidthMm = placement.EnvelopeWidthMm,
            EnvelopeHeightMm = placement.EnvelopeHeightMm,
            AutomaticTextX = automaticPlacement.TextLocation.X,
            AutomaticTextY = automaticPlacement.TextLocation.Y,
            LocalManualOffsetAlongAxisMm = 0d,
            LocalManualOffsetNormalAxisMm = 0d,
            PlacementRotationRadians = placementRotation,
            CombinedDoglegDirectionX = dogleg?.X,
            CombinedDoglegDirectionY = dogleg?.Y,
            ComponentRole = AutoCadFramedBlockContentProductionPolicy.CombinedRole,
            AnnotationGroupId = null,
            RendererGeneration =
                AutoCadFramedBlockContentProductionPolicy.RendererGeneration,
            FrameSize = frameDefinition?.Size,
            R3ReferencePresentationRevision = referencePresentationRevision,
        });
    }

    private static void DeleteOwnedG4CombinedPartsForSourceHandle(
        Database database,
        Transaction transaction,
        string sourceHandle,
        ObjectId keepLeaderId)
    {
        foreach (var entry in ReadAnnotationEntities(database, transaction))
        {
            if (!string.Equals(
                    entry.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) ||
                entry.Id == keepLeaderId)
            {
                continue;
            }

            var isLegacyG4 = AutoCadFramedG4CompositePolicy.IsG4CompositeRole(
                entry.Data.ComponentRole);
            var isPrimary = entry.Data.ComponentRole ==
                TimberMainAnnotationComponentRole.Primary;
            var isNonG5FramedItem =
                entry.Data.ComponentRole ==
                    TimberMainAnnotationComponentRole.FramedItem &&
                entry.Data.RendererGeneration !=
                    AutoCadFramedBlockContentProductionPolicy.RendererGeneration;
            if (!isLegacyG4 && !isPrimary && !isNonG5FramedItem)
            {
                continue;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForWrite,
                    out var entity) &&
                entity is not null &&
                !entity.IsErased)
            {
                EraseMainAnnotation(transaction, entity);
            }
        }
    }

    private static Point3d ReadG5Attachment(MLeader leader)
    {
        foreach (int leaderIndex in leader.GetLeaderIndexes())
        {
            foreach (int lineIndex in leader.GetLeaderLineIndexes(leaderIndex))
            {
                return leader.GetFirstVertex(lineIndex);
            }
        }

        return Point3d.Origin;
    }

    private static Point3d ReadG5Knee(MLeader leader)
    {
        foreach (int leaderIndex in leader.GetLeaderIndexes())
        {
            foreach (int lineIndex in leader.GetLeaderLineIndexes(leaderIndex))
            {
                return leader.GetLastVertex(lineIndex);
            }
        }

        return Point3d.Origin;
    }

    private static void DeleteUnexpectedCompositeComponents(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        var matchingEntries = ReadAnnotationEntities(database, transaction)
            .Where(entry => string.Equals(
                entry.Data.SourceHandle,
                sourceHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var itemStyle = matchingEntries
            .Select(entry => entry.Data.ItemNumberLeaderStyle)
            .FirstOrDefault(ItemNumberLeaderStyle.Plain);
        var keysToDelete =
            TimberCompositeAnnotationLifecycleRules.SelectUnexpectedComponentKeys(
                TimberAnnotationMode.DimensionsWithItemNumber,
                itemStyle,
                matchingEntries.Select(entry => new TimberElementLabelCandidate
                {
                    LabelKey = entry.Id.ToString(),
                    ElementId = entry.Data.ElementId,
                    SourceHandle = entry.Data.SourceHandle,
                    ComponentRole = entry.Data.ComponentRole,
                    AnnotationGroupId = entry.Data.AnnotationGroupId,
                }).ToArray());
        foreach (var entry in matchingEntries.Where(entry =>
            keysToDelete.Contains(entry.Id.ToString(), StringComparer.OrdinalIgnoreCase)))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForWrite,
                    out var annotation) &&
                annotation is not null &&
                !annotation.IsErased)
            {
                EraseMainAnnotation(transaction, annotation);
            }
        }
    }

    private static void WriteLeaderMetadata(
        MLeader leader,
        Transaction transaction,
        TimberElementData data,
        string sourceHandle,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        TimberMainAnnotationComponentRole componentRole)
    {
        var normalizedMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode);
        // DimensionsLeader + standalone Plain ItemOnly: RotationRadians = physical
        // Start→End; PlacementRotation = absolute text presentation (vertical
        // 90°/270° share BOTTOM→TOP). Combined / other modes keep
        // PlacementRotation = placement.
        var placementRotationRadians =
            normalizedMode == TimberAnnotationMode.DimensionsLeader ||
            (normalizedMode == TimberAnnotationMode.ItemNumberLeader &&
             ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) ==
                 ItemNumberLeaderStyle.Plain)
                ? TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(placement.RotationRadians)
                : placement.RotationRadians;
        ElementLabelStore.Write(leader, transaction, new ElementLabelData
        {
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = normalizedMode,
            ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(
                data.ItemNumberLeaderStyle),
            Contents = contents,
            AnchorX = placement.Anchor.X,
            AnchorY = placement.Anchor.Y,
            TextX = placement.TextLocation.X,
            TextY = placement.TextLocation.Y,
            RotationRadians = placement.RotationRadians,
            EnvelopeWidthMm = placement.EnvelopeWidthMm,
            EnvelopeHeightMm = placement.EnvelopeHeightMm,
            AutomaticTextX = automaticPlacement.TextLocation.X,
            AutomaticTextY = automaticPlacement.TextLocation.Y,
            LocalManualOffsetAlongAxisMm = manualOffset.AlongAxisMm,
            LocalManualOffsetNormalAxisMm = manualOffset.NormalAxisMm,
            PlacementRotationRadians = placementRotationRadians,
            CombinedDoglegDirectionX = placement.DoglegDirection?.X,
            CombinedDoglegDirectionY = placement.DoglegDirection?.Y,
            ComponentRole = componentRole,
        });
    }

    public static ElementLabelUpdateResult UpdateAll(Database database, Editor editor)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = UpdateInCurrentTransaction(
            database,
            transaction,
            editor,
            DrawingScanner.FindAllTimberElements(database, transaction, metadataStore),
            metadataStore);
        transaction.Commit();
        return result;
    }

    public static ElementLabelUpdateResult UpdateSelected(
        Database database,
        Editor editor,
        IReadOnlyList<ObjectId> ids)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = UpdateInCurrentTransaction(database, transaction, editor, ids, metadataStore);
        transaction.Commit();
        return result;
    }

    public static bool SetVisible(Database database, Transaction transaction, bool visible)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (!table.Has(LabelLayerName))
        {
            return false;
        }

        var layer = (LayerTableRecord)transaction.GetObject(table[LabelLayerName], OpenMode.ForWrite);
        layer.IsOff = !visible;
        return true;
    }

    internal static IReadOnlyList<TimberElementLabelCandidate> ReadLabelCandidates(
        Database database,
        Transaction transaction)
    {
        return ReadLabels(database, transaction)
            .Select(label => new TimberElementLabelCandidate
            {
                LabelKey = label.Id.ToString(),
                ElementId = label.Data.ElementId,
                SourceHandle = label.Data.SourceHandle,
                ComponentRole = label.Data.ComponentRole,
            })
            .ToList();
    }

    internal static IReadOnlySet<ObjectId> FindCircleNormalizationSourceIds(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> sourceIds,
        AutoCadAnnotationScaleService annotationScaleService)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var sourceByHandle = new Dictionary<
            string,
            (ObjectId Id, TimberAnnotationScaleContext ScaleContext)>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var id in sourceIds.Distinct())
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            sourceByHandle[entity.Handle.ToString()] = (
                id,
                annotationScaleService.ResolveForElement(data));
        }

        var result = new HashSet<ObjectId>();

        foreach (var annotation in ReadLabels(database, transaction))
        {
            if (annotation.Data.ItemNumberLeaderStyle != ItemNumberLeaderStyle.Circle ||
                !sourceByHandle.TryGetValue(annotation.Data.SourceHandle, out var source) ||
                transaction.GetObject(annotation.Id, OpenMode.ForRead, false) is not
                    MLeader leader ||
                !RequiresCircleNormalization(
                    transaction,
                    leader,
                    source.ScaleContext.ScaleFactor))
            {
                continue;
            }

            result.Add(source.Id);
        }

        return result;
    }

    private static bool RequiresCircleNormalization(
        Transaction transaction,
        MLeader leader,
        double presentationScaleFactor)
    {
        if (leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull ||
            Math.Abs(
                leader.BlockScale.X -
                presentationScaleFactor) > 0.001d ||
            Math.Abs(
                leader.BlockScale.Y -
                presentationScaleFactor) > 0.001d ||
            Math.Abs(
                leader.BlockScale.Z -
                presentationScaleFactor) > 0.001d ||
            transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, false) is not
                BlockTableRecord block)
        {
            return true;
        }

        var circles = block
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, true))
            .OfType<Circle>()
            .Where(circle => !circle.IsErased)
            .ToArray();
        var itemNumberAttributes = block
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, true))
            .OfType<AttributeDefinition>()
            .Where(attribute =>
                !attribute.IsErased &&
                string.Equals(
                    attribute.Tag,
                    TimberItemLeaderBlockDefinitionRules.AttributeTag,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return circles.Length != 1 ||
            !TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(
                circles[0].Radius * 2d) ||
            itemNumberAttributes.Length != 1 ||
            !TimberItemLeaderBlockDefinitionRules
                .HasExpectedFramedItemTextHeight(
                    itemNumberAttributes[0].Height) ||
            itemNumberAttributes[0].HorizontalMode !=
                TextHorizontalMode.TextCenter ||
            itemNumberAttributes[0].VerticalMode !=
                TextVerticalMode.TextVerticalMid ||
            itemNumberAttributes[0].Position.DistanceTo(Point3d.Origin) >
                PlacementToleranceMm ||
            itemNumberAttributes[0].AlignmentPoint.DistanceTo(Point3d.Origin) >
                PlacementToleranceMm;
    }

    internal static void PersistFramedManualOffsets(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> annotationIds)
    {
        foreach (var id in annotationIds.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<MLeader>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var leader,
                    database) ||
                leader is null ||
                leader.ContentType != ContentType.BlockContent)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(leader, out var data) ||
                data is null ||
                !TimberAnnotationModeRules.IsFramedItemLeader(
                    data.AnnotationMode,
                    data.ItemNumberLeaderStyle))
            {
                continue;
            }

            var actualBlockPosition = leader.BlockPosition;
            var rotation = data.PlacementRotationRadians ??
                data.RotationRadians ??
                0d;
            var offset = TimberFramedLeaderManualOffsetCalculator.Capture(
                new TimberFramedLeaderManualOffset(
                    data.LocalManualOffsetAlongAxisMm ?? 0d,
                    data.LocalManualOffsetNormalAxisMm ?? 0d),
                data.TextX ?? actualBlockPosition.X,
                data.TextY ?? actualBlockPosition.Y,
                actualBlockPosition.X,
                actualBlockPosition.Y,
                rotation);
            var isCombinedFramedItem =
                TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
                    TimberAnnotationMode.DimensionsWithItemNumber &&
                data.ComponentRole ==
                    TimberMainAnnotationComponentRole.FramedItem;
            Vector3d? actualDoglegDirection = null;
            Point3d? actualLandingStartPoint = null;
            Point3d? actualLandingEndPoint = null;
            if (isCombinedFramedItem &&
                TryGetLandingSegment(
                    leader,
                    out var landingStartPoint,
                    out var landingEndPoint))
            {
                actualDoglegDirection =
                    (landingEndPoint - landingStartPoint).GetNormal();
                actualLandingStartPoint = landingStartPoint;
                actualLandingEndPoint = landingEndPoint;
            }
            ElementLabelStore.Write(leader, transaction, data with
            {
                TextX = actualBlockPosition.X,
                TextY = actualBlockPosition.Y,
                LocalManualOffsetAlongAxisMm = offset.AlongAxisMm,
                LocalManualOffsetNormalAxisMm = offset.NormalAxisMm,
                PlacementRotationRadians = rotation,
                CombinedDoglegDirectionX =
                    actualDoglegDirection?.X ??
                    data.CombinedDoglegDirectionX,
                CombinedDoglegDirectionY =
                    actualDoglegDirection?.Y ??
                    data.CombinedDoglegDirectionY,
            });
            // Assigning XData can force an MLeader reevaluation. Restore the
            // user-observed position after the metadata write so CommandEnded
            // cannot snap BlockContent back to its preceding evaluated point.
            leader.BlockPosition = actualBlockPosition;
            if (isCombinedFramedItem &&
                actualDoglegDirection.HasValue &&
                actualLandingStartPoint.HasValue &&
                actualLandingEndPoint.HasValue)
            {
                var leaderIndex = leader.GetLeaderIndexes().Cast<int>().Single();
                var leaderLineIndex = leader
                    .GetLeaderLineIndexes(leaderIndex)
                    .Cast<int>()
                    .Single();
                leader.DoglegLength =
                    actualLandingStartPoint.Value.DistanceTo(
                        actualLandingEndPoint.Value);
                leader.SetDogleg(leaderIndex, actualDoglegDirection.Value);
                leader.SetLastVertex(
                    leaderLineIndex,
                    actualLandingStartPoint.Value);
                leader.BlockPosition = actualBlockPosition;
                RecenterCombinedDimensionsText(
                    database,
                    transaction,
                    data.SourceHandle,
                    actualLandingStartPoint.Value,
                    actualLandingEndPoint.Value);
            }
        }
    }

    private static void RecenterCombinedDimensionsText(
        Database database,
        Transaction transaction,
        string sourceHandle,
        Point3d landingStartPoint,
        Point3d landingEndPoint)
    {
        var dimensionsEntry = ReadAnnotationEntities(database, transaction)
            .FirstOrDefault(entry =>
                entry.EntityType == MainAnnotationEntityType.MText &&
                entry.Data.ComponentRole == TimberMainAnnotationComponentRole.Primary &&
                TimberAnnotationModeRules.Normalize(entry.Data.AnnotationMode) ==
                    TimberAnnotationMode.DimensionsWithItemNumber &&
                string.Equals(
                    entry.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase));
        if (dimensionsEntry is null ||
            !AutoCadObjectIdAccess.TryGetObject<MText>(
                transaction,
                dimensionsEntry.Id,
                OpenMode.ForWrite,
                out var dimensionsText,
                database) ||
            dimensionsText is null)
        {
            return;
        }

        var dimensionEnvelopeWidthMm =
            dimensionsText.ActualWidth > PlacementToleranceMm
                ? dimensionsText.ActualWidth
                : dimensionsEntry.Data.EnvelopeWidthMm ??
                    TimberCombinedDimensionTypographyRules
                        .CalculateEnvelopeWidthMm(
                            dimensionsText.Contents,
                            dimensionsText.TextHeight /
                            TimberCombinedDimensionTypographyRules
                                .BaseDimensionTextHeightAtScale50Mm);
        var movedLocation = landingStartPoint +
            (landingEndPoint - landingStartPoint).GetNormal() *
                TimberCombinedDimensionTypographyRules
                    .CalculateTextCenterOffsetFromLandingStartMm(
                        landingStartPoint.DistanceTo(landingEndPoint),
                        dimensionEnvelopeWidthMm,
                        dimensionsText.TextHeight);
        dimensionsText.Location = movedLocation;
        ElementLabelStore.Write(dimensionsText, transaction, dimensionsEntry.Data with
        {
            TextX = movedLocation.X,
            TextY = movedLocation.Y,
        });
        dimensionsText.Location = movedLocation;
    }

    internal static bool TryGetLongitudinalInterval(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        out TimberSlopeAnnotationLongitudinalInterval interval)
    {
        interval = new TimberSlopeAnnotationLongitudinalInterval(0d, 0d);
        var sourceHandle = sourceEntity.Handle.ToString();
        var matchingLabel = ReadLabels(database, transaction)
            .FirstOrDefault(label =>
                TimberSlopeAnnotationRules.IsLongitudinalIntervalLabelRole(
                    label.Data.ComponentRole) &&
                TimberSlopeAnnotationRules.HasSameSourceHandle(
                    label.Data.SourceHandle,
                    sourceHandle));
        if (matchingLabel is null ||
            !AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                matchingLabel.Id,
                OpenMode.ForRead,
                out var annotation,
                database) ||
            annotation is not (MText or MLeader or BlockReference))
        {
            return false;
        }

        if (annotation is MLeader candidateLeader &&
            candidateLeader.ContentType == ContentType.NoneContent)
        {
            // G4 leader-only MLeaders have no text content; TextLocation throws
            // eNullPtr. They are also filtered by role above — keep this guard
            // for any mis-tagged legacy entity.
            return false;
        }

        var (labelLocation, labelWidth) = annotation switch
        {
            MText text when matchingLabel.Representation ==
                TimberMainAnnotationRepresentation.BlockLeader => (
                    text.Location,
                    matchingLabel.Data.EnvelopeWidthMm ?? text.ActualWidth),
            MText text => (text.Location, text.ActualWidth),
            MLeader leader when leader.ContentType == ContentType.BlockContent => (
                leader.BlockPosition,
                matchingLabel.Data.EnvelopeWidthMm ?? 0d),
            MLeader leader when leader.ContentType == ContentType.MTextContent &&
                leader.MText is not null => (
                    leader.TextLocation,
                    leader.MText.ActualWidth),
            BlockReference => (
                new Point3d(
                    matchingLabel.Data.TextX ?? 0d,
                    matchingLabel.Data.TextY ?? 0d,
                    0d),
                matchingLabel.Data.EnvelopeWidthMm ?? 0d),
            _ => (Point3d.Origin, 0d),
        };

        var (start, end) = sourceEntity switch
        {
            Line line => (line.StartPoint, line.EndPoint),
            Polyline polyline => (polyline.StartPoint, polyline.EndPoint),
            _ => (Point3d.Origin, Point3d.Origin),
        };
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var axisLength = Math.Sqrt(dx * dx + dy * dy);
        if (axisLength < 0.001d)
        {
            return false;
        }

        var axisX = dx / axisLength;
        var axisY = dy / axisLength;
        var centerDistance = (labelLocation.X - start.X) * axisX +
            (labelLocation.Y - start.Y) * axisY;
        var halfWidth = labelWidth / 2d;
        if (double.IsNaN(halfWidth) || double.IsInfinity(halfWidth) || halfWidth <= 0d)
        {
            return false;
        }

        interval = new TimberSlopeAnnotationLongitudinalInterval(
            centerDistance - halfWidth,
            centerDistance + halfWidth);
        return true;
    }

    internal static int DeleteLabelsForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        if (sourceHandles.Count == 0)
        {
            return 0;
        }

        var targetHandles = new HashSet<string>(
            sourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (targetHandles.Count == 0)
        {
            return 0;
        }

        var existingSourceHandles = ReadTimberSourceHandles(database, transaction);
        var deleted = 0;

        foreach (var label in ReadAnnotationEntities(database, transaction))
        {
            if (!targetHandles.Contains(label.Data.SourceHandle) ||
                existingSourceHandles.Contains(label.Data.SourceHandle) ||
                transaction.GetObject(label.Id, OpenMode.ForWrite, false) is not Entity annotation ||
                annotation.IsErased)
            {
                continue;
            }

            EraseMainAnnotation(transaction, annotation);
            deleted++;
        }

        return deleted;
    }

    internal static int DeleteForSourceHandle(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        var ids = ReadAnnotationEntities(database, transaction)
            .Where(label => string.Equals(
                label.Data.SourceHandle,
                sourceHandle,
                StringComparison.OrdinalIgnoreCase))
            .Select(label => label.Id)
            .ToList();
        return DeleteLabelsByKey(
            transaction,
            ids.ToDictionary(id => id.ToString(), id => id),
            ids.Select(id => id.ToString()).ToList());
    }

    internal static int DeleteDuplicateLabelsForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        var labels = ReadLabels(database, transaction);
        if (labels.Count == 0)
        {
            return 0;
        }

        var labelIdsByKey = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var candidates = labels
            .Select(ToOwnershipCandidate)
            .ToList();
        var timberElementIdBySourceHandle =
            ReadTimberElementIdsBySourceHandle(database, transaction);
        var keysToDelete = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
                candidates,
                ReadTimberSourceHandles(database, transaction))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectSupersededLegacyFramedLeaderKeys(
                    candidates))
            .Concat(
                TimberMainAnnotationOwnershipRules
                    .SelectLegacyCombinedPartsToDeleteWhenG5Present(candidates))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectExtraG4GroupKeysToDelete(
                    candidates))
            .ToList();
        foreach (var sourceGroup in candidates
                     .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourceHandle))
                     .GroupBy(
                         candidate => candidate.SourceHandle.Trim(),
                         StringComparer.OrdinalIgnoreCase))
        {
            timberElementIdBySourceHandle.TryGetValue(sourceGroup.Key, out var preferredElementId);
            var owned = sourceGroup.ToList();
            keysToDelete.AddRange(
                TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
                    owned,
                    preferredElementId));
            // Do not run stale-ElementId cleanup here: Combined upsert updates
            // G4 before Primary, so Primary briefly still has the previous id.
        }

        return DeleteLabelsByKey(
            transaction,
            labelIdsByKey,
            keysToDelete.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void DeleteOwnedAnnotationOwnershipViolations(
        Database database,
        Transaction transaction,
        string sourceHandle,
        string preferredElementId,
        bool deleteStaleElementIds = false)
    {
        var labels = ReadLabels(database, transaction)
            .Where(label =>
                TimberSlopeAnnotationRules.HasSameSourceHandle(
                    label.Data.SourceHandle,
                    sourceHandle))
            .ToList();
        if (labels.Count == 0)
        {
            return;
        }

        var labelIdsByKey = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var candidates = labels.Select(ToOwnershipCandidate).ToList();
        var keysToDelete = TimberMainAnnotationOwnershipRules
            .SelectSupersededLegacyFramedLeaderKeys(candidates)
            .Concat(
                TimberMainAnnotationOwnershipRules
                    .SelectLegacyCombinedPartsToDeleteWhenG5Present(candidates))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectExtraG4GroupKeysToDelete(
                    candidates,
                    preferredElementId))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
                    candidates,
                    preferredElementId))
            .ToList();
        if (deleteStaleElementIds)
        {
            keysToDelete.AddRange(
                TimberMainAnnotationOwnershipRules.SelectStaleElementIdKeysToDelete(
                    candidates,
                    preferredElementId));
        }

        DeleteLabelsByKey(
            transaction,
            labelIdsByKey,
            keysToDelete.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static TimberElementLabelCandidate ToOwnershipCandidate(
        MainAnnotationEntry label) =>
        new()
        {
            LabelKey = label.Id.ToString(),
            ElementId = label.Data.ElementId,
            SourceHandle = label.Data.SourceHandle,
            ComponentRole = label.Data.ComponentRole,
            AnnotationGroupId = label.Data.AnnotationGroupId,
            RendererGeneration = label.Data.RendererGeneration,
        };

    private static void EraseLegacyFramedLeaderBesideG4(
        Transaction transaction,
        ObjectId existingId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                existingId,
                OpenMode.ForWrite,
                out var entity) ||
            entity is null ||
            entity.IsErased ||
            !ElementLabelStore.TryRead(entity, out var data) ||
            data is null)
        {
            return;
        }

        if (AutoCadFramedG4CompositePolicy.IsG4CompositeRole(data.ComponentRole))
        {
            return;
        }

        if (data.ComponentRole == TimberMainAnnotationComponentRole.FramedItem ||
            (entity is MLeader &&
             AutoCadFramedG4CompositePolicy.IsLegacyG2G3BlockLeaderRole(
                 data.ComponentRole) &&
             AutoCadFramedG4CompositeService.IsLegacyG2G3BlockLeader(
                 transaction,
                 existingId)))
        {
            EraseMainAnnotation(transaction, entity);
        }
    }

    internal static int DeleteInsertedLabelsWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> labelIds)
    {
        if (labelIds.Count == 0)
        {
            return 0;
        }

        var labels = ReadLabels(database, transaction)
            .Where(label => labelIds.Contains(label.Id))
            .ToList();
        if (labels.Count == 0)
        {
            return 0;
        }

        var labelIdsByKey = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var keysToDelete = TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
            labels
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                    ComponentRole = label.Data.ComponentRole,
                })
                .ToList(),
            ReadTimberSourceHandles(database, transaction));

        return DeleteLabelsByKey(transaction, labelIdsByKey, keysToDelete);
    }

    /// <summary>
    /// AK_RECALC / targeted Hybrid recalc pipeline for an explicit id set.
    /// Reuses the current transaction. Numbering context may read the drawing.
    /// ElementId metadata is written only when the existing numbering service
    /// actually changes an assignment. Annotation writes cover
    /// <paramref name="ids"/> plus any other timber whose displayed number changed.
    /// </summary>
    internal static ElementLabelUpdateResult UpdateInCurrentTransaction(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids) =>
        UpdateInCurrentTransaction(
            database,
            transaction,
            editor,
            ids,
            numberingTargetIds: null);

    internal static ElementLabelUpdateResult UpdateInCurrentTransaction(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids,
        IReadOnlyList<ObjectId>? numberingTargetIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(ids);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        return UpdateInCurrentTransaction(
            database,
            transaction,
            editor,
            ids,
            metadataStore,
            numberingTargetIds);
    }

    private static ElementLabelUpdateResult UpdateInCurrentTransaction(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids,
        AutoCadTimberElementMetadataStore metadataStore) =>
        UpdateInCurrentTransaction(
            database,
            transaction,
            editor,
            ids,
            metadataStore,
            numberingTargetIds: null);

    private static ElementLabelUpdateResult UpdateInCurrentTransaction(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId>? numberingTargetIds)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            database,
            transaction,
            defaultProfile);
        var distinctIds = ids.Distinct().ToList();
        TimberElementCopyInitializationService.InitializeLocalCopies(
            database,
            transaction,
            metadataStore,
            distinctIds,
            defaultProfile);

        IReadOnlyDictionary<ObjectId, TimberElementData> synchronizedDataById;
        IReadOnlyDictionary<ObjectId, string> previousElementIdById;
        IReadOnlyList<TimberElementNumberingChange> numberingChanges;
        IReadOnlyList<ObjectId> refreshIds;

        var numberingTargets = numberingTargetIds ?? distinctIds;
        if (numberingTargets.Count > 0)
        {
            var sync = TimberElementItemIdentityService.SynchronizeElementIdsDetailed(
                database,
                transaction,
                metadataStore,
                numberingTargets,
                roundingStepMm);
            synchronizedDataById = sync.DataById;
            previousElementIdById = sync.PreviousElementIdById;
            numberingChanges = sync.NumberingChanges;
            refreshIds = distinctIds
                .Concat(sync.WrittenIds)
                .Distinct()
                .ToList();
        }
        else
        {
            previousElementIdById = ReadElementIds(transaction, metadataStore, distinctIds);
            synchronizedDataById = ReadTimberData(transaction, metadataStore, distinctIds);
            numberingChanges = Array.Empty<TimberElementNumberingChange>();
            refreshIds = distinctIds;
        }

        foreach (var id in refreshIds)
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !synchronizedDataById.TryGetValue(id, out var data))
                {
                    skipped++;
                    continue;
                }

                previousElementIdById.TryGetValue(id, out var previousElementId);
                if (TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        entity,
                        data,
                        presentationBatchContext,
                        previousElementId,
                        roundingStepMm))
                {
                    created++;
                }
                else
                {
                    updated++;
                }

            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.CommandLabelsRefreshFailedFormat,
                    id,
                    ex.Message));
            }
        }

        TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(database, transaction);
        return new ElementLabelUpdateResult(created, updated, skipped)
        {
            NumberingChanges = numberingChanges,
        };
    }

    private static IReadOnlyDictionary<ObjectId, TimberElementData> ReadTimberData(
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> ids)
    {
        var result = new Dictionary<ObjectId, TimberElementData>();

        foreach (var id in ids)
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity) &&
                entity is not null &&
                metadataStore.TryRead(entity, out var data) &&
                data is not null)
            {
                result[id] = data;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<ObjectId, string> ReadElementIds(
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> ids)
    {
        var result = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity) &&
                entity is not null &&
                metadataStore.TryRead(entity, out var data) &&
                data is not null)
            {
                result[id] = data.ElementId;
            }
        }

        return result;
    }

    private static ObjectId FindExistingLabelId(
        Database database,
        Transaction transaction,
        string elementId,
        string sourceHandle,
        string? previousElementId,
        TimberMainAnnotationRepresentation desiredRepresentation,
        TimberMainAnnotationComponentRole desiredComponentRole,
        bool preserveCompositeSiblings,
        bool allowElementIdFallback,
        out IReadOnlyList<ObjectId> obsoleteLabelIds)
    {
        obsoleteLabelIds = Array.Empty<ObjectId>();
        var labels = ReadLabels(database, transaction);

        var labelKeys = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var matchingRepresentation = labels
            .Where(label =>
                label.Representation == desiredRepresentation &&
                label.Data.ComponentRole == desiredComponentRole)
            .ToList();
        var selection = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle,
            elementId,
            previousElementId,
            matchingRepresentation
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                    ComponentRole = label.Data.ComponentRole,
                    AnnotationGroupId = label.Data.AnnotationGroupId,
                })
                .ToList(),
            CountTimberElementsWithElementId(database, transaction, elementId),
            CountTimberElementsWithElementId(database, transaction, previousElementId),
            allowElementIdFallback);

        obsoleteLabelIds = selection.LabelKeysToDelete
            .Where(labelKeys.ContainsKey)
            .Select(labelKey => labelKeys[labelKey])
            .Concat(labels
                .Where(label =>
                    (!preserveCompositeSiblings ||
                     label.Data.ComponentRole == desiredComponentRole) &&
                    (label.Representation != desiredRepresentation ||
                     label.Data.ComponentRole != desiredComponentRole) &&
                    string.Equals(label.Data.SourceHandle, sourceHandle, StringComparison.OrdinalIgnoreCase))
                .Select(label => label.Id))
            .Distinct()
            .ToList();

        return selection.LabelKeyToUpdate is not null && labelKeys.TryGetValue(selection.LabelKeyToUpdate, out var labelId)
            ? labelId
            : ObjectId.Null;
    }

    private static IReadOnlyList<MainAnnotationEntry> ReadLabels(
        Database database,
        Transaction transaction)
    {
        return ReadAnnotationEntities(database, transaction)
            .Where(entry =>
                entry.Data.ComponentRole is
                    TimberMainAnnotationComponentRole.Primary or
                    TimberMainAnnotationComponentRole.CircleText or
                    TimberMainAnnotationComponentRole.CircleLeaderLine or
                    TimberMainAnnotationComponentRole.CircleFrame or
                    TimberMainAnnotationComponentRole.FramedItem)
            .Select(entry => new MainAnnotationEntry(
                entry.Id,
                entry.Data,
                ResolveMainAnnotationRepresentation(entry)))
            .ToList();
    }

    /// <summary>
    /// Combined Plain item components use <see cref="TimberMainAnnotationComponentRole.FramedItem"/>
    /// with native Leader representation. Framed Circle/Slot/Rectangle keep BlockLeader.
    /// </summary>
    private static TimberMainAnnotationRepresentation ResolveMainAnnotationRepresentation(
        AnnotationEntityEntry entry)
    {
        if (entry.Data.ComponentRole is
                TimberMainAnnotationComponentRole.CircleText or
                TimberMainAnnotationComponentRole.CircleLeaderLine or
                TimberMainAnnotationComponentRole.CircleFrame)
        {
            return TimberMainAnnotationRepresentation.BlockLeader;
        }

        if (entry.Data.ComponentRole ==
            TimberMainAnnotationComponentRole.FramedItem)
        {
            return ItemNumberLeaderStyleRules.Normalize(
                       entry.Data.ItemNumberLeaderStyle) ==
                   ItemNumberLeaderStyle.Plain
                ? TimberMainAnnotationRepresentation.Leader
                : TimberMainAnnotationRepresentation.BlockLeader;
        }

        if (entry.EntityType == MainAnnotationEntityType.MLeader &&
            TimberAnnotationModeRules.GetRepresentation(
                entry.Data.AnnotationMode,
                entry.Data.ItemNumberLeaderStyle) ==
            TimberMainAnnotationRepresentation.BlockLeader)
        {
            return TimberMainAnnotationRepresentation.BlockLeader;
        }

        if (entry.EntityType is MainAnnotationEntityType.MLeader or
            MainAnnotationEntityType.BlockReference)
        {
            return TimberMainAnnotationRepresentation.Leader;
        }

        return TimberMainAnnotationRepresentation.FullLabel;
    }

    private static IReadOnlyList<AnnotationEntityEntry> ReadAnnotationEntities(
        Database database,
        Transaction transaction)
    {
        var entries = new List<AnnotationEntityEntry>();
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
                    out var annotation,
                    database) ||
                annotation is not (
                    MText or MLeader or BlockReference or Circle or Polyline or DBText) ||
                !ElementLabelStore.TryRead(annotation, out var data) ||
                data is null)
            {
                continue;
            }

            entries.Add(new AnnotationEntityEntry(
                id,
                data,
                annotation switch
                {
                    MLeader => MainAnnotationEntityType.MLeader,
                    BlockReference => MainAnnotationEntityType.BlockReference,
                    Circle => MainAnnotationEntityType.Circle,
                    Polyline => MainAnnotationEntityType.Polyline,
                    DBText => MainAnnotationEntityType.DBText,
                    _ => MainAnnotationEntityType.MText,
                }));
        }

        return entries;
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

    private static IReadOnlyDictionary<string, string> ReadTimberElementIdsBySourceHandle(
        Database database,
        Transaction transaction)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null ||
                string.IsNullOrWhiteSpace(data.ElementId))
            {
                continue;
            }

            result[entity.Handle.ToString()] = data.ElementId.Trim();
        }

        return result;
    }

    private static string? ReadExistingG4AnnotationGroupId(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (var entry in ReadAnnotationEntities(database, transaction))
        {
            if (entry.Data.RendererGeneration ==
                    AutoCadFramedG4CompositePolicy.RendererGeneration &&
                !string.IsNullOrWhiteSpace(entry.Data.AnnotationGroupId) &&
                string.Equals(
                    entry.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) &&
                AutoCadFramedG4CompositePolicy.IsG4CompositeRole(
                    entry.Data.ComponentRole))
            {
                return entry.Data.AnnotationGroupId;
            }
        }

        return null;
    }

    private static void DeleteObsoleteLabels(
        Transaction transaction,
        IReadOnlyList<ObjectId> obsoleteLabelIds,
        ObjectId selectedLabelId)
    {
        foreach (var id in obsoleteLabelIds.Distinct())
        {
            if (id == selectedLabelId ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                OpenMode.ForWrite,
                out var label) ||
                label is null ||
                label.IsErased ||
                !ElementLabelStore.TryRead(label, out _))
            {
                continue;
            }

            EraseMainAnnotation(transaction, label);
        }
    }

    private static int DeleteLabelsByKey(
        Transaction transaction,
        IReadOnlyDictionary<string, ObjectId> labelIdsByKey,
        IReadOnlyList<string> labelKeysToDelete)
    {
        var deleted = 0;

        foreach (var labelKey in labelKeysToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!labelIdsByKey.TryGetValue(labelKey, out var id) ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                OpenMode.ForWrite,
                out var label) ||
                label is null ||
                label.IsErased ||
                !ElementLabelStore.TryRead(label, out _))
            {
                continue;
            }

            EraseMainAnnotation(transaction, label);
            deleted++;
        }

        return deleted;
    }

    private static void EraseMainAnnotation(
        Transaction transaction,
        Entity annotation)
    {
        if (ElementLabelStore.TryRead(annotation, out var circleData) &&
            circleData is not null &&
            IsCircleComponent(circleData.ComponentRole))
        {
            var componentIds = ReadAnnotationEntities(annotation.Database, transaction)
                .Where(entry =>
                    IsCircleComponent(entry.Data.ComponentRole) &&
                    string.Equals(
                        entry.Data.SourceHandle,
                        circleData.SourceHandle,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Id)
                .Distinct()
                .ToList();
            foreach (var componentId in componentIds)
            {
                if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        componentId,
                        OpenMode.ForWrite,
                        out var component) &&
                    component is not null &&
                    !component.IsErased)
                {
                    component.Erase();
                }
            }

            return;
        }

        var generatedBlockDefinitionId = annotation is BlockReference blockReference
            ? blockReference.BlockTableRecord
            : ObjectId.Null;
        annotation.Erase();
        if (generatedBlockDefinitionId.IsNull ||
            transaction.GetObject(
                generatedBlockDefinitionId,
                OpenMode.ForWrite,
                false) is not BlockTableRecord definition ||
            !definition.IsAnonymous)
        {
            return;
        }

        var hasLiveReferences = definition
            .GetBlockReferenceIds(directOnly: true, forceValidity: false)
            .Cast<ObjectId>()
            .Any(id =>
                transaction.GetObject(id, OpenMode.ForRead, true) is BlockReference reference &&
                !reference.IsErased);
        if (!hasLiveReferences)
        {
            try
            {
                definition.Erase();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Niektoré DWG verzie držia anonymný block record referencovaný
                // až do commitu. Hlavná anotácia je už odstránená; nepoužitý
                // anonymný record môže AutoCAD bezpečne purge-nuť neskôr.
            }
        }
    }

    private static bool IsFramedCompositeComponent(
        TimberMainAnnotationComponentRole role) =>
        AutoCadFramedG4CompositePolicy.IsG4CompositeRole(role);

    private static bool IsCircleComponent(TimberMainAnnotationComponentRole role) =>
        IsFramedCompositeComponent(role);

    private static int CountTimberElementsWithElementId(
        Database database,
        Transaction transaction,
        string? elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return 0;
        }

        var count = 0;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(data.ElementId, elementId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static void ApplyLabelAppearance(
        Database database,
        Transaction transaction,
        MText label,
        LabelPlacement placement,
        string contents,
        AttachmentPoint attachment,
        double textHeightMm,
        double? lineSpacingFactor,
        bool updateExistingLayer,
        ObjectId? resolvedTextStyleId = null)
    {
        var labelLayerId = EnsureLabelLayer(database, transaction, updateExistingLayer);
        label.LayerId = labelLayerId;
        label.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        label.Attachment = attachment;
        label.TextHeight = textHeightMm;
        if (resolvedTextStyleId is ObjectId textStyleId)
        {
            label.TextStyleId = textStyleId;
        }
        if (lineSpacingFactor.HasValue)
        {
            label.LineSpacingFactor = lineSpacingFactor.Value;
        }
        label.Rotation = placement.RotationRadians;
        label.Location = placement.Location;
        label.Contents = contents;
    }

    private static ObjectId EnsureLabelLayer(
        Database database,
        Transaction transaction,
        bool updateExisting = true)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;

        if (layerTable.Has(LabelLayerName))
        {
            if (!updateExisting)
            {
                return layerTable[LabelLayerName];
            }

            layer = (LayerTableRecord)transaction.GetObject(layerTable[LabelLayerName], OpenMode.ForWrite);
        }
        else
        {
            layerTable.UpgradeOpen();
            layer = new LayerTableRecord { Name = LabelLayerName };
            layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }

        layer.Color = AcColor.FromColorIndex(ColorMethod.ByAci, LabelLayerColorIndex);
        return layer.ObjectId;
    }

    private static LabelPlacement CalculatePlacement(
        Entity sourceEntity,
        double fullLabelTextHeightMm)
    {
        var (start, end, midpoint) = sourceEntity switch
        {
            Line line => (
                line.StartPoint,
                line.EndPoint,
                new Point3d(
                    (line.StartPoint.X + line.EndPoint.X) / 2d,
                    (line.StartPoint.Y + line.EndPoint.Y) / 2d,
                    (line.StartPoint.Z + line.EndPoint.Z) / 2d)),
            Polyline polyline => (
                polyline.StartPoint,
                polyline.EndPoint,
                polyline.GetPointAtDist(polyline.Length / 2d)),
            _ => throw new NotSupportedException(UiStrings.ErrorLabelUnsupportedEntityType),
        };

        var placement =
            TimberElementLabelPlacementCalculator.Calculate(
            start.X,
            start.Y,
            end.X,
            end.Y,
            midpoint.X,
            midpoint.Y,
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(
                    fullLabelTextHeightMm));
        var location = new Point3d(placement.X, placement.Y, midpoint.Z);
        // Offset keeps calculator readable fold; text rotation matches Plain
        // ItemOnly directed readability (90° and 270° → BOTTOM→TOP).
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var planarLength = Math.Sqrt((dx * dx) + (dy * dy));
        var physicalAxis = planarLength < 0.001d ? 0d : Math.Atan2(dy, dx);
        var textRotation =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(physicalAxis);

        return new LabelPlacement(location, textRotation);
    }

    private static LeaderPlacement CalculateLeaderPlacement(Entity sourceEntity)
    {
        var (start, end, midpoint) = sourceEntity switch
        {
            Line line => (
                line.StartPoint,
                line.EndPoint,
                new Point3d(
                    (line.StartPoint.X + line.EndPoint.X) / 2d,
                    (line.StartPoint.Y + line.EndPoint.Y) / 2d,
                    (line.StartPoint.Z + line.EndPoint.Z) / 2d)),
            Polyline polyline => (
                polyline.StartPoint,
                polyline.EndPoint,
                polyline.GetPointAtDist(polyline.Length / 2d)),
            _ => throw new NotSupportedException(UiStrings.ErrorLabelUnsupportedEntityType),
        };
        var placement = TimberLeaderPlacementCalculator.CalculateLinear(
            start.X,
            start.Y,
            end.X,
            end.Y,
            midpoint.X,
            midpoint.Y);
        return new LeaderPlacement(
            new Point3d(placement.AnchorX, placement.AnchorY, midpoint.Z),
            new Point3d(placement.AnchorX, placement.AnchorY, midpoint.Z),
            new Point3d(placement.TextX, placement.TextY, midpoint.Z),
            placement.RotationRadians,
            Side: end.X < start.X ||
                (Math.Abs(end.X - start.X) <= PlacementToleranceMm && end.Y < start.Y)
                    ? TimberLeaderHorizontalSide.Left
                    : TimberLeaderHorizontalSide.Right);
    }

    private static LeaderPlacement CalculateShortLeaderPlacement(
        Entity sourceEntity,
        string contents,
        ItemNumberLeaderStyle? itemStyle,
        double presentationScaleFactor,
        TimberLeaderHorizontalSide? preferredSideOverride = null,
        bool standaloneNativeOrientation = false)
    {
        var basePlacement = CalculateLeaderPlacement(sourceEntity);
        // Standalone absolute OrientAroundAnchor must see physical Start→End —
        // never the already readability-normalized angle from CalculateLeaderPlacement.
        var rotationForPlacement = standaloneNativeOrientation &&
            TryGetSourceElementAxisRadians(sourceEntity, out var physicalAxis)
            ? physicalAxis
            : basePlacement.RotationRadians;
        return CalculateShortLeaderPlacement(
            new TimberLeaderPlacement(
                basePlacement.Anchor.X,
                basePlacement.Anchor.Y,
                basePlacement.TextLocation.X,
                basePlacement.TextLocation.Y,
                rotationForPlacement),
            basePlacement.Anchor.Z,
            contents,
            itemStyle,
            preferredSideOverride ?? basePlacement.Side,
            presentationScaleFactor,
            usePlainItemNumberPlacement: true,
            standaloneNativeOrientation: standaloneNativeOrientation);
    }

    private static LeaderPlacement CalculateShortLeaderPlacement(
        TimberLeaderPlacement basePlacement,
        double elevation,
        string contents,
        ItemNumberLeaderStyle? itemStyle,
        TimberLeaderHorizontalSide preferredSide = TimberLeaderHorizontalSide.Right,
        double presentationScaleFactor = 1d,
        bool usePlainItemNumberPlacement = false,
        bool standaloneNativeOrientation = false)
    {
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(
            itemStyle ?? ItemNumberLeaderStyle.Plain);
        if (itemStyle.HasValue && normalizedStyle != ItemNumberLeaderStyle.Plain)
        {
            var blockLayout = TimberItemLeaderLayoutCalculator.CalculateBlock(
                basePlacement,
                contents,
                normalizedStyle,
                preferredSide,
                presentationScaleFactor);
            return new LeaderPlacement(
                new Point3d(blockLayout.AnchorX, blockLayout.AnchorY, elevation),
                new Point3d(blockLayout.KneeX, blockLayout.KneeY, elevation),
                new Point3d(blockLayout.ContentX, blockLayout.ContentY, elevation),
                RotationRadians: basePlacement.RotationRadians,
                normalizedStyle,
                blockLayout.Side,
                blockLayout.EnvelopeWidthMm,
                blockLayout.EnvelopeHeightMm);
        }

        TimberItemLeaderLayout layout;
        if (standaloneNativeOrientation && !itemStyle.HasValue)
        {
            // Iba rozmery: WorldXY authoring, then absolute OrientAroundAnchor from
            // physical Start→End (same transform as CREATE MText.Rotation).
            layout = TimberItemLeaderLayoutCalculator
                .CalculateStandaloneNativeDimensionsLeader(
                    basePlacement,
                    contents,
                    preferredSide,
                    presentationScaleFactor);
            layout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
                layout,
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    basePlacement.RotationRadians));
        }
        else if (itemStyle.HasValue && usePlainItemNumberPlacement)
        {
            if (standaloneNativeOrientation)
            {
                // Iba položka Plain: WorldXY authoring, then absolute OrientAroundAnchor
                // from physical Start→End (same transform as CREATE MText.Rotation).
                // Exact 60° dogleg A from T, B ‖ ±T (interior elbow 120°) — same
                // bend as Dimensions; Plain typography clearance only — Combined
                // CalculatePlainItemNumber stays separate.
                layout = TimberItemLeaderLayoutCalculator
                    .CalculateStandaloneNativePlainItemNumber(
                        basePlacement,
                        contents,
                        preferredSide,
                        presentationScaleFactor);
                layout = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
                    layout,
                    TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                        basePlacement.RotationRadians));
            }
            else
            {
                layout = TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
                    basePlacement,
                    contents,
                    preferredSide,
                    presentationScaleFactor);
            }
        }
        else
        {
            layout = TimberItemLeaderLayoutCalculator.Calculate(
                basePlacement,
                contents,
                normalizedStyle,
                preferredSide,
                presentationScaleFactor);
        }

        // Combined Plain / legacy keep RotationRadians=0 (world layout as-authored).
        // Standalone Plain + DimensionsLeader store physical axis while vertices are
        // already absolute-oriented above (never host TransformBy). DoglegDirection
        // is Knee→Content (±T) so CREATE/rebuild can SetDogleg with DoglegLength>0.
        Vector3d? standaloneDoglegDirection = null;
        if (standaloneNativeOrientation)
        {
            var landing =
                TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                    layout.KneeX,
                    layout.KneeY,
                    layout.ContentX,
                    layout.ContentY);
            standaloneDoglegDirection = new Vector3d(landing.DirX, landing.DirY, 0d);
        }

        return new LeaderPlacement(
            new Point3d(layout.AnchorX, layout.AnchorY, elevation),
            new Point3d(layout.KneeX, layout.KneeY, elevation),
            new Point3d(layout.ContentX, layout.ContentY, elevation),
            RotationRadians: standaloneNativeOrientation
                ? basePlacement.RotationRadians
                : 0d,
            itemStyle.HasValue ? normalizedStyle : null,
            layout.Side,
            layout.EnvelopeWidthMm,
            layout.EnvelopeHeightMm,
            DoglegDirection: standaloneDoglegDirection);
    }

    private static LeaderPlacement ApplyCombinedLandingDistance(
        LeaderPlacement framedPlacement,
        double presentationScaleFactor)
    {
        // Landing length and DoglegLength share CombinedFramedLandingDistanceMm *
        // presentationScaleFactor. TextLocation adds envelope half-width once beyond
        // that landing end; the landing distance itself is never applied twice.
        // CalculateBlock leaves Content==Knee, so direction must come from the
        // element-aligned plane (±local H) — never world ±XAxis.
        var contentDelta = framedPlacement.TextLocation - framedPlacement.Knee;
        var landing = TimberItemLeaderLayoutCalculator.ResolveCombinedLandingDirection(
            framedPlacement.RotationRadians,
            framedPlacement.Side,
            contentDelta.X,
            contentDelta.Y);
        var normalizedDirection = new Vector3d(landing.X, landing.Y, 0d);
        var combinedLandingDistanceMm =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            presentationScaleFactor;
        var contentDistance =
            framedPlacement.EnvelopeWidthMm / 2d +
            combinedLandingDistanceMm;
        return framedPlacement with
        {
            TextLocation = framedPlacement.Knee +
                normalizedDirection * contentDistance,
            DoglegDirection = normalizedDirection,
        };
    }

    private static LabelPlacement CalculateCombinedDimensionsTextPlacement(
        Database database,
        Transaction transaction,
        string sourceHandle,
        LeaderPlacement fallbackPlacement,
        double dimensionEnvelopeWidthMm,
        double dimensionTextHeightMm,
        double presentationScaleFactor)
    {
        var framedEntry = ReadAnnotationEntities(database, transaction)
            .FirstOrDefault(entry =>
                entry.EntityType == MainAnnotationEntityType.MLeader &&
                entry.Data.ComponentRole == TimberMainAnnotationComponentRole.FramedItem &&
                TimberAnnotationModeRules.Normalize(entry.Data.AnnotationMode) ==
                    TimberAnnotationMode.DimensionsWithItemNumber &&
                string.Equals(
                    entry.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase));
        if (framedEntry is not null &&
            AutoCadObjectIdAccess.TryGetObject<MLeader>(
                transaction,
                framedEntry.Id,
                OpenMode.ForRead,
                out var framedLeader,
                database) &&
            framedLeader is not null &&
            TryGetLandingSegment(
                framedLeader,
                out var landingStartPoint,
                out var landingEndPoint))
        {
            var landingDirection =
                (landingEndPoint - landingStartPoint).GetNormal();
            var landingRotation = Math.Atan2(landingDirection.Y, landingDirection.X);
            return new LabelPlacement(
                landingStartPoint +
                    landingDirection *
                        TimberCombinedDimensionTypographyRules
                            .CalculateTextCenterOffsetFromLandingStartMm(
                                landingStartPoint.DistanceTo(landingEndPoint),
                                dimensionEnvelopeWidthMm,
                                dimensionTextHeightMm),
                TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                    landingRotation));
        }

        var fallbackDirection = fallbackPlacement.DoglegDirection ??
            (fallbackPlacement.TextLocation - fallbackPlacement.Knee).GetNormal();
        var fallbackEnd = fallbackPlacement.Knee +
            fallbackDirection *
                TimberItemLeaderLayoutCalculator
                    .CombinedFramedLandingDistanceMm *
                presentationScaleFactor;
        return new LabelPlacement(
            fallbackPlacement.Knee +
                fallbackDirection *
                    TimberCombinedDimensionTypographyRules
                        .CalculateTextCenterOffsetFromLandingStartMm(
                            fallbackPlacement.Knee.DistanceTo(fallbackEnd),
                            dimensionEnvelopeWidthMm,
                            dimensionTextHeightMm),
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                fallbackPlacement.RotationRadians));
    }

    private static bool TryGetLandingSegment(
        MLeader leader,
        out Point3d landingStartPoint,
        out Point3d landingEndPoint)
    {
        landingStartPoint = Point3d.Origin;
        landingEndPoint = Point3d.Origin;
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length != 1)
        {
            return false;
        }

        var leaderLineIndexes = leader
            .GetLeaderLineIndexes(leaderIndexes[0])
            .Cast<int>()
            .ToArray();
        if (leaderLineIndexes.Length != 1 ||
            leader.VerticesCount(leaderLineIndexes[0]) != 2)
        {
            return false;
        }

        var doglegDirection = leader.GetDogleg(leaderIndexes[0]);
        if (doglegDirection.Length <= PlacementToleranceMm)
        {
            return false;
        }

        landingStartPoint = leader.GetLastVertex(leaderLineIndexes[0]);
        landingEndPoint = landingStartPoint +
            doglegDirection.GetNormal() * leader.DoglegLength;
        return true;
    }

    private static bool TryCreateDoglegDirection(
        double? directionX,
        double? directionY,
        out Vector3d direction)
    {
        direction = new Vector3d(0d, 0d, 0d);
        if (!directionX.HasValue || !directionY.HasValue)
        {
            return false;
        }

        var candidate = new Vector3d(
            directionX.Value,
            directionY.Value,
            0d);
        if (candidate.Length <= PlacementToleranceMm)
        {
            return false;
        }

        direction = candidate.GetNormal();
        return true;
    }

    private static MLeader CreateNativeMLeader(
        Database database,
        Transaction transaction,
        LeaderPlacement placement,
        string contents,
        bool updateExistingDefinitions,
        TimberAnnotationScaleContext annotationScaleContext,
        bool scaleNativePresentation,
        double baseTextHeightMm,
        AutoCadPlainItemLeaderPresentationPreparation? plainItemPresentation = null,
        double? combinedLandingDistanceMm = null,
        AutoCadDimensionsLeaderPresentationPreparation? dimensionsLeaderPresentation =
            null)
    {
        var presentationScaleFactor = scaleNativePresentation
            ? annotationScaleContext.ScaleFactor
            : 1d;
        var effectiveTextHeight = dimensionsLeaderPresentation is not null
            ? dimensionsLeaderPresentation.ModelHeightMm
            : plainItemPresentation is not null
                ? plainItemPresentation.ModelHeightMm
                : baseTextHeightMm * presentationScaleFactor;
        var resolvedTextStyleId =
            dimensionsLeaderPresentation?.TextStyleId ??
            plainItemPresentation?.TextStyleId;
        var styleId = AcKrovyMLeaderStyleService.Ensure(
            database,
            transaction,
            updateExistingDefinitions);
        var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
            database,
            transaction);
        var isStandalonePlainAbsolute =
            plainItemPresentation is not null &&
            combinedLandingDistanceMm is null;
        var isStandaloneNativeMText =
            combinedLandingDistanceMm is null &&
            (dimensionsLeaderPresentation is not null || isStandalonePlainAbsolute);
        var absoluteTextRotation =
            dimensionsLeaderPresentation is not null || isStandalonePlainAbsolute
                ? TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(placement.RotationRadians)
                : (double?)null;
        var mText = CreateLeaderMText(
            database,
            placement.TextLocation,
            contents,
            effectiveTextHeight,
            resolvedTextStyleId,
            absoluteTextRotation);
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.MLeaderStyle = styleId;
        leader.ContentType = ContentType.MTextContent;
        leader.MText = mText;

        var leaderIndex = leader.AddLeader();
        var leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(leaderLineIndex, placement.Anchor);
        leader.AddLastVertex(leaderLineIndex, placement.Knee);
        leader.TextLocation = placement.TextLocation;
        double? doglegLengthOverride = combinedLandingDistanceMm;
        Vector3d? doglegDirectionOverride = null;
        if (combinedLandingDistanceMm is not null)
        {
            doglegDirectionOverride = placement.DoglegDirection;
        }
        // Standalone Plain / DimensionsOnly: NEVER SetDogleg inside
        // ApplyInstanceProperties before AppendEntity. Host throws eInvalidInput
        // for DimensionsOnly on that early call even when the direction vector is
        // finite/non-zero (timing/topology). Dogleg is applied only after the
        // MLeader is database-resident via ApplyStandaloneNativeMTextLanding.
        else if (isStandaloneNativeMText)
        {
            doglegLengthOverride = null;
            doglegDirectionOverride = null;
        }

        AcKrovyMLeaderStyleService.ApplyInstanceProperties(
            leader,
            database,
            styleId,
            noneArrowId,
            leaderIndex,
            leaderLineIndex,
            placement.Side,
            effectiveTextHeight,
            presentationScaleFactor,
            resolvedTextStyleId,
            doglegLengthOverride: doglegLengthOverride,
            doglegDirectionOverride: doglegDirectionOverride);

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        leader.LayerId = EnsureLabelLayer(database, transaction, updateExistingDefinitions);
        leader.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        leader.LinetypeId = database.ByLayerLinetype;
        leader.LinetypeScale = 1d;
        leader.LineWeight = LineWeight.ByLayer;
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        if (dimensionsLeaderPresentation is not null)
        {
            // Dimensions: MText orientation first, then A/B landing LAST.
            // Assigning MText after SetDogleg lets AutoCAD distort the dogleg;
            // Combined avoids this via BlockContent + TransformBy + finalize.
            ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(
                leader,
                placement.RotationRadians);
            ApplyStandaloneNativeMTextLanding(leader, placement);
        }
        else if (isStandalonePlainAbsolute)
        {
            // Plain ItemOnly: same order — text orientation, then landing LAST.
            ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(
                leader,
                placement.RotationRadians);
            ApplyStandaloneNativeMTextLanding(leader, placement);
        }
        else
        {
            SynchronizeNativeLeaderGeometry(
                leader,
                leaderLineIndex,
                placement.Anchor,
                placement.Knee);
        }
        return leader;
    }

    private static AutoCadFramedItemLeaderPreparation?
        PrepareFramedItemLeaderVariant(
            Database database,
            Transaction transaction,
            TimberElementData data,
            string contents,
            TimberAnnotationScaleContext annotationScaleContext,
            AutoCadAnnotationPresentationContext? presentationContext,
            AutoCadItemLeaderBlockVariantBatchCatalog? variantBatchCatalog,
            out AutoCadItemLeaderBlockVariantResult result)
    {
        if (presentationContext is null || variantBatchCatalog is null)
        {
            result = AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                null,
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "The production framed renderer requires a presentation " +
                "context and a database-bound batch catalog.");
            return null;
        }
        if (presentationContext.AnnotationScaleDenominator !=
            annotationScaleContext.Denominator)
        {
            result = AutoCadItemLeaderBlockVariantResult.InvalidRequest(
                null,
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Presentation and renderer scale contexts do not match.");
            return null;
        }
        var itemCodeText = presentationContext.FramedItemCodeText;
        if (itemCodeText.ResolvedTextStyleId is not ObjectId textStyleId ||
            itemCodeText.ResolvedTextStyleName is null)
        {
            result = AutoCadItemLeaderBlockVariantResult.NoCompatibleTextStyle(
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "The Stage 2 resolution did not supply a compatible text style.");
            return null;
        }
        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            result = AutoCadItemLeaderBlockVariantResult.DatabaseMismatch(
                null,
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Resolved text-style ObjectId belongs to a different database.");
            return null;
        }

        result = AcKrovyItemLeaderBlockVariantService.Ensure(
            database,
            transaction,
            presentationContext,
            data.ItemNumberLeaderStyle,
            contents,
            variantBatchCatalog);
        if (!result.Succeeded ||
            result.BlockTableRecordId is not ObjectId blockId ||
            result.VariantKey is null)
        {
            return null;
        }

        var definition = transaction.GetObject(
            blockId,
            OpenMode.ForRead,
            false) as BlockTableRecord;
        var itemNumberDefinitions = definition is null
            ? []
            : definition
                .Cast<ObjectId>()
                .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
                .OfType<AttributeDefinition>()
                .Where(attribute => string.Equals(
                    attribute.Tag,
                    TimberItemLeaderBlockDefinitionRules.AttributeTag,
                    StringComparison.OrdinalIgnoreCase))
                .Select(attribute => attribute.ObjectId)
                .ToArray();
        if (itemNumberDefinitions.Length != 1)
        {
            result = AutoCadItemLeaderBlockVariantResult.ExistingDefinitionInvalid(
                result.VariantKey,
                result.CanonicalBlockName ??
                    AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(
                        result.VariantKey),
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "The ensured variant does not expose exactly one ITEM_NO " +
                "attribute definition.");
            return null;
        }

        var blockScale =
            AutoCadFramedItemLeaderRendererPolicy.CalculateBlockScale(
                annotationScaleContext);
        // AttributeReference height is paper × default 1:50 denominator.
        // BlockScale applies the per-element annotation ScaleFactor once.
        var attributeHeightMm =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                itemCodeText.PaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator);
        return new AutoCadFramedItemLeaderPreparation(
            result,
            itemNumberDefinitions[0],
            blockScale,
            itemCodeText.ModelHeightMm,
            textStyleId,
            attributeHeightMm);
    }

    private static bool TryUpdateBlockLeader(
        Database database,
        Transaction transaction,
        MLeader leader,
        LeaderPlacement placement,
        string contents,
        bool updateExistingDefinitions,
        bool combinedFramed,
        AutoCadFramedItemLeaderPreparation preparation)
    {
        if (leader.ContentType != ContentType.BlockContent)
        {
            return false;
        }

        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length != 1)
        {
            return false;
        }
        var lineIndexes = leader
            .GetLeaderLineIndexes(leaderIndexes[0])
            .Cast<int>()
            .ToArray();
        if (lineIndexes.Length != 1 || leader.VerticesCount(lineIndexes[0]) < 2)
        {
            return false;
        }

        var styleId = combinedFramed
            ? AcKrovyMLeaderStyleService.EnsureCombinedFramed(
                database,
                transaction,
                updateExistingDefinitions)
            : AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExistingDefinitions);
        if (leader.MLeaderStyle != styleId)
        {
            leader.MLeaderStyle = styleId;
        }

        var contentMatches =
            leader.BlockContentId == preparation.BlockTableRecordId;
        var scaleMatches =
            ScaleMatches(leader.BlockScale, preparation.BlockScale);
        var tokenMatches = contentMatches && ItemNumberTokenMatches(
            leader,
            preparation.AttributeDefinitionId,
            contents);
        var presentationMatches = contentMatches &&
            ItemNumberAttributePresentationMatches(
                leader,
                preparation);
        var mutationPlan = AutoCadFramedItemLeaderMutationPolicy.Create(
            variantEnsureSucceeded: true,
            hasExistingAnnotation: true,
            contentMatches,
            scaleMatches,
            tokenMatches,
            presentationMatches);
        if (mutationPlan.ShouldReplaceBlockContent)
        {
            leader.BlockContentId = preparation.BlockTableRecordId;
        }
        if (mutationPlan.ShouldSetBlockScale)
        {
            leader.BlockScale = new Scale3d(preparation.BlockScale);
        }
        leader.BlockConnectionType = combinedFramed
            ? BlockConnectionType.ConnectExtents
            : BlockConnectionType.ConnectBase;
        leader.BlockRotation = 0d;
        leader.BlockPosition = placement.TextLocation;
        leader.SetFirstVertex(lineIndexes[0], placement.Anchor);
        leader.SetLastVertex(lineIndexes[0], placement.Knee);

        if (combinedFramed)
        {
            AcKrovyMLeaderStyleService.ApplyCombinedBlockInstanceProperties(
                leader,
                database,
                leaderIndexes[0],
                lineIndexes[0],
                placement.Side,
                preparation.BlockScale,
                placement.DoglegDirection);
        }
        else
        {
            var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
                database,
                transaction);
            AcKrovyMLeaderStyleService.ApplyBlockInstanceProperties(
                leader,
                database,
                noneArrowId,
                leaderIndexes[0],
                lineIndexes[0],
                placement.Side,
                preparation.BlockScale);
        }

        if (mutationPlan.ShouldSetItemNumberToken)
        {
            SetItemNumberBlockAttribute(
                transaction,
                leader,
                preparation,
                contents);
        }
        return true;
    }

    private static bool ScaleMatches(Scale3d scale, double expected) =>
        Math.Abs(scale.X - expected) <= PlacementToleranceMm &&
        Math.Abs(scale.Y - expected) <= PlacementToleranceMm &&
        Math.Abs(scale.Z - expected) <= PlacementToleranceMm;

    private static bool ItemNumberTokenMatches(
        MLeader leader,
        ObjectId attributeDefinitionId,
        string contents)
    {
        try
        {
            using var attribute =
                leader.GetBlockAttribute(attributeDefinitionId);
            return string.Equals(
                attribute.TextString,
                contents,
                StringComparison.Ordinal);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool ItemNumberAttributePresentationMatches(
        MLeader leader,
        AutoCadFramedItemLeaderPreparation preparation)
    {
        try
        {
            using var attribute =
                leader.GetBlockAttribute(preparation.AttributeDefinitionId);
            return attribute.TextStyleId == preparation.TextStyleId &&
                Math.Abs(attribute.Height - preparation.AttributeHeightMm) <=
                    PlacementToleranceMm;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static void SetItemNumberBlockAttribute(
        Transaction transaction,
        MLeader leader,
        AutoCadFramedItemLeaderPreparation preparation,
        string contents)
    {
        // G3 AttributeDefinition owns TextStyleId. The per-instance attribute
        // carries only the token and supported height override.
        var attributeDefinition = (AttributeDefinition)transaction.GetObject(
            preparation.AttributeDefinitionId,
            OpenMode.ForRead);
        using var attribute = new AttributeReference();
        attribute.SetAttributeFromBlock(
            attributeDefinition,
            Matrix3d.Identity);
        attribute.TextString = contents;
        attribute.Height = preparation.AttributeHeightMm;
        leader.SetBlockAttribute(preparation.AttributeDefinitionId, attribute);
    }

    private static MLeader CreateBlockMLeader(
        Database database,
        Transaction transaction,
        LeaderPlacement placement,
        string contents,
        bool updateExistingDefinitions,
        bool combinedFramed,
        AutoCadFramedItemLeaderPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var presentationScaleFactor = preparation.BlockScale;
        var styleId = combinedFramed
            ? AcKrovyMLeaderStyleService.EnsureCombinedFramed(
                database,
                transaction,
                updateExistingDefinitions)
            : AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExistingDefinitions);
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.MLeaderStyle = styleId;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = preparation.BlockTableRecordId;
        leader.BlockConnectionType = combinedFramed
            ? BlockConnectionType.ConnectExtents
            : BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(presentationScaleFactor);
        leader.BlockRotation = 0d;
        leader.BlockPosition = placement.TextLocation;

        var leaderIndex = leader.AddLeader();
        var leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(leaderLineIndex, placement.Anchor);
        leader.AddLastVertex(leaderLineIndex, placement.Knee);
        if (combinedFramed)
        {
            AcKrovyMLeaderStyleService.ApplyCombinedBlockInstanceProperties(
                leader,
                database,
                leaderIndex,
                leaderLineIndex,
                placement.Side,
                presentationScaleFactor,
                placement.DoglegDirection);
        }
        else
        {
            var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
                database,
                transaction);
            AcKrovyMLeaderStyleService.ApplyBlockInstanceProperties(
                leader,
                database,
                noneArrowId,
                leaderIndex,
                leaderLineIndex,
                placement.Side,
                presentationScaleFactor);
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        leader.LayerId = EnsureLabelLayer(database, transaction, updateExistingDefinitions);
        leader.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        leader.LinetypeId = database.ByLayerLinetype;
        leader.LinetypeScale = 1d;
        leader.LineWeight = LineWeight.ByLayer;
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
        leader.SetLastVertex(leaderLineIndex, placement.Knee);
        // Apply the token/height last after host instance initialization.
        SetItemNumberBlockAttribute(
            transaction,
            leader,
            preparation,
            contents);
        return leader;
    }

    private static MText CreateLeaderMText(
        Database database,
        Point3d location,
        string contents,
        double textHeightMm,
        ObjectId? resolvedTextStyleId = null,
        double? rotationRadians = null)
    {
        var text = new MText();
        text.SetDatabaseDefaults(database);
        text.Contents = contents;
        text.Location = location;
        text.Attachment = AttachmentPoint.MiddleCenter;
        text.TextHeight = textHeightMm;
        text.TextStyleId = resolvedTextStyleId ?? database.Textstyle;
        text.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        text.LineWeight = LineWeight.ByLayer;
        if (rotationRadians is double absoluteRotation)
        {
            text.Rotation =
                TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    absoluteRotation);
        }

        return text;
    }

    private static bool TryUpdateNativeLeader(
        Database database,
        Transaction transaction,
        MLeader leader,
        LeaderPlacement placement,
        string contents,
        bool updateExistingDefinitions,
        TimberAnnotationScaleContext annotationScaleContext,
        bool scaleNativePresentation,
        double baseTextHeightMm,
        AutoCadPlainItemLeaderPresentationPreparation? plainItemPresentation = null,
        double? combinedLandingDistanceMm = null,
        AutoCadDimensionsLeaderPresentationPreparation? dimensionsLeaderPresentation =
            null,
        TimberStandaloneNativeLeaderSourceSyncDecision standaloneSourceSync =
            default)
    {
        if (leader.ContentType != ContentType.MTextContent)
        {
            return false;
        }

        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length != 1)
        {
            return false;
        }

        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        if (lineIndexes.Length != 1)
        {
            return false;
        }

        var styleId = AcKrovyMLeaderStyleService.Ensure(
            database,
            transaction,
            updateExistingDefinitions);
        var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
            database,
            transaction);
        if (leader.VerticesCount(lineIndexes[0]) < 2)
        {
            return false;
        }

        var presentationScaleFactor = scaleNativePresentation
            ? annotationScaleContext.ScaleFactor
            : 1d;
        var effectiveTextHeight = dimensionsLeaderPresentation is not null
            ? dimensionsLeaderPresentation.ModelHeightMm
            : plainItemPresentation is not null
                ? plainItemPresentation.ModelHeightMm
                : baseTextHeightMm * presentationScaleFactor;
        var resolvedTextStyleId =
            dimensionsLeaderPresentation?.TextStyleId ??
            plainItemPresentation?.TextStyleId;
        leader.MLeaderStyle = styleId;

        // Standalone Plain / Dimensions: content-only when Automatic*/axis
        // unchanged (AK_LABELS / annotation grip). Source MOVE/STRETCH/ROTATE
        // rewrites absolute CREATE canonical (OrientAroundAnchor placement).
        // Combined Plain (combinedLandingDistanceMm != null) keeps full sync.
        if (combinedLandingDistanceMm is null)
        {
            var isStandalonePlainAbsolute = plainItemPresentation is not null;
            if (standaloneSourceSync.RequiresCanonicalRebuild)
            {
                if (dimensionsLeaderPresentation is not null)
                {
                    ApplyStandaloneDimensionsCanonicalRebuild(
                        database,
                        leader,
                        lineIndexes[0],
                        placement,
                        contents,
                        effectiveTextHeight,
                        resolvedTextStyleId);
                }
                else if (isStandalonePlainAbsolute)
                {
                    ApplyStandalonePlainItemCanonicalRebuild(
                        database,
                        leader,
                        lineIndexes[0],
                        placement,
                        contents,
                        effectiveTextHeight,
                        resolvedTextStyleId);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                var liveTextLocation = leader.TextLocation;
                var absoluteTextRotation =
                    dimensionsLeaderPresentation is not null || isStandalonePlainAbsolute
                        ? TimberStandaloneNativeLeaderOrientationRules
                            .ResolveTextPresentationRadians(placement.RotationRadians)
                        : (double?)null;
                leader.MText = CreateLeaderMText(
                    database,
                    liveTextLocation,
                    contents,
                    effectiveTextHeight,
                    resolvedTextStyleId,
                    absoluteTextRotation);
                leader.TextLocation = liveTextLocation;

                if (dimensionsLeaderPresentation is not null)
                {
                    // AK_LABELS / unchanged source: keep live placement, reassert
                    // absolute text orientation (never fall back to horizontal 0).
                    ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(
                        leader,
                        placement.RotationRadians);
                }
                else if (isStandalonePlainAbsolute)
                {
                    ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(
                        leader,
                        placement.RotationRadians);
                }
            }

            return true;
        }

        leader.MText = CreateLeaderMText(
            database,
            placement.TextLocation,
            contents,
            effectiveTextHeight,
            resolvedTextStyleId);
        leader.SetFirstVertex(lineIndexes[0], placement.Anchor);
        leader.SetLastVertex(lineIndexes[0], placement.Knee);
        leader.TextLocation = placement.TextLocation;
        AcKrovyMLeaderStyleService.ApplyInstanceProperties(
            leader,
            database,
            styleId,
            noneArrowId,
            leaderIndexes[0],
            lineIndexes[0],
            placement.Side,
            effectiveTextHeight,
            presentationScaleFactor,
            resolvedTextStyleId,
            doglegLengthOverride: combinedLandingDistanceMm,
            doglegDirectionOverride: placement.DoglegDirection);
        SynchronizeNativeLeaderGeometry(
            leader,
            lineIndexes[0],
            placement.Anchor,
            placement.Knee);
        return true;
    }

    /// <summary>
    /// Standalone DimensionsLeader only: rewrite absolute CREATE canonical
    /// geometry (already OrientAroundAnchor'd in <paramref name="placement"/>)
    /// after source MOVE/STRETCH/ROTATE. Never TransformBy.
    /// </summary>
    private static void ApplyStandaloneDimensionsCanonicalRebuild(
        Database database,
        MLeader leader,
        int leaderLineIndex,
        LeaderPlacement placement,
        string contents,
        double textHeightMm,
        ObjectId? resolvedTextStyleId)
    {
        var absoluteTextRotation =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(placement.RotationRadians);
        leader.MText = CreateLeaderMText(
            database,
            placement.TextLocation,
            contents,
            textHeightMm,
            resolvedTextStyleId,
            absoluteTextRotation);
        _ = leaderLineIndex;
        // Landing LAST — MText assignment can distort dogleg/vertices.
        ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(
            leader,
            placement.RotationRadians);
        ApplyStandaloneNativeMTextLanding(leader, placement);
    }

    /// <summary>
    /// Standalone Plain ItemOnly only: rewrite absolute CREATE canonical
    /// geometry (already OrientAroundAnchor'd in <paramref name="placement"/>)
    /// after source MOVE/STRETCH/ROTATE. Never TransformBy.
    /// </summary>
    private static void ApplyStandalonePlainItemCanonicalRebuild(
        Database database,
        MLeader leader,
        int leaderLineIndex,
        LeaderPlacement placement,
        string contents,
        double textHeightMm,
        ObjectId? resolvedTextStyleId)
    {
        var absoluteTextRotation =
            TimberStandaloneNativeLeaderOrientationRules
                .ResolveTextPresentationRadians(placement.RotationRadians);
        leader.MText = CreateLeaderMText(
            database,
            placement.TextLocation,
            contents,
            textHeightMm,
            resolvedTextStyleId,
            absoluteTextRotation);
        _ = leaderLineIndex;
        // Landing LAST — MText assignment can distort dogleg/vertices.
        ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(
            leader,
            placement.RotationRadians);
        ApplyStandaloneNativeMTextLanding(leader, placement);
    }

    /// <summary>
    /// Standalone Plain / DimensionsLeader only: CREATE/rebuild finalization that
    /// faithfully maps Core 60°/120° onto the live MLeader after host MText
    /// settlement. Segment A = First→Last at 60° from transform T; segment B =
    /// dogleg ‖ ±T with DoglegLength = |Knee→Content|. Style LandingDistance
    /// stays 0. Must run AFTER absolute MText orientation (assigning MText after
    /// SetDogleg is the first host distortion vs Combined). Combined paths must
    /// not call this.
    /// </summary>
    private static void ApplyStandaloneNativeMTextLanding(
        MLeader leader,
        LeaderPlacement placement)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length != 1)
        {
            return;
        }

        var leaderIndex = leaderIndexes[0];
        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndex).Cast<int>().ToArray();
        if (lineIndexes.Length != 1 ||
            leader.VerticesCount(lineIndexes[0]) < 2)
        {
            return;
        }

        if (!TryResolveStandaloneNativeLanding(
                placement,
                out var doglegLength,
                out _))
        {
            return;
        }

        var transform =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                placement.RotationRadians);
        var attachment = new TimberPlanarPoint(
            placement.Anchor.X,
            placement.Anchor.Y);
        var actualKnee = new TimberPlanarPoint(
            placement.Knee.X,
            placement.Knee.Y);
        if (!TimberStandaloneNativeLeaderCreateFinalizationRules
                .TryResolveCreateFinalization(
                    attachment,
                    actualKnee,
                    transform,
                    placement.Side,
                    doglegLength,
                    out var correctedKnee,
                    out var landingEnd,
                    out _))
        {
            return;
        }

        var leaderLineIndex = lineIndexes[0];
        var knee = new Point3d(
            correctedKnee.X,
            correctedKnee.Y,
            placement.Knee.Z);
        var landing = new Point3d(
            landingEnd.X,
            landingEnd.Y,
            placement.TextLocation.Z);
        var rawDirection = landing - knee;
        var canSetDogleg =
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                doglegLength,
                rawDirection.X,
                rawDirection.Y,
                out var unitX,
                out var unitY);

        // Combined-style reassert: vertices → dogleg → TextLocation → vertices.
        // Never call SetDogleg with a near-zero / non-finite vector (eInvalidInput).
        leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
        leader.SetLastVertex(leaderLineIndex, knee);
        leader.EnableLanding = true;
        leader.ExtendLeaderToText = false;
        leader.LandingGap = 0d;
        if (canSetDogleg)
        {
            var doglegDirection = new Vector3d(unitX, unitY, 0d);
#if DEBUG
            AcKrovyDiagnostics.Info(
                "StandaloneNativeLandingSetDoglegProbe",
                "leaderIndex=" +
                leaderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ";DoglegLength=" +
                doglegLength.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ";vectorX=" +
                unitX.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ";vectorY=" +
                unitY.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ";objectIdIsNull=" +
                leader.ObjectId.IsNull +
                ";kneeToLanding=" +
                knee.DistanceTo(landing).ToString(
                    "R",
                    System.Globalization.CultureInfo.InvariantCulture));
#endif
            leader.EnableDogleg = true;
            leader.DoglegLength = doglegLength;
            leader.SetDogleg(leaderIndex, doglegDirection);
            leader.TextLocation = landing;
            leader.SetLastVertex(leaderLineIndex, knee);
            leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
            leader.SetDogleg(leaderIndex, doglegDirection);
            leader.DoglegLength = doglegLength;
        }
        else
        {
            leader.EnableDogleg = false;
            leader.DoglegLength = 0d;
            leader.TextLocation = landing;
            leader.SetLastVertex(leaderLineIndex, knee);
            leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
        }

        leader.RecordGraphicsModified(true);
    }

    private static bool TryResolveStandaloneNativeLanding(
        LeaderPlacement placement,
        out double doglegLength,
        out Vector3d doglegDirection)
    {
        doglegLength = 0d;
        doglegDirection = default;
        try
        {
            var landing =
                TimberItemLeaderLayoutCalculator.ResolveStandaloneNativeLanding(
                    placement.Knee.X,
                    placement.Knee.Y,
                    placement.TextLocation.X,
                    placement.TextLocation.Y);
            doglegLength = landing.LengthMm;
            doglegDirection = placement.DoglegDirection is Vector3d preferred &&
                preferred.LengthSqrd > 1e-18d
                ? preferred.GetNormal()
                : new Vector3d(landing.DirX, landing.DirY, 0d);
            // Keep length as projection onto the preferred ±T when both exist.
            if (placement.DoglegDirection is not null)
            {
                var delta = placement.TextLocation - placement.Knee;
                var projected = Math.Abs(delta.DotProduct(doglegDirection));
                if (projected > 1e-9d)
                {
                    doglegLength = projected;
                }
            }

            return doglegLength > 1e-9d;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Standalone DimensionsLeader only: idempotent absolute MText rotation from
    /// physical Start→End. Must not leave Rotation=0 on AK_LABELS refresh.
    /// </summary>
    private static void ApplyAbsoluteStandaloneDimensionsLeaderTextOrientation(
        MLeader leader,
        double physicalSourceAxisRadians)
    {
        var content = leader.MText;
        if (content is null)
        {
            return;
        }

        content.Rotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(physicalSourceAxisRadians));
        leader.MText = content;
    }

    /// <summary>
    /// Standalone Plain ItemOnly only: idempotent absolute MText rotation from
    /// physical Start→End. Must not leave Rotation=0 on AK_LABELS refresh.
    /// </summary>
    private static void ApplyAbsoluteStandalonePlainItemLeaderTextOrientation(
        MLeader leader,
        double physicalSourceAxisRadians)
    {
        var content = leader.MText;
        if (content is null)
        {
            return;
        }

        content.Rotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                TimberStandaloneNativeLeaderOrientationRules
                    .ResolveTextPresentationRadians(physicalSourceAxisRadians));
        leader.MText = content;
    }

    /// <summary>
    /// Reads live MLeader geometry after a standalone in-place refresh so
    /// metadata Text/Anchor bookkeeping matches the entity: gripped placement
    /// after content-only, or CREATE canonical after source-driven rebuild.
    /// </summary>
    private static LeaderPlacement CaptureLiveNativeLeaderPlacement(
        MLeader leader,
        LeaderPlacement fallback)
    {
        var textLocation = leader.TextLocation;
        var anchor = fallback.Anchor;
        var knee = fallback.Knee;
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 1)
        {
            var lineIndexes = leader
                .GetLeaderLineIndexes(leaderIndexes[0])
                .Cast<int>()
                .ToArray();
            if (lineIndexes.Length == 1 &&
                leader.VerticesCount(lineIndexes[0]) >= 2)
            {
                anchor = leader.GetFirstVertex(lineIndexes[0]);
                knee = leader.GetLastVertex(lineIndexes[0]);
            }
        }

        return fallback with
        {
            Anchor = anchor,
            Knee = knee,
            TextLocation = textLocation,
        };
    }

    private static NativeLeaderGeometrySnapshot SynchronizeNativeLeaderGeometry(
        MLeader leader,
        int leaderLineIndex,
        Point3d anchor,
        Point3d knee,
        double expectedAcuteAngleRadians =
            TimberItemLeaderLayoutCalculator.FirstSegmentAngleRadians)
    {
        leader.SetFirstVertex(leaderLineIndex, anchor);
        leader.SetLastVertex(leaderLineIndex, knee);

        var actualAnchor = leader.GetFirstVertex(leaderLineIndex);
        var actualKnee = leader.GetLastVertex(leaderLineIndex);
        var segment = actualKnee - actualAnchor;
        var orientedAngleRadians = Math.Atan2(segment.Y, segment.X);
        if (orientedAngleRadians < 0d)
        {
            orientedAngleRadians += 2d * Math.PI;
        }

        var acuteAngleRadians = TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
            segment.X,
            segment.Y,
            localHorizontalX: 1d,
            localHorizontalY: 0d);
        if (Math.Abs(
                acuteAngleRadians -
                expectedAcuteAngleRadians) >
            TimberItemLeaderLayoutCalculator.AngleToleranceRadians)
        {
            throw new InvalidOperationException(
                $"Native MLeader first segment has an invalid acute angle " +
                $"{acuteAngleRadians * 180d / Math.PI:R}°.");
        }

        return new NativeLeaderGeometrySnapshot(
            actualAnchor,
            actualKnee,
            segment,
            orientedAngleRadians,
            acuteAngleRadians);
    }

    private static bool LeaderGeometryMatches(
        ElementLabelData existing,
        TimberAnnotationMode mode,
        LeaderPlacement placement) =>
        !TimberAnnotationModeRules.RequiresLeaderRecreation(
            existing.AnnotationMode,
            existing.ItemNumberLeaderStyle,
            mode,
            placement.ItemStyle ?? ItemNumberLeaderStyle.Plain) &&
        NearlyEqual(existing.AnchorX, placement.Anchor.X) &&
        NearlyEqual(existing.AnchorY, placement.Anchor.Y) &&
        NearlyEqual(existing.TextX, placement.TextLocation.X) &&
        NearlyEqual(existing.TextY, placement.TextLocation.Y) &&
        NearlyEqual(existing.RotationRadians, placement.RotationRadians) &&
        NearlyEqual(existing.EnvelopeWidthMm, placement.EnvelopeWidthMm) &&
        NearlyEqual(existing.EnvelopeHeightMm, placement.EnvelopeHeightMm);

    private static bool NearlyEqual(double? left, double right) =>
        left.HasValue && Math.Abs(left.Value - right) <= PlacementToleranceMm;

    private sealed record LabelPlacement(
        Point3d Location,
        double RotationRadians);
    internal sealed record LeaderPlacement(
        Point3d Anchor,
        Point3d Knee,
        Point3d TextLocation,
        double RotationRadians,
        ItemNumberLeaderStyle? ItemStyle = null,
        TimberLeaderHorizontalSide Side = TimberLeaderHorizontalSide.Right,
        double EnvelopeWidthMm = 0d,
        double EnvelopeHeightMm = 0d,
        Vector3d? DoglegDirection = null);
    private sealed record MainAnnotationEntry(
        ObjectId Id,
        ElementLabelData Data,
        TimberMainAnnotationRepresentation Representation);
    private sealed record AnnotationEntityEntry(
        ObjectId Id,
        ElementLabelData Data,
        MainAnnotationEntityType EntityType);
    private enum MainAnnotationEntityType
    {
        MText,
        MLeader,
        BlockReference,
        Circle,
        Polyline,
        DBText,
    }
    private sealed record NativeLeaderGeometrySnapshot(
        Point3d Anchor,
        Point3d Knee,
        Vector3d FirstSegment,
        double OrientedAngleRadians,
        double AcuteAngleRadians);
}

internal sealed record ElementLabelUpdateResult(
    int Created,
    int Updated,
    int Skipped)
{
    public IReadOnlyList<TimberElementNumberingChange> NumberingChanges { get; init; } =
        Array.Empty<TimberElementNumberingChange>();

    public int Processed => Created + Updated;
}
