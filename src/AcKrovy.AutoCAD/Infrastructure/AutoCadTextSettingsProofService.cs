#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadTextSettingsProofService
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
    private static readonly JsonSerializerOptions LibraryJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
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
        string? librarySnapshotJson = null;
        var libraryMutated = false;
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
                        $"\nAK_DEV_TEXT_SETTINGS_CREATE: FAIL - exact " +
                        $"ModelSpace contains {existing.Length} real entities. " +
                        "Layouts, paper-space objects, AEC dictionaries and " +
                        "nested block definitions were not counted.");
                    return;
                }

                EnsureRegApp(database, transaction);
                var standardBefore = SnapshotStandard(database, transaction);

                var classic = AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    TimberAnnotationBuiltInTextStylePreset.Classic);
                var arch = AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    TimberAnnotationBuiltInTextStylePreset.Architectural);
                var technical = AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    TimberAnnotationBuiltInTextStylePreset.Technical);
                var arial = AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    TimberAnnotationBuiltInTextStylePreset.Arial);
                if (classic.TextStyleId is null ||
                    arch.TextStyleId is null ||
                    technical.TextStyleId is null ||
                    arial.TextStyleId is null ||
                    new[] { classic, arch, technical, arial }.Any(result =>
                        result.Kind == AutoCadTextStylePresetEnsureKind.Failed))
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_SETTINGS_CREATE: NOT TESTED - " +
                        "unable to ensure all four app-owned built-ins. " +
                        $"Classic={classic.Kind}; Arch={arch.Kind}; " +
                        $"Technical={technical.Kind}; Arial={arial.Kind}; " +
                        $"{classic.DiagnosticReason ?? arch.DiagnosticReason ??
                            technical.DiagnosticReason ?? arial.DiagnosticReason}");
                    return;
                }
                if (!WriteArchitecturalProofBlock(
                        database,
                        transaction,
                        arch,
                        editor))
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_SETTINGS_CREATE: FAIL - " +
                        "Architectural style is neither available nor a valid " +
                        "Arial fallback.");
                    return;
                }
                if (!VerifyTechnicalProfile(
                        database,
                        transaction,
                        editor))
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_SETTINGS_CREATE: FAIL - Technical " +
                        "profile does not match the audited isocp contract.");
                    return;
                }

                var libraryBefore =
                    TimberAnnotationTextStylePresetLibraryStore.Load().Normalize();
                librarySnapshotJson = JsonSerializer.Serialize(
                    libraryBefore,
                    LibraryJsonOptions);
                var userFont =
                    AutoCadTextSettingsProofPolicy.ResolvePreferredUserFont(
                        AutoCadFontDiscoveryService.IsFontAvailable);
                var userPreset =
                    AutoCadTextSettingsProofPolicy.CreateUserPreset(
                        userFont,
                        libraryBefore.Presets);
                libraryMutated = EnsureProofUserPresetInLibrary(
                    libraryBefore,
                    userPreset,
                    editor);
                var userEnsure = AutoCadTextStylePresetService.EnsureUserPreset(
                    database,
                    transaction,
                    userPreset);
                if (userEnsure.TextStyleId is null)
                {
                    if (libraryMutated)
                    {
                        RestoreUserPresetLibrary(librarySnapshotJson);
                        libraryMutated = false;
                    }

                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_SETTINGS_CREATE: NOT TESTED - " +
                        "unable to ensure user preset style. " +
                        $"Kind={userEnsure.Kind}; {userEnsure.DiagnosticReason}");
                    return;
                }

                var proofCreatedUserTextStyle =
                    userEnsure.Kind == AutoCadTextStylePresetEnsureKind.Created;
                editor.WriteMessage(
                    $"\n  USER preset: stableId=" +
                    $"{AutoCadTextSettingsProofPolicy.UserPresetStableId}; " +
                    $"display='{userPreset.DisplayName}'; " +
                    $"style={userPreset.AutoCadTextStyleName}; " +
                    $"font={userFont}; " +
                    $"libraryMutation={(libraryMutated ? "added" : "reused")}; " +
                    $"textStyleKind={userEnsure.Kind}");

                var defaultProfile = TimberElementDefaultProfile.CreateDefault();
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    defaultProfile);
                if (batch.TextStyleCatalog.CompatibleStyles.Count == 0)
                {
                    if (libraryMutated)
                    {
                        RestoreUserPresetLibrary(librarySnapshotJson);
                        libraryMutated = false;
                    }

                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_SETTINGS_CREATE: NOT TESTED - " +
                        "Architecture DWG has no compatible variable-height " +
                        "nonannotative text style. No proof entity was created.");
                    return;
                }

                modelSpace.UpgradeOpen();
                var expected = new List<AutoCadTextSettingsProofExpectedCase>();

                for (var index = 0;
                     index < AutoCadTextSettingsProofPolicy.Cases.Count;
                     index++)
                {
                    var proofCase = AutoCadTextSettingsProofPolicy.Cases[index];
                    var settings =
                        AutoCadTextSettingsProofPolicy.ResolveTextSettings(
                            proofCase,
                            userPreset.AutoCadTextStyleName);
                    var source = CreateSource(
                        database,
                        transaction,
                        modelSpace,
                        index);
                    WriteMarker(source, proofCase.Token);

                    var data = CreateData(proofCase, settings);
                    ElementDataStore.Write(source, transaction, data);

                    var presentation = batch.ResolveForElement(data);
                    if (!presentation.ItemCodeText.HasCompatibleStyle ||
                        !presentation.DimensionText.HasCompatibleStyle ||
                        !presentation.SlopeText.HasCompatibleStyle ||
                        !presentation.FramedItemCodeText.HasCompatibleStyle)
                    {
                        if (libraryMutated)
                        {
                            RestoreUserPresetLibrary(librarySnapshotJson);
                            libraryMutated = false;
                        }

                        editor.WriteMessage(
                            $"\nAK_DEV_TEXT_SETTINGS_CREATE: NOT TESTED - " +
                            $"case {proofCase.Token} has NoCompatibleStyle.");
                        return;
                    }

                    TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        source,
                        data,
                        batch);

                    var roles = InspectCase(
                        database,
                        transaction,
                        modelSpace,
                        proofCase,
                        source,
                        data,
                        presentation,
                        editor);

                    if (proofCase.IsRoleIsolation)
                    {
                        roles = RunRoleIsolation(
                            database,
                            transaction,
                            modelSpace,
                            batch,
                            proofCase,
                            source,
                            data,
                            presentation,
                            roles,
                            editor);
                    }

                    expected.Add(
                        AutoCadTextSettingsProofPolicy.ToExpected(
                            proofCase,
                            roles));
                }

                AssertNoDuplicateBuiltIns(database, transaction);
                var sharedUserHandle = AssertSharedUserFramedDefinition(
                    database,
                    transaction,
                    modelSpace,
                    editor);

                var manifest = new AutoCadTextSettingsProofManifest(
                    AutoCadTextSettingsProofPolicy.SchemaVersion,
                    AutoCadTextSettingsProofPolicy.SuiteIdentifier,
                    userPreset.AutoCadTextStyleName,
                    userFont,
                    standardBefore,
                    expected,
                    librarySnapshotJson,
                    libraryMutated,
                    proofCreatedUserTextStyle,
                    sharedUserHandle,
                    AutoCadTextSettingsProofPolicy.UserPresetStableId);
                WriteManifest(database, transaction, manifest);
                transaction.Commit();
                // Library mutation is intentional until CLEAN restores snapshot.
                libraryMutated = false;
            }

            using var readTransaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyCore(
                database,
                readTransaction,
                editor);
            editor.WriteMessage(
                passed
                    ? "\nAK_DEV_TEXT_SETTINGS_CREATE: PASS - production " +
                      "Text Settings presentation verified after commit " +
                      "(includes IUFR USER_* framed shared-definition proof)."
                    : "\nAK_DEV_TEXT_SETTINGS_CREATE: FAIL - post-commit " +
                      "readback did not match the expected presentation.");
        }
        catch (System.Exception exception)
        {
            if (libraryMutated && librarySnapshotJson is not null)
            {
                try
                {
                    RestoreUserPresetLibrary(librarySnapshotJson);
                }
                catch (System.Exception restoreException)
                {
                    editor.WriteMessage(
                        $"\n  USER library restore after CREATE failure also " +
                        $"failed: {restoreException.Message}");
                }
            }

            editor.WriteMessage(
                $"\nAK_DEV_TEXT_SETTINGS_CREATE: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}. " +
                "Partial entities were not committed; the single CREATE " +
                "transaction was rolled back. No completed proof manifest " +
                "was written.");
        }
    }

    public static void FreshDrawingCreate()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        var dbmodBefore = Convert.ToInt32(
            AcApplication.GetSystemVariable("DBMOD"));
        try
        {
            var hasFreshManifest = false;
            using (var read =
                   database.TransactionManager.StartOpenCloseTransaction())
            {
                if (ReadFreshDrawingManifest(database, read) is not null)
                {
                    hasFreshManifest = true;
                }
                else
                {
                    var modelSpace = OpenModelSpace(
                        database,
                        read,
                        OpenMode.ForRead);
                    var existingCount = modelSpace.Cast<ObjectId>().Count(id =>
                        read.GetObject(id, OpenMode.ForRead, false)
                            is Entity entity &&
                        !entity.IsErased &&
                        entity.OwnerId == modelSpace.ObjectId);
                    if (existingCount != 0)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: FAIL - exact " +
                            $"ModelSpace contains {existingCount} real entities; " +
                            "run only in a fresh drawing.");
                        return;
                    }
                }
            }

            if (hasFreshManifest)
            {
                RunFreshDrawingIdempotenceEnsure(
                    document,
                    database,
                    editor,
                    dbmodBefore);
                return;
            }

            using (document.LockDocument())
            using (var transaction =
                   database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(database, transaction);
                var standardBefore = SnapshotStandard(database, transaction);
                var ensures = Enum
                    .GetValues<TimberAnnotationBuiltInTextStylePreset>()
                    .Select(preset => AutoCadTextStylePresetService.EnsureBuiltIn(
                        database,
                        transaction,
                        preset))
                    .ToArray();
                if (ensures.Any(result =>
                        result.TextStyleId is null ||
                        result.Kind == AutoCadTextStylePresetEnsureKind.Failed))
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: FAIL - " +
                        "one or more app-owned styles are unresolved.");
                    foreach (var result in ensures)
                    {
                        editor.WriteMessage(
                            $"\n  {result.StyleName}: {result.Kind}; " +
                            $"{result.DiagnosticReason ?? "no diagnostic"}");
                    }
                    return;
                }

                var architectural = ensures.Single(result =>
                    string.Equals(
                        result.StyleName,
                        TimberAnnotationTextStylePresetRules
                            .ArchitecturalStyleName,
                        StringComparison.OrdinalIgnoreCase));
                if (!WriteArchitecturalProofBlock(
                        database,
                        transaction,
                        architectural,
                        editor))
                {
                    throw new InvalidOperationException(
                        "Architectural style fallback state is unresolved.");
                }
                if (!VerifyTechnicalProfile(
                        database,
                        transaction,
                        editor))
                {
                    throw new InvalidOperationException(
                        "Technical profile does not match isocp.shx.");
                }

                var defaultProfile = TimberElementDefaultProfile.CreateDefault();
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    defaultProfile);
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForWrite);
                var expected =
                    new List<AutoCadTextSettingsProofExpectedCase>();
                for (var index = 0;
                     index <
                     AutoCadTextSettingsProofPolicy.FreshDrawingCases.Count;
                     index++)
                {
                    var proofCase =
                        AutoCadTextSettingsProofPolicy.FreshDrawingCases[index];
                    var source = CreateSource(
                        database,
                        transaction,
                        modelSpace,
                        index);
                    WriteMarker(source, proofCase.Token);
                    var data = CreateData(proofCase, proofCase.TextSettings);
                    ElementDataStore.Write(source, transaction, data);
                    var presentation = batch.ResolveForElement(data);
                    if (!presentation.ItemCodeText.HasCompatibleStyle ||
                        !presentation.DimensionText.HasCompatibleStyle ||
                        !presentation.SlopeText.HasCompatibleStyle ||
                        !presentation.FramedItemCodeText.HasCompatibleStyle)
                    {
                        throw new InvalidOperationException(
                            $"Fresh case {proofCase.Token} has no compatible style.");
                    }

                    TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        source,
                        data,
                        batch);
                    var roles = InspectCase(
                        database,
                        transaction,
                        modelSpace,
                        proofCase,
                        source,
                        data,
                        presentation,
                        editor);
                    foreach (var role in roles)
                    {
                        WriteFreshIdentityProofLine(
                            database,
                            transaction,
                            proofCase,
                            role,
                            architectural,
                            editor);
                    }

                    expected.Add(
                        AutoCadTextSettingsProofPolicy.ToExpected(
                            proofCase,
                            roles));
                }

                WriteFreshDrawingManifest(
                    database,
                    transaction,
                    new AutoCadFreshDrawingProofManifest(
                        AutoCadTextSettingsProofPolicy
                            .FreshDrawingSchemaVersion,
                        AutoCadTextSettingsProofPolicy
                            .FreshDrawingSuiteIdentifier,
                        standardBefore,
                        expected,
                        TimberAnnotationTextStylePresetRules
                            .ArchitecturalStyleName,
                        TimberAnnotationTextStylePresetRules
                            .ArchitecturalFontFile));
                transaction.Commit();
            }

            using var verify =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyFreshDrawingCore(database, verify, editor);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: PASS - mutating " +
                      "CREATE completed; DBMOD change is expected; " +
                      $"before={dbmodBefore}, after={dbmodAfter}. Run VERIFY."
                    : $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: FAIL - post-commit " +
                      $"readback failed; DBMOD before={dbmodBefore}, " +
                      $"after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void FreshDrawingVerify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        var dbmodBefore = Convert.ToInt32(
            AcApplication.GetSystemVariable("DBMOD"));
        try
        {
            using var transaction =
                database.TransactionManager.StartOpenCloseTransaction();
            var passed = VerifyFreshDrawingCore(
                database,
                transaction,
                editor);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            passed &= dbmodBefore == dbmodAfter;
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_TEXT_FRESH_DRAWING_VERIFY: PASS - read-only; " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                    : $"\nAK_DEV_TEXT_FRESH_DRAWING_VERIFY: FAIL - " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_FRESH_DRAWING_VERIFY: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}; " +
                $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
    }

    public static void MigrateCreate()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        const string token = "G3_MIGRATE";
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using (document.LockDocument())
            using (var transaction =
                database.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForWrite);
                if (modelSpace.Cast<ObjectId>().Any(id =>
                        transaction.GetObject(id, OpenMode.ForRead, false) is
                            Entity entity &&
                        !entity.IsErased &&
                        entity.OwnerId == modelSpace.ObjectId))
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_G3_MIGRATE_CREATE: FAIL - ModelSpace must be empty.");
                    return;
                }

                EnsureRegApp(database, transaction);
                var classic = AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    TimberAnnotationBuiltInTextStylePreset.Classic);
                if (classic.TextStyleId is not ObjectId classicStyleId)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_TEXT_G3_MIGRATE_CREATE: NOT TESTED - Classic style unavailable.");
                    return;
                }

                var proofCase = AutoCadTextSettingsProofPolicy.Cases.First(
                    candidate =>
                        candidate.Kind ==
                        AutoCadTextSettingsProofKind.ItemRectangle);
                var settings = TimberAnnotationTextSettings.Shared(
                    TimberAnnotationTextStylePresetRules.ClassicStyleName,
                    TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                    TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                    TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);
                var source = CreateSource(
                    database,
                    transaction,
                    modelSpace,
                    0);
                WriteMarker(source, token);
                var data = CreateData(proofCase, settings);
                ElementDataStore.Write(source, transaction, data);
                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    TimberElementDefaultProfile.CreateDefault());
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    source,
                    data,
                    batch);

                var leader = FindFramedItemLeader(
                    modelSpace,
                    transaction,
                    source.Handle.ToString(),
                    TimberAnnotationMode.ItemNumberLeader,
                    proofCase.ItemStyle) ??
                    throw new InvalidOperationException(
                        "Production framed leader was not created.");
                var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
                    proofCase.ItemStyle,
                    data.ElementId);
                var g3Key = AutoCadItemLeaderBlockVariantKey.FromDefinition(
                    definition,
                    AutoCadItemLeaderTextStyleIdentity.Classic);
                var g2Name = AutoCadItemLeaderBlockVariantNamePolicy
                    .CreateCanonicalName(g3Key)
                    .Replace("_G3_CLASSIC", "_G2", StringComparison.Ordinal);
                var blockTable = (BlockTable)transaction.GetObject(
                    database.BlockTableId,
                    OpenMode.ForWrite);
                var g2Block = new BlockTableRecord
                {
                    Name = g2Name,
                    Origin = Point3d.Origin,
                    Annotative = AnnotativeStates.False,
                    BlockScaling = BlockScaling.Uniform,
                };
                var g2BlockId = blockTable.Add(g2Block);
                transaction.AddNewlyCreatedDBObject(g2Block, true);
                AcKrovyItemLeaderBlockService.AddFrameGeometry(
                    database,
                    transaction,
                    g2Block,
                    definition);
                var g2AttributeId =
                    AcKrovyItemLeaderBlockService.AddItemNumberAttribute(
                        database,
                        transaction,
                        g2Block,
                        definition.TextHeightMm,
                        classicStyleId);

                leader.UpgradeOpen();
                leader.BlockContentId = g2BlockId;
                var g2AttributeDefinition =
                    (AttributeDefinition)transaction.GetObject(
                        g2AttributeId,
                        OpenMode.ForRead);
                using var attribute = new AttributeReference();
                attribute.SetAttributeFromBlock(
                    g2AttributeDefinition,
                    Matrix3d.Identity);
                attribute.TextString = data.ElementId;
                attribute.Height = definition.TextHeightMm;
                leader.SetBlockAttribute(g2AttributeId, attribute);
                transaction.Commit();
                editor.WriteMessage(
                    $"\nAK_DEV_TEXT_G3_MIGRATE_CREATE: PASS - legacy {g2Name} leader created. Save, reopen, run AK_DEV_TEXT_G3_MIGRATE_VERIFY.");
            }
        }
        catch (Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_G3_MIGRATE_CREATE: FAIL - {exception.Message}");
        }
    }

    public static void MigrateVerify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        const string token = "G3_MIGRATE";
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using (document.LockDocument())
            using (var transaction =
                database.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForRead);
                var source = FindMarkedSource(modelSpace, transaction, token) ??
                    throw new InvalidOperationException(
                        "Migration source marker was not found.");
                if (!ElementDataStore.TryRead(source, transaction, out var data) ||
                    data is null)
                {
                    throw new InvalidOperationException(
                        "Migration source metadata was not readable.");
                }

                var leader = FindFramedItemLeader(
                    modelSpace,
                    transaction,
                    source.Handle.ToString(),
                    TimberAnnotationMode.ItemNumberLeader,
                    data.ItemNumberLeaderStyle) ??
                    throw new InvalidOperationException(
                        "Migration leader was not found.");
                var g2Id = leader.BlockContentId;
                var g2Name = ((BlockTableRecord)transaction.GetObject(
                    g2Id,
                    OpenMode.ForRead)).Name;
                if (!g2Name.EndsWith("_G2", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Expected G2 source definition, found {g2Name}.");
                }

                var batch = AutoCadAnnotationPresentationBatchContext.Create(
                    database,
                    transaction,
                    TimberElementDefaultProfile.CreateDefault());
                TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    source,
                    data,
                    batch);
                var g3Name = ((BlockTableRecord)transaction.GetObject(
                    leader.BlockContentId,
                    OpenMode.ForRead)).Name;
                if (leader.BlockContentId == g2Id ||
                    !g3Name.Contains("_G3_CLASSIC", StringComparison.Ordinal) ||
                    transaction.GetObject(g2Id, OpenMode.ForRead, false) is not
                        BlockTableRecord)
                {
                    throw new InvalidOperationException(
                        "G2-to-G3 migration contract was not satisfied.");
                }

                transaction.Commit();
                editor.WriteMessage(
                    $"\nAK_DEV_TEXT_G3_MIGRATE_VERIFY: PASS - {g2Name} -> {g3Name}; old G2 definition preserved.");
            }
        }
        catch (Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_G3_MIGRATE_VERIFY: FAIL - {exception.Message}");
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
            var passed = VerifyCore(database, transaction, editor);
            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"));
            editor.WriteMessage(
                passed
                    ? $"\nAK_DEV_TEXT_SETTINGS_VERIFY: PASS - read-only; " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                    : $"\nAK_DEV_TEXT_SETTINGS_VERIFY: FAIL - " +
                      $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_SETTINGS_VERIFY: FAIL - " +
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
                var manifest = ReadManifest(database, transaction);

                var proofHandles = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
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

                    proofHandles.Add(entity.Handle.ToString());
                }

                var removed = 0;
                foreach (var handle in proofHandles)
                {
                    ElementLabelService.DeleteForSourceHandle(
                        database,
                        transaction,
                        handle);
                    SlopeAnnotationService.DeleteForSourceHandle(
                        database,
                        transaction,
                        handle);
                    PostFootprintPerpendicularAnnotationService
                        .DeleteForSourceHandle(
                            database,
                            transaction,
                            handle);
                }

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

                    if (ElementLabelStore.TryRead(entity, out var label) &&
                        label is not null &&
                        proofHandles.Contains(label.SourceHandle))
                    {
                        entity.UpgradeOpen();
                        entity.Erase();
                        removed++;
                        continue;
                    }

                    if (SlopeArrowStore.TryRead(entity, out var arrow) &&
                        arrow is not null &&
                        proofHandles.Contains(arrow.SourceHandle))
                    {
                        entity.UpgradeOpen();
                        entity.Erase();
                        removed++;
                        continue;
                    }

                    if (SlopeAngleTextStore.TryRead(entity, out var angle) &&
                        angle is not null &&
                        proofHandles.Contains(angle.SourceHandle))
                    {
                        entity.UpgradeOpen();
                        entity.Erase();
                        removed++;
                    }
                }

                var styleCleanup = TryCleanupProofOwnedUserTextStyle(
                    database,
                    transaction,
                    manifest,
                    editor);

                DeleteManifest(database, transaction);
                transaction.Commit();

                var libraryCleanup = "library untouched";
                if (manifest is not null &&
                    manifest.ProofOwnedUserPresetLibraryMutation &&
                    !string.IsNullOrWhiteSpace(
                        manifest.UserPresetLibrarySnapshotJson))
                {
                    RestoreUserPresetLibrary(
                        manifest.UserPresetLibrarySnapshotJson!);
                    libraryCleanup =
                        "proof-owned USER preset removed; library restored " +
                        "from CREATE snapshot";
                }

                editor.WriteMessage(
                    $"\nAK_DEV_TEXT_SETTINGS_CLEAN: PASS - removed " +
                    $"{removed} proof-related ModelSpace entities; " +
                    $"{libraryCleanup}; {styleCleanup}");
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_TEXT_SETTINGS_CLEAN: FAIL - " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void RunFreshDrawingIdempotenceEnsure(
        Autodesk.AutoCAD.ApplicationServices.Document document,
        Database database,
        Editor editor,
        int dbmodBefore)
    {
        AutoCadTextStylePresetEnsureResult[] results;
        using (document.LockDocument())
        using (var transaction =
               database.TransactionManager.StartTransaction())
        {
            results = Enum
                .GetValues<TimberAnnotationBuiltInTextStylePreset>()
                .Select(preset => AutoCadTextStylePresetService.EnsureBuiltIn(
                    database,
                    transaction,
                    preset))
                .ToArray();
            transaction.Commit();
        }

        bool verified;
        using (var read =
               database.TransactionManager.StartOpenCloseTransaction())
        {
            verified = VerifyFreshDrawingCore(database, read, editor);
        }

        var kindsAreNoOp = results.All(result =>
            result.Kind is AutoCadTextStylePresetEnsureKind.AlreadyMatched or
                AutoCadTextStylePresetEnsureKind.FontUnavailable);
        var dbmodAfter = Convert.ToInt32(
            AcApplication.GetSystemVariable("DBMOD"));
        var passed = verified && kindsAreNoOp && dbmodBefore == dbmodAfter;
        editor.WriteMessage(
            passed
                ? $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: PASS - idempotent " +
                  "second ensure made no changes; " +
                  $"DBMOD before={dbmodBefore}, after={dbmodAfter}."
                : $"\nAK_DEV_TEXT_FRESH_DRAWING_CREATE: FAIL - second " +
                  $"ensure kinds={string.Join(",", results.Select(result => result.Kind))}; " +
                  $"DBMOD before={dbmodBefore}, after={dbmodAfter}.");
    }

    private static bool VerifyFreshDrawingCore(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var manifest = ReadFreshDrawingManifest(database, transaction);
        if (manifest is null)
        {
            editor.WriteMessage(
                "\n  missing fresh-drawing CREATE manifest; run " +
                "AK_DEV_TEXT_FRESH_DRAWING_CREATE first");
            return false;
        }

        if (manifest.SchemaVersion !=
                AutoCadTextSettingsProofPolicy.FreshDrawingSchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadTextSettingsProofPolicy.FreshDrawingSuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage("\n  unexpected fresh-drawing proof manifest");
            return false;
        }

        var passed = VerifyStandardUntouched(
            database,
            transaction,
            manifest.StandardBefore,
            editor);
        passed &= VerifyBuiltInUniqueness(database, transaction, editor);

        var architectural = ReadArchitecturalEnsureState(
            database,
            transaction);
        passed &= WriteArchitecturalProofBlock(
            database,
            transaction,
            architectural,
            editor);
        passed &= VerifyTechnicalProfile(database, transaction, editor);
        passed &= string.Equals(
            manifest.ArchitecturalRequestedStyleName,
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            StringComparison.Ordinal) &&
            string.Equals(
                manifest.ArchitecturalRequestedFont,
                TimberAnnotationTextStylePresetRules.ArchitecturalFontFile,
                StringComparison.Ordinal);

        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        foreach (var expected in manifest.Cases)
        {
            var source = FindMarkedSource(
                modelSpace,
                transaction,
                expected.Token);
            if (source is null)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}: FAIL - source missing");
                passed = false;
                continue;
            }

            var proofCase =
                AutoCadTextSettingsProofPolicy.FindCase(expected.Token);
            foreach (var role in expected.Roles)
            {
                var rolePassed = VerifyRoleExpectation(
                    database,
                    transaction,
                    modelSpace,
                    source,
                    expected,
                    role,
                    editor);
                passed &= rolePassed;
                passed &= WriteFreshIdentityProofLine(
                    database,
                    transaction,
                    proofCase,
                    role,
                    architectural,
                    editor);
            }
        }

        return passed;
    }

    private static AutoCadTextStylePresetEnsureResult
        ReadArchitecturalEnsureState(
            Database database,
            Transaction transaction)
    {
        var requestedStyle =
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName;
        var requestedFont =
            TimberAnnotationTextStylePresetRules.ArchitecturalFontFile;
        var table = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        if (!table.Has(requestedStyle))
        {
            return new AutoCadTextStylePresetEnsureResult(
                requestedStyle,
                requestedFont,
                AutoCadTextStylePresetEnsureKind.Failed,
                null,
                "App-owned Architectural style is missing.");
        }

        var id = table[requestedStyle];
        var resolvedFont = ResolveFontName(transaction, id);
        if (FontNamesEqual(resolvedFont, requestedFont))
        {
            return new AutoCadTextStylePresetEnsureResult(
                requestedStyle,
                requestedFont,
                AutoCadTextStylePresetEnsureKind.AlreadyMatched,
                id,
                null);
        }

        if (FontNamesEqual(
                resolvedFont,
                TimberAnnotationTextStylePresetRules.ArialFontFile))
        {
            return new AutoCadTextStylePresetEnsureResult(
                requestedStyle,
                requestedFont,
                AutoCadTextStylePresetEnsureKind.FontUnavailable,
                id,
                $"{requestedFont} is unavailable; the app-owned " +
                "Architectural style temporarily uses Arial.");
        }

        return new AutoCadTextStylePresetEnsureResult(
            requestedStyle,
            requestedFont,
            AutoCadTextStylePresetEnsureKind.Failed,
            id,
            $"Unexpected resolved font '{resolvedFont}'.");
    }

    private static bool WriteArchitecturalProofBlock(
        Database database,
        Transaction transaction,
        AutoCadTextStylePresetEnsureResult architectural,
        Editor editor)
    {
        var status = architectural.Kind switch
        {
            AutoCadTextStylePresetEnsureKind.FontUnavailable =>
                "FALLBACK_ACTIVE",
            AutoCadTextStylePresetEnsureKind.Failed => "UNRESOLVED_ERROR",
            _ => "AVAILABLE",
        };
        var resolvedFont = architectural.TextStyleId is ObjectId styleId
            ? ResolveFontName(transaction, styleId)
            : "<unresolved>";
        var textSize = double.NaN;
        var xScale = double.NaN;
        var oblique = double.NaN;
        var bigFont = "<unresolved>";
        if (architectural.TextStyleId is ObjectId architecturalId &&
            transaction.GetObject(
                architecturalId,
                OpenMode.ForRead,
                false) is TextStyleTableRecord architecturalStyle)
        {
            textSize = architecturalStyle.TextSize;
            xScale = architecturalStyle.XScale;
            oblique = architecturalStyle.ObliquingAngle;
            bigFont = architecturalStyle.BigFontFileName?.Trim() ??
                string.Empty;
        }
        var fallbackActive = status == "FALLBACK_ACTIVE" &&
            FontNamesEqual(
                resolvedFont,
                TimberAnnotationTextStylePresetRules.ArialFontFile);
        var available = status == "AVAILABLE" &&
            FontNamesEqual(
                resolvedFont,
                TimberAnnotationTextStylePresetRules.ArchitecturalFontFile);
        var identityPreserved = string.Equals(
            architectural.StyleName,
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            StringComparison.OrdinalIgnoreCase);
        var isStandard = string.Equals(
            architectural.StyleName,
            TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            StringComparison.OrdinalIgnoreCase);
        var profileMatches = AreClose(textSize, 0d) &&
            AreClose(
                xScale,
                TimberAnnotationTextStylePresetRules.DefaultWidthFactor) &&
            AreClose(
                oblique,
                TimberAnnotationTextStylePresetRules
                    .DefaultObliqueAngleDegrees) &&
            string.IsNullOrEmpty(bigFont);
        var passed = (available || fallbackActive) &&
            architectural.TextStyleId is not null &&
            identityPreserved &&
            profileMatches &&
            !isStandard;
        editor.WriteMessage(
            "\n  ARCHITECTURAL:" +
            $"\n    status={status}" +
            "\n    requestedPreset=Architectural" +
            "\n    stableIdentity=ARCHITECTURAL" +
            $"\n    requestedFont=" +
            $"{TimberAnnotationTextStylePresetRules.ArchitecturalFontFile}" +
            $"\n    resolvedTextStyle={architectural.StyleName}" +
            $"\n    resolvedFont={resolvedFont}" +
            $"\n    fallbackReason=" +
            $"{architectural.DiagnosticReason ?? "<none>"}" +
            $"\n    TextSize={textSize:R}" +
            $"\n    XScale={xScale:R}" +
            $"\n    Oblique={oblique:R}" +
            $"\n    BigFont={bigFont}" +
            "\n    settingRewrittenToFallback=false" +
            "\n    laterRehydrateToArialNarrow=true" +
            $"\n    isStandard={isStandard}" +
            $"\n    {(passed ? "PASS" : "FAIL")}");
        _ = database;
        return passed;
    }

    private static bool VerifyTechnicalProfile(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var table = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        var styleName =
            TimberAnnotationTextStylePresetRules.TechnicalStyleName;
        if (!table.Has(styleName) ||
            transaction.GetObject(
                table[styleName],
                OpenMode.ForRead,
                false) is not TextStyleTableRecord style)
        {
            editor.WriteMessage(
                "\n  TECHNICAL: status=UNRESOLVED_ERROR; FAIL - style missing");
            return false;
        }

        var resolvedFont = ResolveFontName(transaction, style.ObjectId);
        var bigFont = style.BigFontFileName?.Trim() ?? string.Empty;
        var passed = FontNamesEqual(
                resolvedFont,
                TimberAnnotationTextStylePresetRules.TechnicalFontFile) &&
            AreClose(
                style.XScale,
                TimberAnnotationTextStylePresetRules.DefaultWidthFactor) &&
            AreClose(
                style.ObliquingAngle,
                TimberAnnotationTextStylePresetRules
                    .DefaultObliqueAngleDegrees) &&
            AreClose(style.TextSize, 0d) &&
            string.IsNullOrEmpty(bigFont);
        editor.WriteMessage(
            $"\n  TECHNICAL: requestedPreset=Technical; " +
            "stableIdentity=TECHNICAL; " +
            $"requestedFont=" +
            $"{TimberAnnotationTextStylePresetRules.TechnicalFontFile}; " +
            $"resolvedTextStyle={style.Name}; resolvedFont={resolvedFont}; " +
            $"XScale={style.XScale:R}; Oblique={style.ObliquingAngle:R}; " +
            $"TextSize={style.TextSize:R}; BigFont={bigFont}; " +
            $"{(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool WriteFreshIdentityProofLine(
        Database database,
        Transaction transaction,
        AutoCadTextSettingsProofCase proofCase,
        AutoCadTextSettingsProofRoleExpectation role,
        AutoCadTextStylePresetEnsureResult architectural,
        Editor editor)
    {
        var identity = AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
            role.RequestedStyleName);
        var requestedFont =
            TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStyleName(
                role.RequestedStyleName,
                out var definition) &&
            definition is not null
                ? definition.FontFile
                : "<user>";
        var resolvedFont = ResolveFontName(
            database,
            transaction,
            role.ResolvedStyleName);
        var fallbackReason =
            identity.Kind ==
                AutoCadItemLeaderTextStyleIdentityKind.Architectural &&
            architectural.Kind ==
                AutoCadTextStylePresetEnsureKind.FontUnavailable
                ? architectural.DiagnosticReason ?? "Arial fallback active."
                : "<none>";
        var isStandard = string.Equals(
            role.ResolvedStyleName,
            TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            StringComparison.OrdinalIgnoreCase);
        var identityStable =
            role.RequestedStyleName.StartsWith(
                TimberAnnotationTextStylePresetRules.UserStyleNamePrefix,
                StringComparison.OrdinalIgnoreCase) ||
            TimberAnnotationTextStylePresetRules.IsBuiltInStyleName(
                role.RequestedStyleName);
        var passed = !isStandard &&
            identityStable &&
            !string.IsNullOrWhiteSpace(resolvedFont) &&
            !string.Equals(resolvedFont, "<missing>", StringComparison.Ordinal);
        var targets = role.EntityType.Contains(
                "AttributeReference",
                StringComparison.Ordinal)
            ? $"{role.Role}+framed G3 AttrDef"
            : role.Role;
        editor.WriteMessage(
            $"\n  FIRST-ELEMENT {proofCase.Token}/{targets}: " +
            $"requestedPreset={role.RequestedStyleName}; " +
            $"stableIdentity={identity.Kind.ToString().ToUpperInvariant()}; " +
            $"requestedFont={requestedFont}; " +
            $"resolvedTextStyle={role.ResolvedStyleName}; " +
            $"resolvedFont={resolvedFont}; " +
            $"fallbackReason={fallbackReason}; " +
            $"isStandard={isStandard}; {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static string ResolveFontName(
        Database database,
        Transaction transaction,
        string styleName)
    {
        var table = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        return table.Has(styleName)
            ? ResolveFontName(transaction, table[styleName])
            : "<missing>";
    }

    private static string ResolveFontName(
        Transaction transaction,
        ObjectId styleId)
    {
        if (transaction.GetObject(styleId, OpenMode.ForRead, false)
                is not TextStyleTableRecord style)
        {
            return "<missing>";
        }

        var fileName = style.FileName?.Trim() ?? string.Empty;
        if (string.Equals(
                Path.GetExtension(fileName),
                ".shx",
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(fileName);
        }

        try
        {
            var typeface = style.Font.TypeFace?.Trim();
            if (!string.IsNullOrWhiteSpace(typeface))
            {
                return typeface;
            }
        }
        catch
        {
            // Diagnostics fall back to FileName below.
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? "<unknown>"
            : Path.GetFileName(fileName);
    }

    private static bool FontNamesEqual(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.GetFileNameWithoutExtension(actual),
            Path.GetFileNameWithoutExtension(expected),
            StringComparison.OrdinalIgnoreCase);

    private static bool VerifyCore(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var manifest = ReadManifest(database, transaction);
        if (manifest is null)
        {
            editor.WriteMessage("\n  missing Text Settings proof manifest");
            return false;
        }

        if (manifest.SchemaVersion !=
                AutoCadTextSettingsProofPolicy.SchemaVersion ||
            !string.Equals(
                manifest.SuiteIdentifier,
                AutoCadTextSettingsProofPolicy.SuiteIdentifier,
                StringComparison.Ordinal))
        {
            editor.WriteMessage("\n  unexpected Text Settings proof manifest");
            return false;
        }

        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        var passed = true;

        if (!VerifyStandardUntouched(
                database,
                transaction,
                manifest.StandardBefore,
                editor))
        {
            passed = false;
        }

        if (!VerifyBuiltInUniqueness(database, transaction, editor))
        {
            passed = false;
        }

        foreach (var expected in manifest.Cases)
        {
            var source = FindMarkedSource(
                modelSpace,
                transaction,
                expected.Token);
            if (source is null)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}: FAIL - source missing");
                passed = false;
                continue;
            }

            foreach (var role in expected.Roles)
            {
                var casePassed = VerifyRoleExpectation(
                    database,
                    transaction,
                    modelSpace,
                    source,
                    expected,
                    role,
                    editor);
                passed &= casePassed;
            }
        }

        if (!VerifySharedUserFramedDefinition(
                database,
                transaction,
                modelSpace,
                manifest,
                editor))
        {
            passed = false;
        }

        return passed;
    }

    private static IReadOnlyList<AutoCadTextSettingsProofRoleExpectation>
        InspectCase(
            Database database,
            Transaction transaction,
            BlockTableRecord modelSpace,
            AutoCadTextSettingsProofCase proofCase,
            Entity source,
            TimberElementData data,
            AutoCadAnnotationPresentationContext presentation,
            Editor editor)
    {
        var handle = source.Handle.ToString();
        var roles = new List<AutoCadTextSettingsProofRoleExpectation>();

        switch (proofCase.Kind)
        {
            case AutoCadTextSettingsProofKind.ItemPlain:
            case AutoCadTextSettingsProofKind.ItemUserPreset:
            case AutoCadTextSettingsProofKind.SlopeNumeric:
                {
                    var item = presentation.FramedItemCodeText;
                    var leader = FindItemLeader(
                        modelSpace,
                        transaction,
                        handle,
                        TimberAnnotationMode.ItemNumberLeader,
                        ItemNumberLeaderStyle.Plain);
                    if (leader is null ||
                        !TryReadMTextPresentation(
                            leader,
                            out _,
                            out var styleId,
                            out var height))
                    {
                        throw new InvalidOperationException(
                            $"Plain item MLeader missing for {proofCase.Token}.");
                    }

                    roles.Add(BuildRole(
                        "ItemCode",
                        item,
                        "MLeader",
                        styleId,
                        height,
                        leader.ObjectId,
                        blockName: null,
                        frameSize: null,
                        blockScale: null));
                    WriteRoleLine(editor, proofCase.Token, roles[^1], true);

                    if (proofCase.Kind ==
                        AutoCadTextSettingsProofKind.SlopeNumeric)
                    {
                        roles.Add(InspectSlopeText(
                            database,
                            transaction,
                            modelSpace,
                            handle,
                            presentation.SlopeText,
                            editor,
                            proofCase.Token));
                    }

                    break;
                }

            case AutoCadTextSettingsProofKind.ItemCircle:
            case AutoCadTextSettingsProofKind.ItemRectangle:
            case AutoCadTextSettingsProofKind.ItemSlot:
                {
                    var item = presentation.FramedItemCodeText;
                    var leader = FindFramedItemLeader(
                        modelSpace,
                        transaction,
                        handle,
                        TimberAnnotationMode.ItemNumberLeader,
                        proofCase.ItemStyle);
                    roles.Add(InspectFramedItem(
                        database,
                        transaction,
                        leader,
                        item,
                        proofCase,
                        editor));
                    break;
                }

            case AutoCadTextSettingsProofKind.CombinedFramed:
            case AutoCadTextSettingsProofKind.RoleIsolation:
                {
                    var itemLeader = FindFramedItemLeader(
                        modelSpace,
                        transaction,
                        handle,
                        TimberAnnotationMode.DimensionsWithItemNumber,
                        proofCase.ItemStyle);
                    roles.Add(InspectFramedItem(
                        database,
                        transaction,
                        itemLeader,
                        presentation.FramedItemCodeText,
                        proofCase,
                        editor));

                    var dim = FindCombinedDimensionsMText(
                        modelSpace,
                        transaction,
                        handle);
                    if (dim is null)
                    {
                        throw new InvalidOperationException(
                            $"Combined dimensions MText missing for {proofCase.Token}.");
                    }

                    roles.Add(BuildRole(
                        "Dimension",
                        presentation.DimensionText,
                        "MText",
                        dim.TextStyleId,
                        dim.TextHeight,
                        dim.ObjectId,
                        blockName: null,
                        frameSize: null,
                        blockScale: null));
                    WriteRoleLine(editor, proofCase.Token, roles[^1], true);

                    roles.Add(InspectSlopeText(
                        database,
                        transaction,
                        modelSpace,
                        handle,
                        presentation.SlopeText,
                        editor,
                        proofCase.Token));
                    break;
                }

            case AutoCadTextSettingsProofKind.FullLabel:
                {
                    var label = FindFullLabel(modelSpace, transaction, handle);
                    if (label is null)
                    {
                        throw new InvalidOperationException(
                            $"FullLabel MText missing for {proofCase.Token}.");
                    }

                    roles.Add(BuildRole(
                        "Dimension",
                        presentation.DimensionText,
                        "MText",
                        label.TextStyleId,
                        label.TextHeight,
                        label.ObjectId,
                        blockName: null,
                        frameSize: null,
                        blockScale: null));
                    WriteRoleLine(editor, proofCase.Token, roles[^1], true);

                    roles.Add(InspectSlopeText(
                        database,
                        transaction,
                        modelSpace,
                        handle,
                        presentation.SlopeText,
                        editor,
                        proofCase.Token));
                    break;
                }

            case AutoCadTextSettingsProofKind.DimensionsLeader:
                {
                    var leader = FindDimensionsLeader(
                        modelSpace,
                        transaction,
                        handle);
                    if (leader is null ||
                        !TryReadMTextPresentation(
                            leader,
                            out _,
                            out var styleId,
                            out var height))
                    {
                        throw new InvalidOperationException(
                            $"DimensionsLeader missing for {proofCase.Token}.");
                    }

                    roles.Add(BuildRole(
                        "Dimension",
                        presentation.DimensionText,
                        "MLeader",
                        styleId,
                        height,
                        leader.ObjectId,
                        blockName: null,
                        frameSize: null,
                        blockScale: null));
                    WriteRoleLine(editor, proofCase.Token, roles[^1], true);

                    roles.Add(InspectSlopeText(
                        database,
                        transaction,
                        modelSpace,
                        handle,
                        presentation.SlopeText,
                        editor,
                        proofCase.Token));
                    break;
                }

            case AutoCadTextSettingsProofKind.HorizontalMarker:
                {
                    roles.Add(InspectSlopeBlock(
                        database,
                        transaction,
                        modelSpace,
                        handle,
                        AutoCadTextSettingsProofPolicy.HorizontalMarkerBlockName,
                        presentation.SlopeText,
                        editor,
                        proofCase.Token));
                    break;
                }

            case AutoCadTextSettingsProofKind.PostPerpendicular:
                {
                    roles.Add(InspectSlopeBlock(
                        database,
                        transaction,
                        modelSpace,
                        handle,
                        AutoCadTextSettingsProofPolicy
                            .PostPerpendicularMarkerBlockName,
                        presentation.SlopeText,
                        editor,
                        proofCase.Token));
                    break;
                }

            default:
                throw new InvalidOperationException(
                    $"Unsupported proof kind {proofCase.Kind}.");
        }

        _ = data;
        return roles;
    }

    private static IReadOnlyList<AutoCadTextSettingsProofRoleExpectation>
        RunRoleIsolation(
            Database database,
            Transaction transaction,
            BlockTableRecord modelSpace,
            AutoCadAnnotationPresentationBatchContext batch,
            AutoCadTextSettingsProofCase proofCase,
            Entity source,
            TimberElementData baselineData,
            AutoCadAnnotationPresentationContext baselinePresentation,
            IReadOnlyList<AutoCadTextSettingsProofRoleExpectation> baselineRoles,
            Editor editor)
    {
        var itemBefore = baselineRoles.Single(role => role.Role == "ItemCode");
        var handle = source.Handle.ToString();
        var itemLeaderBefore = FindFramedItemLeader(
            modelSpace,
            transaction,
            handle,
            TimberAnnotationMode.DimensionsWithItemNumber,
            proofCase.ItemStyle) ??
            throw new InvalidOperationException(
                "Role isolation: baseline framed ItemCode leader missing.");
        var itemObjectId = itemLeaderBefore.ObjectId;
        var itemBlockContentId = itemLeaderBefore.BlockContentId;
        if (itemBlockContentId.IsNull)
        {
            throw new InvalidOperationException(
                "Role isolation: baseline Item BlockContentId missing.");
        }

        var patchedSettings =
            AutoCadTextSettingsProofPolicy.CreateRoleIsolationPatchedSettings(
                baselineData.AnnotationTextSettings!);
        var patchedData = baselineData with
        {
            AnnotationTextSettings = patchedSettings,
        };
        ElementDataStore.Write(source, transaction, patchedData);
        TimberAnnotationService.EnsureForElement(
            database,
            transaction,
            source,
            patchedData,
            batch);

        var presentation = batch.ResolveForElement(patchedData);
        var itemLeader = FindFramedItemLeader(
            modelSpace,
            transaction,
            handle,
            TimberAnnotationMode.DimensionsWithItemNumber,
            proofCase.ItemStyle);
        if (itemLeader is null ||
            itemLeader.ObjectId != itemObjectId ||
            itemLeader.BlockContentId != itemBlockContentId)
        {
            throw new InvalidOperationException(
                "Role isolation: ItemCode ObjectId/BlockContentId changed after " +
                "Dimension/Slope patch.");
        }

        var itemRole = InspectFramedItem(
            database,
            transaction,
            itemLeader,
            presentation.FramedItemCodeText,
            proofCase,
            editor);
        if (!string.Equals(
                itemRole.ResolvedStyleName,
                itemBefore.ResolvedStyleName,
                StringComparison.OrdinalIgnoreCase) ||
            !AreClose(itemRole.PaperHeightMm, itemBefore.PaperHeightMm) ||
            !AreClose(itemRole.ModelHeightMm, itemBefore.ModelHeightMm) ||
            !string.Equals(
                itemRole.BlockName,
                itemBefore.BlockName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                itemRole.FrameSize,
                itemBefore.FrameSize,
                StringComparison.Ordinal) ||
            !AreClose(
                itemRole.BlockScale ?? double.NaN,
                itemBefore.BlockScale ?? double.NaN))
        {
            throw new InvalidOperationException(
                "Role isolation: ItemCode style/height/frame changed after " +
                "Dimension/Slope patch.");
        }

        if (!AreClose(
                itemRole.PaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm) ||
            !AreClose(
                itemRole.ModelHeightMm,
                AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                    TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                    proofCase.Denominator)))
        {
            throw new InvalidOperationException(
                "Role isolation: ItemCode left default 2.7/135 contract " +
                $"(paper={itemRole.PaperHeightMm:R}, modelH={itemRole.ModelHeightMm:R}).");
        }

        var expectedDimHeight =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                AutoCadTextSettingsProofPolicy.RoleIsolationPatchedDimensionHeightMm,
                proofCase.Denominator);
        var dim = FindCombinedDimensionsMText(modelSpace, transaction, handle) ??
            throw new InvalidOperationException(
                "Role isolation: Dimension MText missing after patch.");
        var dimStyle = ResolveStyleName(database, transaction, dim.TextStyleId);
        if (!string.Equals(
                dimStyle,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                StringComparison.OrdinalIgnoreCase) ||
            !AreClose(dim.TextHeight, expectedDimHeight))
        {
            throw new InvalidOperationException(
                "Role isolation: Dimension did not adopt patched Arch/" +
                $"{AutoCadTextSettingsProofPolicy.RoleIsolationPatchedDimensionHeightMm:R}.");
        }

        var expectedSlopeHeight =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                AutoCadTextSettingsProofPolicy.RoleIsolationPatchedSlopeHeightMm,
                proofCase.Denominator);
        var slope = FindSlopeAngleText(modelSpace, transaction, handle) ??
            throw new InvalidOperationException(
                "Role isolation: Slope DBText missing after patch.");
        var slopeStyle = ResolveStyleName(database, transaction, slope.TextStyleId);
        if (!string.Equals(
                slopeStyle,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                StringComparison.OrdinalIgnoreCase) ||
            !AreClose(slope.Height, expectedSlopeHeight))
        {
            throw new InvalidOperationException(
                "Role isolation: Slope did not adopt patched Arch/" +
                $"{AutoCadTextSettingsProofPolicy.RoleIsolationPatchedSlopeHeightMm:R}.");
        }

        var dimRole = BuildRole(
            "Dimension",
            presentation.DimensionText,
            "MText",
            dim.TextStyleId,
            dim.TextHeight,
            dim.ObjectId,
            blockName: null,
            frameSize: null,
            blockScale: null);
        WriteRoleLine(editor, proofCase.Token, dimRole, true);

        var slopeRole = BuildRole(
            "Slope",
            presentation.SlopeText,
            "DBText",
            slope.TextStyleId,
            slope.Height,
            slope.ObjectId,
            blockName: null,
            frameSize: null,
            blockScale: null);
        WriteRoleLine(editor, proofCase.Token, slopeRole, true);

        editor.WriteMessage(
            $"\n  {proofCase.Token}: role-isolation Dimension→Arch/" +
            $"{AutoCadTextSettingsProofPolicy.RoleIsolationPatchedDimensionHeightMm:R}, " +
            $"Slope→Arch/" +
            $"{AutoCadTextSettingsProofPolicy.RoleIsolationPatchedSlopeHeightMm:R}; " +
            "ItemCode Classic/2.7 attrH=135 BlockContentId unchanged; PASS");

        _ = baselinePresentation;
        return
        [
            itemRole with
            {
                ExpectUnchangedObjectId = true,
                ObjectIdHandle = itemObjectId.Handle.ToString(),
            },
            dimRole with { ExpectUnchangedObjectId = false },
            slopeRole with { ExpectUnchangedObjectId = false },
        ];
    }

    private static AutoCadTextSettingsProofRoleExpectation InspectFramedItem(
        Database database,
        Transaction transaction,
        MLeader? leader,
        AutoCadAnnotationTextRolePresentation item,
        AutoCadTextSettingsProofCase proofCase,
        Editor editor)
    {
        if (leader is null ||
            leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            throw new InvalidOperationException(
                $"Framed item leader missing for {proofCase.Token}.");
        }

        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        var attributeDefinition = block
            .Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<AttributeDefinition>()
            .Single(candidate => string.Equals(
                candidate.Tag,
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                StringComparison.OrdinalIgnoreCase));
        using var attribute = leader.GetBlockAttribute(attributeDefinition.ObjectId);
        var expectedDefinitionHeight =
            AutoCadTextSettingsProofPolicy.ExpectedFramedDefinitionHeightMm;
        var expectedAttributeHeight =
            AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                item.PaperHeightMm,
                proofCase.Denominator);
        var expectedScale =
            AutoCadTextSettingsProofPolicy.ExpectedBlockScale(
                proofCase.Denominator);
        var resolved = TimberItemLeaderBlockDefinitionRules.Resolve(
            proofCase.ItemStyle,
            "K1");
        var expectedBlockName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(
                AutoCadItemLeaderBlockVariantKey.FromDefinition(
                    resolved,
                    AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
                        item.RequestedTextStyleName)));

        if (!AreClose(attributeDefinition.Height, expectedDefinitionHeight))
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: AttributeDefinition.Height mismatch. " +
                $"defH={attributeDefinition.Height:R} expected={expectedDefinitionHeight:R}.");
        }

        if (!AreClose(attribute.Height, expectedAttributeHeight) ||
            !AreClose(leader.BlockScale.X, expectedScale))
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: framed attribute/scale mismatch. " +
                $"attrH={attribute.Height:R} expected={expectedAttributeHeight:R}; " +
                $"blockScale={leader.BlockScale.X:R} expected={expectedScale:R}.");
        }

        if (!string.Equals(
                block.Name,
                expectedBlockName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: unexpected framed BlockContentId name. " +
                $"actual={block.Name}; expected shared G" +
                $"{AutoCadItemLeaderBlockVariantKey.CurrentGeometryVersion} " +
                $"definition {expectedBlockName} (denom-independent).");
        }

        var resolvedStyleName = ResolveStyleName(
            database,
            transaction,
            attribute.TextStyleId);
        var expectedStyleName = item.ResolvedTextStyleName ?? string.Empty;
        var definitionStyleName = ResolveStyleName(
            database,
            transaction,
            attributeDefinition.TextStyleId);
        if (!string.Equals(
                resolvedStyleName,
                expectedStyleName,
                StringComparison.OrdinalIgnoreCase) ||
            (item.ResolvedTextStyleId is ObjectId expectedStyleId &&
             attribute.TextStyleId != expectedStyleId))
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: framed AttributeReference.TextStyleId " +
                $"mismatch. requested={item.RequestedTextStyleName}; " +
                $"expected={expectedStyleName}; resolved={resolvedStyleName}; " +
                $"TextStyleId={attribute.TextStyleId.Handle}; " +
                $"definitionStyle={definitionStyleName}.");
        }

        if (attributeDefinition.TextStyleId != attribute.TextStyleId ||
            item.ResolvedTextStyleId is ObjectId definitionStyleId &&
            attributeDefinition.TextStyleId != definitionStyleId)
        {
            throw new InvalidOperationException(
                $"Case {proofCase.Token}: G3 AttributeDefinition.TextStyleId " +
                "does not own the resolved style inherited by AttributeReference. " +
                $"definitionStyle={definitionStyleName}.");
        }

        var identity = AutoCadItemLeaderTextStyleIdentity.FromStoredStyleName(
            item.RequestedTextStyleName);
        if (proofCase.UsesUserPreset)
        {
            if (identity.Kind != AutoCadItemLeaderTextStyleIdentityKind.User ||
                !identity.CreateNameToken().StartsWith(
                    "USER_",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: TextStyleIdentity is not USER_*. " +
                    $"kind={identity.Kind}; token={identity.CreateNameToken()}; " +
                    $"requested={item.RequestedTextStyleName}.");
            }

            if (!block.Name.Contains(
                    $"_G{AutoCadItemLeaderBlockVariantKey.CurrentGeometryVersion}_",
                    StringComparison.OrdinalIgnoreCase) ||
                !block.Name.Contains("USER_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: block name must contain G3 and " +
                    $"USER token/hash. actual={block.Name}");
            }

            if (block.Name.Contains("_CLASSIC", StringComparison.OrdinalIgnoreCase) ||
                block.Name.Contains("_ARCH", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: USER framed block crosstalk with " +
                    $"Classic/Arch. actual={block.Name}");
            }

            if (resolved.Size != TimberItemLeaderBlockSize.Small)
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: expected frameSize=Small, " +
                    $"actual={resolved.Size}.");
            }

            var geometry = AcKrovyItemLeaderBlockVariantService
                .ValidateExistingDefinitionDetailed(
                    database,
                    transaction,
                    leader.BlockContentId,
                    resolved,
                    AutoCadItemLeaderBlockVariantKey.FromDefinition(
                        resolved,
                        identity),
                    item.ResolvedTextStyleId);
            if (!geometry.IsValid)
            {
                throw new InvalidOperationException(
                    $"Case {proofCase.Token}: geometry parity FAIL - " +
                    $"{geometry.Reason}");
            }

            editor.WriteMessage(
                $"\n  {proofCase.Token}: USER framed " +
                $"requested={item.RequestedTextStyleName}; " +
                $"resolved={resolvedStyleName}; " +
                $"TextStyleIdentity={identity.CreateNameToken()}; " +
                $"frameSize={resolved.Size}; " +
                $"defH={attributeDefinition.Height:R}; " +
                $"attrH={attribute.Height:R}; " +
                $"blockScale={leader.BlockScale.X:R}; " +
                $"block={block.Name}; " +
                $"definitionStyle={definitionStyleName}; " +
                $"BlockContentId={leader.BlockContentId.Handle}; " +
                "geometry parity=PASS");
        }
        else
        {
            editor.WriteMessage(
                $"\n  {proofCase.Token}: framed contract " +
                $"defH={attributeDefinition.Height:R}; " +
                $"attrH={attribute.Height:R}; " +
                $"blockScale={leader.BlockScale.X:R}; " +
                $"block={block.Name}; " +
                $"style={resolvedStyleName}; " +
                $"definitionStyle={definitionStyleName}; " +
                $"BlockContentId={leader.BlockContentId.Handle}; PASS");
        }

        // AttributeReference.Height is already role model height (paper×denom).
        // Do not multiply by BlockScale again.
        var role = BuildRole(
            "ItemCode",
            item,
            "MLeader+AttributeReference",
            attribute.TextStyleId,
            attribute.Height,
            leader.ObjectId,
            block.Name,
            resolved.Size.ToString(),
            leader.BlockScale.X,
            blockContentId: leader.BlockContentId);
        WriteRoleLine(editor, proofCase.Token, role, true);
        return role;
    }

    private static AutoCadTextSettingsProofRoleExpectation InspectSlopeText(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        string sourceHandle,
        AutoCadAnnotationTextRolePresentation slope,
        Editor editor,
        string token)
    {
        var text = FindSlopeAngleText(modelSpace, transaction, sourceHandle);
        if (text is null)
        {
            throw new InvalidOperationException(
                $"Slope angle DBText missing for {token}.");
        }

        if (!AreClose(text.Height, slope.ModelHeightMm))
        {
            throw new InvalidOperationException(
                $"Case {token}: slope height {text.Height:R} != {slope.ModelHeightMm:R}.");
        }

        var role = BuildRole(
            "Slope",
            slope,
            "DBText",
            text.TextStyleId,
            text.Height,
            text.ObjectId,
            blockName: null,
            frameSize: null,
            blockScale: null);
        WriteRoleLine(editor, token, role, true);
        _ = database;
        _ = transaction;
        return role;
    }

    private static AutoCadTextSettingsProofRoleExpectation InspectSlopeBlock(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        string sourceHandle,
        string expectedBlockName,
        AutoCadAnnotationTextRolePresentation slope,
        Editor editor,
        string token)
    {
        Entity? glyph = null;
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not Entity entity ||
                entity.IsErased ||
                entity.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!SlopeArrowStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            glyph = entity;
            break;
        }

        if (glyph is not BlockReference blockRef)
        {
            throw new InvalidOperationException(
                $"Case {token}: expected BlockReference slope marker, " +
                $"got {glyph?.GetType().Name ?? "null"}.");
        }

        var block = (BlockTableRecord)transaction.GetObject(
            blockRef.BlockTableRecord,
            OpenMode.ForRead);
        if (!string.Equals(
                block.Name,
                expectedBlockName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Case {token}: block {block.Name} != {expectedBlockName}.");
        }

        // Blocks must not consume Slope TextStyleId / fonts / heights.
        // BlockReference is the production glyph type; numeric slope uses DBText.
        if (glyph is DBText or MText)
        {
            throw new InvalidOperationException(
                $"Case {token}: slope marker unexpectedly uses text entity " +
                $"with TextStyleId ({glyph.GetType().Name}).");
        }

        var role = new AutoCadTextSettingsProofRoleExpectation(
            "Blocks",
            slope.RequestedTextStyleName ?? string.Empty,
            "<block-no-TextStyleId>",
            slope.PaperHeightMm,
            slope.ModelHeightMm > 0
                ? (int)Math.Round(slope.ModelHeightMm / Math.Max(slope.PaperHeightMm, 0.0001d))
                : 50,
            0d,
            "BlockReference",
            block.Name,
            null,
            blockRef.ScaleFactors.X,
            blockRef.ObjectId.Handle.ToString(),
            ExpectUnchangedObjectId: false);
        WriteRoleLine(editor, token, role, true);
        editor.WriteMessage(
            $"\n  {token}: slope block does not consume Slope TextStyleId; PASS");
        _ = database;
        return role;
    }

    private static bool VerifyRoleExpectation(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        Entity source,
        AutoCadTextSettingsProofExpectedCase expected,
        AutoCadTextSettingsProofRoleExpectation role,
        Editor editor)
    {
        var handle = source.Handle.ToString();
        string resolvedStyle;
        ObjectId styleId;
        double modelHeight;
        string entityType;
        string? blockName = null;
        string? frameSize = null;
        double? blockScale = null;
        string objectIdHandle;

        if (string.Equals(role.Role, "Blocks", StringComparison.Ordinal))
        {
            var glyph = FindSlopeGlyph(modelSpace, transaction, handle);
            if (glyph is not BlockReference blockRef)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}/{role.Role}: FAIL - BlockReference missing");
                return false;
            }

            var block = (BlockTableRecord)transaction.GetObject(
                blockRef.BlockTableRecord,
                OpenMode.ForRead);
            var nameMatches = string.Equals(
                block.Name,
                role.BlockName,
                StringComparison.OrdinalIgnoreCase);
            editor.WriteMessage(
                $"\n  {expected.Token}/{role.Role}: requested={role.RequestedStyleName}; " +
                $"resolved=<block-no-TextStyleId>; TextStyleId=n/a; " +
                $"paper={role.PaperHeightMm:R}; denominator={role.Denominator}; " +
                $"modelHeight=n/a; entityType=BlockReference; " +
                $"ObjectId={blockRef.ObjectId.Handle}; blockName={block.Name}; " +
                $"blockScale={blockRef.ScaleFactors.X:R}; " +
                $"{(nameMatches ? "PASS" : "FAIL")}");
            return nameMatches;
        }

        if (string.Equals(role.Role, "Slope", StringComparison.Ordinal))
        {
            var text = FindSlopeAngleText(modelSpace, transaction, handle);
            if (text is null)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}/{role.Role}: FAIL - DBText missing");
                return false;
            }

            styleId = text.TextStyleId;
            modelHeight = text.Height;
            entityType = "DBText";
            objectIdHandle = text.ObjectId.Handle.ToString();
            resolvedStyle = ResolveStyleName(database, transaction, styleId);
        }
        else if (string.Equals(role.Role, "Dimension", StringComparison.Ordinal) &&
                 expected.Kind is AutoCadTextSettingsProofKind.FullLabel
                     or AutoCadTextSettingsProofKind.CombinedFramed
                     or AutoCadTextSettingsProofKind.RoleIsolation)
        {
            Entity? dimEntity = expected.Kind == AutoCadTextSettingsProofKind.FullLabel
                ? FindFullLabel(modelSpace, transaction, handle)
                : FindCombinedDimensionsMText(modelSpace, transaction, handle);
            if (dimEntity is not MText mText)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}/{role.Role}: FAIL - MText missing");
                return false;
            }

            styleId = mText.TextStyleId;
            modelHeight = mText.TextHeight;
            entityType = "MText";
            objectIdHandle = mText.ObjectId.Handle.ToString();
            resolvedStyle = ResolveStyleName(database, transaction, styleId);
        }
        else if (string.Equals(role.Role, "Dimension", StringComparison.Ordinal))
        {
            var leader = FindDimensionsLeader(modelSpace, transaction, handle);
            if (leader is null ||
                !TryReadMTextPresentation(
                    leader,
                    out _,
                    out styleId,
                    out modelHeight))
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}/{role.Role}: FAIL - MLeader missing");
                return false;
            }

            entityType = "MLeader";
            objectIdHandle = leader.ObjectId.Handle.ToString();
            resolvedStyle = ResolveStyleName(database, transaction, styleId);
        }
        else
        {
            // ItemCode
            MLeader? leader = expected.Kind is
                AutoCadTextSettingsProofKind.ItemPlain or
                AutoCadTextSettingsProofKind.ItemUserPreset or
                AutoCadTextSettingsProofKind.SlopeNumeric
                ? FindItemLeader(
                    modelSpace,
                    transaction,
                    handle,
                    TimberAnnotationMode.ItemNumberLeader,
                    ItemNumberLeaderStyle.Plain)
                : FindFramedItemLeader(
                    modelSpace,
                    transaction,
                    handle,
                    expected.Kind is AutoCadTextSettingsProofKind.CombinedFramed
                        or AutoCadTextSettingsProofKind.RoleIsolation
                        ? TimberAnnotationMode.DimensionsWithItemNumber
                        : TimberAnnotationMode.ItemNumberLeader,
                    AutoCadTextSettingsProofPolicy.FindCase(expected.Token)
                        .ItemStyle);

            if (leader is null)
            {
                editor.WriteMessage(
                    $"\n  {expected.Token}/{role.Role}: FAIL - item leader missing");
                return false;
            }

            objectIdHandle = leader.ObjectId.Handle.ToString();
            if (leader.ContentType == ContentType.MTextContent)
            {
                if (!TryReadMTextPresentation(
                        leader,
                        out _,
                        out styleId,
                        out modelHeight))
                {
                    editor.WriteMessage(
                        $"\n  {expected.Token}/{role.Role}: FAIL - MText missing");
                    return false;
                }

                entityType = "MLeader";
                resolvedStyle = ResolveStyleName(database, transaction, styleId);
            }
            else
            {
                var block = (BlockTableRecord)transaction.GetObject(
                    leader.BlockContentId,
                    OpenMode.ForRead);
                var attributeDefinition = block
                    .Cast<ObjectId>()
                    .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
                    .OfType<AttributeDefinition>()
                    .Single(candidate => string.Equals(
                        candidate.Tag,
                        TimberItemLeaderBlockDefinitionRules.AttributeTag,
                        StringComparison.OrdinalIgnoreCase));
                using var attribute =
                    leader.GetBlockAttribute(attributeDefinition.ObjectId);
                styleId = attribute.TextStyleId;
                // AttrRef.Height is role model height; BlockScale is separate.
                modelHeight = attribute.Height;
                entityType = "MLeader+AttributeReference";
                blockName = block.Name;
                blockScale = leader.BlockScale.X;
                frameSize = role.FrameSize;
                resolvedStyle = ResolveStyleName(database, transaction, styleId);

                var expectedDefinitionHeight =
                    AutoCadTextSettingsProofPolicy.ExpectedFramedDefinitionHeightMm;
                var expectedAttributeHeight =
                    AutoCadTextSettingsProofPolicy.ExpectedFramedAttributeHeightMm(
                        role.PaperHeightMm,
                        role.Denominator);
                var expectedScale =
                    AutoCadTextSettingsProofPolicy.ExpectedBlockScale(
                        role.Denominator);
                if (!AreClose(
                        attributeDefinition.Height,
                        expectedDefinitionHeight) ||
                    !AreClose(attribute.Height, expectedAttributeHeight) ||
                    !AreClose(leader.BlockScale.X, expectedScale))
                {
                    editor.WriteMessage(
                        $"\n  {expected.Token}/{role.Role}: FAIL - framed contract " +
                        $"defH={attributeDefinition.Height:R} expected={expectedDefinitionHeight:R}; " +
                        $"attrH={attribute.Height:R} expected={expectedAttributeHeight:R}; " +
                        $"blockScale={leader.BlockScale.X:R} expected={expectedScale:R}");
                    return false;
                }
            }
        }

        var styleMatches = string.Equals(
            resolvedStyle,
            role.ResolvedStyleName,
            StringComparison.OrdinalIgnoreCase);
        var heightMatches = AreClose(modelHeight, role.ModelHeightMm) ||
            string.Equals(role.Role, "Blocks", StringComparison.Ordinal);
        var objectIdMatches = !role.ExpectUnchangedObjectId ||
            string.Equals(
                objectIdHandle,
                role.ObjectIdHandle,
                StringComparison.OrdinalIgnoreCase);
        var casePassed = styleMatches && heightMatches && objectIdMatches;

        editor.WriteMessage(
            $"\n  {expected.Token}/{role.Role}: requested={role.RequestedStyleName}; " +
            $"resolved={resolvedStyle}; TextStyleId={styleId.Handle}; " +
            $"paper={role.PaperHeightMm:R}; denominator={role.Denominator}; " +
            $"modelHeight={modelHeight:R}; expectedHeight={role.ModelHeightMm:R}; " +
            $"entityType={entityType}; ObjectId={objectIdHandle}" +
            (blockName is null ? string.Empty : $"; blockName={blockName}") +
            (frameSize is null ? string.Empty : $"; frameSize={frameSize}") +
            (blockScale is null ? string.Empty : $"; blockScale={blockScale:R}") +
            $"; {(casePassed ? "PASS" : "FAIL")}");
        return casePassed;
    }

    private static AutoCadTextSettingsProofRoleExpectation BuildRole(
        string roleName,
        AutoCadAnnotationTextRolePresentation presentation,
        string entityType,
        ObjectId styleId,
        double modelHeight,
        ObjectId objectId,
        string? blockName,
        string? frameSize,
        double? blockScale,
        ObjectId? blockContentId = null)
    {
        _ = styleId;
        _ = blockContentId;
        return new AutoCadTextSettingsProofRoleExpectation(
            roleName,
            presentation.RequestedTextStyleName ?? string.Empty,
            presentation.ResolvedTextStyleName ?? string.Empty,
            presentation.PaperHeightMm,
            presentation.ModelHeightMm > 0 && presentation.PaperHeightMm > 0
                ? (int)Math.Round(
                    presentation.ModelHeightMm / presentation.PaperHeightMm)
                : 50,
            modelHeight,
            entityType,
            blockName,
            frameSize,
            blockScale,
            objectId.Handle.ToString(),
            ExpectUnchangedObjectId: false);
    }

    private static void WriteRoleLine(
        Editor editor,
        string token,
        AutoCadTextSettingsProofRoleExpectation role,
        bool passed) =>
        editor.WriteMessage(
            $"\n  {token}/{role.Role}: requested={role.RequestedStyleName}; " +
            $"resolved={role.ResolvedStyleName}; " +
            $"paper={role.PaperHeightMm:R}; denominator={role.Denominator}; " +
            $"modelHeight={role.ModelHeightMm:R}; entityType={role.EntityType}; " +
            $"ObjectId={role.ObjectIdHandle}" +
            (role.BlockName is null ? string.Empty : $"; blockName={role.BlockName}") +
            (role.FrameSize is null ? string.Empty : $"; frameSize={role.FrameSize}") +
            (role.BlockScale is null ? string.Empty : $"; blockScale={role.BlockScale:R}") +
            $"; {(passed ? "PASS" : "FAIL")}");

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
        AutoCadTextSettingsProofCase proofCase,
        TimberAnnotationTextSettings settings) =>
        new()
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = "K1",
            ElementType = proofCase.ElementType,
            WidthMm = 80d,
            HeightMm = 160d,
            AnnotationMode = proofCase.AnnotationMode,
            ItemNumberLeaderStyle = proofCase.ItemStyle,
            AnnotationTextSettings = settings,
            AnnotationScaleDenominatorOverride = proofCase.Denominator,
            SlopeDegrees = proofCase.SlopeDegrees,
            RoofPlaneId = "AK_DEV",
        };

    private static MLeader? FindItemLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style)
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

            if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) != mode ||
                ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle) !=
                    style)
            {
                continue;
            }

            return leader;
        }

        return null;
    }

    private static MLeader? FindFramedItemLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle,
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not MLeader leader ||
                leader.IsErased ||
                leader.OwnerId != modelSpace.ObjectId ||
                leader.ContentType != ContentType.BlockContent)
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

            if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) != mode)
            {
                continue;
            }

            if (mode == TimberAnnotationMode.DimensionsWithItemNumber)
            {
                if (data.ComponentRole !=
                        TimberMainAnnotationComponentRole.FramedItem ||
                    ItemNumberLeaderStyleRules.Normalize(
                        data.ItemNumberLeaderStyle) != style)
                {
                    continue;
                }
            }
            else if (ItemNumberLeaderStyleRules.Normalize(
                         data.ItemNumberLeaderStyle) != style)
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

    private static MText? FindFullLabel(
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

            if (TimberAnnotationModeRules.GetRepresentation(
                    data.AnnotationMode,
                    data.ItemNumberLeaderStyle) !=
                TimberMainAnnotationRepresentation.FullLabel)
            {
                continue;
            }

            return label;
        }

        return null;
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

    private static DBText? FindSlopeAngleText(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not DBText text ||
                text.IsErased ||
                text.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!SlopeAngleTextStore.TryRead(text, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return text;
        }

        return null;
    }

    private static Entity? FindSlopeGlyph(
        BlockTableRecord modelSpace,
        Transaction transaction,
        string sourceHandle)
    {
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not Entity entity ||
                entity.IsErased ||
                entity.OwnerId != modelSpace.ObjectId)
            {
                continue;
            }

            if (!SlopeArrowStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(
                    data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return entity;
        }

        return null;
    }

    private static bool TryReadMTextPresentation(
        MLeader leader,
        out MText? mText,
        out ObjectId styleId,
        out double textHeight)
    {
        mText = null;
        styleId = ObjectId.Null;
        textHeight = 0d;
        if (leader.ContentType != ContentType.MTextContent ||
            leader.MText is not MText content)
        {
            return false;
        }

        mText = content;
        styleId = content.TextStyleId;
        textHeight = content.TextHeight;
        return true;
    }

    private static bool EnsureProofUserPresetInLibrary(
        TimberAnnotationTextStylePresetLibrary libraryBefore,
        TimberAnnotationUserTextStylePreset userPreset,
        Editor editor)
    {
        var existing = libraryBefore.Presets.FirstOrDefault(preset =>
            string.Equals(
                preset.StableId,
                userPreset.StableId,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            editor.WriteMessage(
                $"\n  USER library: reused existing preset " +
                $"stableId={existing.StableId}; style={existing.AutoCadTextStyleName}. " +
                "CLEAN will not remove a pre-existing user preset.");
            return false;
        }

        var mutated = new TimberAnnotationTextStylePresetLibrary
        {
            Version = TimberAnnotationTextStylePresetLibrary.CurrentVersion,
            Presets = libraryBefore.Presets
                .Select(CloneUserPreset)
                .Append(CloneUserPreset(userPreset))
                .ToList(),
        };
        TimberAnnotationTextStylePresetLibraryStore.Save(mutated);
        editor.WriteMessage(
            $"\n  USER library: temporarily added proof preset " +
            $"stableId={userPreset.StableId}; style={userPreset.AutoCadTextStyleName}.");
        return true;
    }

    private static void RestoreUserPresetLibrary(string librarySnapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize<TimberAnnotationTextStylePresetLibrary>(
            librarySnapshotJson,
            LibraryJsonOptions)
            ?? TimberAnnotationTextStylePresetLibrary.CreateDefault();
        TimberAnnotationTextStylePresetLibraryStore.Save(snapshot.Normalize());
    }

    private static TimberAnnotationUserTextStylePreset CloneUserPreset(
        TimberAnnotationUserTextStylePreset preset) =>
        new()
        {
            StableId = preset.StableId,
            DisplayName = preset.DisplayName,
            FontFile = preset.FontFile,
            AutoCadTextStyleName = preset.AutoCadTextStyleName,
            WidthFactor = preset.WidthFactor,
            ObliqueAngleDegrees = preset.ObliqueAngleDegrees,
        };

    private static string AssertSharedUserFramedDefinition(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        Editor editor)
    {
        var primary = RequireUserFramedLeader(
            database,
            transaction,
            modelSpace,
            AutoCadTextSettingsProofPolicy.UserFramedToken);
        var twin = RequireUserFramedLeader(
            database,
            transaction,
            modelSpace,
            AutoCadTextSettingsProofPolicy.UserFramedTwinToken);
        if (primary.BlockContentId.IsNull ||
            twin.BlockContentId.IsNull ||
            primary.BlockContentId != twin.BlockContentId)
        {
            throw new InvalidOperationException(
                "IUFR/IUFR2 must share one BlockContentId for the same " +
                "FrameKind+FrameSize+GeometryVersion+TextStyleIdentity. " +
                $"IUFR={primary.BlockContentId.Handle}; " +
                $"IUFR2={twin.BlockContentId.Handle}.");
        }

        var block = (BlockTableRecord)transaction.GetObject(
            primary.BlockContentId,
            OpenMode.ForRead);
        var classic = FindFramedLeaderBlockName(
            database,
            transaction,
            modelSpace,
            "IR");
        var circle = FindFramedLeaderBlockName(
            database,
            transaction,
            modelSpace,
            "IC");
        if ((!string.IsNullOrWhiteSpace(classic) &&
             string.Equals(classic, block.Name, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(circle) &&
             string.Equals(circle, block.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "USER framed definition crosstalk with Classic/Arch framed " +
                $"definition. USER={block.Name}; IR={classic}; IC={circle}.");
        }

        editor.WriteMessage(
            $"\n  IUFR/IUFR2 shared definition reuse=PASS; " +
            $"BlockContentId={primary.BlockContentId.Handle}; " +
            $"block={block.Name}; no Classic/Arch crosstalk");
        _ = database;
        return primary.BlockContentId.Handle.ToString();
    }

    private static bool VerifySharedUserFramedDefinition(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        AutoCadTextSettingsProofManifest manifest,
        Editor editor)
    {
        var hasUserFramed = manifest.Cases.Any(expected =>
            AutoCadTextSettingsProofPolicy.IsUserFramedToken(expected.Token));
        if (!hasUserFramed)
        {
            return true;
        }

        try
        {
            var primary = RequireUserFramedLeader(
                database,
                transaction,
                modelSpace,
                AutoCadTextSettingsProofPolicy.UserFramedToken);
            var twin = RequireUserFramedLeader(
                database,
                transaction,
                modelSpace,
                AutoCadTextSettingsProofPolicy.UserFramedTwinToken);
            if (primary.BlockContentId != twin.BlockContentId)
            {
                editor.WriteMessage(
                    "\n  IUFR/IUFR2: FAIL - shared BlockContentId broken after " +
                    "persist/reopen");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    manifest.SharedUserFramedBlockContentHandle) &&
                !string.Equals(
                    primary.BlockContentId.Handle.ToString(),
                    manifest.SharedUserFramedBlockContentHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                editor.WriteMessage(
                    "\n  IUFR/IUFR2: FAIL - BlockContentId handle changed; " +
                    $"expected={manifest.SharedUserFramedBlockContentHandle}; " +
                    $"actual={primary.BlockContentId.Handle}");
                return false;
            }

            var block = (BlockTableRecord)transaction.GetObject(
                primary.BlockContentId,
                OpenMode.ForRead);
            if (!block.Name.Contains("USER_", StringComparison.OrdinalIgnoreCase) ||
                !block.Name.Contains(
                    $"_G{AutoCadItemLeaderBlockVariantKey.CurrentGeometryVersion}_",
                    StringComparison.OrdinalIgnoreCase))
            {
                editor.WriteMessage(
                    $"\n  IUFR/IUFR2: FAIL - unexpected USER G3 block name " +
                    $"{block.Name}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(manifest.UserPresetStyleName))
            {
                var attributeDefinition = block
                    .Cast<ObjectId>()
                    .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
                    .OfType<AttributeDefinition>()
                    .Single(candidate => string.Equals(
                        candidate.Tag,
                        TimberItemLeaderBlockDefinitionRules.AttributeTag,
                        StringComparison.OrdinalIgnoreCase));
                var definitionStyle = ResolveStyleName(
                    database,
                    transaction,
                    attributeDefinition.TextStyleId);
                if (!string.Equals(
                        definitionStyle,
                        manifest.UserPresetStyleName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    editor.WriteMessage(
                        "\n  IUFR/IUFR2: FAIL - AttrDef TextStyle is not the " +
                        $"USER preset. expected={manifest.UserPresetStyleName}; " +
                        $"actual={definitionStyle}");
                    return false;
                }
            }

            editor.WriteMessage(
                $"\n  IUFR/IUFR2 VERIFY shared definition reuse=PASS; " +
                $"BlockContentId={primary.BlockContentId.Handle}; " +
                $"block={block.Name}; USER preset persists; " +
                "no Classic/Arch crosstalk");
            return true;
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\n  IUFR/IUFR2: FAIL - {exception.Message}");
            return false;
        }
    }

    private static MLeader RequireUserFramedLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        string token)
    {
        var source = FindMarkedSource(modelSpace, transaction, token);
        if (source is null)
        {
            throw new InvalidOperationException(
                $"USER framed source {token} is missing.");
        }

        var leader = FindFramedItemLeader(
            modelSpace,
            transaction,
            source.Handle.ToString(),
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle);
        if (leader is null)
        {
            throw new InvalidOperationException(
                $"USER framed MLeader {token} is missing.");
        }

        _ = database;
        return leader;
    }

    private static string? FindFramedLeaderBlockName(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        string token)
    {
        var source = FindMarkedSource(modelSpace, transaction, token);
        if (source is null)
        {
            return null;
        }

        var proofCase = AutoCadTextSettingsProofPolicy.Cases.FirstOrDefault(
            candidate => string.Equals(
                candidate.Token,
                token,
                StringComparison.OrdinalIgnoreCase));
        if (proofCase is null)
        {
            return null;
        }

        var leader = FindFramedItemLeader(
            modelSpace,
            transaction,
            source.Handle.ToString(),
            TimberAnnotationMode.ItemNumberLeader,
            proofCase.ItemStyle);
        if (leader is null || leader.BlockContentId.IsNull)
        {
            return null;
        }

        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        _ = database;
        return block.Name;
    }

    private static string TryCleanupProofOwnedUserTextStyle(
        Database database,
        Transaction transaction,
        AutoCadTextSettingsProofManifest? manifest,
        Editor editor)
    {
        if (manifest is null ||
            !manifest.ProofCreatedUserTextStyle ||
            string.IsNullOrWhiteSpace(manifest.UserPresetStyleName))
        {
            return "USER TextStyle left in place (not proof-created or unknown)";
        }

        var styleName = manifest.UserPresetStyleName;
        try
        {
            var textStyleTable = (TextStyleTable)transaction.GetObject(
                database.TextStyleTableId,
                OpenMode.ForRead);
            if (!textStyleTable.Has(styleName))
            {
                return $"USER TextStyle {styleName} already absent";
            }

            // Shared G3 block definitions may still reference the style in this
            // disposable proof DWG. Erase only when AutoCAD accepts it; otherwise
            // leave the app-owned style and report clearly.
            textStyleTable.UpgradeOpen();
            var styleId = textStyleTable[styleName];
            var style = (TextStyleTableRecord)transaction.GetObject(
                styleId,
                OpenMode.ForWrite);
            style.Erase();
            editor.WriteMessage(
                $"\n  USER TextStyle cleanup: erased proof-created {styleName}");
            return $"proof-created USER TextStyle {styleName} erased";
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\n  USER TextStyle cleanup: left {styleName} in place " +
                $"(SymbolTable erase not safe: {exception.Message}). " +
                "Existing user presets/styles were not deleted.");
            return $"USER TextStyle {styleName} left in place (erase not safe)";
        }
    }

    private static AutoCadTextSettingsProofStandardSnapshot? SnapshotStandard(
        Database database,
        Transaction transaction)
    {
        var textStyleTable = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        if (!textStyleTable.Has(
                TimberAnnotationTextSettingsRules.DefaultTextStyleName))
        {
            return null;
        }

        var standard = (TextStyleTableRecord)transaction.GetObject(
            textStyleTable[TimberAnnotationTextSettingsRules.DefaultTextStyleName],
            OpenMode.ForRead);
        return new AutoCadTextSettingsProofStandardSnapshot(
            standard.Name,
            standard.FileName ?? string.Empty,
            standard.TextSize,
            standard.XScale,
            standard.ObliquingAngle);
    }

    private static bool VerifyStandardUntouched(
        Database database,
        Transaction transaction,
        AutoCadTextSettingsProofStandardSnapshot? before,
        Editor editor)
    {
        var after = SnapshotStandard(database, transaction);
        if (before is null || after is null)
        {
            editor.WriteMessage(
                "\n  Standard: NOT TESTED - Standard style missing before/after");
            return true;
        }

        var ok =
            string.Equals(before.Name, after.Name, StringComparison.Ordinal) &&
            string.Equals(
                before.FontFileName,
                after.FontFileName,
                StringComparison.OrdinalIgnoreCase) &&
            AreClose(before.TextSize, after.TextSize) &&
            AreClose(before.XScale, after.XScale) &&
            AreClose(before.ObliquingAngle, after.ObliquingAngle);
        editor.WriteMessage(
            $"\n  Standard: font={after.FontFileName}; size={after.TextSize:R}; " +
            $"{(ok ? "PASS (untouched)" : "FAIL")}");
        return ok;
    }

    private static void AssertNoDuplicateBuiltIns(
        Database database,
        Transaction transaction)
    {
        if (!VerifyBuiltInUniqueness(database, transaction, editor: null))
        {
            throw new InvalidOperationException(
                "Duplicate or missing app-owned built-in text styles.");
        }
    }

    private static bool VerifyBuiltInUniqueness(
        Database database,
        Transaction transaction,
        Editor? editor)
    {
        var textStyleTable = (TextStyleTable)transaction.GetObject(
            database.TextStyleTableId,
            OpenMode.ForRead);
        var counts = TimberAnnotationTextStylePresetRules.GetBuiltInDefinitions()
            .ToDictionary(
                definition => definition.AutoCadTextStyleName,
                _ => 0,
                StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in textStyleTable)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is not TextStyleTableRecord style ||
                style.IsErased)
            {
                continue;
            }

            if (counts.ContainsKey(style.Name))
            {
                counts[style.Name]++;
            }
        }

        var ok = counts.Values.All(count => count == 1);
        editor?.WriteMessage(
            $"\n  BuiltIns: " +
            $"{string.Join("; ", counts.Select(pair => $"{pair.Key}={pair.Value}"))}; " +
            $"{(ok ? "PASS" : "FAIL")}");
        return ok;
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
                AutoCadTextSettingsProofPolicy.RegAppName),
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
                currentApp = Convert.ToString(
                    value.Value,
                    CultureInfo.InvariantCulture);
                continue;
            }

            if (!string.Equals(
                    currentApp,
                    AutoCadTextSettingsProofPolicy.RegAppName,
                    StringComparison.OrdinalIgnoreCase) ||
                value.TypeCode != XDataStringCode)
            {
                continue;
            }

            var marker = Convert.ToString(
                value.Value,
                CultureInfo.InvariantCulture);
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
        if (regAppTable.Has(AutoCadTextSettingsProofPolicy.RegAppName))
        {
            return;
        }

        regAppTable.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = AutoCadTextSettingsProofPolicy.RegAppName,
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

    private static void WriteFreshDrawingManifest(
        Database database,
        Transaction transaction,
        AutoCadFreshDrawingProofManifest manifest)
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
        namedObjects.SetAt(
            AutoCadTextSettingsProofPolicy.FreshDrawingManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadFreshDrawingProofManifest? ReadFreshDrawingManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadTextSettingsProofPolicy
                    .FreshDrawingManifestDictionaryKey))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadTextSettingsProofPolicy
                    .FreshDrawingManifestDictionaryKey),
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
                builder.Append(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture));
            }
        }

        return JsonSerializer.Deserialize<AutoCadFreshDrawingProofManifest>(
            builder.ToString(),
            JsonOptions);
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadTextSettingsProofManifest manifest)
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
                AutoCadTextSettingsProofPolicy.ManifestDictionaryKey))
        {
            var existingId = namedObjects.GetAt(
                AutoCadTextSettingsProofPolicy.ManifestDictionaryKey);
            var existing = (DBObject)transaction.GetObject(
                existingId,
                OpenMode.ForWrite);
            existing.Erase();
        }

        namedObjects.SetAt(
            AutoCadTextSettingsProofPolicy.ManifestDictionaryKey,
            record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static AutoCadTextSettingsProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadTextSettingsProofPolicy.ManifestDictionaryKey))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            namedObjects.GetAt(
                AutoCadTextSettingsProofPolicy.ManifestDictionaryKey),
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
                builder.Append(
                    Convert.ToString(value.Value, CultureInfo.InvariantCulture));
            }
        }

        return JsonSerializer.Deserialize<AutoCadTextSettingsProofManifest>(
            builder.ToString(),
            JsonOptions);
    }

    private static void DeleteManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(
                AutoCadTextSettingsProofPolicy.ManifestDictionaryKey))
        {
            return;
        }

        namedObjects.UpgradeOpen();
        var existingId = namedObjects.GetAt(
            AutoCadTextSettingsProofPolicy.ManifestDictionaryKey);
        var existing = (DBObject)transaction.GetObject(
            existingId,
            OpenMode.ForWrite);
        existing.Erase();
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) <= Tolerance;
}
#endif
