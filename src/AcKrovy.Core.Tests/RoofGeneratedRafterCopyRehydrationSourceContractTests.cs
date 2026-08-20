using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedRafterCopyRehydrationSourceContractTests
{
    private static readonly string LiveGeometry = Read("LiveGeometrySynchronizationService.cs");
    private static readonly string Rehydration = Read(
        "RoofGeneratedRafterCopyOwnershipRehydrationService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");
    private static readonly string Association = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedRafterCopyAssociationRules.cs");
    private static readonly string Replacement = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string GeneratedStore = Read("RoofGeneratedTimberStore.cs");

    [Fact]
    public void SameDwgCopy_InvokesRehydrationThroughExistingCommandLifecycle()
    {
        Assert.Contains("IsSameDwgCopyOwnershipCommand", CommandRules);
        Assert.Contains("RequiresGroupedUndoMark", CommandRules);
        Assert.Contains("RequiresGroupedUndoMark(e.GlobalCommandName)", LiveGeometry);
        Assert.Contains(
            "RoofGeneratedRafterCopyOwnershipRehydrationService.Process(",
            LiveGeometry);
        Assert.Contains("IsSameDwgCopyOwnershipCommand(globalCommandName)", Rehydration);
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Rehydration);
    }

    [Fact]
    public void RebindRewritesOwnerAndLayoutSignatureWithoutGeometryOrAnnotations()
    {
        Assert.Contains("RoofGeneratedTimberStore.Write(", Rehydration);
        Assert.Contains("RoofOwnerReference = ownerReference", Rehydration);
        Assert.Contains("LayoutSignature = layoutSignature", Rehydration);
        var rewrite = Member(
            Rehydration,
            "private static bool TryRewriteMember",
            "private static bool TryProcessCopiedClone");
        Assert.DoesNotContain("entity.Erase()", rewrite);
        Assert.Contains("TryEraseLockedCopyClone", Rehydration);
        Assert.DoesNotContain("ElementLabelService", Rehydration);
        Assert.DoesNotContain("SlopeAnnotationService", Rehydration);
        Assert.DoesNotContain("TimberSourceLineCreationService", Rehydration);
        Assert.DoesNotContain("EnsureForCreatedElements", Rehydration);
    }

    [Fact]
    public void AssociationUsesDeterministicLayoutGeometryNotHeuristics()
    {
        Assert.Contains("SimpleGableRafterLayoutSolver.Solve(", Association);
        Assert.Contains("TryMatchCompleteSet(", Association);
        Assert.Contains("GeometryMatches(", Association);
        Assert.DoesNotContain("GetClosestPointTo", Association);
        Assert.DoesNotContain("layer", Association, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACI", Association);
    }

    [Fact]
    public void StretchReplacementRemainsOwnerScopedAfterRehydration()
    {
        Assert.Contains("FindByOwner(", Replacement);
        Assert.Contains("TryReplaceForSupportedResize(", Replacement);
        Assert.DoesNotContain("CopyOwnershipRehydration", Replacement);
    }

    [Fact]
    public void NoDeepCloneArchitectureAndSchemasUnchanged()
    {
        var source = LiveGeometry + Rehydration + Association + GeneratedStore + CommandRules;
        Assert.DoesNotContain("BeginDeepClone", source);
        Assert.DoesNotContain("EndDeepClone", source);
        Assert.DoesNotContain("IdMapping", source);
        Assert.DoesNotContain("DatabaseReactor", Rehydration);
        Assert.DoesNotContain("ObjectOverrule", Rehydration);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, TimberDrawingSettings.DrawingSettingsSchemaVersion);
    }

    [Fact]
    public void ClipboardPasteRemainsOutOfScopeForThisCheckpoint()
    {
        Assert.Contains("Equals(\"COPY\"", CommandRules);
        Assert.DoesNotContain(
            "PASTECLIP",
            Member(CommandRules, "public static bool IsSameDwgCopyOwnershipCommand", "public static bool RequiresGroupedUndoMark"));
        Assert.DoesNotContain(
            "COPYCLIP",
            Member(CommandRules, "public static bool IsSameDwgCopyOwnershipCommand", "public static bool RequiresGroupedUndoMark"));
    }

    private static string Read(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);
}
