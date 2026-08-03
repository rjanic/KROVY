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

internal static class AutoCadDimensionsLeaderProofService
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
            ObjectId refreshedLeaderId;
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
                        $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: FAIL - exact " +
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
                        "\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: NOT TESTED - " +
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
                    new List<AutoCadDimensionsLeaderProofExpectedCase>();
                ObjectId? caseALeaderId = null;

                for (var index = 0;
                     index < AutoCadDimensionsLeaderProofPolicy.Cases.Count;
                     index++)
                {
                    var proofCase =
                        AutoCadDimensionsLeaderProofPolicy.Cases[index];
                    var styleName = ResolveStyleName(proofCase, styles);
                    var source = CreateSource(
                        database,
                        transaction,
                        modelSpace,
                        index);
                    WriteMarker(source, proofCase.Token);

                    var data = CreateData(proofCase, styleName);
                    var presentation = batch.ResolveForElement(data);
                    var itemCodeText = presentation.ItemCodeText;
                    var dimensionText = presentation.DimensionText;
                    if (!dimensionText.HasCompatibleStyle ||
                        !itemCodeText.HasCompatibleStyle)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: NOT TESTED - " +
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

                    if (proofCase.Kind ==
                        AutoCadDimensionsLeaderProofKind.StandalonePlainItemRegression)
                    {
                        ValidateStandalonePlainItem(
                            modelSpace,
                            transaction,
                            sourceHandle,
                            itemCodeText.ModelHeightMm,
                            proofCase.Token);
                        expected.Add(
                            AutoCadDimensionsLeaderProofPolicy.ToExpected(
                                proofCase,
                                itemCodeText.ResolvedTextStyleName ?? styleName,
                                itemCodeText.PaperHeightMm,
                                presentation.AnnotationScaleDenominator,
                                itemCodeText.ResolutionKind.ToString(),
                                itemCodeText.IsFallback));
                        editor.WriteMessage(
                            $"\n  {proofCase.Token}: standalone Plain item " +
                            $"height={itemCodeText.ModelHeightMm:R}; PASS");
                        continue;
                    }

                    var leader = FindDimensionsLeader(
                        modelSpace,
                        transaction,
                        sourceHandle);
                    if (leader is null)
                    {
                        throw new InvalidOperationException(
                            $"DimensionsLeader was not created for {proofCase.Token}.");
                    }

                    if (!TryReadLeaderPresentation(
                            leader,
                            out _,
                            out var leaderStyleId,
                            out var mTextStyleId,
                            out var textHeight))
                    {
                        throw new InvalidOperationException(
                            $"DimensionsLeader MText missing for {proofCase.Token}.");
                    }

                    if (leaderStyleId != mTextStyleId)
                    {
                        throw new InvalidOperationException(
                            $"Case {proofCase.Token}: MLeader.TextStyleId != MText.TextStyleId.");
                    }

                    if (!AreClose(textHeight, dimensionText.ModelHeightMm))
                    {
                        throw new InvalidOperationException(
                            $"Case {proofCase.Token}: model height {textHeight:R} " +
                            $"!= {dimensionText.ModelHeightMm:R}.");
                    }

                    if (proofCase.Token == "A")
                    {
                        caseALeaderId = leader.ObjectId;
                    }

                    if (proofCase.IsFailurePreservationCase)
                    {
                        failureOutcome = RunFailurePreservation(
                            database,
                            transaction,
                            batch,
                            source,
                            data,
                            leader.ObjectId,
                            textHeight,
                            leaderStyleId,
                            mTextStyleId,
                            editor);
                    }

                    expected.Add(
                        AutoCadDimensionsLeaderProofPolicy.ToExpected(
                            proofCase,
                            dimensionText.ResolvedTextStyleName ?? styleName,
                            dimensionText.PaperHeightMm,
                            presentation.AnnotationScaleDenominator,
                            dimensionText.ResolutionKind.ToString(),
                            dimensionText.IsFallback,
                            failureOutcome));

                    editor.WriteMessage(
                        $"\n  {proofCase.Token}: style=" +
                        $"{dimensionText.ResolvedTextStyleName}; " +
                        $"TextStyleId={dimensionText.ResolvedTextStyleId}; " +
                        $"paper={dimensionText.PaperHeightMm:R}; " +
                        $"denominator={presentation.AnnotationScaleDenominator}; " +
                        $"modelHeight={dimensionText.ModelHeightMm:R}; " +
                        $"mLeaderStyle={leaderStyleId.Handle}; " +
                        $"mTextStyle={mTextStyleId.Handle}; " +
                        $"kind={dimensionText.ResolutionKind}" +
                        (string.IsNullOrEmpty(failureOutcome)
                            ? string.Empty
                            : $"; failure={failureOutcome}"));
                }

                if (caseALeaderId is null)
                {
                    throw new InvalidOperationException(
                        "Refresh case A leader ObjectId was not captured.");
                }

                var refreshSource = FindMarkedSource(
                    modelSpace,
                    transaction,
                    AutoCadDimensionsLeaderProofPolicy.RefreshToken) ??
                    throw new InvalidOperationException(
                        "Refresh source A was not found.");
                var refreshData = CreateData(
                    AutoCadDimensionsLeaderProofPolicy.Cases[0],
                    ResolveStyleName(
                        AutoCadDimensionsLeaderProofPolicy.Cases[0],
                        styles));
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    refreshSource,
                    refreshData,
                    batch);
                var refreshed = FindDimensionsLeader(
                    modelSpace,
                    transaction,
                    refreshSource.Handle.ToString());
                if (refreshed is null || refreshed.ObjectId != caseALeaderId)
                {
                    throw new InvalidOperationException(
                        "DimensionsLeader refresh did not preserve the same MLeader ObjectId.");
                }

                if (!TryReadLeaderPresentation(
                        refreshed,
                        out _,
                        out var refreshLeaderStyle,
                        out var refreshMtextStyle,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Refresh leader MText missing.");
                }

                if (refreshLeaderStyle != refreshMtextStyle)
                {
                    throw new InvalidOperationException(
                        "Refresh case A: MLeader.TextStyleId != MText.TextStyleId.");
                }

                editor.WriteMessage(
                    $"\n  {AutoCadDimensionsLeaderProofPolicy.RefreshToken}: " +
                    $"in-place refresh ObjectId={refreshed.ObjectId.Handle}; " +
                    $"mLeaderStyle={refreshLeaderStyle.Handle}; " +
                    $"mTextStyle={refreshMtextStyle.Handle}; PASS");

                refreshedLeaderId = caseALeaderId.Value;

                var manifest = new AutoCadDimensionsLeaderProofManifest(
                    AutoCadDimensionsLeaderProofPolicy.SchemaVersion,
                    AutoCadDimensionsLeaderProofPolicy.SuiteIdentifier,
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
                requireRefreshObjectId: refreshedLeaderId);
            editor.WriteMessage(
                passed
                    ? "\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: PASS - production " +
                      "DimensionsLeader presentation verified after commit."
                    : "\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: FAIL - post-commit " +
                      "readback did not match the expected presentation.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_CREATE: FAIL - " +
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
                requireRefreshObjectId: null);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_VERIFY: PASS - read-only; " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                    : $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_VERIFY: FAIL - " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_VERIFY: FAIL - " +
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
                    $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_CLEAN: PASS - removed " +
                    $"{removed} proof-related ModelSpace entities.");
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_DIMENSIONS_LEADER_TEXT_CLEAN: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string RunFailurePreservation(
        Database database,
        Transaction transaction,
        AutoCadAnnotationPresentationBatchContext batch,
        Entity source,
        TimberElementData data,
        ObjectId leaderId,
        double expectedHeight,
        ObjectId expectedLeaderStyleId,
        ObjectId expectedMtextStyleId,
        Editor editor)
    {
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForRead);
        var sourceHandle = source.Handle.ToString();
        var noCompatible =
            batch.TextStyleCatalog.CompatibleStyles.Count == 0
                ? AutoCadDimensionsLeaderProofPolicy.FailureCasePreserved
                : AutoCadDimensionsLeaderProofPolicy.FailureCaseNotTested;

        AssertDimensionsLeaderUnchanged(
            modelSpace,
            transaction,
            sourceHandle,
            leaderId,
            expectedHeight,
            expectedLeaderStyleId,
            expectedMtextStyleId,
            checkpoint: "E1");
        editor.WriteMessage(
            $"\n    E1: ObjectId={leaderId.Handle}; before null-context TryPrepare");

        if (!AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(
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
                "Null presentation context must fail DimensionsLeader preparation.");
        }

        AssertDimensionsLeaderUnchanged(
            modelSpace,
            transaction,
            sourceHandle,
            leaderId,
            expectedHeight,
            expectedLeaderStyleId,
            expectedMtextStyleId,
            checkpoint: "E2");
        editor.WriteMessage(
            "\n    E2 failure-preservation PASS: ObjectId/presentation identical " +
            "from E1 to E2.");

        TimberAnnotationService.EnsureForElement(
            database,
            transaction,
            source,
            data,
            batch);

        var recovered = FindDimensionsLeader(
            modelSpace,
            transaction,
            sourceHandle);
        if (recovered is null ||
            !TryReadLeaderPresentation(
                recovered,
                out _,
                out var recoveredLeaderStyle,
                out var recoveredMtextStyle,
                out var recoveredHeight) ||
            recoveredLeaderStyle != recoveredMtextStyle ||
            !AreClose(recoveredHeight, expectedHeight))
        {
            throw new InvalidOperationException(
                "E3 recovery Ensure produced an invalid DimensionsLeader.");
        }

        editor.WriteMessage(
            $"\n    E3: recovery ObjectId={recovered.ObjectId.Handle}; " +
            $"sameAsBaseline={recovered.ObjectId == leaderId}; " +
            $"NoCompatibleStyle={noCompatible}");
        return noCompatible;
    }

    private static void AssertDimensionsLeaderUnchanged(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        ObjectId leaderId,
        double expectedHeight,
        ObjectId expectedLeaderStyleId,
        ObjectId expectedMtextStyleId,
        string checkpoint)
    {
        var leader = FindDimensionsLeader(
            modelSpace,
            transaction,
            sourceHandle);
        if (leader is null || leader.ObjectId != leaderId || leader.IsErased)
        {
            throw new InvalidOperationException(
                $"Failure preservation {checkpoint}: DimensionsLeader ObjectId changed.");
        }

        if (!TryReadLeaderPresentation(
                leader,
                out _,
                out var leaderStyleId,
                out var mTextStyleId,
                out var textHeight) ||
            leaderStyleId != expectedLeaderStyleId ||
            mTextStyleId != expectedMtextStyleId ||
            !AreClose(textHeight, expectedHeight))
        {
            throw new InvalidOperationException(
                $"Failure preservation {checkpoint}: DimensionsLeader presentation changed.");
        }
    }

    private static void ValidateStandalonePlainItem(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        double expectedHeight,
        string token)
    {
        var leader = FindStandalonePlainItemLeader(
            modelSpace,
            transaction,
            sourceHandle);
        if (leader is null ||
            !TryReadLeaderPresentation(
                leader,
                out _,
                out var leaderStyleId,
                out var mTextStyleId,
                out var textHeight) ||
            leaderStyleId != mTextStyleId ||
            !AreClose(textHeight, expectedHeight))
        {
            throw new InvalidOperationException(
                $"Standalone regression case {token} did not produce a valid " +
                "Plain ItemNumberLeader at the expected height.");
        }
    }

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor,
        ObjectId? requireRefreshObjectId)
    {
        var manifest = ReadManifest(database, transaction);
        if (manifest is null)
        {
            editor.WriteMessage(
                "\n  missing DimensionsLeader proof manifest");
            return false;
        }

        if (manifest.SchemaVersion !=
                AutoCadDimensionsLeaderProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadDimensionsLeaderProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage(
                "\n  unexpected DimensionsLeader proof manifest");
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
            bool casePassed;
            if (expected.Kind ==
                AutoCadDimensionsLeaderProofKind.StandalonePlainItemRegression)
            {
                var standalone = FindStandalonePlainItemLeader(
                    modelSpace,
                    transaction,
                    sourceHandle);
                casePassed = standalone is not null &&
                    TryReadLeaderPresentation(
                        standalone,
                        out _,
                        out var standaloneLeaderStyle,
                        out var standaloneMtextStyle,
                        out var standaloneHeight) &&
                    standaloneLeaderStyle == standaloneMtextStyle &&
                    AreClose(standaloneHeight, expected.ModelHeightMm);
                editor.WriteMessage(
                    $"\n  {expected.Token}: standalone height=" +
                    $"{(standalone is null ? 0d : standalone.MText?.TextHeight ?? 0d):R}; " +
                    $"expected={expected.ModelHeightMm:R}; " +
                    $"{(casePassed ? "PASS" : "FAIL")}");
            }
            else
            {
                var leader = FindDimensionsLeader(
                    modelSpace,
                    transaction,
                    sourceHandle);
                if (leader is null ||
                    !TryReadLeaderPresentation(
                        leader,
                        out _,
                        out var leaderStyleId,
                        out var mTextStyleId,
                        out var textHeight))
                {
                    editor.WriteMessage(
                        $"\n  {expected.Token}: FAIL - DimensionsLeader missing");
                    passed = false;
                    continue;
                }

                var styleName = ResolveStyleName(
                    database,
                    transaction,
                    leaderStyleId);
                var styleMatches = string.Equals(
                    styleName,
                    expected.StyleName,
                    StringComparison.OrdinalIgnoreCase);
                var heightMatches = AreClose(textHeight, expected.ModelHeightMm);
                var styleParity = leaderStyleId == mTextStyleId;
                casePassed = styleMatches && heightMatches && styleParity;
                if (expected.ExpectRefreshSameObjectId &&
                    requireRefreshObjectId is ObjectId refreshId)
                {
                    casePassed = casePassed && leader.ObjectId == refreshId;
                }

                if (expected.IsFailurePreservationCase)
                {
                    var failureOk =
                        string.Equals(
                            expected.FailureOutcome,
                            AutoCadDimensionsLeaderProofPolicy.FailureCaseNotTested,
                            StringComparison.Ordinal) ||
                        string.Equals(
                            expected.FailureOutcome,
                            AutoCadDimensionsLeaderProofPolicy.FailureCasePreserved,
                            StringComparison.Ordinal);
                    casePassed = casePassed && failureOk;
                }

                editor.WriteMessage(
                    $"\n  {expected.Token}: style={styleName}; " +
                    $"mLeaderStyle={leaderStyleId.Handle}; " +
                    $"mTextStyle={mTextStyleId.Handle}; " +
                    $"paper={expected.PaperHeightMm:R}; " +
                    $"denominator={expected.Denominator}; " +
                    $"modelHeight={textHeight:R}; " +
                    $"expectedHeight={expected.ModelHeightMm:R}; " +
                    $"kind={expected.ResolutionKind}" +
                    (expected.IsFailurePreservationCase
                        ? $"; failure={expected.FailureOutcome}"
                        : string.Empty) +
                    $"; {(casePassed ? "PASS" : "FAIL")}");
            }

            passed &= casePassed;
        }

        if (requireRefreshObjectId is ObjectId)
        {
            editor.WriteMessage(
                $"\n  {AutoCadDimensionsLeaderProofPolicy.RefreshToken}: " +
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
        var originX = index * 2500d;
        var start = new Point3d(originX, 0d, 0d);
        var end = new Point3d(originX + 1200d, 0d, 0d);
        var source = new Line(start, end);
        source.SetDatabaseDefaults(database);
        modelSpace.AppendEntity(source);
        transaction.AddNewlyCreatedDBObject(source, true);
        return source;
    }

    private static TimberElementData CreateData(
        AutoCadDimensionsLeaderProofCase proofCase,
        string styleName)
    {
        TimberAnnotationTextSettings? settings = proofCase.TextSettings;
        if (settings is not null)
        {
            settings = settings with { TextStyleName = styleName };
        }

        var annotationMode = proofCase.Kind switch
        {
            AutoCadDimensionsLeaderProofKind.StandalonePlainItemRegression =>
                TimberAnnotationMode.ItemNumberLeader,
            _ => TimberAnnotationMode.DimensionsLeader,
        };

        return new TimberElementData
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = $"DL-{proofCase.Token}",
            ElementType = TimberElementType.Rafter,
            WidthMm = 80d,
            HeightMm = 160d,
            AnnotationMode = annotationMode,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            AnnotationTextSettings = settings,
            AnnotationScaleDenominatorOverride =
                proofCase.DenominatorOverride,
            SlopeDegrees = 35d,
            RoofPlaneId = "AK_DEV",
        };
    }

    private static string ResolveStyleName(
        AutoCadDimensionsLeaderProofCase proofCase,
        IReadOnlyList<AutoCadTextStyleCatalogEntry> styles)
    {
        if (proofCase.StyleSlot < 0)
        {
            return styles[0].CanonicalName;
        }

        var slot = Math.Min(proofCase.StyleSlot, styles.Count - 1);
        return styles[slot].CanonicalName;
    }

    private static MLeader? FindDimensionsLeader(
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
                TimberAnnotationMode.DimensionsLeader)
            {
                continue;
            }

            return leader;
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
                AutoCadDimensionsLeaderProofPolicy.RegAppName),
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
                    AutoCadDimensionsLeaderProofPolicy.RegAppName,
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
        if (regAppTable.Has(AutoCadDimensionsLeaderProofPolicy.RegAppName))
        {
            return;
        }

        regAppTable.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadDimensionsLeaderProofPolicy.RegAppName,
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
        AutoCadDimensionsLeaderProofManifest manifest)
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
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey))
        {
            var existingId = namedObjects.GetAt(
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey);
            var existing = (DBObject)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            existing.Erase();
        }

        namedObjects.SetAt(
            AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadDimensionsLeaderProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey),
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

        return JsonSerializer.Deserialize<AutoCadDimensionsLeaderProofManifest>(
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
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey))
        {
            return;
        }

        var existing = (DBObject)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadDimensionsLeaderProofPolicy.ManifestDictionaryKey),
            OpenMode.ForWrite);
        existing.Erase();
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) <= Tolerance;
}
#endif
