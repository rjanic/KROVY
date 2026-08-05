#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only geometric dogleg normalize for Combined BlockContent MLeaders.
/// </summary>
public sealed class AutoCadFramedBlockContentNormalizeDoglegCommands
{
    [CommandMethod("AK_DEV_FBC_NORMALIZE_DOGLEG", CommandFlags.Modal)]
    public void Normalize()
    {
        AutoCadFramedBlockContentNormalizeDoglegService.Run();
    }
}
#endif
