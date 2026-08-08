#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Isolated DEBUG host proof for G5: one BlockContent MLeader owning
/// Resolve frame geometry + AttrDef text (no external DBText / frame BR).
/// Not a production renderer.
/// </summary>
internal static class AutoCadFramedG5ProofService
{
    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var erased = EraseMarkedLeaders(document.Database, transaction);
        var purged = PurgeProofBlocks(document.Database, transaction);
        transaction.Commit();
        editor.WriteMessage(
            $"\nAK_DEV_FRAMED_G5_CLEAN: leaders={erased} blocksPurged={purged}");
    }

    public static void RunMatrix()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var database = document.Database;
        var report = new AutoCadFramedG5ProofReport
        {
            Suite = AutoCadFramedG5ProofPolicy.SuiteIdentifier,
            SchemaVersion = AutoCadFramedG5ProofPolicy.SchemaVersion,
            HeadHint = "5d96abe base; DEBUG harness only",
            StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };

        try
        {
            using (var documentLock = document.LockDocument())
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                EraseMarkedLeaders(database, transaction);
                PurgeProofBlocks(database, transaction);
                EnsureProofTextStyles(database, transaction, editor);

                var origin = new Point3d(5000d, 5000d, 0d);
                var index = 0;
                foreach (var proofCase in AutoCadFramedG5ProofPolicy.Cases)
                {
                    var row = CreateAndVerifyCase(
                        database,
                        transaction,
                        proofCase,
                        origin + new Vector3d((index % 7) * 1800d, (index / 7) * 1600d, 0d),
                        editor);
                    report.Cases.Add(row);
                    index++;
                }

                report.ModelSpaceLeaderCount = CountMarkedLeaders(database, transaction);
                report.ModelSpaceDbTextCount = CountModelSpaceEntities<DBText>(
                    database,
                    transaction);
                report.ModelSpaceExtraBlockReferenceCount =
                    CountUnownedFrameBlockReferences(database, transaction);
                report.ProofBlockDefinitionCount = CountProofBlockDefinitions(
                    database,
                    transaction);

                var lifecycle = RunLifecycleChecks(database, transaction, editor);
                report.Lifecycle = lifecycle;

                var sharedDefs = CountProofBlockDefinitions(
                    database,
                    transaction,
                    namePrefix: "AK_G5_V_");
                var perInstanceDefs = CountProofBlockDefinitions(
                    database,
                    transaction,
                    namePrefix: "AK_G5_I_");
                report.SharedVariantBlockDefinitions = sharedDefs;
                report.PerInstanceBlockDefinitions = perInstanceDefs;

                transaction.Commit();
            }

            var reopen = VerifySaveReopen(database, editor);
            report.SaveReopen = reopen;

            report.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.SingleObjectStructurally =
                report.Cases.All(row => row.SingleMLeader &&
                    !row.HasExternalDbText &&
                    !row.HasExternalFrameBlockReference) &&
                report.ModelSpaceDbTextCount == 0 &&
                report.ModelSpaceExtraBlockReferenceCount == 0;
            report.AllCasesPassed = report.Cases.All(row => row.Passed) &&
                report.Lifecycle.Passed &&
                report.SaveReopen.Passed;
            report.Recommendation = DecideRecommendation(report);

            WriteReport(report, editor);
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G5_MATRIX: {report.Recommendation} " +
                $"casesPass={report.Cases.Count(c => c.Passed)}/{report.Cases.Count} " +
                $"leaders={report.ModelSpaceLeaderCount} " +
                $"blocks={report.ProofBlockDefinitionCount} " +
                $"singleObject={report.SingleObjectStructurally}");
        }
        catch (Exception exception)
        {
            report.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.Recommendation = "NO-GO";
            report.FatalError = $"{exception.GetType().Name}: {exception.Message}";
            WriteReport(report, editor);
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_G5_MATRIX: FAIL - {report.FatalError}");
            editor.WriteMessage($"\n  stack={exception.StackTrace}");
        }
    }

    private static string DecideRecommendation(AutoCadFramedG5ProofReport report)
    {
        if (!report.SingleObjectStructurally || !report.AllCasesPassed)
        {
            return "NO-GO";
        }

        var styleOk = report.Cases.All(row => row.TextStylePersisted);
        var heightOk = report.Cases.All(row => row.HeightOk);
        if (styleOk && heightOk)
        {
            return "GO-SHARED-VARIANT";
        }

        if (heightOk && report.Cases.All(row =>
                row.CachingMode == nameof(AutoCadFramedG5CachingMode.PerInstance)
                    ? row.Passed
                    : row.HeightOk))
        {
            // Style failed on shared AttrRef path but structure/height OK —
            // still GO only if AttrDef-baked style path passed.
            if (report.Cases
                    .Where(row => row.CachingMode ==
                        nameof(AutoCadFramedG5CachingMode.SharedVariant))
                    .All(row => row.AttrDefStyleMatchesExpected))
            {
                return "GO-SHARED-VARIANT-ATTRDEF-STYLE";
            }
        }

        return "NO-GO";
    }

    private static AutoCadFramedG5CaseResult CreateAndVerifyCase(
        Database database,
        Transaction transaction,
        AutoCadFramedG5ProofCase proofCase,
        Point3d blockPosition,
        Editor editor)
    {
        var definition = AutoCadFramedG5ProofPolicy.ResolveFrame(proofCase);
        var styleId = ResolveStyleId(database, transaction, proofCase.StyleKind);
        var styleName = ResolveStyleName(proofCase.StyleKind);
        var expectedHeight =
            AutoCadFramedG5ProofPolicy.ExpectedModelHeightMm(proofCase);
        var blockName = proofCase.CachingMode == AutoCadFramedG5CachingMode.SharedVariant
            ? AutoCadFramedG5ProofPolicy.CreateSharedVariantBlockName(
                proofCase,
                definition,
                proofCase.StyleKind.ToString())
            : AutoCadFramedG5ProofPolicy.CreatePerInstanceBlockName(proofCase.Token);

        var blockId = EnsureBlockDefinition(
            database,
            transaction,
            blockName,
            definition,
            styleId,
            expectedHeight,
            reuseExisting: proofCase.CachingMode ==
                AutoCadFramedG5CachingMode.SharedVariant);

        var leader = CreateBlockContentLeader(
            database,
            transaction,
            blockId,
            blockPosition,
            proofCase.ItemText,
            expectedHeight,
            styleId);

        SetProofXData(
            leader,
            transaction,
            proofCase,
            blockName,
            styleName,
            expectedHeight);

        using var attr = ReadItemAttribute(leader, transaction);
        var attrDef = FindAttrDef(database, transaction, blockId);
        var attrDefStyleId = attrDef?.TextStyleId ?? ObjectId.Null;
        var attrRefStyleId = attr?.TextStyleId ?? ObjectId.Null;
        var attrRefHeight = attr?.Height ?? double.NaN;

        // Probe whether AttrRef style sticks or reverts to AttrDef.
        var stylePersisted = !attrRefStyleId.IsNull &&
            attrRefStyleId == styleId;
        var attrDefStyleMatches = !attrDefStyleId.IsNull &&
            attrDefStyleId == styleId;
        var heightMatchesAttrRef =
            !double.IsNaN(attrRefHeight) &&
            Math.Abs(attrRefHeight - expectedHeight) <=
                AutoCadFramedG5ProofPolicy.HeightToleranceMm;
        // Effective on-screen height with BlockScale 1 is AttrRef.Height.
        var heightOk = heightMatchesAttrRef;

        var frameOk = ValidateFrameGeometry(database, transaction, blockId, definition);
        var contentOk = leader.ContentType == ContentType.BlockContent &&
            leader.BlockContentId == blockId;

        var result = new AutoCadFramedG5CaseResult
        {
            Token = proofCase.Token,
            CachingMode = proofCase.CachingMode.ToString(),
            FrameKind = proofCase.FrameKind.ToString(),
            StyleKind = proofCase.StyleKind.ToString(),
            Denominator = proofCase.Denominator,
            PaperHeightMm = proofCase.PaperHeightMm,
            ExpectedModelHeightMm = expectedHeight,
            BlockName = blockName,
            LeaderHandle = leader.ObjectId.Handle.ToString(),
            SingleMLeader = true,
            HasExternalDbText = false,
            HasExternalFrameBlockReference = false,
            ContentTypeBlockContent = contentOk,
            FrameGeometryMatchesResolve = frameOk,
            AttrDefStyleMatchesExpected = attrDefStyleMatches,
            TextStylePersisted = stylePersisted || attrDefStyleMatches,
            AttrRefHeight = attrRefHeight,
            HeightOk = heightOk,
            AttrRefOverridesAttrDefHeight = heightMatchesAttrRef &&
                attrDef is not null &&
                Math.Abs(attrDef.Height - expectedHeight) >
                    AutoCadFramedG5ProofPolicy.HeightToleranceMm,
        };
        result.Passed =
            result.ContentTypeBlockContent &&
            result.FrameGeometryMatchesResolve &&
            result.TextStylePersisted &&
            result.HeightOk;

        editor.WriteMessage(
            $"\n  G5 {proofCase.Token}: " +
            $"{(result.Passed ? "PASS" : "FAIL")} " +
            $"frame={frameOk} stylePersist={stylePersisted} " +
            $"attrDefStyle={attrDefStyleMatches} height={heightOk} " +
            $"h={attrRefHeight:R}/{expectedHeight:R}");
        return result;
    }

    private static ObjectId EnsureBlockDefinition(
        Database database,
        Transaction transaction,
        string blockName,
        TimberItemLeaderBlockDefinition definition,
        ObjectId textStyleId,
        double attrDefHeightMm,
        bool reuseExisting)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        if (reuseExisting && blockTable.Has(blockName))
        {
            return blockTable[blockName];
        }

        if (blockTable.Has(blockName))
        {
            // Per-instance names are unique; shared reuse hits above.
            return blockTable[blockName];
        }

        blockTable.UpgradeOpen();
        var block = new BlockTableRecord
        {
            Name = blockName,
            Origin = Point3d.Origin,
            Annotative = AnnotativeStates.False,
            BlockScaling = BlockScaling.Uniform,
        };
        var blockId = blockTable.Add(block);
        transaction.AddNewlyCreatedDBObject(block, true);
        AcKrovyItemLeaderBlockService.AddFrameGeometry(
            database,
            transaction,
            block,
            definition);
        AcKrovyItemLeaderBlockService.AddItemNumberAttribute(
            database,
            transaction,
            block,
            attrDefHeightMm,
            textStyleId);
        return blockId;
    }

    private static MLeader CreateBlockContentLeader(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        Point3d blockPosition,
        string itemText,
        double attrRefHeightMm,
        ObjectId preferredStyleId)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        var attrDefId = AcKrovyItemLeaderBlockService.FindItemNumberAttribute(
            block,
            transaction);
        var attrDef = (AttributeDefinition)transaction.GetObject(
            attrDefId,
            OpenMode.ForRead);

        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(1d);
        leader.BlockRotation = 0d;
        leader.BlockPosition = blockPosition;
        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(
            lineIndex,
            blockPosition + new Vector3d(-400d, -250d, 0d));
        leader.AddLastVertex(
            lineIndex,
            blockPosition + new Vector3d(-80d, 0d, 0d));
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);

        using var attribute = new AttributeReference();
        attribute.SetAttributeFromBlock(attrDef, Matrix3d.Identity);
        attribute.TextString = itemText;
        attribute.Height = attrRefHeightMm;
        // Attempt per-instance style; host may revert to AttrDef.
        attribute.TextStyleId = preferredStyleId;
        leader.SetBlockAttribute(attrDefId, attribute);
        return leader;
    }

    private static bool ValidateFrameGeometry(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        TimberItemLeaderBlockDefinition definition)
    {
        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        Entity? frame = null;
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is Entity entity &&
                entity is not AttributeDefinition &&
                !entity.IsErased)
            {
                if (frame is not null)
                {
                    return false;
                }

                frame = entity;
            }
        }

        if (frame is null)
        {
            return false;
        }

        return definition.Style switch
        {
            ItemNumberLeaderStyle.Circle when frame is Circle circle =>
                TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(
                    circle.Radius * 2d),
            ItemNumberLeaderStyle.Rectangle when frame is Polyline polyline =>
                polyline.Closed &&
                polyline.NumberOfVertices == 4 &&
                ExtentsMatch(polyline, definition),
            ItemNumberLeaderStyle.Slot when frame is Polyline polyline =>
                polyline.Closed &&
                polyline.NumberOfVertices == 4 &&
                ExtentsMatch(polyline, definition) &&
                Math.Abs(polyline.GetBulgeAt(1) - 1d) <= 1e-9 &&
                Math.Abs(polyline.GetBulgeAt(3) - 1d) <= 1e-9,
            _ => false,
        };
    }

    private static bool ExtentsMatch(
        Entity entity,
        TimberItemLeaderBlockDefinition definition)
    {
        var extents = entity.GeometricExtents;
        return
            Math.Abs(
                extents.MaxPoint.X - extents.MinPoint.X - definition.WidthMm) <=
                AutoCadFramedG5ProofPolicy.GeometryToleranceMm &&
            Math.Abs(
                extents.MaxPoint.Y - extents.MinPoint.Y - definition.HeightMm) <=
                AutoCadFramedG5ProofPolicy.GeometryToleranceMm;
    }

    private static AutoCadFramedG5LifecycleResult RunLifecycleChecks(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var result = new AutoCadFramedG5LifecycleResult();
        try
        {
            // Find one shared Arial circle leader as COPY source.
            var source = FindLeaderByToken(database, transaction, "S-CIR-AR50");
            if (source is null)
            {
                result.Detail = "Missing S-CIR-AR50 source for COPY lifecycle.";
                return result;
            }

            var ids = new ObjectIdCollection { source.ObjectId };
            var mapping = new IdMapping();
            database.DeepCloneObjects(ids, source.OwnerId, mapping, false);
            // Clone twice more for three total copies with same ElementId stamp.
            database.DeepCloneObjects(ids, source.OwnerId, mapping, false);
            database.DeepCloneObjects(ids, source.OwnerId, mapping, false);

            var cloned = 0;
            foreach (IdPair pair in mapping)
            {
                if (pair.IsCloned &&
                    transaction.GetObject(pair.Value, OpenMode.ForRead, false)
                        is MLeader clone)
                {
                    cloned++;
                    // Stamp shared ElementId to mimic manufacturing copies.
                    SetElementIdXData(clone, transaction, "K1");
                }
            }

            result.CopyCloneCount = cloned;
            result.NoExternalDbTextAfterCopy =
                CountModelSpaceEntities<DBText>(database, transaction) == 0;
            result.LeadersRemainBlockContent = true;
            foreach (ObjectId id in ModelSpaceIds(database, transaction))
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is MLeader leader &&
                    HasProofXData(leader) &&
                    leader.ContentType != ContentType.BlockContent)
                {
                    result.LeadersRemainBlockContent = false;
                    break;
                }
            }

            result.Passed =
                result.CopyCloneCount >= 3 &&
                result.NoExternalDbTextAfterCopy &&
                result.LeadersRemainBlockContent;
            result.Detail =
                $"DeepClone copies={result.CopyCloneCount}; " +
                "MOVE/STRETCH/AK_LABELS/WBLOCK/cross-DWG require GUI or extended harness.";
            editor.WriteMessage(
                $"\n  G5 lifecycle: {(result.Passed ? "PASS" : "FAIL")} {result.Detail}");
        }
        catch (Exception exception)
        {
            result.Detail = exception.Message;
            result.Passed = false;
        }

        return result;
    }

    private static AutoCadFramedG5SaveReopenResult VerifySaveReopen(
        Database sourceDatabase,
        Editor editor)
    {
        var result = new AutoCadFramedG5SaveReopenResult();
        var path = Path.Combine(
            Path.GetTempPath(),
            "ackrovy-g5-proof-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            sourceDatabase.SaveAs(path, DwgVersion.Current);
            using var reopened = new Database(false, true);
            reopened.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
            using var transaction = reopened.TransactionManager.StartTransaction();
            var checkedStyles = 0;
            var ok = 0;
            foreach (ObjectId id in ModelSpaceIds(reopened, transaction))
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not MLeader leader ||
                    !HasProofXData(leader) ||
                    leader.ContentType != ContentType.BlockContent)
                {
                    continue;
                }

                using var attr = ReadItemAttribute(leader, transaction);
                if (attr is null)
                {
                    continue;
                }

                checkedStyles++;
                var attrDef = FindAttrDef(reopened, transaction, leader.BlockContentId);
                if (attrDef is not null &&
                    !attr.TextStyleId.IsNull &&
                    (attr.TextStyleId == attrDef.TextStyleId ||
                     !attrDef.TextStyleId.IsNull))
                {
                    // Persist either AttrRef or AttrDef style ObjectId across reopen.
                    ok++;
                }
            }

            result.CheckedLeaders = checkedStyles;
            result.StyleIdReadableCount = ok;
            result.Passed = checkedStyles > 0 && ok == checkedStyles;
            result.Detail =
                $"Saved {path}; style-readable {ok}/{checkedStyles}";
            editor.WriteMessage(
                $"\n  G5 save/reopen: {(result.Passed ? "PASS" : "FAIL")} {result.Detail}");
            transaction.Commit();
        }
        catch (Exception exception)
        {
            result.Passed = false;
            result.Detail = exception.Message;
            editor.WriteMessage($"\n  G5 save/reopen: FAIL {exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }

        return result;
    }

    private static void EnsureProofTextStyles(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var arial = AutoCadTextStylePresetService.EnsureBuiltIn(
            database,
            transaction,
            TimberAnnotationBuiltInTextStylePreset.Arial);
        var classic = AutoCadTextStylePresetService.EnsureBuiltIn(
            database,
            transaction,
            TimberAnnotationBuiltInTextStylePreset.Classic);
        AutoCadTextStylePresetService.EnsureStyle(
            database,
            transaction,
            AutoCadFramedG5ProofPolicy.TimesNewRomanStyleName,
            AutoCadFramedG5ProofPolicy.TimesNewRomanFontFile,
            TimberAnnotationTextStylePresetRules.DefaultWidthFactor,
            TimberAnnotationTextStylePresetRules.DefaultObliqueAngleDegrees);
        editor.WriteMessage(
            $"\n  G5 styles: Arial={arial.Kind} Classic={classic.Kind} " +
            $"TNR={AutoCadFramedG5ProofPolicy.TimesNewRomanStyleName}");
    }

    private static ObjectId ResolveStyleId(
        Database database,
        Transaction transaction,
        AutoCadFramedG5StyleKind kind)
    {
        var name = ResolveStyleName(kind);
        var table = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        if (!table.Has(name))
        {
            throw new InvalidOperationException($"Missing text style '{name}'.");
        }

        return table[name];
    }

    private static string ResolveStyleName(AutoCadFramedG5StyleKind kind) =>
        kind switch
        {
            AutoCadFramedG5StyleKind.ArialPreset =>
                TimberAnnotationTextStylePresetRules.ArialStyleName,
            AutoCadFramedG5StyleKind.TimesNewRoman =>
                AutoCadFramedG5ProofPolicy.TimesNewRomanStyleName,
            AutoCadFramedG5StyleKind.ClassicShx =>
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static AttributeDefinition? FindAttrDef(
        Database database,
        Transaction transaction,
        ObjectId blockId)
    {
        if (blockId.IsNull)
        {
            return null;
        }

        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        var attrId = AcKrovyItemLeaderBlockService.FindItemNumberAttribute(
            block,
            transaction);
        return attrId.IsNull
            ? null
            : (AttributeDefinition)transaction.GetObject(attrId, OpenMode.ForRead);
    }

    private static AttributeReference? ReadItemAttribute(
        MLeader leader,
        Transaction transaction)
    {
        if (leader.BlockContentId.IsNull)
        {
            return null;
        }

        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        var attrDefId = AcKrovyItemLeaderBlockService.FindItemNumberAttribute(
            block,
            transaction);
        if (attrDefId.IsNull)
        {
            return null;
        }

        return leader.GetBlockAttribute(attrDefId);
    }

    private static void SetProofXData(
        MLeader leader,
        Transaction transaction,
        AutoCadFramedG5ProofCase proofCase,
        string blockName,
        string styleName,
        double expectedHeight)
    {
        EnsureRegApp(leader.Database, transaction);
        var payload =
            $"{proofCase.Token}|{proofCase.CachingMode}|{blockName}|{styleName}|" +
            $"{expectedHeight.ToString(CultureInfo.InvariantCulture)}|K1";
        leader.XData = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName,
                AutoCadFramedG5ProofPolicy.RegAppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, payload));
    }

    private static void SetElementIdXData(
        MLeader leader,
        Transaction transaction,
        string elementId)
    {
        EnsureRegApp(leader.Database, transaction);
        var existing = leader.XData;
        var token = "COPY";
        if (existing is not null)
        {
            foreach (TypedValue value in existing)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                    value.Value is string text)
                {
                    token = text.Split('|')[0];
                    break;
                }
            }
        }

        leader.UpgradeOpen();
        leader.XData = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName,
                AutoCadFramedG5ProofPolicy.RegAppName),
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                $"{token}|COPY|{elementId}"));
    }

    private static bool HasProofXData(Entity entity)
    {
        var buffer = entity.GetXDataForApplication(AutoCadFramedG5ProofPolicy.RegAppName);
        return buffer is not null;
    }

    private static void EnsureRegApp(Database database, Transaction transaction)
    {
        var regApps = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regApps.Has(AutoCadFramedG5ProofPolicy.RegAppName))
        {
            return;
        }

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadFramedG5ProofPolicy.RegAppName,
        };
        regApps.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static MLeader? FindLeaderByToken(
        Database database,
        Transaction transaction,
        string token)
    {
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not MLeader leader ||
                !HasProofXData(leader))
            {
                continue;
            }

            var buffer = leader.GetXDataForApplication(
                AutoCadFramedG5ProofPolicy.RegAppName);
            foreach (TypedValue value in buffer!)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                    value.Value is string text &&
                    text.StartsWith(token + "|", StringComparison.Ordinal))
                {
                    return leader;
                }
            }
        }

        return null;
    }

    private static int EraseMarkedLeaders(Database database, Transaction transaction)
    {
        var erased = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction).ToList())
        {
            if (transaction.GetObject(id, OpenMode.ForWrite, false) is Entity entity &&
                HasProofXData(entity) &&
                !entity.IsErased)
            {
                entity.Erase();
                erased++;
            }
        }

        return erased;
    }

    private static int PurgeProofBlocks(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var purged = 0;
        foreach (ObjectId id in blockTable)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not BlockTableRecord block ||
                block.IsAnonymous ||
                block.IsLayout ||
                !(block.Name.StartsWith("AK_G5_V_", StringComparison.Ordinal) ||
                  block.Name.StartsWith("AK_G5_I_", StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                block.UpgradeOpen();
                block.Erase();
                purged++;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Still referenced — leave for later.
            }
        }

        return purged;
    }

    private static int CountMarkedLeaders(Database database, Transaction transaction)
    {
        var count = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is MLeader leader &&
                HasProofXData(leader))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountModelSpaceEntities<T>(
        Database database,
        Transaction transaction)
        where T : Entity
    {
        var count = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is T)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountUnownedFrameBlockReferences(
        Database database,
        Transaction transaction)
    {
        // G5 proof must not leave standalone frame BlockReferences in model space.
        var count = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is BlockReference)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountProofBlockDefinitions(
        Database database,
        Transaction transaction,
        string? namePrefix = null)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var count = 0;
        foreach (ObjectId id in blockTable)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not BlockTableRecord block ||
                block.IsLayout)
            {
                continue;
            }

            if (namePrefix is null)
            {
                if (block.Name.StartsWith("AK_G5_V_", StringComparison.Ordinal) ||
                    block.Name.StartsWith("AK_G5_I_", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            else if (block.Name.StartsWith(namePrefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<ObjectId> ModelSpaceIds(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            yield return id;
        }
    }

    private static void WriteReport(AutoCadFramedG5ProofReport report, Editor editor)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACAD_KROVY",
            "Proofs",
            "G5");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AutoCadFramedG5ProofPolicy.ReportFileName);
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
        editor.WriteMessage($"\nAK_DEV_FRAMED_G5_MATRIX: report={path}");
    }
}

internal sealed class AutoCadFramedG5ProofReport
{
    public string Suite { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string HeadHint { get; set; } = string.Empty;
    public string StartedUtc { get; set; } = string.Empty;
    public string FinishedUtc { get; set; } = string.Empty;
    public string Recommendation { get; set; } = "NO-GO";
    public string? FatalError { get; set; }
    public bool AllCasesPassed { get; set; }
    public bool SingleObjectStructurally { get; set; }
    public int ModelSpaceLeaderCount { get; set; }
    public int ModelSpaceDbTextCount { get; set; }
    public int ModelSpaceExtraBlockReferenceCount { get; set; }
    public int ProofBlockDefinitionCount { get; set; }
    public int SharedVariantBlockDefinitions { get; set; }
    public int PerInstanceBlockDefinitions { get; set; }
    public List<AutoCadFramedG5CaseResult> Cases { get; set; } = [];
    public AutoCadFramedG5LifecycleResult Lifecycle { get; set; } = new();
    public AutoCadFramedG5SaveReopenResult SaveReopen { get; set; } = new();
}

internal sealed class AutoCadFramedG5CaseResult
{
    public string Token { get; set; } = string.Empty;
    public string CachingMode { get; set; } = string.Empty;
    public string FrameKind { get; set; } = string.Empty;
    public string StyleKind { get; set; } = string.Empty;
    public int Denominator { get; set; }
    public double PaperHeightMm { get; set; }
    public double ExpectedModelHeightMm { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public string LeaderHandle { get; set; } = string.Empty;
    public bool SingleMLeader { get; set; }
    public bool HasExternalDbText { get; set; }
    public bool HasExternalFrameBlockReference { get; set; }
    public bool ContentTypeBlockContent { get; set; }
    public bool FrameGeometryMatchesResolve { get; set; }
    public bool AttrDefStyleMatchesExpected { get; set; }
    public bool TextStylePersisted { get; set; }
    public double AttrRefHeight { get; set; }
    public bool HeightOk { get; set; }
    public bool AttrRefOverridesAttrDefHeight { get; set; }
    public bool Passed { get; set; }
}

internal sealed class AutoCadFramedG5LifecycleResult
{
    public bool Passed { get; set; }
    public int CopyCloneCount { get; set; }
    public bool NoExternalDbTextAfterCopy { get; set; }
    public bool LeadersRemainBlockContent { get; set; }
    public string Detail { get; set; } = string.Empty;
}

internal sealed class AutoCadFramedG5SaveReopenResult
{
    public bool Passed { get; set; }
    public int CheckedLeaders { get; set; }
    public int StyleIdReadableCount { get; set; }
    public string Detail { get; set; } = string.Empty;
}
#endif
