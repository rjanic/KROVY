using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Guards diagnostic isolation; does not assert HOST clone behavior.</summary>
public sealed class RoofPhysical3DHostDiagnosticsSourceContractTests
{
    [Fact]
    public void StructuralEvidence_IsAutomaticForTheEntireNativeEditFamily()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        var observation = code[code.IndexOf("private bool ObserveMemberCheckpoint", StringComparison.Ordinal)..
            code.IndexOf("private Dictionary<string, (string Owner", StringComparison.Ordinal)];
        foreach (var command in new[] { "MOVE", "TRIM", "EXTEND", "STRETCH", "GRIP_STRETCH",
                     "ERASE", "COPY", "MIRROR", "BREAK", "BREAKATPOINT" })
            Assert.Contains("\"" + command + "\"", observation);
        Assert.DoesNotContain("Enabled", observation);
        Assert.DoesNotContain("UNDO", observation);
        Assert.DoesNotContain("REDO", observation);
        var maintenance = code[code.IndexOf("public static void MaintenanceComplete", StringComparison.Ordinal)..
            code.IndexOf("public static void TimberRestoreFailure", StringComparison.Ordinal)];
        Assert.True(maintenance.IndexOf("IsUndoRedoCommand(command)", StringComparison.Ordinal) <
            maintenance.IndexOf("tracker.ReportMemberCheckpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void StructuralEvidence_CapturesBothRepresentationsAndRoofContext()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        var checkpoint = code[code.IndexOf("private Dictionary<string, MemberEvidence> CaptureMemberEvidence", StringComparison.Ordinal)..];
        Assert.Contains("RoofStructuralGeneratedStore.Read(line).Data", checkpoint);
        Assert.Contains("\"Structural:\" + structural.LogicalKey", checkpoint);
        Assert.Contains("\"PhysicalStructural:\" + physical.StructuralId", checkpoint);
        Assert.Contains("physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid", checkpoint);
        Assert.Contains("JsonSerializer.Serialize(new { generated, attached, structural, structuralManual })", checkpoint);
        Assert.Contains("RoofStructuralAttachedManualStore.Read(line).Data", checkpoint);
        Assert.Contains("RoofStructuralAttachedManualIdentityRules.PhysicalKey(structuralManual.ManualIdentity)", checkpoint);
        Assert.Contains("Geometry(solid)", checkpoint);
        Assert.Contains("var addedStructuralBodies = current.Where", checkpoint);
        Assert.Contains(".Concat(addedStructuralBodies)", checkpoint);
        Assert.Contains("\"maintenance-created\"", checkpoint);
        Assert.Contains("MEMBER_OWNER_CONTEXT command=", checkpoint);
        Assert.Contains("RoofDefinitionStore.Read(source).Data", checkpoint);
        Assert.Contains("RoofBoundaryIdentityStore.Read(source).Data", checkpoint);
        Assert.Contains("RoofPhysicalElevationStore.Read(source).Data", checkpoint);
        Assert.DoesNotContain("TryBuild", checkpoint);
        Assert.DoesNotContain("TryReconcile", checkpoint);
    }

    [Fact]
    public void StructuralNativeEvents_DoNotDiscardLaterEraseOrUneraseTransitions()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        var native = code[code.IndexOf("private void NativeEvent", StringComparison.Ordinal)..
            code.IndexOf("private void Mapping", StringComparison.Ordinal)];
        Assert.True(native.IndexOf("STRUCTURAL_NATIVE_EVENT command=", StringComparison.Ordinal) <
            native.IndexOf("_nativeMemberReported.Add", StringComparison.Ordinal));
        Assert.Contains("representation={(structural is not null ? \"Plan2D\" : \"Physical3D\")}", native);
        Assert.Contains("identity={structural?.LogicalKey.ToString() ?? physical!.StructuralId}", native);
        Assert.DoesNotContain("DiagnosticSolidGeometry", native);
        Assert.DoesNotContain("MassProperties", native);
        Assert.Contains("e.Erased ? \"ObjectErased\" : \"ObjectUnerased\"", code);
    }

    [Fact]
    public void StructuralCloneEvidence_UsesOnlyExactNativeMappings()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        var mapping = code[code.IndexOf("private void Mapping", StringComparison.Ordinal)..
            code.IndexOf("private Dictionary<string, MemberEvidence> CaptureMemberEvidence", StringComparison.Ordinal)];
        Assert.Contains("RoofStructuralGeneratedStore.Read(source).Data", mapping);
        Assert.Contains("physical.Data?.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid", mapping);
        Assert.Contains("_memberMappings[cloneHandle] = sourceHandle", mapping);
        Assert.Contains("_ambiguousMemberMappings.Add(cloneHandle)", mapping);
        Assert.DoesNotContain("ElementId", mapping);
        Assert.DoesNotContain("Nearest", mapping);
        Assert.DoesNotContain("Geometry(", mapping);
    }

    [Fact]
    public void StructuralSequence_RetainsAppendEventsBeforeOwnershipBecomesReadable()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("_nativeEntityEvents.Add((_nativeEventCount, kind, entity.Handle.ToString(), entity.GetType().Name))", code);
        Assert.Contains("var structuralHandles = _memberBaseline.Concat(current)", code);
        Assert.Contains("_nativeEntityEvents.Where(item => structuralHandles.Contains(item.Handle))", code);
        Assert.Contains("STRUCTURAL_NATIVE_SEQUENCE command=", code);
        var clear = code[code.IndexOf("private void ClearMemberCheckpoint()", StringComparison.Ordinal)..];
        Assert.Contains("_nativeEntityEvents.Clear()", clear);
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, path));
    }

    [Fact]
    public void BreakAtPoint_IsObservedAtNativeAndMaintenanceCheckpoints()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("ObserveMemberCheckpoint => _command is \"BREAK\" or \"COPY\" or \"MIRROR\" or \"BREAKATPOINT\"", code);
        Assert.Contains("\"BREAK\" or \"BREAKATPOINT\" or \"STRETCH\"", code);
        var maintenance = code[code.IndexOf("public static void MaintenanceComplete", StringComparison.Ordinal)..
            code.IndexOf("public static void TimberRestoreFailure", StringComparison.Ordinal)];
        Assert.Contains("LiveGeometryCommandRules.IsUndoRedoCommand(command)", maintenance);
        Assert.Contains("tracker.ReportMemberCheckpoint(command, \"after-maintenance\")", maintenance);
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
        Assert.Contains("if (Observe || ObserveMemberCheckpoint) Write(Document, $\"NATIVE_BEGIN command=", code);
        Assert.Contains("NATIVE_CANCEL_OR_FAIL command=", code);
        Assert.Contains("if (!Observe && !ObserveMemberCheckpoint) return", code);
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

    [Fact]
    public void MemberCheckpoint_IsAutomaticForRequestedCommandsAndClearsOnCancel()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("ObserveMemberCheckpoint => _command is \"BREAK\" or \"COPY\" or \"MIRROR\"", code);
        Assert.Contains("MEMBER_CHECKPOINT_BEGIN command=", code);
        Assert.Contains("ReportMemberCheckpoint(_command, \"native-ended\")", code);
        Assert.Contains("tracker.ReportMemberCheckpoint(command, \"after-maintenance\")", code);
        Assert.DoesNotContain("ReportMemberOwnerCounts(_memberBaseline.Values.Select(member => member.Owner), \"before-native\")", code);
        Assert.Contains("RoofPhysical3DHostDiagnostics.OwnerCounts(Document, transaction, owner", code);
        Assert.Contains("MEMBER_ROOF_SOURCE phase=", code);
        Assert.Contains("MEMBER_PHYSICAL phase=", code);
        var cancel = code[code.IndexOf("private void Cancelled(", StringComparison.Ordinal)..
            code.IndexOf("CaptureOwnedSolidMass()", code.IndexOf("private void Cancelled(", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("ClearMemberCheckpoint()", cancel);
        Assert.DoesNotContain("ReportMemberCheckpoint(", cancel);
    }

    [Fact]
    public void MemberCheckpoint_UsesExactNativePairsAndReportsMissingOrAmbiguousMapping()
    {
        var code = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofPhysical3DHostDiagnostics.cs");
        Assert.Contains("_memberMappings[cloneHandle] = sourceHandle", code);
        Assert.Contains("_ambiguousMemberMappings.Add(cloneHandle)", code);
        Assert.Contains("\"unmapped-new\"", code);
        Assert.Contains("\"ambiguous-map\"", code);
        Assert.Contains("before={JsonSerializer.Serialize(before)} after={JsonSerializer.Serialize(after)}", code);
        Assert.Contains("timber?.ElementId", code);
        var checkpoint = code[code.IndexOf("private Dictionary<string, MemberEvidence> CaptureMemberEvidence", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Nearest", checkpoint);
        Assert.DoesNotContain("MassProperties", checkpoint);
        Assert.DoesNotContain("GeometricExtents", checkpoint);
    }
}
