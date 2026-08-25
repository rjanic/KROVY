using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D3Tests
{
    private static readonly string Manual = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Copy = Read("RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string Replay = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string Resize = Read("RoofLiveResizeService.cs");
    private static readonly string Edit = Read("RoofEditCommandWorkflow.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");

    [Fact]
    public void CopyAssociation_CollectsNeutralMonopitchAndDoesNotInventAStation()
    {
        Assert.Contains("restored.Geometry is null", Copy);
        Assert.Contains("restored.Geometry));", Copy);
        Assert.DoesNotContain("restored.Geometry is not SimpleGableRoofGeometry", Copy);
        Assert.Contains("RoofGeneratedTimberStore.TryClear", Copy);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Copy);
        Assert.Contains("RoofAttachedManualLifecycleService.CreateAnchoredData", Copy);
        Assert.DoesNotContain("StationIndex +", Copy);
    }

    [Fact]
    public void BreakAndSplitTrim_UseTheExistingRoleAwareAttachedManualPromotion()
    {
        Assert.DoesNotContain("attached-manual-not-supported", Manual);
        Assert.Contains("RoofRafterLayoutSolver.Solve", Manual);
        Assert.Contains("TryPromoteSplitFragments", Manual);
        Assert.Contains("RoofAttachedManualOrigin.Split", Manual);
        Assert.Contains("RoofAttachedManualOrigin.Copy", Manual);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("BREAK", RoofKind.Monopitch));
    }

    [Fact]
    public void SourceReplacement_ReplaysBothOriginsAgainstFinalSharedAnchorContext()
    {
        Assert.DoesNotContain("geometry.Kind != RoofKind.Monopitch", Replacement);
        Assert.Contains("RoofGeneratedAnchorResolutionContext.TryCreate", Replacement);
        Assert.Contains("replayAttachedManualChildren: true", Resize);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Copy", ReplayPolicy());
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", ReplayPolicy());
        Assert.Contains("anchorResolutionContext: anchorResolutionContext", ReplayPolicy());
    }

    [Fact]
    public void ExactKeyDormancyAndReactivation_PreserveOnePersistedEntityAndRelativeData()
    {
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", Replay);
        Assert.Contains("childLine.Visible = false", Replay);
        Assert.Contains("var wasDormant = !childLine.Visible", Replay);
        Assert.Contains("childLine.Visible = true", Replay);
        Assert.Contains("reactivated++", Replay);
        Assert.DoesNotContain("SelectNearestAnchor", ReplayMethod());
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", ReplayMethod());
        Assert.DoesNotContain("new Line", ReplayMethod());
    }

    [Fact]
    public void OutsideFootprint_UsesTheSameDormantPathAndRemovesStaleAnnotation()
    {
        Assert.Contains("RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary", ReplayMethod());
        Assert.Contains("dormancyOutsideFootprint++", ReplayMethod());
        Assert.Contains("TimberAnnotationService.DeleteForSourceHandle", Replay);
        Assert.Contains("sourceFootprintVertices", ReplayPolicy());
    }

    [Fact]
    public void ReplayRefreshesSlopeMetadataAnnotationsAndGroupWithinCallerTransaction()
    {
        var replay = ReplayMethod();
        Assert.Contains("SlopeDegrees = anchorResolution.SlopeDegrees", replay);
        Assert.Contains("metadataStore.Write(childLine, timberData)", replay);
        Assert.Contains("TimberAnnotationService.EnsureForElement", replay);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", ReplayPolicy());
        Assert.DoesNotContain("StartTransaction", replay);
        Assert.DoesNotContain("transaction.Commit", replay);
    }

    [Fact]
    public void SemanticMirror_RebasesAttachedStateBeforeGeneratedReplacementAndReplay()
    {
        var apply = Member(
            Edit,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply",
            "private static string GetSoftReplacementMessage");
        var attachedRebase = apply.IndexOf(
            "TryRebaseForMonopitchSemanticMirror",
            StringComparison.Ordinal);
        var replacement = apply.IndexOf("TryReplaceForSupportedResize", StringComparison.Ordinal);
        var replay = apply.IndexOf("ReplayAnchoredChildrenForOwner", StringComparison.Ordinal);
        Assert.True(attachedRebase >= 0 && replacement > attachedRebase && replay > replacement);
        Assert.Contains("RebaseForReversedAnchorDirection", Replay);
    }

    [Fact]
    public void SaveReopenAndUndoRedo_DoNotDependOnDeferredRepair()
    {
        Assert.Contains("RoofAttachedManualTimberStore.Write", Replay);
        Assert.Contains("childLine.Visible = false", Replay);
        var ended = Member(Live, "private void CommandEnded", "private void CommandCancelled");
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", ended);
        Assert.Contains("ClearPendingLiveGeometryState", ended);
        Assert.DoesNotContain("StartTransaction(", ended);
        Assert.DoesNotContain("Application.Idle", Replay + Manual + Live);
        Assert.DoesNotContain("new Timer", Replay + Manual + Live);
    }

    [Fact]
    public void RotatedCopyReplay_UsesFreshAnchorAndIsNonAccumulative()
    {
        var original = Layout(Geometry(6000d, 9000d, 30d, 35d));
        var resized = Layout(Geometry(7600d, 9400d, 30d, 35d));
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            4);
        var oldAnchor = Assert.Single(original.Rafters, item => item.LogicalKey == key);
        var newAnchor = Assert.Single(resized.Rafters, item => item.LogicalKey == key);
        var childStart = Offset(oldAnchor.PlanStart, oldAnchor.RunDirection, 350d, 180d);
        var childEnd = Offset(oldAnchor.PlanEnd, oldAnchor.RunDirection, -250d, 180d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            At(oldAnchor.PlanStart),
            At(oldAnchor.PlanEnd),
            At(childStart),
            At(childEnd),
            out var relative));

        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            At(newAnchor.PlanStart),
            At(newAnchor.PlanEnd),
            relative,
            out var firstStart,
            out var firstEnd));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            At(newAnchor.PlanStart),
            At(newAnchor.PlanEnd),
            relative,
            out var secondStart,
            out var secondEnd));

        Assert.Equal(firstStart, secondStart);
        Assert.Equal(firstEnd, secondEnd);
        Assert.NotEqual(At(childStart), firstStart);
    }

    [Fact]
    public void StationKey_DisappearsAndReturnsWithoutNearestMigrationOrRecordMutation()
    {
        var wide = Layout(Geometry(6000d, 9000d, 0d, 35d));
        var narrow = Layout(Geometry(6000d, 2400d, 0d, 35d));
        var key = wide.Rafters[^1].LogicalKey;
        var persisted = new RoofAttachedManualTimberData(
            RoofAttachedManualTimberDataSchema.CurrentVersion,
            "A1",
            "B2",
            RoofTimberChildRole.AttachedManual,
            key,
            new RoofAttachedManualRelativeSegment(0d, 125d, 0d, 3000d, 125d, 0d),
            RoofAttachedManualOrigin.Copy);

        Assert.DoesNotContain(narrow.Rafters, item => item.LogicalKey == key);
        Assert.Contains(wide.Rafters, item => item.LogicalKey == key);
        var encoded = RoofAttachedManualTimberDataCodec.Encode(persisted);
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(encoded, out var reopened));
        Assert.Equal(persisted, reopened);
        Assert.DoesNotContain("SelectNearestAnchor", ReplayMethod());
    }

    [Fact]
    public void SemanticMirrorTwice_PreservesRotatedAttachedSegmentAndStoredRelationship()
    {
        var definition = Definition(6400d, 8200d, 30d, 35d);
        var before = Solve(definition);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(definition);
        var mirrored = Solve(mirroredDefinition);
        var twice = Solve(MonopitchRoofDefinitionRules.Mirror(mirroredDefinition));
        var beforeAnchor = Layout(before).Rafters[3];
        var mirrorAnchor = Assert.Single(
            Layout(mirrored).Rafters,
            item => item.LogicalKey == beforeAnchor.LogicalKey);
        var twiceAnchor = Assert.Single(
            Layout(twice).Rafters,
            item => item.LogicalKey == beforeAnchor.LogicalKey);
        var relative = new RoofAttachedManualRelativeSegment(
            150d, 220d, 0d, beforeAnchor.PlanLengthMm - 90d, 220d, 0d);
        var once = RoofAttachedManualRelativeGeometryRules.RebaseForReversedAnchorDirection(
            relative,
            beforeAnchor.PlanLengthMm);
        var twiceRelative = RoofAttachedManualRelativeGeometryRules.RebaseForReversedAnchorDirection(
            once,
            mirrorAnchor.PlanLengthMm);

        Assert.Equal(relative, twiceRelative);
        Assert.Equal(beforeAnchor.LogicalKey, mirrorAnchor.LogicalKey);
        Assert.Equal(beforeAnchor.LogicalKey, twiceAnchor.LogicalKey);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            At(beforeAnchor.PlanStart), At(beforeAnchor.PlanEnd), relative, out var p0, out var p1));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            At(mirrorAnchor.PlanStart), At(mirrorAnchor.PlanEnd), once, out var m0, out var m1));
        Assert.Equal(p0.X, m0.X, 7);
        Assert.Equal(p0.Y, m0.Y, 7);
        Assert.Equal(p1.X, m1.X, 7);
        Assert.Equal(p1.Y, m1.Y, 7);
    }

    [Fact]
    public void VersionSchemaAndUnsupportedScaleRemainFrozen()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(3, (int)RoofKind.Monopitch);
        Assert.False(RoofGeneratedMemberEditCommandRules
            .IsSupportedUnlockedGeneratedTimberCommand("SCALE", RoofKind.Monopitch));
        Assert.Contains("unsupported-scale", Manual);
    }

    private static string ReplayPolicy() => Read("RoofSourceResizeChildPolicyService.cs");

    private static string ReplayMethod() => Member(
        Replay,
        "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
        "private static RoofGeneratedAnchorResolution ResolveAnchor");

    private static RoofPoint2D Offset(
        RoofPoint2D point,
        RoofDirection2D direction,
        double along,
        double lateral) =>
        new(
            point.X + direction.X * along - direction.Y * lateral,
            point.Y + direction.Y * along + direction.X * lateral);

    private static RoofPoint3D At(RoofPoint2D point) => new(point.X, point.Y, 0d);

    private static RoofRafterLayout Layout(IRoofGeometry geometry)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry Geometry(
        double runLength,
        double stationLength,
        double rotationDegrees,
        double slopeDegrees) =>
        Solve(Definition(runLength, stationLength, rotationDegrees, slopeDegrees));

    private static RoofDefinition Definition(
        double runLength,
        double stationLength,
        double rotationDegrees,
        double slopeDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        Assert.True(RoofDirection2D.TryCreate(Math.Cos(radians), Math.Sin(radians), out var run));
        Assert.True(RoofDirection2D.TryCreate(-run.Y, run.X, out var station));
        RoofPoint2D Point(double u, double v) =>
            new(run.X * u + station.X * v, run.Y * u + station.Y * v);
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [Point(0d, 0d), Point(runLength, 0d), Point(runLength, stationLength), Point(0d, stationLength)],
            true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        return new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slopeDegrees, SlopeDirection: run),
            RoofKind.Monopitch);
    }

    private static MonopitchRoofGeometry Solve(RoofDefinition definition)
    {
        var solved = MonopitchRoofGeometrySolver.Solve(definition);
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(solved.Geometry);
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        fileName));

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
