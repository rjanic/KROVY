#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG one-command FBC autotest: create matrix, wrong-BTR content-side probe,
/// knee-only synthetic crossing, shared dogleg → content-side normalize,
/// persistence reopen, and lifecycle deferred processor drain — no entity
/// picking, no nested commands. Fixture failures are not product failures.
/// </summary>
internal static class AutoCadFramedBlockContentAutotestService
{
    internal const string DebugRegAppName = "AK_DEV_FBC_AUTOTEST";
    private const string AutotestLayerName = "AK_DEV_FBC_AUTOTEST";
    private const string CommandBanner = "AK_DEV_FBC_AUTOTEST_ALL";

    private static string? _lastRunId;

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
        // All own AUTOTEST markers across run IDs — never other DEBUG/user entities.
        var (found, erased) = EraseMarkedAutotestEntities(
            database,
            transaction,
            runIdOnly: null);
        transaction.Commit();
        editor.WriteMessage("\n=== AK_DEV_FBC_AUTOTEST_CLEAN ===");
        editor.WriteMessage("\nrunId=(all own autotest markers)");
        editor.WriteMessage($"\noldAutotestEntitiesFound={found}");
        editor.WriteMessage($"\noldAutotestEntitiesErased={erased}");
    }

    public static void RunAll()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var cases = TimberFramedBlockContentAutotestRules.BuildCases();
        if (!TimberFramedBlockContentAutotestRules.TryValidateCoverage(
                cases,
                out var coverageNote))
        {
            editor.WriteMessage($"\n{CommandBanner}: FAIL coverage {coverageNote}");
            return;
        }

        var runId = TimberFramedBlockContentAutotestRules.CreateRunId(DateTime.UtcNow);
        _lastRunId = runId;
        var summary = new TimberFramedBlockContentAutotestSummary(runId, cases.Count);
        var trackedLeaders =
            new List<(TimberFramedBlockContentAutotestCase Case, ObjectId LeaderId, bool CreateOk)>();

        TimberFramedBlockContentStretchNormalizeExternalState? isolationSnapshot = null;
        try
        {
            isolationSnapshot =
                AutoCadFramedBlockContentStretchNormalizeLifecycleService
                    .BeginAutotestIsolation(document);
            var session =
                AutoCadFramedBlockContentStretchNormalizeLifecycleService
                    .GetOrCreateSession(document);
            var isolationOk =
                !session.TraceEnabled &&
                !session.ProofEnabled &&
                session.QueuedCount == 0 &&
                string.IsNullOrEmpty(session.ActiveCommandName) &&
                !session.IsProcessing;
            if (!isolationOk)
            {
                summary.RecordFailure(
                    "(runner)",
                    "RunnerIsolation",
                    "Trace=False Proof=False empty queue idle",
                    $"trace={session.TraceEnabled} proof={session.ProofEnabled} " +
                    $"queued={session.QueuedCount} active='{session.ActiveCommandName}' " +
                    $"processing={session.IsProcessing}",
                    TimberFramedBlockContentAutotestCategory.RunnerIsolation);
            }
            else
            {
                summary.AppendDetail(
                    "RunnerIsolation begin Trace=False Proof=False queue cleared");
            }

            using (document.LockDocument())
            {
                var database = document.Database;
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var (oldFound, oldErased) = EraseMarkedAutotestEntities(
                        database,
                        transaction,
                        runIdOnly: null);
                    summary.AppendDetail(
                        $"cleanup_previous found={oldFound} erased={oldErased}");

                    var textStyleId = database.Textstyle;
                    var textStyle = (TextStyleTableRecord)transaction.GetObject(
                        textStyleId,
                        OpenMode.ForRead);
                    var styleName = string.IsNullOrWhiteSpace(textStyle.Name)
                        ? "Standard"
                        : textStyle.Name;
                    var layerId = EnsureAutotestLayer(database, transaction);

                    for (var index = 0; index < cases.Count; index++)
                    {
                        var testCase = cases[index];
                        var createOk = false;
                        try
                        {
                            var leaderId = RunCase(
                                document,
                                database,
                                transaction,
                                testCase,
                                index,
                                styleName,
                                textStyleId,
                                layerId,
                                runId,
                                summary,
                                out createOk);
                            if (!leaderId.IsNull)
                            {
                                trackedLeaders.Add((testCase, leaderId, createOk));
                            }
                        }
                        catch (Exception exception)
                        {
                            summary.RecordFailure(
                                testCase.Key,
                                "Exception",
                                "no exception",
                                exception.Message,
                                TimberFramedBlockContentAutotestCategory.CreatePlacement);
                        }
                    }

                    ValidateExternalInventory(
                        database,
                        transaction,
                        trackedLeaders.Count,
                        summary);

                    transaction.Commit();
                }

                RunPersistencePhases(database, trackedLeaders, summary);

                var lifecycleCase = trackedLeaders.FirstOrDefault(t =>
                    t.Case.PreferLifecycleProcessor && t.CreateOk);
                if (lifecycleCase.LeaderId.IsNull)
                {
                    lifecycleCase = trackedLeaders.FirstOrDefault(t =>
                        t.Case.Presentation ==
                            TimberFramedBlockContentPresentation.Combined &&
                        t.CreateOk);
                }

                if (!lifecycleCase.LeaderId.IsNull)
                {
                    try
                    {
                        RunLifecycleProcessorPhase(
                            document,
                            lifecycleCase.Case,
                            lifecycleCase.LeaderId,
                            summary);
                    }
                    catch (Exception exception)
                    {
                        summary.RecordFailure(
                            lifecycleCase.Case.Key,
                            "LifecycleProcessor",
                            "no exception",
                            exception.Message,
                            TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                    }
                }
                else
                {
                    summary.RecordFixtureFailure(
                        "(lifecycle)",
                        "LifecycleProcessor",
                        "Combined leader available",
                        "none",
                        TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                }

                summary.SealCoverageCategories(cases);

                if (summary.OverallPass)
                {
                    using var cleanTxn = database.TransactionManager.StartTransaction();
                    EraseMarkedAutotestEntities(database, cleanTxn, runIdOnly: runId);
                    cleanTxn.Commit();
                    summary.AppendDetail("auto_cleanup=PASS erased current run");
                }
                else
                {
                    summary.AppendDetail(
                        "auto_cleanup=SKIPPED leave failed cases for visual check");
                }
            }
        }
        finally
        {
            if (isolationSnapshot is TimberFramedBlockContentStretchNormalizeExternalState snap)
            {
                AutoCadFramedBlockContentStretchNormalizeLifecycleService
                    .EndAutotestIsolation(document, snap);
            }

            summary.ExternalLifecycleMutations =
                AutoCadFramedBlockContentStretchNormalizeLifecycleService
                    .ExternalLifecycleMutationsDuringAutotest;
            if (summary.ExternalLifecycleMutations != 0)
            {
                summary.RecordFailure(
                    "(runner)",
                    "RunnerIsolation",
                    "ExternalLifecycleMutations=0",
                    $"ExternalLifecycleMutations={summary.ExternalLifecycleMutations}",
                    TimberFramedBlockContentAutotestCategory.RunnerIsolation);
            }
            else
            {
                var isolationFailed = summary.Failures.Any(f =>
                    f.Phase.StartsWith("RunnerIsolation", StringComparison.Ordinal));
                if (!isolationFailed)
                {
                    summary.MarkCategory(
                        TimberFramedBlockContentAutotestCategory.RunnerIsolation,
                        true);
                    summary.AppendDetail(
                        "RunnerIsolation=PASS ExternalLifecycleMutations=0");
                }
            }
        }

        var detailPath = WriteDetailLog(summary);
        summary.DetailLogPath = detailPath;
        foreach (var line in summary.FormatConsoleSummary()
                     .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            editor.WriteMessage("\n" + line);
        }
    }

    private static ObjectId RunCase(
        Document document,
        Database database,
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        int caseIndex,
        string styleName,
        ObjectId textStyleId,
        ObjectId layerId,
        string runId,
        TimberFramedBlockContentAutotestSummary summary,
        out bool createOk)
    {
        _ = document;
        createOk = false;
        var request = BuildRequest(
            testCase,
            caseIndex,
            styleName,
            textStyleId,
            layerId);
        var created = AutoCadFramedBlockContentAnnotationService.Create(
            database,
            transaction,
            request);
        if (!created.Succeeded ||
            created.LeaderId is not ObjectId leaderId ||
            leaderId.IsNull)
        {
            summary.RecordFailure(
                testCase.Key,
                "Create",
                "Succeeded MLeader",
                created.DiagnosticReason ?? "create failed",
                MapCreateCategory(testCase));
            return ObjectId.Null;
        }

        MarkAutotestEntity(database, transaction, leaderId, runId, testCase.Key);
        var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForRead);
        var handle = leader.ObjectId.Handle.ToString();
        var attrsBefore = CaptureAttrSnapshot(transaction, leader);

        if (leader.ContentType != ContentType.BlockContent)
        {
            summary.RecordFailure(
                testCase.Key,
                "Create",
                "BlockContent",
                leader.ContentType.ToString(),
                MapCreateCategory(testCase));
            return leaderId;
        }

        if (testCase.Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            if (!ValidateCreateReferenceFinalWorldAngles(
                    testCase,
                    handle,
                    summary))
            {
                return leaderId;
            }

            if (!ValidateCreateWholeAnnotationHalfTurn(
                    transaction,
                    testCase,
                    leader,
                    created.ReferencePresentationRevision,
                    summary))
            {
                return leaderId;
            }

            if (!ValidateAdoptedReferenceVerticalGripBoundary(
                    database,
                    transaction,
                    testCase,
                    request,
                    leader,
                    handle,
                    runId,
                    summary))
            {
                return leaderId;
            }

            if (!TryEvaluatePlacement(
                    transaction,
                    leader,
                    out var createEval,
                    out _,
                    out var createNote) ||
                !createEval.Current.IsCorrect)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "CreatePlacement",
                    "currentPlacementCorrect=True",
                    createNote + " " + (createEval.Current.Reason ?? string.Empty),
                    TimberFramedBlockContentAutotestCategory.CreatePlacement);
                return leaderId;
            }

            summary.RecordPass(testCase.Key, "CreatePlacement", "K→D→I correct");
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.CreatePlacement,
                true);
            MarkAngleScaleCategories(testCase, summary, true);
            createOk = true;

            if (!ValidateExistingReferenceUpdateAndSecondRefresh(
                    database,
                    transaction,
                    testCase,
                    request,
                    leader,
                    handle,
                    summary))
            {
                return leaderId;
            }

            leader.UpgradeOpen();
            var contentNoOp =
                AutoCadFramedBlockContentNormalizeContentSideService
                    .TryNormalizeContentSide(database, transaction, leader);
            if (contentNoOp.Changed ||
                contentNoOp.BeforeBlockContentId != contentNoOp.AfterBlockContentId)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "CreateContentSideNoOp",
                    "changed=False BlockContentId unchanged",
                    $"changed={contentNoOp.Changed} reason={contentNoOp.Reason}",
                    TimberFramedBlockContentAutotestCategory.CreatePlacement);
                return leaderId;
            }

            summary.RecordPass(
                testCase.Key,
                "CreateContentSideNoOp",
                contentNoOp.Reason);

            if (!RunContentSideWrongBtrCycle(
                    database,
                    transaction,
                    testCase,
                    leader,
                    handle,
                    attrsBefore,
                    summary))
            {
                return leaderId;
            }

            if (!RunCrossingNormalizeCycle(
                    database,
                    transaction,
                    testCase,
                    leader,
                    handle,
                    attrsBefore,
                    firstCrossingIsRightToLeft:
                        testCase.Side == TimberLeaderHorizontalSide.Right,
                    summary))
            {
                return leaderId;
            }

            if (!RunCrossingNormalizeCycle(
                    database,
                    transaction,
                    testCase,
                    leader,
                    handle,
                    attrsBefore,
                    firstCrossingIsRightToLeft:
                        testCase.Side != TimberLeaderHorizontalSide.Right,
                    summary))
            {
                return leaderId;
            }
        }
        else
        {
            if (created.AttributeTags.Contains("WIDTH") ||
                created.AttributeTags.Contains("HEIGHT"))
            {
                summary.RecordFailure(
                    testCase.Key,
                    "ItemOnly",
                    "ITEM_NO only",
                    string.Join(",", created.AttributeTags),
                    TimberFramedBlockContentAutotestCategory.ItemOnly);
                return leaderId;
            }

            leader.UpgradeOpen();
            var beforeBlock = leader.BlockContentId;
            var dogleg = AutoCadFramedBlockContentNormalizeDoglegService
                .TryNormalizeDogleg(transaction, leader);
            var afterBlock = leader.BlockContentId;
            if (beforeBlock != afterBlock)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "ItemOnly",
                    "BlockContentId unchanged",
                    "swapped",
                    TimberFramedBlockContentAutotestCategory.ItemOnly);
                return leaderId;
            }

            summary.RecordPass(
                testCase.Key,
                "ItemOnly",
                $"dogleg changed={dogleg.Changed} contentSide=N/A");
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.ItemOnly,
                true);
            MarkAngleScaleCategories(testCase, summary, true);
            createOk = true;
        }

        return leaderId;
    }

    private static bool ValidateExistingReferenceUpdateAndSecondRefresh(
        Database database,
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        AutoCadFramedBlockContentAnnotationRequest request,
        MLeader leader,
        string originalHandle,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var sourcePhysical =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                testCase.ElementAxisDegrees * Math.PI / 180d);
        if (Math.Abs(Math.Abs(sourcePhysical) - (Math.PI / 2d)) <= 1e-9d)
        {
            return ValidateExistingVerticalWholeAnnotationRefreshTwice(
                transaction,
                testCase,
                leader,
                originalHandle,
                summary);
        }

        // Test expectation is intentionally independent of the production
        // classifier. The old test reused that classifier and silently skipped
        // the real 270°/-90° CREATE case when production misclassified it.
        if (!IsExpectedReferencePresentationCase(testCase))
        {
            return true;
        }

        if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var adoptedCreateWorld,
                    out var createNote))
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingReferenceUpdate",
                "measurable create world",
                createNote,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        leader.UpgradeOpen();
        var wrongWorld = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            adoptedCreateWorld + Math.PI);
        var wrongBlockRotation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                leader.BlockRotation + Math.PI);
        leader.BlockRotation = wrongBlockRotation;

        var cos = Math.Cos(request.ElementAxisRadians);
        var sin = Math.Sin(request.ElementAxisRadians);
        var start = new Point3d(
            request.AttachmentX - (cos * 1000d),
            request.AttachmentY - (sin * 1000d),
            0d);
        var end = new Point3d(
            request.AttachmentX + (cos * 1000d),
            request.AttachmentY + (sin * 1000d),
            0d);
        using var source = new Line(start, end);
        var revision = ElementLabelService
            .ApplyG5CombinedReferencePresentationAfterRefresh(
                database,
                transaction,
                leader,
                source,
                request,
                sourceHandle: "AUTOTEST-" + testCase.Key,
                currentRevision: 0);
        var hasFirstRefreshWorld =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var firstRefreshWorld,
                    out var firstRefreshNote);
        if (revision !=
                TimberFramedBlockContentReadableOrientationRules
                    .ReferencePresentationRevision ||
            !hasFirstRefreshWorld)
        {
#if DEBUG
            var hasTrace = AutoCadFramedBlockContentAnnotationService
                .TryGetCreatePresentationTrace(
                    leader.ObjectId.Handle.ToString(),
                    out var failedTrace);
#endif
            summary.RecordFailure(
                testCase.Key,
                "ExistingReferenceUpdate",
                "revision=1 and measurable final world",
                $"revision={revision}; world={firstRefreshNote}; " +
#if DEBUG
                (hasTrace
                    ? $"trace={failedTrace.MeasurementNote}; " +
                      $"sequence={failedTrace.PresentationOperationSequence}; " +
                      $"BR={failedTrace.BlockRotationBefore * 180d / Math.PI:R}" +
                      $"->{failedTrace.BlockRotationRequested * 180d / Math.PI:R}" +
                      $"->{failedTrace.BlockRotationAfter * 180d / Math.PI:R}; " +
                      $"world={failedTrace.FrameWorldOrientationBefore * 180d / Math.PI:R}" +
                      $"->{failedTrace.VerticalRuleOutput * 180d / Math.PI:R}" +
                      $"->{failedTrace.FrameWorldOrientationAfter * 180d / Math.PI:R}"
                    : "trace=<unavailable>") +
#else
                "trace=<debug-only>" +
#endif
                string.Empty,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        var expected = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            wrongWorld + Math.PI);
        var firstDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                firstRefreshWorld - expected));
        var blockBeforeSecondRefresh = leader.BlockRotation;
        var revisionAfterSecond = ElementLabelService
            .ApplyG5CombinedReferencePresentationAfterRefresh(
                database,
                transaction,
                leader,
                source,
                request,
                sourceHandle: "AUTOTEST-" + testCase.Key,
                currentRevision: revision);
        var hasSecondWorld =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var secondRefreshWorld,
                    out var secondRefreshNote);
        var secondDelta = hasSecondWorld
            ? Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                secondRefreshWorld - firstRefreshWorld))
            : double.PositiveInfinity;
        var blockDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                leader.BlockRotation - blockBeforeSecondRefresh));
        var sameHandle = string.Equals(
            originalHandle,
            leader.ObjectId.Handle.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var placementOk =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryEvaluate(
                    transaction,
                    leader,
                    out var placement,
                    out _,
                    out var placementNote) &&
            placement.Current.IsCorrect;
        var itemWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
                transaction,
                leader,
                TimberFramedBlockContentDefinitionRules.ItemNoTag);
        var widthWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
                transaction,
                leader,
                TimberFramedBlockContentDefinitionRules.WidthTag);
        var heightWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
                transaction,
                leader,
                TimberFramedBlockContentDefinitionRules.HeightTag);
        var attributesCoherent =
            itemWorld is double item &&
            widthWorld is double width &&
            heightWorld is double height &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                item - secondRefreshWorld)) <= 1e-6d &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                width - secondRefreshWorld)) <= 1e-6d &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                height - secondRefreshWorld)) <= 1e-6d;
        const double tolerance = 1e-6d;
        if (firstDelta > tolerance ||
            revisionAfterSecond != revision ||
            secondDelta > tolerance ||
            blockDelta > tolerance ||
            !sameHandle ||
            !placementOk ||
            !attributesCoherent)
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingReferenceUpdate",
                "world+180; second refresh delta=0; same handle; K->D->F",
                $"firstDeltaDeg={firstDelta * 180d / Math.PI:R}; " +
                $"secondDeltaDeg={secondDelta * 180d / Math.PI:R}; " +
                $"blockDeltaDeg={blockDelta * 180d / Math.PI:R}; " +
                $"revision={revision}->{revisionAfterSecond}; " +
                $"sameHandle={sameHandle}; placement={placementNote}; " +
                $"attrsCoherent={attributesCoherent}; " +
                $"secondWorld={secondRefreshNote}",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            "ExistingReferenceUpdate",
            $"before={wrongWorld * 180d / Math.PI:R}; " +
            $"after={firstRefreshWorld * 180d / Math.PI:R}; " +
            "secondRefreshDelta=0; sameHandle=True; towardKnee=True");
        return true;
    }

    private static bool ValidateExistingVerticalWholeAnnotationRefreshTwice(
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        MLeader leader,
        string originalHandle,
        TimberFramedBlockContentAutotestSummary summary)
    {
        leader.UpgradeOpen();
        var legacyWorldReason = string.Empty;
        if (!AutoCadWholeMLeaderHalfTurnService.TryApplyRigidHalfTurn(
                transaction,
                leader,
                out var legacySetup,
                out var setupReason) ||
            !AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var legacyWorld,
                    out legacyWorldReason))
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingVerticalWholeRefresh",
                "legacy unadopted setup",
                setupReason + "; " + legacyWorldReason,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        var firstWorldReason = string.Empty;
        if (!AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(
                transaction,
                leader,
                testCase.ElementAxisDegrees * Math.PI / 180d,
                TimberFramedBlockContentReadableOrientationRules
                    .ReferencePresentationRevision,
                "AutotestExistingRefresh1",
                out var first,
                out var firstReason) ||
            !AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var firstWorld,
                    out firstWorldReason))
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingVerticalWholeRefresh",
                "first refresh applies one whole transform",
                firstReason + "; " + firstWorldReason,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        var blockBeforeSecond = leader.BlockRotation;
        var secondWorldReason = string.Empty;
        if (!AutoCadWholeMLeaderHalfTurnService.TryApplyRequiredState(
                transaction,
                leader,
                testCase.ElementAxisDegrees * Math.PI / 180d,
                first.Decision.RevisionAfter,
                "AutotestExistingRefresh2",
                out var second,
                out var secondReason) ||
            !AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var secondWorld,
                    out secondWorldReason))
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingVerticalWholeRefresh",
                "second refresh is a no-op",
                secondReason + "; " + secondWorldReason,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        var expectedFirstWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                legacyWorld + Math.PI);
        var sameHandle = string.Equals(
            originalHandle,
            leader.ObjectId.Handle.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var placementOk =
            AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var placement,
                out _,
                out _) &&
            placement.Current.IsCorrect;
        const double tolerance = 1e-6d;
        var pass = legacySetup.AttachmentDelta <= tolerance &&
            first.Transform.TransformApplied &&
            first.Transform.AttachmentDelta <= tolerance &&
            first.Decision.RevisionAfter ==
                TimberFramedBlockContentWholeAnnotationHalfTurnRules
                    .AppliedStateRevision &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                firstWorld - expectedFirstWorld)) <= tolerance &&
            !second.Transform.TransformApplied &&
            second.Transform.AttachmentDelta <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                secondWorld - firstWorld)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                leader.BlockRotation - blockBeforeSecond)) <= tolerance &&
            sameHandle &&
            placementOk;
        if (!pass)
        {
            summary.RecordFailure(
                testCase.Key,
                "ExistingVerticalWholeRefresh",
                "first=180 second=0 attachmentDelta=0 sameHandle K→D→I",
                $"firstTransform={first.Transform.TransformApplied}; " +
                $"secondTransform={second.Transform.TransformApplied}; " +
                $"attachment={first.Transform.AttachmentDelta:R}/" +
                $"{second.Transform.AttachmentDelta:R}; " +
                $"world={legacyWorld * 180d / Math.PI:R}->" +
                $"{firstWorld * 180d / Math.PI:R}->" +
                $"{secondWorld * 180d / Math.PI:R}; " +
                $"sameHandle={sameHandle}; placement={placementOk}",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            "ExistingVerticalWholeRefresh",
            "first refresh whole=180; second refresh whole=0; " +
            "attachmentDelta=0; sameHandle=True; K→D→I=True");
        return true;
    }

    private static bool ValidateCreateReferenceFinalWorldAngles(
        TimberFramedBlockContentAutotestCase testCase,
        string leaderHandle,
        TimberFramedBlockContentAutotestSummary summary)
    {
        if (!IsExpectedReferencePresentationCase(testCase))
        {
            return true;
        }

        var source = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            testCase.ElementAxisDegrees * Math.PI / 180d);

        if (!AutoCadFramedBlockContentAnnotationService
                .TryGetCreatePresentationTrace(leaderHandle, out var trace) ||
            trace.VerticalRuleInput is not double input ||
            trace.VerticalRuleOutput is not double output ||
            trace.FrameWorldOrientationBefore is not double frameBefore ||
            trace.FrameWorldOrientationAfter is not double frameAfter)
        {
            summary.RecordFailure(
                testCase.Key,
                "CreateReferenceFinalWorldAngles",
                "CREATE trace with measured before/after half-turn",
                "trace unavailable/incomplete",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        _ = TryGetExpectedReferenceCreateContract(
            source,
            frameBefore,
            out var expected,
            out var expectedHalfTurn);
        var sourceDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                trace.SourcePhysicalAxisAngle - source));
        var frameDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                frameAfter - expected));
        var ruleInputDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(input - frameBefore));
        var ruleOutputDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(output - expected));
        var itemDelta = trace.ItemTextWorldAngle is double item
            ? Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                item - frameAfter))
            : double.PositiveInfinity;
        var widthDelta = trace.WidthTextWorldAngle is double width
            ? Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                width - frameAfter))
            : double.PositiveInfinity;
        var heightDelta = trace.HeightTextWorldAngle is double height
            ? Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                height - frameAfter))
            : double.PositiveInfinity;
        var expectedBlockDelta = expectedHalfTurn ? Math.PI : 0d;
        var requestedBlockDelta = Math.Abs(
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                trace.BlockRotationRequested - trace.BlockRotationBefore)) -
            expectedBlockDelta);
        var appliedBlockDelta = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                trace.BlockRotationAfter - trace.BlockRotationRequested));
        const double tolerance = 1e-6d;
        if (sourceDelta > tolerance ||
            frameDelta > tolerance ||
            ruleInputDelta > tolerance ||
            ruleOutputDelta > tolerance ||
            itemDelta > tolerance ||
            widthDelta > tolerance ||
            heightDelta > tolerance ||
            trace.AppliedHalfTurn != expectedHalfTurn ||
            requestedBlockDelta > tolerance ||
            appliedBlockDelta > tolerance ||
            trace.ReferenceRevisionAfter !=
                TimberFramedBlockContentReadableOrientationRules
                    .ReferencePresentationRevision ||
            !trace.DimensionsTowardKneeAfter)
        {
            summary.RecordFailure(
                testCase.Key,
                "CreateReferenceFinalWorldAngles",
                "explicit source-sign world contract + coherent attributes",
                $"input={input * 180d / Math.PI:R}; " +
                $"output={output * 180d / Math.PI:R}; " +
                $"frameBefore={frameBefore * 180d / Math.PI:R}; " +
                $"frameAfter={frameAfter * 180d / Math.PI:R}; " +
                $"item={trace.ItemTextWorldAngle * 180d / Math.PI:R}; " +
                $"width={trace.WidthTextWorldAngle * 180d / Math.PI:R}; " +
                $"height={trace.HeightTextWorldAngle * 180d / Math.PI:R}; " +
                $"BTR={trace.BlockNameBeforeCorrection}->{trace.BlockNameAfterCorrection}; " +
                $"towardKnee={trace.DimensionsTowardKneeAfter}; " +
                trace.MeasurementNote,
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            "CreateReferenceFinalWorldAngles",
            $"source={testCase.ElementAxisDegrees:R}; " +
            $"input={input * 180d / Math.PI:R}; " +
            $"output={frameAfter * 180d / Math.PI:R}; " +
            $"BR={trace.BlockRotationBefore * 180d / Math.PI:R}" +
            $"->{trace.BlockRotationAfter * 180d / Math.PI:R}; " +
            $"BTR={trace.BlockNameBeforeCorrection}->{trace.BlockNameAfterCorrection}; " +
            $"towardKnee={trace.DimensionsTowardKneeAfter}");
        return true;
    }

    private static bool ValidateCreateWholeAnnotationHalfTurn(
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        MLeader leader,
        int resultingRevision,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var source = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            testCase.ElementAxisDegrees * Math.PI / 180d);
        // Independent host oracle: exact vertical modulo directions only.
        var expectedRequired =
            Math.Abs(Math.Abs(source) - (Math.PI / 2d)) <= 1e-9d;
        var handle = leader.ObjectId.Handle.ToString();
        if (!AutoCadWholeMLeaderHalfTurnService.TryGetLatestTrace(
                handle,
                out var trace))
        {
            summary.RecordFailure(
                testCase.Key,
                "CreateWholeAnnotationHalfTurn",
                "whole-annotation CREATE trace",
                "trace unavailable",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        var hasPlacement =
            AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var placement,
                out _,
                out var placementReason) &&
            placement.Current.IsCorrect;
        var expectedRotation = expectedRequired ? Math.PI : 0d;
        const double tolerance = 1e-6d;
        var pass = trace.Required == expectedRequired &&
            trace.AppliedBefore == false &&
            trace.AppliedAfter == expectedRequired &&
            trace.TransformAppliedThisOperation == expectedRequired &&
            Math.Abs(trace.RotationRadians - expectedRotation) <= tolerance &&
            trace.AttachmentDelta <= tolerance &&
            trace.DimensionsTowardKneeDotBefore > 0d &&
            trace.DimensionsTowardKneeDotAfter > 0d &&
            resultingRevision == trace.RevisionAfter &&
            hasPlacement;
        if (!pass)
        {
            summary.RecordFailure(
                testCase.Key,
                "CreateWholeAnnotationHalfTurn",
                "vertical=one rigid 180; nonvertical=no transform; attachmentDelta=0",
                $"required={trace.Required}; before={trace.AppliedBefore}; " +
                $"after={trace.AppliedAfter}; " +
                $"transform={trace.TransformAppliedThisOperation}; " +
                $"rotationDeg={trace.RotationRadians * 180d / Math.PI:R}; " +
                $"attachmentDelta={trace.AttachmentDelta:R}; " +
                $"revision={trace.RevisionBefore}->{trace.RevisionAfter}/" +
                $"{resultingRevision}; placement={placementReason}",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            "CreateWholeAnnotationHalfTurn",
            $"required={expectedRequired}; " +
            $"transform={(expectedRequired ? 180d : 0d):R}; " +
            $"attachmentDelta={trace.AttachmentDelta:R}; K→D→I=True");
        return true;
    }

    private static bool IsExpectedReferencePresentationCase(
        TimberFramedBlockContentAutotestCase testCase)
    {
        var source = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            testCase.ElementAxisDegrees * Math.PI / 180d);
        var isVertical = Math.Abs(
            Math.Abs(source) - (Math.PI / 2d)) <= 1e-9d;
        var isOneEighty = Math.Abs(Math.Abs(source) - Math.PI) <= 1e-9d;
        return isVertical || isOneEighty;
    }

    private static bool TryGetExpectedReferenceCreateContract(
        double source,
        double frameBefore,
        out double expectedWorld,
        out bool expectedHalfTurn)
    {
        // Independent host oracle. Never derive this table from the production
        // ResolveCreateReferenceFinalWorldPresentation implementation.
        if (Math.Abs(source - (Math.PI / 2d)) <= 1e-9d)
        {
            expectedWorld = Math.PI / 2d;
            expectedHalfTurn = true;
            return true;
        }

        if (Math.Abs(source + (Math.PI / 2d)) <= 1e-9d)
        {
            expectedWorld = -Math.PI / 2d;
            expectedHalfTurn = false;
            return true;
        }

        if (Math.Abs(Math.Abs(source) - Math.PI) <= 1e-9d)
        {
            expectedWorld =
                TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                    frameBefore + Math.PI);
            expectedHalfTurn = true;
            return true;
        }

        expectedWorld = frameBefore;
        expectedHalfTurn = false;
        return false;
    }

    private static bool ValidateAdoptedReferenceVerticalGripBoundary(
        Database database,
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        AutoCadFramedBlockContentAnnotationRequest request,
        MLeader leader,
        string originalHandle,
        string runId,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var sourcePhysical =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                testCase.ElementAxisDegrees * Math.PI / 180d);
        if (Math.Abs(Math.Abs(sourcePhysical) - (Math.PI / 2d)) > 1e-9d)
        {
            return true;
        }

        var expected = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            sourcePhysical + Math.PI);
        var expectedDirection = new Vector3d(
            Math.Cos(expected),
            Math.Sin(expected),
            0d);

        var cos = Math.Cos(request.ElementAxisRadians);
        var sin = Math.Sin(request.ElementAxisRadians);
        var source = new Line(
            new Point3d(
                request.AttachmentX - (cos * 1000d),
                request.AttachmentY - (sin * 1000d),
                0d),
            new Point3d(
                request.AttachmentX + (cos * 1000d),
                request.AttachmentY + (sin * 1000d),
                0d));
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForWrite);
        var sourceId = modelSpace.AppendEntity(source);
        transaction.AddNewlyCreatedDBObject(source, true);
        MarkAutotestEntity(
            database,
            transaction,
            sourceId,
            runId,
            testCase.Key + "-SOURCE");

        leader.UpgradeOpen();
        ElementLabelStore.Write(
            leader,
            transaction,
            new ElementLabelData
            {
                SchemaVersion =
                    AutoCadFramedBlockContentProductionPolicy
                        .LabelMetadataSchemaVersion,
                ElementId = "AUTOTEST-" + testCase.Key,
                SourceHandle = sourceId.Handle.ToString(),
                RendererGeneration =
                    AutoCadFramedBlockContentProductionPolicy
                        .RendererGeneration,
                R3ReferencePresentationRevision =
                    TimberFramedBlockContentWholeAnnotationHalfTurnRules
                        .AppliedStateRevision,
            });

        var frame = leader.BlockPosition;
        var oldKnee = ReadKnee(leader);
        var landingLength = Math.Max(frame.DistanceTo(oldKnee), 1d);
        leader.SetLastVertex(
            GetPrimaryLeaderLineIndex(leader),
            new Point3d(
                frame.X - (expectedDirection.X * landingLength),
                frame.Y - (expectedDirection.Y * landingLength),
                frame.Z));
        // Native MLeader grip emulation may drag BlockPosition with the knee.
        // The reported production case has the frame fixed and final
        // knee→frame landing exactly +90°, so restore only that captured frame
        // point in the test fixture before invoking the real post-grip sync.
        var movedKnee = ReadKnee(leader);
        leader.BlockPosition = new Point3d(
            movedKnee.X + (expectedDirection.X * landingLength),
            movedKnee.Y + (expectedDirection.Y * landingLength),
            frame.Z);
        var primaryLeaderIndex = leader.GetLeaderIndexes().Cast<int>().First();
        leader.SetDogleg(primaryLeaderIndex, expectedDirection);

        var hasBeforeWorld =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var worldBefore,
                    out var beforeNote);
        var blockBefore = leader.BlockRotation;
        var outcome = AutoCadFramedBlockContentProductionGripNormalizeService
            .TryNormalizeR3ContentVariantOnlyForAutotest(
                database,
                transaction,
                leader,
                out var normalizeNote);
        var hasAfterWorld =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var worldAfter,
                    out var afterNote);
        var kneeAfter = ReadKnee(leader);
        var landingAfter = Math.Atan2(
            leader.BlockPosition.Y - kneeAfter.Y,
            leader.BlockPosition.X - kneeAfter.X);
        var placementOk =
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryEvaluate(
                    transaction,
                    leader,
                    out var placement,
                    out _,
                    out var placementNote) &&
            placement.Current.IsCorrect;
        var itemWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.ItemNoTag);
        var widthWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.WidthTag);
        var heightWorld = AutoCadFramedBlockContentAnnotationService
            .TryReadAttributeRotationRadians(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.HeightTag);
        var tolerance = 1e-6d;
        var pass = hasBeforeWorld &&
            hasAfterWorld &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                worldBefore - expected)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                worldAfter - expected)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                landingAfter - expected)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                leader.BlockRotation - blockBefore)) <= tolerance &&
            itemWorld is double item &&
            widthWorld is double width &&
            heightWorld is double height &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                item - expected)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                width - expected)) <= tolerance &&
            Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                height - expected)) <= tolerance &&
            placementOk &&
            string.Equals(
                originalHandle,
                leader.ObjectId.Handle.ToString(),
                StringComparison.OrdinalIgnoreCase) &&
            outcome is
                TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp or
                TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged;
        if (!pass)
        {
            summary.RecordFailure(
                testCase.Key,
                "AdoptedReferenceVerticalGripBoundary",
                "directed landing/world/attrs; BR delta=0; revision=current; K->D->I",
                $"before={beforeNote}; after={afterNote}; " +
                $"normalize={outcome}:{normalizeNote}; " +
                $"world={worldBefore * 180d / Math.PI:R}" +
                $"->{worldAfter * 180d / Math.PI:R}; " +
                $"landing={landingAfter * 180d / Math.PI:R}; " +
                $"BR={blockBefore * 180d / Math.PI:R}" +
                $"->{leader.BlockRotation * 180d / Math.PI:R}; " +
                $"placement={placementNote}",
                TimberFramedBlockContentAutotestCategory.CreatePlacement);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            "AdoptedReferenceVerticalGripBoundary",
            $"source={sourcePhysical * 180d / Math.PI:R}; " +
            $"landing={expected * 180d / Math.PI:R}; " +
            $"world={expected * 180d / Math.PI:R}; BR delta=0; " +
            $"revision={TimberFramedBlockContentReadableOrientationRules.ReferencePresentationRevision}; " +
            "towardKnee=True");
        return true;
    }

    /// <summary>
    /// A: inject opposite immutable R2 Combined BTR (DIMNX↔DIMPX) without
    /// moving attachment/knee/BP; require wrong current / correct mirrored;
    /// then shared content-side normalize restores original.
    /// </summary>
    private static bool RunContentSideWrongBtrCycle(
        Database database,
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        MLeader leader,
        string expectedHandle,
        Dictionary<string, AttrValue> attrsBaseline,
        TimberFramedBlockContentAutotestSummary summary)
    {
        const string phase = "ContentSideWrongBtr";
        var correctBlockId = leader.BlockContentId;
        var beforeAttachment = ReadAttachment(leader);
        var beforeKnee = ReadKnee(leader);
        var beforeBp = leader.BlockPosition;
        var beforeDoglegLength = leader.DoglegLength;
        var beforeDogleg = TryReadDogleg(leader);
        var beforeScale = leader.BlockScale;
        var beforeRotation = leader.BlockRotation;

        if (!TryInjectOppositeCombinedBtr(
                database,
                transaction,
                leader,
                attrsBaseline,
                out var injectNote))
        {
            summary.RecordFixtureFailure(
                testCase.Key,
                phase + "/Inject",
                "opposite DIMNX↔DIMPX applied",
                injectNote,
                TimberFramedBlockContentAutotestCategory.ContentSideService);
            return false;
        }

        var handleAfter = leader.ObjectId.Handle.ToString();
        if (!string.Equals(handleAfter, expectedHandle, StringComparison.Ordinal))
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/SameHandle",
                expectedHandle,
                handleAfter,
                TimberFramedBlockContentAutotestCategory.SameHandle);
            return false;
        }

        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.SameHandle,
            true);

        if (!TryEvaluatePlacement(
                transaction,
                leader,
                out var wrongEval,
                out var wrongPoints,
                out var wrongNote) ||
            wrongEval.Current.IsCorrect ||
            !wrongEval.Mirrored.IsCorrect)
        {
            summary.RecordFixtureFailure(
                testCase.Key,
                phase + "/PreNormalize",
                "currentPlacementCorrect=False mirroredPlacementCorrect=True",
                FormatEvalDiag(wrongEval, wrongPoints, wrongNote),
                TimberFramedBlockContentAutotestCategory.ContentSideService);
            // Best-effort restore so later phases can still run.
            TryNormalizeContentSideQuiet(database, transaction, leader);
            return false;
        }

        var contentResult =
            AutoCadFramedBlockContentNormalizeContentSideService
                .TryNormalizeContentSide(database, transaction, leader);
        if (!contentResult.Changed ||
            contentResult.AfterBlockContentId != correctBlockId)
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/Normalize",
                "changed=True original BlockContentId restored",
                $"changed={contentResult.Changed} after={contentResult.AfterBlockContentId} expected={correctBlockId} reason={contentResult.Reason}",
                TimberFramedBlockContentAutotestCategory.ContentSideService);
            return false;
        }

        if (!TryEvaluatePlacement(
                transaction,
                leader,
                out var fixedEval,
                out _,
                out var fixedNote) ||
            !fixedEval.Current.IsCorrect)
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/Normalize",
                "K→D→I correct",
                fixedNote + " " + (fixedEval.Current.Reason ?? string.Empty),
                TimberFramedBlockContentAutotestCategory.ContentSideService);
            return false;
        }

        if (ReadAttachment(leader).DistanceTo(beforeAttachment) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            ReadKnee(leader).DistanceTo(beforeKnee) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            leader.BlockPosition.DistanceTo(beforeBp) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            Math.Abs(leader.DoglegLength - beforeDoglegLength) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            Math.Abs(leader.BlockRotation - beforeRotation) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/ForbiddenDrift",
                "attachment/knee/BP/dogleg/rotation preserved",
                "geometry drifted",
                TimberFramedBlockContentAutotestCategory.ForbiddenDrift);
            return false;
        }

        var attrsAfter = CaptureAttrSnapshot(transaction, leader);
        if (!AttrsPreserved(attrsBaseline, attrsAfter, out var attrNote))
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/Attrs",
                "ITEM_NO/WIDTH/HEIGHT texts and heights preserved",
                attrNote,
                TimberFramedBlockContentAutotestCategory.ForbiddenDrift);
            return false;
        }

        if (!AttrsValuesMatch(attrsBaseline, attrsAfter, out var baselineNote))
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/Attrs",
                "ITEM_NO/WIDTH/HEIGHT texts and heights preserved",
                baselineNote,
                TimberFramedBlockContentAutotestCategory.ForbiddenDrift);
            return false;
        }

        var content2 =
            AutoCadFramedBlockContentNormalizeContentSideService
                .TryNormalizeContentSide(database, transaction, leader);
        if (content2.Changed)
        {
            summary.RecordFailure(
                testCase.Key,
                phase + "/SecondNormalize",
                "changed=False",
                $"changed={content2.Changed}",
                TimberFramedBlockContentAutotestCategory.ContentSideService);
            return false;
        }

        // Restore dogleg vector if AutoCAD rewrote it during attr reapply.
        if (beforeDogleg is Vector3d doglegVector)
        {
            RestoreDogleg(leader, doglegVector);
            leader.BlockScale = beforeScale;
            leader.BlockRotation = beforeRotation;
            leader.DoglegLength = beforeDoglegLength;
            leader.SetFirstVertex(GetPrimaryLeaderLineIndex(leader), beforeAttachment);
            leader.SetLastVertex(GetPrimaryLeaderLineIndex(leader), beforeKnee);
            leader.BlockPosition = beforeBp;
        }

        summary.RecordPass(
            testCase.Key,
            phase,
            "wrong BTR → content-side restored original");
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.ContentSideService,
            true);
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.ForbiddenDrift,
            true);
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift,
            true);
        return true;
    }

    /// <summary>
    /// B: knee-only synthetic crossing. Then dogleg B→C (BP may move) and
    /// content-side C→D (strict no BP drift). ForbiddenDrift baseline for
    /// attachment/knee is B; BP movement during dogleg is DoglegGeometry, not
    /// ContentSideForbiddenDrift.
    /// </summary>
    private static bool RunCrossingNormalizeCycle(
        Database database,
        Transaction transaction,
        TimberFramedBlockContentAutotestCase testCase,
        MLeader leader,
        string expectedHandle,
        Dictionary<string, AttrValue> attrsBaseline,
        bool firstCrossingIsRightToLeft,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var phaseCrossing = firstCrossingIsRightToLeft
            ? "RightToLeft"
            : "LeftToRight";
        var category = firstCrossingIsRightToLeft
            ? TimberFramedBlockContentAutotestCategory.RightToLeft
            : TimberFramedBlockContentAutotestCategory.LeftToRight;

        if (!TryEvaluatePlacement(
                transaction,
                leader,
                out var beforeEval,
                out var beforePoints,
                out var beforeNote))
        {
            summary.RecordFixtureFailure(
                testCase.Key,
                "SyntheticCrossingSetup",
                "pre-crossing evaluate",
                beforeNote,
                TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup);
            return false;
        }

        var beforeKnee = ReadKnee(leader);
        var beforeBp = leader.BlockPosition;
        var beforeAttachment = ReadAttachment(leader);
        var beforeBlockId = leader.BlockContentId;
        var beforeDiag = FormatEvalDiag(beforeEval, beforePoints, beforeNote);

        if (!TimberFramedBlockContentAutotestRules
                .TryComputeSyntheticKneeOnlyCrossing(
                    new TimberPlanarPoint(beforeKnee.X, beforeKnee.Y),
                    new TimberPlanarPoint(beforeBp.X, beforeBp.Y),
                    out var newKnee,
                    out var doglegDir))
        {
            summary.RecordFixtureFailure(
                testCase.Key,
                "SyntheticCrossingSetup",
                "knee-only crossing geometry",
                "degenerate",
                TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup);
            return false;
        }

        ApplyKneeOnlyCrossing(
            leader,
            beforeAttachment,
            new Point3d(newKnee.X, newKnee.Y, beforeKnee.Z),
            beforeBp,
            new Vector3d(doglegDir.X, doglegDir.Y, 0d));

        var handleAfter = leader.ObjectId.Handle.ToString();
        if (!string.Equals(handleAfter, expectedHandle, StringComparison.Ordinal))
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/SameHandle",
                expectedHandle,
                handleAfter,
                TimberFramedBlockContentAutotestCategory.SameHandle);
            return false;
        }

        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.SameHandle,
            true);

        if (!TryEvaluatePlacement(
                transaction,
                leader,
                out var wrongEval,
                out var wrongPoints,
                out var wrongNote) ||
            wrongEval.Current.IsCorrect ||
            !wrongEval.Mirrored.IsCorrect)
        {
            var afterDiag = FormatEvalDiag(wrongEval, wrongPoints, wrongNote);
            summary.RecordFixtureFailure(
                testCase.Key,
                "SyntheticCrossingSetup",
                "currentPlacementCorrect=False mirroredPlacementCorrect=True after knee-only reflect through I",
                $"before={beforeDiag}; after={afterDiag}; BlockPosition unchanged={beforeBp.DistanceTo(leader.BlockPosition) <= TimberFramedBlockContentAutotestRules.DriftToleranceMm}; BlockContentId unchanged={leader.BlockContentId == beforeBlockId}",
                TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup);
            summary.AppendDetail(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SyntheticCrossingSetup\t{0}\tphase={1}\tK_before=({2:R},{3:R})\tD_before=({4:R},{5:R})\tI_before=({6:R},{7:R})\tt_before={8:R}\tK_after=({9:R},{10:R})\tD_after=({11:R},{12:R})\tI_after=({13:R},{14:R})\tt_after={15:R}\tBP_before=({16:R},{17:R})\tBP_after=({18:R},{19:R})\tBlockContentId_before={20}\tBlockContentId_after={21}",
                    testCase.Key,
                    phaseCrossing,
                    beforePoints.Knee.X,
                    beforePoints.Knee.Y,
                    beforeEval.Current.DimensionColumnCenter.X,
                    beforeEval.Current.DimensionColumnCenter.Y,
                    beforePoints.ItemAlignment.X,
                    beforePoints.ItemAlignment.Y,
                    beforeEval.Current.ParameterT,
                    wrongPoints.Knee.X,
                    wrongPoints.Knee.Y,
                    wrongEval.Current.DimensionColumnCenter.X,
                    wrongEval.Current.DimensionColumnCenter.Y,
                    wrongPoints.ItemAlignment.X,
                    wrongPoints.ItemAlignment.Y,
                    wrongEval.Current.ParameterT,
                    beforeBp.X,
                    beforeBp.Y,
                    leader.BlockPosition.X,
                    leader.BlockPosition.Y,
                    beforeBlockId,
                    leader.BlockContentId));
            return false;
        }

        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup,
            true);

        // --- State B (post-crossing / pre-dogleg) ---
        var attB = ReadAttachment(leader);
        var kneeB = ReadKnee(leader);
        var bpB = leader.BlockPosition;
        var doglegLengthB = leader.DoglegLength;
        var scaleB = leader.BlockScale;
        var rotationB = leader.BlockRotation;
        var attrsB = CaptureAttrSnapshot(transaction, leader);
        var attrLocalsB = CaptureAttrRefLocals(transaction, leader);
        var connectOffsetB =
            TimberFramedBlockContentDoglegRules.MeasureConnectBaseContentOffsetMm(
                new TimberPlanarPoint(kneeB.X, kneeB.Y),
                new TimberPlanarPoint(bpB.X, bpB.Y),
                doglegLengthB);
        var towardB =
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                new TimberPlanarPoint(attB.X, attB.Y),
                new TimberPlanarPoint(kneeB.X, kneeB.Y),
                new TimberPlanarPoint(bpB.X, bpB.Y));

        // --- Dogleg B→C ---
        var doglegResult =
            AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                transaction,
                leader);

        var attC = ReadAttachment(leader);
        var kneeC = ReadKnee(leader);
        var bpC = leader.BlockPosition;
        var doglegLengthC = leader.DoglegLength;
        var doglegC = TryReadDogleg(leader);
        var attrLocalsC = CaptureAttrRefLocals(transaction, leader);
        var attDriftBC = attB.DistanceTo(attC);
        var kneeDriftBC = kneeB.DistanceTo(kneeC);
        var bpDriftBC = bpB.DistanceTo(bpC);
        var towardC =
            TimberFramedBlockContentDoglegRules.LandingPointsTowardAttachment(
                new TimberPlanarPoint(attC.X, attC.Y),
                new TimberPlanarPoint(kneeC.X, kneeC.Y),
                new TimberPlanarPoint(bpC.X, bpC.Y));
        var connectOffsetC =
            TimberFramedBlockContentDoglegRules.MeasureConnectBaseContentOffsetMm(
                new TimberPlanarPoint(kneeC.X, kneeC.Y),
                new TimberPlanarPoint(bpC.X, bpC.Y),
                doglegLengthC);

        summary.AppendDetail(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\tDogleg B→C\t{2}\toldBP=({3:R},{4:R})\tnewBP=({5:R},{6:R})\ttowardB={7}\ttowardC={8}\tconnectOffsetB={9:R}\tconnectOffsetC={10:R}\tdoglegChanged={11}\treason={12}",
                testCase.Key,
                phaseCrossing,
                TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
                    "B→C",
                    attDriftBC,
                    kneeDriftBC,
                    bpDriftBC),
                bpB.X,
                bpB.Y,
                bpC.X,
                bpC.Y,
                towardB,
                towardC,
                connectOffsetB,
                connectOffsetC,
                doglegResult.Changed,
                doglegResult.Reason));

        var doglegGeomOk = true;
        if (leader.ObjectId.Handle.ToString() != expectedHandle)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/SameHandle",
                expectedHandle,
                leader.ObjectId.Handle.ToString(),
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (attDriftBC > TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            kneeDriftBC > TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/AttKnee",
                "attachment/knee drift=0 during dogleg B→C",
                TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
                    "B→C",
                    attDriftBC,
                    kneeDriftBC,
                    bpDriftBC),
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (!AttrLocalsNearlyEqual(
                attrLocalsB,
                attrLocalsC,
                out var attrLocalNote))
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/AttrRefLocal",
                "AttrRef local drift≈0",
                attrLocalNote,
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (Math.Abs(doglegLengthC - doglegLengthB) >
            TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/DoglegLength",
                $"DoglegLength preserved ({doglegLengthB:R})",
                doglegLengthC.ToString("R", CultureInfo.InvariantCulture),
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (towardC)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/Landing",
                "landingPointsTowardAttachment=False after dogleg",
                "True",
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (doglegC is Vector3d doglegVectorC)
        {
            var unit = doglegVectorC.Length > 1e-9d
                ? doglegVectorC.GetNormal()
                : doglegVectorC;
            if (!TimberFramedBlockContentAutotestRules.BlockPositionLiesOnDoglegDirection(
                    new TimberPlanarPoint(kneeC.X, kneeC.Y),
                    new TimberPlanarPoint(bpC.X, bpC.Y),
                    new TimberPlanarVector(unit.X, unit.Y)))
            {
                summary.RecordFailure(
                    testCase.Key,
                    phaseCrossing + "/DoglegGeometry/BpOnDogleg",
                    "BP on final DoglegDirection",
                    $"knee=({kneeC.X:R},{kneeC.Y:R}) bp=({bpC.X:R},{bpC.Y:R}) dogleg=({unit.X:R},{unit.Y:R})",
                    TimberFramedBlockContentAutotestCategory.DoglegGeometry);
                doglegGeomOk = false;
            }
        }
        else
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/Dogleg",
                "DoglegDirection present",
                "missing",
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (Math.Abs(connectOffsetC - connectOffsetB) >
            TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/DoglegGeometry/ConnectBaseOffset",
                $"ConnectBase offset preserved ({connectOffsetB:R})",
                connectOffsetC.ToString("R", CultureInfo.InvariantCulture),
                TimberFramedBlockContentAutotestCategory.DoglegGeometry);
            doglegGeomOk = false;
        }

        if (doglegGeomOk)
        {
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.DoglegGeometry,
                true);
            summary.AppendDetail(
                $"{testCase.Key}\t{phaseCrossing}\tDoglegGeometry=PASS\tbpDriftB→C={bpDriftBC:R} (allowed when dogleg mirrors/repairs landing)");
        }
        else
        {
            return false;
        }

        // --- Content-side C→D (strict) ---
        var contentResult =
            AutoCadFramedBlockContentNormalizeContentSideService
                .TryNormalizeContentSide(database, transaction, leader);

        var attD = ReadAttachment(leader);
        var kneeD = ReadKnee(leader);
        var bpD = leader.BlockPosition;
        var doglegLengthD = leader.DoglegLength;
        var doglegD = TryReadDogleg(leader);
        var scaleD = leader.BlockScale;
        var rotationD = leader.BlockRotation;
        var attrsD = CaptureAttrSnapshot(transaction, leader);
        var attDriftCD = attC.DistanceTo(attD);
        var kneeDriftCD = kneeC.DistanceTo(kneeD);
        var bpDriftCD = bpC.DistanceTo(bpD);

        summary.AppendDetail(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\tContentSide C→D\t{2}\tcontentChanged={3}\treason={4}",
                testCase.Key,
                phaseCrossing,
                TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
                    "C→D",
                    attDriftCD,
                    kneeDriftCD,
                    bpDriftCD),
                contentResult.Changed,
                contentResult.Reason));

        var contentDriftOk = true;
        if (leader.ObjectId.Handle.ToString() != expectedHandle)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/ContentSideForbiddenDrift/SameHandle",
                expectedHandle,
                leader.ObjectId.Handle.ToString(),
                TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift);
            contentDriftOk = false;
        }

        if (attDriftCD > TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            kneeDriftCD > TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            bpDriftCD > TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/ContentSideForbiddenDrift",
                "attachment/knee/BP drift=0 during content-side C→D",
                TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
                    "C→D",
                    attDriftCD,
                    kneeDriftCD,
                    bpDriftCD),
                TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift);
            contentDriftOk = false;
        }

        if (Math.Abs(doglegLengthD - doglegLengthC) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            Math.Abs(rotationD - rotationB) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            Math.Abs(scaleD.X - scaleB.X) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            !DoglegVectorsEqual(doglegC, doglegD))
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/ContentSideForbiddenDrift/StableProps",
                "DoglegDirection/Length BlockScale BlockRotation unchanged",
                $"len {doglegLengthC:R}->{doglegLengthD:R} rot {rotationB:R}->{rotationD:R} scale {scaleB.X:R}->{scaleD.X:R}",
                TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift);
            contentDriftOk = false;
        }

        if (!AttrsPreserved(attrsB, attrsD, out var attrNoteCd))
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/ContentSideForbiddenDrift/Attrs",
                "AttrRef texts/heights preserved",
                attrNoteCd,
                TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift);
            contentDriftOk = false;
        }

        if (contentDriftOk)
        {
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift,
                true);
            // Legacy umbrella: content-side C→D is the only strict forbidden-drift.
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.ForbiddenDrift,
                true);
        }
        else
        {
            return false;
        }

        // --- Overall RTL/LTR at D ---
        if (!TryEvaluatePlacement(
                transaction,
                leader,
                out var fixedEval,
                out _,
                out var fixedNote) ||
            !fixedEval.Current.IsCorrect)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/Normalize",
                "currentPlacementCorrect=True after dogleg+content-side",
                fixedNote + " " + (fixedEval.Current.Reason ?? string.Empty),
                category);
            return false;
        }

        if (attB.DistanceTo(attD) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
            kneeB.DistanceTo(kneeD) >
                TimberFramedBlockContentAutotestRules.DriftToleranceMm)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/AttKneeFromB",
                "attachment/knee unchanged from crossing baseline B",
                TimberFramedBlockContentAutotestRules.FormatPhaseDrift(
                    "B→D",
                    attB.DistanceTo(attD),
                    kneeB.DistanceTo(kneeD),
                    bpB.DistanceTo(bpD)),
                category);
            return false;
        }

        if (!AttrsValuesMatch(attrsBaseline, attrsD, out var baselineNote))
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/AttrsBaseline",
                "create attr values preserved",
                baselineNote,
                category);
            return false;
        }

        var dogleg2 =
            AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                transaction,
                leader);
        var content2 =
            AutoCadFramedBlockContentNormalizeContentSideService
                .TryNormalizeContentSide(database, transaction, leader);
        if (dogleg2.Changed || content2.Changed)
        {
            summary.RecordFailure(
                testCase.Key,
                phaseCrossing + "/SecondNormalize",
                "dogleg changed=False content-side changed=False",
                $"dogleg={dogleg2.Changed} content={content2.Changed}",
                category);
            return false;
        }

        summary.RecordPass(
            testCase.Key,
            phaseCrossing,
            $"doglegChanged={doglegResult.Changed} bpDriftB→C={bpDriftBC:R} contentChanged={contentResult.Changed} bpDriftC→D={bpDriftCD:R}");
        summary.MarkCategory(category, true);
        return true;
    }

    private static void RunPersistencePhases(
        Database database,
        List<(TimberFramedBlockContentAutotestCase Case, ObjectId LeaderId, bool CreateOk)>
            trackedLeaders,
        TimberFramedBlockContentAutotestSummary summary)
    {
        foreach (var (testCase, leaderId, createOk) in trackedLeaders)
        {
            if (!testCase.RequirePersistence || !createOk || leaderId.IsNull)
            {
                continue;
            }

            try
            {
                ObjectId correctBlockId;
                Dictionary<string, AttrValue> attrsBaseline;
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    if (transaction.GetObject(leaderId, OpenMode.ForWrite, true) is not
                            MLeader leader ||
                        leader.IsErased)
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence",
                            "MLeader present",
                            "missing",
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        transaction.Commit();
                        continue;
                    }

                    // Ensure correct starting state before inject.
                    AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                        transaction,
                        leader);
                    AutoCadFramedBlockContentNormalizeContentSideService
                        .TryNormalizeContentSide(database, transaction, leader);

                    if (!TryEvaluatePlacement(
                            transaction,
                            leader,
                            out var startEval,
                            out _,
                            out var startNote) ||
                        !startEval.Current.IsCorrect)
                    {
                        summary.RecordFixtureFailure(
                            testCase.Key,
                            "Persistence/Start",
                            "correct K→D→I before inject",
                            startNote,
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        transaction.Commit();
                        continue;
                    }

                    correctBlockId = leader.BlockContentId;
                    attrsBaseline = CaptureAttrSnapshot(transaction, leader);
                    if (!TryInjectOppositeCombinedBtr(
                            database,
                            transaction,
                            leader,
                            attrsBaseline,
                            out var injectNote))
                    {
                        summary.RecordFixtureFailure(
                            testCase.Key,
                            "Persistence/Inject",
                            "opposite BTR applied",
                            injectNote,
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        transaction.Commit();
                        continue;
                    }

                    if (!TryEvaluatePlacement(
                            transaction,
                            leader,
                            out var wrongEval,
                            out var wrongPoints,
                            out var wrongNote) ||
                        wrongEval.Current.IsCorrect)
                    {
                        summary.RecordFixtureFailure(
                            testCase.Key,
                            "Persistence/PreNormalize",
                            "currentPlacementCorrect=False",
                            FormatEvalDiag(wrongEval, wrongPoints, wrongNote),
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        transaction.Commit();
                        continue;
                    }

                    var content =
                        AutoCadFramedBlockContentNormalizeContentSideService
                            .TryNormalizeContentSide(database, transaction, leader);
                    if (!content.Changed ||
                        content.AfterBlockContentId != correctBlockId ||
                        !TryEvaluatePlacement(
                            transaction,
                            leader,
                            out var fixedEval,
                            out _,
                            out _) ||
                        !fixedEval.Current.IsCorrect)
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence/Normalize",
                            "changed=True + K→D→I correct",
                            $"changed={content.Changed} reason={content.Reason}",
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        transaction.Commit();
                        continue;
                    }

                    transaction.Commit();
                }

                using (var reopen = database.TransactionManager.StartTransaction())
                {
                    if (reopen.GetObject(leaderId, OpenMode.ForRead, true) is not
                            MLeader leader ||
                        leader.IsErased)
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence/Reopen",
                            "MLeader reopened",
                            "missing",
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        reopen.Commit();
                        continue;
                    }

                    if (!TryEvaluatePlacement(
                            reopen,
                            leader,
                            out var evaluation,
                            out _,
                            out var note) ||
                        !evaluation.Current.IsCorrect)
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence/Reopen",
                            "K→D→I correct after reopen",
                            note,
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        reopen.Commit();
                        continue;
                    }

                    var attrs = CaptureAttrSnapshot(reopen, leader);
                    if (!AttrsPreserved(attrsBaseline, attrs, out var attrNote))
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence/Attrs",
                            "texts/heights preserved",
                            attrNote,
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        reopen.Commit();
                        continue;
                    }

                    leader.UpgradeOpen();
                    var dogleg =
                        AutoCadFramedBlockContentNormalizeDoglegService
                            .TryNormalizeDogleg(reopen, leader);
                    var content =
                        AutoCadFramedBlockContentNormalizeContentSideService
                            .TryNormalizeContentSide(database, reopen, leader);
                    if (dogleg.Changed || content.Changed)
                    {
                        summary.RecordFailure(
                            testCase.Key,
                            "Persistence/SecondNormalize",
                            "changed=False",
                            $"dogleg={dogleg.Changed} content={content.Changed}",
                            TimberFramedBlockContentAutotestCategory.Persistence);
                        reopen.Commit();
                        continue;
                    }

                    summary.RecordPass(
                        testCase.Key,
                        "Persistence",
                        "wrong BTR → normalize → reopen ok");
                    summary.MarkCategory(
                        TimberFramedBlockContentAutotestCategory.Persistence,
                        true);
                    reopen.Commit();
                }
            }
            catch (Exception exception)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "Persistence",
                    "no exception",
                    exception.Message,
                    TimberFramedBlockContentAutotestCategory.Persistence);
            }
        }
    }

    private static void RunLifecycleProcessorPhase(
        Document document,
        TimberFramedBlockContentAutotestCase testCase,
        ObjectId leaderId,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var database = document.Database;
        var session =
            AutoCadFramedBlockContentStretchNormalizeLifecycleService
                .GetOrCreateSession(document);

        // Fully isolated: keep external Proof/Trace OFF; empty queue; do not
        // ArmLifecycleTest (that would turn public proof on mid-run).
        if (session.TraceEnabled ||
            session.ProofEnabled ||
            session.QueuedCount != 0 ||
            session.IsProcessing)
        {
            summary.RecordFailure(
                testCase.Key,
                "LifecycleProcessor/Isolation",
                "Trace=False Proof=False empty queue guard released",
                $"trace={session.TraceEnabled} proof={session.ProofEnabled} " +
                $"queued={session.QueuedCount} processing={session.IsProcessing}",
                TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
            return;
        }

        ObjectId correctBlockId;
        using (document.LockDocument())
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            if (transaction.GetObject(leaderId, OpenMode.ForWrite, true) is not
                    MLeader leader ||
                leader.IsErased)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "LifecycleProcessor",
                    "leader present",
                    "missing",
                    TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                transaction.Commit();
                return;
            }

            AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                transaction,
                leader);
            AutoCadFramedBlockContentNormalizeContentSideService
                .TryNormalizeContentSide(database, transaction, leader);

            if (!TryEvaluatePlacement(
                    transaction,
                    leader,
                    out var startEval,
                    out _,
                    out var startNote) ||
                !startEval.Current.IsCorrect)
            {
                summary.RecordFixtureFailure(
                    testCase.Key,
                    "LifecycleProcessor/Start",
                    "correct Combined before inject",
                    startNote,
                    TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                transaction.Commit();
                return;
            }

            correctBlockId = leader.BlockContentId;
            var attrs = CaptureAttrSnapshot(transaction, leader);
            if (!TryInjectOppositeCombinedBtr(
                    database,
                    transaction,
                    leader,
                    attrs,
                    out var injectNote))
            {
                summary.RecordFixtureFailure(
                    testCase.Key,
                    "LifecycleProcessor/Inject",
                    "opposite DIMNX↔DIMPX",
                    injectNote,
                    TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                transaction.Commit();
                return;
            }

            transaction.Commit();
        }

        using (document.LockDocument())
        using (var reopen = database.TransactionManager.StartTransaction())
        {
            var preDrainNote = "missing";
            var preDrainOk = false;
            if (reopen.GetObject(leaderId, OpenMode.ForRead, true) is MLeader leader &&
                !leader.IsErased &&
                TryEvaluatePlacement(
                    reopen,
                    leader,
                    out var wrongEval,
                    out var wrongPoints,
                    out var wrongNote))
            {
                preDrainNote = FormatEvalDiag(wrongEval, wrongPoints, wrongNote);
                preDrainOk = !wrongEval.Current.IsCorrect;
            }

            if (!preDrainOk)
            {
                summary.RecordFixtureFailure(
                    testCase.Key,
                    "LifecycleProcessor/PreDrain",
                    "currentPlacementCorrect=False after reopen",
                    preDrainNote,
                    TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                reopen.Commit();
                return;
            }

            reopen.Commit();
        }

        // One internal drain — shared dogleg→content-side; no CommandWillStart/
        // CommandEnded event pair that would double-fire public subscriptions.
        AutoCadFramedBlockContentStretchNormalizeLifecycleService
            .EnqueueAndDrainNormalizeForTest(document, leaderId);

        if (session.QueuedCount != 0 || session.IsProcessing)
        {
            summary.RecordFailure(
                testCase.Key,
                "LifecycleProcessor",
                "queue empty and reentrancy released",
                $"queued={session.QueuedCount} processing={session.IsProcessing}",
                TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
            session.ForceReleaseProcessingGuard();
            session.ClearQueue();
            return;
        }

        AutoCadFramedBlockContentStretchNormalizeLifecycleService
            .DrainNormalizeForTestIfQueued(document);
        if (session.QueuedCount != 0 || session.IsProcessing)
        {
            summary.RecordFailure(
                testCase.Key,
                "LifecycleProcessor/SecondDrain",
                "no-op empty queue",
                $"queued={session.QueuedCount} processing={session.IsProcessing}",
                TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
            return;
        }

        // Isolation must still hold after internal drain (Proof/Trace stay off).
        if (session.TraceEnabled || session.ProofEnabled)
        {
            summary.RecordFailure(
                testCase.Key,
                "LifecycleProcessor/Isolation",
                "Proof/Trace remain False after internal drain",
                $"trace={session.TraceEnabled} proof={session.ProofEnabled}",
                TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
            return;
        }

        using (document.LockDocument())
        using (var read = database.TransactionManager.StartTransaction())
        {
            string note = "missing";
            var placementOk = false;
            var contentChangedExpected = false;
            if (read.GetObject(leaderId, OpenMode.ForRead, true) is MLeader leader &&
                !leader.IsErased &&
                TryEvaluatePlacement(read, leader, out var evaluation, out _, out note))
            {
                placementOk = evaluation.Current.IsCorrect;
                contentChangedExpected = leader.BlockContentId == correctBlockId;
            }

            if (!placementOk || !contentChangedExpected)
            {
                summary.RecordFailure(
                    testCase.Key,
                    "LifecycleProcessor/Placement",
                    "K→D→I correct + original BlockContentId after deferred normalize",
                    note + $" blockMatch={contentChangedExpected}",
                    TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
                read.Commit();
                return;
            }

            read.Commit();
        }

        summary.RecordPass(
            testCase.Key,
            "LifecycleProcessor",
            "wrong BTR → dogleg→content-side via internal queue drain");
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.LifecycleProcessor,
            true);
        summary.AppendDetail(
            "LifecycleProcessor uses shared TryNormalizeDogleg/TryNormalizeContentSide; " +
            "UNDO production wiring remains blocked; external Proof stays OFF.");
    }

    private static bool TryInjectOppositeCombinedBtr(
        Database database,
        Transaction transaction,
        MLeader leader,
        Dictionary<string, AttrValue> attrs,
        out string note)
    {
        note = string.Empty;
        if (!TryReadCombinedEnsureContext(
                transaction,
                leader,
                out var context,
                out note))
        {
            return false;
        }

        var opposite =
            TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                context.CurrentSide);
        var ensure = AcKrovyFramedBlockContentDefinitionService.Ensure(
            database,
            transaction,
            new AutoCadFramedBlockContentRequest(
                context.ContentKind,
                TimberFramedBlockContentPresentation.Combined,
                context.ItemTextStyleName,
                context.DimensionTextStyleName,
                context.ItemPaperHeightMm,
                context.DimensionPaperHeightMm,
                context.ItemTextStyleId,
                context.DimensionTextStyleId,
                context.ItemTextForFrameSizing,
                opposite));
        if (!ensure.Succeeded ||
            ensure.BlockTableRecordId is not ObjectId targetBlockId ||
            targetBlockId.IsNull)
        {
            note = "Ensure opposite BTR: " + ensure.DiagnosticReason;
            return false;
        }

        if (targetBlockId == leader.BlockContentId)
        {
            note = "opposite Ensure returned same BlockContentId";
            return false;
        }

        var attachment = ReadAttachment(leader);
        var knee = ReadKnee(leader);
        var blockPosition = leader.BlockPosition;
        var doglegLength = leader.DoglegLength;
        var dogleg = TryReadDogleg(leader);
        var scale = leader.BlockScale;
        var rotation = leader.BlockRotation;

        leader.BlockContentId = targetBlockId;
        ReapplyAttributes(transaction, leader, targetBlockId, attrs);
        RestoreLeaderGeometry(
            leader,
            attachment,
            knee,
            blockPosition,
            doglegLength,
            dogleg,
            scale,
            rotation);
        note = "injected " +
            TimberFramedBlockContentVariantRules.ToDimensionColumnSideToken(
                context.CurrentSide) +
            " -> " +
            TimberFramedBlockContentVariantRules.ToDimensionColumnSideToken(opposite);
        return true;
    }

    private static bool TryReadCombinedEnsureContext(
        Transaction transaction,
        MLeader leader,
        out CombinedEnsureContext context,
        out string note)
    {
        context = default;
        note = string.Empty;
        var blockId = leader.BlockContentId;
        if (blockId.IsNull)
        {
            note = "BlockContentId null";
            return false;
        }

        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        if (!TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                block.Name,
                out var parse) ||
            parse.DimensionColumnSide is not
                TimberFramedBlockContentDimensionColumnSide currentSide)
        {
            note = "Combined BTR name must parse as R2 DIMNX/DIMPX.";
            return false;
        }

        AttributeDefinition? itemDef = null;
        AttributeDefinition? widthDef = null;
        Entity? frame = null;
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            if (entity is AttributeDefinition attribute)
            {
                if (string.Equals(
                        attribute.Tag,
                        TimberFramedBlockContentDefinitionRules.ItemNoTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    itemDef = attribute;
                }
                else if (string.Equals(
                             attribute.Tag,
                             TimberFramedBlockContentDefinitionRules.WidthTag,
                             StringComparison.OrdinalIgnoreCase))
                {
                    widthDef = attribute;
                }
            }
            else
            {
                frame ??= entity;
            }
        }

        if (itemDef is null || widthDef is null)
        {
            note = "Combined BTR missing ITEM_NO/WIDTH AttrDefs.";
            return false;
        }

        if (!TryResolveContentKind(frame, out var contentKind, out note))
        {
            return false;
        }

        var itemStyle = (TextStyleTableRecord)transaction.GetObject(
            itemDef.TextStyleId,
            OpenMode.ForRead);
        var dimStyle = (TextStyleTableRecord)transaction.GetObject(
            widthDef.TextStyleId,
            OpenMode.ForRead);
        var itemStyleName = string.IsNullOrWhiteSpace(itemStyle.Name)
            ? "Standard"
            : itemStyle.Name;
        var dimStyleName = string.IsNullOrWhiteSpace(dimStyle.Name)
            ? "Standard"
            : dimStyle.Name;

        var itemText = "12";
        using (var itemAttr = leader.GetBlockAttribute(itemDef.ObjectId))
        {
            if (itemAttr is not null && !string.IsNullOrWhiteSpace(itemAttr.TextString))
            {
                itemText = itemAttr.TextString;
            }
        }

        var itemPaper =
            itemDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;
        var dimPaper =
            widthDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;

        context = new CombinedEnsureContext(
            currentSide,
            contentKind,
            itemStyleName,
            dimStyleName,
            itemPaper,
            dimPaper,
            itemDef.TextStyleId,
            widthDef.TextStyleId,
            itemText);
        return true;
    }

    private static bool TryResolveContentKind(
        Entity? frame,
        out TimberFramedBlockContentKind kind,
        out string note)
    {
        kind = default;
        note = string.Empty;
        if (frame is null)
        {
            note = "Combined BTR missing frame/connection entity.";
            return false;
        }

        if (frame is DBPoint)
        {
            kind = TimberFramedBlockContentKind.Plain;
            return true;
        }

        if (frame is Circle)
        {
            kind = TimberFramedBlockContentKind.Circle;
            return true;
        }

        if (frame is Polyline polyline && polyline.Closed && polyline.NumberOfVertices == 4)
        {
            var hasBulge = Enumerable.Range(0, 4).Any(i =>
                Math.Abs(polyline.GetBulgeAt(i)) >
                TimberFramedBlockContentDefinitionRules.AttributeTolerance);
            kind = hasBulge
                ? TimberFramedBlockContentKind.Slot
                : TimberFramedBlockContentKind.Rectangle;
            return true;
        }

        note = "Unable to classify Combined frame geometry kind.";
        return false;
    }

    private static void ApplyKneeOnlyCrossing(
        MLeader leader,
        Point3d attachment,
        Point3d knee,
        Point3d blockPosition,
        Vector3d doglegDirection)
    {
        // Knee + dogleg only — BlockPosition / BlockContent stay put.
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);
        leader.BlockPosition = blockPosition;
        if (leaderIndexes.Length > 0 && doglegDirection.Length > 1e-9d)
        {
            leader.SetDogleg(leaderIndexes[0], doglegDirection.GetNormal());
        }

        leader.SetLastVertex(lineIndex, knee);
        leader.BlockPosition = blockPosition;
    }

    private static void RestoreLeaderGeometry(
        MLeader leader,
        Point3d attachment,
        Point3d knee,
        Point3d blockPosition,
        double doglegLength,
        Vector3d? dogleg,
        Scale3d scale,
        double rotation)
    {
        leader.BlockScale = scale;
        leader.BlockRotation = rotation;
        leader.DoglegLength = doglegLength;
        RestoreDogleg(leader, dogleg);
        leader.BlockPosition = blockPosition;
        var lineIndex = GetPrimaryLeaderLineIndex(leader);
        leader.SetFirstVertex(lineIndex, attachment);
        leader.SetLastVertex(lineIndex, knee);
        leader.BlockPosition = blockPosition;
        RestoreDogleg(leader, dogleg);
    }

    private static void RestoreDogleg(MLeader leader, Vector3d? dogleg)
    {
        if (dogleg is not Vector3d doglegVector)
        {
            return;
        }

        var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (indexes.Length > 0)
        {
            leader.SetDogleg(indexes[0], doglegVector);
        }
    }

    private static void ReapplyAttributes(
        Transaction transaction,
        MLeader leader,
        ObjectId blockId,
        Dictionary<string, AttrValue> values)
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

            if (!values.TryGetValue(definition.Tag, out var value))
            {
                continue;
            }

            using var attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = value.Text;
            attribute.Height = value.Height;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }
    }

    private static void TryNormalizeContentSideQuiet(
        Database database,
        Transaction transaction,
        MLeader leader) =>
        AutoCadFramedBlockContentNormalizeContentSideService.TryNormalizeContentSide(
            database,
            transaction,
            leader);

    private static void ValidateExternalInventory(
        Database database,
        Transaction transaction,
        int expectedLeaders,
        TimberFramedBlockContentAutotestSummary summary)
    {
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForRead);
        var markedLeaders = 0;
        var externalMText = 0;
        var externalDbText = 0;
        var standaloneBr = 0;
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            if (HasAutotestMarker(entity))
            {
                if (entity is MLeader)
                {
                    markedLeaders++;
                }

                continue;
            }

            if (!string.Equals(
                    entity.Layer,
                    AutotestLayerName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (entity)
            {
                case MText:
                    externalMText++;
                    break;
                case DBText:
                    externalDbText++;
                    break;
                case BlockReference:
                    standaloneBr++;
                    break;
            }
        }

        var ok = markedLeaders == expectedLeaders &&
            externalMText == 0 &&
            externalDbText == 0 &&
            standaloneBr == 0;
        if (!ok)
        {
            summary.RecordFailure(
                "(inventory)",
                "ExternalEntities",
                $"markedLeaders={expectedLeaders} external=0",
                $"marked={markedLeaders} mtext={externalMText} dbtext={externalDbText} br={standaloneBr}",
                TimberFramedBlockContentAutotestCategory.ExternalEntities);
            return;
        }

        summary.RecordPass(
            "(inventory)",
            "ExternalEntities",
            $"markedLeaders={markedLeaders}");
        summary.MarkCategory(
            TimberFramedBlockContentAutotestCategory.ExternalEntities,
            true);
    }

    private static AutoCadFramedBlockContentAnnotationRequest BuildRequest(
        TimberFramedBlockContentAutotestCase testCase,
        int caseIndex,
        string styleName,
        ObjectId styleId,
        ObjectId layerId)
    {
        var denom = testCase.ScaleDenominator;
        var scale = TimberAnnotationScaleRules.GetScaleFactor(denom);
        var frame = ResolveFrame(testCase);
        var frameWidth = frame?.WidthMm * scale ?? 0d;
        var frameHeight = frame?.HeightMm * scale ?? 0d;
        var dimPaper = TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm;
        var envelope =
            TimberFramedBlockContentDefinitionRules
                .CalculateReferenceDimensionEnvelopeWidthMm(dimPaper) * scale;
        var firstSegment =
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm * scale;
        var landing =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scale;
        var grid = TimberFramedBlockContentAutotestRules.ResolveGridPoint(caseIndex);

        return new AutoCadFramedBlockContentAnnotationRequest(
            grid.X,
            grid.Y,
            testCase.ElementAxisDegrees * Math.PI / 180d,
            testCase.Side,
            testCase.Kind,
            testCase.Presentation,
            frameWidth,
            frameHeight,
            envelope,
            denom,
            TimberFramedBlockContentAutotestRules.DefaultItemPaperHeightMm,
            dimPaper,
            styleName,
            styleName,
            styleId,
            styleId,
            ItemNoText: testCase.Kind == TimberFramedBlockContentKind.Plain
                ? "1"
                : "12",
            WidthText: testCase.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? "120"
                : string.Empty,
            HeightText: testCase.Presentation ==
                TimberFramedBlockContentPresentation.Combined
                ? "60"
                : string.Empty,
            firstSegment,
            landing,
            layerId,
            AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh);
    }

    private static TimberItemLeaderBlockDefinition? ResolveFrame(
        TimberFramedBlockContentAutotestCase testCase)
    {
        if (testCase.Kind == TimberFramedBlockContentKind.Plain)
        {
            return null;
        }

        var style = TimberFramedBlockContentDefinitionRules.ToItemNumberLeaderStyle(
            testCase.Kind);
        return TimberItemLeaderBlockDefinitionRules.Resolve(style, "12");
    }

    private static bool TryEvaluatePlacement(
        Transaction transaction,
        MLeader leader,
        out TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation,
        out AutoCadFramedBlockContentDimensionColumnPlacementService.WorldAttributePoints
            points,
        out string note) =>
        AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
            transaction,
            leader,
            out evaluation,
            out points,
            out note);

    private static string FormatEvalDiag(
        TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation,
        AutoCadFramedBlockContentDimensionColumnPlacementService.WorldAttributePoints points,
        string note)
    {
        if (string.IsNullOrWhiteSpace(points.BlockName))
        {
            return string.IsNullOrWhiteSpace(note) ? "eval unavailable" : note;
        }

        return TimberFramedBlockContentAutotestRules.FormatPlacementDiag(
            new TimberPlanarPoint(points.Knee.X, points.Knee.Y),
            evaluation.Current.DimensionColumnCenter,
            new TimberPlanarPoint(points.ItemAlignment.X, points.ItemAlignment.Y),
            evaluation.Current.ParameterT,
            evaluation.Current.IsCorrect,
            evaluation.Mirrored.IsCorrect) +
            (string.IsNullOrWhiteSpace(note) ? string.Empty : " note=" + note);
    }

    private static Dictionary<string, AttrValue> CaptureAttrSnapshot(
        Transaction transaction,
        MLeader leader)
    {
        var result = new Dictionary<string, AttrValue>(
            StringComparer.OrdinalIgnoreCase);
        if (leader.BlockContentId.IsNull)
        {
            return result;
        }

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

            result[definition.Tag] = new AttrValue(attribute.TextString, attribute.Height);
        }

        return result;
    }

    private static bool AttrsPreserved(
        Dictionary<string, AttrValue> before,
        Dictionary<string, AttrValue> after,
        out string note)
    {
        note = string.Empty;
        foreach (var pair in before)
        {
            if (!after.TryGetValue(pair.Key, out var value))
            {
                note = "missing " + pair.Key;
                return false;
            }

            if (!string.Equals(pair.Value.Text, value.Text, StringComparison.Ordinal))
            {
                note = pair.Key + " text changed";
                return false;
            }

            if (Math.Abs(pair.Value.Height - value.Height) >
                TimberFramedBlockContentAutotestRules.AttrHeightToleranceMm)
            {
                note = pair.Key + " height changed";
                return false;
            }
        }

        return true;
    }

    private static bool AttrsValuesMatch(
        Dictionary<string, AttrValue> baseline,
        Dictionary<string, AttrValue> after,
        out string note)
    {
        note = string.Empty;
        foreach (var tag in new[] { "ITEM_NO", "WIDTH", "HEIGHT" })
        {
            if (!baseline.TryGetValue(tag, out var expected))
            {
                continue;
            }

            if (!after.TryGetValue(tag, out var actual) ||
                !string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
            {
                note = tag + " value mismatch";
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)>
        CaptureAttrRefLocals(Transaction transaction, MLeader leader)
    {
        var list = new List<(string, Point3d, Point3d)>();
        var blockId = leader.BlockContentId;
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

            var tag = definition.Tag.ToUpperInvariant();
            if (tag is not (
                TimberFramedBlockContentDefinitionRules.ItemNoTag or
                TimberFramedBlockContentDefinitionRules.WidthTag or
                TimberFramedBlockContentDefinitionRules.HeightTag))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                continue;
            }

            var localPos = WorldToBlockLocal(
                attribute.Position,
                leader.BlockPosition,
                leader.BlockScale,
                leader.BlockRotation,
                leader.Normal);
            Point3d worldAlign;
            try
            {
                worldAlign = attribute.AlignmentPoint;
            }
            catch (AcadException)
            {
                worldAlign = attribute.Position;
            }

            var localAlign = WorldToBlockLocal(
                worldAlign,
                leader.BlockPosition,
                leader.BlockScale,
                leader.BlockRotation,
                leader.Normal);
            list.Add((tag, localPos, localAlign));
        }

        return list;
    }

    private static Point3d WorldToBlockLocal(
        Point3d world,
        Point3d blockPosition,
        Scale3d blockScale,
        double blockRotation,
        Vector3d normal)
    {
        var matrix = Matrix3d.Displacement(blockPosition.GetAsVector()) *
            Matrix3d.Rotation(blockRotation, normal, Point3d.Origin) *
            Matrix3d.Scaling(blockScale.X, Point3d.Origin);
        return world.TransformBy(matrix.Inverse());
    }

    private static bool AttrLocalsNearlyEqual(
        IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)> before,
        IReadOnlyList<(string Tag, Point3d LocalPos, Point3d LocalAlign)> after,
        out string note)
    {
        note = string.Empty;
        if (before.Count != after.Count)
        {
            note = $"count {before.Count}->{after.Count}";
            return false;
        }

        var afterByTag = after.ToDictionary(
            x => x.Tag,
            StringComparer.OrdinalIgnoreCase);
        foreach (var a in before)
        {
            if (!afterByTag.TryGetValue(a.Tag, out var b) ||
                a.LocalPos.DistanceTo(b.LocalPos) >
                    TimberFramedBlockContentAutotestRules.DriftToleranceMm ||
                a.LocalAlign.DistanceTo(b.LocalAlign) >
                    TimberFramedBlockContentAutotestRules.DriftToleranceMm)
            {
                note = a.Tag + " local drifted";
                return false;
            }
        }

        return true;
    }

    private static bool DoglegVectorsEqual(Vector3d? left, Vector3d? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value.IsEqualTo(
            right.Value,
            new Tolerance(
                TimberFramedBlockContentAutotestRules.DriftToleranceMm,
                TimberFramedBlockContentAutotestRules.DriftToleranceMm));
    }

    private static void MarkAngleScaleCategories(
        TimberFramedBlockContentAutotestCase testCase,
        TimberFramedBlockContentAutotestSummary summary,
        bool pass)
    {
        if (testCase.AngleBand == TimberFramedBlockContentAutotestAngleBand.Cardinal)
        {
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.CardinalAngles,
                pass);
        }
        else if (testCase.AngleBand ==
                 TimberFramedBlockContentAutotestAngleBand.NearCardinal)
        {
            summary.MarkCategory(
                TimberFramedBlockContentAutotestCategory.NearCardinalAngles,
                pass);
        }

        summary.MarkCategory(TimberFramedBlockContentAutotestCategory.Scales, pass);
    }

    private static TimberFramedBlockContentAutotestCategory MapCreateCategory(
        TimberFramedBlockContentAutotestCase testCase) =>
        testCase.Presentation == TimberFramedBlockContentPresentation.ItemOnly
            ? TimberFramedBlockContentAutotestCategory.ItemOnly
            : TimberFramedBlockContentAutotestCategory.CreatePlacement;

    private static (int Found, int Erased) EraseMarkedAutotestEntities(
        Database database,
        Transaction transaction,
        string? runIdOnly)
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
                entity.IsErased)
            {
                continue;
            }

            if (!TryReadAutotestPayload(entity, out var payload))
            {
                continue;
            }

            if (runIdOnly is null)
            {
                if (!TimberFramedBlockContentAutotestRules.IsOwnAutotestMarker(payload))
                {
                    continue;
                }
            }
            else if (!TimberFramedBlockContentAutotestRules.IsOwnAutotestMarkerForRun(
                         payload,
                         runIdOnly))
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

    private static void MarkAutotestEntity(
        Database database,
        Transaction transaction,
        ObjectId entityId,
        string runId,
        string caseKey)
    {
        if (entityId.IsNull ||
            transaction.GetObject(entityId, OpenMode.ForWrite, true) is not Entity entity ||
            entity.IsErased)
        {
            return;
        }

        EnsureDebugRegApp(database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, DebugRegAppName));
        retained.Add(
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                TimberFramedBlockContentAutotestRules.BuildMarkerPayload(
                    runId,
                    caseKey)));
        entity.XData = new ResultBuffer(retained.ToArray());
    }

    private static bool HasAutotestMarker(Entity entity) =>
        TryReadAutotestPayload(entity, out var payload) &&
        TimberFramedBlockContentAutotestRules.IsOwnAutotestMarker(payload);

    private static bool TryReadAutotestPayload(Entity entity, out string payload)
    {
        payload = string.Empty;
        using var buffer = entity.GetXDataForApplication(DebugRegAppName);
        if (buffer is null)
        {
            return false;
        }

        foreach (var value in buffer.AsArray())
        {
            if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
            {
                payload = Convert.ToString(value.Value) ?? string.Empty;
                return payload.Length > 0;
            }
        }

        return false;
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

    private static ObjectId EnsureAutotestLayer(
        Database database,
        Transaction transaction)
    {
        var layerTable = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        if (layerTable.Has(AutotestLayerName))
        {
            return layerTable[AutotestLayerName];
        }

        layerTable.UpgradeOpen();
        var layer = new LayerTableRecord
        {
            Name = AutotestLayerName,
            Color = AcColor.FromColorIndex(ColorMethod.ByAci, 3),
        };
        var id = layerTable.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
        return id;
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

    private static Point3d ReadAttachment(MLeader leader) =>
        leader.GetFirstVertex(GetPrimaryLeaderLineIndex(leader));

    private static Point3d ReadKnee(MLeader leader) =>
        leader.GetLastVertex(GetPrimaryLeaderLineIndex(leader));

    private static Vector3d? TryReadDogleg(MLeader leader)
    {
        try
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            return indexes.Length == 0 ? null : leader.GetDogleg(indexes[0]);
        }
        catch (AcadException)
        {
            return null;
        }
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

    private static string WriteDetailLog(TimberFramedBlockContentAutotestSummary summary)
    {
        var fileName = TimberFramedBlockContentAutotestRules.BuildDetailLogFileName(
            summary.RunId);
        var directory = TryFindScratchDirectory() ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AcKrovy");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var body = new StringBuilder();
        body.AppendLine(summary.FormatConsoleSummary());
        body.AppendLine();
        body.AppendLine("--- detail ---");
        body.Append(summary.BuildDetailLogBody());
        File.WriteAllText(path, body.ToString(), Encoding.UTF8);
        return path;
    }

    private static string? TryFindScratchDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(
                    typeof(AutoCadFramedBlockContentAutotestService).Assembly.Location) ??
                AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
                {
                    var scratch = Path.Combine(dir.FullName, "_scratch");
                    Directory.CreateDirectory(scratch);
                    return scratch;
                }

                dir = dir.Parent;
            }
        }
        catch (Exception)
        {
            // Fall back to LocalAppData.
        }

        return null;
    }

    private readonly record struct AttrValue(string Text, double Height);

    private readonly record struct CombinedEnsureContext(
        TimberFramedBlockContentDimensionColumnSide CurrentSide,
        TimberFramedBlockContentKind ContentKind,
        string ItemTextStyleName,
        string DimensionTextStyleName,
        double ItemPaperHeightMm,
        double DimensionPaperHeightMm,
        ObjectId ItemTextStyleId,
        ObjectId DimensionTextStyleId,
        string ItemTextForFrameSizing);
}
#endif
