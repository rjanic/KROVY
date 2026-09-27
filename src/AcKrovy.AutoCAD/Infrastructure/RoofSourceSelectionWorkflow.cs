using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only explicit selection of the native footprint through any roof child.</summary>
internal static class RoofSourceSelectionWorkflow
{
    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        var selected = editor.GetEntity(new PromptEntityOptions(
            UiStrings.GetString("Command_RoofSelectSource_Prompt")));
        if (selected.Status != PromptStatus.OK)
        {
            return;
        }

        ObjectId ownerId;
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var resolution = RoofOwnerSelectionResolver.Resolve(
                document.Database, transaction, selected.ObjectId);
            if (!resolution.IsResolved ||
                transaction.GetObject(resolution.OwnerId, OpenMode.ForRead) is not Polyline owner ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return;
            }

            ownerId = resolution.OwnerId;
        }

        // Select only the original entity. No group/layer/database mutation or grip overrule.
        editor.SetImpliedSelection(new[] { ownerId });
    }
}
