using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Targeted per-element AK_RECALC pipeline for accepted generated-member edits.
/// Numbering uses <see cref="TimberElementItemIdentityService"/> /
/// <see cref="AcKrovy.Core.Services.TimberElementItemNumbering"/>; annotations
/// use <see cref="ElementLabelService.UpdateInCurrentTransaction"/>.
/// </summary>
internal static class RoofGeneratedMemberTargetedRecalcService
{
    public static bool TryRecalculate(
        Document document,
        Transaction transaction,
        string? globalCommandName,
        string ownerHandle,
        IReadOnlyList<RoofGeneratedMemberRecalcItem> changedItems,
        out string stage,
        out string reason,
        out string? failHandle,
        out System.Exception? error)
    {
        stage = "synchronize-and-ensure";
        reason = string.Empty;
        failHandle = null;
        error = null;
        if (changedItems.Count == 0)
        {
            return true;
        }

        var changedIds = changedItems.Select(item => item.Id).ToList();
        failHandle = changedItems[0].Handle;
        var numberingTargets = changedItems
            .Where(item => RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
                item.OldSignature,
                item.NewSignature))
            .Select(item => item.Id)
            .ToList();

        try
        {
            stage = numberingTargets.Count > 0
                ? "numbering-synchronize"
                : "annotation-refresh";
            var result = ElementLabelService.UpdateInCurrentTransaction(
                document.Database,
                transaction,
                document.Editor,
                changedIds,
                numberingTargets);
            if (result.Skipped > 0)
            {
                stage = "annotation-refresh";
                reason = "recalc-skipped";
                return false;
            }

            WriteSuccess(
                document,
                globalCommandName,
                ownerHandle,
                changedItems,
                result.NumberingChanges);
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex;
            reason = "targeted-recalc-failure";
            return false;
        }
    }

    private static void WriteSuccess(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        IReadOnlyList<RoofGeneratedMemberRecalcItem> changedItems,
        IReadOnlyList<TimberElementNumberingChange> numberingChanges)
    {
#if DEBUG
        var newNumberById = numberingChanges.ToDictionary(change => change.Id, change => change.ElementId);
        var signatureGroupsChanged = RoofGeneratedMemberRecalcScopeRules.CountAffectedSignatureGroups(
            changedItems.Select(item => new RoofGeneratedMemberSignatureTransition(
                item.OldSignature,
                item.NewSignature)));
        RoofGeneratedMemberManualEditDiag.WriteRecalc(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            changedItems.Count,
            signatureGroupsChanged,
            "ok");

        foreach (var item in changedItems)
        {
            var newNumber = newNumberById.TryGetValue(item.Id, out var assigned)
                ? assigned
                : item.OldElementId;
            if (string.Equals(item.OldElementId, newNumber, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RoofGeneratedMemberManualEditDiag.WriteRecalcItem(
                document.Editor,
                item.Handle,
                RoofGeneratedMemberRecalcScopeRules.FormatSignature(item.OldSignature),
                RoofGeneratedMemberRecalcScopeRules.FormatSignature(item.NewSignature),
                item.OldElementId,
                newNumber);
        }

        foreach (var change in numberingChanges)
        {
            if (changedItems.Any(item => item.Id == change.Id))
            {
                continue;
            }

            RoofGeneratedMemberManualEditDiag.WriteRecalcItem(
                document.Editor,
                change.Handle,
                "-",
                "-",
                change.PreviousElementId,
                change.ElementId);
        }
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = changedItems;
        _ = numberingChanges;
#endif
    }
}

internal sealed record RoofGeneratedMemberRecalcItem(
    ObjectId Id,
    string Handle,
    string OldElementId,
    TimberElementSignature OldSignature,
    TimberElementSignature NewSignature);
