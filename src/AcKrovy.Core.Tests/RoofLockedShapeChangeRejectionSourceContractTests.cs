using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for the LOCKED-roof shape-change rejection fix:
/// a Locked roof must never accept a shape-changing source STRETCH as a supported
/// resize (apply-resizes). The EditState-aware policy downgrades a Locked roof's
/// geometrically-valid-but-shape-changing SupportedResize to Unsupported so the
/// existing unsupported STRETCH recovery path restores the source to pre-command.
/// A Locked rigid whole-roof translation (RigidEquivalent) is untouched, and an
/// Unlocked roof's SupportedResize stays a supported resize.
/// </summary>
public sealed class RoofLockedShapeChangeRejectionSourceContractTests
{
    private const string Infra = "src/AcKrovy.AutoCAD/Infrastructure/";
    private const string Core = "src/AcKrovy.Core/Services/Roofs/";

    [Fact]
    public void Policy_RejectsLockedSupportedResizeAsUnsupported()
    {
        Assert.Equal(
            RoofSourceChangeKind.Unsupported,
            RoofSourceChangeEditStatePolicy.EffectiveKind(
                RoofEditState.Locked,
                RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void Policy_KeepsLockedRigidEquivalentAsRigid()
    {
        Assert.Equal(
            RoofSourceChangeKind.RigidEquivalent,
            RoofSourceChangeEditStatePolicy.EffectiveKind(
                RoofEditState.Locked,
                RoofSourceChangeKind.RigidEquivalent));
    }

    [Fact]
    public void Policy_KeepsUnlockedSupportedResizeAsSupported()
    {
        Assert.Equal(
            RoofSourceChangeKind.SupportedResize,
            RoofSourceChangeEditStatePolicy.EffectiveKind(
                RoofEditState.Unlocked,
                RoofSourceChangeKind.SupportedResize));
    }

    [Fact]
    public void LiveResizeClassifyOwner_AppliesEditStatePolicy()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        var method = Member(
            resize,
            "private static RoofSourceChangeClassification ClassifyOwner",
            "private static bool TryInvokeUndoMark");
        Assert.Contains("RoofDefinitionPersistence.Classify", method);
        Assert.Contains("RoofSourceChangeEditStatePolicy.EffectiveKind", method);
        Assert.Contains("stored.Data.EditState", method);
    }

    [Fact]
    public void RecoveryClassify_AppliesEditStatePolicy()
    {
        var recovery = Read(Infra + "RoofUnsupportedStretchRecoveryService.cs");
        var method = Member(
            recovery,
            "private static RoofSourceChangeClassification Classify",
            "private static Point3d ToAcad");
        Assert.Contains("RoofDefinitionPersistence.Classify", method);
        Assert.Contains("RoofSourceChangeEditStatePolicy.EffectiveKind", method);
        Assert.Contains("stored.Data.EditState", method);
    }

    [Fact]
    public void Inspect_RoutesSupportedResizeToResizeOwners_OnlyWhenNotLocked()
    {
        // The Inspect boundary routes a Locked roof's shape-change (now Unsupported via
        // the policy) into the recovery path, not apply-resizes.
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        var inspect = Member(
            resize,
            "private static InspectionPlan Inspect(",
            "return new InspectionPlan(");
        Assert.Contains("switch (ClassifyOwner(polyline).Kind)", inspect);
        Assert.Contains("case RoofSourceChangeKind.SupportedResize:", inspect);
        Assert.Contains("resizeOwners.Add(id);", inspect);
        Assert.Contains("case RoofSourceChangeKind.Unsupported:", inspect);
        Assert.Contains("unsupportedOwners.Add(id);", inspect);
    }

    [Fact]
    public void Process_RoutesUnsupportedOwnersToRecovery_NotApplyResizes()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        var process = Member(
            resize,
            "public static IReadOnlyCollection<ObjectId> Process(",
            "public static bool TryBeginGroupedUndo");
        // Unsupported owners are recovered (not regenerated).
        Assert.Contains("plan.UnsupportedOwnerIds", process);
        Assert.Contains("TryRecoverUnsupportedOwners(", process);
        Assert.Contains("ApplyResizes(document, plan.ResizeOwnerIds", process);
    }

    [Fact]
    public void ZeroDbUndoRedo_IsNotWeakened()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", resize);
        Assert.Contains("return Array.Empty<ObjectId>();", resize);
        Assert.DoesNotContain("new Timer", resize);
        Assert.DoesNotContain("SendStringToExecute", resize);
    }

    [Fact]
    public void T2GeneratedCreationOrder_Untouched()
    {
        var resize = Read(Infra + "RoofLiveResizeService.cs");
        var recovery = Read(Infra + "RoofUnsupportedStretchRecoveryService.cs");
        var policy = Read(Core + "RoofSourceChangeEditStatePolicy.cs");
        Assert.DoesNotContain("new Line(", policy);
        Assert.DoesNotContain("AppendEntity", policy);
        Assert.DoesNotContain("WriteAtomic", policy);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", policy);
        // Recovery restores snapshot geometry; it does not regenerate via T2.
        Assert.Contains("RestorePolylineGeometry", recovery);
        Assert.DoesNotContain("new Line(", recovery);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

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

    private static string Member(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
