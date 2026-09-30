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
        Assert.Contains("_command is \"COPY\" or \"MIRROR\" or \"ERASE\" or", code);
        Assert.Contains("\"MOVE\" or \"TRIM\" or \"EXTEND\" or \"BREAK\"", code);
        Assert.Contains("\"GRIP_STRETCH\" or \"AK_ROOF_EDIT\"", code);
        Assert.Contains("NATIVE_BEGIN command=", code);
        Assert.Contains("LiveGeometryCommandRules.NormalizeCommandName(e.GlobalCommandName)", code);
        Assert.Contains("TRACE_COMMAND raw=", code);
        Assert.Contains("if (Observe) Write(Document, $\"NATIVE_BEGIN command=", code);
        Assert.Contains("NATIVE_CANCEL_OR_FAIL command=", code);
        Assert.Contains("if (!Observe) return", code);
        Assert.Contains("document.Database.ObjectAppended += Appended", code);
        Assert.Contains("document.Database.ObjectModified += Modified", code);
        Assert.Contains("document.Database.ObjectErased += Erased", code);
        Assert.Contains("NATIVE_END command=", code);
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

    [Fact]
    public void GripStructuralRestoreFailure_ReportsWhichReadOnlyProbeFailed()
    {
        var recovery = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryService.cs");
        Assert.Contains("detail: \"open-or-line-type-failure\"", recovery);
        Assert.Contains("detail: $\"element-id-mismatch:snapshot=", recovery);
        Assert.Contains("TryRestoreStructuralHipValleyMembersOnly", recovery);
        Assert.Contains("database, transaction, ownerHandle, timber, \"open-for-write\"", recovery);
        Assert.Contains("database, transaction, ownerHandle, timber, \"element-id-check\"", recovery);
        var diagnostic = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("geometryMatchesSnapshot=", diagnostic);
        Assert.Contains("snapshotElementId=", diagnostic);
        Assert.Contains("liveElementId=", diagnostic);
        Assert.Contains("readProbeFailed=", diagnostic);
    }

    [Fact]
    public void MemberEvidence_IncludesPlanRolesNativeClonePairsAndPersistedFallback()
    {
        var diagnostic = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("PLAN_MEMBER", diagnostic);
        Assert.Contains("NATIVE_MEMBER command=", diagnostic);
        Assert.Contains("MEMBER_MAP command=", diagnostic);
        Assert.Contains("generated is null && attached is null", diagnostic);
        var manual = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofGeneratedMemberManualEditDiag.cs");
        var recovery = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofUnsupportedStretchRecoveryDiag.cs");
        Assert.StartsWith("#if DEBUG", manual);
        Assert.StartsWith("#if DEBUG", recovery);
        Assert.Contains("AcKrovyDiagnostics.Info(\"ROOF_MANUAL_EDIT_TRACE\", line)", manual);
        Assert.Contains("AcKrovyDiagnostics.Info(FallbackPrefix, line)", recovery);
    }
}
