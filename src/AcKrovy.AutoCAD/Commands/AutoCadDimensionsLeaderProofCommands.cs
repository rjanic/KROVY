#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadDimensionsLeaderProofCommands
{
    [CommandMethod("AK_DEV_DIMENSIONS_LEADER_TEXT_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadDimensionsLeaderProofService.Create();

    [CommandMethod("AK_DEV_DIMENSIONS_LEADER_TEXT_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadDimensionsLeaderProofService.Verify();

    [CommandMethod("AK_DEV_DIMENSIONS_LEADER_TEXT_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadDimensionsLeaderProofService.Clean();
}
#endif
