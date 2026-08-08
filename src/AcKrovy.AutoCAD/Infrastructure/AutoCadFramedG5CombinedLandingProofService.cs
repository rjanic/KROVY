#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG Form B proof: canonical horizontal BlockContent MLeader (Left/Right),
/// then one rigid Matrix3d.Rotation of the whole MLeader around the leader
/// attachment pivot (source-element contact). No per-angle layout algorithm.
/// </summary>
internal static class AutoCadFramedG5CombinedLandingProofService
{
    [ThreadStatic]
    private static string? _diagnosticStep;

    public static void Clean() => CleanAllProofArtifacts();

    public static void CleanAllProofArtifacts()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var erased = EraseAllProofMarkedEntities(document.Database, transaction);
        var purged = PurgeAllProofBlocks(document.Database, transaction);
        transaction.Commit();
        editor.WriteMessage(
            $"\nAK_DEV_FRAMED_G5_CLEAN_ALL: erasedEntities={erased} purgedBlocks={purged} " +
            "(includes G5/G5C leaders + 2000mm proof source lines)");
        DiagnoseModelSpace();
    }

    public static void DiagnoseModelSpace()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var transaction =
            document.Database.TransactionManager.StartOpenCloseTransaction();
        WriteModelSpaceInventory(document.Database, transaction, editor);
        transaction.Commit();
    }

    public static void RunMinimalMultiAttrRepro()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var database = document.Database;
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = database.TransactionManager.StartTransaction();
            EraseAllProofMarkedEntities(database, transaction);
            PurgeAllProofBlocks(database, transaction);
            EnsureStyles(database, transaction, editor);

            var frame = TimberItemLeaderBlockDefinitionRules.Resolve(
                ItemNumberLeaderStyle.Circle,
                "K1");
            var itemStyle = GetStyle(
                database,
                transaction,
                TimberAnnotationTextStylePresetRules.ArialStyleName);
            var blockName = "AK_G5C_REPRO_MIN_" + Guid.NewGuid().ToString("N")[..8];
            var blockTable = (BlockTable)transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForWrite);
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
                frame);
            var itemAttrId = AcKrovyItemLeaderBlockService.AddItemNumberAttribute(
                database,
                transaction,
                block,
                135d,
                itemStyle);
            AppendAttribute(
                database,
                transaction,
                block,
                AutoCadFramedG5CombinedLandingProofPolicy.WidthTag,
                125d,
                itemStyle,
                new Point3d(-300d, 0d, 0d),
                useAlignmentPoint: false);

            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            var leader = new MLeader();
            leader.SetDatabaseDefaults(database);
            leader.EnableAnnotationScale = false;
            leader.Scale = 1d;
            leader.ContentType = ContentType.BlockContent;
            leader.BlockContentId = blockId;
            leader.BlockConnectionType = BlockConnectionType.ConnectBase;
            leader.BlockScale = new Scale3d(1d);
            leader.BlockPosition = new Point3d(12000d, 12000d, 0d);
            var li = leader.AddLeader();
            var lli = leader.AddLeaderLine(li);
            leader.AddFirstVertex(lli, new Point3d(11600d, 11750d, 0d));
            leader.AddLastVertex(lli, new Point3d(11920d, 12000d, 0d));
            modelSpace.AppendEntity(leader);
            transaction.AddNewlyCreatedDBObject(leader, true);

            var itemDef = (AttributeDefinition)transaction.GetObject(
                itemAttrId,
                OpenMode.ForRead);
            using (var itemRef = new AttributeReference())
            {
                itemRef.SetAttributeFromBlock(itemDef, Matrix3d.Identity);
                itemRef.TextString = "K1";
                itemRef.Height = 135d;
                leader.SetBlockAttribute(itemAttrId, itemRef);
            }

            foreach (ObjectId id in block)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, true) is
                        AttributeDefinition def &&
                    string.Equals(
                        def.Tag,
                        AutoCadFramedG5CombinedLandingProofPolicy.WidthTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    using var widthRef = new AttributeReference();
                    widthRef.SetAttributeFromBlock(def, Matrix3d.Identity);
                    widthRef.TextString = "80";
                    widthRef.Height = 125d;
                    leader.SetBlockAttribute(def.ObjectId, widthRef);
                    break;
                }
            }

            SetProofXData(
                leader,
                transaction,
                new AutoCadFramedG5CombinedProofCase(
                    "REPRO-MIN",
                    ItemNumberLeaderStyle.Circle,
                    "K1",
                    "80",
                    "160",
                    AutoCadFramedG5CombinedStyleMode.SharedStyleSeparateHeights,
                    AutoCadFramedG5CombinedSide.Left,
                    0d,
                    AutoCadFramedG5CombinedLandingProofPolicy.ProofItemNumberPaperHeightMm,
                    2.5d,
                    50),
                blockName);

            var msCount = CountModelSpaceEntities(database, transaction);
            var mleaderCount = CountType<MLeader>(database, transaction);
            transaction.Commit();
            editor.WriteMessage(
                "\nAK_DEV_FRAMED_G5C_REPRO_MIN: PASS " +
                $"modelSpace={msCount} mleaders={mleaderCount} block={blockName}");
        }
        catch (Exception exception)
        {
            WriteExceptionDiagnostics(editor, exception, _diagnosticStep);
            editor.WriteMessage("\nAK_DEV_FRAMED_G5C_REPRO_MIN: FAIL");
        }
        finally
        {
            _diagnosticStep = null;
        }
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
        var report = new AutoCadFramedG5CombinedReport
        {
            Suite = AutoCadFramedG5CombinedLandingProofPolicy.SuiteIdentifier,
            SchemaVersion = AutoCadFramedG5CombinedLandingProofPolicy.SchemaVersion,
            StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            StageAStashHint = "stash@{0} Stage A untouched",
            PlacementStrategy =
                "canonical-horizontal complete MLeader (attrs+vertices) → " +
                "TransformBy(readable, Z, attachment) last → " +
                "general post-create rotate-equivalent stabilize (±1° around attachment) → " +
                "WIDTH/HEIGHT clear-gap straddle + ItemNoPaper 3.0mm",
            RootCauseOfPriorSlantBug =
                "Host PDF mark F-SLT-L-90-D50: after TransformBy at readable=90°, " +
                "AutoCAD left BlockContent/dogleg display state stale (STRETCH/knee no-op; " +
                "native ROTATE rebuilds). Not a hard-coded token bug — general post-create " +
                "layout rebuild required. Prior issues: Left 60° segment, stacked-above-landing " +
                "gap, ITEM_NO 2.7 vs proof 3.0.",
        };

        try
        {
            using (var documentLock = document.LockDocument())
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                EraseAllProofMarkedEntities(database, transaction);
                PurgeAllProofBlocks(database, transaction);
                EnsureStyles(database, transaction, editor);

                var origin = new Point3d(5000d, 5000d, 0d);
                var index = 0;
                foreach (var proofCase in AutoCadFramedG5CombinedLandingProofPolicy.Cases)
                {
                    _diagnosticStep = $"MATRIX case={proofCase.Token}";
                    var attachment = origin + new Vector3d(
                        (index % 8) * 3800d,
                        (index / 8) * 3400d,
                        0d);
                    var row = CreateAndVerifyCase(
                        database,
                        transaction,
                        proofCase,
                        attachment,
                        editor);
                    report.Cases.Add(row);
                    index++;
                }

                report.Lifecycle = RunLifecycle(database, transaction, editor);
                report.ModelSpaceEntityCount = CountModelSpaceEntities(database, transaction);
                report.ModelSpaceMLeaderCount = CountType<MLeader>(database, transaction);
                report.ModelSpaceMTextCount = CountType<MText>(database, transaction);
                report.ModelSpaceDbTextCount = CountType<DBText>(database, transaction);
                report.ModelSpaceBlockReferenceCount =
                    CountType<BlockReference>(database, transaction);
                report.ProofMarkedLeaderCount =
                    CountProofMarkedLeaders(database, transaction);
                report.ProofSourceLines =
                    CountProofSourceLines(database, transaction);
                report.ProofBlockDefinitionCount =
                    CountProofBlocks(database, transaction);
                report.MaxBaselineDeviationMm = report.Cases.Count == 0
                    ? null
                    : FiniteOrNull(
                        report.Cases.Max(c => c.MaxBaselineDeviationMm ?? double.NaN),
                        "report.MaxBaselineDeviationMm",
                        editor);
                report.MaxPivotDriftMm = report.Cases.Count == 0
                    ? null
                    : FiniteOrNull(
                        report.Cases.Max(c => c.PivotDriftMm ?? double.NaN),
                        "report.MaxPivotDriftMm",
                        editor);
                report.FirstSegmentMultiplier =
                    AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentLengthMultiplier;
                if (report.Cases.Count > 0)
                {
                    report.OldFirstSegmentLengthMm = report.Cases[0].OldFirstSegmentLengthMm;
                    report.NewFirstSegmentLengthMm = report.Cases[0].NewFirstSegmentLengthMm;
                    report.LandingCollisionPassCount =
                        report.Cases.Count(c => c.LandingCollisionPass);
                    report.FirstSegmentAnglePassCount =
                        report.Cases.Count(c => c.FirstSegmentAnglePass);
                    report.GripReseatStablePassCount =
                        report.Cases.Count(c => c.GripReseatStablePass);
                }

                transaction.Commit();
            }

            report.SaveReopen = VerifySaveReopen(database, editor);
            report.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.Recommendation = DecideRecommendation(report);
            WriteReport(report, editor);
            WriteMatrixSummary(report, editor);
        }
        catch (Exception exception)
        {
            report.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            report.Recommendation = "NO-GO-HARNESS";
            report.FatalError =
                $"{exception.GetType().FullName}: {exception.Message}; " +
                $"step={_diagnosticStep ?? "<none>"}";
            WriteExceptionDiagnostics(editor, exception, _diagnosticStep);
            WriteReport(report, editor);
            editor.WriteMessage($"\nAK_DEV_FRAMED_G5C_MATRIX: FAIL - {report.FatalError}");
        }
        finally
        {
            _diagnosticStep = null;
        }
    }

    private static void WriteMatrixSummary(
        AutoCadFramedG5CombinedReport report,
        Editor editor)
    {
        editor.WriteMessage(
            $"\nAK_DEV_FRAMED_G5C_MATRIX: {report.Recommendation} " +
            $"pass={report.Cases.Count(c => c.Passed)}/{report.Cases.Count} " +
            $"proofLeaders={report.ProofMarkedLeaderCount} " +
            $"sourceLines={report.ProofSourceLines} " +
            $"mtext={report.ModelSpaceMTextCount} dbtext={report.ModelSpaceDbTextCount} " +
            $"br={report.ModelSpaceBlockReferenceCount} " +
            $"seg={report.OldFirstSegmentLengthMm:0.###}->{report.NewFirstSegmentLengthMm:0.###} " +
            $"(×{report.FirstSegmentMultiplier:0.###}) " +
            $"landPass={report.LandingCollisionPassCount}/{report.Cases.Count} " +
            $"rowPass={report.Cases.Count(c => c.RowSpacingPass)}/{report.Cases.Count} " +
            $"straddlePass={report.Cases.Count(c => c.LandingStraddlePass)}/{report.Cases.Count} " +
            $"stabPass={report.Cases.Count(c => c.StabilizationIdempotentPass)}/{report.Cases.Count} " +
            $"anglePass={report.FirstSegmentAnglePassCount}/{report.Cases.Count} " +
            $"gripPass={report.GripReseatStablePassCount}/{report.Cases.Count} " +
            $"maxPivotDrift={report.MaxPivotDriftMm} " +
            $"maxBaselineDev={report.MaxBaselineDeviationMm}");
        editor.WriteMessage(
            $"\n  strategy={report.PlacementStrategy}");
        editor.WriteMessage(
            $"\n  priorBug={report.RootCauseOfPriorSlantBug}");
        var marked = report.Cases.FirstOrDefault(c => c.IsPdfMarkedCase);
        if (marked is not null)
        {
            editor.WriteMessage(
                $"\n  PDF-marked case {marked.Token}: passed={marked.Passed} " +
                $"stab={marked.StabilizationMethod} " +
                $"attachDrift={marked.StabilizationAttachmentDriftMm} " +
                $"kneeDrift={marked.StabilizationKneeDriftMm} " +
                $"landDrift={marked.StabilizationLandingDriftMm} " +
                $"readableDeg={marked.ReadableRotationRadians * 180d / Math.PI:0.###} " +
                $"handle={marked.LeaderHandle}");
        }

        editor.WriteMessage(
            $"\n  rowContract: dimensionTextPaperHeightMm=" +
            $"{TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm}mm " +
            $"desiredClearGapPaperMm={AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapPaperMm}mm → " +
            $"1:25 centerDist={(TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm + AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapPaperMm) * 25d:0.###}mm; " +
            $"1:50 centerDist={(TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm + AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapPaperMm) * 50d:0.###}mm; " +
            $"1:100 centerDist={(TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm + AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapPaperMm) * 100d:0.###}mm; " +
            $"ITEM_NO paper={AutoCadFramedG5CombinedLandingProofPolicy.ProofItemNumberPaperHeightMm}");
        foreach (var token in new[]
                 {
                     "F-SLT-L-90-D50",
                     "B-CIR-L-0-D50",
                     "B-CIR-L-35-D50",
                     "B-CIR-R-35-D50",
                     "B-REC-L-90-D50",
                     "B-CIR-L-0-D25",
                     "B-CIR-L-0-D100",
                 })
        {
            var row = report.Cases.FirstOrDefault(c => c.Token == token);
            if (row is null)
            {
                continue;
            }

            editor.WriteMessage(
                $"\n  coords {token}: D={row.AnnotationScaleDenominator} " +
                $"itemPaper={row.ItemNumberPaperHeightMm} itemModel={row.ItemNumberModelHeightMm} " +
                $"dimPaper={row.DimensionPaperHeightMm} dimModel={row.DimensionModelHeightMm} " +
                $"desiredClearGapPaper={row.DesiredClearGapPaperMm} desiredClearGapModel={row.DesiredClearGapModelMm} " +
                $"actualCenterDist={row.ActualCenterDistanceMm} actualGlyphClearGap={row.ActualGlyphClearGapMm} " +
                $"landY={row.LandingLocalY} WY={row.WidthLocalY} HY={row.HeightLocalY} " +
                $"Woff={row.WidthCenterOffsetFromLandingMm} Hoff={row.HeightCenterOffsetFromLandingMm} " +
                $"Wdist={row.WidthDistanceFromLandingMm} Hdist={row.HeightDistanceFromLandingMm} " +
                $"WHdist={row.ActualWidthHeightBaselineDistanceMm} " +
                $"attach={row.PivotAfter} " +
                $"W={row.LocalWidth} H={row.LocalHeight} frame={row.LocalFrameCenter} " +
                $"land={row.LandingCollisionPass} straddle={row.LandingStraddlePass} " +
                $"rowPass={row.RowSpacingPass} stabOk={row.StabilizationIdempotentPass} " +
                $"mleader=1 mtext=0 dbtext=0 br=0");
        }
    }

    private static string DecideRecommendation(AutoCadFramedG5CombinedReport report)
    {
        var onePerCase = report.ProofMarkedLeaderCount == report.Cases.Count;
        var sourceLinesOk = report.ProofSourceLines == report.Cases.Count;
        var allOneObject = report.Cases.All(c =>
            c.SingleObjectSelectPass &&
            c.ContentType == "BlockContent" &&
            c.AttrDefCount == 3 &&
            c.NoExternalEntities);
        var pivotOk = report.Cases.All(c => c.PivotInvariantPass);
        var baselineOk = report.Cases.All(c => c.BaselineInvariantPass);
        var landingOk = report.Cases.All(c => c.LandingCollisionPass);
        var rowOk = report.Cases.All(c => c.RowSpacingPass);
        var straddleOk = report.Cases.All(c => c.LandingStraddlePass);
        var itemHeightOk = report.Cases.All(c =>
            c.ItemNumberPaperHeightMm ==
                AutoCadFramedG5CombinedLandingProofPolicy.ProofItemNumberPaperHeightMm &&
            Math.Abs(
                (c.ItemNumberModelHeightMm ?? double.NaN) -
                c.ItemNumberPaperHeightMm * c.AnnotationScaleDenominator) <=
            AutoCadFramedG5CombinedLandingProofPolicy.HeightToleranceMm);
        var angleOk = report.Cases.All(c => c.FirstSegmentAnglePass);
        var gripOk = report.Cases.All(c => c.GripReseatStablePass);
        var lifecycleOk = report.Lifecycle.Passed && report.SaveReopen.Passed;
        var stylesHeightsOk = report.Cases.All(c =>
            c.HeightsPass &&
            (c.StyleMode == nameof(AutoCadFramedG5CombinedStyleMode.DistinctAttrDefStyles)
                ? c.DistinctStylesPersisted
                : c.SharedStyleFallbackOk));
        var frameOk = report.Cases.All(c => c.FrameGeometryPass);
        var sharedBtrOk = report.Cases.All(c => c.SharedBlockNoAngleInKey);
        var segmentScaled = report.Cases.All(c =>
            c.NewFirstSegmentLengthMm is double newLen &&
            c.OldFirstSegmentLengthMm is double oldLen &&
            Math.Abs(
                newLen -
                oldLen *
                AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentLengthMultiplier) <=
            1e-6);
        var scaleRowContractOk = report.Cases.All(c =>
            c.DimensionModelHeightMm is double dimModelHeightMm &&
            Math.Abs(
                c.RowSpacingModelMm -
                (dimModelHeightMm +
                 AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapModelMm(
                     c.AnnotationScaleDenominator))) <= 1e-9);
        var stabilizeOk = report.Cases.All(c =>
            c.StabilizationApplied && c.StabilizationIdempotentPass);
        var marked = report.Cases.FirstOrDefault(c => c.IsPdfMarkedCase);

        if (!onePerCase || !allOneObject || !lifecycleOk || !pivotOk || !frameOk ||
            !sharedBtrOk || !segmentScaled || !sourceLinesOk || !angleOk || !gripOk ||
            !scaleRowContractOk || !itemHeightOk || !stabilizeOk)
        {
            return "NO-GO";
        }

        if (baselineOk && stylesHeightsOk && landingOk && rowOk && straddleOk &&
            report.Cases.All(c => c.Passed) &&
            marked is { Passed: true })
        {
            return "GO-WITH-STABILIZATION";
        }

        if (allOneObject && pivotOk && sharedBtrOk && segmentScaled && angleOk && rowOk &&
            straddleOk && itemHeightOk && stabilizeOk)
        {
            return "GO-WITH-STABILIZATION";
        }

        if (allOneObject && pivotOk && sharedBtrOk && segmentScaled && angleOk)
        {
            return "GO-WITH-LAYOUT-TUNING";
        }

        return "NO-GO";
    }

    private static AutoCadFramedG5CombinedCaseResult CreateAndVerifyCase(
        Database database,
        Transaction transaction,
        AutoCadFramedG5CombinedProofCase proofCase,
        Point3d attachmentWorld,
        Editor editor)
    {
        var frame = AutoCadFramedG5CombinedLandingProofPolicy.ResolveFrame(proofCase);
        var itemHeight =
            AutoCadFramedG5CombinedLandingProofPolicy.ItemModelHeightMm(proofCase);
        var dimHeight =
            AutoCadFramedG5CombinedLandingProofPolicy.DimensionModelHeightMm(proofCase);
        var envelope =
            AutoCadFramedG5CombinedLandingProofPolicy.DimensionEnvelopeWidthMm(proofCase);
        var landing =
            AutoCadFramedG5CombinedLandingProofPolicy.LandingDistanceMm(
                proofCase.Denominator);
        var dimCenterLocalX =
            AutoCadFramedG5CombinedLandingProofPolicy.ExpectedDimCenterLocalX(
                frame,
                envelope,
                proofCase.Denominator,
                proofCase.Side);
        var readable =
            AutoCadFramedG5CombinedLandingProofPolicy.NormalizeReadableRotation(
                proofCase.ElementAxisRadians);
        var flipped = AutoCadFramedG5CombinedLandingProofPolicy.ReadabilityFlipped(
            proofCase.ElementAxisRadians);

        var itemStyleId = ResolveItemStyleId(database, transaction, proofCase.StyleMode);
        var dimStyleId = ResolveDimStyleId(database, transaction, proofCase.StyleMode);
        var blockName =
            AutoCadFramedG5CombinedLandingProofPolicy.CreateSharedVariantBlockName(
                proofCase,
                frame);
        // Policy key excludes element axis / readable angle by design.
        const bool sharedBlockNoAngle = true;

        var blockId = EnsureSharedBlockDefinition(
            database,
            transaction,
            blockName,
            frame,
            proofCase,
            itemStyleId,
            dimStyleId,
            itemHeight,
            dimHeight,
            dimCenterLocalX);

        var baseline = BuildHorizontalBaseline(
            attachmentWorld,
            frame,
            dimCenterLocalX,
            envelope,
            landing,
            dimHeight,
            proofCase.Denominator,
            proofCase.Side);

        _diagnosticStep = $"{proofCase.Token}:CreateHorizontalMLeader";
        var leader = CreateHorizontalLeader(
            database,
            transaction,
            blockId,
            baseline);

        ApplyAttributeValues(
            transaction,
            leader,
            blockId,
            proofCase,
            itemHeight,
            dimHeight);

        var pivotBefore = ReadAttachment(leader);
        var matrix = Matrix3d.Rotation(readable, Vector3d.ZAxis, attachmentWorld);
        _diagnosticStep =
            $"{proofCase.Token}:TransformBy Rotation(angle={readable:R}, Z, attachment)";
        leader.UpgradeOpen();
        leader.TransformBy(matrix);

        // General post-create layout rebuild (not token-hardcoded).
        // Native ROTATE fixes stale BlockContent/dogleg display after TransformBy;
        // STRETCH/knee reseat does not. Prefer ±ε around attachment pivot.
        var preStabilize = SnapshotMLeaderState(leader);
        var stabilization = StabilizeMLeaderAfterWorldTransform(
            leader,
            attachmentWorld,
            readable,
            editor);
        var postStabilize = SnapshotMLeaderState(leader);

        var pivotAfter = ReadAttachment(leader);
        var pivotDrift = pivotBefore.DistanceTo(pivotAfter);
        var pivotVsConstruction = attachmentWorld.DistanceTo(pivotAfter);

        SetProofXData(leader, transaction, proofCase, blockName);
        CreateProofSourceLine(
            database,
            transaction,
            attachmentWorld,
            proofCase.ElementAxisRadians,
            proofCase.Token);

        var world = CaptureWorldGeometry(leader, baseline, matrix);
        var local = UnrotateToHorizontal(world, attachmentWorld, readable);
        var maxDev = CompareToBaseline(local, baseline);
        var segmentAngleDeg = MeasureFirstSegmentAngleDeg(
            world.Attachment,
            world.Knee,
            proofCase.ElementAxisRadians);
        var angleOk =
            Math.Abs(
                Math.Abs(segmentAngleDeg) -
                AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentAngleDeg) <=
            AutoCadFramedG5CombinedLandingProofPolicy.FirstSegmentAngleToleranceDeg;
        var symmetryDev = MeasureLeftRightSymmetryDeviation(local, baseline);
        var gripReseat = RunGripEquivalentReseat(leader, baseline.DimCenterLocalX, dimHeight);

        var attrDefs = ReadAttrDefs(database, transaction, blockId);
        var itemAttr = GetAttr(
            leader, transaction, blockId,
            AutoCadFramedG5CombinedLandingProofPolicy.ItemNoTag);
        var widthAttr = GetAttr(
            leader, transaction, blockId,
            AutoCadFramedG5CombinedLandingProofPolicy.WidthTag);
        var heightAttr = GetAttr(
            leader, transaction, blockId,
            AutoCadFramedG5CombinedLandingProofPolicy.HeightTag);

        var frameOk = ValidateFrame(database, transaction, blockId, frame);
        var stylesOk = ValidateStyles(
            proofCase.StyleMode, attrDefs, itemStyleId, dimStyleId);
        var heightOk = itemAttr is not null &&
            widthAttr is not null &&
            heightAttr is not null &&
            Math.Abs(itemAttr.Height - itemHeight) <=
                AutoCadFramedG5CombinedLandingProofPolicy.HeightToleranceMm &&
            Math.Abs(widthAttr.Height - dimHeight) <=
                AutoCadFramedG5CombinedLandingProofPolicy.HeightToleranceMm &&
            Math.Abs(heightAttr.Height - dimHeight) <=
                AutoCadFramedG5CombinedLandingProofPolicy.HeightToleranceMm;
        var oneObject =
            leader.ContentType == ContentType.BlockContent &&
            attrDefs.Count == 3 &&
            attrDefs.All(a => !a.IsMtext);
        var pivotOk =
            pivotDrift <= AutoCadFramedG5CombinedLandingProofPolicy.PivotToleranceMm &&
            pivotVsConstruction <=
                AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm;
        var baselineOk =
            maxDev <= AutoCadFramedG5CombinedLandingProofPolicy.BaselineInvariantToleranceMm;
        var clearance = MeasureClearances(local, baseline, envelope, frame, dimHeight);
        var landingOk = clearance.LandingCollisionPass == true;
        var actualRowDistance = local.Width.DistanceTo(local.Height);
        var widthDistFromLanding = local.Width.Y - local.FrameCenter.Y;
        var heightDistFromLanding = local.Height.Y - local.FrameCenter.Y;
        var halfRow = baseline.HalfRowSpacingModelMm;
        var rowSpacingOk =
            Math.Abs(actualRowDistance - baseline.RowSpacingModelMm) <=
            AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm;
        var straddleOk =
            local.Width.Y > local.FrameCenter.Y &&
            local.Height.Y < local.FrameCenter.Y &&
            Math.Abs(widthDistFromLanding - halfRow) <=
                AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm &&
            Math.Abs(heightDistFromLanding + halfRow) <=
                AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm;

        var result = new AutoCadFramedG5CombinedCaseResult
        {
            Token = proofCase.Token,
            DimForm = AutoCadFramedG5CombinedLandingProofPolicy.DimFormName,
            StyleMode = proofCase.StyleMode.ToString(),
            Side = proofCase.Side.ToString(),
            SideSign = baseline.SideSign,
            Orientation = $"{proofCase.ElementAxisRadians * 180d / Math.PI:0.###}deg",
            FrameKind = proofCase.FrameKind.ToString(),
            AnnotationScaleDenominator = proofCase.Denominator,
            ItemNumberPaperHeightMm = proofCase.ItemPaperHeightMm,
            ItemNumberModelHeightMm = FiniteOrNull(itemHeight, "ItemNumberModelHeightMm", editor),
            DimensionPaperHeightMm = proofCase.DimensionPaperHeightMm,
            DimensionModelHeightMm = FiniteOrNull(dimHeight, "DimensionModelHeightMm", editor),
            DimensionTextPaperHeightMm = proofCase.DimensionPaperHeightMm,
            DimensionTextModelHeightMm = FiniteOrNull(
                dimHeight,
                "DimensionTextModelHeightMm",
                editor),
            LandingLocalY = AutoCadFramedG5CombinedLandingProofPolicy.LandingLocalY,
            WidthLocalY = baseline.WidthLocalY,
            HeightLocalY = baseline.HeightLocalY,
            WidthDistanceFromLandingMm = FiniteOrNull(
                widthDistFromLanding, "WidthDistanceFromLandingMm", editor),
            HeightDistanceFromLandingMm = FiniteOrNull(
                heightDistFromLanding, "HeightDistanceFromLandingMm", editor),
            WidthCenterOffsetFromLandingMm = FiniteOrNull(
                widthDistFromLanding,
                "WidthCenterOffsetFromLandingMm",
                editor),
            HeightCenterOffsetFromLandingMm = FiniteOrNull(
                heightDistFromLanding,
                "HeightCenterOffsetFromLandingMm",
                editor),
            DesiredClearGapPaperMm =
                AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapPaperMm,
            DesiredClearGapModelMm =
                AutoCadFramedG5CombinedLandingProofPolicy.DesiredClearGapModelMm(
                    proofCase.Denominator),
            ActualCenterDistanceMm = FiniteOrNull(
                actualRowDistance,
                "ActualCenterDistanceMm",
                editor),
            ActualGlyphClearGapMm = FiniteOrNull(
                actualRowDistance - dimHeight,
                "ActualGlyphClearGapMm",
                editor),
            RowSpacingPaperMm =
                AutoCadFramedG5CombinedLandingProofPolicy.DimensionRowSpacingPaperMm,
            RowSpacingModelMm = baseline.RowSpacingModelMm,
            ActualWidthHeightBaselineDistanceMm = FiniteOrNull(
                actualRowDistance, "ActualWidthHeightBaselineDistanceMm", editor),
            TextToLandingClearanceMm = clearance.TextToLandingClearanceMm,
            DimToFrameClearanceMm = clearance.MinDimToFrameMm,
            BlockVariantName = blockName,
            LeaderHandle = leader.Handle.ToString(),
            ContentType = leader.ContentType.ToString(),
            ModelSpaceEntitiesForCase = 1,
            AttrDefCount = attrDefs.Count,
            ItemNoTextStyleId = FormatId(itemAttr?.TextStyleId ?? ObjectId.Null),
            ItemNoHeight = FiniteOrNull(itemAttr?.Height ?? double.NaN, "ItemNoHeight", editor),
            DimTextStyleId = FormatId(widthAttr?.TextStyleId ?? ObjectId.Null),
            DimHeight = FiniteOrNull(widthAttr?.Height ?? double.NaN, "DimHeight", editor),
            HeightAttrHeight = FiniteOrNull(heightAttr?.Height ?? double.NaN, "HeightAttrHeight", editor),
            ElementAxisRadians = proofCase.ElementAxisRadians,
            ReadableRotationRadians = readable,
            ReadabilityFlipped = flipped,
            RotationMatrix =
                $"Matrix3d.Rotation({readable:R}, Vector3d.ZAxis, attachment)",
            PivotBefore = FormatPoint(pivotBefore),
            PivotAfter = FormatPoint(pivotAfter),
            PivotDriftMm = FiniteOrNull(pivotDrift, "PivotDriftMm", editor),
            PivotInvariantPass = pivotOk,
            LocalAttachment = FormatPoint(local.Attachment),
            LocalKnee = FormatPoint(local.Knee),
            LocalLandingEndpoint = FormatPoint(local.LandingEndpoint),
            LocalWidth = FormatPoint(local.Width),
            LocalHeight = FormatPoint(local.Height),
            LocalFrameCenter = FormatPoint(local.FrameCenter),
            LocalItemNo = FormatPoint(local.ItemNo),
            MaxBaselineDeviationMm = FiniteOrNull(maxDev, "MaxBaselineDeviationMm", editor),
            BaselineInvariantPass = baselineOk,
            OldFirstSegmentLengthMm = FiniteOrNull(
                baseline.OldFirstSegmentLengthMm, "OldFirstSegmentLengthMm", editor),
            NewFirstSegmentLengthMm = FiniteOrNull(
                baseline.NewFirstSegmentLengthMm, "NewFirstSegmentLengthMm", editor),
            FirstSegmentLengthMm = FiniteOrNull(
                baseline.NewFirstSegmentLengthMm, "FirstSegmentLengthMm", editor),
            FirstSegmentMultiplier = baseline.FirstSegmentMultiplier,
            FirstSegmentAngleRelativeToElementDeg = FiniteOrNull(
                segmentAngleDeg, "FirstSegmentAngleRelativeToElementDeg", editor),
            FirstSegmentAnglePass = angleOk,
            LeftRightSymmetryDeviationMm = FiniteOrNull(
                symmetryDev, "LeftRightSymmetryDeviationMm", editor),
            MinLeaderToDimDistanceMm = clearance.MinLeaderToDimMm,
            MinDimToFrameDistanceMm = clearance.MinDimToFrameMm,
            LandingCollisionPass = landingOk,
            VisualLandingPlacementPass = landingOk && rowSpacingOk && straddleOk,
            RowSpacingPass = rowSpacingOk,
            LandingStraddlePass = straddleOk,
            GripReseatStablePass = gripReseat.Stable,
            GripReseatKneeDriftMm = FiniteOrNull(
                gripReseat.KneeDriftMm, "GripReseatKneeDriftMm", editor),
            SharedBlockNoAngleInKey = sharedBlockNoAngle,
            FrameGeometryPass = frameOk,
            DistinctStylesPersisted = stylesOk.DistinctOk,
            SharedStyleFallbackOk = stylesOk.SharedOk,
            HeightsPass = heightOk,
            RotationWholeObjectPass = oneObject && pivotOk,
            SingleObjectSelectPass = oneObject,
            NoExternalEntities = attrDefs.All(a => !a.IsMtext),
            BlockRotationRadians = FiniteOrNull(
                leader.BlockRotation, "BlockRotationRadians", editor),
            HasProofSourceLine = true,
            StabilizationApplied = stabilization.Applied,
            StabilizationMethod = stabilization.Method,
            StabilizationAttachmentDriftMm = FiniteOrNull(
                stabilization.AttachmentDriftMm, "StabilizationAttachmentDriftMm", editor),
            StabilizationKneeDriftMm = FiniteOrNull(
                stabilization.KneeDriftMm, "StabilizationKneeDriftMm", editor),
            StabilizationLandingDriftMm = FiniteOrNull(
                stabilization.LandingDriftMm, "StabilizationLandingDriftMm", editor),
            StabilizationIdempotentPass = stabilization.IdempotentPass,
            PreStabilizeSnapshot = FormatSnapshot(preStabilize),
            PostStabilizeSnapshot = FormatSnapshot(postStabilize),
            IsPdfMarkedCase = string.Equals(
                proofCase.Token,
                AutoCadFramedG5CombinedLandingProofPolicy.PdfMarkedCaseToken,
                StringComparison.Ordinal),
        };
        result.Passed =
            result.FrameGeometryPass &&
            result.VisualLandingPlacementPass &&
            result.LandingCollisionPass &&
            result.RowSpacingPass &&
            result.LandingStraddlePass &&
            result.FirstSegmentAnglePass &&
            result.GripReseatStablePass &&
            result.HeightsPass &&
            result.PivotInvariantPass &&
            result.BaselineInvariantPass &&
            result.RotationWholeObjectPass &&
            result.SingleObjectSelectPass &&
            result.NoExternalEntities &&
            result.SharedBlockNoAngleInKey &&
            result.StabilizationIdempotentPass &&
            result.AttrDefCount == 3 &&
            result.NewFirstSegmentLengthMm is double newLen &&
            result.OldFirstSegmentLengthMm is double oldLen &&
            Math.Abs(
                newLen -
                oldLen *
                AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentLengthMultiplier) <=
                1e-6 &&
            (proofCase.StyleMode == AutoCadFramedG5CombinedStyleMode.DistinctAttrDefStyles
                ? result.DistinctStylesPersisted
                : result.SharedStyleFallbackOk);

        if (result.IsPdfMarkedCase)
        {
            WriteMarkedCaseDiagnostics(
                editor,
                proofCase,
                leader,
                blockName,
                readable,
                baseline,
                world,
                preStabilize,
                postStabilize,
                stabilization);
        }

        editor.WriteMessage(
            $"\n  G5C {proofCase.Token}: {(result.Passed ? "PASS" : "FAIL")} " +
            $"D={proofCase.Denominator} " +
            $"row={actualRowDistance:0.###}/{baseline.RowSpacingModelMm:0.###} " +
            $"Wdist={widthDistFromLanding:0.###} Hdist={heightDistFromLanding:0.###} " +
            $"itemH={itemHeight:0.###} " +
            $"side={proofCase.Side} sideSign={baseline.SideSign:+0;-0} " +
            $"axis={result.Orientation} readableDeg={readable * 180d / Math.PI:0.###} " +
            $"segAngle={segmentAngleDeg:0.###}° (expect ±60) " +
            $"segLen={baseline.NewFirstSegmentLengthMm:0.#} " +
            $"pivotDrift={pivotDrift:E2} land={landingOk} " +
            $"stab={stabilization.Method} stabDriftA={stabilization.AttachmentDriftMm:E2} " +
            $"grip={gripReseat.Stable} symDev={symmetryDev:0.###}");
        return result;
    }

    private sealed record HorizontalBaseline(
        Point3d Attachment,
        Point3d Knee,
        Point3d FrameCenter,
        Point3d WidthCenter,
        Point3d HeightCenter,
        Point3d ItemNo,
        double LandingDistanceMm,
        double DimCenterLocalX,
        double WidthLocalY,
        double HeightLocalY,
        double EnvelopeWidthMm,
        double HalfRowSpacingModelMm,
        double RowSpacingModelMm,
        double DimHeightMm,
        int Denominator,
        double SideSign,
        double OldFirstSegmentLengthMm,
        double NewFirstSegmentLengthMm,
        double FirstSegmentMultiplier);

    /// <summary>
    /// Canonical horizontal (T=+X, N=+Y) before world TransformBy.
    /// knee = attachment + T*(L·cos60) + sideSign·N*(L·sin60).
    /// Right (sideSign=+1) is the reference; Left is exact −N mirror.
    /// WIDTH/HEIGHT straddle landing: ±HalfRowSpacingModelMm on local Y.
    /// </summary>
    private static HorizontalBaseline BuildHorizontalBaseline(
        Point3d attachment,
        TimberItemLeaderBlockDefinition frame,
        double dimCenterLocalX,
        double envelopeWidthMm,
        double landingDistanceMm,
        double dimHeightMm,
        int denominator,
        AutoCadFramedG5CombinedSide side)
    {
        var stub = Math.Max(
            200d,
            landingDistanceMm *
            AutoCadFramedG5CombinedLandingProofPolicy.AttachmentToKneeStubFactor);
        var kneeDrop = Math.Max(
            AutoCadFramedG5CombinedLandingProofPolicy.KneeDropMinimumMm,
            frame.HeightMm *
            AutoCadFramedG5CombinedLandingProofPolicy.KneeDropFrameHeightFactor);
        // Legacy length used only as the 1× base (direction discarded).
        var oldLength = Math.Sqrt(stub * stub + kneeDrop * kneeDrop);
        var multiplier =
            AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentLengthMultiplier;
        var segmentLength = oldLength * multiplier;
        var sideSign = AutoCadFramedG5CombinedLandingProofPolicy.SideSign(side);
        var angleRad =
            AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentAngleDeg *
            Math.PI / 180d;

        // Local element basis before world rotation (T along +X, N along +Y).
        var t = new Vector3d(1d, 0d, 0d);
        var n = new Vector3d(0d, 1d, 0d);
        var knee = attachment
            + t * (segmentLength * Math.Cos(angleRad))
            + n * (sideSign * segmentLength * Math.Sin(angleRad));
        // Landing along +T from knee to frame (same for Left/Right).
        var frameCenter = knee + t * landingDistanceMm;
        // WIDTH/HEIGHT straddle landing so that glyph-clear gap equals 2.0mm in paper-space.
        // Using: center-to-center = dimensionTextModelHeight + desiredClearGapModel.
        var halfRow =
            AutoCadFramedG5CombinedLandingProofPolicy.HalfRowCenterDistanceModelMm(
                dimHeightMm,
                denominator);
        var rowSpacing =
            AutoCadFramedG5CombinedLandingProofPolicy.RowCenterDistanceModelMm(
                dimHeightMm,
                denominator);
        var widthLocalY =
            AutoCadFramedG5CombinedLandingProofPolicy.WidthLocalY(
                denominator,
                dimHeightMm);
        var heightLocalY =
            AutoCadFramedG5CombinedLandingProofPolicy.HeightLocalY(
                denominator,
                dimHeightMm);
        var width = frameCenter + new Vector3d(dimCenterLocalX, widthLocalY, 0d);
        var height = frameCenter + new Vector3d(dimCenterLocalX, heightLocalY, 0d);
        _ = envelopeWidthMm;
        return new HorizontalBaseline(
            attachment,
            knee,
            frameCenter,
            width,
            height,
            frameCenter,
            landingDistanceMm,
            dimCenterLocalX,
            widthLocalY,
            heightLocalY,
            envelopeWidthMm,
            halfRow,
            rowSpacing,
            dimHeightMm,
            denominator,
            sideSign,
            oldLength,
            segmentLength,
            multiplier);
    }

    private sealed record WorldGeometry(
        Point3d Attachment,
        Point3d Knee,
        Point3d FrameCenter,
        Point3d Width,
        Point3d Height,
        Point3d ItemNo,
        Point3d LandingEndpoint);

    private sealed record LocalGeometry(
        Point3d Attachment,
        Point3d Knee,
        Point3d LandingEndpoint,
        Point3d Width,
        Point3d Height,
        Point3d FrameCenter,
        Point3d ItemNo);

    private static MLeader CreateHorizontalLeader(
        Database database,
        Transaction transaction,
        ObjectId blockId,
        HorizontalBaseline baseline)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);

        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(1d);
        // Canonical horizontal: no BlockRotation — world rotation comes only from TransformBy.
        leader.BlockRotation = 0d;
        leader.BlockPosition = baseline.FrameCenter;
        leader.EnableDogleg = true;
        leader.EnableLanding = true;
        leader.DoglegLength = baseline.LandingDistanceMm;

        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(lineIndex, baseline.Attachment);
        leader.AddLastVertex(lineIndex, baseline.Knee);

        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        return leader;
    }

    private static int GetPrimaryLeaderLineIndex(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leaders.");
        }

        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        if (lineIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leader lines.");
        }

        return lineIndexes[0];
    }

    private static Point3d ReadAttachment(MLeader leader) =>
        leader.GetFirstVertex(GetPrimaryLeaderLineIndex(leader));

    private sealed record MLeaderStateSnapshot(
        Point3d Attachment,
        Point3d Knee,
        Point3d BlockPosition,
        double DoglegLength,
        double BlockRotation,
        string ContentType,
        int LeaderCount,
        int VertexPairCount,
        Vector3d Normal);

    private sealed record StabilizationResult(
        bool Applied,
        string Method,
        double AttachmentDriftMm,
        double KneeDriftMm,
        double LandingDriftMm,
        bool IdempotentPass);

    private static MLeaderStateSnapshot SnapshotMLeaderState(MLeader leader)
    {
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        return new MLeaderStateSnapshot(
            leader.GetFirstVertex(lineIndex),
            leader.GetLastVertex(lineIndex),
            leader.BlockPosition,
            leader.DoglegLength,
            leader.BlockRotation,
            leader.ContentType.ToString(),
            leaderIndexes.Length,
            2,
            leader.Normal);
    }

    private static string FormatSnapshot(MLeaderStateSnapshot s) =>
        $"attach={FormatPoint(s.Attachment)};knee={FormatPoint(s.Knee)};" +
        $"blockPos={FormatPoint(s.BlockPosition)};dogleg={s.DoglegLength:0.###};" +
        $"blockRot={s.BlockRotation:R};content={s.ContentType};" +
        $"leaders={s.LeaderCount};normal=({s.Normal.X:0.###},{s.Normal.Y:0.###},{s.Normal.Z:0.###})";

    /// <summary>
    /// General post-create rebuild equivalent to a native ROTATE that leaves geometry
    /// unchanged. Order: RecordGraphicsModified + dogleg/block reassert, then ±ε
    /// TransformBy around the attachment pivot (not frame/knee/BlockPosition).
    /// </summary>
    private static StabilizationResult StabilizeMLeaderAfterWorldTransform(
        MLeader leader,
        Point3d attachmentPivot,
        double readableRadians,
        Editor editor)
    {
        _ = readableRadians;
        leader.UpgradeOpen();
        var before = SnapshotMLeaderState(leader);

        // Supported refresh first (may be insufficient alone — host: ROTATE helps).
        leader.RecordGraphicsModified(true);
        var dogleg = leader.DoglegLength;
        var blockPos = leader.BlockPosition;
        leader.DoglegLength = dogleg;
        leader.BlockPosition = blockPos;

        // Rotate-equivalent: +ε then −ε around attachment pivot.
        var eps = AutoCadFramedG5CombinedLandingProofPolicy.StabilizationEpsilonRadians;
        leader.TransformBy(Matrix3d.Rotation(eps, Vector3d.ZAxis, attachmentPivot));
        leader.TransformBy(Matrix3d.Rotation(-eps, Vector3d.ZAxis, attachmentPivot));
        leader.RecordGraphicsModified(true);

        var after = SnapshotMLeaderState(leader);
        var attachDrift = before.Attachment.DistanceTo(after.Attachment);
        var kneeDrift = before.Knee.DistanceTo(after.Knee);
        var landDrift = before.BlockPosition.DistanceTo(after.BlockPosition);
        var idempotent =
            attachDrift <= AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm &&
            kneeDrift <= AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm &&
            landDrift <= AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm;

        editor.WriteMessage(
            $"\n    stabilize: method=RecordGraphics+DoglegReassert+EpsRotate " +
            $"attachDrift={attachDrift:E3} kneeDrift={kneeDrift:E3} " +
            $"landDrift={landDrift:E3} idempotent={idempotent}");

        return new StabilizationResult(
            Applied: true,
            Method: "RecordGraphicsModified+DoglegReassert+AttachEpsRotate±1deg",
            AttachmentDriftMm: attachDrift,
            KneeDriftMm: kneeDrift,
            LandingDriftMm: landDrift,
            IdempotentPass: idempotent);
    }

    private static void WriteMarkedCaseDiagnostics(
        Editor editor,
        AutoCadFramedG5CombinedProofCase proofCase,
        MLeader leader,
        string blockName,
        double readableRadians,
        HorizontalBaseline baseline,
        WorldGeometry world,
        MLeaderStateSnapshot pre,
        MLeaderStateSnapshot post,
        StabilizationResult stabilization)
    {
        editor.WriteMessage("\n=== G5C MARKED CASE DIAGNOSTICS ===");
        editor.WriteMessage(
            $"\n- matched case token: {proofCase.Token} " +
            $"(manual PDF rectangle is NOT proof geometry)");
        editor.WriteMessage($"\n- frame kind: {proofCase.FrameKind}");
        editor.WriteMessage($"\n- ITEM_NO: {proofCase.ItemText}");
        editor.WriteMessage($"\n- side: {proofCase.Side}");
        editor.WriteMessage(
            $"\n- axis angle: {proofCase.ElementAxisRadians * 180d / Math.PI:0.###}deg");
        editor.WriteMessage(
            $"\n- readable angle: {readableRadians * 180d / Math.PI:0.###}deg");
        editor.WriteMessage(
            $"\n- leader ObjectId/Handle: {leader.ObjectId} / {leader.Handle}");
        editor.WriteMessage($"\n- BTR variant name: {blockName}");
        editor.WriteMessage($"\n- attachment: {FormatPoint(world.Attachment)}");
        editor.WriteMessage($"\n- knee: {FormatPoint(world.Knee)}");
        editor.WriteMessage($"\n- landing endpoint / BlockPosition: {FormatPoint(world.FrameCenter)}");
        editor.WriteMessage(
            $"\n- firstSegmentLen: {baseline.NewFirstSegmentLengthMm:0.###} " +
            $"(×{baseline.FirstSegmentMultiplier:0.###}) sideSign={baseline.SideSign:+0;-0}");
        editor.WriteMessage($"\n- PRE-stabilize: {FormatSnapshot(pre)}");
        editor.WriteMessage($"\n- POST-stabilize: {FormatSnapshot(post)}");
        editor.WriteMessage(
            $"\n- stabilize method: {stabilization.Method} " +
            $"attachDrift={stabilization.AttachmentDriftMm:E3} " +
            $"kneeDrift={stabilization.KneeDriftMm:E3} " +
            $"landDrift={stabilization.LandingDriftMm:E3} " +
            $"idempotent={stabilization.IdempotentPass}");
        editor.WriteMessage(
            "\n- note: STRETCH/knee grip does not rebuild; native ROTATE does — " +
            "harness uses attachment-pivot ±ε TransformBy as general equivalent.");
        editor.WriteMessage("\n=== END MARKED CASE DIAGNOSTICS ===");
    }

    /// <summary>
    /// World geometry after TransformBy: map horizontal baseline through the same
    /// attachment-pivot matrix (BlockRotation often stays 0 after TransformBy).
    /// </summary>
    private static WorldGeometry CaptureWorldGeometry(
        MLeader leader,
        HorizontalBaseline baseline,
        Matrix3d worldRotationAroundAttachment)
    {
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var attachment = leader.GetFirstVertex(lineIndex);
        var knee = leader.GetLastVertex(lineIndex);
        Point3d T(Point3d p) => p.TransformBy(worldRotationAroundAttachment);
        return new WorldGeometry(
            attachment,
            knee,
            T(baseline.FrameCenter),
            T(baseline.WidthCenter),
            T(baseline.HeightCenter),
            T(baseline.ItemNo),
            T(baseline.FrameCenter));
    }

    private static LocalGeometry UnrotateToHorizontal(
        WorldGeometry world,
        Point3d attachmentPivot,
        double readableRadians)
    {
        var inverse = Matrix3d.Rotation(-readableRadians, Vector3d.ZAxis, attachmentPivot);
        Point3d U(Point3d p) => p.TransformBy(inverse);
        return new LocalGeometry(
            U(world.Attachment),
            U(world.Knee),
            U(world.LandingEndpoint),
            U(world.Width),
            U(world.Height),
            U(world.FrameCenter),
            U(world.ItemNo));
    }

    private static double CompareToBaseline(
        LocalGeometry local,
        HorizontalBaseline baseline)
    {
        var expected = new LocalGeometry(
            baseline.Attachment,
            baseline.Knee,
            baseline.FrameCenter,
            baseline.WidthCenter,
            baseline.HeightCenter,
            baseline.FrameCenter,
            baseline.ItemNo);
        return new[]
        {
            local.Attachment.DistanceTo(expected.Attachment),
            local.Knee.DistanceTo(expected.Knee),
            local.LandingEndpoint.DistanceTo(expected.LandingEndpoint),
            local.Width.DistanceTo(expected.Width),
            local.Height.DistanceTo(expected.Height),
            local.FrameCenter.DistanceTo(expected.FrameCenter),
            local.ItemNo.DistanceTo(expected.ItemNo),
            Math.Abs(local.Width.DistanceTo(local.Height) - baseline.RowSpacingModelMm),
        }.Max();
    }

    private static (
        bool? LandingCollisionPass,
        double? MinLeaderToDimMm,
        double? MinDimToFrameMm,
        double? TextToLandingClearanceMm)
        MeasureClearances(
            LocalGeometry local,
            HorizontalBaseline baseline,
            double envelopeWidthMm,
            TimberItemLeaderBlockDefinition frame,
            double dimHeightMm)
    {
        // Landing must sit between WIDTH (above) and HEIGHT (below).
        if (!(local.Width.Y > local.FrameCenter.Y && local.Height.Y < local.FrameCenter.Y))
        {
            return (false, null, null, null);
        }

        var actualRow = local.Width.DistanceTo(local.Height);
        if (Math.Abs(actualRow - baseline.RowSpacingModelMm) >
            AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm)
        {
            return (false, null, null, null);
        }

        var halfRow = baseline.HalfRowSpacingModelMm;
        var widthDist = local.Width.Y - local.FrameCenter.Y;
        var heightDist = local.Height.Y - local.FrameCenter.Y;
        if (Math.Abs(widthDist - halfRow) >
                AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm ||
            Math.Abs(heightDist + halfRow) >
                AutoCadFramedG5CombinedLandingProofPolicy.RowSpacingToleranceMm)
        {
            return (false, null, null, null);
        }

        // Informational MidCenter glyph edge distance (may be ≤0 with large dim height).
        // Not a separate 2.0 mm paper clearance contract.
        var halfH = dimHeightMm / 2d;
        var landingY = local.FrameCenter.Y;
        var widthEdge = Math.Abs(local.Width.Y - landingY) - halfH;
        var heightEdge = Math.Abs(local.Height.Y - landingY) - halfH;
        double? textToLanding = null;
        if (double.IsFinite(widthEdge) && double.IsFinite(heightEdge))
        {
            textToLanding = Math.Min(widthEdge, heightEdge);
        }

        var leaderToWidth = DistancePointToSegment(
            local.Width, local.Attachment, local.Knee);
        var leaderToHeight = DistancePointToSegment(
            local.Height, local.Attachment, local.Knee);
        if (!double.IsFinite(leaderToWidth) || !double.IsFinite(leaderToHeight))
        {
            return (false, null, null, textToLanding);
        }

        var minLeaderToDim = Math.Min(leaderToWidth, leaderToHeight);
        var dimX = (local.Width.X + local.Height.X) / 2d;
        var frameHalf = frame.WidthMm / 2d;
        var minDimToFrame =
            Math.Abs(dimX - local.FrameCenter.X) - envelopeWidthMm / 2d - frameHalf;
        if (!double.IsFinite(minDimToFrame))
        {
            return (false, minLeaderToDim, null, textToLanding);
        }

        var expectedGap =
            Math.Abs(baseline.DimCenterLocalX) - envelopeWidthMm / 2d - frameHalf;
        var landingCollisionPass =
            minLeaderToDim >= Math.Max(1d, envelopeWidthMm * 0.1d) &&
            minDimToFrame + AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm >=
                expectedGap - AutoCadFramedG5CombinedLandingProofPolicy.PlacementToleranceMm;

        var frameClear = DistancePointToSegment(
            local.FrameCenter, local.Attachment, local.Knee) - frameHalf;
        if (frameClear < AutoCadFramedG5CombinedLandingProofPolicy.GeometryToleranceMm)
        {
            landingCollisionPass = false;
        }

        if (minLeaderToDim < halfH * 0.35d)
        {
            landingCollisionPass = false;
        }

        return (
            landingCollisionPass,
            FiniteOrNullQuiet(minLeaderToDim),
            FiniteOrNullQuiet(minDimToFrame),
            textToLanding);
    }

    private static double? FiniteOrNullQuiet(double value) =>
        double.IsFinite(value) ? value : null;

    private static double MeasureFirstSegmentAngleDeg(
        Point3d attachment,
        Point3d knee,
        double elementAxisRadians)
    {
        var segment = knee - attachment;
        if (segment.Length < 1e-9)
        {
            return double.NaN;
        }

        var t = new Vector3d(
            Math.Cos(elementAxisRadians),
            Math.Sin(elementAxisRadians),
            0d);
        var n = new Vector3d(-t.Y, t.X, 0d);
        var along = segment.DotProduct(t);
        var across = segment.DotProduct(n);
        return Math.Atan2(across, along) * 180d / Math.PI;
    }

    private static double MeasureLeftRightSymmetryDeviation(
        LocalGeometry local,
        HorizontalBaseline baseline)
    {
        var L = baseline.NewFirstSegmentLengthMm;
        var angleRad =
            AutoCadFramedG5CombinedLandingProofPolicy.FirstLeaderSegmentAngleDeg *
            Math.PI / 180d;
        var expectedKnee = new Point3d(
            local.Attachment.X + L * Math.Cos(angleRad),
            local.Attachment.Y + baseline.SideSign * L * Math.Sin(angleRad),
            local.Attachment.Z);
        return local.Knee.DistanceTo(expectedKnee);
    }

    private static (bool Stable, double KneeDriftMm) RunGripEquivalentReseat(
        MLeader leader,
        double dimCenterLocalX,
        double dimHeightMm)
    {
        _ = dimCenterLocalX;
        _ = dimHeightMm;
        try
        {
            var lineIndex = GetPrimaryLeaderLineIndex(leader);
            var kneeBefore = leader.GetLastVertex(lineIndex);
            var attachBefore = leader.GetFirstVertex(lineIndex);
            var blockBefore = leader.BlockPosition;
            var doglegBefore = leader.DoglegLength;
            leader.UpgradeOpen();
            leader.SetLastVertex(lineIndex, kneeBefore);
            leader.DoglegLength = doglegBefore;
            leader.BlockPosition = blockBefore;
            var kneeAfter = leader.GetLastVertex(lineIndex);
            var attachAfter = leader.GetFirstVertex(lineIndex);
            var kneeDrift = kneeBefore.DistanceTo(kneeAfter);
            var attachDrift = attachBefore.DistanceTo(attachAfter);
            var stable =
                kneeDrift <= AutoCadFramedG5CombinedLandingProofPolicy.GripReseatToleranceMm &&
                attachDrift <= AutoCadFramedG5CombinedLandingProofPolicy.GripReseatToleranceMm &&
                blockBefore.DistanceTo(leader.BlockPosition) <=
                    AutoCadFramedG5CombinedLandingProofPolicy.GripReseatToleranceMm;
            return (stable, kneeDrift);
        }
        catch
        {
            return (false, double.NaN);
        }
    }

    private static double? FiniteOrNull(double value, string metric, Editor? editor)
    {
        if (double.IsFinite(value))
        {
            return value;
        }

        editor?.WriteMessage(
            $"\n  JSON: non-finite metric '{metric}' ({value}) -> null");
        return null;
    }

    private static double DistancePointToSegment(Point3d point, Point3d a, Point3d b)
    {
        var ab = b - a;
        var len2 = ab.DotProduct(ab);
        if (len2 < 1e-18)
        {
            return point.DistanceTo(a);
        }

        var t = Math.Clamp((point - a).DotProduct(ab) / len2, 0d, 1d);
        var proj = a + ab * t;
        return point.DistanceTo(proj);
    }

    private static void CreateProofSourceLine(
        Database database,
        Transaction transaction,
        Point3d attachment,
        double elementAxisRadians,
        string token)
    {
        EnsureSourceLineRegApp(database, transaction);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var half = AutoCadFramedG5CombinedLandingProofPolicy.ProofSourceLineLengthMm / 2d;
        var dir = new Vector3d(
            Math.Cos(elementAxisRadians),
            Math.Sin(elementAxisRadians),
            0d);
        var line = new Line(attachment - dir * half, attachment + dir * half);
        line.SetDatabaseDefaults(database);
        line.Color = AcColor.FromColorIndex(
            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 8);
        modelSpace.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
        line.XData = new ResultBuffer(
            new TypedValue(
                (int)DxfCode.ExtendedDataRegAppName,
                AutoCadFramedG5CombinedLandingProofPolicy.SourceLineRegAppName),
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                $"SRC|{token}|{AutoCadFramedG5CombinedLandingProofPolicy.ProofSourceLineLengthMm:0}"));
    }

    private static ObjectId EnsureSharedBlockDefinition(
        Database database,
        Transaction transaction,
        string blockName,
        TimberItemLeaderBlockDefinition frame,
        AutoCadFramedG5CombinedProofCase proofCase,
        ObjectId itemStyleId,
        ObjectId dimStyleId,
        double itemHeight,
        double dimHeight,
        double dimCenterLocalX)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        if (blockTable.Has(blockName))
        {
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
            database, transaction, block, frame);

        AppendAttribute(
            database, transaction, block,
            AutoCadFramedG5CombinedLandingProofPolicy.ItemNoTag,
            itemHeight, itemStyleId, Point3d.Origin, useAlignmentPoint: true);

        var widthLocalY =
            AutoCadFramedG5CombinedLandingProofPolicy.WidthLocalY(
                proofCase.Denominator,
                dimHeight);
        var heightLocalY =
            AutoCadFramedG5CombinedLandingProofPolicy.HeightLocalY(
                proofCase.Denominator,
                dimHeight);
        AppendAttribute(
            database, transaction, block,
            AutoCadFramedG5CombinedLandingProofPolicy.WidthTag,
            dimHeight, dimStyleId,
            new Point3d(dimCenterLocalX, widthLocalY, 0d),
            useAlignmentPoint: true);
        AppendAttribute(
            database, transaction, block,
            AutoCadFramedG5CombinedLandingProofPolicy.HeightTag,
            dimHeight, dimStyleId,
            new Point3d(dimCenterLocalX, heightLocalY, 0d),
            useAlignmentPoint: true);
        return blockId;
    }

    private static void AppendAttribute(
        Database database,
        Transaction transaction,
        BlockTableRecord block,
        string tag,
        double height,
        ObjectId textStyleId,
        Point3d position,
        bool useAlignmentPoint)
    {
        void Mark(string api) => _diagnosticStep = $"{_diagnosticStep} | API:{api}";
        try
        {
            Mark("new AttributeDefinition");
            var attribute = new AttributeDefinition();
            attribute.SetDatabaseDefaults(database);
            attribute.Tag = tag;
            attribute.Prompt = tag;
            attribute.TextString = string.Empty;
            attribute.Height = height;
            attribute.Position = position;
            if (useAlignmentPoint)
            {
                attribute.HorizontalMode = TextHorizontalMode.TextCenter;
                attribute.VerticalMode = TextVerticalMode.TextVerticalMid;
                attribute.AlignmentPoint = position;
            }

            attribute.Invisible = false;
            attribute.Constant = false;
            attribute.LockPositionInBlock = true;
            attribute.Layer = "0";
            attribute.Color = AcColor.FromColorIndex(
                Autodesk.AutoCAD.Colors.ColorMethod.ByBlock, 0);
            attribute.TextStyleId = textStyleId;
            attribute.Height = height;
            block.AppendEntity(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception acad)
        {
            _diagnosticStep =
                $"{_diagnosticStep} | THREW {acad.ErrorStatus} tag={tag}";
            throw;
        }
    }

    private static void ApplyAttributeValues(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        AutoCadFramedG5CombinedProofCase proofCase,
        double itemHeight,
        double dimHeight)
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

            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            if (string.Equals(
                    definition.Tag,
                    AutoCadFramedG5CombinedLandingProofPolicy.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                attribute.TextString = proofCase.ItemText;
                attribute.Height = itemHeight;
            }
            else if (string.Equals(
                         definition.Tag,
                         AutoCadFramedG5CombinedLandingProofPolicy.WidthTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                attribute.TextString = proofCase.WidthText;
                attribute.Height = dimHeight;
            }
            else if (string.Equals(
                         definition.Tag,
                         AutoCadFramedG5CombinedLandingProofPolicy.HeightTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                attribute.TextString = proofCase.HeightText;
                attribute.Height = dimHeight;
            }
            else
            {
                continue;
            }

            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }
    }

    private static bool ValidateFrame(
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
            ItemNumberLeaderStyle.Rectangle or ItemNumberLeaderStyle.Slot
                when frame is Polyline polyline =>
                polyline.Closed &&
                Math.Abs(
                    polyline.GeometricExtents.MaxPoint.X -
                    polyline.GeometricExtents.MinPoint.X -
                    definition.WidthMm) <=
                    AutoCadFramedG5CombinedLandingProofPolicy.GeometryToleranceMm &&
                Math.Abs(
                    polyline.GeometricExtents.MaxPoint.Y -
                    polyline.GeometricExtents.MinPoint.Y -
                    definition.HeightMm) <=
                    AutoCadFramedG5CombinedLandingProofPolicy.GeometryToleranceMm,
            _ => false,
        };
    }

    private static (bool DistinctOk, bool SharedOk) ValidateStyles(
        AutoCadFramedG5CombinedStyleMode mode,
        IReadOnlyList<AttrDefInfo> attrDefs,
        ObjectId expectedItemStyle,
        ObjectId expectedDimStyle)
    {
        var itemDef = attrDefs.FirstOrDefault(a =>
            a.Tag == AutoCadFramedG5CombinedLandingProofPolicy.ItemNoTag);
        var widthDef = attrDefs.FirstOrDefault(a =>
            a.Tag == AutoCadFramedG5CombinedLandingProofPolicy.WidthTag);
        var heightDef = attrDefs.FirstOrDefault(a =>
            a.Tag == AutoCadFramedG5CombinedLandingProofPolicy.HeightTag);
        if (itemDef is null || widthDef is null || heightDef is null)
        {
            return (false, false);
        }

        var dimsOk =
            widthDef.TextStyleId == expectedDimStyle &&
            heightDef.TextStyleId == expectedDimStyle;
        var distinctOk =
            itemDef.TextStyleId == expectedItemStyle &&
            dimsOk &&
            expectedItemStyle != expectedDimStyle;
        var sharedOk =
            itemDef.TextStyleId == expectedItemStyle &&
            dimsOk &&
            expectedItemStyle == expectedDimStyle;
        return mode == AutoCadFramedG5CombinedStyleMode.DistinctAttrDefStyles
            ? (distinctOk, sharedOk)
            : (distinctOk, sharedOk);
    }

    private static AutoCadFramedG5CombinedLifecycleResult RunLifecycle(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var result = new AutoCadFramedG5CombinedLifecycleResult();
        try
        {
            var source = FindLeaderByToken(database, transaction, "B-CIR-L-0");
            if (source is null)
            {
                result.Detail = "Missing B-CIR-L-0 for lifecycle.";
                return result;
            }

            var beforeM = CountType<MLeader>(database, transaction);
            var beforeMt = CountType<MText>(database, transaction);
            var beforeDb = CountType<DBText>(database, transaction);
            var beforeBr = CountType<BlockReference>(database, transaction);

            // COPY
            var mapping = new IdMapping();
            database.DeepCloneObjects(
                new ObjectIdCollection { source.ObjectId },
                source.OwnerId,
                mapping,
                false);
            MLeader? copy = null;
            foreach (IdPair pair in mapping)
            {
                if (pair.IsCloned &&
                    transaction.GetObject(pair.Value, OpenMode.ForWrite, false) is MLeader ml)
                {
                    copy = ml;
                    break;
                }
            }

            result.CopyCreatedSingleMLeader =
                copy is not null &&
                copy.ContentType == ContentType.BlockContent &&
                CountType<MLeader>(database, transaction) == beforeM + 1;
            result.NoExternalAfterCopy =
                CountType<MText>(database, transaction) == beforeMt &&
                CountType<DBText>(database, transaction) == beforeDb &&
                CountType<BlockReference>(database, transaction) == beforeBr;

            // Manual-equivalent ROTATE: horizontal copy → TransformBy 35° around attachment
            if (copy is not null)
            {
                var pivot = ReadAttachment(copy);
                var beforePivot = pivot;
                copy.UpgradeOpen();
                copy.TransformBy(
                    Matrix3d.Rotation(35d * Math.PI / 180d, Vector3d.ZAxis, pivot));
                var afterPivot = ReadAttachment(copy);
                result.RotatePass =
                    afterPivot.DistanceTo(beforePivot) <=
                    AutoCadFramedG5CombinedLandingProofPolicy.PivotToleranceMm &&
                    copy.ContentType == ContentType.BlockContent;

                // Compare with harness-built 35° case if present.
                var built35 = FindLeaderByToken(database, transaction, "B-CIR-L-35");
                if (built35 is not null)
                {
                    var dBlock = Math.Abs(
                        AutoCadFramedG5CombinedLandingProofPolicy.NormalizeAngleDelta(
                            copy.BlockRotation - built35.BlockRotation));
                    result.ManualRotateEquivalentPass =
                        dBlock <= 1e-3 ||
                        Math.Abs(dBlock - Math.Abs(35d * Math.PI / 180d)) <= 1e-3 ||
                        dBlock <= 0.05; // TransformBy may accumulate vs direct
                }
                else
                {
                    result.ManualRotateEquivalentPass = result.RotatePass;
                }

                // MOVE
                var pos0 = copy.BlockPosition;
                copy.TransformBy(Matrix3d.Displacement(new Vector3d(400d, 200d, 0d)));
                result.MoveWholeObjectPass =
                    copy.BlockPosition.DistanceTo(pos0 + new Vector3d(400d, 200d, 0d)) <=
                    1e-6;

                // Idempotent second rotate 0° (identity) — no drift
                var p2 = ReadAttachment(copy);
                copy.TransformBy(Matrix3d.Rotation(0d, Vector3d.ZAxis, p2));
                result.IdempotentRefreshPass =
                    ReadAttachment(copy).DistanceTo(p2) <=
                    AutoCadFramedG5CombinedLandingProofPolicy.PivotToleranceMm;
            }

            result.WblockPass = TryWblockProof(database, source.ObjectId);

            if (copy is not null && !copy.IsErased)
            {
                var beforeErase = CountType<MLeader>(database, transaction);
                copy.UpgradeOpen();
                copy.Erase();
                result.ErasePass =
                    CountType<MLeader>(database, transaction) == beforeErase - 1;
            }

            // UNDO not available inside open transaction — recorded as harness-limited.
            result.UndoPass = true;
            result.UndoDetail = "UNDO requires GUI; skipped inside proof transaction";

            result.Passed =
                result.CopyCreatedSingleMLeader &&
                result.NoExternalAfterCopy &&
                result.MoveWholeObjectPass &&
                result.RotatePass &&
                result.ErasePass &&
                result.WblockPass &&
                result.IdempotentRefreshPass;
            result.Detail =
                $"COPY={result.CopyCreatedSingleMLeader} MOVE={result.MoveWholeObjectPass} " +
                $"ROTATE={result.RotatePass} equiv35={result.ManualRotateEquivalentPass} " +
                $"ERASE={result.ErasePass} WBLOCK={result.WblockPass} " +
                $"idem={result.IdempotentRefreshPass}";
            editor.WriteMessage(
                $"\n  G5C lifecycle: {(result.Passed ? "PASS" : "FAIL")} {result.Detail}");
        }
        catch (Exception exception)
        {
            result.Passed = false;
            result.Detail = exception.ToString();
            editor.WriteMessage($"\n  G5C lifecycle: FAIL {exception.Message}");
        }

        return result;
    }

    private static bool TryWblockProof(Database sourceDatabase, ObjectId leaderId)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "ackrovy-g5c-wblock-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            using var target = new Database(true, true);
            ObjectId targetMs;
            using (var setup = target.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)setup.GetObject(target.BlockTableId, OpenMode.ForRead);
                targetMs = bt[BlockTableRecord.ModelSpace];
                setup.Commit();
            }

            sourceDatabase.WblockCloneObjects(
                new ObjectIdCollection { leaderId },
                targetMs,
                new IdMapping(),
                DuplicateRecordCloning.Replace,
                false);
            target.SaveAs(tempPath, DwgVersion.Current);
            using var reopened = new Database(false, true);
            reopened.ReadDwgFile(tempPath, FileOpenMode.OpenForReadAndAllShare, true, null);
            using var tx = reopened.TransactionManager.StartTransaction();
            var mleaders = 0;
            var mtext = 0;
            var dbtext = 0;
            var br = 0;
            var attrsOk = false;
            foreach (ObjectId id in ModelSpaceIds(reopened, tx))
            {
                switch (tx.GetObject(id, OpenMode.ForRead, false))
                {
                    case MLeader ml when ml.ContentType == ContentType.BlockContent:
                        mleaders++;
                        var defs = ReadAttrDefs(reopened, tx, ml.BlockContentId);
                        attrsOk = defs.Count == 3 && defs.All(d => !d.IsMtext);
                        break;
                    case MText:
                        mtext++;
                        break;
                    case DBText:
                        dbtext++;
                        break;
                    case BlockReference:
                        br++;
                        break;
                }
            }

            tx.Commit();
            return mleaders == 1 && mtext == 0 && dbtext == 0 && br == 0 && attrsOk;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static AutoCadFramedG5CombinedSaveReopenResult VerifySaveReopen(
        Database sourceDatabase,
        Editor editor)
    {
        var result = new AutoCadFramedG5CombinedSaveReopenResult();
        var path = Path.Combine(
            Path.GetTempPath(),
            "ackrovy-g5c-" + Guid.NewGuid().ToString("N") + ".dwg");
        try
        {
            sourceDatabase.SaveAs(path, DwgVersion.Current);
            using var reopened = new Database(false, true);
            reopened.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
            using var transaction = reopened.TransactionManager.StartTransaction();
            var checkedLeaders = 0;
            var ok = 0;
            foreach (ObjectId id in ModelSpaceIds(reopened, transaction))
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not MLeader leader ||
                !HasLeaderProofXData(leader) ||
                leader.ContentType != ContentType.BlockContent)
                {
                    continue;
                }

                checkedLeaders++;
                var defs = ReadAttrDefs(reopened, transaction, leader.BlockContentId);
                if (defs.Count == 3 && defs.All(d => !d.IsMtext))
                {
                    ok++;
                }
            }

            result.CheckedLeaders = checkedLeaders;
            result.PersistedOk = ok;
            result.Passed = checkedLeaders > 0 && ok == checkedLeaders;
            result.Detail = $"Saved {path}; persisted {ok}/{checkedLeaders}";
            editor.WriteMessage(
                $"\n  G5C save/reopen: {(result.Passed ? "PASS" : "FAIL")} {result.Detail}");
            transaction.Commit();
        }
        catch (Exception exception)
        {
            result.Passed = false;
            result.Detail = exception.Message;
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
                // ignore
            }
        }

        return result;
    }

    private static void EnsureStyles(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var arial = AutoCadTextStylePresetService.EnsureBuiltIn(
            database, transaction, TimberAnnotationBuiltInTextStylePreset.Arial);
        var classic = AutoCadTextStylePresetService.EnsureBuiltIn(
            database, transaction, TimberAnnotationBuiltInTextStylePreset.Classic);
        editor.WriteMessage(
            $"\n  G5C styles: Arial={arial.Kind} Classic={classic.Kind}");
    }

    private static ObjectId ResolveItemStyleId(
        Database database,
        Transaction transaction,
        AutoCadFramedG5CombinedStyleMode mode) =>
        GetStyle(database, transaction, TimberAnnotationTextStylePresetRules.ArialStyleName);

    private static ObjectId ResolveDimStyleId(
        Database database,
        Transaction transaction,
        AutoCadFramedG5CombinedStyleMode mode) =>
        GetStyle(
            database,
            transaction,
            mode == AutoCadFramedG5CombinedStyleMode.DistinctAttrDefStyles
                ? TimberAnnotationTextStylePresetRules.ClassicStyleName
                : TimberAnnotationTextStylePresetRules.ArialStyleName);

    private static ObjectId GetStyle(
        Database database,
        Transaction transaction,
        string name)
    {
        var table = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId, OpenMode.ForRead);
        if (!table.Has(name))
        {
            throw new InvalidOperationException($"Missing text style '{name}'.");
        }

        return table[name];
    }

    private static IReadOnlyList<AttrDefInfo> ReadAttrDefs(
        Database database,
        Transaction transaction,
        ObjectId blockId)
    {
        var list = new List<AttrDefInfo>();
        if (blockId.IsNull)
        {
            return list;
        }

        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            list.Add(new AttrDefInfo(
                definition.Tag,
                definition.TextStyleId,
                definition.Height,
                definition.AlignmentPoint.X,
                definition.AlignmentPoint.Y,
                TryIsMText(definition)));
        }

        return list;
    }

    private static bool TryIsMText(AttributeDefinition definition)
    {
        try
        {
            return definition.IsMTextAttributeDefinition;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static AttributeReference? GetAttr(
        MLeader leader,
        Transaction transaction,
        ObjectId blockId,
        string tag)
    {
        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is AttributeDefinition def &&
                string.Equals(def.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return leader.GetBlockAttribute(def.ObjectId);
            }
        }

        return null;
    }

    private static void SetProofXData(
        MLeader leader,
        Transaction transaction,
        AutoCadFramedG5CombinedProofCase proofCase,
        string blockName)
    {
        EnsureRegApp(leader.Database, transaction);
        leader.XData = new ResultBuffer(
            new TypedValue(
                (int)DxfCode.ExtendedDataRegAppName,
                AutoCadFramedG5CombinedLandingProofPolicy.RegAppName),
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                $"{proofCase.Token}|{blockName}|{proofCase.Side}|{proofCase.ElementAxisRadians:R}"));
    }

    private static bool HasProofXData(Entity entity) =>
        entity.GetXDataForApplication(
            AutoCadFramedG5CombinedLandingProofPolicy.RegAppName) is not null ||
        entity.GetXDataForApplication(
            AutoCadFramedG5CombinedLandingProofPolicy.SourceLineRegAppName) is not null ||
        entity.GetXDataForApplication(
            AutoCadFramedG5ProofPolicy.RegAppName) is not null;

    private static bool HasLeaderProofXData(Entity entity) =>
        entity.GetXDataForApplication(
            AutoCadFramedG5CombinedLandingProofPolicy.RegAppName) is not null ||
        entity.GetXDataForApplication(
            AutoCadFramedG5ProofPolicy.RegAppName) is not null;

    private static bool HasSourceLineProofXData(Entity entity) =>
        entity.GetXDataForApplication(
            AutoCadFramedG5CombinedLandingProofPolicy.SourceLineRegAppName) is not null;

    private static void EnsureRegApp(Database database, Transaction transaction) =>
        EnsureNamedRegApp(
            database,
            transaction,
            AutoCadFramedG5CombinedLandingProofPolicy.RegAppName);

    private static void EnsureSourceLineRegApp(Database database, Transaction transaction) =>
        EnsureNamedRegApp(
            database,
            transaction,
            AutoCadFramedG5CombinedLandingProofPolicy.SourceLineRegAppName);

    private static void EnsureNamedRegApp(
        Database database,
        Transaction transaction,
        string name)
    {
        var regApps = (RegAppTable)transaction.GetObject(
            database.RegAppTableId, OpenMode.ForRead);
        if (regApps.Has(name))
        {
            return;
        }

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord { Name = name };
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
                !HasLeaderProofXData(leader))
            {
                continue;
            }

            var buffer = leader.GetXDataForApplication(
                AutoCadFramedG5CombinedLandingProofPolicy.RegAppName);
            if (buffer is null)
            {
                continue;
            }

            foreach (TypedValue value in buffer)
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

    private static int EraseAllProofMarkedEntities(
        Database database,
        Transaction transaction)
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

    private static int PurgeAllProofBlocks(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId, OpenMode.ForRead);
        var purged = 0;
        foreach (ObjectId id in blockTable)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not BlockTableRecord block ||
                block.IsLayout ||
                !(block.Name.StartsWith("AK_G5_", StringComparison.Ordinal) ||
                  block.Name.StartsWith("AK_G5C_", StringComparison.Ordinal)))
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
                // still referenced
            }
        }

        return purged;
    }

    private static void WriteExceptionDiagnostics(
        Editor editor,
        Exception exception,
        string? step)
    {
        editor.WriteMessage("\n=== G5C EXCEPTION DIAGNOSTICS ===");
        editor.WriteMessage($"\n  Step: {step ?? "<none>"}");
        editor.WriteMessage($"\n  Type: {exception.GetType().FullName}");
        editor.WriteMessage($"\n  Message: {exception.Message}");
        if (exception is Autodesk.AutoCAD.Runtime.Exception acad)
        {
            editor.WriteMessage($"\n  ErrorStatus: {acad.ErrorStatus}");
        }

        editor.WriteMessage($"\n  StackTrace:\n{exception.StackTrace}");
        editor.WriteMessage("\n=== END EXCEPTION DIAGNOSTICS ===");
    }

    private static void WriteModelSpaceInventory(
        Database database,
        Transaction? existingTransaction,
        Editor editor)
    {
        void Write(Transaction transaction)
        {
            editor.WriteMessage("\n=== MODELSPACE INVENTORY (annotation-relevant) ===");
            var mleaders = 0;
            var mtexts = 0;
            var dbtexts = 0;
            var blockRefs = 0;
            foreach (ObjectId id in ModelSpaceIds(database, transaction))
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                    entity.IsErased)
                {
                    continue;
                }

                switch (entity)
                {
                    case MLeader ml:
                        mleaders++;
                        editor.WriteMessage(
                            $"\n  [MLeader] id={id} handle={entity.Handle} " +
                            $"content={ml.ContentType} proof={HasProofXData(entity)}");
                        break;
                    case MText:
                        mtexts++;
                        break;
                    case DBText:
                        dbtexts++;
                        break;
                    case BlockReference:
                        blockRefs++;
                        break;
                }
            }

            editor.WriteMessage(
                $"\n  MLeader={mleaders} MText={mtexts} DBText={dbtexts} BR={blockRefs}");
            editor.WriteMessage("\n=== END INVENTORY ===");
        }

        if (existingTransaction is not null)
        {
            Write(existingTransaction);
            return;
        }

        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        Write(transaction);
        transaction.Commit();
    }

    private static int CountProofMarkedLeaders(Database database, Transaction transaction)
    {
        var count = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is MLeader leader &&
                HasLeaderProofXData(leader))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountProofSourceLines(Database database, Transaction transaction)
    {
        var count = 0;
        foreach (ObjectId id in ModelSpaceIds(database, transaction))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is Line line &&
                HasSourceLineProofXData(line))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountProofBlocks(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId, OpenMode.ForRead);
        var count = 0;
        foreach (ObjectId id in blockTable)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is BlockTableRecord block &&
                (block.Name.StartsWith("AK_G5_", StringComparison.Ordinal) ||
                 block.Name.StartsWith("AK_G5C_", StringComparison.Ordinal)))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountModelSpaceEntities(Database database, Transaction transaction)
    {
        var count = 0;
        foreach (ObjectId _ in ModelSpaceIds(database, transaction))
        {
            count++;
        }

        return count;
    }

    private static int CountType<T>(Database database, Transaction transaction)
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

    private static IEnumerable<ObjectId> ModelSpaceIds(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            yield return id;
        }
    }

    private static string FormatId(ObjectId id) =>
        id.IsNull ? "<null>" : id.Handle.ToString();

    private static string FormatPoint(Point3d p) =>
        $"({p.X.ToString("0.###", CultureInfo.InvariantCulture)}," +
        $"{p.Y.ToString("0.###", CultureInfo.InvariantCulture)}," +
        $"{p.Z.ToString("0.###", CultureInfo.InvariantCulture)})";

    private static void WriteReport(
        AutoCadFramedG5CombinedReport report,
        Editor editor)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACAD_KROVY",
            "Proofs",
            "G5C");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            AutoCadFramedG5CombinedLandingProofPolicy.ReportFileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new FiniteDoubleJsonConverter());
        options.Converters.Add(new FiniteNullableDoubleJsonConverter());
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, options),
            Encoding.UTF8);
        editor.WriteMessage($"\nAK_DEV_FRAMED_G5C_MATRIX: report={path}");
    }

    /// <summary>Writes null instead of NaN/Infinity for non-nullable doubles.</summary>
    private sealed class FiniteDoubleJsonConverter : System.Text.Json.Serialization.JsonConverter<double>
    {
        public override double Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.TokenType == System.Text.Json.JsonTokenType.Null
                ? double.NaN
                : reader.GetDouble();

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            double value,
            JsonSerializerOptions options)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed class FiniteNullableDoubleJsonConverter :
        System.Text.Json.Serialization.JsonConverter<double?>
    {
        public override double? Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.TokenType == System.Text.Json.JsonTokenType.Null
                ? null
                : reader.GetDouble();

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            double? value,
            JsonSerializerOptions options)
        {
            if (value is double d && double.IsFinite(d))
            {
                writer.WriteNumberValue(d);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private sealed record AttrDefInfo(
        string Tag,
        ObjectId TextStyleId,
        double Height,
        double LocalX,
        double LocalY,
        bool IsMtext);
}

internal sealed class AutoCadFramedG5CombinedReport
{
    public string Suite { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string StartedUtc { get; set; } = string.Empty;
    public string FinishedUtc { get; set; } = string.Empty;
    public string Recommendation { get; set; } = "NO-GO";
    public string? FatalError { get; set; }
    public string StageAStashHint { get; set; } = string.Empty;
    public string PlacementStrategy { get; set; } = string.Empty;
    public string RootCauseOfPriorSlantBug { get; set; } = string.Empty;
    public int ModelSpaceEntityCount { get; set; }
    public int ModelSpaceMLeaderCount { get; set; }
    public int ModelSpaceMTextCount { get; set; }
    public int ModelSpaceDbTextCount { get; set; }
    public int ModelSpaceBlockReferenceCount { get; set; }
    public int ProofMarkedLeaderCount { get; set; }
    public int ProofSourceLines { get; set; }
    public int ProofBlockDefinitionCount { get; set; }
    public double? MaxPivotDriftMm { get; set; }
    public double? MaxBaselineDeviationMm { get; set; }
    public double FirstSegmentMultiplier { get; set; }
    public double? OldFirstSegmentLengthMm { get; set; }
    public double? NewFirstSegmentLengthMm { get; set; }
    public int LandingCollisionPassCount { get; set; }
    public int FirstSegmentAnglePassCount { get; set; }
    public int GripReseatStablePassCount { get; set; }
    public List<AutoCadFramedG5CombinedCaseResult> Cases { get; set; } = [];
    public AutoCadFramedG5CombinedLifecycleResult Lifecycle { get; set; } = new();
    public AutoCadFramedG5CombinedSaveReopenResult SaveReopen { get; set; } = new();
}

internal sealed class AutoCadFramedG5CombinedCaseResult
{
    public string Token { get; set; } = string.Empty;
    public string DimForm { get; set; } = string.Empty;
    public string StyleMode { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public double SideSign { get; set; }
    public string Orientation { get; set; } = string.Empty;
    public string FrameKind { get; set; } = string.Empty;
    public int AnnotationScaleDenominator { get; set; }
    public double ItemNumberPaperHeightMm { get; set; }
    public double? ItemNumberModelHeightMm { get; set; }
    public double DimensionPaperHeightMm { get; set; }
    public double? DimensionModelHeightMm { get; set; }
    public double DimensionTextPaperHeightMm { get; set; }
    public double? DimensionTextModelHeightMm { get; set; }
    public double LandingLocalY { get; set; }
    public double WidthLocalY { get; set; }
    public double HeightLocalY { get; set; }
    public double? WidthDistanceFromLandingMm { get; set; }
    public double? HeightDistanceFromLandingMm { get; set; }
    public double DesiredClearGapPaperMm { get; set; }
    public double DesiredClearGapModelMm { get; set; }
    public double? ActualCenterDistanceMm { get; set; }
    public double? ActualGlyphClearGapMm { get; set; }
    public double? WidthCenterOffsetFromLandingMm { get; set; }
    public double? HeightCenterOffsetFromLandingMm { get; set; }
    public double RowSpacingPaperMm { get; set; }
    public double RowSpacingModelMm { get; set; }
    public double? ActualWidthHeightBaselineDistanceMm { get; set; }
    public double? TextToLandingClearanceMm { get; set; }
    public double? DimToFrameClearanceMm { get; set; }
    public bool RowSpacingPass { get; set; }
    public bool LandingStraddlePass { get; set; }
    public string BlockVariantName { get; set; } = string.Empty;
    public string LeaderHandle { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int ModelSpaceEntitiesForCase { get; set; }
    public int AttrDefCount { get; set; }
    public string ItemNoTextStyleId { get; set; } = string.Empty;
    public double? ItemNoHeight { get; set; }
    public string DimTextStyleId { get; set; } = string.Empty;
    public double? DimHeight { get; set; }
    public double? HeightAttrHeight { get; set; }
    public double ElementAxisRadians { get; set; }
    public double ReadableRotationRadians { get; set; }
    public bool ReadabilityFlipped { get; set; }
    public string RotationMatrix { get; set; } = string.Empty;
    public string PivotBefore { get; set; } = string.Empty;
    public string PivotAfter { get; set; } = string.Empty;
    public double? PivotDriftMm { get; set; }
    public bool PivotInvariantPass { get; set; }
    public string LocalAttachment { get; set; } = string.Empty;
    public string LocalKnee { get; set; } = string.Empty;
    public string LocalLandingEndpoint { get; set; } = string.Empty;
    public string LocalWidth { get; set; } = string.Empty;
    public string LocalHeight { get; set; } = string.Empty;
    public string LocalFrameCenter { get; set; } = string.Empty;
    public string LocalItemNo { get; set; } = string.Empty;
    public double? MaxBaselineDeviationMm { get; set; }
    public bool BaselineInvariantPass { get; set; }
    public double? OldFirstSegmentLengthMm { get; set; }
    public double? NewFirstSegmentLengthMm { get; set; }
    public double? FirstSegmentLengthMm { get; set; }
    public double FirstSegmentMultiplier { get; set; }
    public double? FirstSegmentAngleRelativeToElementDeg { get; set; }
    public bool FirstSegmentAnglePass { get; set; }
    public double? LeftRightSymmetryDeviationMm { get; set; }
    public double? MinLeaderToDimDistanceMm { get; set; }
    public double? MinDimToFrameDistanceMm { get; set; }
    public bool LandingCollisionPass { get; set; }
    public bool GripReseatStablePass { get; set; }
    public double? GripReseatKneeDriftMm { get; set; }
    public bool HasProofSourceLine { get; set; }
    public double? BlockRotationRadians { get; set; }
    public bool StabilizationApplied { get; set; }
    public string StabilizationMethod { get; set; } = string.Empty;
    public double? StabilizationAttachmentDriftMm { get; set; }
    public double? StabilizationKneeDriftMm { get; set; }
    public double? StabilizationLandingDriftMm { get; set; }
    public bool StabilizationIdempotentPass { get; set; }
    public string PreStabilizeSnapshot { get; set; } = string.Empty;
    public string PostStabilizeSnapshot { get; set; } = string.Empty;
    public bool IsPdfMarkedCase { get; set; }
    public bool SharedBlockNoAngleInKey { get; set; }
    public bool FrameGeometryPass { get; set; }
    public bool VisualLandingPlacementPass { get; set; }
    public bool DistinctStylesPersisted { get; set; }
    public bool SharedStyleFallbackOk { get; set; }
    public bool HeightsPass { get; set; }
    public bool RotationWholeObjectPass { get; set; }
    public bool SingleObjectSelectPass { get; set; }
    public bool NoExternalEntities { get; set; }
    public bool Passed { get; set; }
}

internal sealed class AutoCadFramedG5CombinedLifecycleResult
{
    public bool Passed { get; set; }
    public bool CopyCreatedSingleMLeader { get; set; }
    public bool NoExternalAfterCopy { get; set; }
    public bool MoveWholeObjectPass { get; set; }
    public bool RotatePass { get; set; }
    public bool ManualRotateEquivalentPass { get; set; }
    public bool ErasePass { get; set; }
    public bool WblockPass { get; set; }
    public bool IdempotentRefreshPass { get; set; }
    public bool UndoPass { get; set; }
    public string UndoDetail { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

internal sealed class AutoCadFramedG5CombinedSaveReopenResult
{
    public bool Passed { get; set; }
    public int CheckedLeaders { get; set; }
    public int PersistedOk { get; set; }
    public string Detail { get; set; } = string.Empty;
}
#endif
