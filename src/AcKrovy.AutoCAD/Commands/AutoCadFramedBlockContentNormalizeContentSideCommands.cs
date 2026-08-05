#if DEBUG
using AcKrovy.AutoCAD.Infrastructure;
using Autodesk.AutoCAD.Runtime;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only Combined BlockContent dimension-column side normalize
/// (mirrored R2 BTR swap; same MLeader handle).
/// </summary>
public sealed class AutoCadFramedBlockContentNormalizeContentSideCommands
{
    [CommandMethod("AK_DEV_FBC_NORMALIZE_CONTENT_SIDE", CommandFlags.Modal)]
    public void Normalize()
    {
        AutoCadFramedBlockContentNormalizeContentSideService.Run();
    }
}
#endif
