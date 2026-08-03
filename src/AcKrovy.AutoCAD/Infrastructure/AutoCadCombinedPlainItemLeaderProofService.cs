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

internal static class AutoCadCombinedPlainItemLeaderProofService
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
            ObjectId refreshedItemLeaderId;
            ObjectId refreshedDimensionsId;
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
                        $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: FAIL - exact " +
                        $"ModelSpace contains {existing.Length} real entities. " +
                        "Layouts, paper-space objects, AEC dictionaries and " +
                        "nested block definitions were not counted.");
                    return;
                }

                var defaultProfile = TimberElementDefaultProfile.CreateDefault();
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    defaultProfile);
                if (batch.TextStyleCatalog.CompatibleStyles.Count == 0)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: NOT TESTED - " +
                        "Architecture DWG has no compatible variable-height " +
                        "nonannotative text style. No proof entity was created.");
                    return;
                }

                var styles = batch.TextStyleCatalog.CompatibleStyles
                    .Take(2)
                    .ToArray();
                EnsureRegApp(database, transaction);
                modelSpace.UpgradeOpen();

                var expected =
                    new List<AutoCadCombinedPlainItemLeaderProofExpectedCase>();
                ObjectId? caseAItemId = null;
                ObjectId? caseADimensionsId = null;

                for (var index = 0;
                     index < AutoCadCombinedPlainItemLeaderProofPolicy.Cases.Count;
                     index++)
                {
                    var proofCase =
                        AutoCadCombinedPlainItemLeaderProofPolicy.Cases[index];
                    var styleName = ResolveStyleName(proofCase, styles);
                    var source = CreateSource(
                        database,
                        transaction,
                        modelSpace,
                        index);
                    WriteMarker(source, proofCase.Token);

                    var data = CreateData(proofCase, styleName);
                    var presentation = batch.ResolveForElement(data);
                    if (!presentation.HasCompatibleStyle)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: NOT TESTED - " +
                            $"case {proofCase.Token} has NoCompatibleStyle.");
                        return;
                    }

                    TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        source,
                        data,
                        batch);

                    var sourceHandle = source.Handle.ToString();
                    string failureOutcome = string.Empty;

                    if (proofCase.IsStandaloneRegressionCase)
                    {
                        var standalone = FindStandalonePlainItemLeader(
                            modelSpace,
                            transaction,
                            sourceHandle);
                        if (standalone is null ||
                            !TryReadLeaderPresentation(
                                standalone,
                                out _,
                                out var standaloneLeaderStyle,
                                out var standaloneMtextStyle,
                                out var standaloneHeight) ||
                            standaloneLeaderStyle != standaloneMtextStyle ||
                            !AreClose(
                                standaloneHeight,
                                presentation.ItemNumberModelHeight))
                        {
                            throw new InvalidOperationException(
                                "Standalone regression case F did not produce " +
                                "a valid Plain ItemNumberLeader.");
                        }

                        expected.Add(
                            AutoCadCombinedPlainItemLeaderProofPolicy.ToExpected(
                                proofCase,
                                presentation.ResolvedTextStyleName ?? styleName,
                                presentation.EffectiveTextSettings
                                    .ItemCodePaperHeightMm,
                                presentation.AnnotationScaleDenominator,
                                presentation.TextStyleResolutionKind.ToString(),
                                presentation.IsFallback));
                        editor.WriteMessage(
                            $"\n  {proofCase.Token}: standalone style=" +
                            $"{presentation.ResolvedTextStyleName}; " +
                            $"modelHeight={presentation.ItemNumberModelHeight:R}; " +
                            "PASS");
                        continue;
                    }

                    var itemLeader = FindCombinedPlainItemLeader(
                        modelSpace,
                        transaction,
                        sourceHandle);
                    var dimensions = FindCombinedDimensionsMText(
                        modelSpace,
                        transaction,
                        sourceHandle);
                    if (itemLeader is null || dimensions is null)
                    {
                        throw new InvalidOperationException(
                            $"Combined Plain composite missing for {proofCase.Token}.");
                    }

                    if (!TryReadLeaderPresentation(
                            itemLeader,
                            out _,
                            out var itemLeaderStyleId,
                            out var itemMtextStyleId,
                            out var itemHeight))
                    {
                        throw new InvalidOperationException(
                            $"Combined Plain item MText missing for {proofCase.Token}.");
                    }

                    if (itemLeaderStyleId != itemMtextStyleId)
                    {
                        throw new InvalidOperationException(
                            $"Case {proofCase.Token}: item MLeader.TextStyleId != MText.TextStyleId.");
                    }

                    if (proofCase.Token == "A")
                    {
                        caseAItemId = itemLeader.ObjectId;
                        caseADimensionsId = dimensions.ObjectId;
                    }

                    if (proofCase.IsFailurePreservationCase)
                    {
                        failureOutcome = RunFailurePreservation(
                            database,
                            transaction,
                            batch,
                            source,
                            data,
                            itemLeader.ObjectId,
                            dimensions.ObjectId,
                            itemHeight,
                            itemLeaderStyleId,
                            itemMtextStyleId,
                            dimensions.TextHeight,
                            dimensions.TextStyleId,
                            editor);
                    }

                    expected.Add(
                        AutoCadCombinedPlainItemLeaderProofPolicy.ToExpected(
                            proofCase,
                            presentation.ResolvedTextStyleName ?? styleName,
                            presentation.EffectiveTextSettings
                                .ItemCodePaperHeightMm,
                            presentation.AnnotationScaleDenominator,
                            presentation.TextStyleResolutionKind.ToString(),
                            presentation.IsFallback,
                            failureOutcome));

                    editor.WriteMessage(
                        $"\n  {proofCase.Token}: itemStyle=" +
                        $"{presentation.ResolvedTextStyleName}; " +
                        $"itemHeight={itemHeight:R}; " +
                        $"expectedItem={presentation.ItemNumberModelHeight:R}; " +
                        $"dimensionsHeight={dimensions.TextHeight:R}; " +
                        $"mLeaderStyle={itemLeaderStyleId.Handle}; " +
                        $"mTextStyle={itemMtextStyleId.Handle}" +
                        (string.IsNullOrEmpty(failureOutcome)
                            ? string.Empty
                            : $"; failure={failureOutcome}"));
                }

                if (caseAItemId is null || caseADimensionsId is null)
                {
                    throw new InvalidOperationException(
                        "Refresh case A composite ObjectIds were not captured.");
                }

                var refreshSource = FindMarkedSource(
                    modelSpace,
                    transaction,
                    "A") ??
                    throw new InvalidOperationException(
                        "Refresh source A was not found.");
                var refreshData = CreateData(
                    AutoCadCombinedPlainItemLeaderProofPolicy.Cases[0],
                    ResolveStyleName(
                        AutoCadCombinedPlainItemLeaderProofPolicy.Cases[0],
                        styles));
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    refreshSource,
                    refreshData,
                    batch);
                var refreshedItem = FindCombinedPlainItemLeader(
                    modelSpace,
                    transaction,
                    refreshSource.Handle.ToString());
                var refreshedDimensions = FindCombinedDimensionsMText(
                    modelSpace,
                    transaction,
                    refreshSource.Handle.ToString());
                if (refreshedItem is null ||
                    refreshedDimensions is null ||
                    refreshedItem.ObjectId != caseAItemId ||
                    refreshedDimensions.ObjectId != caseADimensionsId)
                {
                    var expectedItemId = caseAItemId!.Value;
                    var expectedDimensionsId = caseADimensionsId!.Value;
                    WriteCompositeCheckpoint(
                        editor,
                        "A-refresh",
                        modelSpace,
                        transaction,
                        refreshSource.Handle.ToString(),
                        refreshedItem?.ObjectId ?? ObjectId.Null,
                        refreshedDimensions?.ObjectId ?? ObjectId.Null,
                        $"expectedItem={expectedItemId.Handle}; " +
                        $"expectedDimensions={expectedDimensionsId.Handle}");
                    var actualItemHandle = refreshedItem is null
                        ? "<missing>"
                        : refreshedItem.ObjectId.Handle.ToString();
                    var actualDimensionsHandle = refreshedDimensions is null
                        ? "<missing>"
                        : refreshedDimensions.ObjectId.Handle.ToString();
                    throw new InvalidOperationException(
                        "Combined Plain refresh did not preserve both composite ObjectIds. " +
                        $"expectedItem={expectedItemId.Handle}; " +
                        $"actualItem={actualItemHandle}; " +
                        $"expectedDimensions={expectedDimensionsId.Handle}; " +
                        $"actualDimensions={actualDimensionsHandle}.");
                }

                editor.WriteMessage(
                    $"\n  {AutoCadCombinedPlainItemLeaderProofPolicy.RefreshToken}: " +
                    $"itemObjectId={refreshedItem.ObjectId.Handle}; " +
                    $"dimensionsObjectId={refreshedDimensions.ObjectId.Handle}; PASS");

                refreshedItemLeaderId = caseAItemId.Value;
                refreshedDimensionsId = caseADimensionsId.Value;
                var manifest = new AutoCadCombinedPlainItemLeaderProofManifest(
                    AutoCadCombinedPlainItemLeaderProofPolicy.SchemaVersion,
                    AutoCadCombinedPlainItemLeaderProofPolicy.SuiteIdentifier,
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
                requireRefreshItemId: refreshedItemLeaderId,
                requireRefreshDimensionsId: refreshedDimensionsId);
            editor.WriteMessage(
                passed
                    ? "\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: PASS - production " +
                      "combined Plain item presentation verified after commit."
                    : "\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: FAIL - post-commit " +
                      "readback did not match the expected presentation.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}. " +
                "Partial entities were not committed; the single CREATE " +
                "transaction was rolled back. No completed proof manifest " +
                "was written.");
        }
    }

    public static void Verify()
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
            var dbmodBefore = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            using var transaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyCore(
                database,
                transaction,
                editor,
                requireRefreshItemId: null,
                requireRefreshDimensionsId: null);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_VERIFY: PASS - read-only; " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                    : $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_VERIFY: FAIL - " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_VERIFY: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void Clean()
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
                    OpenMode.ForWrite);
                var removed = 0;
                foreach (ObjectId id in modelSpace)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false)
                            is not Entity entity ||
                        entity.IsErased ||
                        entity.OwnerId != modelSpace.ObjectId ||
                        !HasMarker(entity))
                    {
                        continue;
                    }

                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }

                foreach (ObjectId id in modelSpace)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false)
                            is not Entity entity ||
                        entity.IsErased ||
                        entity.OwnerId != modelSpace.ObjectId)
                    {
                        continue;
                    }

                    if (!ElementLabelStore.TryRead(entity, out var data) ||
                        data is null)
                    {
                        continue;
                    }

                    entity.UpgradeOpen();
                    entity.Erase();
                    removed++;
                }

                DeleteManifest(database, transaction);
                transaction.Commit();
                editor.WriteMessage(
                    $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CLEAN: PASS - removed " +
                    $"{removed} proof-related ModelSpace entities.");
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_COMBINED_PLAIN_ITEM_TEXT_CLEAN: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string RunFailurePreservation(
        Database database,
        Transaction transaction,
        AutoCadAnnotationPresentationBatchContext batch,
        Entity source,
        TimberElementData data,
        ObjectId itemLeaderId,
        ObjectId dimensionsId,
        double expectedItemHeight,
        ObjectId expectedItemLeaderStyleId,
        ObjectId expectedItemMtextStyleId,
        double expectedDimensionsHeight,
        ObjectId expectedDimensionsStyleId,
        Editor editor)
    {
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForRead);
        var sourceHandle = source.Handle.ToString();
        var noCompatible =
            batch.TextStyleCatalog.CompatibleStyles.Count == 0
                ? AutoCadCombinedPlainItemLeaderProofPolicy.FailureCasePreserved
                : AutoCadCombinedPlainItemLeaderProofPolicy.FailureCaseNotTested;

        WriteCompositeCheckpoint(
            editor,
            "E0",
            modelSpace,
            transaction,
            sourceHandle,
            itemLeaderId,
            dimensionsId,
            "baseline after successful combined create");

        WriteCompositeCheckpoint(
            editor,
            "E1",
            modelSpace,
            transaction,
            sourceHandle,
            itemLeaderId,
            dimensionsId,
            "immediately before null-context TryPrepare");
        AssertCompositeUnchanged(
            modelSpace,
            transaction,
            sourceHandle,
            itemLeaderId,
            dimensionsId,
            expectedItemHeight,
            expectedItemLeaderStyleId,
            expectedItemMtextStyleId,
            expectedDimensionsHeight,
            expectedDimensionsStyleId,
            checkpoint: "E1");

        editor.WriteMessage(
            "\n    E1 mutations: UpsertLeader=no UpsertLabel=no " +
            "DeleteUnexpectedCompositeComponents=no EraseMainAnnotation=no " +
            "CreateNativeMLeader=no TryUpdateNativeLeader=no " +
            "(null-context call is policy-only)");

        if (!AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(
                database,
                presentationContext: null,
                out _,
                out var nullDiagnostic))
        {
            editor.WriteMessage(
                $"\n    E null-context prepare failed before ForWrite: {nullDiagnostic}");
        }
        else
        {
            throw new InvalidOperationException(
                "Null presentation context must fail Plain item preparation.");
        }

        WriteCompositeCheckpoint(
            editor,
            "E2",
            modelSpace,
            transaction,
            sourceHandle,
            itemLeaderId,
            dimensionsId,
            "immediately after null-context TryPrepare returned false");
        AssertCompositeUnchanged(
            modelSpace,
            transaction,
            sourceHandle,
            itemLeaderId,
            dimensionsId,
            expectedItemHeight,
            expectedItemLeaderStyleId,
            expectedItemMtextStyleId,
            expectedDimensionsHeight,
            expectedDimensionsStyleId,
            checkpoint: "E2");

        editor.WriteMessage(
            "\n    E2 failure-preservation PASS: ObjectId/Handle/presentation " +
            "identical from E1 to E2. No composite mutation occurred.");

        // Recovery Ensure is a separate lifecycle probe. Create-before-erase may
        // legitimately replace ObjectIds; that must not affect E1→E2 PASS.
        TimberAnnotationService.EnsureForElement(
            database,
            transaction,
            source,
            data,
            batch);

        var recoveredItem = FindCombinedPlainItemLeader(
            modelSpace,
            transaction,
            sourceHandle);
        var recoveredDimensions = FindCombinedDimensionsMText(
            modelSpace,
            transaction,
            sourceHandle);
        if (recoveredItem is null || recoveredDimensions is null)
        {
            throw new InvalidOperationException(
                "E3 recovery Ensure did not recreate the combined composite.");
        }

        WriteCompositeCheckpoint(
            editor,
            "E3",
            modelSpace,
            transaction,
            sourceHandle,
            recoveredItem.ObjectId,
            recoveredDimensions.ObjectId,
            recoveredItem.ObjectId == itemLeaderId &&
            recoveredDimensions.ObjectId == dimensionsId
                ? "recovery Ensure preserved ObjectIds in-place"
                : "recovery Ensure replaced ObjectIds (create-before-erase OK)");

        if (!TryReadLeaderPresentation(
                recoveredItem,
                out _,
                out var recoveredLeaderStyle,
                out var recoveredMtextStyle,
                out var recoveredHeight) ||
            recoveredLeaderStyle != recoveredMtextStyle ||
            !AreClose(recoveredHeight, expectedItemHeight) ||
            !AreClose(recoveredDimensions.TextHeight, expectedDimensionsHeight))
        {
            throw new InvalidOperationException(
                "E3 recovery Ensure produced an invalid combined presentation.");
        }

        editor.WriteMessage(
            $"\n    E NoCompatibleStyle={noCompatible}; " +
            "failure preservation judged solely by E1→E2; " +
            "E3 recovery is a separate lifecycle check.");
        return noCompatible;
    }

    private static void AssertCompositeUnchanged(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        ObjectId itemLeaderId,
        ObjectId dimensionsId,
        double expectedItemHeight,
        ObjectId expectedItemLeaderStyleId,
        ObjectId expectedItemMtextStyleId,
        double expectedDimensionsHeight,
        ObjectId expectedDimensionsStyleId,
        string checkpoint)
    {
        var itemLeader = FindCombinedPlainItemLeader(
            modelSpace,
            transaction,
            sourceHandle);
        var dimensions = FindCombinedDimensionsMText(
            modelSpace,
            transaction,
            sourceHandle);
        if (itemLeader is null ||
            dimensions is null ||
            itemLeader.ObjectId != itemLeaderId ||
            dimensions.ObjectId != dimensionsId)
        {
            throw new InvalidOperationException(
                $"Failure preservation {checkpoint}: combined composite " +
                "ObjectIds changed.");
        }

        if (itemLeader.IsErased || dimensions.IsErased)
        {
            throw new InvalidOperationException(
                $"Failure preservation {checkpoint}: composite entity erased.");
        }

        if (!TryReadLeaderPresentation(
                itemLeader,
                out _,
                out var itemLeaderStyleId,
                out var itemMtextStyleId,
                out var itemHeight) ||
            itemLeaderStyleId != expectedItemLeaderStyleId ||
            itemMtextStyleId != expectedItemMtextStyleId ||
            !AreClose(itemHeight, expectedItemHeight) ||
            !AreClose(dimensions.TextHeight, expectedDimensionsHeight) ||
            dimensions.TextStyleId != expectedDimensionsStyleId)
        {
            throw new InvalidOperationException(
                $"Failure preservation {checkpoint}: combined composite " +
                "presentation changed.");
        }
    }

    private static void WriteCompositeCheckpoint(
        Editor editor,
        string checkpoint,
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        ObjectId itemLeaderId,
        ObjectId dimensionsId,
        string note)
    {
        var itemLeader = FindCombinedPlainItemLeader(
            modelSpace,
            transaction,
            sourceHandle);
        var dimensions = FindCombinedDimensionsMText(
            modelSpace,
            transaction,
            sourceHandle);
        var matchingCount = CountCompositeEntities(
            modelSpace,
            transaction,
            sourceHandle);

        editor.WriteMessage(
            $"\n    {checkpoint}: note={note}; matchingCompositeEntities={matchingCount}");
        WriteEntityCheckpoint(
            editor,
            checkpoint,
            "item",
            itemLeader,
            itemLeaderId,
            expected: true);
        WriteEntityCheckpoint(
            editor,
            checkpoint,
            "dimensions",
            dimensions,
            dimensionsId,
            expected: true);
    }

    private static void WriteEntityCheckpoint(
        Editor editor,
        string checkpoint,
        string label,
        Entity? entity,
        ObjectId expectedId,
        bool expected)
    {
        if (entity is null)
        {
            editor.WriteMessage(
                $"\n      {checkpoint}.{label}: MISSING expectedId={expectedId.Handle}");
            return;
        }

        ElementLabelStore.TryRead(entity, out var data);
        var representation = data is null
            ? "<none>"
            : TimberAnnotationModeRules.GetRepresentation(
                data.AnnotationMode,
                data.ItemNumberLeaderStyle).ToString();
        editor.WriteMessage(
            $"\n      {checkpoint}.{label}: ObjectId={entity.ObjectId.Handle}; " +
            $"Handle={entity.Handle}; IsErased={entity.IsErased}; " +
            $"SourceHandle={data?.SourceHandle ?? "<none>"}; " +
            $"role={data?.ComponentRole.ToString() ?? "<none>"}; " +
            $"representation={representation}; " +
            $"sameAsExpected={entity.ObjectId == expectedId}; " +
            $"expected={(expected ? "present" : "absent")}");
    }

    private static int CountCompositeEntities(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        var count = 0;
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not Entity entity ||
                entity.IsErased ||
                entity.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) ||
                TimberAnnotationModeRules.Normalize(data.AnnotationMode) !=
                    TimberAnnotationMode.DimensionsWithItemNumber)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor,
        ObjectId? requireRefreshItemId,
        ObjectId? requireRefreshDimensionsId)
    {
        var manifest = ReadManifest(database, transaction);
        if (manifest is null)
        {
            editor.WriteMessage(
                "\n  missing combined Plain item proof manifest");
            return false;
        }

        if (manifest.SchemaVersion !=
                AutoCadCombinedPlainItemLeaderProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadCombinedPlainItemLeaderProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage(
                "\n  unexpected combined Plain item proof manifest");
            return false;
        }

        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        var passed = true;
        foreach (var expected in manifest.Cases)
        {
            var source = FindMarkedSource(
                modelSpace,
                transaction,
                expected.Token);
            if (source is null)
            {
                editor.WriteMessage($"\n  {expected.Token}: FAIL - source missing");
                passed = false;
                continue;
            }

            var sourceHandle = source.Handle.ToString();
            if (expected.IsStandaloneRegressionCase)
            {
                var standalone = FindStandalonePlainItemLeader(
                    modelSpace,
                    transaction,
                    sourceHandle);
                if (standalone is null ||
                    !TryReadLeaderPresentation(
                        standalone,
                        out _,
                        out var standaloneLeaderStyle,
                        out var standaloneMtextStyle,
                        out var standaloneHeight))
                {
                    editor.WriteMessage(
                        $"\n  {expected.Token}: FAIL - standalone leader missing");
                    passed = false;
                    continue;
                }

                var standaloneOk =
                    standaloneLeaderStyle == standaloneMtextStyle &&
                    AreClose(standaloneHeight, expected.ItemModelHeightMm);
                editor.WriteMessage(
                    $"\n  {expected.Token}: standaloneHeight={standaloneHeight:R}; " +
                    $"expected={expected.ItemModelHeightMm:R}; " +
                    (standaloneOk ? "PASS" : "FAIL"));
                passed &= standaloneOk;
                continue;
            }

            var itemLeader = FindCombinedPlainItemLeader(
                modelSpace,
                transaction,
                sourceHandle);
            var dimensions = FindCombinedDimensionsMText(
                modelSpace,
                transaction,
                sourceHandle);
            if (itemLeader is null || dimensions is null)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}: FAIL - composite missing");
                passed = false;
                continue;
            }

            if (!TryReadLeaderPresentation(
                    itemLeader,
                    out _,
                    out var itemLeaderStyleId,
                    out var itemMtextStyleId,
                    out var itemHeight))
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}: FAIL - item MText missing");
                passed = false;
                continue;
            }

            var styleName = ResolveStyleName(
                database,
                transaction,
                itemLeaderStyleId);
            var styleMatches = string.Equals(
                styleName,
                expected.StyleName,
                StringComparison.OrdinalIgnoreCase);
            var itemHeightMatches = AreClose(itemHeight, expected.ItemModelHeightMm);
            var dimensionsHeightMatches = AreClose(
                dimensions.TextHeight,
                expected.DimensionsModelHeightMm);
            var styleParity = itemLeaderStyleId == itemMtextStyleId;
            var casePassed = styleMatches &&
                itemHeightMatches &&
                dimensionsHeightMatches &&
                styleParity;

            if (expected.ExpectRefreshSameObjectId &&
                requireRefreshItemId is ObjectId refreshItemId &&
                requireRefreshDimensionsId is ObjectId refreshDimensionsId)
            {
                casePassed = casePassed &&
                    itemLeader.ObjectId == refreshItemId &&
                    dimensions.ObjectId == refreshDimensionsId;
            }

            if (expected.IsFailurePreservationCase)
            {
                var failureOk =
                    string.Equals(
                        expected.FailureOutcome,
                        AutoCadCombinedPlainItemLeaderProofPolicy
                            .FailureCaseNotTested,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        expected.FailureOutcome,
                        AutoCadCombinedPlainItemLeaderProofPolicy
                            .FailureCasePreserved,
                        StringComparison.Ordinal);
                casePassed = casePassed && failureOk;
            }

            editor.WriteMessage(
                $"\n  {expected.Token}: style={styleName}; " +
                $"itemHeight={itemHeight:R}; " +
                $"expectedItem={expected.ItemModelHeightMm:R}; " +
                $"dimensionsHeight={dimensions.TextHeight:R}; " +
                $"expectedDimensions={expected.DimensionsModelHeightMm:R}; " +
                $"kind={expected.ResolutionKind}" +
                (expected.IsFailurePreservationCase
                    ? $"; failure={expected.FailureOutcome}"
                    : string.Empty) +
                $"; {(casePassed ? "PASS" : "FAIL")}");
            passed &= casePassed;
        }

        if (requireRefreshItemId is ObjectId)
        {
            editor.WriteMessage(
                $"\n  {AutoCadCombinedPlainItemLeaderProofPolicy.RefreshToken}: " +
                "in-place refresh checked via case A ObjectId parity.");
        }

        return passed;
    }

    private static Entity CreateSource(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        int index)
    {
        var source = new Line(
            new Point3d(index * 2500d, 0d, 0d),
            new Point3d(index * 2500d + 1200d, 0d, 0d));
        source.SetDatabaseDefaults(database);
        modelSpace.AppendEntity(source);
        transaction.AddNewlyCreatedDBObject(source, true);
        return source;
    }

    private static TimberElementData CreateData(
        AutoCadCombinedPlainItemLeaderProofCase proofCase,
        string styleName)
    {
        TimberAnnotationTextSettings? settings = proofCase.TextSettings;
        if (settings is not null)
        {
            settings = settings with { TextStyleName = styleName };
        }

        return new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = $"CP-{proofCase.Token}",
            ElementType = TimberElementType.Rafter,
            WidthMm = 80d,
            HeightMm = 160d,
            AnnotationMode = proofCase.IsStandaloneRegressionCase
                ? TimberAnnotationMode.ItemNumberLeader
                : TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            AnnotationTextSettings = settings,
            AnnotationScaleDenominatorOverride =
                proofCase.DenominatorOverride,
            SlopeDegrees = 35d,
            RoofPlaneId = "AK_DEV",
        };
    }

    private static string ResolveStyleName(
        AutoCadCombinedPlainItemLeaderProofCase proofCase,
        IReadOnlyList<AutoCadTextStyleCatalogEntry> styles)
    {
        if (proofCase.StyleSlot < 0)
        {
            return styles[0].CanonicalName;
        }

        var slot = Math.Min(proofCase.StyleSlot, styles.Count - 1);
        return styles[slot].CanonicalName;
    }

    private static MLeader? FindCombinedPlainItemLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not MLeader leader ||
                leader.IsErased ||
                leader.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(leader, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) !=
                    TimberAnnotationMode.DimensionsWithItemNumber ||
                ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) !=
                    ItemNumberLeaderStyle.Plain ||
                data.ComponentRole !=
                    TimberMainAnnotationComponentRole.FramedItem)
            {
                continue;
            }

            return leader;
        }

        return null;
    }

    private static MText? FindCombinedDimensionsMText(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not MText label ||
                label.IsErased ||
                label.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(label, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) !=
                    TimberAnnotationMode.DimensionsWithItemNumber ||
                data.ComponentRole !=
                    TimberMainAnnotationComponentRole.Primary)
            {
                continue;
            }

            return label;
        }

        return null;
    }

    private static MLeader? FindStandalonePlainItemLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not MLeader leader ||
                leader.IsErased ||
                leader.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!ElementLabelStore.TryRead(leader, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) !=
                    TimberAnnotationMode.ItemNumberLeader ||
                ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) !=
                    ItemNumberLeaderStyle.Plain)
            {
                continue;
            }

            return leader;
        }

        return null;
    }

    private static bool TryReadLeaderPresentation(
        MLeader leader,
        out MText? mText,
        out ObjectId leaderStyleId,
        out ObjectId mTextStyleId,
        out double textHeight)
    {
        mText = null;
        leaderStyleId = leader.TextStyleId;
        mTextStyleId = ObjectId.Null;
        textHeight = 0d;
        if (leader.ContentType != ContentType.MTextContent ||
            leader.MText is not MText content)
        {
            return false;
        }

        mText = content;
        mTextStyleId = content.TextStyleId;
        textHeight = content.TextHeight;
        return true;
    }

    private static Entity? FindMarkedSource(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string token)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not Entity entity ||
                entity.IsErased ||
                entity.OwnerId != modelSpace.ObjectId ||
                !HasMarker(entity, token))
            {
                continue;
            }

            return entity;
        }

        return null;
    }

    private static void WriteMarker(Entity entity, string token)
    {
        entity.XData = new ResultBuffer(
            new TypedValue(
                XDataRegAppCode,
                AutoCadCombinedPlainItemLeaderProofPolicy.RegAppName),
            new TypedValue(XDataStringCode, token));
    }

    private static bool HasMarker(Entity entity, string? token = null)
    {
        var buffer = entity.XData;
        if (buffer is null)
        {
            return false;
        }

        string? currentApp = null;
        foreach (TypedValue value in buffer)
        {
            if (value.TypeCode == XDataRegAppCode)
            {
                currentApp = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (!string.Equals(
                    currentApp,
                    AutoCadCombinedPlainItemLeaderProofPolicy.RegAppName,
                    StringComparison.OrdinalIgnoreCase) ||
                value.TypeCode != XDataStringCode)
            {
                continue;
            }

            var marker = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
            return token is null ||
                string.Equals(marker, token, StringComparison.Ordinal);
        }

        return false;
    }

    private static void EnsureRegApp(Database database, Transaction transaction)
    {
        var regAppTable = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regAppTable.Has(AutoCadCombinedPlainItemLeaderProofPolicy.RegAppName))
        {
            return;
        }

        regAppTable.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadCombinedPlainItemLeaderProofPolicy.RegAppName,
        };
        regAppTable.Add(record);
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

    private static string ResolveStyleName(
        Database database,
        Transaction transaction,
        ObjectId textStyleId)
    {
        if (textStyleId.IsNull ||
            !AutoCadDatabaseIdentity.IsSame(database, textStyleId))
        {
            return "<invalid>";
        }

        return transaction.GetObject(textStyleId, OpenMode.ForRead, false)
            is TextStyleTableRecord style
            ? style.Name
            : "<missing>";
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadCombinedPlainItemLeaderProofManifest manifest)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var record = new Xrecord();
        var values = new ResultBuffer();
        for (var offset = 0; offset < json.Length; offset += XRecordChunkLength)
        {
            var length = Math.Min(XRecordChunkLength, json.Length - offset);
            values.Add(new TypedValue(
                XRecordStringCode,
                json.Substring(offset, length)));
        }

        record.Data = values;
        if (namedObjects.Contains(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey))
        {
            var existingId = namedObjects.GetAt(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey);
            var existing = (DBObject)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            existing.Erase();
        }

        namedObjects.SetAt(
            AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadCombinedPlainItemLeaderProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey),
            OpenMode.ForRead);
        if (record.Data is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (TypedValue value in record.Data)
        {
            if (value.TypeCode == XRecordStringCode)
            {
                builder.Append(Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture));
            }
        }

        return JsonSerializer.Deserialize<AutoCadCombinedPlainItemLeaderProofManifest>(
            builder.ToString(),
            JsonOptions);
    }

    private static void DeleteManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForWrite);
        if (!namedObjects.Contains(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey))
        {
            return;
        }

        var existing = (DBObject)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadCombinedPlainItemLeaderProofPolicy.ManifestDictionaryKey),
            OpenMode.ForWrite);
        existing.Erase();
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) <= Tolerance;
}
#endif
