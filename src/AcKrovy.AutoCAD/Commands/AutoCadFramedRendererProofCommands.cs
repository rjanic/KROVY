#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

public sealed class AutoCadFramedRendererProofCommands
{
    [CommandMethod("AK_DEV_FRAMED_RENDERER_CREATE", CommandFlags.Modal)]
    public void Create() => AutoCadFramedRendererProofService.Create();

    [CommandMethod("AK_DEV_FRAMED_RENDERER_VERIFY", CommandFlags.Modal)]
    public void Verify() => AutoCadFramedRendererProofService.Verify();

    [CommandMethod("AK_DEV_FRAMED_RENDERER_CLEAN", CommandFlags.Modal)]
    public void Clean() => AutoCadFramedRendererProofService.Clean();
}
#endif
