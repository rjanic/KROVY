#if DEBUG
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only, READ-ONLY proof that a Generated member receives at most ONE Entity.XData
/// assignment across its creation transaction. Traces any post-atomic identity-sync XData
/// rewrite (the suspected REDO loss point) and emits a per-batch summary. No DB access on
/// the U/UNDO/REDO/MREDO boundary (this only runs inside the genuine creation transaction).
/// </summary>
internal static class RoofGeneratedPostAtomicWriteDiag
{
    private static int _syncWriteCount;

    public static void ResetBatch() => _syncWriteCount = 0;

    public static void TraceSyncWrite(Entity entity)
    {
        if (entity is null || entity.ObjectId.IsNull)
        {
            return;
        }

        try
        {
            if (RoofGeneratedTimberStore.Read(entity).Data is null)
            {
                return;
            }

            _syncWriteCount++;
        }
        catch
        {
        }
    }

    public static void EmitSummary(int generatedCount)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager
                .MdiActiveDocument?
                .Editor;
            editor?.WriteMessage(
                "\nROOF_GENERATED_POST_ATOMIC_SUMMARY" +
                " generated=" + generatedCount +
                " singleSetter=" + (generatedCount - _syncWriteCount) +
                " multiSetter=" + _syncWriteCount +
                " identitySyncWrites=" + _syncWriteCount +
                " result=" + (_syncWriteCount == 0 ? "ok" : "warning"));
        }
        catch
        {
        }
    }
}
#endif
