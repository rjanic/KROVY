#if DEBUG
using System.Diagnostics;
using System.Text;
using AcKrovy.Infrastructure.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>
/// DEBUG-only runtime identity of the actually executing AcKrovy.AutoCAD.dll.
/// Distinguishes a stale NETLOAD from the verified current Debug build before
/// any further whole-roof MIRROR diagnosis.
/// </summary>
public static class AutoCadRuntimeBuildCommands
{
    /// <summary>
    /// Assembly-local compile-time marker proving this Debug build includes the
    /// whole-roof MIRROR implementation. Not derived from scanning source trees.
    /// </summary>
    public const string WholeRoofMirrorRuntimeMarker = "ROOF_WHOLE_MIRROR_V1";

    [CommandMethod("AK_RUNTIME_BUILD", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
    public static void WriteRuntimeBuild()
    {
        var editor = AcApplication.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null)
        {
            return;
        }

        try
        {
            editor.WriteMessage("\n" + FormatRuntimeBuild(typeof(AutoCadRuntimeBuildCommands).Assembly));
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\nKROVY_RUNTIME_BUILD" +
                "\nresult=fail" +
                "\nerror=" + Sanitize(exception.GetType().Name));
        }
    }

    internal static string FormatRuntimeBuild(System.Reflection.Assembly assembly)
    {
        var info = BuildInfoCollector.Collect(assembly);
        using var process = Process.GetCurrentProcess();
        var processPath = SafeProcessPath(process);
        var containsDetect = ContainsUtf16Token(info.AssemblyLocation, "ROOF_WHOLE_MIRROR_DETECT");
        var containsRebind = ContainsUtf16Token(info.AssemblyLocation, "ROOF_WHOLE_MIRROR_REBIND");
        var mirrorTokenPresent = containsDetect && containsRebind;

        var builder = new StringBuilder();
        builder.Append("KROVY_RUNTIME_BUILD");
        builder.Append("\nassemblyLocation=").Append(Token(info.AssemblyLocation));
        builder.Append("\nassemblyLastWriteTimeUtc=").Append(Token(info.DllLastWriteTimeUtc));
        builder.Append("\nassemblyFileLength=").Append(Token(info.DllLengthBytes));
        builder.Append("\nassemblySha256=").Append(Token(info.DllSha256));
        builder.Append("\nassemblyName=").Append(Token(info.AssemblyName));
        builder.Append("\nassemblyVersion=").Append(Token(info.AssemblyVersion));
        builder.Append("\ninformationalVersion=").Append(Token(info.InformationalVersion));
        builder.Append("\nprocessId=").Append(Token(info.ProcessId));
        builder.Append("\nprocessPath=").Append(Token(processPath));
        builder.Append("\nconfiguration=DEBUG");
        builder.Append("\nmirrorWholeRoofRuntimeMarker=").Append(WholeRoofMirrorRuntimeMarker);
        builder.Append("\nmirrorWholeRoofDiagnosticToken=")
            .Append(mirrorTokenPresent ? "true" : "false");
        builder.Append("\nresult=ok");
        return builder.ToString();
    }

    private static string SafeProcessPath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ? BuildInfoCollector.Unavailable : path.Trim();
        }
        catch
        {
            return BuildInfoCollector.Unavailable;
        }
    }

    private static bool ContainsUtf16Token(string assemblyLocation, string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(assemblyLocation) ||
                assemblyLocation == BuildInfoCollector.Unavailable ||
                !System.IO.File.Exists(assemblyLocation))
            {
                return false;
            }

            var bytes = System.IO.File.ReadAllBytes(assemblyLocation);
            var pattern = Encoding.Unicode.GetBytes(token);
            for (var i = 0; i <= bytes.Length - pattern.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (bytes[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
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
            .Replace('\n', ' ');
    }

    private static string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
#endif
