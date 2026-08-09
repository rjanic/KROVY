using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Standalone framed ItemOnly (Iba položka v kruhu/obdĺžniku/slote) only:
/// one native BlockContent MLeader owning leader + Circle/Rect/Slot frame + ITEM_NO.
/// Does not call R3 Combined placement, lifecycle, RIGHT/LEFT, or landing services.
/// <para>
/// Orientation contract: absolute from physical Start→End —
/// (1) lay out in WorldXY, (2) <see cref="TimberItemLeaderLayoutCalculator.OrientAroundAnchor"/>
/// for leader geometry, (3) assign absolute <see cref="MLeader.BlockRotation"/>.
/// CREATE always writes that canonical once. Existing-owner refresh
/// (<see cref="TryUpdateInPlace"/>) is content-only when source Automatic*/axis
/// is unchanged (AK_LABELS / annotation grip); when source timber MOVE/STRETCH/
/// ROTATE, rewrites the same absolute CREATE canonical — never preserves old
/// manual content placement and never <c>TransformBy(readable)</c>.
/// </para>
/// </summary>
internal static class AutoCadStandaloneFramedItemOnlyAnnotationService
{
    public const int RendererGeneration = 5;
    public const int LabelMetadataSchemaVersion = 5;

    public static TimberMainAnnotationComponentRole OwnerRole { get; } =
        TimberMainAnnotationComponentRole.Primary;

    public static bool IsStandaloneFramedItemOnlyOwner(ElementLabelData data) =>
        data.ComponentRole == OwnerRole &&
        TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
            TimberAnnotationMode.ItemNumberLeader &&
        TimberAnnotationModeRules.IsFramedItemLeader(
            data.AnnotationMode,
            data.ItemNumberLeaderStyle) &&
        (data.RendererGeneration is null ||
         data.RendererGeneration == RendererGeneration);

    public static AutoCadStandaloneFramedItemOnlyCreateResult Create(
        Database database,
        Transaction transaction,
        AutoCadFramedBlockContentRequest definitionRequest,
        TimberItemLeaderLayout canonicalLayout,
        double physicalAxisRadians,
        double blockScale,
        ObjectId layerId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(definitionRequest);
        ArgumentNullException.ThrowIfNull(canonicalLayout);

        var definition = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            definitionRequest);
        if (!definition.Succeeded ||
            definition.BlockTableRecordId is not ObjectId blockId ||
            blockId.IsNull)
        {
            return AutoCadStandaloneFramedItemOnlyCreateResult.Fail(
                definition.DiagnosticReason);
        }

        try
        {
            var styleId = AcKrovyMLeaderStyleService.EnsureFramed(
                database,
                transaction,
                updateExisting: false);
            var desiredRotation =
                TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                    physicalAxisRadians);
            var oriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
                canonicalLayout,
                desiredRotation);
            var attachment = new Point3d(oriented.AnchorX, oriented.AnchorY, 0d);
            var framePoint = new Point3d(oriented.ContentX, oriented.ContentY, 0d);

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
            ApplyStraightSourceToFrameLeader(leader);

            var leaderIndex = leader.AddLeader();
            var lineIndex = leader.AddLeaderLine(leaderIndex);
            leader.AddFirstVertex(lineIndex, attachment);
            leader.AddLastVertex(lineIndex, framePoint);
            leader.SetLeaderLineType(lineIndex, LeaderType.StraightLeader);
            leader.BlockPosition = framePoint;

            leader.ArrowSymbolId = AcKrovyMLeaderStyleService.GetNoneArrowBlockId(
                database,
                transaction);
            leader.ArrowSize =
                TimberNativeLeaderStyleRules.FramedSettings.ArrowheadSize *
                blockScale;
            leader.LeaderLineColor = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
            leader.LeaderLineTypeId = database.ByBlockLinetype;
            leader.LeaderLineWeight = LineWeight.ByBlock;
            leader.LayerId = layerId;
            leader.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
            leader.LinetypeId = database.ByLayerLinetype;
            leader.LinetypeScale = 1d;
            leader.LineWeight = LineWeight.ByLayer;

            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(leader);
            transaction.AddNewlyCreatedDBObject(leader, true);

            leader.SetFirstVertex(lineIndex, attachment);
            leader.SetLastVertex(lineIndex, framePoint);
            ApplyStraightSourceToFrameLeader(leader);
            leader.BlockPosition = framePoint;
            ApplyItemNoAttribute(transaction, leader, blockId, definitionRequest);
            ApplyAbsoluteBlockContentOrientation(leader, desiredRotation);
            ReassertStraightLeader(leader);

            return AutoCadStandaloneFramedItemOnlyCreateResult.Ok(
                leader.ObjectId,
                definition.ResolvedBlockName ?? string.Empty);
        }
        catch (Exception exception) when (
            exception is AcadException or InvalidOperationException)
        {
            return AutoCadStandaloneFramedItemOnlyCreateResult.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Existing-owner refresh. AK_LABELS / unchanged source: ITEM_NO/content only
    /// (live attachment/frame/BlockRotation preserved). Source timber MOVE/STRETCH/
    /// ROTATE: rewrite absolute CREATE canonical via OrientAroundAnchor.
    /// </summary>
    public static bool TryUpdateInPlace(
        Database database,
        Transaction transaction,
        MLeader leader,
        AutoCadFramedBlockContentRequest definitionRequest,
        TimberItemLeaderLayout canonicalLayout,
        double physicalAxisRadians,
        double blockScale,
        TimberStandaloneNativeLeaderSourceSyncDecision sourceSync = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(definitionRequest);
        ArgumentNullException.ThrowIfNull(canonicalLayout);

        var definition = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            definitionRequest);
        if (!definition.Succeeded ||
            definition.BlockTableRecordId is not ObjectId blockId ||
            blockId.IsNull)
        {
            return false;
        }

        try
        {
            leader.UpgradeOpen();
            leader.ContentType = ContentType.BlockContent;
            leader.BlockContentId = blockId;
            leader.BlockConnectionType = BlockConnectionType.ConnectBase;
            leader.BlockScale = new Scale3d(blockScale);
            ApplyStraightSourceToFrameLeader(leader);
            EnsureSingleLeaderLine(leader);
            ApplyItemNoAttribute(transaction, leader, blockId, definitionRequest);

            if (sourceSync.RequiresCanonicalRebuild)
            {
                ApplyCanonicalLayout(leader, canonicalLayout, physicalAxisRadians);
            }
            else
            {
                // Keep live vertices / BlockPosition / BlockRotation — do not
                // re-orient around the anchor or assign absolute rotation here.
                ReassertStraightLeader(leader);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is AcadException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Source MOVE/STRETCH/ROTATE: same absolute CREATE geometry as
    /// <see cref="Create"/> — OrientAroundAnchor + absolute BlockRotation.
    /// </summary>
    private static void ApplyCanonicalLayout(
        MLeader leader,
        TimberItemLeaderLayout canonicalLayout,
        double physicalAxisRadians)
    {
        var desiredRotation =
            TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
                physicalAxisRadians);
        var oriented = TimberItemLeaderLayoutCalculator.OrientAroundAnchor(
            canonicalLayout,
            desiredRotation);
        var attachment = new Point3d(oriented.AnchorX, oriented.AnchorY, 0d);
        var framePoint = new Point3d(oriented.ContentX, oriented.ContentY, 0d);
        var lineIndex = EnsureSingleLeaderLine(leader);
        ApplyStraightSourceToFrameLeader(leader);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, framePoint);
        leader.SetLeaderLineType(lineIndex, LeaderType.StraightLeader);
        leader.BlockPosition = framePoint;
        ApplyAbsoluteBlockContentOrientation(leader, desiredRotation);
        ReassertStraightLeader(leader);
    }

    private static void ApplyStraightSourceToFrameLeader(MLeader leader)
    {
        leader.EnableDogleg = false;
        leader.EnableLanding = false;
        leader.DoglegLength = 0d;
        leader.LandingGap = 0d;
        leader.ExtendLeaderToText = false;
        leader.LeaderLineType = LeaderType.StraightLeader;
    }

    /// <summary>
    /// Assigns absolute BlockContent orientation. Idempotent: same
    /// <paramref name="desiredRotationRadians"/> converges to the same state.
    /// Must not call <see cref="Entity.TransformBy"/> — that compounds on refresh.
    /// </summary>
    private static void ApplyAbsoluteBlockContentOrientation(
        MLeader leader,
        double desiredRotationRadians)
    {
        leader.BlockRotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                desiredRotationRadians);
    }

    /// <summary>
    /// Keeps Source→frame straight and preserves absolute <see cref="MLeader.BlockRotation"/>.
    /// Does not rotate the MLeader; leader vertices stay at the absolute oriented positions.
    /// </summary>
    private static void ReassertStraightLeader(MLeader leader)
    {
        var preservedRotation = leader.BlockRotation;
        var lineIndex = EnsureSingleLeaderLine(leader);
        var attachment = leader.GetFirstVertex(lineIndex);
        var block = leader.BlockPosition;
        ApplyStraightSourceToFrameLeader(leader);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, block);
        leader.SetLeaderLineType(lineIndex, LeaderType.StraightLeader);
        leader.BlockPosition = block;
        leader.BlockRotation = preservedRotation;
    }

    private static void ApplyItemNoAttribute(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        AutoCadFramedBlockContentRequest request)
    {
        if (transaction.GetObject(blockId, OpenMode.ForRead, false) is not
            BlockTableRecord block)
        {
            return;
        }

        var baselineHeight =
            TimberFramedBlockContentDefinitionRules.CalculateBaselineItemModelHeightMm(
                request.ItemPaperHeightMm);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not
                AttributeDefinition definition ||
                definition.Constant ||
                !string.Equals(
                    definition.Tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = request.ItemTextForFrameSizing;
            attribute.Height = baselineHeight;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
            return;
        }
    }

    private static int EnsureSingleLeaderLine(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        int leaderIndex;
        if (leaderIndexes.Length == 0)
        {
            leaderIndex = leader.AddLeader();
        }
        else
        {
            leaderIndex = leaderIndexes[0];
            for (var i = 1; i < leaderIndexes.Length; i++)
            {
                leader.RemoveLeader(leaderIndexes[i]);
            }
        }

        var lineIndexes = leader
            .GetLeaderLineIndexes(leaderIndex)
            .Cast<int>()
            .ToArray();
        if (lineIndexes.Length == 0)
        {
            return leader.AddLeaderLine(leaderIndex);
        }

        for (var i = 1; i < lineIndexes.Length; i++)
        {
            leader.RemoveLeaderLine(lineIndexes[i]);
        }

        return lineIndexes[0];
    }
}

internal readonly struct AutoCadStandaloneFramedItemOnlyCreateResult
{
    private AutoCadStandaloneFramedItemOnlyCreateResult(
        bool succeeded,
        ObjectId leaderId,
        string resolvedBlockName,
        string? diagnostic)
    {
        Succeeded = succeeded;
        LeaderId = leaderId;
        ResolvedBlockName = resolvedBlockName;
        Diagnostic = diagnostic;
    }

    public bool Succeeded { get; }
    public ObjectId LeaderId { get; }
    public string ResolvedBlockName { get; }
    public string? Diagnostic { get; }

    public static AutoCadStandaloneFramedItemOnlyCreateResult Ok(
        ObjectId leaderId,
        string resolvedBlockName) =>
        new(true, leaderId, resolvedBlockName, null);

    public static AutoCadStandaloneFramedItemOnlyCreateResult Fail(string? diagnostic) =>
        new(false, ObjectId.Null, string.Empty, diagnostic);
}
