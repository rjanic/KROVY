#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFramedRendererProofService
{
    private const int XDataRegAppCode = 1001;
    private const int XDataStringCode = 1000;
    private const int XRecordStringCode = 1;
    private const int XRecordChunkLength = 240;
    private const double Tolerance = 0.001d;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        var database = document.Database;
        var editor = document.Editor;

        try
        {
            using (document.LockDocument())
            {
                using var transaction =
                    database.TransactionManager.StartTransaction();
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForRead);
                var existing = modelSpace
                    .Cast<ObjectId>()
                    .Select(id => transaction.GetObject(
                        id,
                        OpenMode.ForRead,
                        false))
                    .OfType<Entity>()
                    .Where(entity =>
                        !entity.IsErased &&
                        entity.OwnerId == modelSpace.ObjectId)
                    .ToArray();
                if (existing.Length != 0)
                {
                    editor.WriteMessage(
                        $"\nAK_DEV_FRAMED_RENDERER_CREATE: FAIL - exact " +
                        $"ModelSpace contains {existing.Length} real entities. " +
                        "Layouts, paper-space objects, AEC dictionaries and " +
                        "nested block definitions were not counted.");
                    foreach (var entity in existing.Take(20))
                    {
                        editor.WriteMessage(
                            $"\n  handle={entity.Handle}; id={entity.ObjectId}; " +
                            $"type={entity.GetRXClass().DxfName}; " +
                            $"owner={entity.OwnerId}");
                    }
                    return;
                }

                var defaultProfile = TimberElementDefaultProfile.CreateDefault();
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    defaultProfile);
                var styles = batch.TextStyleCatalog.CompatibleStyles
                    .Where(style => !string.Equals(
                        style.CanonicalName,
                        TimberAnnotationTextSettingsRules.DefaultTextStyleName,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                if (styles.Length < 2)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_FRAMED_RENDERER_CREATE: FAIL - create two " +
                        "distinct variable-height nonannotative text styles first. " +
                        "No proof entity was created.");
                    return;
                }

                var rectangleMediumInnerWidth =
                    TimberItemLeaderBlockDefinitionRules.MediumFrameWidthMm -
                    2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;
                var rectangleLargeInnerWidth =
                    TimberItemLeaderBlockDefinitionRules.LargeFrameWidthMm -
                    2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;
                var rectangleSelection = AutoCadFramedRendererProofPolicy
                    .SelectRectangleLargeFitCandidate(
                        token => TryMeasureWidth(
                            database,
                            styles[0].TextStyleId,
                            AutoCadFramedRendererProofPolicy
                                .RectangleLargeCaseTemplate
                                .ItemNumberPaperHeightMm,
                            token),
                        rectangleMediumInnerWidth,
                        rectangleLargeInnerWidth);
                WriteSelectionAttempts(editor, "E", rectangleSelection);
                var runtimeCases = AutoCadFramedRendererProofPolicy.Cases
                    .ToList();
                if (rectangleSelection is
                    {
                        IsTested: true,
                        SelectedCandidate: { } rectangleCandidate,
                    })
                {
                    var rectangleCase = AutoCadFramedRendererProofPolicy
                        .RectangleLargeCaseTemplate with
                    {
                        ItemText = rectangleCandidate.ItemText,
                        CustomElementPrefix = rectangleCandidate.Prefix,
                    };
                    runtimeCases.Insert(
                        runtimeCases.FindIndex(candidate =>
                            candidate.Token == "F"),
                        rectangleCase);
                }
                else
                {
                    editor.WriteMessage(
                        $"\n  E: {AutoCadFramedRendererProofPolicy.NotTested} - " +
                        rectangleSelection.DiagnosticReason);
                }

                EnsureRegApp(database, transaction);
                modelSpace.UpgradeOpen();
                var expected = new List<
                    AutoCadFramedRendererProofExpectedCase>();
                var sources = new Dictionary<string, ObjectId>(
                    StringComparer.Ordinal);

                for (var index = 0;
                     index < runtimeCases.Count;
                     index++)
                {
                    var proofCase = runtimeCases[index];
                    var source = new Line(
                        new Point3d(index * 2500d, 0d, 0d),
                        new Point3d(index * 2500d + 1000d, 0d, 0d));
                    source.SetDatabaseDefaults(database);
                    modelSpace.AppendEntity(source);
                    transaction.AddNewlyCreatedDBObject(source, true);
                    WriteMarker(source, proofCase.Token);
                    sources.Add(proofCase.Token, source.ObjectId);

                    var data = CreateData(
                        proofCase,
                        styles[proofCase.StyleSlot].CanonicalName);
                    AutoCadItemLeaderBlockVariantResult? renderResult = null;
                    TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        source,
                        data,
                        batch,
                        variantResultObserver: result =>
                            renderResult = result);
                    if (proofCase.Token == "E" &&
                        renderResult?.TextFit is { } creationTextFit)
                    {
                        var isRequiredLargeFit = creationTextFit.Fits &&
                            creationTextFit.EvaluatedDefinition.Size ==
                                TimberItemLeaderBlockSize.Large &&
                            creationTextFit.MeasuredTextWidthMm >
                                rectangleMediumInnerWidth &&
                            creationTextFit.MeasuredTextWidthMm <=
                                rectangleLargeInnerWidth;
                        var creationFitOutcome = isRequiredLargeFit
                            ? AutoCadFramedRendererProofPolicy.FitPass
                            : "INVALID E FIT";
                        editor.WriteMessage(
                            $"\n  E: token={proofCase.ItemText}; " +
                            $"resolvedStyle=" +
                            $"{styles[proofCase.StyleSlot].CanonicalName}; " +
                            $"frameSize=" +
                            $"{creationTextFit.EvaluatedDefinition.Size}; " +
                            $"measuredTextWidth=" +
                            $"{creationTextFit.MeasuredTextWidthMm:R}; " +
                            $"mediumInnerWidth=" +
                            $"{rectangleMediumInnerWidth:R}; " +
                            $"largeInnerWidth=" +
                            $"{rectangleLargeInnerWidth:R}; " +
                            $"padding=" +
                            $"{creationTextFit.HorizontalPaddingMm:R}; " +
                            $"{creationFitOutcome}");
                        if (!isRequiredLargeFit)
                        {
                            throw new InvalidOperationException(
                                "E did not satisfy Medium-overflow/Large-fit.");
                        }
                    }
                    if (renderResult is not { Succeeded: true } ||
                        renderResult.ResolvedBlockName is null ||
                        renderResult.TextFit is not { Fits: true } textFit)
                    {
                        throw new InvalidOperationException(
                            $"Production render failed for {proofCase.Token}: " +
                            (renderResult?.DiagnosticReason ??
                             "no structured variant result"));
                    }

                    expected.Add(new AutoCadFramedRendererProofExpectedCase(
                        proofCase.Token,
                        proofCase.Mode,
                        proofCase.ItemStyle,
                        proofCase.FramedRole,
                        styles[proofCase.StyleSlot].CanonicalName,
                        proofCase.ItemNumberPaperHeightMm,
                        proofCase.Denominator,
                        proofCase.ItemText,
                        renderResult.ResolvedBlockName,
                        renderResult.Kind.ToString(),
                        textFit.EvaluatedDefinition.Size,
                        textFit.MeasuredTextWidthMm,
                        textFit.AvailableInnerWidthMm,
                        textFit.HorizontalPaddingMm));
                }

                var legacy = AcKrovyItemLeaderBlockService.Ensure(
                    database,
                    transaction,
                    ItemNumberLeaderStyle.Circle,
                    "G",
                    preserveExistingDefinition: true);
                var legacyBlock = (BlockTableRecord)transaction.GetObject(
                    legacy.BlockId,
                    OpenMode.ForRead);
                var gSource = (Entity)transaction.GetObject(
                    sources["G"],
                    OpenMode.ForRead);
                var gLeader = FindFramedLeader(
                    modelSpace,
                    transaction,
                    gSource.Handle.ToString(),
                    TimberMainAnnotationComponentRole.Primary);
                gLeader.UpgradeOpen();
                gLeader.BlockContentId = legacy.BlockId;
                SetToken(
                    transaction,
                    gLeader,
                    legacy.AttributeDefinitionId,
                    "G");
                var gCase = runtimeCases
                    .Single(candidate => candidate.Token == "G");
                AutoCadItemLeaderBlockVariantResult? migrationResult = null;
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    gSource,
                    CreateData(gCase, styles[0].CanonicalName),
                    batch,
                    variantResultObserver: result =>
                        migrationResult = result);
                if (migrationResult is not { Succeeded: true } ||
                    gLeader.BlockContentId == legacy.BlockId)
                {
                    throw new InvalidOperationException(
                        "Production legacy migration G did not select the " +
                        "immutable variant.");
                }

                var overflowCase = RunExpectedOverflowCase(
                    database,
                    transaction,
                    editor,
                    modelSpace,
                    batch,
                    styles[0],
                    sources,
                    expected,
                    rectangleLargeInnerWidth);
                var rectangleCaseManifest =
                    new AutoCadFramedRendererRectangleCaseManifest(
                        "E",
                        rectangleSelection.IsTested
                            ? AutoCadFramedRendererProofPolicy.FitPass
                            : AutoCadFramedRendererProofPolicy.NotTested,
                        rectangleSelection.SelectedCandidate?.ItemText,
                        styles[0].CanonicalName,
                        rectangleSelection.MeasuredTextWidthMm,
                        rectangleMediumInnerWidth,
                        rectangleLargeInnerWidth,
                        TimberItemLeaderBlockDefinitionRules
                            .HorizontalPaddingMm,
                        rectangleSelection.DiagnosticReason,
                        rectangleSelection.Attempts);
                var manifest = new AutoCadFramedRendererProofManifest(
                    AutoCadFramedRendererProofPolicy.SchemaVersion,
                    AutoCadFramedRendererProofPolicy.SuiteIdentifier,
                    legacyBlock.Name,
                    batch.ItemLeaderVariantCatalog.Count,
                    AutoCadFramedRendererProofPolicy.FailureCaseNotTested,
                    rectangleCaseManifest,
                    overflowCase,
                    expected);
                WriteManifest(database, transaction, manifest);
                transaction.Commit();
            }

            using var readTransaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyCore(
                database,
                readTransaction,
                editor,
                writeSuccessMessage: false);
            editor.WriteMessage(passed
                ? "\nAK_DEV_FRAMED_RENDERER_CREATE: PASS - production " +
                  "renderer success cases committed and independent " +
                  "post-commit readback passed; E/J may be explicitly " +
                  "NOT TESTED. I = NOT TESTED."
                : "\nAK_DEV_FRAMED_RENDERER_CREATE: FAIL - post-commit " +
                  "readback failed.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_FRAMED_RENDERER_CREATE: FAIL - " +
                exception.Message);
        }
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        try
        {
            using var transaction = document.Database.TransactionManager
                .StartOpenCloseTransaction();
            _ = VerifyCore(
                document.Database,
                transaction,
                document.Editor,
                writeSuccessMessage: true);
        }
        catch (System.Exception exception)
        {
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_RENDERER_VERIFY: FAIL - " +
                exception.Message);
        }
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }
        var removed = 0;
        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager
                .StartTransaction())
            {
                var modelSpace = OpenModelSpace(
                    document.Database,
                    transaction,
                    OpenMode.ForRead);
                var sourceHandles = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var id in modelSpace.Cast<ObjectId>().ToArray())
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false) is
                            not Entity entity ||
                        !TryReadMarker(entity, out _))
                    {
                        continue;
                    }
                    sourceHandles.Add(entity.Handle.ToString());
                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }
                foreach (var id in modelSpace.Cast<ObjectId>().ToArray())
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false) is
                            not Entity entity ||
                        !ElementLabelStore.TryRead(entity, out var label) ||
                        label is null ||
                        !sourceHandles.Contains(label.SourceHandle))
                    {
                        continue;
                    }
                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }
                RemoveManifest(document.Database, transaction);
                transaction.Commit();
            }
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_RENDERER_CLEAN: PASS - removed " +
                $"{removed} owned source/annotation entities. Immutable " +
                "variant and legacy definitions were not purged.");
        }
        catch (System.Exception exception)
        {
            document.Editor.WriteMessage(
                $"\nAK_DEV_FRAMED_RENDERER_CLEAN: FAIL - " +
                exception.Message);
        }
    }

    private static AutoCadFramedRendererOverflowCaseManifest
        RunExpectedOverflowCase(
            Database database,
            Transaction transaction,
            Editor editor,
            BlockTableRecord modelSpace,
            AutoCadAnnotationPresentationBatchContext batch,
            AutoCadTextStyleCatalogEntry style,
            IReadOnlyDictionary<string, ObjectId> sources,
            IReadOnlyList<AutoCadFramedRendererProofExpectedCase> expected,
            double rectangleLargeInnerWidth)
    {
        var selection = AutoCadFramedRendererProofPolicy
            .SelectRectangleOverflowCandidate(
                token => TryMeasureWidth(
                    database,
                    style.TextStyleId,
                    AutoCadFramedRendererProofPolicy
                        .RectangleLargeCaseTemplate
                        .ItemNumberPaperHeightMm,
                    token),
                rectangleLargeInnerWidth);
        WriteSelectionAttempts(editor, "J", selection);
        if (selection is not
            {
                IsTested: true,
                SelectedCandidate: { } candidate,
                MeasuredTextWidthMm: { } measuredWidth,
            })
        {
            editor.WriteMessage(
                $"\n  J: {AutoCadFramedRendererProofPolicy.NotTested} - " +
                selection.DiagnosticReason);
            return new AutoCadFramedRendererOverflowCaseManifest(
                "J",
                AutoCadFramedRendererProofPolicy.NotTested,
                null,
                AutoCadItemLeaderBlockVariantResultKind.TextOverflow.ToString(),
                style.CanonicalName,
                null,
                rectangleLargeInnerWidth,
                TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm,
                0,
                0,
                0,
                0,
                AutoCadFramedRendererProofPolicy.NotTested,
                selection.DiagnosticReason,
                selection.Attempts);
        }

        var targetToken = sources.ContainsKey("E") ? "E" : "D";
        var targetExpected = expected.Single(item => item.Token == targetToken);
        var targetSource = (Entity)transaction.GetObject(
            sources[targetToken],
            OpenMode.ForRead);
        var targetLeader = FindFramedLeader(
            modelSpace,
            transaction,
            targetSource.Handle.ToString(),
            targetExpected.FramedRole);
        var leaderSignatureBefore = ReadLeaderPreservationSignature(
            transaction,
            targetLeader);
        var modelSpaceCountBefore = CountLiveOwnedEntities(
            modelSpace,
            transaction);
        var blockDefinitionCountBefore = CountLiveBlockDefinitions(
            database,
            transaction);
        var catalogCountBefore = batch.ItemLeaderVariantCatalog.Count;
        var overflowCase = AutoCadFramedRendererProofPolicy
            .RectangleLargeCaseTemplate with
        {
            Token = "J",
            ItemText = candidate.ItemText,
            CustomElementPrefix = candidate.Prefix,
        };
        AutoCadItemLeaderBlockVariantResult? result = null;
        _ = TimberAnnotationService.EnsureForElement(
            database,
            transaction,
            targetSource,
            CreateData(overflowCase, style.CanonicalName),
            batch,
            previousElementId: targetExpected.ItemText,
            variantResultObserver: observed => result = observed);

        var targetLeaderAfter = FindFramedLeader(
            modelSpace,
            transaction,
            targetSource.Handle.ToString(),
            targetExpected.FramedRole);
        var leaderSignatureAfter = ReadLeaderPreservationSignature(
            transaction,
            targetLeaderAfter);
        var modelSpaceDelta = CountLiveOwnedEntities(
                modelSpace,
                transaction) -
            modelSpaceCountBefore;
        var blockDefinitionDelta = CountLiveBlockDefinitions(
                database,
                transaction) -
            blockDefinitionCountBefore;
        var catalogDelta = batch.ItemLeaderVariantCatalog.Count -
            catalogCountBefore;
        var preservationPassed =
            result is
            {
                Kind: AutoCadItemLeaderBlockVariantResultKind.TextOverflow,
                Succeeded: false,
                BlockTableRecordId: null,
                VariantKey: null,
                TextFit: { Fits: false } overflowFit,
            } &&
            Math.Abs(overflowFit.MeasuredTextWidthMm - measuredWidth) <=
                Tolerance &&
            overflowFit.MeasuredTextWidthMm > rectangleLargeInnerWidth &&
            modelSpaceDelta == 0 &&
            blockDefinitionDelta == 0 &&
            catalogDelta == 0 &&
            targetLeader.ObjectId == targetLeaderAfter.ObjectId &&
            string.Equals(
                leaderSignatureBefore,
                leaderSignatureAfter,
                StringComparison.Ordinal);
        if (!preservationPassed)
        {
            throw new InvalidOperationException(
                "J expected TextOverflow did not preserve the existing " +
                "annotation/database/cache state.");
        }

        editor.WriteMessage(
            $"\n  J: token={candidate.ItemText}; " +
            $"resolvedStyle={style.CanonicalName}; " +
            $"measuredTextWidth={measuredWidth:R}; " +
            $"largeInnerWidth={rectangleLargeInnerWidth:R}; " +
            $"padding=" +
            $"{TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm:R}; " +
            $"resultKind={result!.Kind}; " +
            $"{AutoCadFramedRendererProofPolicy.PreservationPass}");
        return new AutoCadFramedRendererOverflowCaseManifest(
            "J",
            AutoCadFramedRendererProofPolicy.ExpectedOverflowPass,
            candidate.ItemText,
            AutoCadItemLeaderBlockVariantResultKind.TextOverflow.ToString(),
            style.CanonicalName,
            measuredWidth,
            rectangleLargeInnerWidth,
            TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm,
            0,
            modelSpaceDelta,
            blockDefinitionDelta,
            catalogDelta,
            AutoCadFramedRendererProofPolicy.PreservationPass,
            selection.DiagnosticReason,
            selection.Attempts);
    }

    private static double? TryMeasureWidth(
        Database database,
        ObjectId textStyleId,
        double paperHeightMm,
        string itemText)
    {
        var measurement = AutoCadItemLeaderTextMeasurementService.Measure(
            database,
            textStyleId,
            paperHeightMm,
            itemText);
        return measurement.Succeeded
            ? measurement.MeasuredWidthMm
            : null;
    }

    private static void WriteSelectionAttempts(
        Editor editor,
        string caseToken,
        AutoCadFramedRendererTokenSelection selection)
    {
        foreach (var attempt in selection.Attempts)
        {
            editor.WriteMessage(
                $"\n  {caseToken} candidate={attempt.ItemText}; " +
                $"prefix={attempt.Prefix}; valid=" +
                $"{attempt.IsValidProductionToken}; measuredWidth=" +
                $"{(attempt.MeasuredTextWidthMm.HasValue
                    ? attempt.MeasuredTextWidthMm.Value.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    : "<unavailable>")}; rangeMatch=" +
                $"{attempt.MatchesRequestedRange}; " +
                attempt.DiagnosticReason);
        }
    }

    private static int CountLiveOwnedEntities(
        BlockTableRecord modelSpace,
        Transaction transaction) =>
        modelSpace
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, true))
            .OfType<Entity>()
            .Count(entity =>
                !entity.IsErased && entity.OwnerId == modelSpace.ObjectId);

    private static int CountLiveBlockDefinitions(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return blockTable
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, true))
            .OfType<BlockTableRecord>()
            .Count(block => !block.IsErased);
    }

    private static string ReadLeaderPreservationSignature(
        Transaction transaction,
        MLeader leader)
    {
        var definition = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        var itemDefinition = definition
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<AttributeDefinition>()
            .Single(attribute => string.Equals(
                attribute.Tag,
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                StringComparison.OrdinalIgnoreCase));
        using var itemAttribute = leader.GetBlockAttribute(
            itemDefinition.ObjectId);
        _ = ElementLabelStore.TryRead(leader, out var metadata);
        return string.Join(
            "|",
            leader.ObjectId.ToString(),
            leader.BlockContentId.ToString(),
            leader.ContentType.ToString(),
            leader.BlockPosition.X.ToString("R", CultureInfo.InvariantCulture),
            leader.BlockPosition.Y.ToString("R", CultureInfo.InvariantCulture),
            leader.BlockScale.X.ToString("R", CultureInfo.InvariantCulture),
            itemAttribute.TextString,
            metadata?.ElementId ?? "<null>",
            metadata?.Contents ?? "<null>",
            metadata?.SourceHandle ?? "<null>");
    }

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor,
        bool writeSuccessMessage)
    {
        var manifest = ReadManifest(database, transaction) ??
            throw new InvalidOperationException(
                "The dedicated production-renderer manifest is missing.");
        if (manifest.SchemaVersion !=
                AutoCadFramedRendererProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadFramedRendererProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The production-renderer manifest has an invalid identity.");
        }

        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        var sourceByToken = modelSpace
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<Entity>()
            .Select(entity => (Entity: entity,
                HasMarker: TryReadMarker(entity, out var token),
                Token: token))
            .Where(entry => entry.HasMarker)
            .ToDictionary(
                entry => entry.Token!,
                entry => entry.Entity,
                StringComparer.Ordinal);
        var blockByToken = new Dictionary<string, ObjectId>(
            StringComparer.Ordinal);

        foreach (var expected in manifest.Cases)
        {
            if (!sourceByToken.TryGetValue(expected.Token, out var source))
            {
                throw new InvalidOperationException(
                    $"Source marker {expected.Token} is missing.");
            }
            var leader = FindFramedLeader(
                modelSpace,
                transaction,
                source.Handle.ToString(),
                expected.FramedRole);
            if (leader.ContentType != ContentType.BlockContent)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: expected BlockContent MLeader.");
            }
            var block = (BlockTableRecord)transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead);
            if (!string.Equals(
                    block.Name,
                    expected.BlockName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: block name changed after persistence.");
            }
            var attribute = block
                .Cast<ObjectId>()
                .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
                .OfType<AttributeDefinition>()
                .Single(candidate => string.Equals(
                    candidate.Tag,
                    TimberItemLeaderBlockDefinitionRules.AttributeTag,
                    StringComparison.OrdinalIgnoreCase));
            var style = (TextStyleTableRecord)transaction.GetObject(
                attribute.TextStyleId,
                OpenMode.ForRead);
            var measurement = AutoCadItemLeaderTextMeasurementService.Measure(
                database,
                attribute.TextStyleId,
                expected.ItemNumberPaperHeightMm,
                expected.ItemText);
            if (!measurement.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: {measurement.DiagnosticReason}");
            }
            var textFit = TimberItemLeaderBlockDefinitionRules
                .EvaluateMeasuredTextWidth(
                    expected.ItemStyle,
                    expected.ItemText,
                    measurement.MeasuredWidthMm);
            var fitOutcome = textFit.Fits
                ? "FIT PASS"
                : "OVERFLOW FAILURE";
            editor.WriteMessage(
                $"\n  {expected.Token}: token={expected.ItemText}; " +
                $"resolvedStyle={style.Name}; frameSize=" +
                $"{textFit.EvaluatedDefinition.Size}; measuredTextWidth=" +
                $"{textFit.MeasuredTextWidthMm:R}; availableInnerWidth=" +
                $"{textFit.AvailableInnerWidthMm:R}; padding=" +
                $"{textFit.HorizontalPaddingMm:R}; {fitOutcome}");
            if (!textFit.Fits)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: explicit OVERFLOW FAILURE.");
            }
            if (expected.Token == "E" &&
                (textFit.EvaluatedDefinition.Size !=
                    TimberItemLeaderBlockSize.Large ||
                 textFit.MeasuredTextWidthMm <=
                    manifest.RectangleCaseE.MediumInnerWidthMm ||
                 textFit.MeasuredTextWidthMm >
                    manifest.RectangleCaseE.LargeInnerWidthMm))
            {
                throw new InvalidOperationException(
                    "E: persisted entity is not a Medium-overflow/Large-fit case.");
            }
            if (textFit.EvaluatedDefinition.Size != expected.FrameSize ||
                Math.Abs(
                    textFit.MeasuredTextWidthMm -
                    expected.MeasuredTextWidthMm) > Tolerance ||
                Math.Abs(
                    textFit.AvailableInnerWidthMm -
                    expected.AvailableInnerWidthMm) > Tolerance ||
                Math.Abs(
                    textFit.HorizontalPaddingMm -
                    expected.HorizontalPaddingMm) > Tolerance)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: persisted text-fit manifest mismatch.");
            }
            var definition = textFit.EvaluatedDefinition;
            var expectedBaseHeight = expected.ItemNumberPaperHeightMm *
                TimberAnnotationScaleRules.DefaultDenominator;
            var expectedScale = expected.Denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator;
            using var instanceAttribute =
                leader.GetBlockAttribute(attribute.ObjectId);
            if (!string.Equals(
                    style.Name,
                    expected.StyleName,
                    StringComparison.Ordinal) ||
                Math.Abs(attribute.Height - expectedBaseHeight) > Tolerance ||
                Math.Abs(leader.BlockScale.X - expectedScale) > Tolerance ||
                Math.Abs(
                    attribute.Height * leader.BlockScale.X -
                    expectedBaseHeight * expectedScale) > Tolerance ||
                !string.Equals(
                    instanceAttribute.TextString,
                    expected.ItemText,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: style/height/scale/token mismatch.");
            }
            var key = AutoCadItemLeaderBlockVariantKey.FromDefinition(
                definition,
                expected.StyleName,
                expected.ItemNumberPaperHeightMm);
            var validation = AcKrovyItemLeaderBlockVariantService
                .ValidateExistingDefinitionDetailed(
                    database,
                    transaction,
                    block.ObjectId,
                    definition,
                    key,
                    style.ObjectId);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: geometry/definition validation " +
                    $"failed: {validation.Reason}");
            }
            blockByToken.Add(expected.Token, block.ObjectId);
            editor.WriteMessage(
                $"\n  {expected.Token}: key=" +
                AutoCadItemLeaderBlockVariantNamePolicy
                    .CreateFingerprintPayload(key) +
                $"; block={block.Name}; handle={block.Handle}; " +
                $"result={expected.ResultKind}; BlockScale=" +
                $"{leader.BlockScale.X:R}; definitionHeight=" +
                $"{attribute.Height:R}; effectiveHeight=" +
                $"{attribute.Height * leader.BlockScale.X:R}");

            if (expected.Token == "F")
            {
                var hasDimensions = modelSpace.Cast<ObjectId>()
                    .Select(id => transaction.GetObject(
                        id,
                        OpenMode.ForRead,
                        false))
                    .OfType<MText>()
                    .Any(text =>
                        ElementLabelStore.TryRead(text, out var label) &&
                        label is not null &&
                        string.Equals(
                            label.SourceHandle,
                            source.Handle.ToString(),
                            StringComparison.OrdinalIgnoreCase) &&
                        label.ComponentRole ==
                            TimberMainAnnotationComponentRole.Primary);
                if (!hasDimensions)
                {
                    throw new InvalidOperationException(
                        "F: combined dimensions MText component is missing.");
                }
            }
        }

        RequireSame(blockByToken, "A", "B");
        RequireDifferent(blockByToken, "A", "C");
        RequireDifferent(blockByToken, "A", "D");
        if (string.Equals(
                manifest.RectangleCaseE.State,
                AutoCadFramedRendererProofPolicy.FitPass,
                StringComparison.Ordinal))
        {
            if (!blockByToken.ContainsKey("E") ||
                manifest.Cases.Count(item => item.Token == "E") != 1)
            {
                throw new InvalidOperationException(
                    "E: FIT PASS manifest must reference one successful entity.");
            }
            RequireDifferent(blockByToken, "A", "E");
        }
        else if (!string.Equals(
                     manifest.RectangleCaseE.State,
                     AutoCadFramedRendererProofPolicy.NotTested,
                     StringComparison.Ordinal) ||
                 blockByToken.ContainsKey("E") ||
                 sourceByToken.ContainsKey("E") ||
                 manifest.Cases.Any(item => item.Token == "E"))
        {
            throw new InvalidOperationException(
                "E: NOT TESTED must not claim a successful proof entity.");
        }
        RequireSame(blockByToken, "H1", "H2");
        RequireSame(blockByToken, "H1", "H3");
        VerifyExpectedOverflowManifest(
            modelSpace,
            transaction,
            sourceByToken,
            manifest);
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        if (!blockTable.Has(manifest.LegacyBlockName) ||
            blockByToken["G"] == blockTable[manifest.LegacyBlockName])
        {
            throw new InvalidOperationException(
                "G: legacy definition was removed or migration did not occur.");
        }

        editor.WriteMessage(
            $"\n  E={manifest.RectangleCaseE.State}; token=" +
            $"{manifest.RectangleCaseE.ItemText ?? "<none>"}; " +
            $"mediumInnerWidth=" +
            $"{manifest.RectangleCaseE.MediumInnerWidthMm:R}; " +
            $"largeInnerWidth=" +
            $"{manifest.RectangleCaseE.LargeInnerWidthMm:R}.");
        editor.WriteMessage(
            $"\n  J={manifest.OverflowCaseJ.State}; token=" +
            $"{manifest.OverflowCaseJ.ItemText ?? "<none>"}; " +
            $"expected={manifest.OverflowCaseJ.ExpectedResultKind}; " +
            $"createdEntities=" +
            $"{manifest.OverflowCaseJ.ExpectedCreatedEntityCount}; " +
            $"{manifest.OverflowCaseJ.PreservationState}.");
        editor.WriteMessage(
            $"\nAK_DEV_FRAMED_RENDERER_VERIFY: success cases PASS; " +
            $"catalog definitions={manifest.VariantCatalogCount}; " +
            $"I={manifest.FailureCaseState}.");
        if (writeSuccessMessage)
        {
            editor.WriteMessage(
                "\nAK_DEV_FRAMED_RENDERER_VERIFY: PASS - read-only " +
                "SAVE/CLOSE/REOPEN production-renderer verification complete.");
        }
        return true;
    }

    private static void VerifyExpectedOverflowManifest(
        BlockTableRecord modelSpace,
        Transaction transaction,
        IReadOnlyDictionary<string, Entity> sourceByToken,
        AutoCadFramedRendererProofManifest manifest)
    {
        var overflow = manifest.OverflowCaseJ;
        if (sourceByToken.ContainsKey("J") ||
            manifest.Cases.Any(item => item.Token == "J"))
        {
            throw new InvalidOperationException(
                "J must not persist a source or successful entity case.");
        }
        if (string.Equals(
                overflow.State,
                AutoCadFramedRendererProofPolicy.NotTested,
                StringComparison.Ordinal))
        {
            if (overflow.ItemText is not null ||
                overflow.PreservationState !=
                    AutoCadFramedRendererProofPolicy.NotTested)
            {
                throw new InvalidOperationException(
                    "J: NOT TESTED manifest claims a tested token or preservation result.");
            }
            return;
        }

        var candidate = AutoCadFramedRendererProofPolicy
            .RectangleOverflowCandidates
            .SingleOrDefault(item => string.Equals(
                item.ItemText,
                overflow.ItemText,
                StringComparison.Ordinal));
        var parsedNumber = candidate is null
            ? null
            : TimberElementIdentityRules.TryParseElementNumber(
                candidate.ItemText,
                candidate.Prefix);
        var validToken = candidate is not null && parsedNumber is > 0 &&
            string.Equals(
                TimberElementIdentityRules.CreateElementId(
                    candidate.Prefix,
                    parsedNumber.Value),
                candidate.ItemText,
                StringComparison.Ordinal);
        if (!string.Equals(
                overflow.State,
                AutoCadFramedRendererProofPolicy.ExpectedOverflowPass,
                StringComparison.Ordinal) ||
            !string.Equals(
                overflow.ExpectedResultKind,
                AutoCadItemLeaderBlockVariantResultKind.TextOverflow.ToString(),
                StringComparison.Ordinal) ||
            !validToken ||
            overflow.MeasuredTextWidthMm is not double measuredWidth ||
            measuredWidth <= overflow.LargeInnerWidthMm ||
            overflow.ExpectedCreatedEntityCount != 0 ||
            overflow.ModelSpaceEntityDelta != 0 ||
            overflow.BlockDefinitionDelta != 0 ||
            overflow.VariantCatalogDelta != 0 ||
            !string.Equals(
                overflow.PreservationState,
                AutoCadFramedRendererProofPolicy.PreservationPass,
                StringComparison.Ordinal) ||
            ContainsFramedItemToken(
                modelSpace,
                transaction,
                overflow.ItemText!))
        {
            throw new InvalidOperationException(
                "J: expected TextOverflow/preservation manifest is invalid.");
        }
    }

    private static bool ContainsFramedItemToken(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string itemText)
    {
        foreach (var leader in modelSpace
                     .Cast<ObjectId>()
                     .Select(id => transaction.GetObject(
                         id,
                         OpenMode.ForRead,
                         false))
                     .OfType<MLeader>()
                     .Where(item =>
                         !item.IsErased &&
                         item.ContentType == ContentType.BlockContent))
        {
            var definition = transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead,
                false) as BlockTableRecord;
            if (definition is null)
            {
                continue;
            }
            foreach (var attribute in definition
                         .Cast<ObjectId>()
                         .Select(id => transaction.GetObject(
                             id,
                             OpenMode.ForRead,
                             false))
                         .OfType<AttributeDefinition>()
                         .Where(item => string.Equals(
                             item.Tag,
                             TimberItemLeaderBlockDefinitionRules.AttributeTag,
                             StringComparison.OrdinalIgnoreCase)))
            {
                using var instance = leader.GetBlockAttribute(
                    attribute.ObjectId);
                if (string.Equals(
                        instance.TextString,
                        itemText,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static TimberElementData CreateData(
        AutoCadFramedRendererProofCase proofCase,
        string styleName)
    {
        var isCustom = proofCase.ElementType == TimberElementType.Custom;
        return new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = proofCase.ItemText,
            ElementType = proofCase.ElementType,
            CustomElementTypeId = isCustom
                ? "0123456789abcdef0123456789abcdef"
                : null,
            CustomElementTypeName = isCustom
                ? "Proof custom element"
                : null,
            CustomElementTypePrefix = isCustom
                ? proofCase.CustomElementPrefix
                : null,
            AnnotationMode = proofCase.Mode,
            ItemNumberLeaderStyle = proofCase.ItemStyle,
            AnnotationScaleDenominatorOverride = proofCase.Denominator,
            AnnotationTextSettings = new TimberAnnotationTextSettings(
                styleName,
                2.5d,
                proofCase.ItemNumberPaperHeightMm,
                2.5d),
            WidthMm = 80d,
            HeightMm = 160d,
            SlopeDegrees = 35d,
            RoofPlaneId = "AK_DEV",
        };
    }

    private static MLeader FindFramedLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        TimberMainAnnotationComponentRole role) =>
        modelSpace
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<MLeader>()
            .Single(leader =>
                ElementLabelStore.TryRead(leader, out var label) &&
                label is not null &&
                string.Equals(
                    label.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) &&
                label.ComponentRole == role);

    private static void SetToken(
        Transaction transaction,
        MLeader leader,
        ObjectId attributeDefinitionId,
        string token)
    {
        var definition = (AttributeDefinition)transaction.GetObject(
            attributeDefinitionId,
            OpenMode.ForRead);
        using var attribute = new AttributeReference();
        attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
        attribute.TextString = token;
        leader.SetBlockAttribute(attributeDefinitionId, attribute);
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

    private static void EnsureRegApp(
        Database database,
        Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(AutoCadFramedRendererProofPolicy.RegAppName))
        {
            return;
        }
        table.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadFramedRendererProofPolicy.RegAppName,
        };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void WriteMarker(Entity source, string token)
    {
        using var marker = new ResultBuffer(
            new TypedValue(
                XDataRegAppCode,
                AutoCadFramedRendererProofPolicy.RegAppName),
            new TypedValue(
                XDataStringCode,
                AutoCadFramedRendererProofPolicy.SuiteIdentifier),
            new TypedValue(XDataStringCode, token));
        source.XData = marker;
    }

    private static bool TryReadMarker(Entity entity, out string? token)
    {
        token = null;
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return false;
        }
        var values = xdata.AsArray();
        for (var index = 0; index + 2 < values.Length; index++)
        {
            if (values[index].TypeCode == XDataRegAppCode &&
                string.Equals(
                    Convert.ToString(
                        values[index].Value,
                        CultureInfo.InvariantCulture),
                    AutoCadFramedRendererProofPolicy.RegAppName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    Convert.ToString(
                        values[index + 1].Value,
                        CultureInfo.InvariantCulture),
                    AutoCadFramedRendererProofPolicy.SuiteIdentifier,
                    StringComparison.Ordinal) &&
                values[index + 2].TypeCode == XDataStringCode)
            {
                token = Convert.ToString(
                    values[index + 2].Value,
                    CultureInfo.InvariantCulture);
                return !string.IsNullOrWhiteSpace(token);
            }
        }
        return false;
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadFramedRendererProofManifest manifest)
    {
        RemoveManifest(database, transaction);
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var values = new List<TypedValue>();
        for (var index = 0; index < json.Length; index += XRecordChunkLength)
        {
            values.Add(new TypedValue(
                XRecordStringCode,
                json.Substring(
                    index,
                    Math.Min(XRecordChunkLength, json.Length - index))));
        }
        var record = new Xrecord
        {
            Data = new ResultBuffer(values.ToArray()),
        };
        dictionary.SetAt(
            AutoCadFramedRendererProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadFramedRendererProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!dictionary.Contains(
                AutoCadFramedRendererProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }
        var record = (Xrecord)transaction.GetObject(
            dictionary.GetAt(
                AutoCadFramedRendererProofPolicy.ManifestDictionaryKey),
            OpenMode.ForRead);
        using var data = record.Data;
        if (data is null)
        {
            return null;
        }
        var json = new StringBuilder();
        foreach (var value in data.AsArray())
        {
            if (value.TypeCode == XRecordStringCode)
            {
                json.Append(Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture));
            }
        }
        return json.Length == 0
            ? null
            : JsonSerializer.Deserialize<
                AutoCadFramedRendererProofManifest>(
                    json.ToString(),
                    JsonOptions);
    }

    private static void RemoveManifest(
        Database database,
        Transaction transaction)
    {
        var dictionary = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!dictionary.Contains(
                AutoCadFramedRendererProofPolicy.ManifestDictionaryKey))
        {
            return;
        }
        dictionary.UpgradeOpen();
        var recordId = dictionary.GetAt(
            AutoCadFramedRendererProofPolicy.ManifestDictionaryKey);
        var record = (Xrecord)transaction.GetObject(
            recordId,
            OpenMode.ForWrite,
            false);
        dictionary.Remove(
            AutoCadFramedRendererProofPolicy.ManifestDictionaryKey);
        if (!record.IsErased)
        {
            record.Erase();
        }
    }

    private static void RequireSame(
        IReadOnlyDictionary<string, ObjectId> blocks,
        string left,
        string right)
    {
        if (blocks[left] != blocks[right])
        {
            throw new InvalidOperationException(
                $"Expected {left}/{right} to reuse one BlockTableRecord.");
        }
    }

    private static void RequireDifferent(
        IReadOnlyDictionary<string, ObjectId> blocks,
        string left,
        string right)
    {
        if (blocks[left] == blocks[right])
        {
            throw new InvalidOperationException(
                $"Expected {left}/{right} to use distinct BlockTableRecords.");
        }
    }
}
#endif
