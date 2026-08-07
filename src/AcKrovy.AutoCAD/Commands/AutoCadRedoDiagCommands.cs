#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG observe-only REDO loss diagnostics. Zero DWG writes.
/// </summary>
public sealed class AutoCadRedoDiagCommands
{
    [CommandMethod("AK_DEV_REDO_DIAG_ON", CommandFlags.Modal)]
    public void On() => AutoCadRedoDiagService.Enable();

    [CommandMethod("AK_DEV_REDO_DIAG_STATUS", CommandFlags.Modal)]
    public void Status() => AutoCadRedoDiagService.WriteStatus();

    [CommandMethod("AK_DEV_REDO_DIAG_OFF", CommandFlags.Modal)]
    public void Off() => AutoCadRedoDiagService.Disable();
}

/// <summary>
/// DEBUG grip/overrule registration snapshot for OFF completeness checks.
/// </summary>
public sealed class AutoCadFramedBlockContentGripRegistrationStatusCommands
{
    [CommandMethod("AK_DEV_FBC_GRIP_REGISTRATION_STATUS", CommandFlags.Modal)]
    public void Status() =>
        AutoCadFramedBlockContentGripRegistrationSnapshot.WriteStatus();
}
#endif
