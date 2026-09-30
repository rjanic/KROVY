using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Code-path guard only; actual Solid3d and COPY/MIRROR need HOST retest.</summary>
public sealed class RoofOrdinaryRafterSolidSourceContractTests
{
    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return File.ReadAllText(Path.Combine(directory!.FullName,
            "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Fact]
    public void OrdinarySolids_UseExistingPhysicalOwnership_AndStayOutOfRoofSurfaceCompleteness()
    {
        var materialization = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var physical = Read("RoofPhysical3DMaterializationService.cs");
        var lifecycle = Read("RoofPhysical3DLifecycleService.cs");
        var collector = Read("RoofAssemblyGroupMemberCollector.cs");

        Assert.Contains("RoofPhysical3DGeneratedStore.Write(", materialization);
        Assert.Contains("RoofPhysical3DGeneratedRole.OrdinaryRafterSolid", materialization);
        Assert.Contains("MatchesGeneratedMemberKeys(database, transaction, ownerReference, model)",
            materialization);
        Assert.Contains("RoofGeneratedMemberKey.From(generated)", materialization);
        Assert.Contains("actual.SetEquals(expected)", materialization);
        Assert.Contains("modelSpace.AppendEntity(solid)", materialization);
        Assert.Contains("transaction.AddNewlyCreatedDBObject(solid, true)", materialization);
        Assert.Contains("horizontalCut?.SourcePrismVertices", materialization);
        Assert.Contains("horizontalCut.EaveElevationMm", materialization);
        Assert.Contains("solid.Slice(plane, false)", materialization);
        var rebuild = materialization.IndexOf(
            "// The caller owns the transaction", StringComparison.Ordinal);
        var erase = materialization.IndexOf(
            "RoofPhysical3DMaterializationService.EraseOrdinaryRafterSolids(",
            rebuild, StringComparison.Ordinal);
        var create = materialization.IndexOf("foreach (var member in model.Members)",
            erase, StringComparison.Ordinal);
        Assert.True(rebuild >= 0 && erase > rebuild && create > erase);
        Assert.Contains("EraseRoofSurfaceOwned(database, transaction, ownerReference)", physical);
        Assert.Contains("EraseOrdinaryRafterSolids", physical);
        Assert.Contains("data?.Role is RoofPhysical3DGeneratedRole.OrdinaryRafterSolid or", lifecycle);
        Assert.Contains("RoofPhysical3DGeneratedRole.StructuralRafterSolid", lifecycle);
        Assert.Contains("RoofPhysical3DGeneratedRole.OrdinaryRafterSolid", collector);
    }

    [Fact]
    public void GeneratedPlan_UsesZeroElevation_AndPhysicalBuilderUsesReplayPlan()
    {
        var generated = Read("RoofGeneratedRafterSetService.cs");
        var physical = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var command = Read("RoofRafterCommandWorkflow.cs");

        Assert.Contains("var sourceElevation = 0d", generated);
        Assert.Contains("RoofOrdinaryRafterSolidMaterializationService.ReconcileInTransaction(",
            generated);
        Assert.Contains("new RoofAutomaticRafterPhysicalSettings(", physical);
        Assert.Contains("recipe.WidthMm, recipe.HeightMm", physical);
        Assert.Contains("new RoofFaceRafterTransientPreviewController(\n            document,\n            0d)",
            command.Replace("\r\n", "\n"));
        Assert.True(command.IndexOf("if (existingRecipe == editedRecipe", StringComparison.Ordinal) <
            command.IndexOf("TryReplaceWithEditedRecipe(", StringComparison.Ordinal));
        Assert.Contains("TryReconcileExistingInTransaction(", command);
        var settingsOnly = command.Substring(
            command.IndexOf("if (existingRecipe == editedRecipe", StringComparison.Ordinal),
            command.IndexOf("TryReplaceWithEditedRecipe(", StringComparison.Ordinal) -
            command.IndexOf("if (existingRecipe == editedRecipe", StringComparison.Ordinal));
        Assert.Contains("TryReconcileExistingInTransaction(", settingsOnly);
        Assert.DoesNotContain("TryReplaceWithEditedRecipe(", settingsOnly);
    }

    [Fact]
    public void StructuralSideTrim_UsesExistingStructuralIdentityAndWidth_WithoutExtraEntity()
    {
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var physical = Read("RoofPhysical3DMaterializationService.cs");
        var generated = Read("RoofGeneratedRafterSetService.cs");

        Assert.Contains("RoofStructuralEdgeIdentityResolver.Resolve(hip, provenance)", ordinary);
        Assert.Contains("RoofBoundaryIdentityError.CurrentRawWindingMismatch", ordinary);
        Assert.Contains("RoofBoundaryIdentityRules.CreateSequential(", ordinary);
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner(", ordinary);
        Assert.Contains("metadata.TryRead(line, out TimberElementData? timber)", ordinary);
        Assert.Contains("RoofPhysicalElevationRules.ToState(elevation, hip.RiseMm)", ordinary);
        Assert.Contains("if (!structuralReconcilePending)", ordinary);
        Assert.Contains("structuralReconcilePending: true", generated);
        Assert.Contains("item.TimberData.WidthMm", ordinary);
        Assert.Contains("structuralCut.PlanePoint", ordinary);
        Assert.Contains("structuralCut.PlaneNormal", ordinary);
        Assert.Equal(3, ordinary.Split("solid.Slice(plane, false)").Length - 1);
        Assert.Contains("ridgeOverlapCut.RetainedNormal", ordinary);
        Assert.Contains("ridgeOverlapCut?.SourcePrismVertices", ordinary);
        Assert.Equal(2, ordinary.Split("modelSpace.AppendEntity(solid)").Length - 1);
        Assert.Contains("EraseRoofSurfaceOwned(database, transaction, ownerReference)",
            physical);
    }

    [Fact]
    public void AcceptedMoveTrim_RebuildsOnlySameKeyBody_AndFailureRestores2DSnapshot()
    {
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var edit = Read("RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("TryReconcileModifiedMembersInTransaction(", ordinary);
        Assert.Contains("RoofGeneratedMemberKey.From(data)", ordinary);
        Assert.Contains("RoofPhysicalStretchRules.TrySelectRebuildKeys(", ordinary);
        Assert.Contains("changedKeys, collateralIds, out var rebuildKeys", ordinary);
        Assert.Contains("rebuildKeys.Select(PhysicalMemberId)", ordinary);
        Assert.Contains("if (existing.Count != members.Count", ordinary);
        Assert.Contains("RoofPhysical3DGeneratedStore.Write(solid, transaction, old.Data)", ordinary);
        Assert.Contains("oldEntity.Erase()", ordinary);
        Assert.Contains("TryBuildExistingModelInTransaction(", ordinary);
        Assert.Contains("if (RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(globalCommandName))", edit);
        Assert.Contains("TryReconcileModifiedMembersInTransaction(", edit);
        Assert.Contains("catch (OrdinaryPhysicalReconcileException ex)", edit);
        Assert.Contains("RecoverFailedOrdinaryPhysicalReconcile(document, ownerId, ex)", edit);
        Assert.Contains("TryRecoverGeneratedMembersOnly(", edit);
    }

    [Fact]
    public void AcceptedGeneratedErase_RemovesOnlySuppressedKeyBody_WithoutOrphans()
    {
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var edit = Read("RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("newlySuppressedKeys.Add(key)", edit);
        Assert.Contains("RoofGeneratedMemberOverride.Suppress(key, elementId)", edit);
        Assert.Contains("TryRemoveSuppressedMembersInTransaction(", edit);
        Assert.Contains("ordinaryPhysicalSuppressed", edit);
        Assert.Contains("suppressedKeys.Select(PhysicalMemberId)", ordinary);
        Assert.Contains("activeIds.Concat(suppressedIds).ToHashSet()", ordinary);
        Assert.Contains("physical.Keys.ToHashSet().SetEquals(expectedBefore)", ordinary);
        Assert.Contains("RoofAssemblyGroupSyncService.DetachMembersBeforeErase(", ordinary);
        Assert.Contains("solid.Erase()", ordinary);
        Assert.DoesNotContain("EraseRoofSurfaceOwned", ordinary.Substring(
            ordinary.IndexOf("public static bool TryRemoveSuppressedMembersInTransaction(",
                StringComparison.Ordinal),
            ordinary.IndexOf("public static bool TryReconcileExistingInTransaction(",
                StringComparison.Ordinal) -
            ordinary.IndexOf("public static bool TryRemoveSuppressedMembersInTransaction(",
                StringComparison.Ordinal)));
    }
}
