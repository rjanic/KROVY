#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadFullLabelProofCommands
{
    [CommandMethod("AK_DEV_FULLLABEL_TEXT_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadFullLabelProofService.Create();

    [CommandMethod("AK_DEV_FULLLABEL_TEXT_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadFullLabelProofService.Verify();

    [CommandMethod("AK_DEV_FULLLABEL_TEXT_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFullLabelProofService.Clean();
}
#endif
