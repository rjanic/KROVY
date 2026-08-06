#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only one-command FBC autotest matrix. No entity picking.
/// </summary>
public sealed class AutoCadFramedBlockContentAutotestCommands
{
    [CommandMethod("AK_DEV_FBC_AUTOTEST_ALL", CommandFlags.Modal)]
    public void RunAll()
    {
        AutoCadFramedBlockContentAutotestService.RunAll();
    }

    [CommandMethod("AK_DEV_FBC_AUTOTEST_CLEAN", CommandFlags.Modal)]
    public void Clean()
    {
        AutoCadFramedBlockContentAutotestService.Clean();
    }
}
#endif
