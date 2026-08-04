#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// Disposable AutoCAD 2027 host experiment for v0.23.0 Stage 3.
/// This command class is not compiled into Release builds.
/// </summary>
public sealed class AutoCadFramedTextAttributeProofCommands
{
    [CommandMethod("AK_DEV_TEXTATTR_CREATE", CommandFlags.Modal)]
    public void Create()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.Create(document);
        }
    }

    [CommandMethod("AK_DEV_TEXTATTR_VERIFY", CommandFlags.Modal)]
    public void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.Verify(document);
        }
    }

    [CommandMethod("AK_DEV_FRAMED_ITEM_HEIGHT_CREATE", CommandFlags.Modal)]
    public void CreateFramedItemHeightProof()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.CreateHeight(document);
        }
    }

    [CommandMethod("AK_DEV_FRAMED_ITEM_HEIGHT_VERIFY", CommandFlags.Modal)]
    public void VerifyFramedItemHeightProof()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.VerifyHeight(document);
        }
    }

    [CommandMethod("AK_DEV_FRAMED_ITEM_HEIGHT_CLEAN", CommandFlags.Modal)]
    public void CleanFramedItemHeightProof()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.CleanHeight(document);
        }
    }

    [CommandMethod("AK_DEV_TEXTSTYLE_DIAG", CommandFlags.Modal)]
    public void DiagnoseTextStyles()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeProofService.DiagnoseTextStyles(document);
        }
    }

    [CommandMethod("AK_DEV_TEXTATTR_MATRIX", CommandFlags.Modal)]
    public void RunMatrix()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeMatrixService.Run(document);
        }
    }

    [CommandMethod("AK_DEV_TEXTATTR_MATRIX_CLEAN", CommandFlags.Modal)]
    public void CleanMatrix()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            AutoCadFramedTextAttributeMatrixService.Cleanup(document);
        }
    }
}
#endif
