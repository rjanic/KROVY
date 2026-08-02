#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadPlainItemLeaderProofCommands
{
    [CommandMethod("AK_DEV_PLAIN_ITEM_TEXT_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadPlainItemLeaderProofService.Create();

    [CommandMethod("AK_DEV_PLAIN_ITEM_TEXT_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadPlainItemLeaderProofService.Verify();

    [CommandMethod("AK_DEV_PLAIN_ITEM_TEXT_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadPlainItemLeaderProofService.Clean();
}
#endif
