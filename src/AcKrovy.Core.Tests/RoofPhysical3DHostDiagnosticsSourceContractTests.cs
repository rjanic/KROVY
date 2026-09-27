using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Guards diagnostic isolation; does not assert HOST clone behavior.</summary>
public sealed class RoofPhysical3DHostDiagnosticsSourceContractTests
{
    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, path));
    }

    [Fact]
    public void DiagnosticObservers_AreReadOnlyAndExcludeUndoRedoCommands()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.StartsWith("#if DEBUG", code);
        Assert.DoesNotContain("OpenMode.ForWrite", code);
        Assert.DoesNotContain(".Commit()", code);
        Assert.DoesNotContain(".Erase(", code);
        Assert.DoesNotContain(".TransformBy(", code);
        Assert.DoesNotContain(".XData =", code);
        Assert.DoesNotContain(".Visible =", code);
        Assert.Contains("_command is \"COPY\" or \"MIRROR\" or \"ERASE\" or \"AK_ROOF_EDIT\"", code);
        Assert.Contains("if (!Observe) return", code);
        Assert.Contains("sourcePhysical=", code);
        Assert.Contains("clonePhysical=", code);
        Assert.Contains("Generation", Read("src/AcKrovy.Core/Models/Roofs/RoofPhysical3DGeneratedData.cs"));
    }

    [Fact]
    public void NativeSnapshotObserver_RegistersBeforeProductionMaintenance()
    {
        var entry = Read("src/AcKrovy.AutoCAD/PluginEntry.cs");
        Assert.True(entry.IndexOf("RoofPhysical3DHostDiagnostics.Start()", StringComparison.Ordinal) <
            entry.IndexOf("LiveGeometrySynchronizationService.Start()", StringComparison.Ordinal));
        Assert.Contains("RoofPhysical3DHostDiagnostics.Stop()", entry);
        var materialization = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DMaterializationService.cs");
        Assert.Contains("ownerReference, \"before\"", materialization);
        Assert.Contains("ownerReference, \"after-create\"", materialization);
    }
}
