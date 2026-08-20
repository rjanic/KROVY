using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofUnsupportedStretchRecoveryRulesTests
{
    [Theory]
    [InlineData("STRETCH", true)]
    [InlineData("GRIP_STRETCH", true)]
    [InlineData("_STRETCH", true)]
    [InlineData("MOVE", false)]
    [InlineData("COPY", false)]
    [InlineData("U", false)]
    public void IsRecoveryCommand_MatchesStretchFamilyOnly(string command, bool expected)
    {
        Assert.Equal(expected, RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand(command));
    }

    [Fact]
    public void EligibleSnapshot_RequiresClosedFourFiniteVertices()
    {
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(ValidSnapshot()));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(null));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(
            ValidSnapshot() with { IsClosed = false }));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(
            ValidSnapshot() with { Vertices = new[] { P(0, 0), P(10, 0), P(10, 6) } }));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(
            ValidSnapshot() with { OwnerHandle = " " }));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(
            ValidSnapshot() with { NormalX = 0, NormalY = 0, NormalZ = 0 }));
    }

    [Fact]
    public void CanAttemptRecovery_RequiresUnsupportedPlusMatchingSnapshot()
    {
        var snap = ValidSnapshot();
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            snap,
            "A1",
            RoofSourceChangeKind.Unsupported));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            snap,
            "A1",
            RoofSourceChangeKind.SupportedResize));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            snap,
            "B2",
            RoofSourceChangeKind.Unsupported));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "MOVE",
            snap,
            "A1",
            RoofSourceChangeKind.Unsupported));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            null,
            "A1",
            RoofSourceChangeKind.Unsupported));
    }

    [Fact]
    public void RestoredMatchesSnapshot_ComparesExactVertices()
    {
        var snap = ValidSnapshot();
        Assert.True(RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            snap.Vertices,
            true,
            snap));
        Assert.False(RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            Trapezoid(),
            true,
            snap));
        Assert.False(RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            snap.Vertices,
            false,
            snap));
    }

    [Fact]
    public void AcceptableRestoredClassification_IsRigidEquivalentOnly()
    {
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsAcceptableRestoredClassification(
            RoofSourceChangeKind.RigidEquivalent));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsAcceptableRestoredClassification(
            RoofSourceChangeKind.SupportedResize));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsAcceptableRestoredClassification(
            RoofSourceChangeKind.Unsupported));
    }

    [Fact]
    public void AllOwnersRecoverable_RequiresEveryOwnerEligible()
    {
        var assemblyA = Assembly("A");
        var assemblyB = Assembly("B");
        var owners = new[]
        {
            ("A", RoofSourceChangeKind.Unsupported),
            ("B", RoofSourceChangeKind.Unsupported),
        };
        Assert.True(RoofUnsupportedStretchRecoveryRules.AllOwnersRecoverable(
            "STRETCH",
            owners,
            handle => handle == "A" ? assemblyA : handle == "B" ? assemblyB : null));
        Assert.False(RoofUnsupportedStretchRecoveryRules.AllOwnersRecoverable(
            "STRETCH",
            owners,
            handle => handle == "A" ? assemblyA : null));
        Assert.False(RoofUnsupportedStretchRecoveryRules.AllOwnersRecoverable(
            "STRETCH",
            owners,
            _ => null));
    }

    [Fact]
    public void Assembly_WithTimberAndAnnotations_IsEligible_AndPreservesIdentityFields()
    {
        var timber = new RoofUnsupportedStretchTimberLineSnapshotData(
            "T1",
            "EL-1",
            "T1",
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(1000, 0, 0));
        var annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
            "AN1",
            "T1",
            RoofUnsupportedStretchAnnotationKind.MText,
            new RoofPoint3D(10, 20, 0),
            0.5d,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            SnapshotFrom(Rect(), "OWN1"),
            [timber],
            [annotation]);
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly));
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
            "STRETCH",
            assembly,
            "OWN1",
            RoofSourceChangeKind.Unsupported));
        Assert.Equal("EL-1", assembly.TimberLines[0].ElementId);
        Assert.Equal("T1", assembly.TimberLines[0].EntityHandle);
        Assert.Equal("AN1", assembly.Annotations[0].EntityHandle);
    }

    [Fact]
    public void Assembly_AnnotationBoundToUnknownTimber_IsRejected()
    {
        var timber = new RoofUnsupportedStretchTimberLineSnapshotData(
            "T1",
            "EL-1",
            "T1",
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(1000, 0, 0));
        var annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
            "AN1",
            "OTHER",
            RoofUnsupportedStretchAnnotationKind.MText,
            new RoofPoint3D(10, 20, 0),
            0d,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(
            new RoofUnsupportedStretchAssemblySnapshotData(
                SnapshotFrom(Rect(), "OWN1"),
                [timber],
                [annotation])));
    }

    [Fact]
    public void Assembly_DuplicateTimberHandle_IsRejected()
    {
        var timber = new RoofUnsupportedStretchTimberLineSnapshotData(
            "T1",
            "EL-1",
            "T1",
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(1000, 0, 0));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(
            new RoofUnsupportedStretchAssemblySnapshotData(
                SnapshotFrom(Rect(), "OWN1"),
                [timber, timber],
                [])));
    }

    [Fact]
    public void PointsEqual_UsesRecoveryTolerance()
    {
        var a = new RoofPoint3D(0, 0, 0);
        var b = new RoofPoint3D(0.005, 0, 0);
        var c = new RoofPoint3D(1, 0, 0);
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(a, b));
        Assert.False(RoofUnsupportedStretchRecoveryRules.PointsEqual(a, c));
    }

    private static RoofUnsupportedStretchAssemblySnapshotData Assembly(string handle) =>
        new(SnapshotFrom(Rect(), handle), [], []);


    [Fact]
    public void TrapezoidLive_WithValidPreSnapshot_IsRecoveryEligible()
    {
        var original = Rect();
        var data = Create(original);
        var trap = TrapezoidInput();
        Assert.Equal(RoofSourceChangeKind.Unsupported, Classify(trap, data).Kind);
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            SnapshotFrom(original, "OWN1"),
            "OWN1",
            RoofSourceChangeKind.Unsupported));
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, Classify(original, data).Kind);
    }

    [Fact]
    public void SupportedResize_BypassesRecoveryEligibility()
    {
        var original = Rect();
        var data = Create(original);
        var resized = StretchEave(original, 2000d);
        Assert.Equal(RoofSourceChangeKind.SupportedResize, Classify(resized, data).Kind);
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            SnapshotFrom(original, "OWN1"),
            "OWN1",
            RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void OrientationFlippedBaseline_RemainsRigidEquivalent_NotRecoveryTarget()
    {
        var original = Rect();
        var data = Create(original);
        var flipped = Reverse(original);
        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, Classify(flipped, data).Kind);
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "GRIP_STRETCH",
            SnapshotFrom(flipped, "OWN1"),
            "OWN1",
            RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void OrientationFlippedThenTrapezoid_StillUnsupported_AndRecoverableFromSnapshot()
    {
        var original = Rect();
        var data = Create(original);
        var flippedTrap = new RoofFootprintInput(
            new[] { P(0, 0), P(1000, 6000), P(9000, 6000), P(10000, 0) },
            true,
            false,
            true);
        Assert.Equal(RoofSourceChangeKind.Unsupported, Classify(flippedTrap, data).Kind);
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            SnapshotFrom(Reverse(original), "OWN1"),
            "OWN1",
            RoofSourceChangeKind.Unsupported));
    }

    [Fact]
    public void MissingSnapshot_NeverGuessesRecovery()
    {
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            null,
            "OWN1",
            RoofSourceChangeKind.Unsupported));
    }

    [Fact]
    public void AmbiguousHandleMismatch_RejectsRecovery()
    {
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptRecovery(
            "STRETCH",
            SnapshotFrom(Rect(), "A"),
            "A-COPY",
            RoofSourceChangeKind.Unsupported));
    }

    [Theory]
    [InlineData("U")]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    [InlineData("MREDO")]
    public void UndoRedoCommands_AreNotRecoveryCommands(string command)
    {
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand(command));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
            command,
            Assembly("OWN1"),
            "OWN1",
            RoofSourceChangeKind.Unsupported));
    }

    [Theory]
    [InlineData("GROUP")]
    [InlineData("_GROUP")]
    [InlineData("CLASSICGROUP")]
    [InlineData("GROUPEDIT")]
    public void NestedGroupFamilyCommands_AreNotRecoveryCommands(string command)
    {
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand(command));
        Assert.False(LiveGeometryCommandRules.IsUndoGroupingSourceCommand(command));
    }


    [Fact]
    public void AssemblyRecovery_BypassesSupportedResizeAndRigidEquivalent()
    {
        var assembly = Assembly("OWN1");
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
            "STRETCH",
            assembly,
            "OWN1",
            RoofSourceChangeKind.SupportedResize));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
            "STRETCH",
            assembly,
            "OWN1",
            RoofSourceChangeKind.RigidEquivalent));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
            "STRETCH",
            assembly,
            "OWN1",
            RoofSourceChangeKind.None));
    }

    [Fact]
    public void MLeaderTopology_IndexDriftIsRecoverable_MultiLeaderIsNot()
    {
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
            1,
            1,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
            2,
            1,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
            1,
            2,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
            1,
            1,
            RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            RoofUnsupportedStretchMLeaderContentKind.MTextContent));
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsIndexOnlyTopologyDrift(
            0,
            0,
            liveLeaderIndex: 3,
            liveLineIndex: 7));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsIndexOnlyTopologyDrift(
            0,
            0,
            liveLeaderIndex: 0,
            liveLineIndex: 0));
    }

    [Fact]
    public void MLeaderDoglegRestore_RequiresSafeLengthAndDirection()
    {
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanRestoreMLeaderDogleg(
            true,
            new RoofPoint3D(1, 0, 0),
            25d));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanRestoreMLeaderDogleg(
            true,
            new RoofPoint3D(1, 0, 0),
            1d));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanRestoreMLeaderDogleg(
            true,
            new RoofPoint3D(0, 0, 0),
            25d));
        Assert.False(RoofUnsupportedStretchRecoveryRules.CanRestoreMLeaderDogleg(
            false,
            new RoofPoint3D(1, 0, 0),
            25d));
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleMLeaderTopology(0, 0));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsEligibleMLeaderTopology(-1, 0));
    }

    [Fact]
    public void MLeaderSnapshot_StoresTopologyIndexesAndContentKind()
    {
        var annotation = new RoofUnsupportedStretchAnnotationSnapshotData(
            "AN1",
            "T1",
            RoofUnsupportedStretchAnnotationKind.MLeader,
            new RoofPoint3D(1, 0, 0),
            0.5d,
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(100, 0, 0),
            new RoofPoint3D(150, 0, 0),
            40d,
            null,
            null,
            null,
            null,
            MLeaderLeaderIndex: 0,
            MLeaderLeaderLineIndex: 0,
            MLeaderContentKind: RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            MLeaderEnableDogleg: true);
        Assert.Equal(0, annotation.MLeaderLeaderIndex);
        Assert.Equal(0, annotation.MLeaderLeaderLineIndex);
        Assert.Equal(
            RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            annotation.MLeaderContentKind);
        Assert.True(annotation.MLeaderEnableDogleg);
        Assert.True(RoofUnsupportedStretchRecoveryRules.CanRestoreMLeaderDogleg(
            annotation.MLeaderEnableDogleg,
            annotation.Position,
            annotation.SecondaryScalar));
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            annotation.SecondaryPoint!.Value,
            new RoofPoint3D(0, 0, 0)));
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            annotation.TertiaryPoint!.Value,
            new RoofPoint3D(100, 0, 0)));
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            annotation.QuaternaryPoint!.Value,
            new RoofPoint3D(150, 0, 0)));
    }

    [Fact]
    public void MultipleTimberSnapshots_PreserveExactStartEndAndElementIds()
    {
        var t1 = new RoofUnsupportedStretchTimberLineSnapshotData(
            "T1",
            "EL-1",
            "T1",
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(1000, 0, 0));
        var t2 = new RoofUnsupportedStretchTimberLineSnapshotData(
            "T2",
            "EL-2",
            "T2",
            new RoofPoint3D(0, 500, 0),
            new RoofPoint3D(1000, 500, 0));
        var a1 = new RoofUnsupportedStretchAnnotationSnapshotData(
            "AN1",
            "T1",
            RoofUnsupportedStretchAnnotationKind.MText,
            new RoofPoint3D(12.5, 34.5, 0),
            0.25d,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var assembly = new RoofUnsupportedStretchAssemblySnapshotData(
            SnapshotFrom(Rect(), "OWN1"),
            [t1, t2],
            [a1]);
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleAssembly(assembly));
        Assert.Equal(2, assembly.TimberLines.Count);
        Assert.Equal("EL-1", assembly.TimberLines[0].ElementId);
        Assert.Equal("EL-2", assembly.TimberLines[1].ElementId);
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            assembly.TimberLines[0].Start,
            new RoofPoint3D(0, 0, 0)));
        Assert.True(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            assembly.TimberLines[0].End,
            new RoofPoint3D(1000, 0, 0)));
        Assert.False(RoofUnsupportedStretchRecoveryRules.PointsEqual(
            assembly.TimberLines[0].End,
            new RoofPoint3D(1001, 0, 0)));
        Assert.Equal(12.5d, assembly.Annotations[0].Position!.Value.X);
        Assert.Equal(0.25d, assembly.Annotations[0].Rotation);
    }

    [Fact]
    public void AllOwnersRecoverable_SucceedsForMultipleRoofs()
    {
        var owners = new[]
        {
            ("R1", RoofSourceChangeKind.Unsupported),
            ("R2", RoofSourceChangeKind.Unsupported),
        };
        Assert.True(RoofUnsupportedStretchRecoveryRules.AllOwnersRecoverable(
            "GRIP_STRETCH",
            owners,
            handle => Assembly(handle)));
    }

    private static RoofUnsupportedStretchSourceSnapshotData ValidSnapshot() =>
        SnapshotFrom(Rect(), "A1");

    private static RoofUnsupportedStretchSourceSnapshotData SnapshotFrom(
        RoofFootprintInput source,
        string handle) =>
        new(
            handle,
            source.Vertices!,
            true,
            0d,
            0d,
            0d,
            1d);

    private static RoofFootprintInput Rect() =>
        new(
            new[] { P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000) },
            true,
            false,
            true);

    private static RoofFootprintInput TrapezoidInput() =>
        new(Trapezoid(), true, false, true);

    private static IReadOnlyList<RoofPoint2D> Trapezoid() =>
        new[] { P(0, 0), P(10000, 0), P(9000, 6000), P(1000, 6000) };

    private static RoofFootprintInput StretchEave(RoofFootprintInput source, double delta)
    {
        var v = source.Vertices!.ToArray();
        return new RoofFootprintInput(
            new[]
            {
                v[0],
                new RoofPoint2D(v[1].X + delta, v[1].Y),
                new RoofPoint2D(v[2].X + delta, v[2].Y),
                v[3],
            },
            true,
            false,
            true);
    }

    private static RoofFootprintInput Reverse(RoofFootprintInput source)
    {
        var verts = source.Vertices!.ToList();
        verts.Reverse();
        return new RoofFootprintInput(verts, true, false, true);
    }

    private static RoofDefinitionData Create(RoofFootprintInput source)
    {
        var footprint = Val(source);
        var v = source.Vertices!;
        Assert.True(RoofDirection2D.TryCreate(v[1].X - v[0].X, v[1].Y - v[0].Y, out var direction));
        var solved = SimpleGableRoofGeometrySolver.Solve(
            new RoofDefinition(footprint, new RoofParameters(35d, direction)));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return RoofDefinitionPersistence.Create(source, footprint, solved.Geometry!);
    }

    private static RoofSourceChangeClassification Classify(
        RoofFootprintInput source,
        RoofDefinitionData data) =>
        RoofDefinitionPersistence.Classify(source, Val(source), data);

    private static RoofFootprint Val(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofPoint2D P(double x, double y) => new(x, y);
}
