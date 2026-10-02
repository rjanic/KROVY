using System.Text.Json;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>Runs the production recovery phase service with in-memory ports and real
/// Core builders. Native DWG transactions, notifications and MLeader APIs require HOST.</summary>
public sealed class RoofStructuralLockedRecoveryServiceTests
{
    [Theory]
    [InlineData("MOVE", false)]
    [InlineData("COPY", false)]
    [InlineData("MIRROR", false)]
    [InlineData("MIRROR", true)]
    [InlineData("ERASE", false)]
    [InlineData("STRETCH", false)]
    [InlineData("GRIP_STRETCH", false)]
    [InlineData("TRIM", false)]
    [InlineData("EXTEND", false)]
    public void ManualStructural_LockedEditRestoresStoredIdentityPlacementAndBody(string command, bool eraseSource)
    {
        var fixture = new RecoveryFixture(manual: true);
        var beforeIdentity = fixture.Manual;
        var beforeState = JsonSerializer.Serialize(fixture.GeneratedState);
        var beforeBody = fixture.Body.ToArray();
        var beforeAnnotations = fixture.Annotations.ToArray();
        fixture.ApplyNative(command, eraseSource);
        Assert.True(fixture.Complete());
        Assert.Equal(fixture.SnapshotPlan, fixture.Plan);
        Assert.False(fixture.PlanErased);
        Assert.False(fixture.CloneExists);
        Assert.Equal(beforeIdentity, fixture.Manual);
        Assert.Equal(beforeIdentity!.ManualIdentity, fixture.Manual!.ManualIdentity);
        Assert.Equal(beforeIdentity.Placement, fixture.Manual.Placement);
        Assert.Equal(beforeState, JsonSerializer.Serialize(fixture.GeneratedState));
        Assert.False(RoofStructuralEditRules.Get(fixture.GeneratedState, fixture.Key).Suppressed);
        Assert.Equal(beforeBody, fixture.Body);
        Assert.Equal(beforeAnnotations, fixture.Annotations);
        AssertCanonicalAnnotationsAndGroup(fixture);
        Assert.Equal(new[] { "plan", "physical", "annotations", "group" }, fixture.Phases);
    }

    [Theory]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    public void GeneratedStructural_LockedGeometryRecoveryPreservesExistingEditState(string command)
    {
        var fixture = new RecoveryFixture(manual: false);
        var beforeState = JsonSerializer.Serialize(fixture.GeneratedState);
        var beforeBody = fixture.Body.ToArray();
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked));
        fixture.ApplyNative(command, false);
        Assert.True(fixture.Complete());
        Assert.Equal(fixture.SnapshotPlan, fixture.Plan);
        Assert.Equal(beforeState, JsonSerializer.Serialize(fixture.GeneratedState));
        Assert.False(RoofStructuralEditRules.Get(fixture.GeneratedState, fixture.Key).Suppressed);
        Assert.Equal(beforeBody, fixture.Body);
        AssertCanonicalAnnotationsAndGroup(fixture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedPhase_StopsBeforeLaterWritesOrSuccessfulCompletion(int failurePhase)
    {
        var calls = new List<int>();
        bool Run(int phase) { calls.Add(phase); return phase != failurePhase; }
        Assert.False(RoofStructuralLockedRecoveryCompletion.TryCompleteStructuralRecovery(
            true, () => Run(0), () => Run(1), () => Run(2), () => Run(3)));
        Assert.Equal(Enumerable.Range(0, failurePhase + 1), calls);
    }

    [Fact]
    public void UnaffectedOrUnlockedRecovery_SkipsStructuralReconcile()
    {
        var calls = new List<string>();
        Assert.True(RoofStructuralLockedRecoveryCompletion.TryCompleteStructuralRecovery(false,
            () => { calls.Add("plan"); return true; },
            () => throw new InvalidOperationException("Unrelated structural reconcile"),
            () => { calls.Add("annotations"); return true; },
            () => { calls.Add("group"); return true; }));
        Assert.Equal(new[] { "plan", "annotations", "group" }, calls);
    }

    [Fact]
    public void PlanOnlyRecovery_ReproducesPhysicalDivergenceUntilStructuralCompletionRuns()
    {
        var fixture = new RecoveryFixture(manual: true);
        var canonicalBody = fixture.Body.ToArray();
        fixture.ApplyNative("GRIP_STRETCH", false);
        Assert.True(fixture.Complete(structuralRecovery: false));
        Assert.Equal(fixture.SnapshotPlan, fixture.Plan);
        Assert.NotEqual(canonicalBody, fixture.Body.ToArray());
        Assert.Equal(new[] { "plan", "annotations", "group" }, fixture.Phases);
        fixture.Phases.Clear();
        Assert.True(fixture.Complete());
        Assert.Equal(canonicalBody, fixture.Body);
    }

    [Fact]
    public void RepeatedLockedRecovery_LeavesOneAnnotationSetAndPhysicalBodyInGroup()
    {
        var fixture = new RecoveryFixture(manual: true);
        var before = fixture.Body.ToArray();
        fixture.ApplyNative("STRETCH", false);
        Assert.True(fixture.Complete());
        fixture.Phases.Clear();
        Assert.True(fixture.Complete());
        Assert.Equal(before, fixture.Body);
        AssertCanonicalAnnotationsAndGroup(fixture);
        Assert.Single(fixture.Group, member => member == "physical:canonical");
    }

    private static void AssertCanonicalAnnotationsAndGroup(RecoveryFixture fixture)
    {
        Assert.Equal(3, fixture.Annotations.Count);
        Assert.All(fixture.Annotations, annotation => Assert.Equal("A1", annotation.SourceHandle));
        Assert.Equal(3, fixture.Annotations.Select(annotation => annotation.Role).Distinct().Count());
        Assert.Contains(fixture.Annotations, annotation => annotation.Role == "dimension" && annotation.Text.Contains("120x160"));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(fixture.Group,
            new[] { "owner", "plan:A1", "physical:canonical", "dimension", "arrow", "angle" }));
    }

    private sealed class RecoveryFixture
    {
        private readonly RoofStructuralRafterPolyhedron _canonical;
        private readonly RoofSegment3D _automaticPlan;
        public readonly RoofStructuralLogicalKey Key;
        public readonly RoofSegment3D SnapshotPlan;
        public RoofStructuralAttachedManualData? Manual;
        public RoofStructuralEditState GeneratedState;
        public RoofSegment3D Plan;
        public bool PlanErased;
        public bool CloneExists;
        public IReadOnlyList<RoofPoint3D> Body;
        public List<(string SourceHandle, string Role, string Text)> Annotations = new();
        public List<string> Group = new();
        public List<string> Phases = new();

        public RecoveryFixture(bool manual)
        {
            var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
                new[] { new RoofPoint2D(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }, true));
            var roof = (HipRoofGeometry)RoofGeometrySolver.Solve(new(footprint.Footprint!, new(30), RoofKind.Hip)).Geometry!;
            var topology = roof.Topology;
            var faces = RoofFaceRafterLayoutService.Create(topology, 600).Layout!;
            Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var plan));
            var sources = topology.Edges.Select((edge, index) => (edge, index))
                .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
                .Select(item => new RoofStructuralRafterTrimSource(item.index, RoofRafterBoundaryRole.Hip,
                    topology.Segment(item.edge), 120)).ToArray();
            Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("roof", topology, faces, plan,
                3000, 80, 125, new(), null, sources, out var ordinary, out var failure), failure);
            var edgeIndex = sources.First(source => ordinary!.Members.Any(member => member.StructuralCut?.TopologyEdgeIndex == source.TopologyEdgeIndex)).TopologyEdgeIndex;
            var edge = topology.Edges[edgeIndex];
            var boundaryIds = edge.FaceIndices.Select(index => topology.Faces[index].SourceEdgeIndex + 1).OrderBy(id => id).ToArray();
            Key = new(RoofStructuralRole.Hip, boundaryIds[0], boundaryIds[1]);
            _automaticPlan = topology.Segment(edge);
            var resolved = new ResolvedRoofStructuralEdge(Key, edgeIndex, _automaticPlan, IsPhysicalFoldTimberEligible: true);
            Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(new(topology, resolved,
                3000, 120, RoofStructuralHeightMode.Explicit, 160, ordinary!.Members), out var canonical, out failure), failure);
            _canonical = canonical!;
            GeneratedState = RoofStructuralEditRules.Upsert(RoofStructuralEditState.Empty,
                RoofStructuralEditRules.Get(RoofStructuralEditState.Empty, Key) with { OffsetXmm = 250, OffsetYmm = -70 });
            if (manual)
            {
                Assert.True(RoofStructuralManualPlacementRules.TryCaptureFrame(_canonical, out var placement));
                placement = RoofStructuralManualPlacementRules.Translate(placement!, 4000, 1000, 0);
                Manual = RoofStructuralAttachedManualDataRules.Create("2912", "f3a561b7af1a44dcb8bfd61c99c901a8",
                    Key, RoofStructuralAttachedManualCreationKind.Mirror, 120, RoofStructuralHeightMode.Explicit, 160, placement).Data!;
            }
            var dx = manual ? 4000 : 250;
            var dy = manual ? 1000 : -70;
            Plan = SnapshotPlan = new(new(_automaticPlan.Start.X + dx, _automaticPlan.Start.Y + dy, 0),
                new(_automaticPlan.End.X + dx, _automaticPlan.End.Y + dy, 0));
            Body = BuildBody();
            Assert.True(Complete());
            Phases.Clear();
        }

        public void ApplyNative(string command, bool eraseSource)
        {
            var action = RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked);
            if (command is "COPY" or "MIRROR") CloneExists = !eraseSource;
            if (command == "ERASE") PlanErased = true;
            if (command != "COPY" && !(command == "MIRROR" && !eraseSource))
                Plan = Plan with { End = new(Plan.End.X + 800, Plan.End.Y - 250, 75) };
            Assert.True(action is RoofStructuralNativeAction.RestorePlan or RoofStructuralNativeAction.RejectClone or RoofStructuralNativeAction.Unclaimed);
            if (command != "COPY" && !(command == "MIRROR" && !eraseSource))
                Body = Body.Select(point => new RoofPoint3D(point.X + 800, point.Y - 250, point.Z + 75)).ToArray();
            Group.Add("native:collateral");
        }

        public bool Complete(bool structuralRecovery = true) => RoofStructuralLockedRecoveryCompletion.TryCompleteStructuralRecovery(structuralRecovery,
            () => { Phases.Add("plan"); Plan = SnapshotPlan; PlanErased = false; CloneExists = false; return true; },
            () => { Phases.Add("physical"); Assert.Equal(SnapshotPlan, Plan); Assert.False(CloneExists); Body = BuildBody(); return true; },
            () =>
            {
                Phases.Add("annotations");
                var data = TimberElementDefaults.For(TimberElementType.HipRafter) with
                    { WidthMm = 120, HeightMm = 160, SlopeDegrees = 30, AnnotationMode = TimberAnnotationMode.DimensionsLeader };
                var annotationPlan = TimberAnnotationRefreshPlanner.Create(data);
                Assert.True(annotationPlan.EnsureLabel && annotationPlan.ShouldSlopeArrowExist && annotationPlan.ShouldSlopeAngleTextExist);
                Annotations = new() { ("A1", "dimension", TimberElementLabelFormatter.FormatDimensions(data)),
                    ("A1", "arrow", ""), ("A1", "angle", "30°") };
                return true;
            },
            () => { Phases.Add("group"); Group = new() { "owner", "plan:A1", "physical:canonical", "dimension", "arrow", "angle" }; return true; });

        private IReadOnlyList<RoofPoint3D> BuildBody()
        {
            if (Manual is null)
                return RoofStructuralEditRules.Place(_canonical, _automaticPlan,
                    RoofStructuralEditRules.Get(GeneratedState, Key)).ConvexHalves[0].SourcePrismVertices;
            Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(Manual.SourceRole,
                Manual.WidthMm, Manual.Placement!, out var body, out var failure), failure);
            return body!.ConvexHalves[0].SourcePrismVertices;
        }
    }
}
