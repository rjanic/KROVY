using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract regression for Independent Elevation Apply vs shared v2 lazy migration.
/// HOST proved GRIP already migrates; Elevation Apply previously failed with independent_no_build_state.
/// </summary>
public sealed class StructuralMemberElevationIndependentMigrationSourceContractTests
{
    [Fact]
    public void IndependentApply_ReusesSharedTryMigrate_WhenPersistentBuildStateMissing()
    {
        var apply = IndependentApplyBlock();
        Assert.Contains("RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction)", apply);
        Assert.Contains("if (buildState is null)", apply);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate(", apply);
        Assert.Contains("FindIndependentSolid(", apply);
        Assert.Contains("decision: \"independent_no_build_state\"", apply);
        Assert.Contains("independent_applied_after_migration", apply);
        Assert.Contains("migrationPersisted=True", apply);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, acceptedState)", apply);
        Assert.Contains("StructuralMemberElevationStore.Write(line, transaction, requested)", apply);
        Assert.Contains("sectionFrameCaptured=True", apply);
        Assert.Contains("SynchronizeAnnotationsAfterAcceptedElevation(document, transaction, line)", apply);
        Assert.Contains("annotationsSynced=True", apply);
        // Must not invent a second migration stack.
        Assert.DoesNotContain("TryRecoverRecipe", apply);
        Assert.DoesNotContain("ManualOverride", apply);
        Assert.DoesNotContain("AttachedManual", apply);
        Assert.DoesNotContain("Suppressed", apply);
    }

    [Fact]
    public void IndependentApply_PreservesIndependentMemberId_AndOnlyPersistsAfterSuccess()
    {
        var apply = IndependentApplyBlock();
        Assert.Contains("independentIdBefore", apply);
        Assert.Contains("independentIdAfter", apply);
        Assert.Contains("decision: \"independent_id_changed\"", apply);
        var migrateIndex = apply.IndexOf("TryMigrate(", StringComparison.Ordinal);
        var persistIndex = apply.IndexOf("PhysicalStateService.Persist(", StringComparison.Ordinal);
        var commitIndex = apply.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(migrateIndex >= 0 && persistIndex > migrateIndex && commitIndex > persistIndex);

        // Failed migration returns BuildStateUnavailable before any elevation/build-state write.
        var afterMigrate = apply[migrateIndex..];
        var migrationFailReturn = afterMigrate.IndexOf(
            "return ApplyResult.BuildStateUnavailable;", StringComparison.Ordinal);
        Assert.True(migrationFailReturn >= 0);
        var beforeFirstFailReturn = afterMigrate[..migrationFailReturn];
        Assert.DoesNotContain("StructuralMemberElevationStore.Write", beforeFirstFailReturn);
        Assert.DoesNotContain("PhysicalStateService.Persist", beforeFirstFailReturn);
        Assert.DoesNotContain("transaction.Commit()", beforeFirstFailReturn);
    }

    [Fact]
    public void IndependentApply_SecondPassPrefersPersistentBuildStateWithoutRemigration()
    {
        var apply = IndependentApplyBlock();
        // Read persistent first; migrate only inside null branch.
        var readIndex = apply.IndexOf("RoofOrdinaryPhysicalBuildStateStore.Read", StringComparison.Ordinal);
        var nullBranch = apply.IndexOf("if (buildState is null)", StringComparison.Ordinal);
        var migrateIndex = apply.IndexOf("TryMigrate(", StringComparison.Ordinal);
        Assert.True(readIndex >= 0 && nullBranch > readIndex && migrateIndex > nullBranch);
        Assert.Contains("buildStateSource=persistent_member_xrecord", apply);
        Assert.Contains("buildStateSource=migrated_member_package", apply);
    }

    [Fact]
    public void AkEdit_SuccessfulElevationOnly_CountsAsModified()
    {
        var edit = Member(Commands, "public void Edit()", "public void FlipSlopeDirection()");
        Assert.Contains("elevationLifecycleSucceeded", edit);
        Assert.Contains("if (elevationLifecycleSucceeded && changed == 0)", edit);
        Assert.Contains("changed = 1;", edit);
        Assert.Contains("Command_Edit_ResultFormat", edit);
        // Elevation-only early path also prints ResultFormat with count 1.
        var elevationOnly = edit.IndexOf(
            "if (elevationLifecycleSucceeded)\r\n            {\r\n                // Elevation-only Apply",
            StringComparison.Ordinal);
        if (elevationOnly < 0)
        {
            elevationOnly = edit.IndexOf(
                "if (elevationLifecycleSucceeded)\n            {\n                // Elevation-only Apply",
                StringComparison.Ordinal);
        }
        Assert.True(elevationOnly >= 0);
        var earlyReturn = edit.IndexOf("Command_Edit_NoChanges", elevationOnly, StringComparison.Ordinal);
        Assert.True(earlyReturn > elevationOnly);
        var earlyBlock = edit[elevationOnly..earlyReturn];
        Assert.Contains("Command_Edit_ResultFormat", earlyBlock);
        Assert.Contains("1,", earlyBlock);
    }

    [Fact]
    public void AkEdit_FailedElevationLifecycle_IsNotCountedAsModified()
    {
        var edit = Member(Commands, "public void Edit()", "public void FlipSlopeDirection()");
        Assert.Contains("elevationLifecycleSucceeded", edit);
        Assert.Contains("return;", edit);
        Assert.Contains("RoofOrdinaryElevation_Failed", edit);
        Assert.Contains("UserCancelledDetach", edit);
        // Failed result must abort before timber patch loop / upravené accounting.
        var failReturn = edit.IndexOf("RoofOrdinaryElevation_Failed", StringComparison.Ordinal);
        var patchLoop = edit.IndexOf("foreach (var id in ids)", StringComparison.Ordinal);
        Assert.True(failReturn >= 0 && patchLoop > failReturn);
        var between = edit[failReturn..patchLoop];
        Assert.Contains("return;", between);
        Assert.DoesNotContain("changed++", between);
        Assert.DoesNotContain("changed = 1;", between);
    }

    [Fact]
    public void ElevationApply_RefreshesSlopeAnnotationViaSharedEnsureForElement_AfterPersist()
    {
        var service = Read("RoofOrdinaryElevationLifecycleService.cs");
        var sync = Member(service,
            "private static void SynchronizeAnnotationsAfterAcceptedElevation(",
            "private static IReadOnlyList<ObjectId> FindAnnotationsOwnedByLine(");
        Assert.Contains("TimberAnnotationService.EnsureForElement(", sync);
        Assert.Contains("TimberElementItemIdentityService.SynchronizeElementIdsDetailed(", sync);
        Assert.Contains("SlopeDegrees", sync); // diag uses synced timber slope
        Assert.DoesNotContain("SlopeAnnotationService.EnsureForElement(", sync); // via TimberAnnotationService only

        var independent = IndependentApplyBlock();
        var persistIndex = independent.IndexOf(
            "PhysicalStateService.Persist(line, transaction, acceptedState)", StringComparison.Ordinal);
        var syncIndex = independent.IndexOf(
            "SynchronizeAnnotationsAfterAcceptedElevation(document, transaction, line)", StringComparison.Ordinal);
        var commitIndex = independent.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(persistIndex >= 0 && syncIndex > persistIndex && commitIndex > syncIndex);

        // AUTO detach path must also sync annotations after Persist.
        var autoPersist = service.IndexOf(
            "decision: \"detached_and_rebuilt\"", StringComparison.Ordinal);
        Assert.True(autoPersist > 0);
        var beforeAutoCommit = service[
            service.LastIndexOf("StructuralMemberElevationStore.Write", autoPersist, StringComparison.Ordinal)..
            autoPersist];
        Assert.Contains("SynchronizeAnnotationsAfterAcceptedElevation", beforeAutoCommit);

        // Must reuse GRIP's shared TimberAnnotationService path — not invent a parallel Elevation annotator.
        Assert.Contains("TimberAnnotationService.EnsureForElement(",
            Read("RoofOrdinaryGripLifecycleService.cs"));
    }

    [Theory]
    [InlineData(50d)]
    [InlineData(35d)]
    public void ElevationApply_UpdatesTimberSlopeDegreesMetadata_ToTargetPitch(double targetSlopeDegrees)
    {
        // CAD-neutral contract: after Elevation patch + TryBuild, authoritative slope
        // for annotation is AbsolutePitchDegrees(target). The AutoCAD adapter must
        // write that into timber metadata before EnsureForElement (source-contract above).
        var planLen = 3000d;
        var elevation = targetSlopeDegrees >= 0
            ? StructuralMemberElevationRules.CreateSloped(0d, planLen * Math.Tan(targetSlopeDegrees * Math.PI / 180d))
            : StructuralMemberElevationRules.CreateSloped(
                planLen * Math.Tan(Math.Abs(targetSlopeDegrees) * Math.PI / 180d), 0d);
        var expected = StructuralMemberElevationRules.AbsolutePitchDegrees(targetSlopeDegrees);
        Assert.Equal(expected,
            StructuralMemberElevationRules.AbsolutePitchDegrees(
                StructuralMemberElevationRules.DeriveSlopeDegrees(
                    elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, planLen)),
            0.05);

        var rebuild = Member(
            Read("RoofOrdinaryElevationLifecycleService.cs"),
            "private static bool RebuildPhysical(",
            "private static void SynchronizeAnnotationsAfterAcceptedElevation(");
        Assert.Contains("SlopeDegrees = targetPitch", rebuild);
        Assert.Contains("IsSlopeDirectionReversed = fallReversed", rebuild);
        Assert.Contains("ResolveIsSlopeDirectionReversedForDownhill", rebuild);
    }

    [Fact]
    public void GripLegacyCapture_StillUsesSameSharedTryMigrate()
    {
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        var capture = grip[grip.IndexOf("public static Snapshot Capture", StringComparison.Ordinal)..
            grip.IndexOf("public static IReadOnlyCollection<ObjectId> Process", StringComparison.Ordinal)];
        Assert.Contains("else if (independent is not null)", capture);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate", capture);
        Assert.DoesNotContain("PhysicalStateService.Persist", capture);
    }

    [Fact]
    public void LengthenAndRotate_StillRouteThroughMigratedBuildStateFlag()
    {
        var lengthen = Read("RoofOrdinaryLengthenLifecycleService.cs");
        var rotate = Read("RoofOrdinaryRotateLifecycleService.cs");
        Assert.Contains("BuildStateMigrated", lengthen);
        Assert.Contains("migrated_member_package", lengthen);
        Assert.Contains("BuildStateMigrated", rotate);
        Assert.Contains("migrated_member_package", rotate);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate",
            Read("RoofOrdinaryGripLifecycleService.cs"));
    }

    [Fact]
    public void ElevationApply_DoesNotMovePlan2DOrChangePlanZ()
    {
        var service = Read("RoofOrdinaryElevationLifecycleService.cs");
        Assert.Contains("Plan2D Z is never modified", service);
        Assert.Contains("Plan2D invariant: the Line's StartPoint and EndPoint are never moved", service);
        var rebuild = Member(service, "private static bool RebuildPhysical(",
            "private static void SynchronizeAnnotationsAfterAcceptedElevation(");
        Assert.DoesNotContain("line.StartPoint", rebuild);
        Assert.DoesNotContain("line.EndPoint", rebuild);
        Assert.DoesNotContain(".Z =", rebuild);
    }

    [Fact]
    public void SharedMigrationRules_StillExposeMeasuredMemberStructuralCutsVerified()
    {
        var rules = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofOrdinaryPhysicalBuildStateMigrationRules.cs"));
        Assert.Contains("MeasuredMemberStructuralCutsVerified", rules);
        Assert.Contains("MatchesCurrentBody", rules);
    }

    private static string IndependentApplyBlock()
    {
        var service = Read("RoofOrdinaryElevationLifecycleService.cs");
        var start = service.IndexOf("// Independent member: apply directly", StringComparison.Ordinal);
        var end = service.IndexOf("return ApplyResult.NotOrdinaryRafter;", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return service[start..end];
    }

    private static string Commands => ReadFrom("Commands", "AcKrovyCommands.cs");

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, endMarker);
        return source[start..end];
    }

    private static string Read(string file) =>
        ReadFrom("Infrastructure", file);

    private static string ReadFrom(string folder, string file)
    {
        return File.ReadAllText(Path.Combine(RepoRoot(), "src", "AcKrovy.AutoCAD", folder, file));
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }
}
