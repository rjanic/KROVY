#if DEBUG
using System.Globalization;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only C2 success proof for one committed external import. Arms against the
/// dedicated C2 command and emits machine-readable evidence on CommandEnded.
/// </summary>
internal static class RoofExternalImportC2SuccessProof
{
    internal const string CommandName = AutoCadRoofExternalImportSuccessProofCommand.CommandName;
    internal const string ProofMarker = "ROOF_IMPORT_C2_SUCCESS_PROOF";
    internal const string SummaryMarker = "ROOF_IMPORT_C2_OBJECT_SUMMARY";

    private static Document? _armedDocument;
    private static bool _isArmed;
    private static SuccessProof? _proof;

    internal static void Arm(Document? document)
    {
        Disarm();
        if (document is null)
        {
            return;
        }

        _armedDocument = document;
        _isArmed = true;
        document.CommandEnded += CommandEnded;
        document.CommandCancelled += CommandCancelledOrFailed;
        document.CommandFailed += CommandCancelledOrFailed;
    }

    internal static bool IsArmedFor(Document document) =>
        _isArmed && ReferenceEquals(document, _armedDocument);

    internal static void Record(
        Document document,
        RoofExternalImportOperationFacts operation,
        RoofExternalImportSanitizationResult sanitation,
        bool transactionCommitted,
        bool mappingValidation,
        bool returnedRootValid,
        bool sanitizationValid,
        int dbmodBefore)
    {
        if (!IsArmedFor(document))
        {
            return;
        }

        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(document.Database);
        var table = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var rootPresent = table.Has(operation.Manifest.DestinationBlockName) &&
            table[operation.Manifest.DestinationBlockName] == operation.ReturnedRootId &&
            IsAlive(transaction, operation.ReturnedRootId, out var root) &&
            root is BlockTableRecord aliveRoot &&
            !aliveRoot.IsErased &&
            string.Equals(
                aliveRoot.Name,
                operation.Manifest.DestinationBlockName,
                StringComparison.Ordinal);

        var support = operation.Manifest.SupportBlockTableRecords.ToHashSet();
        var expected = operation.Manifest.ExpectedCloneObjectIds.ToHashSet();
        var erasedAnnotations = sanitation.ErasedAnnotationIds.ToHashSet();
        var pairs = operation.Mapping
            .GroupBy(pair => pair.SourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var required = 0;
        var present = 0;
        var sentinelChecked = 0;
        var supportReused = 0;
        foreach (var sourceId in expected)
        {
            if (!pairs.TryGetValue(sourceId, out var sourcePairs) || sourcePairs.Length != 1)
            {
                continue;
            }

            var pair = sourcePairs[0];
            var isSupport = support.Contains(sourceId);
            if (isSupport && !pair.IsCloned)
            {
                supportReused++;
            }

            if (!RoofExternalImportSuccessRules.IsNewImportedContentCandidate(
                    sourceIsExpectedClone: true,
                    isCloned: pair.IsCloned,
                    isSupportBlockTableRecord: isSupport))
            {
                continue;
            }

            if (erasedAnnotations.Contains(pair.DestinationId))
            {
                continue;
            }

            required++;
            var isSentinel = false;
            var objectIsValid = false;
            var objectIsErased = true;
            try
            {
                objectIsValid = pair.DestinationId.IsValid;
                objectIsErased = pair.DestinationId.IsErased;
                if (objectIsValid && !objectIsErased &&
                    transaction.GetObject(pair.DestinationId, OpenMode.ForRead, true) is
                        BlockBegin or BlockEnd)
                {
                    isSentinel = true;
                    sentinelChecked++;
                }
            }
            catch
            {
                objectIsValid = false;
                objectIsErased = true;
            }

            if (RoofExternalImportSuccessRules.IsRealImportedObjectPresent(
                    objectIsValid,
                    objectIsErased,
                    isSentinel))
            {
                present++;
            }
        }

        var importedObjectsPresent = RoofExternalImportSuccessRules.AreImportedObjectsPresent(
            required,
            present,
            sentinelChecked);

        var topLevelReferencePresent = false;
        var topLevelReferenceTargetsReturnedRoot = false;
        var topLevelReferenceInModelSpace = false;
        var topLevelReferenceOperationBound = false;
        var explicitReference = operation.ExplicitReference;
        if (IsAlive(transaction, explicitReference.Id, out var referenceObject) &&
            referenceObject is BlockReference reference &&
            !reference.IsErased)
        {
            topLevelReferencePresent = true;
            topLevelReferenceTargetsReturnedRoot =
                reference.BlockTableRecord == operation.ReturnedRootId;
            topLevelReferenceInModelSpace = reference.OwnerId == modelSpaceId;
            topLevelReferenceOperationBound =
                explicitReference.ConstructedByCurrentOperation &&
                reference.Handle == explicitReference.Handle &&
                reference.Database == operation.TargetDatabase;
        }

        var supportBtrsValid = support.All(sourceId =>
        {
            if (!pairs.TryGetValue(sourceId, out var sourcePairs) || sourcePairs.Length != 1)
            {
                return false;
            }

            var destination = sourcePairs[0].DestinationId;
            return IsAlive(transaction, destination, out var value) &&
                   value is BlockTableRecord block &&
                   !block.IsErased;
        });

        try
        {
            document.Editor.WriteMessage(
                "\n" + SummaryMarker + " " +
                $"requiredNewContent={required} presentRealContent={present} " +
                $"structuralSentinelsSkipped={sentinelChecked} supportReused={supportReused}");
        }
        catch
        {
            // DEBUG observation must never change the success path.
        }

        _proof = new(
            transactionCommitted,
            mappingValidation,
            returnedRootValid,
            rootPresent,
            importedObjectsPresent,
            topLevelReferencePresent,
            topLevelReferenceTargetsReturnedRoot &&
                topLevelReferenceInModelSpace &&
                topLevelReferenceOperationBound,
            supportBtrsValid || support.Count == 0,
            sanitizationValid,
            dbmodBefore);
    }

    private static void CommandEnded(object? sender, CommandEventArgs e)
    {
        if (!_isArmed ||
            !ReferenceEquals(sender, _armedDocument) ||
            !string.Equals(
                e.GlobalCommandName ?? string.Empty,
                CommandName,
                StringComparison.Ordinal))
        {
            return;
        }

        var document = _armedDocument;
        var proof = _proof;
        Disarm();
        if (document is not null)
        {
            WriteProof(document.Editor, proof);
        }
    }

    private static void CommandCancelledOrFailed(object? sender, CommandEventArgs e)
    {
        if (_isArmed &&
            ReferenceEquals(sender, _armedDocument) &&
            string.Equals(
                e.GlobalCommandName ?? string.Empty,
                CommandName,
                StringComparison.Ordinal))
        {
            Disarm();
        }
    }

    private static void Disarm()
    {
        var document = _armedDocument;
        _isArmed = false;
        _armedDocument = null;
        _proof = null;
        if (document is null)
        {
            return;
        }

        document.CommandEnded -= CommandEnded;
        document.CommandCancelled -= CommandCancelledOrFailed;
        document.CommandFailed -= CommandCancelledOrFailed;
    }

    private static void WriteProof(Editor editor, SuccessProof? proof)
    {
        try
        {
            var dbmodCommandEnded = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"),
                CultureInfo.InvariantCulture);
            if (proof is null)
            {
                editor.WriteMessage(
                    "\n" + ProofMarker + " result=fail reason=proof-missing " +
                    $"dbmodCommandEnded={dbmodCommandEnded}");
                return;
            }

            var dbmodEquivalent = proof.DbmodBefore == dbmodCommandEnded;
            var pass = proof.TransactionCommitted &&
                proof.MappingValidation &&
                proof.ReturnedRootValid &&
                proof.RootPresent &&
                proof.ImportedObjectsPresent &&
                proof.TopLevelReferencePresent &&
                proof.TopLevelReferenceTargetsReturnedRoot &&
                proof.SupportBtrsValid &&
                proof.SanitizationValid;
            editor.WriteMessage(
                "\n" + ProofMarker + " " +
                $"transactionCommitted={Bool(proof.TransactionCommitted)} " +
                $"mappingValidation={Bool(proof.MappingValidation)} " +
                $"returnedRootValid={Bool(proof.ReturnedRootValid)} " +
                $"rootPresent={Bool(proof.RootPresent)} " +
                $"importedObjectsPresent={Bool(proof.ImportedObjectsPresent)} " +
                $"topLevelReferencePresent={Bool(proof.TopLevelReferencePresent)} " +
                $"topLevelReferenceTargetsReturnedRoot={Bool(proof.TopLevelReferenceTargetsReturnedRoot)} " +
                $"supportBtrsValid={Bool(proof.SupportBtrsValid)} " +
                $"sanitizationValid={Bool(proof.SanitizationValid)} " +
                $"dbmodBefore={proof.DbmodBefore} dbmodCommandEnded={dbmodCommandEnded} " +
                $"dbmodEquivalent={Bool(dbmodEquivalent)} " +
                $"result={(pass ? "pass" : "fail")}");
        }
        catch
        {
            // DEBUG observation must never change the success path.
        }
    }

    private static bool IsAlive(Transaction transaction, ObjectId id, out DBObject? value)
    {
        value = null;
        try
        {
            if (id.IsNull || !id.IsValid || id.IsErased)
            {
                return false;
            }

            value = transaction.GetObject(id, OpenMode.ForRead, false);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed record SuccessProof(
        bool TransactionCommitted,
        bool MappingValidation,
        bool ReturnedRootValid,
        bool RootPresent,
        bool ImportedObjectsPresent,
        bool TopLevelReferencePresent,
        bool TopLevelReferenceTargetsReturnedRoot,
        bool SupportBtrsValid,
        bool SanitizationValid,
        int DbmodBefore);
}
#endif
