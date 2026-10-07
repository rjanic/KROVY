using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Runs the final native grip/transient refresh after CommandEnded has unwound.</summary>
internal static class RoofOrdinaryGripRollbackRefreshService
{
    public static IReadOnlyList<ObjectId> CaptureSelection(Editor editor)
    {
        try
        {
            var result = editor.SelectImplied();
            return result.Status == PromptStatus.OK && result.Value is not null
                ? result.Value.GetObjectIds()
                : Array.Empty<ObjectId>();
        }
        catch
        {
            return Array.Empty<ObjectId>();
        }
    }

    public static void Schedule(
        Document document,
        IReadOnlyList<ObjectId> selectionBefore,
        IReadOnlyList<ObjectId> affectedEntityIds,
        IReadOnlyList<ObjectId> lineIds)
        => ScheduleCore(document, selectionBefore, affectedEntityIds, lineIds,
            representation: "Plan2D", solidIds: Array.Empty<ObjectId>());

    public static void SchedulePhysical3D(
        Document document,
        IReadOnlyList<ObjectId> selectionBefore,
        IReadOnlyList<ObjectId> affectedEntityIds,
        IReadOnlyList<ObjectId> lineIds,
        IReadOnlyList<ObjectId> solidIds)
        => ScheduleCore(document, selectionBefore, affectedEntityIds, lineIds,
            representation: "Physical3D", solidIds: solidIds);

    private static void ScheduleCore(
        Document document,
        IReadOnlyList<ObjectId> selectionBefore,
        IReadOnlyList<ObjectId> affectedEntityIds,
        IReadOnlyList<ObjectId> lineIds,
        string representation,
        IReadOnlyList<ObjectId> solidIds)
    {
        EventHandler? idleHandler = null;
        idleHandler = (_, _) =>
        {
            AcApp.Idle -= idleHandler;
            Execute(document, selectionBefore, affectedEntityIds, lineIds,
                representation, solidIds);
        };
        AcApp.Idle += idleHandler;
    }

    private static void Execute(
        Document document,
        IReadOnlyList<ObjectId> selectionBefore,
        IReadOnlyList<ObjectId> affectedEntityIds,
        IReadOnlyList<ObjectId> lineIds,
        string representation,
        IReadOnlyList<ObjectId> solidIds)
    {
        var graphicsInvalidated = false;
        var deferredRegenExecuted = false;
        var selectionRestored = selectionBefore.Count == 0;
        var screenUpdated = false;
        const bool deferredRegenScheduled = true;
        const bool commandActiveAtRegen = false;
        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in affectedEntityIds.Distinct())
                {
                    if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                            transaction,
                            id,
                            OpenMode.ForWrite,
                            out var entity,
                            document.Database) || entity is null)
                        continue;
                    entity.RecordGraphicsModified(true);
                    graphicsInvalidated = true;
                }

                transaction.Commit();
            }

            if (graphicsInvalidated)
                document.Database.TransactionManager.QueueForGraphicsFlush();

            document.Editor.Regen();
            deferredRegenExecuted = true;

            if (selectionBefore.Count > 0)
            {
                selectionRestored = CaptureSelection(document.Editor).ToHashSet().SetEquals(selectionBefore);
                if (!selectionRestored)
                {
                    document.Editor.SetImpliedSelection(selectionBefore.ToArray());
                    selectionRestored = CaptureSelection(document.Editor).ToHashSet().SetEquals(selectionBefore);
                }
            }

            document.Editor.UpdateScreen();
            screenUpdated = true;
        }
        catch
        {
            screenUpdated = false;
        }

#if DEBUG
        var selectionText = string.Join(",", selectionBefore.Select(id => id.Handle.ToString()));
        var lineText = string.Join(",", lineIds.Select(id => id.Handle.ToString()));
        var solidText = string.Join(",", solidIds.Select(id => id.Handle.ToString()));
        var message = $"representation={representation}" +
            $" line={(string.IsNullOrEmpty(lineText) ? "<none>" : lineText)}" +
            $" solid={(string.IsNullOrEmpty(solidText) ? "<none>" : solidText)}" +
            $" restoredEntityCount={affectedEntityIds.Distinct().Count()}" +
                $" selectionBefore={(string.IsNullOrEmpty(selectionText) ? "<none>" : selectionText)}" +
                " selectionCleared=false" +
                $" selectionRestored={selectionRestored}" +
                $" graphicsInvalidated={graphicsInvalidated}" +
                $" deferredRegenScheduled={deferredRegenScheduled}" +
                $" deferredRegenExecuted={deferredRegenExecuted}" +
                $" commandActiveAtRegen={commandActiveAtRegen}" +
                $" result={(deferredRegenExecuted && !commandActiveAtRegen && graphicsInvalidated && selectionRestored && screenUpdated ? "api-verified" : "failed")}";
        AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info(
            "ROOF_ORDINARY_GRIP_ROLLBACK_REFRESH", message);
        document.Editor.WriteMessage("\nROOF_ORDINARY_GRIP_ROLLBACK_REFRESH " + message);
#endif
    }
}
