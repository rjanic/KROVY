using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for DEBUG-only AK_RUNTIME_BUILD identity diagnostics.
/// </summary>
public sealed class AutoCadRuntimeBuildSourceContractTests
{
    private static readonly string Commands = Read(
        "src", "AcKrovy.AutoCAD", "Commands", "AutoCadRuntimeBuildCommands.cs");

    [Fact]
    public void RuntimeBuildCommand_IsDebugOnly()
    {
        var trimmed = Commands.TrimStart();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.Contains("#endif", Commands);
        Assert.Contains("[CommandMethod(\"AK_RUNTIME_BUILD\"", Commands);
    }

    [Fact]
    public void RuntimeBuildCommand_UsesExecutingAssembly_NotExpectedDiskPath()
    {
        Assert.Contains("typeof(AutoCadRuntimeBuildCommands).Assembly", Commands);
        Assert.Contains("BuildInfoCollector.Collect(assembly)", Commands);
        Assert.Contains("DllSha256", Commands);
        Assert.DoesNotContain("bin\\x64\\Debug", Commands);
        Assert.DoesNotContain("bin/x64/Debug", Commands);
        Assert.DoesNotContain("git.exe", Commands, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartTransaction", Commands);
        Assert.DoesNotContain("OpenMode.ForWrite", Commands);
    }

    [Fact]
    public void RuntimeBuildCommand_ExposesAssemblyLocalWholeRoofMirrorMarker()
    {
        Assert.Contains(
            "public const string WholeRoofMirrorRuntimeMarker = \"ROOF_WHOLE_MIRROR_V1\"",
            Commands);
        Assert.Contains("mirrorWholeRoofRuntimeMarker=", Commands);
        Assert.Contains("mirrorWholeRoofDiagnosticToken=", Commands);
        Assert.Contains("KROVY_RUNTIME_BUILD", Commands);
        Assert.Contains("configuration=DEBUG", Commands);
        Assert.Contains("ROOF_WHOLE_MIRROR_DETECT", Commands);
        Assert.Contains("ROOF_WHOLE_MIRROR_REBIND", Commands);
    }

    [Fact]
    public void RuntimeBuildCommand_DoesNotModifyMirrorLifecycle()
    {
        Assert.DoesNotContain("RoofWholeRoofCopyRebindService", Commands);
        Assert.DoesNotContain("RoofMirrorCloneDetachService", Commands);
        Assert.DoesNotContain("LiveGeometrySynchronizationService", Commands);
        Assert.DoesNotContain("ObjectAppended", Commands);
        Assert.DoesNotContain("CommandEnded", Commands);
    }

    private static string Read(params string[] segments) =>
        RoofUxSourceContractText.Read(segments);
}
