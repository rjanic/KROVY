using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using AcKrovy.Core.Services;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AcKrovyMLeaderStyleService
{
    public static ObjectId Ensure(
        Database database,
        Transaction transaction,
        bool updateExisting = true) =>
        Ensure(database, transaction, framed: false, updateExisting);

    public static ObjectId EnsureFramed(
        Database database,
        Transaction transaction,
        bool updateExisting = true) =>
        Ensure(database, transaction, framed: true, updateExisting);

    public static ObjectId EnsureCombinedFramed(
        Database database,
        Transaction transaction,
        bool updateExisting = true) =>
        Ensure(
            database,
            transaction,
            framed: true,
            updateExisting,
            combinedFramed: true);

    private static ObjectId Ensure(
        Database database,
        Transaction transaction,
        bool framed,
        bool updateExisting,
        bool combinedFramed = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var arrowSymbolId = combinedFramed
            ? ObjectId.Null
            : EnsureNoneArrowBlock(database, transaction);
        var dictionary = (DBDictionary)transaction.GetObject(
            database.MLeaderStyleDictionaryId,
            OpenMode.ForRead);
        var settings = combinedFramed
            ? TimberNativeLeaderStyleRules.CombinedFramedSettings
            : framed
                ? TimberNativeLeaderStyleRules.FramedSettings
                : TimberNativeLeaderStyleRules.Settings;
        if (dictionary.Contains(settings.StyleName))
        {
            var existingId = dictionary.GetAt(settings.StyleName);
            if (!updateExisting)
            {
                return existingId;
            }

            var existing = (MLeaderStyle)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            ApplyStyleProperties(
                existing,
                database,
                arrowSymbolId,
                framed,
                combinedFramed);
            return existingId;
        }

        var style = new MLeaderStyle();
        ApplyStyleProperties(
            style,
            database,
            arrowSymbolId,
            framed,
            combinedFramed);
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
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide,
        double textHeightMm,
        double presentationScaleFactor,
        ObjectId? resolvedTextStyleId = null)
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
        leader.ArrowSize = settings.ArrowheadSize * presentationScaleFactor;
        leader.EnableLanding = settings.HasHorizontalLanding;
        leader.EnableDogleg = settings.HasHorizontalLanding;
        leader.DoglegLength = settings.LandingDistance * presentationScaleFactor;
        leader.ExtendLeaderToText = settings.ExtendsLeaderToText;
        leader.EnableFrameText = false;
        leader.LandingGap = 0d * presentationScaleFactor;
        leader.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
        leader.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.LeftLeader);
        leader.SetTextAttachmentType(
            TextAttachmentType.AttachmentBottomLine,
            LeaderDirectionType.RightLeader);
        leader.TextStyleId = resolvedTextStyleId ?? database.Textstyle;
        if (TimberNativeLeaderStyleRules.RequiresExplicitDoglegDirection)
        {
            leader.SetDogleg(
                leaderIndex,
                contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                    ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                    : Autodesk.AutoCAD.Geometry.Vector3d.XAxis);
        }
        ApplyTextHeight(leader, textHeightMm);
    }

    public static void ApplyBlockInstanceProperties(
        MLeader leader,
        Database database,
        ObjectId arrowSymbolId,
        int leaderIndex,
        int leaderLineIndex,
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide,
        double presentationScaleFactor,
        Autodesk.AutoCAD.Geometry.Vector3d? doglegDirectionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(database);

        var settings = TimberNativeLeaderStyleRules.FramedSettings;
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
        leader.ArrowSize = settings.ArrowheadSize * presentationScaleFactor;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.EnableLanding = settings.HasHorizontalLanding;
        leader.EnableDogleg = settings.HasHorizontalLanding;
        leader.DoglegLength = settings.LandingDistance * presentationScaleFactor;
        leader.ExtendLeaderToText = settings.ExtendsLeaderToText;
        leader.LandingGap = 0d * presentationScaleFactor;
        if (settings.LandingDistance > 0d)
        {
            leader.SetDogleg(
                leaderIndex,
                doglegDirectionOverride ??
                    (contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                        ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                        : Autodesk.AutoCAD.Geometry.Vector3d.XAxis));
        }
    }

    public static void ApplyCombinedBlockInstanceProperties(
        MLeader leader,
        Database database,
        int leaderIndex,
        int leaderLineIndex,
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide,
        double presentationScaleFactor,
        Autodesk.AutoCAD.Geometry.Vector3d? doglegDirectionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(database);

        var settings = TimberNativeLeaderStyleRules.CombinedFramedSettings;
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
        leader.ArrowSymbolId = ObjectId.Null;
        leader.ArrowSize = settings.ArrowheadSize * presentationScaleFactor;
        leader.BlockConnectionType = BlockConnectionType.ConnectExtents;
        leader.EnableLanding = settings.HasHorizontalLanding;
        leader.EnableDogleg = settings.HasHorizontalLanding;
        leader.DoglegLength = settings.LandingDistance * presentationScaleFactor;
        leader.ExtendLeaderToText = settings.ExtendsLeaderToText;
        leader.LandingGap = 0d * presentationScaleFactor;
        leader.SetDogleg(
            leaderIndex,
            doglegDirectionOverride ??
                (contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                    ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                    : Autodesk.AutoCAD.Geometry.Vector3d.XAxis));
    }

    public static ObjectId GetNoneArrowBlockId(
        Database database,
        Transaction transaction) =>
        EnsureNoneArrowBlock(database, transaction);

    private static void ApplyStyleProperties(
        MLeaderStyle style,
        Database database,
        ObjectId arrowSymbolId,
        bool framed,
        bool combinedFramed)
    {
        var settings = combinedFramed
            ? TimberNativeLeaderStyleRules.CombinedFramedSettings
            : framed
                ? TimberNativeLeaderStyleRules.FramedSettings
                : TimberNativeLeaderStyleRules.Settings;
        style.ContentType = framed ? ContentType.BlockContent : ContentType.MTextContent;
        style.LeaderLineType = combinedFramed || !framed
            ? LeaderType.StraightLeader
            : LeaderType.SplineLeader;
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
