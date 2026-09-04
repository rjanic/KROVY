#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only arming controls for the one-shot INSERT document-lock veto probe.
/// </summary>
public sealed class AutoCadRoofImportDocumentLockVetoCommands
{
    [CommandMethod("AK_DEBUG_INSERT_VETO_ON", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
    public void On()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            RoofImportDocumentLockVetoProbe.ArmDashInsert(document);
        }
    }

    [CommandMethod("AK_DEBUG_INSERT_PALETTE_VETO_ON", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
    public void PaletteOn()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            RoofImportDocumentLockVetoProbe.ArmPaletteInsert(document);
        }
    }

    [CommandMethod("AK_DEBUG_CLASSICINSERT_VETO_ON", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
    public void ClassicOn()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            RoofImportDocumentLockVetoProbe.ArmClassicInsert(document);
        }
    }

    [CommandMethod("AK_DEBUG_INSERT_VETO_OFF", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
    public void Off()
    {
        RoofImportDocumentLockVetoProbe.Disarm(
            AcApplication.DocumentManager.MdiActiveDocument);
    }
}
#endif
