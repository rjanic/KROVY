#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadCombinedPlainItemLeaderProofCommands
{
    [CommandMethod("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadCombinedPlainItemLeaderProofService.Create();

    [CommandMethod("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadCombinedPlainItemLeaderProofService.Verify();

    [CommandMethod("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadCombinedPlainItemLeaderProofService.Clean();
}
#endif
