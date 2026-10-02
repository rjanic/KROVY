using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using System.Text.Json;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Portable semantics and adapter contracts; AutoCAD HOST retest remains separate.</summary>
public sealed class RoofStructuralMirrorCloneTests
{
    [Theory]
    [InlineData("MIRROR")]
    [InlineData("_.MIRROR")]
    public void UnlockedMirror_UsesSharedManualCloneClaim(string command)
    {
        Assert.True(RoofStructuralEditRules.HasFirstClaimOpportunity(command));
        Assert.True(RoofStructuralEditRules.RequiresAssemblySnapshotCapture(command));
        Assert.Equal(RoofStructuralNativeAction.AcceptManualClone,
            RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));
        Assert.False(RoofStructuralEditRules.IsCloneRejectCommand(command));
        Assert.False(RoofStructuralEditRules.IsPlanRestoreCommand(command));
        Assert.Equal(RoofStructuralCommandDisposition.Supported,
            RoofStructuralEditRules.GetDisposition(command));
        Assert.Equal(RoofStructuralAttachedManualCreationKind.Mirror,
            RoofStructuralEditRules.CreationKindForCommand(command));
        Assert.Equal(RoofStructuralNativeAction.RejectClone,
            RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.AcceptManualClone,
            RoofStructuralEditRules.Classify(command, true, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RejectClone,
            RoofStructuralEditRules.Classify(command, true, RoofEditState.Locked));
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, 0d)]
    [InlineData(RoofStructuralRole.Hip, 90d)]
    [InlineData(RoofStructuralRole.Hip, 37d)]
    [InlineData(RoofStructuralRole.Valley, 0d)]
    [InlineData(RoofStructuralRole.Valley, 90d)]
    [InlineData(RoofStructuralRole.Valley, 37d)]
    public void MirrorClone_ReflectsPhysicalFrameAndPreservesIndependentIdentity(
        RoofStructuralRole role, double normalDegrees)
    {
        var sourceKey = new RoofStructuralLogicalKey(role, 1, 2);
        var generated = new[] { sourceKey, new RoofStructuralLogicalKey(role, 2, 3) };
        var originalKeys = generated.ToArray();
        var plan = new RoofSegment3D(new(35351.85656854813, 10951.930768028607, 0),
            new(38351.85656854813, 13951.930768028607, 0));
        var frame = SourceFrame(plan);
        var originalFrame = frame with { };
        var angle = normalDegrees * Math.PI / 180d;
        var nx = Math.Cos(angle);
        var ny = Math.Sin(angle);
        var mirrorOrigin = new RoofPoint3D(20000, 12000, 0);
        RoofPoint3D Reflect(RoofPoint3D point)
        {
            var distance = (point.X - mirrorOrigin.X) * nx + (point.Y - mirrorOrigin.Y) * ny;
            return new(point.X - 2 * nx * distance, point.Y - 2 * ny * distance, point.Z);
        }
        var clonePlan = new RoofSegment3D(Reflect(plan.Start), Reflect(plan.End));
        Assert.Equal(0d, clonePlan.Start.Z);
        Assert.Equal(0d, clonePlan.End.Z);
        Assert.True(RoofStructuralManualPlacementRules.TryReflectFrame(
            frame, plan, clonePlan, out var reflected));
        Assert.NotNull(reflected);
        AssertPoint(Reflect(new(frame.AxisStartX, frame.AxisStartY, frame.AxisStartZ)),
            new(reflected!.AxisStartX, reflected.AxisStartY, reflected.AxisStartZ));
        AssertPoint(Reflect(new(frame.AxisEndX, frame.AxisEndY, frame.AxisEndZ)),
            new(reflected.AxisEndX, reflected.AxisEndY, reflected.AxisEndZ));
        Assert.Equal(frame.SectionHeightMm, reflected.SectionHeightMm);
        Assert.Equal(originalFrame, frame);

        var created = RoofStructuralAttachedManualDataRules.Create(
            "2912", RoofStructuralAttachedManualIdentityRules.Create(), sourceKey,
            RoofStructuralAttachedManualCreationKind.Mirror, 120,
            RoofStructuralHeightMode.Explicit, 160);
        Assert.True(created.IsValid);
        var persisted = RoofStructuralAttachedManualDataRules.WithPlacement(created.Data!, reflected);
        Assert.True(persisted.IsValid);
        var manual = JsonSerializer.Deserialize<RoofStructuralAttachedManualData>(
            JsonSerializer.Serialize(persisted.Data));
        Assert.NotNull(manual);
        Assert.Equal(reflected, manual!.Placement);
        Assert.Equal(sourceKey, manual.SourceLogicalKey);
        Assert.Equal(RoofStructuralAttachedManualCreationKind.Mirror, manual.CreationKind);
        var second = RoofStructuralAttachedManualIdentityRules.Create();
        Assert.NotEqual(manual.ManualIdentity, second);
        var physicalKey = RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity);
        Assert.StartsWith("ManualStructural:", physicalKey);
        Assert.NotEqual(sourceKey.ToString(), physicalKey);
        Assert.Equal(originalKeys, generated);
        Assert.Equal(generated.Length + 1,
            generated.Select(k => k.ToString()).Append(physicalKey).Distinct().Count());

        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, 120, frame, out var originalBody, out var reason), reason);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, manual.WidthMm, manual.Placement!, out var mirroredBody, out reason), reason);
        Assert.Equal(role, mirroredBody!.Geometry.Role);
        Assert.Equal(originalBody!.Geometry.WidthMm, mirroredBody.Geometry.WidthMm);
        for (var i = 0; i < 8; i++)
            AssertPoint(Reflect(originalBody.ConvexHalves[0].SourcePrismVertices[i]),
                mirroredBody.ConvexHalves[0].SourcePrismVertices[i]);
        // Repeated rebuild uses the persisted reflected frame; it cannot reflect twice.
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, manual.WidthMm, manual.Placement!, out var rebuilt, out reason), reason);
        Assert.Equal(mirroredBody.ConvexHalves[0].SourcePrismVertices,
            rebuilt!.ConvexHalves[0].SourcePrismVertices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MirrorParallelToSource_ReflectsSectionEvenWhenPlanDirectionIsUnchanged(bool onPlane)
    {
        var plan = new RoofSegment3D(new(100, 200, 0), new(1100, 200, 0));
        var clonePlan = onPlane ? plan : new RoofSegment3D(new(100, 800, 0), new(1100, 800, 0));
        var frame = new RoofStructuralManualPlacement(100, 210, 1200, 1100, 210, 1600,
            0, 1, 0, 0, 0, 1, 160);
        Assert.True(RoofStructuralManualPlacementRules.TryReflectFrame(frame, plan, clonePlan, out var reflected));
        Assert.Equal(onPlane ? 190d : 790d, reflected!.AxisStartY, 6);
        Assert.Equal(-1d, reflected.SideY, 6);
        Assert.Equal(frame.AxisStartZ, reflected.AxisStartZ);
        Assert.Equal(frame.UpZ, reflected.UpZ);
    }

    [Fact]
    public void MirrorRejectsLengthChangeNonPlanarNonFiniteAndGlideReflection()
    {
        var plan = new RoofSegment3D(new(100, 200, 0), new(1100, 200, 0));
        var frame = SourceFrame(plan);
        var invalid = new[]
        {
            new RoofSegment3D(new(100, 800, 0), new(1110, 800, 0)),
            new RoofSegment3D(new(100, 800, 1), new(1100, 800, 0)),
            new RoofSegment3D(new(double.NaN, 800, 0), new(1100, 800, 0)),
            new RoofSegment3D(new(100, 800, 0), new(double.PositiveInfinity, 800, 0)),
            new RoofSegment3D(new(100, 800, 0), new(100, 800, 0)),
            // Reflection across a horizontal line plus a tangential translation is not MIRROR.
            new RoofSegment3D(new(300, 800, 0), new(1300, 800, 0)),
        };
        foreach (var clone in invalid)
        {
            Assert.False(RoofStructuralManualPlacementRules.TryReflectFrame(frame, plan, clone, out var result));
            Assert.Null(result);
        }
    }

    [Fact]
    public void MirrorAdapter_UsesExistingAtomicConversionRebuildAndGroupVerification()
    {
        var router = Read("RoofStructuralNativeEditService");
        var convert = RoofUxSourceContractText.Member(router,
            "private static bool TryConvertCloneToAttachedManual", "private static bool VerifyCommitted");
        Assert.Contains("RoofStructuralAttachedManualIdentityRules.Create()", convert);
        Assert.Contains("sourceKey", convert);
        Assert.Contains("creationKind", convert);
        Assert.Contains("WriteReplacingGenerated(line, transaction, created.Data)", convert);
        Assert.Contains("generatedAfter.Data is not null", convert);
        Assert.Contains("manualAfter.Data is null", convert);
        Assert.Contains("line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0)", convert);
        Assert.Contains("line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0)", convert);
        Assert.Contains("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction", router);
        Assert.Contains("TrySyncForOwner(document, transaction, ownerId)", router);
        Assert.Contains("VerifyCommitted(document, ownerId, commandName, manualizedCloneIds)", router);
        Assert.Contains("physicalKeys.SetEquals(keys)", router);
        var snapshot = router.IndexOf("var before = snapshot.Assembly.TimberLines", StringComparison.Ordinal);
        var sourceGuard = router.IndexOf("before is not null)", snapshot, StringComparison.Ordinal);
        var sourcePolicy = router.IndexOf("action = candidate.Manual", sourceGuard, StringComparison.Ordinal);
        var clone = router.IndexOf("// Appended structural clone carrying inherited Generated LogicalKey", sourcePolicy, StringComparison.Ordinal);
        Assert.True(snapshot < sourceGuard && sourceGuard < sourcePolicy && sourcePolicy < clone);
        Assert.Contains("RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName)", router[snapshot..sourceGuard]);
        Assert.Contains("? RoofStructuralNativeAction.AcceptPlan : RoofStructuralNativeAction.RejectClone", router[sourcePolicy..clone]);

        var adapter = Read("RoofStructuralRafterSolidMaterializationService");
        var placement = RoofUxSourceContractText.Member(adapter,
            "private static bool TryResolveManualPlacement", "private static Solid3d CreateSolid");
        Assert.Contains("manual.CreationKind == RoofStructuralAttachedManualCreationKind.Mirror", placement);
        Assert.Contains("TryReflectFrame(frame, sourcePlan, copiedPlan, out placement)", placement);
        Assert.Contains("TryMatchRigidPlanCopy", placement);
        Assert.Contains("RoofStructuralManualPlacementRules.Translate(frame, dx, dy, 0)", placement);
        Assert.Contains("RoofStructuralAttachedManualDataRules.WithPlacement(manual, placement)", placement);
        Assert.Contains("RoofStructuralAttachedManualStore.Write(plan, transaction, persisted.Data)", placement);
        Assert.Contains("if (manual.Placement is { } stored)", placement);
        Assert.Contains("RoofStructuralManualPlacementRules.TryBuildPrism", adapter);
        Assert.Contains("RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity)", adapter);
        Assert.Contains("RigidMirror", placement);
    }

    private static RoofStructuralManualPlacement SourceFrame(RoofSegment3D plan)
    {
        var start = new RoofPoint3D(plan.Start.X - 30, plan.Start.Y - 30, 1200);
        var end = new RoofPoint3D(plan.End.X + 60, plan.End.Y + 60, 2600);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        var xyLength = Math.Sqrt(dx * dx + dy * dy);
        var length = Math.Sqrt(xyLength * xyLength + dz * dz);
        return new(start.X, start.Y, start.Z, end.X, end.Y, end.Z,
            -dy / xyLength, dx / xyLength, 0,
            -dx / xyLength * dz / length, -dy / xyLength * dz / length, xyLength / length, 160);
    }

    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(actual.DistanceTo(expected), 0d, 1e-6);

    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");
}
