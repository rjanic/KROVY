#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only host verify for one-MLeader G5 BlockContent create path.
/// Does not wire production label commands (AK_LABELS / AK_LABELSELECTED).
/// </summary>
public sealed class AutoCadFramedBlockContentCreateVerifyCommands
{
    [CommandMethod("AK_DEV_FBC_CREATE_VERIFY", CommandFlags.Modal)]
    public void Verify()
    {
        AutoCadFramedBlockContentCreateVerifyService.Verify();
    }

    [CommandMethod("AK_DEV_FBC_CREATE_CLEAN", CommandFlags.Modal)]
    public void Clean()
    {
        AutoCadFramedBlockContentCreateVerifyService.Clean();
    }
}
#endif
