#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG host verify: Ensure FBC R2 BTRs only (no annotations), print inventory,
/// and confirm Ensure-twice returns the same ObjectId. Combined cases cover both
/// dimension-column sides without mutating R1 definitions.
/// </summary>
internal static class AutoCadFramedBlockContentDefinitionVerifyService
{
    public static void Verify(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        using var documentLock = document.LockDocument();
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        var textStyleId = database.Textstyle;
        var textStyle = (TextStyleTableRecord)transaction.GetObject(
            textStyleId,
            OpenMode.ForRead);
        var styleName = string.IsNullOrWhiteSpace(textStyle.Name)
            ? "Standard"
            : textStyle.Name;

        var cases = new List<(string Label, AutoCadFramedBlockContentRequest Request)>();
        foreach (var kind in new[]
                 {
                     TimberFramedBlockContentKind.Plain,
                     TimberFramedBlockContentKind.Circle,
                     TimberFramedBlockContentKind.Rectangle,
                     TimberFramedBlockContentKind.Slot,
                 })
        {
            foreach (var side in new[]
                     {
                         TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
                         TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
                     })
            {
                cases.Add((
                    $"{kind} Combined {side}",
                    CreateRequest(
                        kind,
                        TimberFramedBlockContentPresentation.Combined,
                        styleName,
                        textStyleId,
                        kind == TimberFramedBlockContentKind.Plain ? "1" : "12",
                        side)));
            }
        }

        foreach (var kind in new[]
                 {
                     TimberFramedBlockContentKind.Circle,
                     TimberFramedBlockContentKind.Rectangle,
                     TimberFramedBlockContentKind.Slot,
                 })
        {
            cases.Add((
                $"{kind} ItemOnly",
                CreateRequest(
                    kind,
                    TimberFramedBlockContentPresentation.ItemOnly,
                    styleName,
                    textStyleId,
                    "12",
                    dimensionColumnSide: null)));
        }

        editor.WriteMessage("\n=== AK_DEV_FBC_DEFINITIONS_VERIFY ===");
        var allOk = true;
        foreach (var (label, request) in cases)
        {
            var first = AcKrovyFramedBlockContentDefinitionService.Ensure(
                database,
                transaction,
                request);
            var second = AcKrovyFramedBlockContentDefinitionService.Ensure(
                database,
                transaction,
                request);
            var ok = first.Succeeded &&
                second.Succeeded &&
                first.BlockTableRecordId == second.BlockTableRecordId &&
                first.ResolvedBlockName is not null &&
                AutoCadFramedBlockContentPolicy.IsProductionFamilyName(
                    first.ResolvedBlockName) &&
                first.ResolvedBlockName.Contains(
                    "_" + TimberFramedBlockContentVariantRules.FamilyRevisionToken + "_",
                    StringComparison.Ordinal);
            allOk &= ok;
            WriteCase(editor, label, first, second, ok);
        }

        transaction.Commit();
        editor.WriteMessage(
            allOk
                ? "\nFBC definitions verify: PASS"
                : "\nFBC definitions verify: FAIL");
    }

    private static AutoCadFramedBlockContentRequest CreateRequest(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentPresentation presentation,
        string styleName,
        ObjectId styleId,
        string itemText,
        TimberFramedBlockContentDimensionColumnSide? dimensionColumnSide) =>
        new(
            kind,
            presentation,
            styleName,
            styleName,
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            styleId,
            styleId,
            itemText,
            dimensionColumnSide);

    private static void WriteCase(
        Editor editor,
        string label,
        AutoCadFramedBlockContentResult first,
        AutoCadFramedBlockContentResult second,
        bool ok)
    {
        editor.WriteMessage(
            $"\n[{(ok ? "OK" : "FAIL")}] {label}: " +
            $"name={first.ResolvedBlockName ?? "<none>"} " +
            $"kind1={first.Kind} kind2={second.Kind} " +
            $"sameId={first.BlockTableRecordId == second.BlockTableRecordId} " +
            $"reason={first.DiagnosticReason}");
    }
}
#endif
