using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
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
internal static class ElementLabelService
{
    public const string LabelLayerName = "KROV_POPIS";

    private const short LabelLayerColorIndex = 8;
    private const double DefaultTextHeightMm = TimberMainAnnotationTextRules.TextHeightMm;
    private const double LabelOffsetMm = 180d;
    private const double PlacementToleranceMm = 0.001d;

    public static bool UpsertForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity) || string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(data, AutoCadEntityHelpers.GetPlanLengthMm(sourceEntity), roundingStepMm);
        var labelText = TimberMainAnnotationFormatter.Format(data, measurement);
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
                    : null);
            return UpsertLeader(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                leaderPlacement,
                labelText);
        }

        var placement = CalculatePlacement(sourceEntity);
        return UpsertLabel(
            database,
            transaction,
            sourceEntity,
            data,
            previousElementId,
            placement,
            labelText,
            AttachmentPoint.MiddleCenter,
            DefaultTextHeightMm,
            lineSpacingFactor: null);
    }

    public static bool UpsertForPostFootprint(
        Database database,
        Transaction transaction,
        Polyline sourcePolyline,
        TimberElementData data,
        TimberRectangularFootprintGeometry geometry,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourcePolyline);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(geometry);

        if (string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(
            data,
            planLengthMm: null,
            roundingIncrementMm: roundingStepMm);
        var normalizedMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode);
        var labelText = normalizedMode == TimberAnnotationMode.FullLabel
            ? TimberPostFootprintLabelFormatter.Format(data, measurement.ActualLengthMm)
            : TimberMainAnnotationFormatter.Format(data, measurement);
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
                    : null);
            return UpsertLeader(
                database,
                transaction,
                sourcePolyline,
                data,
                previousElementId,
                itemPlacement,
                labelText);
        }

        var footprintPlacement = TimberPostFootprintLabelPlacementCalculator.Calculate(geometry.Bounds);
        var elevation = sourcePolyline.GetPoint3dAt(0).Z;
        var placement = new LabelPlacement(
            new Point3d(footprintPlacement.AnchorX, footprintPlacement.AnchorY, elevation),
            footprintPlacement.RotationRadians);

        return UpsertLabel(
            database,
            transaction,
            sourcePolyline,
            data,
            previousElementId,
            placement,
            labelText,
            AttachmentPoint.BottomCenter,
            TimberPostFootprintLabelPlacementCalculator.TextHeightMm,
            TimberPostFootprintLabelPlacementCalculator.LineSpacingFactor);
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
        double? lineSpacingFactor)
    {
        var sourceHandle = sourceEntity.Handle.ToString();
        var existingLabelId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            TimberMainAnnotationRepresentation.FullLabel,
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
            lineSpacingFactor);
        ElementLabelStore.Write(label, transaction, new ElementLabelData
        {
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = TimberAnnotationMode.FullLabel,
            Contents = labelText,
            TextX = placement.Location.X,
            TextY = placement.Location.Y,
            RotationRadians = placement.RotationRadians,
        });
        DeleteObsoleteLabels(transaction, obsoleteLabelIds, label.ObjectId);
        DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);

        return isCreated;
    }

    private static bool UpsertLeader(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        LeaderPlacement placement,
        string contents)
    {
        var sourceHandle = sourceEntity.Handle.ToString();
        var desiredRepresentation = TimberAnnotationModeRules.GetRepresentation(
            data.AnnotationMode,
            data.ItemNumberLeaderStyle);
        var existingId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            desiredRepresentation,
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
                // Insertion-point-connected BlockContent is evaluated from
                // the terminal leader vertex. Move that vertex by the same
                // manual delta; changing BlockPosition alone is discarded by
                // AutoCAD during MLeader evaluation.
                Knee = placement.Knee + manualDelta,
            };
        }
        var geometryMatches = existingData is not null &&
            LeaderGeometryMatches(existingData, data.AnnotationMode, placement);

        MLeader leader;
        var isCreated = existingId.IsNull;
        var metadataWritten = false;
        if (!isCreated &&
            geometryMatches &&
            transaction.GetObject(existingId, OpenMode.ForWrite, false) is MLeader existingLeader &&
            TryUpdateNativeLeader(
                database,
                transaction,
                existingLeader,
                placement,
                contents))
        {
            leader = existingLeader;
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
                    data.ItemNumberLeaderStyle)
                : CreateNativeMLeader(
                    database,
                    transaction,
                    placement,
                    contents);
            WriteLeaderMetadata(
                leader,
                transaction,
                data,
                sourceHandle,
                placement,
                automaticPlacement,
                manualOffset,
                contents);
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
                contents);
        }

        DeleteObsoleteLabels(transaction, obsoleteIds, leader.ObjectId);
        DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        return isCreated;
    }

    private static void WriteLeaderMetadata(
        MLeader leader,
        Transaction transaction,
        TimberElementData data,
        string sourceHandle,
        LeaderPlacement placement,
        LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents)
    {
        ElementLabelStore.Write(leader, transaction, new ElementLabelData
        {
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
            AnnotationMode = TimberAnnotationModeRules.Normalize(data.AnnotationMode),
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
            PlacementRotationRadians = placement.RotationRadians,
        });
    }

    public static ElementLabelUpdateResult UpdateAll(Database database, Editor editor)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = Update(
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
        var result = Update(database, transaction, editor, ids, metadataStore);
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
            })
            .ToList();
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
            ElementLabelStore.Write(leader, transaction, data with
            {
                TextX = actualBlockPosition.X,
                TextY = actualBlockPosition.Y,
                LocalManualOffsetAlongAxisMm = offset.AlongAxisMm,
                LocalManualOffsetNormalAxisMm = offset.NormalAxisMm,
                PlacementRotationRadians = rotation,
            });
            // Assigning XData can force an MLeader reevaluation. Restore the
            // user-observed position after the metadata write so CommandEnded
            // cannot snap BlockContent back to its preceding evaluated point.
            leader.BlockPosition = actualBlockPosition;
        }
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
            .FirstOrDefault(label => TimberSlopeAnnotationRules.HasSameSourceHandle(
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
            MLeader leader => (leader.TextLocation, leader.MText?.ActualWidth ?? 0d),
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
        var keysToDelete = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            labels
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                })
                .ToList(),
            ReadTimberSourceHandles(database, transaction));

        return DeleteLabelsByKey(transaction, labelIdsByKey, keysToDelete);
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
                })
                .ToList(),
            ReadTimberSourceHandles(database, transaction));

        return DeleteLabelsByKey(transaction, labelIdsByKey, keysToDelete);
    }

    private static ElementLabelUpdateResult Update(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var distinctIds = ids.Distinct().ToList();
        TimberElementCopyInitializationService.InitializeLocalCopies(
            database,
            transaction,
            metadataStore,
            distinctIds,
            defaultProfile);
        var previousElementIdById = ReadElementIds(transaction, metadataStore, distinctIds);
        var synchronizedDataById = TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            distinctIds,
            roundingStepMm);

        foreach (var id in distinctIds)
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
        return new ElementLabelUpdateResult(created, updated, skipped);
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
        out IReadOnlyList<ObjectId> obsoleteLabelIds)
    {
        obsoleteLabelIds = Array.Empty<ObjectId>();
        var labels = ReadLabels(database, transaction);

        var labelKeys = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var matchingRepresentation = labels
            .Where(label => label.Representation == desiredRepresentation)
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
                })
                .ToList(),
            CountTimberElementsWithElementId(database, transaction, elementId),
            CountTimberElementsWithElementId(database, transaction, previousElementId));

        obsoleteLabelIds = selection.LabelKeysToDelete
            .Where(labelKeys.ContainsKey)
            .Select(labelKey => labelKeys[labelKey])
            .Concat(labels
                .Where(label =>
                    label.Representation != desiredRepresentation &&
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
                    TimberMainAnnotationComponentRole.CircleText)
            .Select(entry => new MainAnnotationEntry(
                entry.Id,
                entry.Data,
                entry.Data.ComponentRole == TimberMainAnnotationComponentRole.CircleText ||
                entry.EntityType == MainAnnotationEntityType.MLeader &&
                TimberAnnotationModeRules.GetRepresentation(
                    entry.Data.AnnotationMode,
                    entry.Data.ItemNumberLeaderStyle) ==
                TimberMainAnnotationRepresentation.BlockLeader
                    ? TimberMainAnnotationRepresentation.BlockLeader
                    : entry.EntityType is MainAnnotationEntityType.MLeader or
                        MainAnnotationEntityType.BlockReference
                        ? TimberMainAnnotationRepresentation.Leader
                        : TimberMainAnnotationRepresentation.FullLabel))
            .ToList();
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
                annotation is not (MText or MLeader or BlockReference or Circle or Polyline) ||
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

    private static bool IsCircleComponent(TimberMainAnnotationComponentRole role) =>
        role is
            TimberMainAnnotationComponentRole.CircleText or
            TimberMainAnnotationComponentRole.CircleFrame or
            TimberMainAnnotationComponentRole.CircleLeaderLine;

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
        double? lineSpacingFactor)
    {
        var labelLayerId = EnsureLabelLayer(database, transaction);
        label.LayerId = labelLayerId;
        label.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        label.Attachment = attachment;
        label.TextHeight = textHeightMm;
        if (lineSpacingFactor.HasValue)
        {
            label.LineSpacingFactor = lineSpacingFactor.Value;
        }
        label.Rotation = placement.RotationRadians;
        label.Location = placement.Location;
        label.Contents = contents;
    }

    private static ObjectId EnsureLabelLayer(Database database, Transaction transaction)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;

        if (layerTable.Has(LabelLayerName))
        {
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

    private static LabelPlacement CalculatePlacement(Entity sourceEntity)
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

        var placement = TimberElementLabelPlacementCalculator.Calculate(
            start.X,
            start.Y,
            end.X,
            end.Y,
            midpoint.X,
            midpoint.Y,
            LabelOffsetMm);
        var location = new Point3d(placement.X, placement.Y, midpoint.Z);

        return new LabelPlacement(location, placement.RotationRadians);
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
        ItemNumberLeaderStyle? itemStyle)
    {
        var basePlacement = CalculateLeaderPlacement(sourceEntity);
        return CalculateShortLeaderPlacement(
            new TimberLeaderPlacement(
                basePlacement.Anchor.X,
                basePlacement.Anchor.Y,
                basePlacement.TextLocation.X,
                basePlacement.TextLocation.Y,
                basePlacement.RotationRadians),
            basePlacement.Anchor.Z,
            contents,
            itemStyle,
            basePlacement.Side);
    }

    private static LeaderPlacement CalculateShortLeaderPlacement(
        TimberLeaderPlacement basePlacement,
        double elevation,
        string contents,
        ItemNumberLeaderStyle? itemStyle,
        TimberLeaderHorizontalSide preferredSide = TimberLeaderHorizontalSide.Right)
    {
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(
            itemStyle ?? ItemNumberLeaderStyle.Plain);
        if (itemStyle.HasValue && normalizedStyle != ItemNumberLeaderStyle.Plain)
        {
            var blockLayout = TimberItemLeaderLayoutCalculator.CalculateBlock(
                basePlacement,
                contents,
                normalizedStyle,
                preferredSide);
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

        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            basePlacement,
            contents,
            normalizedStyle,
            preferredSide);
        return new LeaderPlacement(
            new Point3d(layout.AnchorX, layout.AnchorY, elevation),
            new Point3d(layout.KneeX, layout.KneeY, elevation),
            new Point3d(layout.ContentX, layout.ContentY, elevation),
            RotationRadians: 0d,
            itemStyle.HasValue ? normalizedStyle : null,
            layout.Side,
            layout.EnvelopeWidthMm,
            layout.EnvelopeHeightMm);
    }

    private static MLeader CreateNativeMLeader(
        Database database,
        Transaction transaction,
        LeaderPlacement placement,
        string contents)
    {
        var styleId = AcKrovyMLeaderStyleService.Ensure(database, transaction);
        var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
            database,
            transaction);
        var mText = CreateLeaderMText(database, placement.TextLocation, contents);
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
        AcKrovyMLeaderStyleService.ApplyInstanceProperties(
            leader,
            database,
            styleId,
            noneArrowId,
            leaderIndex,
            leaderLineIndex,
            placement.Side);

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        leader.LayerId = EnsureLabelLayer(database, transaction);
        leader.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        leader.LineWeight = LineWeight.ByLayer;
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        SynchronizeNativeLeaderGeometry(
            leader,
            leaderLineIndex,
            placement.Anchor,
            placement.Knee);
        return leader;
    }

    private static MLeader CreateBlockMLeader(
        Database database,
        Transaction transaction,
        LeaderPlacement placement,
        string contents,
        ItemNumberLeaderStyle itemStyle)
    {
        var styleId = AcKrovyMLeaderStyleService.EnsureFramed(database, transaction);
        var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
            database,
            transaction);
        var block = AcKrovyItemLeaderBlockService.Ensure(
            database,
            transaction,
            itemStyle,
            contents);
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.MLeaderStyle = styleId;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = block.BlockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(1d);
        leader.BlockRotation = 0d;
        leader.BlockPosition = placement.TextLocation;

        var leaderIndex = leader.AddLeader();
        var leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(leaderLineIndex, placement.Anchor);
        leader.AddLastVertex(leaderLineIndex, placement.Knee);
        AcKrovyMLeaderStyleService.ApplyBlockInstanceProperties(
            leader,
            database,
            noneArrowId,
            leaderIndex,
            leaderLineIndex,
            placement.Side);

        var attributeDefinition = (AttributeDefinition)transaction.GetObject(
            block.AttributeDefinitionId,
            OpenMode.ForRead);
        using (var attribute = new AttributeReference())
        {
            attribute.SetAttributeFromBlock(
                attributeDefinition,
                Matrix3d.Identity);
            attribute.TextString = contents;
            leader.SetBlockAttribute(block.AttributeDefinitionId, attribute);
        }

        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        leader.LayerId = EnsureLabelLayer(database, transaction);
        leader.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        leader.LineWeight = LineWeight.ByLayer;
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
        leader.SetLastVertex(leaderLineIndex, placement.Knee);
        return leader;
    }

    private static MText CreateLeaderMText(
        Database database,
        Point3d location,
        string contents)
    {
        var text = new MText();
        text.SetDatabaseDefaults(database);
        text.Contents = contents;
        text.Location = location;
        text.Attachment = AttachmentPoint.MiddleCenter;
        text.TextHeight = DefaultTextHeightMm;
        text.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        text.LineWeight = LineWeight.ByLayer;
        return text;
    }

    private static bool TryUpdateNativeLeader(
        Database database,
        Transaction transaction,
        MLeader leader,
        LeaderPlacement placement,
        string contents)
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

        var styleId = AcKrovyMLeaderStyleService.Ensure(database, transaction);
        var noneArrowId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
            database,
            transaction);
        if (leader.VerticesCount(lineIndexes[0]) < 2)
        {
            return false;
        }

        leader.MLeaderStyle = styleId;
        leader.MText = CreateLeaderMText(database, placement.TextLocation, contents);
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
            placement.Side);
        SynchronizeNativeLeaderGeometry(
            leader,
            lineIndexes[0],
            placement.Anchor,
            placement.Knee);
        return true;
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

    private sealed record LabelPlacement(Point3d Location, double RotationRadians);
    private sealed record LeaderPlacement(
        Point3d Anchor,
        Point3d Knee,
        Point3d TextLocation,
        double RotationRadians,
        ItemNumberLeaderStyle? ItemStyle = null,
        TimberLeaderHorizontalSide Side = TimberLeaderHorizontalSide.Right,
        double EnvelopeWidthMm = 0d,
        double EnvelopeHeightMm = 0d);
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
    }
    private sealed record NativeLeaderGeometrySnapshot(
        Point3d Anchor,
        Point3d Knee,
        Vector3d FirstSegment,
        double OrientedAngleRadians,
        double AcuteAngleRadians);
}

internal sealed record ElementLabelUpdateResult(int Created, int Updated, int Skipped)
{
    public int Processed => Created + Updated;
}
