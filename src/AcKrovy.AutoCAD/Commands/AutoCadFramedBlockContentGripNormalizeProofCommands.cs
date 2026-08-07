#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG Stage E: native grip move + in-callback shared dogleg → content-side
/// normalize. Production wiring stays OFF. Host: SETUP → knee cross → STATUS → OFF.
/// REDO preservation is a separate unresolved limitation — not claimed here.
/// </summary>
public sealed class AutoCadFramedBlockContentGripNormalizeProofCommands
{
    [CommandMethod("AK_DEV_FBC_GRIP_NORMALIZE_SETUP", CommandFlags.Modal)]
    public void Setup() =>
        AutoCadFramedBlockContentGripNormalizeProofService.Setup();

    [CommandMethod("AK_DEV_FBC_GRIP_NORMALIZE_STATUS", CommandFlags.Modal)]
    public void Status() =>
        AutoCadFramedBlockContentGripNormalizeProofService.WriteStatus();

    [CommandMethod("AK_DEV_FBC_GRIP_NORMALIZE_OFF", CommandFlags.Modal)]
    public void Off() =>
        AutoCadFramedBlockContentGripNormalizeProofService.DisableKeepEntities();

    [CommandMethod("AK_DEV_FBC_GRIP_NORMALIZE_CLEAN", CommandFlags.Modal)]
    public void Clean() =>
        AutoCadFramedBlockContentGripNormalizeProofService.Clean();
}
#endif
