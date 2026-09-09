#if DEBUG
using System.Globalization;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>DEBUG-only pre-command generated-assembly snapshot capture/lookup telemetry.</summary>
internal static class RoofGeneratedSnapshotDiag
{
    public static void WriteCapture(
        Editor? editor,
        string? owner,
        string? command,
        string? roofKind,
        string? editState,
        int sourceVertexCount,
        int normalizedVertexCount,
        string? classifier,
        int generatedCount,
        int annotationCount,
        string action,
        string? reason)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_SNAPSHOT" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" roofKind={Token(roofKind)}" +
            $" editState={Token(editState)}" +
            $" sourceVertexCount={F(sourceVertexCount)}" +
            $" normalizedVertexCount={F(normalizedVertexCount)}" +
            $" classifier={Token(classifier)}" +
            $" generatedCount={F(generatedCount)}" +
            $" annotationCount={F(annotationCount)}" +
            $" action={Token(action)}" +
            $" reason={Token(reason)}";
        Write(editor, line);
    }

    public static void WriteLookup(
        Editor? editor,
        string? owner,
        string? command,
        bool found,
        int snapshotGenerated,
        int snapshotAnnotations,
        string? result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GENERATED_SNAPSHOT_LOOKUP" +
            $" owner={Token(owner)}" +
            $" command={Token(command)}" +
            $" found={(found ? "1" : "0")}" +
            $" snapshotGenerated={F(snapshotGenerated)}" +
            $" snapshotAnnotations={F(snapshotAnnotations)}" +
            $" result={Token(result)}";
        Write(editor, line);
    }

    private static void Write(Editor editor, string line)
    {
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string F(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

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
