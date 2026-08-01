#if DEBUG
using System.Globalization;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFramedTextAttributeMatrixService
{
    internal const string MatrixRegAppName = "AK23_TEXTATTR_MATRIX";

    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;

        MatrixSetup setup;
        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            if (!TryValidateEmptyModelSpace(database, transaction, editor))
            {
                return;
            }

            var catalog = AutoCadTextStyleResolver.ReadCatalog(database, transaction);
            var block = AcKrovyItemLeaderBlockService.Ensure(
                database,
                transaction,
                ItemNumberLeaderStyle.Circle,
                AutoCadFramedTextAttributeMatrixPolicy.Variants[0].Token,
                preserveExistingDefinition: true);
            var sharedBlock = (BlockTableRecord)transaction.GetObject(
                block.BlockId,
                OpenMode.ForRead);
            var definition = (AttributeDefinition)transaction.GetObject(
                block.AttributeDefinitionId,
                OpenMode.ForRead);
            var definitionStyleName = ReadCanonicalStyleName(
                database,
                transaction,
                definition.TextStyleId);
            var selectedStyle = catalog.CompatibleStyles.FirstOrDefault(style =>
                    style.TextStyleId != definition.TextStyleId) ??
                catalog.CompatibleStyles.FirstOrDefault();
            if (selectedStyle is null)
            {
                editor.WriteMessage(
                    "\nAK23 matrix requires at least one compatible text style. " +
                    "No matrix entity was created.");
                return;
            }

            EnsureRegApp(database, transaction, MatrixRegAppName);
            setup = new MatrixSetup(
                block.BlockId,
                block.AttributeDefinitionId,
                CaptureDefinitionSnapshot(
                    sharedBlock,
                    definition,
                    definitionStyleName),
                selectedStyle,
                selectedStyle.TextStyleId != definition.TextStyleId);
            transaction.Commit();
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK23 matrix setup failed; transaction rolled back: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        WriteDefinitionSnapshot(editor, setup.Definition);
        if (!setup.DistinctStyleOverrideExpected)
        {
            editor.WriteMessage(
                "\nSTYLE NOT TESTED: no compatible text style differs from the " +
                "shared AttributeDefinition.TextStyleId.");
        }

        var results = new List<AutoCadFramedTextAttributeMatrixVariantResult>();
        var definitionCheckpoints = new List<MatrixDefinitionCheckpoint>();
        foreach (var variant in AutoCadFramedTextAttributeMatrixPolicy.Variants)
        {
            results.Add(RunVariant(database, editor, setup, variant));
            definitionCheckpoints.Add(
                CaptureDefinitionCheckpoint(database, setup, variant.Name));
        }

        var integrityPreserved = AuditDefinitionIntegrity(
            editor,
            setup.Definition,
            definitionCheckpoints);
        WriteOutcome(
            editor,
            AutoCadFramedTextAttributeMatrixPolicy.DetermineOutcome(results),
            AutoCadFramedTextAttributeMatrixPolicy.SummarizeCapabilities(results),
            integrityPreserved);
    }

    public static void Cleanup(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;

        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database),
                OpenMode.ForRead);
            var erased = 0;
            foreach (ObjectId id in modelSpace.Cast<ObjectId>().ToArray())
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not
                        MLeader leader ||
                    !TryReadMarker(leader, out _))
                {
                    continue;
                }

                leader.UpgradeOpen();
                leader.Erase();
                erased++;
            }

            transaction.Commit();
            editor.WriteMessage(
                $"\nAK23 matrix cleanup removed {erased} marked MLeader(s). " +
                "The shared block definition and text styles were not changed.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK23 matrix cleanup failed; transaction rolled back: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static AutoCadFramedTextAttributeMatrixVariantResult RunVariant(
        Database database,
        Editor editor,
        MatrixSetup setup,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        ObjectId leaderId = ObjectId.Null;
        AutoCadFramedTextAttributeMatrixObservation? preCommit = null;
        AutoCadFramedTextAttributeMatrixObservation? postCommit = null;
        System.Exception? failure = null;

        try
        {
            if (variant.Kind ==
                AutoCadFramedTextAttributeMatrixVariantKind.SecondWriteTransaction)
            {
                leaderId = CreateDefaultLeaderInFirstTransaction(
                    database,
                    setup,
                    variant);
                preCommit = ModifyInSecondWriteTransaction(
                    database,
                    setup,
                    variant,
                    leaderId);
            }
            else
            {
                (leaderId, preCommit) = RunSingleWriteTransaction(
                    database,
                    setup,
                    variant);
            }
        }
        catch (System.Exception exception)
        {
            failure = exception;
        }

        if (!leaderId.IsNull)
        {
            try
            {
                postCommit = ReadObservation(
                    database,
                    setup.AttributeDefinitionId,
                    leaderId);
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        var preResult = preCommit is null
            ? null
            : Evaluate(setup, variant, preCommit);
        var postResult = postCommit is null
            ? null
            : Evaluate(setup, variant, postCommit);
        WriteVariantResult(
            editor,
            setup,
            variant,
            leaderId,
            preCommit,
            preResult,
            postCommit,
            postResult,
            failure);
        return new AutoCadFramedTextAttributeMatrixVariantResult(
            variant,
            preResult,
            postResult);
    }

    private static (
        ObjectId LeaderId,
        AutoCadFramedTextAttributeMatrixObservation PreCommit)
        RunSingleWriteTransaction(
            Database database,
            MatrixSetup setup,
            AutoCadFramedTextAttributeMatrixCase variant)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var definition = (AttributeDefinition)transaction.GetObject(
            setup.AttributeDefinitionId,
            OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForWrite);
        using var leader = CreateLeader(
            database,
            setup.BlockId,
            variant,
            applyBlockScale: variant.BlockScaleOrder ==
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .BeforeSetBlockAttribute);

        switch (variant.Kind)
        {
            case AutoCadFramedTextAttributeMatrixVariantKind.PreDatabaseCurrent:
                SetFromDefinition(leader, definition, setup.Style, variant);
                AppendLeader(modelSpace, transaction, leader);
                break;

            case AutoCadFramedTextAttributeMatrixVariantKind.AppendBeforeSet:
                AppendLeader(modelSpace, transaction, leader);
                SetFromDefinition(leader, definition, setup.Style, variant);
                break;

            case AutoCadFramedTextAttributeMatrixVariantKind
                    .GetModifySetAfterAppend:
                AppendLeader(modelSpace, transaction, leader);
                ModifyExistingAttribute(leader, definition, setup.Style, variant);
                break;

            case AutoCadFramedTextAttributeMatrixVariantKind.BlockScaleAfterSet:
                AppendLeader(modelSpace, transaction, leader);
                SetFromDefinition(leader, definition, setup.Style, variant);
                leader.BlockScale = new Scale3d(variant.ExpectedBlockScale);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }

        SetMatrixXData(leader, variant);
        var observation = CaptureObservation(
            database,
            transaction,
            leader,
            definition.ObjectId);
        var leaderId = leader.ObjectId;
        transaction.Commit();
        return (leaderId, observation);
    }

    private static ObjectId CreateDefaultLeaderInFirstTransaction(
        Database database,
        MatrixSetup setup,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForWrite);
        using var leader = CreateLeader(
            database,
            setup.BlockId,
            variant,
            applyBlockScale: true);
        AppendLeader(modelSpace, transaction, leader);
        SetMatrixXData(leader, variant);
        var leaderId = leader.ObjectId;
        transaction.Commit();
        return leaderId;
    }

    private static AutoCadFramedTextAttributeMatrixObservation
        ModifyInSecondWriteTransaction(
            Database database,
            MatrixSetup setup,
            AutoCadFramedTextAttributeMatrixCase variant,
            ObjectId leaderId)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var definition = (AttributeDefinition)transaction.GetObject(
            setup.AttributeDefinitionId,
            OpenMode.ForRead);
        var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForWrite);
        ModifyExistingAttribute(leader, definition, setup.Style, variant);
        var observation = CaptureObservation(
            database,
            transaction,
            leader,
            definition.ObjectId);
        transaction.Commit();
        return observation;
    }

    private static MLeader CreateLeader(
        Database database,
        ObjectId blockId,
        AutoCadFramedTextAttributeMatrixCase variant,
        bool applyBlockScale)
    {
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        if (applyBlockScale)
        {
            leader.BlockScale = new Scale3d(variant.ExpectedBlockScale);
        }
        leader.BlockRotation = 0d;
        leader.BlockPosition = new Point3d(variant.BlockPositionX, 0d, 0d);
        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(
            lineIndex,
            new Point3d(variant.BlockPositionX - 300d, -250d, 0d));
        leader.AddLastVertex(
            lineIndex,
            new Point3d(variant.BlockPositionX - 100d, 0d, 0d));
        return leader;
    }

    private static void AppendLeader(
        BlockTableRecord modelSpace,
        Transaction transaction,
        MLeader leader)
    {
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
    }

    private static void SetFromDefinition(
        MLeader leader,
        AttributeDefinition definition,
        AutoCadTextStyleCatalogEntry style,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        using var attribute = new AttributeReference();
        attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
        ApplyExpectedValues(attribute, style, variant);
        leader.SetBlockAttribute(definition.ObjectId, attribute);
    }

    private static void ModifyExistingAttribute(
        MLeader leader,
        AttributeDefinition definition,
        AutoCadTextStyleCatalogEntry style,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        using var attribute = leader.GetBlockAttribute(definition.ObjectId);
        ApplyExpectedValues(attribute, style, variant);
        leader.SetBlockAttribute(definition.ObjectId, attribute);
    }

    private static void ApplyExpectedValues(
        AttributeReference attribute,
        AutoCadTextStyleCatalogEntry style,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        attribute.TextString = variant.Token;
        attribute.TextStyleId = style.TextStyleId;
        attribute.Height = variant.ExpectedBaseHeight;
    }

    private static AutoCadFramedTextAttributeMatrixObservation ReadObservation(
        Database database,
        ObjectId definitionId,
        ObjectId leaderId)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForRead);
        return CaptureObservation(database, transaction, leader, definitionId);
    }

    private static AutoCadFramedTextAttributeMatrixObservation CaptureObservation(
        Database database,
        Transaction transaction,
        MLeader leader,
        ObjectId definitionId)
    {
        using var attribute = leader.GetBlockAttribute(definitionId);
        return new AutoCadFramedTextAttributeMatrixObservation(
            attribute.TextString,
            attribute.Height,
            leader.BlockScale.X,
            attribute.TextStyleId.Handle.ToString(),
            ReadCanonicalStyleName(database, transaction, attribute.TextStyleId));
    }

    private static AutoCadFramedTextAttributeMatrixPhaseResult Evaluate(
        MatrixSetup setup,
        AutoCadFramedTextAttributeMatrixCase variant,
        AutoCadFramedTextAttributeMatrixObservation observation) =>
        AutoCadFramedTextAttributeMatrixPolicy.Evaluate(
            variant,
            observation,
            setup.Style.TextStyleId.Handle.ToString(),
            setup.Definition.TextStyleHandle,
            setup.DistinctStyleOverrideExpected);

    private static AutoCadFramedTextAttributeDefinitionAuditSnapshot
        CaptureDefinitionSnapshot(
        BlockTableRecord sharedBlock,
        AttributeDefinition definition,
        string textStyleName) =>
        new(
            definition.ObjectId.ToString(),
            definition.Handle.ToString(),
            sharedBlock.Handle.ToString(),
            definition.Tag,
            definition.Prompt,
            definition.TextString,
            definition.TextStyleId.Handle.ToString(),
            textStyleName,
            definition.Height,
            definition.Position.X,
            definition.Position.Y,
            definition.Position.Z,
            definition.AlignmentPoint.X,
            definition.AlignmentPoint.Y,
            definition.AlignmentPoint.Z,
            definition.Rotation,
            definition.WidthFactor,
            definition.Oblique,
            (int)definition.HorizontalMode,
            (int)definition.VerticalMode,
            definition.Invisible,
            definition.Constant,
            definition.Preset,
            definition.Verifiable,
            definition.LockPositionInBlock,
            definition.IsErased,
            definition.Layer,
            (int)definition.Color.ColorMethod,
            definition.LinetypeId.Handle.ToString(),
            (int)definition.LineWeight);

    private static void WriteDefinitionSnapshot(
        Editor editor,
        AutoCadFramedTextAttributeDefinitionAuditSnapshot definition) =>
        editor.WriteMessage(
            "\nAK23 shared AttributeDefinition snapshot (read-only):" +
            $"\n  ObjectId={definition.DiagnosticObjectId}, " +
            $"handle={definition.Handle}, " +
            $"ownerBlockHandle={definition.OwnerBlockHandle}" +
            $"\n  Tag={definition.Tag}, Prompt={definition.Prompt}, " +
            $"TextString={definition.TextString}" +
            $"\n  TextStyleId={definition.TextStyleHandle}, " +
            $"style={definition.TextStyleName}" +
            $"\n  Height={Format(definition.Height)}" +
            $"\n  Position=({Format(definition.PositionX)}, " +
            $"{Format(definition.PositionY)}, {Format(definition.PositionZ)}), " +
            $"AlignmentPoint=({Format(definition.AlignmentX)}, " +
            $"{Format(definition.AlignmentY)}, {Format(definition.AlignmentZ)})" +
            $"\n  Rotation={Format(definition.Rotation)}, " +
            $"WidthFactor={Format(definition.WidthFactor)}, " +
            $"Oblique={Format(definition.Oblique)}" +
            $"\n  HorizontalMode={definition.HorizontalMode}, " +
            $"VerticalMode={definition.VerticalMode}, " +
            $"Invisible={definition.Invisible}, Constant={definition.Constant}, " +
            $"Preset={definition.Preset}, Verifiable={definition.Verifiable}, " +
            $"LockPositionInBlock={definition.LockPositionInBlock}, " +
            $"IsErased={definition.IsErased}" +
            $"\n  Layer={definition.Layer}, ColorMethod={definition.ColorMethod}, " +
            $"LinetypeId={definition.LinetypeHandle}, " +
            $"LineWeight={definition.LineWeight}");

    private static void WriteVariantResult(
        Editor editor,
        MatrixSetup setup,
        AutoCadFramedTextAttributeMatrixCase variant,
        ObjectId leaderId,
        AutoCadFramedTextAttributeMatrixObservation? preObservation,
        AutoCadFramedTextAttributeMatrixPhaseResult? preResult,
        AutoCadFramedTextAttributeMatrixObservation? postObservation,
        AutoCadFramedTextAttributeMatrixPhaseResult? postResult,
        System.Exception? failure)
    {
        var handle = TryReadHandle(leaderId);
        editor.WriteMessage(
            $"\n\n{variant.Name}" +
            $"\n  MLeader={handle}, " +
            $"sharedBlock={setup.Definition.OwnerBlockHandle}" +
            $"\n  definitionBaseHeight={Format(setup.Definition.Height)}, " +
            $"definitionStyle={setup.Definition.TextStyleName} " +
            $"({setup.Definition.TextStyleHandle})" +
            $"\n  expectedToken={variant.Token}, " +
            $"expectedBaseHeight={Format(variant.ExpectedBaseHeight)}, " +
            $"expectedBlockScale={Format(variant.ExpectedBlockScale)}, " +
            $"expectedEffectiveHeight={Format(variant.ExpectedEffectiveHeight)}, " +
            $"expectedStyle={setup.Style.CanonicalName} " +
            $"({setup.Style.TextStyleId.Handle}), " +
            $"BlockScaleOrder={variant.BlockScaleOrder}");
        WritePhase(editor, "PRE-COMMIT", preObservation, preResult);
        WritePhase(editor, "POST-COMMIT", postObservation, postResult);
        if (failure is not null)
        {
            editor.WriteMessage(
                $"\n  EXCEPTION: {failure.GetType().Name}: {failure.Message}");
        }
        if (postResult is
            {
                BaseHeightStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
                EffectiveHeightStatus:
                    AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
                StyleStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
                BlockScaleStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            })
        {
            editor.WriteMessage("\n  HOST-SUPPORTED CANDIDATE");
        }
    }

    private static void WritePhase(
        Editor editor,
        string phase,
        AutoCadFramedTextAttributeMatrixObservation? observation,
        AutoCadFramedTextAttributeMatrixPhaseResult? result)
    {
        if (observation is null || result is null)
        {
            editor.WriteMessage($"\n  {phase}: NOT AVAILABLE");
            return;
        }

        editor.WriteMessage(
            $"\n  {phase}: {Status(result.OverallStatus)}, " +
            $"BASE HEIGHT={Status(result.BaseHeightStatus)}, " +
            $"EFFECTIVE HEIGHT={Status(result.EffectiveHeightStatus)}, " +
            $"STYLE={Status(result.StyleStatus)}, " +
            $"BLOCK SCALE={Status(result.BlockScaleStatus)}, " +
            $"TOKEN={Status(result.TokenStatus)}" +
            $"\n    actualToken={observation.Token}, " +
            $"rawAttributeHeight={Format(observation.RawAttributeHeight)}, " +
            $"normalizedBaseHeight={Format(observation.NormalizedBaseHeight)}, " +
            $"actualStyle={observation.TextStyleName} " +
            $"({observation.TextStyleHandle}), " +
            $"BlockScale={Format(observation.BlockScale)}, " +
            $"actualEffectiveHeight={Format(observation.ActualEffectiveHeight)}");
    }

    private static void WriteOutcome(
        Editor editor,
        AutoCadFramedTextAttributeMatrixOutcome outcome,
        AutoCadFramedTextAttributeMatrixCapabilitySummary capabilities,
        bool? definitionIntegrityPreserved)
    {
        var message = outcome switch
        {
            AutoCadFramedTextAttributeMatrixOutcome.HostSupportedCandidate =>
                "HOST-SUPPORTED CANDIDATE found. Run SAVE/CLOSE/REOPEN before " +
                "considering any production integration.",
            AutoCadFramedTextAttributeMatrixOutcome
                    .PerInstanceHeightAndStyleNotSupported =>
                "PER-INSTANCE HEIGHT/STYLE NOT SUPPORTED BY TESTED MLEADER API PATHS.",
            AutoCadFramedTextAttributeMatrixOutcome.MixedResults =>
                "MIXED RESULTS: Height and TextStyleId must be evaluated separately.",
            _ =>
                "INCONCLUSIVE: one or more variants or the distinct style override " +
                "could not be tested.",
        };
        editor.WriteMessage(
            $"\n\nPER-INSTANCE TOKEN SUPPORT: {Capability(capabilities.Token)}" +
            $"\nPER-INSTANCE BASE HEIGHT SUPPORT: " +
            $"{Capability(capabilities.BaseHeight)}" +
            $"\nPER-INSTANCE TEXT STYLE SUPPORT: " +
            $"{Capability(capabilities.TextStyle)}" +
            $"\nBLOCK SCALE SUPPORT: {Capability(capabilities.BlockScale)}" +
            $"\nSHARED ATTRIBUTE DEFINITION INTEGRITY: " +
            (definitionIntegrityPreserved.HasValue
                ? definitionIntegrityPreserved.Value ? "PRESERVED" : "CHANGED"
                : "INCONCLUSIVE") +
            $"\n\nAK23 MATRIX RESULT: {message}");
    }

    private static MatrixDefinitionCheckpoint CaptureDefinitionCheckpoint(
        Database database,
        MatrixSetup setup,
        string label)
    {
        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var block = (BlockTableRecord)transaction.GetObject(
                setup.BlockId,
                OpenMode.ForRead);
            var definition = (AttributeDefinition)transaction.GetObject(
                setup.AttributeDefinitionId,
                OpenMode.ForRead);
            return new MatrixDefinitionCheckpoint(
                label,
                CaptureDefinitionSnapshot(
                    block,
                    definition,
                    ReadCanonicalStyleName(
                        database,
                        transaction,
                        definition.TextStyleId)),
                null);
        }
        catch (System.Exception exception)
        {
            return new MatrixDefinitionCheckpoint(
                label,
                null,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool? AuditDefinitionIntegrity(
        Editor editor,
        AutoCadFramedTextAttributeDefinitionAuditSnapshot before,
        IReadOnlyList<MatrixDefinitionCheckpoint> checkpoints)
    {
        AutoCadFramedTextAttributeDefinitionAuditSnapshot previous = before;
        var auditAvailable = true;
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint.Snapshot is null)
            {
                auditAvailable = false;
                editor.WriteMessage(
                    $"\nDefinition checkpoint after {checkpoint.Label}: " +
                    $"UNAVAILABLE: {checkpoint.Error}");
                continue;
            }

            var transition = AutoCadFramedTextAttributeMatrixPolicy
                .CompareDefinitionSnapshots(previous, checkpoint.Snapshot);
            editor.WriteMessage(
                transition.IntegrityPreserved
                    ? $"\nDefinition checkpoint after {checkpoint.Label}: UNCHANGED."
                    : $"\nDefinition checkpoint after {checkpoint.Label}: CHANGED. " +
                      $"Fields: {string.Join(", ", transition.ChangedIntegrityFields)}");
            previous = checkpoint.Snapshot;
        }

        if (!auditAvailable || checkpoints.Count == 0 ||
            checkpoints[^1].Snapshot is null)
        {
            editor.WriteMessage(
                "\nShared AttributeDefinition integrity: INCONCLUSIVE.");
            return null;
        }

        try
        {
            var audit = AutoCadFramedTextAttributeMatrixPolicy
                .CompareDefinitionSnapshots(before, checkpoints[^1].Snapshot!);
            editor.WriteMessage(
                "\n\nShared AttributeDefinition field-by-field integrity audit:");
            foreach (var field in audit.Fields)
            {
                var relevance = field.IsIntegrityRelevant
                    ? string.Empty
                    : " [DIAGNOSTIC ONLY]";
                editor.WriteMessage(
                    field.HasChanged
                        ? $"\n  {field.FieldName}: CHANGED: {field.Before} -> " +
                          $"{field.After}{relevance}"
                        : $"\n  {field.FieldName}: UNCHANGED: " +
                          $"{field.After}{relevance}");
            }
            editor.WriteMessage(
                audit.IntegrityPreserved
                    ? "\nShared AttributeDefinition integrity: PRESERVED."
                    : "\nShared AttributeDefinition integrity: CHANGED. Fields: " +
                      string.Join(", ", audit.ChangedIntegrityFields));
            return audit.IntegrityPreserved;
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nShared AttributeDefinition verification failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static bool TryValidateEmptyModelSpace(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForRead);
        if (!modelSpace.Cast<ObjectId>().Any())
        {
            return true;
        }

        editor.WriteMessage(
            "\nAK23 matrix requires a new disposable drawing with empty model space. " +
            "No drawing data was changed.");
        return false;
    }

    private static void EnsureRegApp(
        Database database,
        Transaction transaction,
        string name)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(name))
        {
            return;
        }

        table.UpgradeOpen();
        using var record = new RegAppTableRecord { Name = name };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void SetMatrixXData(
        MLeader leader,
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        using var buffer = new ResultBuffer(
            new TypedValue(
                (int)DxfCode.ExtendedDataRegAppName,
                MatrixRegAppName),
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                AutoCadFramedTextAttributeMatrixPolicy.CreateMarker(variant)));
        leader.XData = buffer;
    }

    private static bool TryReadMarker(
        MLeader leader,
        out AutoCadFramedTextAttributeMatrixCase? variant)
    {
        using var buffer = leader.GetXDataForApplication(MatrixRegAppName);
        var marker = buffer?.AsArray()
            .FirstOrDefault(value => value.TypeCode ==
                (int)DxfCode.ExtendedDataAsciiString)
            .Value as string;
        return AutoCadFramedTextAttributeMatrixPolicy.TryParseMarker(
            marker,
            out variant);
    }

    private static string ReadCanonicalStyleName(
        Database database,
        Transaction transaction,
        ObjectId textStyleId)
    {
        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId) ||
            !AutoCadObjectIdAccess.TryGetObject<TextStyleTableRecord>(
                transaction,
                textStyleId,
                OpenMode.ForRead,
                out var record,
                database))
        {
            return "<unreadable>";
        }

        return record!.Name;
    }

    private static string TryReadHandle(ObjectId id)
    {
        try
        {
            return id.IsNull ? "<none>" : id.Handle.ToString();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "<unavailable>";
        }
    }

    private static string Status(
        AutoCadFramedTextAttributeMatrixCheckStatus status) =>
        status switch
        {
            AutoCadFramedTextAttributeMatrixCheckStatus.Pass => "PASS",
            AutoCadFramedTextAttributeMatrixCheckStatus.Fail => "FAIL",
            _ => "NOT TESTED",
        };

    private static string Capability(
        AutoCadFramedTextAttributeMatrixCapabilityStatus status) =>
        status switch
        {
            AutoCadFramedTextAttributeMatrixCapabilityStatus.Supported =>
                "SUPPORTED",
            AutoCadFramedTextAttributeMatrixCapabilityStatus
                    .NotSupportedByTestedPaths =>
                "NOT SUPPORTED BY TESTED PATHS",
            AutoCadFramedTextAttributeMatrixCapabilityStatus.NotTested =>
                "NOT TESTED",
            _ => "INCONCLUSIVE",
        };

    private static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value.HasValue ? Format(value.Value) : "<invalid>";

    private sealed record MatrixSetup(
        ObjectId BlockId,
        ObjectId AttributeDefinitionId,
        AutoCadFramedTextAttributeDefinitionAuditSnapshot Definition,
        AutoCadTextStyleCatalogEntry Style,
        bool DistinctStyleOverrideExpected);

    private sealed record MatrixDefinitionCheckpoint(
        string Label,
        AutoCadFramedTextAttributeDefinitionAuditSnapshot? Snapshot,
        string? Error);
}
#endif
