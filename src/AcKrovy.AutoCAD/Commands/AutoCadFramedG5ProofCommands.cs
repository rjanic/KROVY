#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// Isolated DEBUG host proof for G5 single BlockContent MLeader.
/// Not compiled into Release builds. Not a production renderer.
/// </summary>
public sealed class AutoCadFramedG5ProofCommands
{
    [CommandMethod("AK_DEV_FRAMED_G5_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFramedG5ProofService.Clean();

    [CommandMethod("AK_DEV_FRAMED_G5_MATRIX", CommandFlags.Modal)]
    public void RunMatrix()
    {
        if (AcApplication.DocumentManager.MdiActiveDocument is not null)
        {
            AutoCadFramedG5ProofService.RunMatrix();
        }
    }
}
#endif
