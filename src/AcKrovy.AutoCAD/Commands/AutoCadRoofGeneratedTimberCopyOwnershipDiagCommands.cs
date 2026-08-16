#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only same-DWG COPY ownership diagnostics for roof-generated rafters.
/// </summary>
public sealed class AutoCadRoofGeneratedTimberCopyOwnershipDiagCommands
{
    [CommandMethod("AK_DEV_ROOF_GENERATED_OWNER_DIAG", CommandFlags.Modal)]
    public void OwnerDiag() => RoofGeneratedTimberCopyOwnershipDiagService.RunOwnerDiag();

    [CommandMethod("AK_DEV_ROOF_RAFTER_REPLACE_DIAG_ON", CommandFlags.Modal)]
    public void ReplaceDiagOn() => RoofGeneratedTimberCopyOwnershipDiagService.EnableReplaceDiag();

    [CommandMethod("AK_DEV_ROOF_RAFTER_REPLACE_DIAG_OFF", CommandFlags.Modal)]
    public void ReplaceDiagOff() => RoofGeneratedTimberCopyOwnershipDiagService.DisableReplaceDiag();
}
#endif
