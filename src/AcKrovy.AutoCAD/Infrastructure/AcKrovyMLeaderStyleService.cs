using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Services;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using System.Globalization;
using System.Linq;
using System.Text;

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
        ObjectId? resolvedTextStyleId = null,
        double? doglegLengthOverride = null,
        Autodesk.AutoCAD.Geometry.Vector3d? doglegDirectionOverride = null)
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
        var doglegLength = doglegLengthOverride ??
            settings.LandingDistance * presentationScaleFactor;
        var fallbackDirection =
            contentSide == AcKrovy.Core.Models.TimberLeaderHorizontalSide.Left
                ? -Autodesk.AutoCAD.Geometry.Vector3d.XAxis
                : Autodesk.AutoCAD.Geometry.Vector3d.XAxis;
        var candidateDirection = doglegDirectionOverride ?? fallbackDirection;
        // Standalone CREATE leaves LandingDistance/overrides at 0 so SetDogleg is
        // not invoked here. Combined still may call SetDogleg with overrides.
        // Guard rejects near-flush / non-finite inputs before the AutoCAD API.
        var canSetDogleg =
            TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                doglegLength,
                candidateDirection.X,
                candidateDirection.Y,
                out var unitX,
                out var unitY);
        leader.EnableDogleg = settings.HasHorizontalLanding && canSetDogleg;
        leader.DoglegLength = canSetDogleg ? doglegLength : 0d;
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
        // Standalone DimensionsOnly CREATE must never SetDogleg here (pre-AppendEntity).
        // Combined still passes doglegLengthOverride and keeps its existing early call.
        // Standalone leaves override null and LandingDistance 0 → canSetDogleg is false.
        if (canSetDogleg &&
            leader.ObjectId.IsNull &&
            doglegLengthOverride is null)
        {
#if DEBUG
            AcKrovyDiagnostics.Info(
                "SetDoglegDeferredPreAppend",
                "Skipped SetDogleg because MLeader.ObjectId is null and no " +
                "combined override; leaderIndex=" +
                leaderIndex.ToString(CultureInfo.InvariantCulture) +
                ";doglegLengthResolved=" +
                FormatDouble(doglegLength));
#endif
            canSetDogleg = false;
            leader.EnableDogleg = false;
            leader.DoglegLength = 0d;
        }

        if (canSetDogleg)
        {
            var doglegVector = new Vector3d(unitX, unitY, 0d);
#if DEBUG
            LogSetDoglegProbe(
                leader,
                leaderIndex,
                leaderLineIndex,
                contentSide,
                doglegLength,
                doglegLengthOverride,
                doglegDirectionOverride,
                doglegVector,
                canSetDogleg);
#endif
            if (!LeaderIndexIsPresent(leader, leaderIndex))
            {
#if DEBUG
                AcKrovyDiagnostics.Warning(
                    "SetDoglegSkippedInvalidLeaderIndex",
                    $"leaderIndex={leaderIndex.ToString(CultureInfo.InvariantCulture)}; " +
                    $"leaders=[{FormatLeaderIndexes(leader)}]");
#endif
            }
            else
            {
                leader.SetDogleg(leaderIndex, doglegVector);
            }
        }
#if DEBUG
        else
        {
            AcKrovyDiagnostics.Info(
                "SetDoglegNotCalled",
                "canSetDogleg=false;doglegLengthResolved=" +
                FormatDouble(doglegLength) +
                ";objectIdIsNull=" +
                leader.ObjectId.IsNull +
                ";override=" +
                (doglegLengthOverride is double skippedOverride
                    ? FormatDouble(skippedOverride)
                    : "<null>"));
        }
#endif
        ApplyTextHeight(leader, textHeightMm);
    }

#if DEBUG
    private static void LogSetDoglegProbe(
        MLeader leader,
        int leaderIndex,
        int leaderLineIndex,
        AcKrovy.Core.Models.TimberLeaderHorizontalSide contentSide,
        double doglegLength,
        double? doglegLengthOverride,
        Vector3d? doglegDirectionOverride,
        Vector3d doglegVector,
        bool canSetDogleg)
    {
        var builder = new StringBuilder(512);
        builder.Append("SetDoglegProbe;");
        builder.Append("leaderIndex=").Append(leaderIndex.ToString(CultureInfo.InvariantCulture)).Append(';');
        builder.Append("leaderLineIndex=").Append(leaderLineIndex.ToString(CultureInfo.InvariantCulture)).Append(';');
        builder.Append("leaders=[").Append(FormatLeaderIndexes(leader)).Append("];");
        builder.Append("lines=[").Append(FormatLeaderLineIndexes(leader, leaderIndex)).Append("];");
        builder.Append("EnableDogleg=").Append(leader.EnableDogleg).Append(';');
        builder.Append("DoglegLength=").Append(FormatDouble(leader.DoglegLength)).Append(';');
        builder.Append("doglegLengthResolved=").Append(FormatDouble(doglegLength)).Append(';');
        builder.Append("doglegLengthOverride=").Append(
            doglegLengthOverride is double overrideLength
                ? FormatDouble(overrideLength)
                : "<null>").Append(';');
        builder.Append("overrideX=").Append(
            doglegDirectionOverride is Vector3d ov ? FormatDouble(ov.X) : "<null>").Append(';');
        builder.Append("overrideY=").Append(
            doglegDirectionOverride is Vector3d ovY ? FormatDouble(ovY.Y) : "<null>").Append(';');
        builder.Append("overrideZ=").Append(
            doglegDirectionOverride is Vector3d ovZ ? FormatDouble(ovZ.Z) : "<null>").Append(';');
        builder.Append("vectorX=").Append(FormatDouble(doglegVector.X)).Append(';');
        builder.Append("vectorY=").Append(FormatDouble(doglegVector.Y)).Append(';');
        builder.Append("vectorZ=").Append(FormatDouble(doglegVector.Z)).Append(';');
        builder.Append("vectorLength=").Append(FormatDouble(doglegVector.Length)).Append(';');
        builder.Append("isFinite=").Append(
            !(double.IsNaN(doglegVector.X) ||
              double.IsNaN(doglegVector.Y) ||
              double.IsNaN(doglegVector.Z) ||
              double.IsInfinity(doglegVector.X) ||
              double.IsInfinity(doglegVector.Y) ||
              double.IsInfinity(doglegVector.Z))).Append(';');
        builder.Append("isNearZero=").Append(
            doglegVector.Length <=
            TimberNativeMLeaderDoglegInputRules.DirectionLengthTolerance).Append(';');
        builder.Append("canSetDogleg=").Append(canSetDogleg).Append(';');
        builder.Append("ContentType=").Append(leader.ContentType).Append(';');
        builder.Append("contentSide=").Append(contentSide).Append(';');
        builder.Append("objectIdIsNull=").Append(leader.ObjectId.IsNull).Append(';');
        builder.Append("TextLocation=(")
            .Append(FormatDouble(leader.TextLocation.X)).Append(',')
            .Append(FormatDouble(leader.TextLocation.Y)).Append(',')
            .Append(FormatDouble(leader.TextLocation.Z)).Append(')');
        if (leader.MText is MText mText)
        {
            builder.Append(";MTextAttachment=").Append(mText.Attachment);
            builder.Append(";MTextRotation=").Append(FormatDouble(mText.Rotation));
            builder.Append(";MTextHeight=").Append(FormatDouble(mText.TextHeight));
        }

        AcKrovyDiagnostics.Info("SetDoglegProbe", builder.ToString());
    }

    private static string FormatDouble(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatLeaderIndexes(MLeader leader)
    {
        try
        {
            return string.Join(
                ",",
                leader.GetLeaderIndexes().Cast<int>()
                    .Select(static index => index.ToString(CultureInfo.InvariantCulture)));
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FormatLeaderLineIndexes(MLeader leader, int leaderIndex)
    {
        try
        {
            return string.Join(
                ",",
                leader.GetLeaderLineIndexes(leaderIndex).Cast<int>()
                    .Select(static index => index.ToString(CultureInfo.InvariantCulture)));
        }
        catch
        {
            return "<unavailable>";
        }
    }
#endif

    private static bool LeaderIndexIsPresent(MLeader leader, int leaderIndex)
    {
        try
        {
            return leader.GetLeaderIndexes().Cast<int>().Any(index => index == leaderIndex);
        }
        catch
        {
            return false;
        }
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
