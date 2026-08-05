#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only LEFT Combined knee STRETCH diagnostic. No production fix path.
/// </summary>
public sealed class AutoCadFramedBlockContentLeftStretchDiagCommands
{
    [CommandMethod("AK_DEV_FBC_LEFT_STRETCH_DIAG", CommandFlags.Modal)]
    public void Diagnose()
    {
        AutoCadFramedBlockContentLeftStretchDiagService.Run();
    }
}
#endif
