#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// Disposable AutoCAD 2027 host proof for v0.23.0 Stage 4A. This command
/// class is omitted from Release builds and is not registered in product UI.
/// </summary>
public sealed class AutoCadItemLeaderBlockVariantProofCommands
{
    [CommandMethod("AK_DEV_BLOCKVARIANT_CREATE", CommandFlags.Modal)]
    public void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadItemLeaderBlockVariantProofService.Create(document);
        }
    }

    [CommandMethod("AK_DEV_BLOCKVARIANT_VERIFY", CommandFlags.Modal)]
    public void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadItemLeaderBlockVariantProofService.Verify(document);
        }
    }

    [CommandMethod("AK_DEV_BLOCKVARIANT_CLEAN", CommandFlags.Modal)]
    public void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadItemLeaderBlockVariantProofService.Cleanup(document);
        }
    }
}
#endif
