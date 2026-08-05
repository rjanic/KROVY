#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only host verify for immutable FBC BlockTableRecord definitions.
/// Creates BTRs only — no ModelSpace annotations / MLeaders.
/// </summary>
public sealed class AutoCadFramedBlockContentDefinitionVerifyCommands
{
    [CommandMethod("AK_DEV_FBC_DEFINITIONS_VERIFY", CommandFlags.Modal)]
    public void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        AutoCadFramedBlockContentDefinitionVerifyService.Verify(document);
    }
}
#endif
