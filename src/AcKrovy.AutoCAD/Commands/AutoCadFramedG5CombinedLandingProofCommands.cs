#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG proof / diagnostics for G5C combined landing harness.
/// Not compiled into Release. Not production.
/// </summary>
public sealed class AutoCadFramedG5CombinedLandingProofCommands
{
    [CommandMethod("AK_DEV_FRAMED_G5C_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFramedG5CombinedLandingProofService.CleanAllProofArtifacts();

    [CommandMethod("AK_DEV_FRAMED_G5_CLEAN_ALL", CommandFlags.Modal)]
    public void CleanAll() => AutoCadFramedG5CombinedLandingProofService.CleanAllProofArtifacts();

    [CommandMethod("AK_DEV_FRAMED_G5C_DIAG", CommandFlags.Modal)]
    public void Diagnose() => AutoCadFramedG5CombinedLandingProofService.DiagnoseModelSpace();

    [CommandMethod("AK_DEV_FRAMED_G5C_REPRO_MIN", CommandFlags.Modal)]
    public void ReproMin() =>
        AutoCadFramedG5CombinedLandingProofService.RunMinimalMultiAttrRepro();

    [CommandMethod("AK_DEV_FRAMED_G5C_MATRIX", CommandFlags.Modal)]
    public void RunMatrix()
    {
        if (AcApplication.DocumentManager.MdiActiveDocument is not null)
        {
            AutoCadFramedG5CombinedLandingProofService.RunMatrix();
        }
    }
}
#endif
