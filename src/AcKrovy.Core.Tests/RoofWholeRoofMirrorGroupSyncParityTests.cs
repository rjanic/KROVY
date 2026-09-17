using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral proof that whole-roof MIRROR group-sync fails until the deep-cloned
/// Hip definition is rewritten to the live mirrored footprint — the same gate that
/// HOST reports as stage=group-sync / staleKrovyDuplicate on the display-only
/// AK_ROOF_* group (owner + ridge/hip/valley display only).
/// </summary>
public sealed class RoofWholeRoofMirrorGroupSyncParityTests
{
    [Fact]
    public void MirroredHip_StrictRestoreFailsUntilUpdateGeometry_ThenMatches()
    {
        var sourceDefinition = CreateHipDefinition(Rectangle());
        var mirroredInput = new RoofFootprintInput(MirrorAcrossHorizontal(Rectangle()), IsClosed: true);
        var mirroredNormalized = RoofFootprintValidator.ValidateWithProvenance(mirroredInput);
        Assert.True(mirroredNormalized.Validation.IsValid);
        var mirroredSolved = RoofGeometrySolver.Solve(new RoofDefinition(
            mirroredNormalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(mirroredSolved.IsValid);
        var mirroredGeometry = Assert.IsType<HipRoofGeometry>(mirroredSolved.Geometry);

        // Deep-cloned XData still carries the pre-mirror RigidFootprint.
        var restoreBefore = RoofDefinitionPersistence.Restore(
            mirroredInput,
            mirroredNormalized.Validation.Footprint!,
            sourceDefinition);
        Assert.False(restoreBefore.IsValid);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, restoreBefore.Error);

        var classify = RoofDefinitionPersistence.Classify(
            mirroredInput,
            mirroredNormalized.Validation.Footprint!,
            sourceDefinition);
        Assert.NotNull(classify.Geometry);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classify.Kind);

        var rewritten = RoofDefinitionPersistence.UpdateGeometry(
            sourceDefinition,
            mirroredInput,
            mirroredGeometry);
        var restoreAfter = RoofDefinitionPersistence.Restore(
            mirroredInput,
            mirroredNormalized.Validation.Footprint!,
            rewritten);
        Assert.True(restoreAfter.IsValid);
        Assert.NotNull(restoreAfter.Geometry);

        // Original source definition remains a strict Restore match on the source footprint.
        var sourceInput = new RoofFootprintInput(Rectangle(), IsClosed: true);
        var sourceNormalized = RoofFootprintValidator.ValidateWithProvenance(sourceInput);
        Assert.True(sourceNormalized.Validation.IsValid);
        var sourceRestore = RoofDefinitionPersistence.Restore(
            sourceInput,
            sourceNormalized.Validation.Footprint!,
            sourceDefinition);
        Assert.True(sourceRestore.IsValid);
    }

    [Fact]
    public void HostComplexX_DisplayOnlyMembershipExpandsToFullCanonicalSet()
    {
        // HOST Complex-X wireframe: 7 ridge + 10 hip + 4 valley = 21 display edges.
        // Rebuild EnsureGroup before timber sync → owner + 21 = 22 (the HOST memberCount).
        var topology = SolveHostComplexX();
        var displayCount =
            topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge) +
            topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip) +
            topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley);
        Assert.Equal(21, displayCount);

        var displayOnly = new List<string> { "Owner" };
        displayOnly.AddRange(Enumerable.Range(0, displayCount).Select(i => $"D{i}"));
        Assert.Equal(22, displayOnly.Count);
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            displayOnly,
            FullExpected(displayCount, ordinary: 56, structural: 12, annotations: 216)));

        var expected = FullExpected(displayCount, ordinary: 56, structural: 12, annotations: 216);
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(displayOnly, expected);
        Assert.Empty(plan.RemoveOnce);
        Assert.Equal(expected.Count - 22, plan.AppendOnce.Count);

        foreach (var add in plan.AppendOnce)
        {
            displayOnly.Add(add);
        }

        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(displayOnly, expected));
        Assert.DoesNotContain(displayOnly, id => id.StartsWith("OldOwner", StringComparison.Ordinal));
    }

    [Fact]
    public void ForeignDuplicateGroup_OverlappingCanonicalMembers_IsNotCanonical()
    {
        // Native MIRROR may deep-clone a second AK_ROOF_* / anonymous group that still
        // holds a subset of the new owner's members. Canonical validation requires the
        // expected set exactly once; a foreign 22-member display group is not canonical.
        var expected = FullExpected(display: 21, ordinary: 10, structural: 4, annotations: 20);
        var foreign = expected.Take(22).ToArray();
        Assert.Equal(22, foreign.Length);
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(foreign, expected));
        Assert.Equal(
            expected.Count - 22,
            RoofAssemblyGroupMembershipRules.CountMissing(foreign, expected));
    }

    [Fact]
    public void OriginalOwnerMembership_UnchangedByMirrorExpansionPlan()
    {
        var originalExpected = FullExpected(display: 21, ordinary: 56, structural: 12, annotations: 216);
        var originalActual = originalExpected.ToList();
        var mirrorDisplayOnly = new List<string> { "MirrorOwner" };
        mirrorDisplayOnly.AddRange(Enumerable.Range(0, 21).Select(i => $"MD{i}"));

        var mirrorExpected = new HashSet<string>(StringComparer.Ordinal)
        {
            "MirrorOwner",
        };
        foreach (var id in Enumerable.Range(0, 21).Select(i => $"MD{i}"))
        {
            mirrorExpected.Add(id);
        }

        foreach (var id in Enumerable.Range(0, 56).Select(i => $"MG{i}"))
        {
            mirrorExpected.Add(id);
        }

        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(
            mirrorDisplayOnly,
            mirrorExpected);
        foreach (var add in plan.AppendOnce)
        {
            mirrorDisplayOnly.Add(add);
        }

        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            originalActual,
            originalExpected));
        Assert.Equal(originalExpected.Count, originalActual.Count);
        Assert.DoesNotContain(originalActual, id => id.StartsWith("Mirror", StringComparison.Ordinal));
        Assert.DoesNotContain(mirrorDisplayOnly, originalExpected.Contains);
    }

    private static HashSet<string> FullExpected(
        int display,
        int ordinary,
        int structural,
        int annotations)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "Owner" };
        for (var i = 0; i < display; i++)
        {
            expected.Add($"D{i}");
        }

        for (var i = 0; i < ordinary; i++)
        {
            expected.Add($"G{i}");
        }

        for (var i = 0; i < structural; i++)
        {
            expected.Add($"S{i}");
        }

        for (var i = 0; i < annotations; i++)
        {
            expected.Add($"A{i}");
        }

        return expected;
    }

    private static RoofTopology SolveHostComplexX()
    {
        var input = new RoofFootprintInput(HostComplexX(), IsClosed: true);
        var solved = RoofTopologySolver.Solve(input, 30d);
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<RoofTopology>(solved.Topology);
    }

    private static RoofDefinitionData CreateHipDefinition(RoofPoint2D[] footprint)
    {
        var input = new RoofFootprintInput(footprint, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid);
        return RoofDefinitionPersistence.Create(
            input,
            normalized.Validation.Footprint!,
            solved.Geometry!);
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] MirrorAcrossHorizontal(RoofPoint2D[] source)
    {
        var axisY = source.Average(point => point.Y);
        return source.Select(point => new RoofPoint2D(point.X, 2d * axisY - point.Y)).ToArray();
    }

    private static RoofPoint2D[] HostComplexX() =>
    [
        new(39543.03572550637, 17861.178862711335),
        new(39543.03572550637, 14904.255448377728),
        new(42773.215027821076, 14904.255448377728),
        new(42773.215027821076, 11621.799274236655),
        new(47523.47865744759, 11621.799274236655),
        new(47523.47865744759, 15202.660605693112),
        new(50997.957058859145, 15202.660605693112),
        new(50997.957058859145, 18620.75548770483),
        new(46872.013823711604, 18620.75548770483),
        new(46872.013823711604, 22472.89416536154),
        new(43370.39098304298, 22472.89416536154),
        new(43370.39098304298, 17861.178862711335),
    ];
}
