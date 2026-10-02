using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using System.Text.Json;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>In-place manual edit semantics and adapter wiring; not AutoCAD HOST validation.</summary>
public sealed class RoofStructuralManualMirrorEditTests
{
    [Fact]
    public void ExistingManualMirror_PersistsReflectedFrameBeforePhysicalRebuild()
    {
        var router = Read("RoofStructuralNativeEditService");
        var manualBranch = RoofUxSourceContractText.Member(router,
            "if (candidate.Manual)", "if (!expected.TryGetValue(candidate.Key, out var item)");
        var mirror = manualBranch.IndexOf("RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName)", StringComparison.Ordinal);
        Assert.True(mirror >= 0, "Existing Manual MIRROR must update placement at the semantic edit boundary.");
        var reflect = manualBranch.IndexOf("RoofStructuralManualPlacementRules.TryReflectFrame", mirror, StringComparison.Ordinal);
        var persist = manualBranch.IndexOf("RoofStructuralAttachedManualStore.Write(line, transaction, mirrored.Data)", reflect, StringComparison.Ordinal);
        Assert.True(mirror < reflect && reflect < persist);
        Assert.Contains("new(before.Start, before.End)", manualBranch[mirror..persist]);
        Assert.Contains("new(Point(line.StartPoint), Point(line.EndPoint))", manualBranch[mirror..persist]);
        Assert.Contains("RoofStructuralAttachedManualDataRules.WithPlacement(manualData, reflected)", manualBranch);
        var edit = manualBranch[mirror..persist];
        Assert.Contains("line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0)", edit);
        Assert.Contains("line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0)", edit);
        Assert.Contains("before.Start.DistanceTo(Point(line.StartPoint)) > 1e-6", edit);
        Assert.Contains("before.End.DistanceTo(Point(line.EndPoint)) > 1e-6", edit);
        Assert.Contains("Placement: { } frame", edit);
        Assert.DoesNotContain("RoofStructuralAttachedManualIdentityRules.Create", edit);
        Assert.DoesNotContain("TryConvertCloneToAttachedManual", edit);
        Assert.DoesNotContain("Translate", edit);
        var physical = router.IndexOf("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction", StringComparison.Ordinal);
        Assert.True(router.IndexOf("RoofStructuralAttachedManualStore.Write(line, transaction, mirrored.Data)", StringComparison.Ordinal) < physical);
        var group = router.IndexOf("TrySyncForOwner(document, transaction, ownerId)", physical, StringComparison.Ordinal);
        var commit = router.IndexOf("transaction.Commit()", group, StringComparison.Ordinal);
        Assert.True(physical < group && group < commit);
        Assert.Contains("ROOF_STRUCT_MANUAL_MIRROR_PLACEMENT", manualBranch);
        Assert.Contains("placementMode=InPlaceRigidMirror", manualBranch);

        // The builder must consume the persisted frame, without guessing a second transform.
        var builder = Read("RoofStructuralRafterSolidMaterializationService");
        var placement = RoofUxSourceContractText.Member(builder,
            "private static bool TryResolveManualPlacement", "private static Solid3d CreateSolid");
        Assert.Contains("if (manual.Placement is { } stored)", placement);
        Assert.True(placement.IndexOf("placement = stored;", StringComparison.Ordinal) <
            placement.IndexOf("RoofStructuralRafterPolyhedronService.TryBuild", StringComparison.Ordinal));
        Assert.Contains("RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity)", builder);
    }

    [Fact]
    public void MirrorRouting_SeparatesExistingManualEditFromGeneratedCloneIdentity()
    {
        var router = Read("RoofStructuralNativeEditService");
        var guard = RoofUxSourceContractText.Member(router,
            "// Appended Generated/Manual MIRROR creates a new Manual identity", "if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>");
        Assert.Contains("action == RoofStructuralNativeAction.AcceptManualClone", guard);
        Assert.Contains("RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName)", guard);
        Assert.Contains("before is not null)", guard);
        Assert.DoesNotContain("|| candidate.Manual", guard);
        Assert.Contains("action = candidate.Manual", guard);
        Assert.Contains("? RoofStructuralNativeAction.AcceptPlan : RoofStructuralNativeAction.RejectClone", guard);
        Assert.Equal(RoofStructuralNativeAction.RejectClone,
            RoofStructuralEditRules.Classify("MIRROR", false, RoofEditState.Locked));
        var manual = RoofUxSourceContractText.Member(router,
            "if (candidate.Manual)", "if (!expected.TryGetValue(candidate.Key, out var item)");
        var appended = RoofUxSourceContractText.Member(manual,
            "if (before is null)", "if (action == RoofStructuralNativeAction.AcceptManualClone)");
        Assert.Contains("if (before is null)", appended);
        // COPY retains its existing remint/source-claim branches, and Generated MIRROR
        // retains the first fix's conversion function (verified by its own focused suite).
        Assert.Contains("TryEnsureManualCloneIdentity", manual);
        Assert.Contains("// COPY source Manual Structural remains Manual", manual);
        Assert.Contains("TryConvertCloneToAttachedManual", router);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Explicit)]
    public void ExistingManualMirror_HostGeometryUpdatesPlacementWithoutChangingIdentityOrSection(
        RoofStructuralRole role, RoofStructuralAttachedManualCreationKind creationKind, RoofStructuralHeightMode heightMode)
    {
        var before = Manual(role, creationKind, heightMode);
        var original = before.Placement!;
        var beforePlan = BeforePlan();
        var mirroredPlan = MirroredPlan();
        Assert.Equal(0d, mirroredPlan.Start.Z);
        Assert.Equal(0d, mirroredPlan.End.Z);
        Assert.True(RoofStructuralManualPlacementRules.TryReflectFrame(
            original, beforePlan, mirroredPlan, out var reflected));
        Assert.NotNull(reflected);
        var persisted = RoofStructuralAttachedManualDataRules.WithPlacement(before, reflected!);
        Assert.True(persisted.IsValid);
        var after = Assert.IsType<RoofStructuralAttachedManualData>(persisted.Data);
        Assert.Equal(before.ManualIdentity, after.ManualIdentity);
        Assert.Equal(before.SourceLogicalKey, after.SourceLogicalKey);
        Assert.Equal(before.SourceRole, after.SourceRole);
        Assert.Equal(before.WidthMm, after.WidthMm);
        Assert.Equal(before.HeightMode, after.HeightMode);
        Assert.Equal(before.ExplicitHeightMm, after.ExplicitHeightMm);
        Assert.Equal(before.CreationKind, after.CreationKind);
        Assert.Equal(before with { Placement = after.Placement }, after);
        Assert.Equal(original, before.Placement);
        Assert.NotEqual(original, after.Placement);
        Assert.Equal(RoofStructuralAttachedManualIdentityRules.PhysicalKey(before.ManualIdentity),
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(after.ManualIdentity));
        AssertPoint(Reflect(beforePlan.Start), mirroredPlan.Start);
        AssertPoint(Reflect(beforePlan.End), mirroredPlan.End);
        AssertPoint(Reflect(new(original.AxisStartX, original.AxisStartY, original.AxisStartZ)),
            new(reflected!.AxisStartX, reflected.AxisStartY, reflected.AxisStartZ));
        AssertPoint(Reflect(new(original.AxisEndX, original.AxisEndY, original.AxisEndZ)),
            new(reflected.AxisEndX, reflected.AxisEndY, reflected.AxisEndZ));
        Assert.InRange(reflected.AxisStartX, 30633.8288737 - 1e-6, 30633.8288737 + 1e-6);
        Assert.InRange(reflected.AxisEndX, 33720.3887943 - 1e-6, 33720.3887943 + 1e-6);
        Assert.Equal(original.AxisStartZ, reflected.AxisStartZ);
        Assert.Equal(original.AxisEndZ, reflected.AxisEndZ);
        Assert.Equal(-original.SideX, reflected.SideX, 9);
        Assert.Equal(original.SideY, reflected.SideY, 9);

        // Persist/reopen, then rebuild using the same stored placement the adapter consumes.
        var reopened = JsonSerializer.Deserialize<RoofStructuralAttachedManualData>(JsonSerializer.Serialize(after));
        Assert.NotNull(reopened);
        Assert.Equal(after, reopened);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, before.WidthMm, original, out var oldBody, out var failure), failure);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, reopened!.WidthMm, reopened.Placement!, out var rebuilt, out failure), failure);
        var oldPoints = oldBody!.ConvexHalves[0].SourcePrismVertices;
        var newPoints = rebuilt!.ConvexHalves[0].SourcePrismVertices;
        for (var i = 0; i < 8; i++) AssertPoint(Reflect(oldPoints[i]), newPoints[i]);
        Assert.True(newPoints[0].DistanceTo(oldPoints[0]) > 1000d);
        Assert.Equal(role, rebuilt.Geometry.Role);
        Assert.Equal(oldBody.Geometry.WidthMm, rebuilt.Geometry.WidthMm);
        Assert.Equal(oldBody.Geometry.PhysicalVerticalHeightMm, rebuilt.Geometry.PhysicalVerticalHeightMm);
    }

    [Fact]
    public void RepeatedManualMirror_TransformsCurrentPersistedFrameAndPreservesIdentity()
    {
        var before = Manual(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Mirror,
            RoofStructuralHeightMode.Explicit);
        Assert.True(RoofStructuralManualPlacementRules.TryReflectFrame(
            before.Placement!, BeforePlan(), MirroredPlan(), out var first));
        var persisted = RoofStructuralAttachedManualDataRules.WithPlacement(before, first!);
        Assert.True(persisted.IsValid);
        Assert.True(RoofStructuralManualPlacementRules.TryReflectFrame(
            persisted.Data!.Placement!, MirroredPlan(), BeforePlan(), out var second));
        var twice = RoofStructuralAttachedManualDataRules.WithPlacement(persisted.Data, second!);
        Assert.True(twice.IsValid);
        Assert.Equal(before.ManualIdentity, twice.Data!.ManualIdentity);
        Assert.Equal(before.SourceLogicalKey, twice.Data.SourceLogicalKey);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            before.SourceRole, before.WidthMm, before.Placement!, out var oldBody, out var failure), failure);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            twice.Data.SourceRole, twice.Data.WidthMm, twice.Data.Placement!, out var twiceBody, out failure), failure);
        for (var i = 0; i < 8; i++)
            AssertPoint(oldBody!.ConvexHalves[0].SourcePrismVertices[i], twiceBody!.ConvexHalves[0].SourcePrismVertices[i]);
    }

    private static RoofStructuralAttachedManualData Manual(RoofStructuralRole role,
        RoofStructuralAttachedManualCreationKind creationKind, RoofStructuralHeightMode heightMode)
    {
        var frame = new RoofStructuralManualPlacement(
            34035.12031771148, 10908.504361157415, -49.567145124853056,
            30948.56039718791, 13995.064281680987, 1732.4590558593404,
            -1 / Math.Sqrt(2), -1 / Math.Sqrt(2), 0, 0, 0, 1, 160);
        var created = RoofStructuralAttachedManualDataRules.Create(
            "2912", "f3a561b7af1a44dcb8bfd61c99c901a8", new(role, 1, 4),
            creationKind, 120, heightMode, heightMode == RoofStructuralHeightMode.Explicit ? 160 : null, frame);
        Assert.True(created.IsValid);
        return created.Data!;
    }

    private static RoofSegment3D BeforePlan() => new(
        new(33991.69391084029, 10951.930768028607, 0),
        new(30991.693910840288, 13951.930768028607, 0));

    private static RoofSegment3D MirroredPlan() => new(
        new(30677.255280611484, 10951.930768028607, 0),
        new(33677.25528061148, 13951.930768028607, 0));

    // Independently computed WCS vertical mirror plane from the supplied HOST endpoints.
    private static RoofPoint3D Reflect(RoofPoint3D point) => new(64668.94919145177 - point.X, point.Y, point.Z);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(actual.DistanceTo(expected), 0d, 1e-6);

    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");
}
