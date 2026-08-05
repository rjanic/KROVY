#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG host verify: Ensure FBC BTRs only (no annotations), print inventory,
/// and confirm Ensure-twice returns the same ObjectId.
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

        var cases = new (string Label, AutoCadFramedBlockContentRequest Request)[]
        {
            ("Plain Combined", CreateRequest(
                TimberFramedBlockContentKind.Plain,
                TimberFramedBlockContentPresentation.Combined,
                styleName,
                textStyleId,
                "1")),
            ("Circle Combined", CreateRequest(
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.Combined,
                styleName,
                textStyleId,
                "12")),
            ("Rectangle Combined", CreateRequest(
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.Combined,
                styleName,
                textStyleId,
                "12")),
            ("Slot Combined", CreateRequest(
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.Combined,
                styleName,
                textStyleId,
                "12")),
            ("Circle ItemOnly", CreateRequest(
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.ItemOnly,
                styleName,
                textStyleId,
                "12")),
            ("Rectangle ItemOnly", CreateRequest(
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.ItemOnly,
                styleName,
                textStyleId,
                "12")),
            ("Slot ItemOnly", CreateRequest(
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.ItemOnly,
                styleName,
                textStyleId,
                "12")),
        };

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
                    first.ResolvedBlockName);
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
        string itemText) =>
        new(
            kind,
            presentation,
            styleName,
            styleName,
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            styleId,
            styleId,
            itemText);

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
