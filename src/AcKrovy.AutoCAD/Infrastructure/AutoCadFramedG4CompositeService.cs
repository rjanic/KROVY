using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production G4 framed composite renderer: leader-only MLeader + frame-only
/// BlockReference + independent DBText item code. Geometry stays frozen via
/// Resolve(style, text); item height is paper × effective denominator.
/// </summary>
internal static class AutoCadFramedG4CompositeService
{
    private const double PlacementToleranceMm = 0.001d;

    public static AutoCadFramedG4Preparation? TryPrepare(
        Database database,
        Transaction transaction,
        TimberElementData data,
        string contents,
        TimberAnnotationScaleContext annotationScaleContext,
        AutoCadAnnotationPresentationContext presentationContext,
        AutoCadItemLeaderFrameOnlyBlockBatchCatalog? frameBatchCatalog,
        bool combinedFramed,
        string? existingAnnotationGroupId,
        out AutoCadItemLeaderFrameOnlyBlockResult frameResult)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        ArgumentNullException.ThrowIfNull(presentationContext);

        if (presentationContext.AnnotationScaleDenominator !=
            annotationScaleContext.Denominator)
        {
            frameResult = AutoCadItemLeaderFrameOnlyBlockResult.InvalidRequest(
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Presentation and renderer scale contexts do not match.");
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.04",
                frameResult.Diagnostic ?? "scale context mismatch");
#endif
            return null;
        }

        var itemCodeText = presentationContext.FramedItemCodeText;
#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.06",
            $"resolve ItemCode TextStyleId=" +
            $"{(itemCodeText.ResolvedTextStyleId?.ToString() ?? "<null>")} " +
            $"name={itemCodeText.ResolvedTextStyleName ?? "<none>"} " +
            $"paper={itemCodeText.PaperHeightMm:R} model={itemCodeText.ModelHeightMm:R}");
#endif
        if (itemCodeText.ResolvedTextStyleId is not ObjectId textStyleId ||
            itemCodeText.ResolvedTextStyleName is null)
        {
            frameResult = AutoCadItemLeaderFrameOnlyBlockResult.InvalidRequest(
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "The Stage 2 resolution did not supply a compatible text style.");
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.06",
                frameResult.Diagnostic ?? "missing text style");
#endif
            return null;
        }

        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            frameResult = AutoCadItemLeaderFrameOnlyBlockResult.DatabaseMismatch(
                null,
                AutoCadDatabaseIdentity.TryGetIdentity(database),
                "Resolved text-style ObjectId belongs to a different database.");
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.06",
                frameResult.Diagnostic ?? "text style database mismatch",
                textStyleId: textStyleId);
#endif
            return null;
        }

#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.04",
            $"ensure G4 frame definition style={data.ItemNumberLeaderStyle} " +
            $"contents={contents} combinedFramed={combinedFramed}");
#endif
        frameResult = AcKrovyItemLeaderFrameOnlyBlockService.Ensure(
            database,
            transaction,
            data.ItemNumberLeaderStyle,
            contents,
            frameBatchCatalog);
        if (!frameResult.Succeeded ||
            frameResult.BlockTableRecordId is not ObjectId ||
            frameResult.VariantKey is null)
        {
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.04",
                frameResult.Diagnostic ?? "frame Ensure failed",
                frameBlockName: frameResult.ResolvedBlockName ??
                    frameResult.CanonicalBlockName,
                frameBlockId: frameResult.BlockTableRecordId);
#endif
            return null;
        }

#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.04",
            $"frame definition OK name={frameResult.ResolvedBlockName} " +
            $"id={frameResult.BlockTableRecordId}");
#endif
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            data.ItemNumberLeaderStyle,
            contents);
        var textStyleIdentity =
            AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
                itemCodeText.ResolvedTextStyleName);

        return new AutoCadFramedG4Preparation(
            frameResult,
            definition,
            AutoCadFramedG4CompositePolicy.CalculateFrameBlockScale(
                annotationScaleContext),
            itemCodeText.ModelHeightMm,
            itemCodeText.PaperHeightMm,
            textStyleId,
            itemCodeText.ResolvedTextStyleName,
            textStyleIdentity,
            string.IsNullOrWhiteSpace(existingAnnotationGroupId)
                ? AutoCadFramedG4CompositePolicy.CreateAnnotationGroupId()
                : existingAnnotationGroupId.Trim(),
            combinedFramed);
    }

    public static bool TryUpsert(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId,
        ElementLabelService.LeaderPlacement placement,
        ElementLabelService.LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        bool copySourcePreservation,
        AutoCadFramedG4Preparation preparation,
        out ObjectId itemCodeId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(preparation);

        itemCodeId = ObjectId.Null;
        var sourceHandle = sourceEntity.Handle.ToString();
        var existing = FindExistingComposite(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            currentElementOwnerCount: ElementLabelService.CountTimberElementsWithElementIdForG4(
                database,
                transaction,
                data.ElementId),
            previousElementOwnerCount: ElementLabelService.CountTimberElementsWithElementIdForG4(
                database,
                transaction,
                previousElementId),
            allowElementIdFallback: !copySourcePreservation);

        // Migrate legacy G2/G3 BlockContent MLeaders by replacing the whole
        // annotation group. Never mutate old block definitions.
        if (existing.LegacyBlockLeaderId is ObjectId legacyId &&
            !legacyId.IsNull)
        {
            EraseEntityIfPresent(transaction, legacyId);
            existing = AutoCadFramedG4ExistingComposite.Empty;
        }

        if (existing.HasCompleteComposite)
        {
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Step(
                "F.05",
                $"update frame BlockReference id={existing.FrameId}");
            AutoCadFramedG4HostDiagnostics.Step(
                "F.07",
                $"update DBText ItemCode id={existing.ItemCodeId}");
#endif
            var updatePreparation = string.IsNullOrWhiteSpace(existing.AnnotationGroupId)
                ? preparation
                : preparation with
                {
                    AnnotationGroupId = existing.AnnotationGroupId!,
                };
            UpdateComposite(
                database,
                transaction,
                existing,
                placement,
                contents,
                updatePreparation,
                updateExistingLayer: !copySourcePreservation);
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Step(
                "F.08",
                $"write composite metadata group={updatePreparation.AnnotationGroupId}");
#endif
            WriteCompositeMetadata(
                transaction,
                existing,
                data,
                sourceHandle,
                placement,
                automaticPlacement,
                manualOffset,
                contents,
                updatePreparation);
            itemCodeId = existing.ItemCodeId;
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Outcome(
                "UPDATED",
                $"group={updatePreparation.AnnotationGroupId} " +
                $"leader={existing.LeaderId} frame={existing.FrameId} " +
                $"item={existing.ItemCodeId} " +
                $"frameBlock={updatePreparation.FrameBlockName}");
#endif
            return false;
        }

        // Partial orphans — erase and recreate cleanly.
        ErasePartialComposite(transaction, existing);

#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.05",
            "create frame BlockReference + leader + ItemCode");
        AutoCadFramedG4HostDiagnostics.Step(
            "F.07",
            "create DBText ItemCode");
#endif
        var created = CreateComposite(
            database,
            transaction,
            placement,
            contents,
            preparation,
            updateExistingLayer: !copySourcePreservation);
#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.08",
            $"write composite metadata group={preparation.AnnotationGroupId}");
#endif
        WriteCompositeMetadata(
            transaction,
            created,
            data,
            sourceHandle,
            placement,
            automaticPlacement,
            manualOffset,
            contents,
            preparation);
        itemCodeId = created.ItemCodeId;
#if DEBUG
        AutoCadFramedG4HostDiagnostics.Outcome(
            "CREATED",
            $"group={preparation.AnnotationGroupId} " +
            $"leader={created.LeaderId} frame={created.FrameId} " +
            $"item={created.ItemCodeId} " +
            $"frameBlock={preparation.FrameBlockName} " +
            $"TextStyleId={preparation.TextStyleId} " +
            $"height={preparation.ItemCodeModelHeightMm:R}");
#endif
        return true;
    }

    public static bool IsLegacyG2G3BlockLeader(
        Transaction transaction,
        ObjectId entityId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<MLeader>(
                transaction,
                entityId,
                OpenMode.ForRead,
                out var leader) ||
            leader is null ||
            leader.IsErased)
        {
            return false;
        }

        return leader.ContentType == ContentType.BlockContent &&
            !leader.BlockContentId.IsNull;
    }

    private static AutoCadFramedG4ExistingComposite FindExistingComposite(
        Database database,
        Transaction transaction,
        string elementId,
        string sourceHandle,
        string? previousElementId,
        int currentElementOwnerCount,
        int previousElementOwnerCount,
        bool allowElementIdFallback)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        var candidates = new List<TimberFramedG4CompositeCandidate>();
        var entityIdsByKey = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is not (MLeader or BlockReference or DBText or MText) ||
                !ElementLabelStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            var isG4Role = AutoCadFramedG4CompositePolicy.IsG4CompositeRole(
                data.ComponentRole);
            var isLegacyRole =
                AutoCadFramedG4CompositePolicy.IsLegacyG2G3BlockLeaderRole(
                    data.ComponentRole) &&
                entity is MLeader &&
                IsLegacyG2G3BlockLeader(transaction, id);
            if (!isG4Role && !isLegacyRole)
            {
                continue;
            }

            if (isG4Role)
            {
                if (data.ComponentRole == AutoCadFramedG4CompositePolicy.LeaderRole &&
                    entity is not MLeader)
                {
                    continue;
                }

                if (data.ComponentRole == AutoCadFramedG4CompositePolicy.FrameRole &&
                    entity is not BlockReference)
                {
                    continue;
                }
            }

            var entityKey = id.ToString();
            entityIdsByKey[entityKey] = id;
            candidates.Add(new TimberFramedG4CompositeCandidate
            {
                EntityKey = entityKey,
                ElementId = data.ElementId,
                SourceHandle = data.SourceHandle,
                ComponentRole = data.ComponentRole,
                AnnotationGroupId =
                    data.RendererGeneration ==
                        AutoCadFramedG4CompositePolicy.RendererGeneration
                        ? data.AnnotationGroupId
                        : null,
                IsLegacyBlockLeader = isLegacyRole,
            });
        }

        var selection = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle,
            elementId,
            previousElementId,
            candidates,
            currentElementOwnerCount,
            previousElementOwnerCount,
            allowElementIdFallback);

        foreach (var obsoleteKey in selection.EntityKeysToDelete)
        {
            if (entityIdsByKey.TryGetValue(obsoleteKey, out var obsoleteId))
            {
                EraseEntityIfPresent(transaction, obsoleteId);
            }
        }

        return new AutoCadFramedG4ExistingComposite(
            ResolveSelectedEntityId(selection.LeaderKey, entityIdsByKey),
            ResolveSelectedEntityId(selection.FrameKey, entityIdsByKey),
            ResolveSelectedEntityId(selection.ItemCodeKey, entityIdsByKey),
            ResolveSelectedEntityId(selection.LegacyBlockLeaderKey, entityIdsByKey),
            selection.AnnotationGroupId);
    }

    private static ObjectId ResolveSelectedEntityId(
        string? entityKey,
        IReadOnlyDictionary<string, ObjectId> entityIdsByKey) =>
        !string.IsNullOrWhiteSpace(entityKey) &&
        entityIdsByKey.TryGetValue(entityKey, out var id)
            ? id
            : ObjectId.Null;

    private static AutoCadFramedG4ExistingComposite CreateComposite(
        Database database,
        Transaction transaction,
        ElementLabelService.LeaderPlacement placement,
        string contents,
        AutoCadFramedG4Preparation preparation,
        bool updateExistingLayer)
    {
        var layerId = ElementLabelService.EnsureLabelLayerForG4(
            database,
            transaction,
            updateExistingLayer);
        var modelSpace = OpenModelSpaceForWrite(database, transaction);

        var leader = CreateLeaderOnlyMLeader(
            database,
            transaction,
            placement,
            preparation,
            layerId);
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        FinalizeLeaderVertices(leader, placement);

        var frame = CreateFrameReference(
            database,
            placement,
            preparation,
            layerId);
        modelSpace.AppendEntity(frame);
        transaction.AddNewlyCreatedDBObject(frame, true);

        var itemText = CreateItemCodeDbText(
            database,
            placement,
            contents,
            preparation,
            layerId);
        modelSpace.AppendEntity(itemText);
        transaction.AddNewlyCreatedDBObject(itemText, true);

        return new AutoCadFramedG4ExistingComposite(
            leader.ObjectId,
            frame.ObjectId,
            itemText.ObjectId,
            ObjectId.Null,
            preparation.AnnotationGroupId);
    }

    private static void UpdateComposite(
        Database database,
        Transaction transaction,
        AutoCadFramedG4ExistingComposite existing,
        ElementLabelService.LeaderPlacement placement,
        string contents,
        AutoCadFramedG4Preparation preparation,
        bool updateExistingLayer)
    {
        var layerId = ElementLabelService.EnsureLabelLayerForG4(
            database,
            transaction,
            updateExistingLayer);

        var leader = (MLeader)transaction.GetObject(
            existing.LeaderId,
            OpenMode.ForWrite);
        ApplyLeaderGeometry(database, transaction, leader, placement, preparation);
        leader.LayerId = layerId;
        ApplyByLayerAppearance(leader, database);

        var frame = (BlockReference)transaction.GetObject(
            existing.FrameId,
            OpenMode.ForWrite);
        frame.BlockTableRecord = preparation.FrameBlockTableRecordId;
        frame.Position = placement.TextLocation;
        frame.ScaleFactors = new Scale3d(preparation.FrameBlockScale);
        frame.Rotation = placement.RotationRadians;
        frame.LayerId = layerId;
        ApplyByLayerAppearance(frame, database);

        if (transaction.GetObject(existing.ItemCodeId, OpenMode.ForWrite) is DBText dbText)
        {
            ApplyItemCodeDbText(dbText, placement, contents, preparation, layerId, database);
        }
        else if (transaction.GetObject(existing.ItemCodeId, OpenMode.ForWrite) is MText mText)
        {
            // Prefer DBText; if an older probe left MText, rewrite in place.
            mText.Contents = contents;
            mText.Location = placement.TextLocation;
            mText.Attachment = AttachmentPoint.MiddleCenter;
            mText.TextHeight = preparation.ItemCodeModelHeightMm;
            mText.TextStyleId = preparation.TextStyleId;
            mText.Rotation = placement.RotationRadians;
            mText.LayerId = layerId;
            ApplyByLayerAppearance(mText, database);
        }
    }

    private static MLeader CreateLeaderOnlyMLeader(
        Database database,
        Transaction transaction,
        ElementLabelService.LeaderPlacement placement,
        AutoCadFramedG4Preparation preparation,
        ObjectId layerId)
    {
        var styleId = preparation.CombinedFramed
            ? AcKrovyMLeaderStyleService.EnsureCombinedFramed(
                database,
                transaction,
                updateExisting: true)
            : AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExisting: true);

        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.MLeaderStyle = styleId;
        // Leader-only: no BlockContent and no visible text content.
        leader.ContentType = ContentType.NoneContent;

        var leaderIndex = leader.AddLeader();
        var leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(leaderLineIndex, placement.Anchor);
        leader.AddLastVertex(leaderLineIndex, placement.Knee);

        if (preparation.CombinedFramed)
        {
            AcKrovyMLeaderStyleService.ApplyCombinedBlockInstanceProperties(
                leader,
                database,
                leaderIndex,
                leaderLineIndex,
                placement.Side,
                preparation.FrameBlockScale,
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
                preparation.FrameBlockScale);
            ApplyStandaloneFramedItemOnlyStraightLeader(leader, leaderLineIndex);
        }

        // Re-assert after style helpers that may assume BlockContent.
        leader.ContentType = ContentType.NoneContent;
        leader.LayerId = layerId;
        ApplyByLayerAppearance(leader, database);
        return leader;
    }

    private static void ApplyLeaderGeometry(
        Database database,
        Transaction transaction,
        MLeader leader,
        ElementLabelService.LeaderPlacement placement,
        AutoCadFramedG4Preparation preparation)
    {
        var styleId = preparation.CombinedFramed
            ? AcKrovyMLeaderStyleService.EnsureCombinedFramed(
                database,
                transaction,
                updateExisting: true)
            : AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExisting: true);
        if (leader.MLeaderStyle != styleId)
        {
            leader.MLeaderStyle = styleId;
        }

        leader.ContentType = ContentType.NoneContent;
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        int leaderIndex;
        int leaderLineIndex;
        if (leaderIndexes.Length == 1)
        {
            leaderIndex = leaderIndexes[0];
            var lineIndexes = leader
                .GetLeaderLineIndexes(leaderIndex)
                .Cast<int>()
                .ToArray();
            if (lineIndexes.Length == 1 && leader.VerticesCount(lineIndexes[0]) >= 2)
            {
                leaderLineIndex = lineIndexes[0];
                leader.SetFirstVertex(leaderLineIndex, placement.Anchor);
                leader.SetLastVertex(leaderLineIndex, placement.Knee);
            }
            else
            {
                RebuildLeaderLine(leader, placement, out leaderIndex, out leaderLineIndex);
            }
        }
        else
        {
            RebuildLeaderLine(leader, placement, out leaderIndex, out leaderLineIndex);
        }

        if (preparation.CombinedFramed)
        {
            AcKrovyMLeaderStyleService.ApplyCombinedBlockInstanceProperties(
                leader,
                database,
                leaderIndex,
                leaderLineIndex,
                placement.Side,
                preparation.FrameBlockScale,
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
                preparation.FrameBlockScale);
            ApplyStandaloneFramedItemOnlyStraightLeader(leader, leaderLineIndex);
        }

        leader.ContentType = ContentType.NoneContent;
    }

    /// <summary>
    /// Standalone G4 ItemOnly only: one ordinary straight leader segment
    /// Anchor→frame (Content==Knee). Does not enable Combined landing/dogleg.
    /// CombinedFramed keeps ApplyCombinedBlockInstanceProperties untouched.
    /// </summary>
    private static void ApplyStandaloneFramedItemOnlyStraightLeader(
        MLeader leader,
        int leaderLineIndex)
    {
        leader.LeaderLineType = LeaderType.StraightLeader;
        leader.SetLeaderLineType(leaderLineIndex, LeaderType.StraightLeader);
        leader.EnableLanding = false;
        leader.EnableDogleg = false;
        leader.DoglegLength = 0d;
        leader.LandingGap = 0d;
    }

    private static void RebuildLeaderLine(
        MLeader leader,
        ElementLabelService.LeaderPlacement placement,
        out int leaderIndex,
        out int leaderLineIndex)
    {
        foreach (int existingLeader in leader.GetLeaderIndexes().Cast<int>().ToArray())
        {
            leader.RemoveLeader(existingLeader);
        }

        leaderIndex = leader.AddLeader();
        leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(leaderLineIndex, placement.Anchor);
        leader.AddLastVertex(leaderLineIndex, placement.Knee);
    }

    private static void FinalizeLeaderVertices(
        MLeader leader,
        ElementLabelService.LeaderPlacement placement)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length != 1)
        {
            return;
        }

        var lineIndexes = leader
            .GetLeaderLineIndexes(leaderIndexes[0])
            .Cast<int>()
            .ToArray();
        if (lineIndexes.Length != 1)
        {
            return;
        }

        leader.SetFirstVertex(lineIndexes[0], placement.Anchor);
        leader.SetLastVertex(lineIndexes[0], placement.Knee);
    }

    private static BlockReference CreateFrameReference(
        Database database,
        ElementLabelService.LeaderPlacement placement,
        AutoCadFramedG4Preparation preparation,
        ObjectId layerId)
    {
        var frame = new BlockReference(
            placement.TextLocation,
            preparation.FrameBlockTableRecordId);
        frame.SetDatabaseDefaults(database);
        frame.ScaleFactors = new Scale3d(preparation.FrameBlockScale);
        frame.Rotation = placement.RotationRadians;
        frame.LayerId = layerId;
        ApplyByLayerAppearance(frame, database);
        return frame;
    }

    private static DBText CreateItemCodeDbText(
        Database database,
        ElementLabelService.LeaderPlacement placement,
        string contents,
        AutoCadFramedG4Preparation preparation,
        ObjectId layerId)
    {
        var text = new DBText();
        text.SetDatabaseDefaults(database);
        ApplyItemCodeDbText(
            text,
            placement,
            contents,
            preparation,
            layerId,
            database);
        return text;
    }

    private static void ApplyItemCodeDbText(
        DBText text,
        ElementLabelService.LeaderPlacement placement,
        string contents,
        AutoCadFramedG4Preparation preparation,
        ObjectId layerId,
        Database database)
    {
        text.TextString = contents;
        text.Height = preparation.ItemCodeModelHeightMm;
        text.TextStyleId = preparation.TextStyleId;
        text.HorizontalMode = TextHorizontalMode.TextCenter;
        text.VerticalMode = TextVerticalMode.TextVerticalMid;
        text.AlignmentPoint = placement.TextLocation;
        text.Position = placement.TextLocation;
        text.Rotation = placement.RotationRadians;
        text.LayerId = layerId;
        ApplyByLayerAppearance(text, database);
        // AlignmentPoint must be set after Horizontal/Vertical mode for
        // MiddleCenter to stick on DBText.
        text.AlignmentPoint = placement.TextLocation;
    }

    private static void WriteCompositeMetadata(
        Transaction transaction,
        AutoCadFramedG4ExistingComposite composite,
        TimberElementData data,
        string sourceHandle,
        ElementLabelService.LeaderPlacement placement,
        ElementLabelService.LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        AutoCadFramedG4Preparation preparation)
    {
        WriteRoleMetadata(
            transaction,
            composite.LeaderId,
            data,
            sourceHandle,
            placement,
            automaticPlacement,
            manualOffset,
            contents,
            preparation,
            AutoCadFramedG4CompositePolicy.LeaderRole);
        WriteRoleMetadata(
            transaction,
            composite.FrameId,
            data,
            sourceHandle,
            placement,
            automaticPlacement,
            manualOffset,
            contents,
            preparation,
            AutoCadFramedG4CompositePolicy.FrameRole);
        WriteRoleMetadata(
            transaction,
            composite.ItemCodeId,
            data,
            sourceHandle,
            placement,
            automaticPlacement,
            manualOffset,
            contents,
            preparation,
            AutoCadFramedG4CompositePolicy.ItemCodeRole);
    }

    private static void WriteRoleMetadata(
        Transaction transaction,
        ObjectId entityId,
        TimberElementData data,
        string sourceHandle,
        ElementLabelService.LeaderPlacement placement,
        ElementLabelService.LeaderPlacement automaticPlacement,
        TimberFramedLeaderManualOffset manualOffset,
        string contents,
        AutoCadFramedG4Preparation preparation,
        TimberMainAnnotationComponentRole role)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                entityId,
                OpenMode.ForWrite,
                out var entity) ||
            entity is null)
        {
            return;
        }

        ElementLabelStore.Write(entity, transaction, new ElementLabelData
        {
            SchemaVersion = AutoCadFramedG4CompositePolicy.LabelMetadataSchemaVersion,
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
            CombinedDoglegDirectionX = placement.DoglegDirection?.X,
            CombinedDoglegDirectionY = placement.DoglegDirection?.Y,
            ComponentRole = role,
            AnnotationGroupId = preparation.AnnotationGroupId,
            RendererGeneration = AutoCadFramedG4CompositePolicy.RendererGeneration,
            FrameSize = preparation.Definition.Size,
            ItemCodePaperHeightMm = preparation.ItemCodePaperHeightMm,
            ItemCodeTextStyleName = preparation.TextStyleName,
        });
    }

    private static void ErasePartialComposite(
        Transaction transaction,
        AutoCadFramedG4ExistingComposite existing)
    {
        EraseEntityIfPresent(transaction, existing.LeaderId);
        EraseEntityIfPresent(transaction, existing.FrameId);
        EraseEntityIfPresent(transaction, existing.ItemCodeId);
        EraseEntityIfPresent(transaction, existing.LegacyBlockLeaderId);
    }

    private static void EraseEntityIfPresent(Transaction transaction, ObjectId id)
    {
        if (id.IsNull)
        {
            return;
        }

        if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                id,
                OpenMode.ForWrite,
                out var entity) &&
            entity is not null &&
            !entity.IsErased)
        {
            entity.Erase();
        }
    }

    private static void ApplyByLayerAppearance(Entity entity, Database database)
    {
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        entity.LinetypeId = database.ByLayerLinetype;
        entity.LinetypeScale = 1d;
        entity.LineWeight = LineWeight.ByLayer;
    }

    private static BlockTableRecord OpenModelSpaceForWrite(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
    }
}

internal sealed record AutoCadFramedG4ExistingComposite(
    ObjectId LeaderId,
    ObjectId FrameId,
    ObjectId ItemCodeId,
    ObjectId LegacyBlockLeaderId,
    string? AnnotationGroupId)
{
    public static AutoCadFramedG4ExistingComposite Empty { get; } =
        new(ObjectId.Null, ObjectId.Null, ObjectId.Null, ObjectId.Null, null);

    public bool HasCompleteComposite =>
        !LeaderId.IsNull && !FrameId.IsNull && !ItemCodeId.IsNull;
}

/// <summary>
/// Narrow bridge so the G4 composite service can ensure the label layer and
/// reuse timber owner counts without widening ElementLabelService's private
/// surface beyond these helpers.
/// </summary>
internal static partial class ElementLabelService
{
    internal static ObjectId EnsureLabelLayerForG4(
        Database database,
        Transaction transaction,
        bool updateExistingLayer) =>
        EnsureLabelLayer(database, transaction, updateExistingLayer);

    internal static int CountTimberElementsWithElementIdForG4(
        Database database,
        Transaction transaction,
        string? elementId) =>
        CountTimberElementsWithElementId(database, transaction, elementId);
}
