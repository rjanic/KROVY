#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG Stage D: native grip move + read-only K→D→I / would-normalize report.
/// Production wiring stays OFF.
/// </summary>
public sealed class AutoCadFramedBlockContentGripReadonlyProofCommands
{
    [CommandMethod("AK_DEV_FBC_GRIP_READONLY_SETUP", CommandFlags.Modal)]
    public void Setup() =>
        AutoCadFramedBlockContentGripReadonlyProofService.Setup();

    [CommandMethod("AK_DEV_FBC_GRIP_READONLY_STATUS", CommandFlags.Modal)]
    public void Status() =>
        AutoCadFramedBlockContentGripReadonlyProofService.WriteStatus();

    [CommandMethod("AK_DEV_FBC_GRIP_READONLY_OFF", CommandFlags.Modal)]
    public void Off() =>
        AutoCadFramedBlockContentGripReadonlyProofService.DisableKeepEntities();

    [CommandMethod("AK_DEV_FBC_GRIP_READONLY_CLEAN", CommandFlags.Modal)]
    public void Clean() =>
        AutoCadFramedBlockContentGripReadonlyProofService.Clean();
}
#endif
