using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCanonicalGroupPersistenceSourceContractTests
{
    private static readonly string Infra = Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure");
    private static readonly string Sync = Read("RoofAssemblyGroupSyncService.cs");
    private static readonly string Group = Read("RoofDisplayGroupService.cs");
    private static readonly string Collector = Read("RoofAssemblyGroupMemberCollector.cs");
    private static readonly string Display = Read("RoofDisplayService.cs");
    private static readonly string Materializer = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string Selectability = Read("RoofDisplayGroupSelectabilityService.cs");
    private static readonly string Lifecycle = Read("LiveGeometrySynchronizationService.cs");

    [Fact]
    public void FullSync_ValidatesCurrentDisplayWithoutLegacySevenMemberGate()
    {
        var sync = RoofUxSourceContractText.Member(
            Sync,
            "public static bool TrySyncForOwner",
            "/// <summary>");

        Assert.Contains("TryCollectCurrentStructuralDisplayChildIds", sync);
        Assert.Contains("RoofDisplayGroupService.EnsureGroup", sync);
        Assert.DoesNotContain("ExpectedStructuralDisplayChildCount", sync);
        Assert.DoesNotContain("HipRoofGeometry", sync);
    }

    [Fact]
    public void DisplaySubset_IsValidatedAgainstRestoredTopologyAndMetadata()
    {
        var collect = RoofUxSourceContractText.Member(
            Display,
            "public static bool TryCollectCurrentStructuralDisplayChildIds",
            "private static List<ScannedDisplayChild>");

        Assert.Contains("TryGetExpectedDisplay(owner", collect);
        Assert.Contains("RoofDisplayValidator.Validate", collect);
        Assert.Contains("validation.IsCurrent", collect);
        Assert.DoesNotContain("ExpectedStructuralDisplayChildCount", collect);
        Assert.DoesNotContain("== 7", collect);
    }

    [Fact]
    public void PermanentMaterialization_AnnotatesThenRequiresFullGroupSyncBeforeCommit()
    {
        var materialize = RoofUxSourceContractText.Member(
            Materializer,
            "private static MaterializationResult MaterializeCore",
            "private sealed record MaterializationResult");
        var annotations = materialize.IndexOf(
            "TimberCreatedElementAnnotationService.EnsureForCreatedElements", StringComparison.Ordinal);
        var sync = materialize.IndexOf(
            "RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);

        Assert.True(annotations >= 0 && sync > annotations);
        Assert.Contains("throw new InvalidOperationException", materialize);
        Assert.Contains("canonical roof assembly group could not be synchronized", materialize);
        Assert.DoesNotContain("transaction.Commit", materialize);
    }

    [Fact]
    public void DisplayRebuild_SynchronizesFullAssemblyFromOnlyItsCurrentDisplaySubset()
    {
        var rebuild = RoofUxSourceContractText.Member(
            Display,
            "public static bool Rebuild",
            "private static List<ObjectId> CollectDisplayIdsToErase");

        Assert.Contains("inspection.ChildIds", rebuild);
        Assert.Contains("newChildIds", rebuild);
        Assert.Equal(2, Count(rebuild, "RoofDisplayGroupService.EnsureGroup("));
        Assert.Contains("RoofAssemblyGroupMemberCollector.TryCollect", Group);
    }

    [Fact]
    public void Collector_PreservesOnlySameOwnerTimberAndBoundAnnotations()
    {
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", Collector);
        Assert.Contains("RoofAttachedManualTimberStore.FindByOwner", Collector);
        Assert.Contains("RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle", Collector);
        Assert.Contains("timberSourceHandles.Contains(sourceHandle)", Collector);
        Assert.DoesNotContain("GeometricExtents", Collector);
        Assert.DoesNotContain("GetBoundingBox", Collector);
    }

    [Fact]
    public void FullAssemblyDiff_RemovesForeignAndStaleMembersAndIsIdempotent()
    {
        var ensure = RoofUxSourceContractText.Member(
            Group,
            "public static void EnsureGroup",
            "private static void VerifyGroupUndoInvariant");

        Assert.Contains("RoofAssemblyGroupMembershipRules.PlanCanonicalization(", ensure);
        Assert.Contains("plan.RemoveOnce", ensure);
        Assert.Contains("plan.AppendOnce", ensure);
        Assert.Contains("group.Remove(removeId)", ensure);
        Assert.Contains("group.Append(addId)", ensure);
        Assert.DoesNotContain("group.Clear()", ensure);
        Assert.Contains("DissociateOwnerFromForeignGroups", ensure);
        Assert.DoesNotContain("toRemove = current.Where(id => !expected.Contains(id))", ensure);
        Assert.DoesNotContain("toAdd = expected.Where(id => !current.Contains(id))", ensure);
    }

    [Fact]
    public void ReopenPath_DiagnosesPersistedMembershipWithoutGroupRepair()
    {
        var reconcile = RoofUxSourceContractText.Member(
            Selectability,
            "public static bool ReconcileAllRoofOwners",
            "private static bool TryApplyForOwner");
        var startup = RoofUxSourceContractText.Member(
            Lifecycle,
            "private void TryReconcileRoofGroupSelectabilityOnce",
            "public void Dispose()");

        Assert.Contains("reopen-before-selectability", reconcile);
        Assert.DoesNotContain("RoofAssemblyGroupSyncService", reconcile + startup);
        Assert.DoesNotContain("EnsureGroup", reconcile + startup);
        Assert.Contains("if (RoofDisplayGroupSelectabilityService.ReconcileAllRoofOwners", startup);
        Assert.Contains("transaction.Commit();", startup);
    }

    [Fact]
    public void Diagnostics_RecordActualCanonicalMemberHandlesAtAllBoundaries()
    {
        Assert.Contains("group.GetAllEntityIds()", Sync);
        Assert.Contains("id.Handle.ToString()", Sync);
        Assert.Contains("ROOF_GROUP_MEMBERS", Sync);
        Assert.Contains("materialize-before-full-sync", Materializer);
        Assert.Contains("materialize-after-full-sync", Materializer);
        Assert.Contains("reopen-before-selectability", Selectability);
        Assert.Contains("\"Generated\"", Sync);
        Assert.Contains("\"Annotation\"", Sync);
        Assert.Contains("\"AttachedManual\"", Sync);
        Assert.Contains("\"Foreign\"", Sync);
    }

    [Theory]
    [InlineData(5, 64, 192, 262)]
    [InlineData(5, 72, 216, 294)]
    public void RectangleHip_CanonicalAssemblyCountIncludesEveryOwnedMember(
        int display,
        int generated,
        int annotations,
        int expected)
    {
        Assert.Equal(expected, 1 + display + generated + annotations);
    }

    [Fact]
    public void GableAndMonopitch_ContinueThroughSharedMaterializationAndSync()
    {
        Assert.Contains("SimpleGableRoofGeometry geometry", Materializer);
        Assert.Contains("IRoofGeometry geometry", Materializer);
        Assert.True(Count(Materializer, "MaterializeCore(") >= 3);
        Assert.DoesNotContain("HipRoofGeometry", Sync + Group + Collector);
    }

    [Fact]
    public void FixAddsNoSecondGroupPickstyleOrStartupMembershipWrite()
    {
        var source = Sync + Group + Collector + Display + Materializer + Selectability + Lifecycle;
        Assert.Equal(1, Count(Group, "GroupNamePrefix = \"AK_ROOF_\""));
        Assert.DoesNotContain("AK_ROOF_DISPLAY", source);
        Assert.DoesNotContain("AK_ROOF_RAFTER", source);
        Assert.DoesNotContain("SetSystemVariable", source);
        Assert.DoesNotContain("TrySyncForOwner", RoofUxSourceContractText.Member(
            Lifecycle,
            "private void TryReconcileRoofGroupSelectabilityOnce",
            "public void Dispose()"));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(Infra, fileName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
