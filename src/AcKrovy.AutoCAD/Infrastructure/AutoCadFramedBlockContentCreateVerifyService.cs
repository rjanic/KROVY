#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG host matrix for <see cref="AutoCadFramedBlockContentAnnotationService"/>.
/// Creates one BlockContent MLeader per case; never routes production labels.
/// </summary>
internal static class AutoCadFramedBlockContentCreateVerifyService
{
    /// <summary>
    /// DEV-only RegApp for this verify harness. Not production framed annotation schema.
    /// </summary>
    internal const string DebugRegAppName = "AK_DEV_FBC_CREATE";

    private const string VerifyLayerName = "AK_DEV_FBC_CREATE";
    private const string DebugMarkerToken = "FBC_CREATE_VERIFY";
    private const double PlacementToleranceMm = 2.0d;
    private const double HeightToleranceMm = 0.05d;
    private const double GridOriginXMm = 5000d;
    private const double GridOriginYMm = 5000d;
    private const double GridStepMm = 5000d;
    private const int GridColumns = 5;
    private const double DebugCoordinateLimitMm = 200_000d;
    private const double AttrRefDriftToleranceMm = 0.05d;
    private const double DriftProbeDisplacementXMm = 250d;
    private const double DriftProbeDisplacementYMm = 125d;
    private const double KneeStretchDeltaXMm = -400d;
    private const double KneeStretchDeltaYMm = 50d;

    private sealed record VerifyCase(
        string Token,
        TimberFramedBlockContentKind Kind,
        TimberFramedBlockContentPresentation Presentation,
        TimberLeaderHorizontalSide Side,
        double ElementAxisDegrees,
        int Denominator,
        double ItemPaperHeightMm,
        AutoCadFramedBlockContentStabilizationMode StabilizationMode);

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        var (found, erased) = EraseMarkedDebugEntities(database, transaction);
        transaction.Commit();
        editor.WriteMessage(
            $"\n=== AK_DEV_FBC_CREATE_CLEAN ===");
        editor.WriteMessage($"\noldDebugEntitiesFound={found}");
        editor.WriteMessage($"\noldDebugEntitiesErased={erased}");
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        var (oldFound, oldErased) = EraseMarkedDebugEntities(database, transaction);
        editor.WriteMessage(
            $"\n=== AK_DEV_FBC_CREATE_VERIFY ===");
        editor.WriteMessage($"\noldDebugEntitiesFound={oldFound}");
        editor.WriteMessage($"\noldDebugEntitiesErased={oldErased}");

        var textStyleId = database.Textstyle;
        var textStyle = (TextStyleTableRecord)transaction.GetObject(
            textStyleId,
            OpenMode.ForRead);
        var styleName = string.IsNullOrWhiteSpace(textStyle.Name)
            ? "Standard"
            : textStyle.Name;
        var layerId = EnsureVerifyLayer(database, transaction);

        var cases = BuildCases();
        editor.WriteMessage($"\ncurrentRunCases={cases.Count}");
        editor.WriteMessage(
            "\nVertex contract: verts=2 is native AutoCAD (attachment+knee); " +
            "dogleg/landing is DoglegLength + BlockPosition, not a 3rd vertex.");
        editor.WriteMessage(
            "\nStabilization: matrix uses A/B only (CreateOrderOnly / " +
            "RecordGraphicsRefresh). EpsilonRotate (D) is out of this run.");

        var results = new List<AutoCadFramedBlockContentAnnotationResult>(
            cases.Count);
        var currentRunLeaderIds = new List<ObjectId>(cases.Count);
        var allOk = true;

        for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            var proofCase = cases[caseIndex];
            var grid = ResolveGridPoint(caseIndex);
            editor.WriteMessage(
                $"\nPLAN {proofCase.Token}: grid=({grid.X:0.#},{grid.Y:0.#}) " +
                $"row={caseIndex / GridColumns} col={caseIndex % GridColumns}");
            if (!IsFiniteWithinDebugLimit(grid.X) ||
                !IsFiniteWithinDebugLimit(grid.Y))
            {
                editor.WriteMessage(
                    $"\n[FAIL] {proofCase.Token}: planned grid outside DEBUG limit " +
                    $"abs<= {DebugCoordinateLimitMm}");
                allOk = false;
                results.Add(
                    AutoCadFramedBlockContentAnnotationResult.Fail(
                        AutoCadFramedBlockContentAnnotationResultKind.InvalidRequest,
                        proofCase.StabilizationMode,
                        "DEBUG grid coordinate outside safe limit."));
                continue;
            }

            var request = BuildRequest(
                proofCase,
                caseIndex,
                styleName,
                textStyleId,
                layerId);
            var result = AutoCadFramedBlockContentAnnotationService.Create(
                database,
                transaction,
                request);
            results.Add(result);
            if (result.Succeeded &&
                result.LeaderId is ObjectId createdId &&
                !createdId.IsNull)
            {
                MarkDebugEntity(database, transaction, createdId, proofCase.Token);
                currentRunLeaderIds.Add(createdId);
            }

            var caseOk = ValidateCase(
                database,
                transaction,
                proofCase,
                result,
                grid,
                editor);
            allOk &= caseOk;
            WriteCase(editor, proofCase, result, caseOk);
        }

        allOk &= ValidateAttrRefRelativeDrift(
            database,
            transaction,
            cases,
            results,
            editor);

        allOk &= ValidateKneeStretchAttrRefRigidity(
            database,
            transaction,
            cases,
            results,
            editor);

        var inventoryOk = ValidateCurrentRunInventory(
            database,
            transaction,
            cases.Count,
            currentRunLeaderIds,
            editor);
        allOk &= inventoryOk;

        // Same BTR / different denom: AttrRef baseline heights match; BlockScale differs.
        allOk &= ValidateSharedBtrAcrossDenominators(
            cases,
            results,
            editor);

        // Optional reopen verify pass for cases that requested it.
        allOk &= RunReopenVerifyPass(
            database,
            transaction,
            cases,
            results,
            editor);

        transaction.Commit();
        editor.WriteMessage(
            allOk
                ? "\nFBC create verify: PASS"
                : "\nFBC create verify: FAIL");
        editor.WriteMessage(
            "\nLifecycle notes (manual): MOVE/ROTATE/ERASE/COPY the MLeader as an " +
            "ordinary AutoCAD entity; SAVE/REOPEN; AttrRef values persist; BTR stays " +
            "shared immutable. STRETCH of timber element is P4. Grip STRETCH of the " +
            "MLeader must keep ITEM_NO/WIDTH/HEIGHT rigidly bound (ConnectBase).");
    }

    private static IReadOnlyList<VerifyCase> BuildCases() =>
    [
        // Plain Combined L/R — A/B stabilization only
        new("P-COMB-L-0-D50", TimberFramedBlockContentKind.Plain,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 0d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("P-COMB-R-35-D50", TimberFramedBlockContentKind.Plain,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 35d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),

        // Circle Combined L/R + scales
        new("C-COMB-L-0-D25", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 0d, 25, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("C-COMB-R-90-D50", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 90d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("C-COMB-L-180-D100", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 180d, 100, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),

        // Rectangle Combined L/R
        new("R-COMB-L-35-D50", TimberFramedBlockContentKind.Rectangle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 35d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("R-COMB-R-0-D50", TimberFramedBlockContentKind.Rectangle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 0d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.CreateOrderOnly),

        // Slot Combined L/R (no EpsilonRotate in matrix — A/B only)
        new("S-COMB-L-90-D50", TimberFramedBlockContentKind.Slot,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 90d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("S-COMB-R-180-D50", TimberFramedBlockContentKind.Slot,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 180d, 50, 3.0d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("C-COMB-L-270-D50", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 270d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),

        // Framed ItemOnly — same positive Combined landing contract (Core > 0)
        new("C-ITEM-L-0-D50", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.ItemOnly,
            TimberLeaderHorizontalSide.Left, 0d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("R-ITEM-R-35-D50", TimberFramedBlockContentKind.Rectangle,
            TimberFramedBlockContentPresentation.ItemOnly,
            TimberLeaderHorizontalSide.Right, 35d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("S-ITEM-L-90-D50", TimberFramedBlockContentKind.Slot,
            TimberFramedBlockContentPresentation.ItemOnly,
            TimberLeaderHorizontalSide.Left, 90d, 50, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.ReopenVerify),

        // Extra scale / angle coverage
        new("C-COMB-R-0-D100", TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 0d, 100, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("R-COMB-L-180-D25", TimberFramedBlockContentKind.Rectangle,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Left, 180d, 25, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
        new("S-COMB-R-35-D100", TimberFramedBlockContentKind.Slot,
            TimberFramedBlockContentPresentation.Combined,
            TimberLeaderHorizontalSide.Right, 35d, 100, 2.7d,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh),
    ];

    private static Point3d ResolveGridPoint(int caseIndex)
    {
        // Deterministic compact grid from case index only — never hash, never
        // prior-result base, never cumulative transform.
        var column = caseIndex % GridColumns;
        var row = caseIndex / GridColumns;
        return new Point3d(
            GridOriginXMm + column * GridStepMm,
            GridOriginYMm + row * GridStepMm,
            0d);
    }

    private static bool IsFiniteWithinDebugLimit(double value) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value) &&
        Math.Abs(value) <= DebugCoordinateLimitMm;

    private static AutoCadFramedBlockContentAnnotationRequest BuildRequest(
        VerifyCase proofCase,
        int caseIndex,
        string styleName,
        ObjectId styleId,
        ObjectId layerId)
    {
        var denom = proofCase.Denominator;
        var scale = TimberAnnotationScaleRules.GetScaleFactor(denom);
        var frame = ResolveFrame(proofCase);
        var frameWidth = frame?.WidthMm * scale ?? 0d;
        var frameHeight = frame?.HeightMm * scale ?? 0d;
        var dimPaper = TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm;
        var envelope =
            TimberFramedBlockContentDefinitionRules
                .CalculateReferenceDimensionEnvelopeWidthMm(dimPaper) * scale;
        var firstSegment =
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm * scale;
        // ItemOnly shares the confirmed Combined landing length. Legacy
        // zero framed-item landing is not valid on the G5 layout path (Core > 0).
        var landing =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scale;

        var attachment = ResolveGridPoint(caseIndex);

        return new AutoCadFramedBlockContentAnnotationRequest(
            attachment.X,
            attachment.Y,
            proofCase.ElementAxisDegrees * Math.PI / 180d,
            proofCase.Side,
            proofCase.Kind,
            proofCase.Presentation,
            frameWidth,
            frameHeight,
            envelope,
            denom,
            proofCase.ItemPaperHeightMm,
            dimPaper,
            styleName,
            styleName,
            styleId,
            styleId,
            ItemNoText: proofCase.Kind == TimberFramedBlockContentKind.Plain
                ? "1"
                : "12",
            WidthText: proofCase.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? "120"
                : string.Empty,
            HeightText: proofCase.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? "60"
                : string.Empty,
            firstSegment,
            landing,
            layerId,
            proofCase.StabilizationMode);
    }

    private static TimberItemLeaderBlockDefinition? ResolveFrame(VerifyCase proofCase)
    {
        if (proofCase.Kind == TimberFramedBlockContentKind.Plain)
        {
            return null;
        }

        var style = TimberFramedBlockContentDefinitionRules.ToItemNumberLeaderStyle(
            proofCase.Kind);
        return TimberItemLeaderBlockDefinitionRules.Resolve(style, "12");
    }

    private static bool ValidateCase(
        Database database,
        Transaction transaction,
        VerifyCase proofCase,
        AutoCadFramedBlockContentAnnotationResult result,
        Point3d expectedAttachment,
        Editor editor)
    {
        if (!result.Succeeded ||
            result.LeaderId is not ObjectId leaderId ||
            leaderId.IsNull)
        {
            return false;
        }

        if (transaction.GetObject(leaderId, OpenMode.ForRead) is not MLeader leader)
        {
            return false;
        }

        var ok = leader.ContentType == ContentType.BlockContent &&
            result.ContentType == ContentType.BlockContent &&
            result.LeaderCount == 1 &&
            // Native API: attachment + knee only; dogleg is DoglegLength.
            result.VertexCount == 2 &&
            leader.BlockConnectionType == BlockConnectionType.ConnectBase &&
            result.ResolvedBlockName is not null &&
            AutoCadFramedBlockContentPolicy.IsProductionFamilyName(
                result.ResolvedBlockName) &&
            Math.Abs(
                result.ItemEffectiveModelHeightMm -
                proofCase.ItemPaperHeightMm * proofCase.Denominator) <=
                HeightToleranceMm &&
            Math.Abs(result.BlockScale -
                TimberAnnotationScaleRules.GetScaleFactor(proofCase.Denominator)) <=
                HeightToleranceMm &&
            result.AttachmentDriftMm <= PlacementToleranceMm &&
            result.KneeDriftMm <= PlacementToleranceMm &&
            result.LandingDriftMm <= PlacementToleranceMm &&
            result.AttachmentWorld is Point3d attach &&
            IsFiniteWithinDebugLimit(attach.X) &&
            IsFiniteWithinDebugLimit(attach.Y) &&
            attach.DistanceTo(expectedAttachment) <= PlacementToleranceMm;

        if (proofCase.Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            ok &= result.AttributeTags.Count == 3 &&
                result.AttributeTags.Contains("ITEM_NO") &&
                result.AttributeTags.Contains("WIDTH") &&
                result.AttributeTags.Contains("HEIGHT");

            if (ok)
            {
                ok &= ValidateCombinedWorldColumnPlacement(
                    transaction,
                    leader,
                    proofCase.Token,
                    editor);
            }
        }
        else
        {
            ok &= result.AttributeTags.Count == 1 &&
                result.AttributeTags.Contains("ITEM_NO") &&
                !result.AttributeTags.Contains("WIDTH") &&
                !result.AttributeTags.Contains("HEIGHT");
        }

        _ = database;
        return ok;
    }

    private static bool ValidateCombinedWorldColumnPlacement(
        Transaction transaction,
        MLeader leader,
        string token,
        Editor editor)
    {
        if (!AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var evaluation,
                out var points,
                out var note))
        {
            editor.WriteMessage(
                $"\n[FAIL] {token}: K→D→I evaluate failed: {note}");
            return false;
        }

        editor.WriteMessage(
            $"\n{token} " +
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .FormatEvaluationDiagnostics(points, evaluation));

        if (!evaluation.Current.IsCorrect ||
            evaluation.Decision !=
                TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp)
        {
            editor.WriteMessage(
                $"\n[FAIL] {token}: Combined must satisfy K→D→I on first display " +
                $"(decision={TimberFramedBlockContentDimensionColumnPlacementRules.DescribeDecision(evaluation.Decision)}).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// DEBUG: create Combined → record AttrRef offsets vs BlockPosition →
    /// TransformBy displacement (supported whole-leader move) → remeasure.
    /// Relative drift must stay ~0 (rigid BlockContent binding).
    /// </summary>
    private static bool ValidateAttrRefRelativeDrift(
        Database database,
        Transaction transaction,
        IReadOnlyList<VerifyCase> cases,
        IReadOnlyList<AutoCadFramedBlockContentAnnotationResult> results,
        Editor editor)
    {
        var index = IndexOf(cases, "C-COMB-L-0-D25");
        if (index < 0 ||
            !results[index].Succeeded ||
            results[index].LeaderId is not ObjectId leaderId ||
            leaderId.IsNull)
        {
            editor.WriteMessage(
                "\nAttrRef relative drift: FAIL missing Combined probe case");
            return false;
        }

        var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForWrite);
        if (leader.BlockContentId.IsNull ||
            leader.BlockConnectionType != BlockConnectionType.ConnectBase)
        {
            editor.WriteMessage(
                "\nAttrRef relative drift: FAIL ConnectBase/BlockContent missing");
            return false;
        }

        var before = CaptureAttrOffsets(transaction, leader);
        if (before.Count < 3)
        {
            editor.WriteMessage(
                $"\nAttrRef relative drift: FAIL tags={before.Count} (need 3)");
            return false;
        }

        leader.TransformBy(
            Matrix3d.Displacement(
                new Vector3d(
                    DriftProbeDisplacementXMm,
                    DriftProbeDisplacementYMm,
                    0d)));

        var after = CaptureAttrOffsets(transaction, leader);
        var maxDrift = 0d;
        foreach (var (tag, beforeOffset) in before)
        {
            if (!after.TryGetValue(tag, out var afterOffset))
            {
                editor.WriteMessage(
                    $"\nAttrRef relative drift: FAIL missing after tag={tag}");
                return false;
            }

            var drift = (afterOffset - beforeOffset).Length;
            maxDrift = Math.Max(maxDrift, drift);
        }

        var ok = maxDrift <= AttrRefDriftToleranceMm;
        editor.WriteMessage(
            $"\nAttrRef relative drift (TransformBy Δ=" +
            $"{DriftProbeDisplacementXMm},{DriftProbeDisplacementYMm}): " +
            $"maxDrift={maxDrift:E2} mm tags={before.Count} " +
            $"=> {(ok ? "OK" : "FAIL")}");
        editor.WriteMessage(
            "\nHost manual STRETCH protocol: select Circle Combined L and R " +
            "MLeaders, grip STRETCH the knee; ITEM_NO/WIDTH/HEIGHT must stay " +
            "rigid with frame (no LEFT-only WIDTH/HEIGHT drift). Code probes " +
            "cover whole-leader move + SetLastVertex knee stretch L/R.");

        _ = database;
        return ok;
    }

    /// <summary>
    /// DEBUG: simulate knee grip STRETCH via SetLastVertex on Left and Right
    /// Combined leaders; WIDTH/HEIGHT offsets vs ITEM_NO must stay rigid.
    /// </summary>
    private static bool ValidateKneeStretchAttrRefRigidity(
        Database database,
        Transaction transaction,
        IReadOnlyList<VerifyCase> cases,
        IReadOnlyList<AutoCadFramedBlockContentAnnotationResult> results,
        Editor editor)
    {
        var leftOk = ProbeKneeStretchCase(
            transaction,
            cases,
            results,
            "C-COMB-L-0-D25",
            "LEFT",
            editor);
        var rightOk = ProbeKneeStretchCase(
            transaction,
            cases,
            results,
            "R-COMB-R-0-D50",
            "RIGHT",
            editor);
        _ = database;
        return leftOk && rightOk;
    }

    private static bool ProbeKneeStretchCase(
        Transaction transaction,
        IReadOnlyList<VerifyCase> cases,
        IReadOnlyList<AutoCadFramedBlockContentAnnotationResult> results,
        string token,
        string sideLabel,
        Editor editor)
    {
        var index = IndexOf(cases, token);
        if (index < 0 ||
            !results[index].Succeeded ||
            results[index].LeaderId is not ObjectId leaderId ||
            leaderId.IsNull)
        {
            editor.WriteMessage(
                $"\nKnee stretch AttrRef ({sideLabel}): FAIL missing {token}");
            return false;
        }

        var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForWrite);
        if (leader.BlockConnectionType != BlockConnectionType.ConnectBase)
        {
            editor.WriteMessage(
                $"\nKnee stretch AttrRef ({sideLabel}): FAIL not ConnectBase");
            return false;
        }

        var before = CaptureAttrOffsets(transaction, leader);
        var beforeMutual = CaptureAttrMutualOffsets(before);
        if (beforeMutual is null)
        {
            editor.WriteMessage(
                $"\nKnee stretch AttrRef ({sideLabel}): FAIL need ITEM_NO/WIDTH/HEIGHT");
            return false;
        }

        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var kneeBefore = leader.GetLastVertex(lineIndex);

        // Approximate knee grip STRETCH: move last vertex only. Do not
        // reassert dogleg/BlockPosition — that would mask host reseat behavior.
        leader.SetLastVertex(
            lineIndex,
            kneeBefore + new Vector3d(KneeStretchDeltaXMm, KneeStretchDeltaYMm, 0d));

        var after = CaptureAttrOffsets(transaction, leader);
        var afterMutual = CaptureAttrMutualOffsets(after);
        if (afterMutual is null)
        {
            editor.WriteMessage(
                $"\nKnee stretch AttrRef ({sideLabel}): FAIL attrs missing after stretch");
            return false;
        }

        var widthDrift = (afterMutual.Value.WidthFromItem - beforeMutual.Value.WidthFromItem)
            .Length;
        var heightDrift = (afterMutual.Value.HeightFromItem - beforeMutual.Value.HeightFromItem)
            .Length;
        var maxDrift = Math.Max(widthDrift, heightDrift);
        var ok = maxDrift <= AttrRefDriftToleranceMm;
        editor.WriteMessage(
            $"\nKnee stretch AttrRef ({sideLabel} {token}, Δknee=" +
            $"{KneeStretchDeltaXMm},{KneeStretchDeltaYMm}): " +
            $"Wdrift={widthDrift:E2} Hdrift={heightDrift:E2} " +
            $"connect={leader.BlockConnectionType} " +
            $"=> {(ok ? "OK" : "FAIL")}");
        return ok;
    }

    private static (
        Vector3d WidthFromItem,
        Vector3d HeightFromItem)? CaptureAttrMutualOffsets(
        Dictionary<string, Vector3d> offsets)
    {
        if (!offsets.TryGetValue("ITEM_NO", out var item) ||
            !offsets.TryGetValue("WIDTH", out var width) ||
            !offsets.TryGetValue("HEIGHT", out var height))
        {
            return null;
        }

        return (width - item, height - item);
    }

    private static int GetPrimaryLeaderLineIndex(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leaders.");
        }

        var lineIndexes = leader
            .GetLeaderLineIndexes(leaderIndexes[0])
            .Cast<int>()
            .ToArray();
        if (lineIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leader lines.");
        }

        return lineIndexes[0];
    }

    private static Dictionary<string, Vector3d> CaptureAttrOffsets(
        Transaction transaction,
        MLeader leader)
    {
        var offsets = new Dictionary<string, Vector3d>(
            StringComparer.OrdinalIgnoreCase);
        var blockPos = leader.BlockPosition;
        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased)
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                continue;
            }

            // Prefer AlignmentPoint (centered AttrDefs); fall back to Position.
            var world = attribute.AlignmentPoint;
            if (world.DistanceTo(Point3d.Origin) < 1e-12d &&
                attribute.Position.DistanceTo(Point3d.Origin) > 1e-12d)
            {
                world = attribute.Position;
            }

            offsets[definition.Tag.ToUpperInvariant()] = world - blockPos;
        }

        return offsets;
    }

    private static bool ValidateCurrentRunInventory(
        Database database,
        Transaction transaction,
        int expectedCases,
        IReadOnlyList<ObjectId> currentRunLeaderIds,
        Editor editor)
    {
        var mLeaders = 0;
        var mTexts = 0;
        var dbTexts = 0;
        var blockRefs = 0;
        var other = 0;

        foreach (var id in currentRunLeaderIds)
        {
            if (id.IsNull ||
                transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            // Inventory is scoped to this verify run's ObjectIds only — never
            // all ModelSpace MLeaders (user geometry / prior unmarked entities).
            if (!HasDebugMarker(entity))
            {
                other++;
                continue;
            }

            switch (entity)
            {
                case MLeader:
                    mLeaders++;
                    break;
                case MText:
                    mTexts++;
                    break;
                case DBText:
                    dbTexts++;
                    break;
                case BlockReference:
                    blockRefs++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        var ok = currentRunLeaderIds.Count == expectedCases &&
            mLeaders == expectedCases &&
            mTexts == 0 &&
            dbTexts == 0 &&
            blockRefs == 0 &&
            other == 0;
        editor.WriteMessage($"\ncurrentRunMLeaders={mLeaders}");
        editor.WriteMessage($"\nMText={mTexts}");
        editor.WriteMessage($"\nDBText={dbTexts}");
        editor.WriteMessage($"\nstandaloneBlockReference={blockRefs}");
        editor.WriteMessage(
            $"\nCurrent-run inventory: cases={currentRunLeaderIds.Count}/" +
            $"{expectedCases} markedMLeaders={mLeaders} other={other} " +
            $"=> {(ok ? "OK" : "FAIL")}");

        _ = database;
        return ok;
    }

    private static (int Found, int Erased) EraseMarkedDebugEntities(
        Database database,
        Transaction transaction)
    {
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForRead);
        var candidates = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            candidates.Add(id);
        }

        var found = 0;
        var erased = 0;
        foreach (var id in candidates)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased ||
                !HasDebugMarker(entity))
            {
                continue;
            }

            found++;
            if (!entity.IsWriteEnabled)
            {
                entity.UpgradeOpen();
            }

            entity.Erase();
            erased++;
        }

        return (found, erased);
    }

    private static void MarkDebugEntity(
        Database database,
        Transaction transaction,
        ObjectId entityId,
        string caseToken)
    {
        if (entityId.IsNull ||
            transaction.GetObject(entityId, OpenMode.ForWrite, true) is not Entity entity ||
            entity.IsErased)
        {
            return;
        }

        EnsureDebugRegApp(database, transaction);
        // Preserve any foreign XData apps; replace only our DEV RegApp chunk.
        var retained = ReadForeignXData(entity);
        retained.Add(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, DebugRegAppName));
        retained.Add(
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                $"{DebugMarkerToken}|{caseToken}"));
        entity.XData = new ResultBuffer(retained.ToArray());
    }

    private static bool HasDebugMarker(Entity entity)
    {
        using var buffer = entity.GetXDataForApplication(DebugRegAppName);
        return buffer is not null;
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        using (xdata)
        {
            var skip = false;
            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    skip = string.Equals(
                        Convert.ToString(value.Value),
                        DebugRegAppName,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (!skip)
                {
                    retained.Add(value);
                }
            }
        }

        return retained;
    }

    private static void EnsureDebugRegApp(Database database, Transaction transaction)
    {
        var regApps = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regApps.Has(DebugRegAppName))
        {
            return;
        }

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = DebugRegAppName,
        };
        regApps.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static BlockTableRecord OpenModelSpace(
        Database database,
        Transaction transaction,
        OpenMode mode)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            mode);
    }

    private static bool ValidateSharedBtrAcrossDenominators(
        IReadOnlyList<VerifyCase> cases,
        IReadOnlyList<AutoCadFramedBlockContentAnnotationResult> results,
        Editor editor)
    {
        // C-COMB-L-0-D25 and C-COMB-L-180-D100 share Circle Combined 2.7 paper
        // (angle/side/denom excluded from BTR key) — same BTR, different BlockScale.
        var d25 = IndexOf(cases, "C-COMB-L-0-D25");
        var d100 = IndexOf(cases, "C-COMB-L-180-D100");
        if (d25 < 0 || d100 < 0)
        {
            editor.WriteMessage("\nShared-BTR check: FAIL missing scale pair");
            return false;
        }

        var a = results[d25];
        var b = results[d100];
        var sameBtr = a.Succeeded &&
            b.Succeeded &&
            a.BlockTableRecordId == b.BlockTableRecordId &&
            a.ResolvedBlockName == b.ResolvedBlockName &&
            Math.Abs(a.ItemAttrRefHeightMm - b.ItemAttrRefHeightMm) <=
                HeightToleranceMm &&
            Math.Abs(a.BlockScale - b.BlockScale) > HeightToleranceMm &&
            Math.Abs(
                a.ItemEffectiveModelHeightMm - b.ItemEffectiveModelHeightMm) >
                HeightToleranceMm;
        editor.WriteMessage(
            $"\nShared-BTR 1:25 vs 1:100: sameId={a.BlockTableRecordId == b.BlockTableRecordId} " +
            $"attrRefH={a.ItemAttrRefHeightMm:0.###}/{b.ItemAttrRefHeightMm:0.###} " +
            $"blockScale={a.BlockScale:0.###}/{b.BlockScale:0.###} " +
            $"effH={a.ItemEffectiveModelHeightMm:0.###}/{b.ItemEffectiveModelHeightMm:0.###} " +
            $"=> {(sameBtr ? "OK" : "FAIL")}");
        return sameBtr;
    }

    private static bool RunReopenVerifyPass(
        Database database,
        Transaction transaction,
        IReadOnlyList<VerifyCase> cases,
        IReadOnlyList<AutoCadFramedBlockContentAnnotationResult> results,
        Editor editor)
    {
        var ok = true;
        for (var i = 0; i < cases.Count; i++)
        {
            if (cases[i].StabilizationMode !=
                AutoCadFramedBlockContentStabilizationMode.ReopenVerify)
            {
                continue;
            }

            var result = results[i];
            if (!result.Succeeded ||
                result.LeaderId is not ObjectId leaderId ||
                leaderId.IsNull)
            {
                ok = false;
                continue;
            }

            // C: reopen ForWrite and verify content type / vertices still present.
            var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForWrite);
            var stillOk = leader.ContentType == ContentType.BlockContent &&
                !leader.BlockContentId.IsNull &&
                leader.BlockConnectionType == BlockConnectionType.ConnectBase &&
                leader.GetLeaderIndexes().Cast<int>().Any();
            leader.RecordGraphicsModified(true);
            ok &= stillOk;
            editor.WriteMessage(
                $"\nReopenVerify {cases[i].Token}: {(stillOk ? "OK" : "FAIL")}");
        }

        _ = database;
        return ok;
    }

    private static int IndexOf(IReadOnlyList<VerifyCase> cases, string token)
    {
        for (var i = 0; i < cases.Count; i++)
        {
            if (string.Equals(cases[i].Token, token, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void WriteCase(
        Editor editor,
        VerifyCase proofCase,
        AutoCadFramedBlockContentAnnotationResult result,
        bool ok)
    {
        editor.WriteMessage(
            $"\n[{(ok ? "OK" : "FAIL")}] {proofCase.Token}: " +
            $"id={result.LeaderId} handle={result.LeaderHandle ?? "-"} " +
            $"btr={result.ResolvedBlockName ?? "-"} " +
            $"content={result.ContentType} leaders={result.LeaderCount} " +
            $"verts={result.VertexCount} tags=[{string.Join(",", result.AttributeTags)}] " +
            $"itemH={result.ItemAttrRefHeightMm:0.###} " +
            $"itemEff={result.ItemEffectiveModelHeightMm:0.###} " +
            $"dimH={result.DimensionAttrRefHeightMm:0.###} " +
            $"dimEff={result.DimensionEffectiveModelHeightMm:0.###} " +
            $"blockScale={result.BlockScale:0.###} " +
            $"attach={Format(result.AttachmentWorld)} " +
            $"knee={Format(result.KneeWorld)} " +
            $"landing={Format(result.LandingEndWorld)} " +
            $"clearGap={result.RowClearGapModelMm:0.###} " +
            $"stab={result.StabilizationMode} " +
            $"driftA/K/L={result.AttachmentDriftMm:E2}/" +
            $"{result.KneeDriftMm:E2}/{result.LandingDriftMm:E2} " +
            $"reason={result.DiagnosticReason}");
    }

    private static string Format(Point3d? point) =>
        point is Point3d p
            ? $"({p.X:0.#},{p.Y:0.#})"
            : "-";

    private static ObjectId EnsureVerifyLayer(
        Database database,
        Transaction transaction)
    {
        var layerTable = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        if (layerTable.Has(VerifyLayerName))
        {
            return layerTable[VerifyLayerName];
        }

        layerTable.UpgradeOpen();
        var layer = new LayerTableRecord
        {
            Name = VerifyLayerName,
            Color = AcColor.FromColorIndex(ColorMethod.ByAci, 7),
        };
        var id = layerTable.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
        return id;
    }
}
#endif
