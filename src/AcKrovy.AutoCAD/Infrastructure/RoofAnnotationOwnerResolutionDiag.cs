#if DEBUG
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only owner resolution for generated annotation entities (DBText/MLeader/frame).
/// Emits only on miss so LockedAnnotationTamper drop-outs are visible on HOST.
/// </summary>
internal static class RoofAnnotationOwnerResolutionDiag
{
    public static void Write(
        string? handle,
        string? entityType,
        string? sourceHandle,
        string? owner,
        string result,
        string? reason)
    {
        Editor? editor = null;
        try
        {
            editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
        }
        catch
        {
        }

        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ANNOTATION_OWNER_RESOLUTION" +
            $" handle={Token(handle)}" +
            $" entityType={Token(entityType)}" +
            $" sourceHandle={Token(sourceHandle)}" +
            $" owner={Token(owner)}" +
            $" result={Token(result)}" +
            $" reason={Token(reason)}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
#endif
