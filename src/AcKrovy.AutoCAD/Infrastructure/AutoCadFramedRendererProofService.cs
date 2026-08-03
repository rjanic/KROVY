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
                var rectangleCase = AutoCadFramedRendererProofPolicy
                    .RectangleLargeCaseTemplate;
                var rectangleSelection = new AutoCadFramedRendererTokenSelection(
                    new AutoCadFramedRendererTokenCandidate(
                        rectangleCase.ItemText,
                        rectangleCase.CustomElementPrefix ?? "VT"),
                    MeasuredTextWidthMm: 0d,
                    [
                        new AutoCadFramedRendererTokenAttempt(
                            rectangleCase.ItemText,
                            rectangleCase.CustomElementPrefix ?? "VT",
                            true,
                            0d,
                            true,
                            "Resolve-based Rectangle Large token VT1234."),
                    ],
                    "Resolve-based Rectangle Large selection.");
                editor.WriteMessage(
                    $"\n  E: Resolve Large token={rectangleCase.ItemText} " +
                    "(font-independent baseline sizing).");
                var runtimeCases = AutoCadFramedRendererProofPolicy.Cases
                    .ToList();
                runtimeCases.Insert(
                    runtimeCases.FindIndex(candidate =>
                        candidate.Token == "F"),
                    rectangleCase);

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
                    var resolvedDefinition =
                        TimberItemLeaderBlockDefinitionRules.Resolve(
                            proofCase.ItemStyle,
                            proofCase.ItemText);
                    if (proofCase.Token == "E")
                    {
                        var isRequiredLargeFit =
                            resolvedDefinition.Size ==
                                TimberItemLeaderBlockSize.Large;
                        var creationFitOutcome = isRequiredLargeFit
                            ? AutoCadFramedRendererProofPolicy.FitPass
                            : "INVALID E FIT";
                        editor.WriteMessage(
                            $"\n  E: token={proofCase.ItemText}; " +
                            $"resolvedStyle=" +
                            $"{styles[proofCase.StyleSlot].CanonicalName}; " +
                            $"frameSize={resolvedDefinition.Size}; " +
                            $"resolveWidth={resolvedDefinition.WidthMm:R}; " +
                            $"{creationFitOutcome}");
                        if (!isRequiredLargeFit)
                        {
                            throw new InvalidOperationException(
                                "E did not Resolve to Rectangle Large (VT1234).");
                        }
                    }
                    if (renderResult is not { Succeeded: true } ||
                        renderResult.ResolvedBlockName is null ||
                        renderResult.VariantKey is null)
                    {
                        throw new InvalidOperationException(
                            $"Production render failed for {proofCase.Token}: " +
                            (renderResult?.DiagnosticReason ??
                             "no structured variant result"));
                    }
                    if (renderResult.VariantKey.FrameSize !=
                        resolvedDefinition.Size)
                    {
                        throw new InvalidOperationException(
                            $"Production size mismatch for {proofCase.Token}: " +
                            $"key={renderResult.VariantKey.FrameSize}, " +
                            $"resolve={resolvedDefinition.Size}");
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
                        resolvedDefinition.Size,
                        MeasuredTextWidthMm: 0d,
                        TimberItemLeaderBlockDefinitionRules
                            .CalculateAvailableInnerWidthMm(resolvedDefinition),
                        TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm));
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
        // J: Circle diameter invariant.  TextOverflow was the old production
        // sizing failure path.  The new contract uses font-independent Resolve
        // (frozen S/M/L), so Circle always maps to Small regardless of token length.
        // We prove the shared Circle definition is unchanged for a very long token.
        var circleToken = AutoCadFramedRendererProofPolicy.CircleLongInvariantText;
        const string circlePrefix = "WWWWWWWW";
        var parsedNumber = TimberElementIdentityRules.TryParseElementNumber(
            circleToken, circlePrefix);
        var validToken = parsedNumber is > 0 && string.Equals(
            TimberElementIdentityRules.CreateElementId(circlePrefix, parsedNumber.Value),
            circleToken,
            StringComparison.Ordinal);
        if (!validToken)
        {
            throw new InvalidOperationException(
                "J: Circle invariant token is not a valid canonical element id.");
        }

        var resolvedCircle = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle, circleToken);
        if (resolvedCircle.Size != TimberItemLeaderBlockSize.Small)
        {
            throw new InvalidOperationException(
                "J: Resolve(Circle, long_token) must always yield Small " +
                $"(got {resolvedCircle.Size}).");
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
            transaction, targetLeader);
        var modelSpaceCountBefore = CountLiveOwnedEntities(modelSpace, transaction);
        var blockDefinitionCountBefore =
            CountLiveBlockDefinitions(database, transaction);
        var catalogCountBefore = batch.ItemLeaderVariantCatalog.Count;

        // Verify Circle definition in catalog — no production EnsureForElement
        // mutation expected; Circle Small is already resolved from case A.
        var circleResult = AcKrovyItemLeaderBlockVariantService.EnsureResolved(
            database,
            transaction,
            ItemNumberLeaderStyle.Circle,
            circleToken,
            batch.ItemLeaderVariantCatalog);

        var leaderSignatureAfter = ReadLeaderPreservationSignature(
            transaction, targetLeader);
        var modelSpaceDelta =
            CountLiveOwnedEntities(modelSpace, transaction) - modelSpaceCountBefore;
        var blockDefinitionDelta =
            CountLiveBlockDefinitions(database, transaction) - blockDefinitionCountBefore;
        var catalogDelta =
            batch.ItemLeaderVariantCatalog.Count - catalogCountBefore;

        var preservationPassed =
            circleResult.Succeeded &&
            modelSpaceDelta == 0 &&
            blockDefinitionDelta == 0 &&
            catalogDelta == 0 &&
            string.Equals(
                leaderSignatureBefore,
                leaderSignatureAfter,
                StringComparison.Ordinal) &&
            !ContainsFramedItemToken(modelSpace, transaction, circleToken);
        if (!preservationPassed)
        {
            throw new InvalidOperationException(
                "J Circle invariant: unexpected mutation or catalog growth.");
        }

        editor.WriteMessage(
            $"\n  J: circleToken={circleToken}; resolvedSize={resolvedCircle.Size}; " +
            $"CircleDiameter=" +
            $"{TimberItemLeaderBlockDefinitionRules.CircleDiameterMm:R}; " +
            $"resultKind={circleResult.Kind}; blockName={circleResult.ResolvedBlockName}; " +
            $"modelSpaceDelta={modelSpaceDelta}; blockDefinitionDelta={blockDefinitionDelta}; " +
            $"catalogDelta={catalogDelta}; " +
            $"{AutoCadFramedRendererProofPolicy.CircleInvariantPass}");
        return new AutoCadFramedRendererOverflowCaseManifest(
            "J",
            AutoCadFramedRendererProofPolicy.CircleInvariantPass,
            circleToken,
            circleResult.Kind.ToString(),
            style.CanonicalName,
            null,
            rectangleLargeInnerWidth,
            TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm,
            0,
            modelSpaceDelta,
            blockDefinitionDelta,
            catalogDelta,
            AutoCadFramedRendererProofPolicy.PreservationPass,
            "Resolve(Circle, long_token) always yields Small; " +
            "shared definition is invariant.",
            []);
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
            var resolvedDefinition =
                TimberItemLeaderBlockDefinitionRules.Resolve(
                    expected.ItemStyle,
                    expected.ItemText);
            editor.WriteMessage(
                $"\n  {expected.Token}: token={expected.ItemText}; " +
                $"frameSize={resolvedDefinition.Size}; " +
                $"resolveWidth={resolvedDefinition.WidthMm:R}; " +
                $"availableInnerWidth=" +
                $"{TimberItemLeaderBlockDefinitionRules.CalculateAvailableInnerWidthMm(resolvedDefinition):R}; " +
                $"padding={TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm:R}; " +
                $"{AutoCadFramedRendererProofPolicy.FitPass}");
            if (expected.Token == "E" &&
                resolvedDefinition.Size != TimberItemLeaderBlockSize.Large)
            {
                throw new InvalidOperationException(
                    "E: persisted entity did not Resolve to Rectangle Large.");
            }
            if (resolvedDefinition.Size != expected.FrameSize ||
                Math.Abs(
                    TimberItemLeaderBlockDefinitionRules
                        .CalculateAvailableInnerWidthMm(resolvedDefinition) -
                    expected.AvailableInnerWidthMm) > Tolerance ||
                Math.Abs(
                    TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm -
                    expected.HorizontalPaddingMm) > Tolerance)
            {
                throw new InvalidOperationException(
                    $"{expected.Token}: persisted Resolve size/inner-width mismatch.");
            }
            var definition = resolvedDefinition;
            var expectedBaseHeight = expected.ItemNumberPaperHeightMm *
                TimberAnnotationScaleRules.DefaultDenominator;
            var expectedScale = expected.Denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator;
            using var instanceAttribute =
                leader.GetBlockAttribute(attribute.ObjectId);
            var instanceStyle = (TextStyleTableRecord)transaction.GetObject(
                instanceAttribute.TextStyleId,
                OpenMode.ForRead);
            if (!string.Equals(
                    instanceStyle.Name,
                    expected.StyleName,
                    StringComparison.Ordinal) ||
                Math.Abs(
                    attribute.Height -
                    TimberItemLeaderBlockDefinitionRules
                        .BaseFramedItemTextHeightAtScale50Mm) > Tolerance ||
                Math.Abs(
                    instanceAttribute.Height - expectedBaseHeight) > Tolerance ||
                Math.Abs(leader.BlockScale.X - expectedScale) > Tolerance ||
                Math.Abs(
                    instanceAttribute.Height * leader.BlockScale.X -
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
                definition);
            var validation = AcKrovyItemLeaderBlockVariantService
                .ValidateExistingDefinitionDetailed(
                    database,
                    transaction,
                    block.ObjectId,
                    definition,
                    key);
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
        RequireSame(blockByToken, "A", "C");
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

        // J Circle invariant: Resolve(Circle, any_token) = Small; zero DB mutation.
        if (!string.Equals(
                overflow.State,
                AutoCadFramedRendererProofPolicy.CircleInvariantPass,
                StringComparison.Ordinal) ||
            overflow.ItemText is null ||
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
                overflow.ItemText))
        {
            throw new InvalidOperationException(
                "J: Circle invariant/preservation manifest is invalid.");
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
            AnnotationTextSettings = TimberAnnotationTextSettings.Shared(
                styleName,
                proofCase.ItemNumberPaperHeightMm,
                2.5d,
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
