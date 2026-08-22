using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSuppressedLogicalAnchorSourceContractTests
{
    private static readonly string Context = Read("RoofGeneratedAnchorResolutionContext.cs");
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string Policy = Read("RoofSourceResizeChildPolicyService.cs");
    private static readonly string Edit = Read("RoofEditCommandWorkflow.cs");
    private static readonly string WholeCopy = Read("RoofWholeRoofCopyRebindService.cs");
    private static readonly string Mirror = Read("RoofMirrorCloneDetachService.cs");
    private static readonly string Live = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string CoreContext = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofLogicalGeneratedAnchorContext.cs");

    [Fact]
    public void PhysicalLookupPrecedesLogicalFallback_AndPhysicalCarriesActualLineGeometry()
    {
        var resolve = Member(Context, "public RoofGeneratedAnchorResolution Resolve", "private static Point3d ToAcad");
        var physical = resolve.IndexOf("_physicalByKey.TryGetValue", StringComparison.Ordinal);
        var logical = resolve.IndexOf("_logical.Resolve(key)", StringComparison.Ordinal);

        Assert.True(physical >= 0 && logical > physical);
        Assert.Contains("physical.Start", resolve);
        Assert.Contains("physical.End", resolve);
        Assert.Contains("RoofGeneratedAnchorResolutionKind.Physical", resolve);
    }

    [Fact]
    public void MissingPhysical_UsesOnlyExactSuppressedLogicalResolution()
    {
        Assert.Contains("RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed", Context);
        Assert.Contains("RoofGeneratedAnchorResolutionKind.VirtualSuppressed", Context);
        Assert.Contains("RoofLogicalGeneratedAnchorResolutionKind.LogicalKeyAbsent", Context);
        Assert.Contains("RoofGeneratedAnchorResolutionKind.Inconsistent", Context);
        Assert.DoesNotContain("SelectNearestAnchor", Context);
        Assert.DoesNotContain("SelectNearestMirrorAnchor", Context);
        Assert.Contains("_overrides.TryGet(key", CoreContext);
        Assert.Contains("!mapped.Suppressed", CoreContext);
    }

    [Fact]
    public void RegenerationBuildsOneOwnerContextFromAlreadySolvedLayout()
    {
        var replace = Member(
            Replacement,
            "public static ReplacementOutcome TryReplaceForSupportedResize(\n        Database database,\n        Transaction transaction,\n        Editor editor,\n        Polyline owner,\n        SimpleGableRoofGeometry geometry,\n        TimberElementDefaultProfile defaultProfile,\n        ElementLayerProfile layerProfile,\n        out RoofGeneratedAnchorResolutionContext? anchorResolutionContext",
            "public static IReadOnlyDictionary<ObjectId, TimberElementData> Materialize");
        Assert.Contains("var layoutResult = SimpleGableRafterLayoutSolver.Solve(", replace);
        Assert.Contains("var created = Materialize(", replace);
        Assert.Contains("RoofGeneratedAnchorResolutionContext.TryCreate(", replace);
        Assert.Contains("layoutResult.Layout", replace);
        Assert.Equal(1, Count(replace, "SimpleGableRafterLayoutSolver.Solve("));
        Assert.DoesNotContain("RoofDefinitionPersistence.Restore", Lifecycle);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver", Lifecycle);
    }

    [Fact]
    public void ReplayUsesResolutionWithoutRewritingPersistedAnchorMetadata()
    {
        var replay = Member(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static RoofGeneratedAnchorResolution ResolveAnchor");
        Assert.Contains("ResolveAnchor(", replay);
        Assert.Contains("anchorResolution.Start", replay);
        Assert.Contains("anchorResolution.End", replay);
        Assert.Contains("stored.Data.RelativeSegment", replay);
        Assert.DoesNotContain("CreateAnchoredData(", replay);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write(", replay);
        Assert.DoesNotContain("SelectNearestAnchor", replay);
        Assert.DoesNotContain("SelectNearestMirrorAnchor", replay);
    }

    [Fact]
    public void CopyAndSplitShareSameContext_AndFootprintRunsAfterReplay()
    {
        Assert.Equal(2, Count(Policy, "anchorResolutionContext: anchorResolutionContext"));
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Copy", Policy);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", Policy);
        Assert.Equal(2, Count(Edit, "anchorResolutionContext: anchorResolutionContext"));

        var replay = Member(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static RoofGeneratedAnchorResolution ResolveAnchor");
        var resolve = replay.IndexOf("ResolveAnchor(", StringComparison.Ordinal);
        var relative = replay.IndexOf("TryReplay(", StringComparison.Ordinal);
        var containment = replay.IndexOf(
            "RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary",
            StringComparison.Ordinal);
        Assert.True(resolve >= 0 && relative > resolve && containment > relative);
    }

    [Fact]
    public void FailureStatesUseExistingDormancyPath()
    {
        var replay = Member(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static RoofGeneratedAnchorResolution ResolveAnchor");
        Assert.Contains("if (!anchorResolution.IsResolved)", replay);
        Assert.Contains("MakeCopyChildDormant(document, transaction, childLine)", replay);
        Assert.Contains("anchorResolution.DiagnosticToken", replay);
        Assert.Contains("\"anchor-missing\"", replay);
    }

    [Fact]
    public void ReplayDiagnosticReportsResolutionKind()
    {
        Assert.Contains("$\" resolution={resolution}\"", Lifecycle);
        Assert.Contains("\"virtual-suppressed\"", Context);
        Assert.Contains("\"logical-absent\"", Context);
        Assert.Contains("\"inconsistent\"", Context);
    }

    [Fact]
    public void WholeRoofCopyAndMirrorSuppressionSemanticsRemainExactKey()
    {
        Assert.Contains("AnchorGeneratedMemberKey", WholeCopy);
        Assert.DoesNotContain("SelectNearestAnchor", WholeCopy);
        Assert.DoesNotContain("SelectNearestMirrorAnchor", WholeCopy);
        Assert.Contains("RoofGeneratedMemberOverride.Suppress(key, elementId)", Mirror);
        Assert.DoesNotContain("RoofLogicalGeneratedAnchorContext", Mirror);
    }

    [Fact]
    public void UndoRedoGuardsAndGeneratedT2OrderingRemainUntouched()
    {
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Live);
        Assert.DoesNotContain("new Timer", Context + Lifecycle + Replacement);
        Assert.DoesNotContain("SendStringToExecute", Context + Lifecycle + Replacement);

        var creation = Member(
            RoofUxSourceContractText.Read(
                "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberSourceLineCreationService.cs"),
            "// Generated timber: AppendEntity -> WriteAtomic -> AddNewlyCreatedDBObject",
            "layerService.ApplyLayerForTimberType");
        var append = creation.IndexOf("AppendEntity", StringComparison.Ordinal);
        var atomic = creation.IndexOf("WriteAtomic", StringComparison.Ordinal);
        var add = creation.IndexOf("AddNewlyCreatedDBObject", StringComparison.Ordinal);
        Assert.True(append >= 0 && atomic > append && add > atomic);
    }

    private static int Count(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);
}
