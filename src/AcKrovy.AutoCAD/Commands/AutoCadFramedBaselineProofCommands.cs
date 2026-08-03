#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadFramedBaselineProofCommands
{
    [CommandMethod("AK_DEV_FRAMED_BASELINE_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadFramedBaselineProofService.Create();

    [CommandMethod("AK_DEV_FRAMED_BASELINE_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadFramedBaselineProofService.Verify();

    [CommandMethod("AK_DEV_FRAMED_BASELINE_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFramedBaselineProofService.Clean();
}
#endif
