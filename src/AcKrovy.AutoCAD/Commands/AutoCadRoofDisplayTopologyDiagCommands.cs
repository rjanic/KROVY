#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>DEBUG-only roof display / GROUP topology diagnostics.</summary>
public sealed class AutoCadRoofDisplayTopologyDiagCommands
{
    [CommandMethod("AK_DEV_ROOF_GROUP_TOPOLOGY_DIAG", CommandFlags.Modal)]
    public void GroupTopologyDiag() => RoofDisplayTopologyDiagService.RunGroupTopologyDiag();
}
#endif
