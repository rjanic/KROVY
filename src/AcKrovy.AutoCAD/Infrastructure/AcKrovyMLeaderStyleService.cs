using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AcKrovy.Core.Services;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AcKrovyMLeaderStyleService
{
    public static ObjectId Ensure(
        Database database,
        Transaction transaction) =>
        Ensure(database, transaction, framed: false);

    public static ObjectId EnsureFramed(
        Database database,
        Transaction transaction) =>
        Ensure(database, transaction, framed: true);

    private static ObjectId Ensure(
        Database database,
        Transaction transaction,
        bool framed)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var arrowSymbolId = EnsureNoneArrowBlock(database, transaction);
        var dictionary = (DBDictionary)transaction.GetObject(
            database.MLeaderStyleDictionaryId,
            OpenMode.ForRead);
        var settings = framed
            ? TimberNativeLeaderStyleRules.FramedSettings
            : TimberNativeLeaderStyleRules.Settings;
        if (dictionary.Contains(settings.StyleName))
        {
            var existingId = dictionary.GetAt(settings.StyleName);
            var existing = (MLeaderStyle)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            ApplyStyleProperties(existing, database, arrowSymbolId, framed);
            return existingId;
        }

        var style = new MLeaderStyle();
        ApplyStyleProperties(style, database, arrowSymbolId, framed);
        var styleId = style.PostMLeaderStyleToDb(
            database,
            settings.StyleName);
        transaction.AddNewlyCreatedDBObject(style, true);
        return styleId;
    }

    public static void ApplyInstanceProperties(
        MLeader leader,
        Database database,
        ObjectId styleId,
        ObjectId arrowSymbolId,
        int leaderIndex,
        int leaderLineIndex,
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(database);

        var settings = TimberNativeLeaderStyleRules.Settings;
        leader.Scale = settings.Scale;
        leader.EnableAnnotationScale = settings.UsesAnnotationScale;
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
        leader.ArrowSymbolId = arrowSymbolId;
        leader.ArrowSize = settings.ArrowheadSize;
        leader.EnableLanding = settings.HasHorizontalLanding;
        leader.EnableDogleg = settings.HasHorizontalLanding;
        leader.DoglegLength = settings.LandingDistance;
        leader.ExtendLeaderToText = settings.ExtendsLeaderToText;
        leader.EnableFrameText = false;
        leader.LandingGap = 0d;
        leader.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
        leader.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.LeftLeader);
        leader.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.RightLeader);
        if (TimberNativeLeaderStyleRules.RequiresExplicitDoglegDirection)
        {
            leader.SetDogleg(
                leaderIndex,
                contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                    ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                    : Autodesk.AutoCAD.Geometry.Vector3d.XAxis);
        }
        ApplyTextHeight(leader, settings.TextHeightMm);
    }

    public static void ApplyBlockInstanceProperties(
        MLeader leader,
        Database database,
        ObjectId arrowSymbolId,
        int leaderIndex,
        int leaderLineIndex,
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(database);

        var settings = TimberNativeLeaderStyleRules.Settings;
        leader.Scale = settings.Scale;
        leader.EnableAnnotationScale = settings.UsesAnnotationScale;
        leader.LeaderLineType = LeaderType.SplineLeader;
        leader.LeaderLineColor = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        leader.LeaderLineTypeId = database.ByBlockLinetype;
        leader.LeaderLineWeight = LineWeight.ByBlock;
        leader.SetLeaderLineType(leaderLineIndex, LeaderType.SplineLeader);
        leader.SetLeaderLineColor(
            leaderLineIndex,
            AcColor.FromColorIndex(ColorMethod.ByBlock, 0));
        leader.SetLeaderLineTypeId(leaderLineIndex, database.ByBlockLinetype);
        leader.SetLeaderLineWeight(leaderLineIndex, LineWeight.ByBlock);
        leader.ArrowSymbolId = arrowSymbolId;
        leader.ArrowSize = settings.ArrowheadSize;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.EnableLanding = settings.HasHorizontalLanding;
        leader.EnableDogleg = settings.HasHorizontalLanding;
        leader.DoglegLength = settings.LandingDistance;
        leader.ExtendLeaderToText = settings.ExtendsLeaderToText;
        leader.LandingGap = 0d;
        leader.SetDogleg(
            leaderIndex,
            contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                : Autodesk.AutoCAD.Geometry.Vector3d.XAxis);
    }

    public static ObjectId GetNoneArrowBlockId(
        Database database,
        Transaction transaction) =>
        EnsureNoneArrowBlock(database, transaction);

    private static void ApplyStyleProperties(
        MLeaderStyle style,
        Database database,
        ObjectId arrowSymbolId,
        bool framed)
    {
        var settings = TimberNativeLeaderStyleRules.Settings;
        style.ContentType = framed ? ContentType.BlockContent : ContentType.MTextContent;
        style.LeaderLineType = framed ? LeaderType.SplineLeader : LeaderType.StraightLeader;
        style.LeaderLineColor = AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
        style.LeaderLineTypeId = database.ByBlockLinetype;
        style.LeaderLineWeight = LineWeight.ByBlock;
        style.TextHeight = settings.TextHeightMm;
        style.Scale = settings.Scale;
        // AutoCAD has no 40-degree enum constraint. The framed style therefore
        // permits the explicitly calculated 40-degree vertex geometry.
        style.FirstSegmentAngleConstraint = framed
            ? AngleConstraint.DegreesAny
            : AngleConstraint.Degrees060;
        style.ArrowSymbolId = arrowSymbolId;
        style.ArrowSize = settings.ArrowheadSize;
        style.EnableLanding = settings.HasHorizontalLanding;
        style.EnableDogleg = settings.HasHorizontalLanding;
        style.DoglegLength = settings.LandingDistance;
        style.ExtendLeaderToText = settings.ExtendsLeaderToText;
        style.EnableFrameText = false;
        style.LandingGap = 0d;
        style.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
        style.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.LeftLeader);
        style.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.RightLeader);
    }

    private static void ApplyTextHeight(MLeader leader, double textHeightMm)
    {
        var content = leader.MText;
        if (content is not null)
        {
            content.TextHeight = textHeightMm;
            leader.MText = content;
        }

        leader.TextHeight = textHeightMm;
    }

    private static ObjectId EnsureNoneArrowBlock(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var blockName = TimberNativeLeaderStyleRules.Settings.NoneArrowBlockName;
        if (blockTable.Has(blockName))
        {
            return blockTable[blockName];
        }

        blockTable.UpgradeOpen();
        var definition = new BlockTableRecord
        {
            Name = blockName,
            Origin = Autodesk.AutoCAD.Geometry.Point3d.Origin,
        };
        var definitionId = blockTable.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);
        return definitionId;
    }
}
