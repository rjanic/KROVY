#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// Optional DEBUG diagnostics for the production G5 Combined GripOverrule.
/// Does not arm or disable production — production registers on NETLOAD.
/// </summary>
public sealed class AutoCadFramedBlockContentProductionGripNormalizeCommands
{
    [CommandMethod("AK_DEV_FBC_PRODUCTION_GRIP_STATUS", CommandFlags.Modal)]
    public void Status() =>
        AutoCadFramedBlockContentProductionGripNormalizeService.WriteStatus();
}
#endif
