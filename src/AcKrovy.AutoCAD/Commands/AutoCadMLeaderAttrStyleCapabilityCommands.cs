#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadMLeaderAttrStyleCapabilityCommands
{
    [CommandMethod("AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY", CommandFlags.Modal)]
    public void Run() => AutoCadMLeaderAttrStyleCapabilityService.Run();

    [CommandMethod("AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadMLeaderAttrStyleCapabilityService.Verify();

    [CommandMethod("AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadMLeaderAttrStyleCapabilityService.Clean();
}
#endif
