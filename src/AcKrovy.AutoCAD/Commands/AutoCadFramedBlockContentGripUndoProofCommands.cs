#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG P4B grip-scoped undo proof commands. Default OFF after NETLOAD.
/// Production lifecycle wiring stays OFF.
/// </summary>
public sealed class AutoCadFramedBlockContentGripUndoProofCommands
{
    [CommandMethod("AK_DEV_FBC_UNDO_PROOF_SETUP", CommandFlags.Modal)]
    public void Setup() =>
        AutoCadFramedBlockContentGripUndoProofService.Setup();

    [CommandMethod("AK_DEV_FBC_UNDO_PROOF_STATUS", CommandFlags.Modal)]
    public void Status() =>
        AutoCadFramedBlockContentGripUndoProofService.WriteStatus();

    [CommandMethod("AK_DEV_FBC_UNDO_PROOF_OFF", CommandFlags.Modal)]
    public void Off() =>
        AutoCadFramedBlockContentGripUndoProofService.DisableProofKeepEntities();

    [CommandMethod("AK_DEV_FBC_UNDO_PROOF_CLEAN", CommandFlags.Modal)]
    public void Clean() =>
        AutoCadFramedBlockContentGripUndoProofService.Clean();
}
#endif
