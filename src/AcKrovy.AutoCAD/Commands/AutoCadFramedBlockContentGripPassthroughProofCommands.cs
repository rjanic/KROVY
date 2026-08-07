#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG Stage A/B: minimal GripOverrule pass-through only. Default OFF after
/// NETLOAD. Production wiring stays OFF. No normalize / dogleg / content-side.
/// </summary>
public sealed class AutoCadFramedBlockContentGripPassthroughProofCommands
{
    [CommandMethod("AK_DEV_FBC_GRIP_PASSTHROUGH_SETUP", CommandFlags.Modal)]
    public void Setup() =>
        AutoCadFramedBlockContentGripPassthroughProofService.Setup();

    [CommandMethod("AK_DEV_FBC_GRIP_PASSTHROUGH_OFF", CommandFlags.Modal)]
    public void Off() =>
        AutoCadFramedBlockContentGripPassthroughProofService.DisableKeepEntities();

    [CommandMethod("AK_DEV_FBC_GRIP_PASSTHROUGH_CLEAN", CommandFlags.Modal)]
    public void Clean() =>
        AutoCadFramedBlockContentGripPassthroughProofService.Clean();
}
#endif
