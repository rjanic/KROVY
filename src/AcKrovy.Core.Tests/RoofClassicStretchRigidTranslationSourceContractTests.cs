using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for the LOCKED classic STRETCH pure-translation fix:
/// a geometrically-pure whole-roof STRETCH is accepted by the same shared locked
/// rigid-translation path as GRIP_STRETCH and normalized to snapshot + delta,
/// while a shape-changing STRETCH is NOT accepted and keeps the existing locked
/// recovery. Pin: shared candidate gate, geometric classifier reuse, snapshot +
/// delta target, no regeneration / metadata rewrite, and MOVE/ROTATE/GRIP
/// non-regression.
/// </summary>
public sealed class RoofClassicStretchRigidTranslationSourceContractTests
{
    private static readonly string ManualEdit = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofRigidGroupTransformRules.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs");

    private static readonly string ProcessOwnerBody = RoofUxSourceContractText.Member(
        ManualEdit,
        "private static OwnerEditOutcome ProcessOwner(",
        "private static bool TryAcceptLockedRigidTranslation(");

    private static readonly string RigidTranslationHelper = RoofUxSourceContractText.Member(
        ManualEdit,
        "private static bool TryAcceptLockedRigidTranslation(",
        "private static void RefreshModifiedAttachedManualNumberingAndAnnotations(");

    private static readonly string NormalizeMethod = RoofUxSourceContractText.Member(
        Recovery,
        "public static bool TryNormalizeRigidTranslation(",
        "private static bool TryTranslateAssembly(");

    private static readonly string TranslateAssembly = RoofUxSourceContractText.Member(
        Recovery,
        "private static bool TryTranslateAssembly(",
        "public static bool TryUnEraseAndRestore(");

    private static readonly string Classifier = RoofUxSourceContractText.Member(
        Rules,
        "public static bool TryClassifySourceOnlyTranslation(",
        "private static bool TryUniquePlanarTranslation(");

    [Fact]
    public void SharedCandidateGateAcceptsClassicStretch()
    {
        // Classic STRETCH is a rigid-translation candidate via the shared gate.
        Assert.Contains(
            "RoofGeneratedMemberEditCommandRules.IsClassicStretch(globalCommandName)",
            RigidTranslationHelper);
        Assert.Contains(
            "LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName)",
            RigidTranslationHelper);
    }

    [Fact]
    public void ClassicStretchIsAssemblySnapshotAndClassicCommandRule()
    {
        Assert.Contains(
            "normalized.Equals(\"STRETCH\", StringComparison.OrdinalIgnoreCase)",
            CommandRules);
    }

    [Fact]
    public void AcceptedClassicStretchRunsBeforeOldRecovery()
    {
        // The accepted branch returns Skipped before the old-position recovery call.
        Assert.Contains("TryAcceptLockedRigidTranslation", ProcessOwnerBody);
        Assert.Contains("return OwnerEditOutcome.Skipped;", ProcessOwnerBody);
        Assert.True(
            ProcessOwnerBody.IndexOf("TryAcceptLockedRigidTranslation", StringComparison.Ordinal) <
            ProcessOwnerBody.IndexOf("TryRecoverGeneratedMembersOnly", StringComparison.Ordinal));
    }

    [Fact]
    public void ShapeChangingClassicStretchStillUsesExistingRecovery()
    {
        // When the geometric classifier rejects (shape change / partial), the
        // existing locked recovery remains in place.
        Assert.Contains(
            "var recovered = RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(",
            ProcessOwnerBody);
        Assert.Contains("LockedRecovered", ProcessOwnerBody);
        Assert.Contains("return false;", RigidTranslationHelper);
    }

    [Fact]
    public void ClassicStretchIsGeometricallyClassifiedNotCommandOnly()
    {
        // Acceptance is mandatory-gated by the geometric source-only classifier,
        // never by command name alone.
        Assert.Contains("RoofRigidGroupTransformRules.TryClassifySourceOnlyTranslation", RigidTranslationHelper);
        Assert.Contains("RoofPolylineExtractor.Extract(owner)", RigidTranslationHelper);
        Assert.Contains("entry.Assembly.RoofSource.Vertices", RigidTranslationHelper);
    }

    [Fact]
    public void ClassifierIsTheExistingSourceOnlyCore()
    {
        // Same class, same tolerance, same private geometry core — reused, not
        // duplicated.
        Assert.Contains("ToleranceMm", Classifier);
        Assert.Contains("TryUniquePlanarTranslation", Classifier);
        Assert.Contains("SourceShapeRigidEquivalent", Classifier);
        Assert.Contains("Count != 4", Classifier);
    }

    [Fact]
    public void NormalizationWritesSnapshotPlusDeltaForGeneratedAttachedAndAnnotations()
    {
        Assert.Contains("TryNormalizeRigidTranslation", RigidTranslationHelper);
        Assert.Contains("TryTranslateAssembly", NormalizeMethod);
        Assert.Contains("TryRestoreTimberLines", NormalizeMethod);
        Assert.Contains("TryRestoreAnnotations", NormalizeMethod);
        // Delta is applied to SNAPSHOT records (with-expressions), idempotent with
        // respect to native AutoCAD displacement.
        Assert.Contains("deltaX", TranslateAssembly);
        Assert.Contains("deltaY", TranslateAssembly);
        Assert.Contains("timber with", TranslateAssembly);
        Assert.Contains("annotation with", TranslateAssembly);
    }

    [Fact]
    public void ClassicStretchNormalizationNeverRegeneratesOrRewritesMetadata()
    {
        var code = NormalizeMethod + TranslateAssembly;
        Assert.Contains("OpenMode.ForRead", NormalizeMethod);
        Assert.DoesNotContain("RoofDefinitionStore.Write", NormalizeMethod);
        Assert.DoesNotContain("CreateAnchoredData", code);
        Assert.DoesNotContain("WriteAnchored", code);
        Assert.DoesNotContain("ReplayAnchoredChildrenForOwner", code);
        Assert.DoesNotContain("TryReplaceForSupportedResize", code);
        Assert.DoesNotContain("Materialize", code);
        Assert.DoesNotContain("RoofGeneratedRafterSetService", code);
    }

    [Fact]
    public void NoBoundingBoxesOrNearestHeuristicsInNewCode()
    {
        var code = RigidTranslationHelper + NormalizeMethod + TranslateAssembly;
        Assert.DoesNotContain("GetBoundingBox", code);
        Assert.DoesNotContain("GeometricExtents", code);
        Assert.DoesNotContain("Extents", code);
        Assert.DoesNotContain("GetClosestPointTo", code);
    }

    [Fact]
    public void ZeroDbBoundaryPreserved()
    {
        // Runs inside the caller's genuine-command transaction (CommandEnded
        // lifecycle); never deferred, never at a U/UNDO/REDO boundary.
        Assert.DoesNotContain("StartTransaction", RigidTranslationHelper);
        Assert.DoesNotContain("LockDocument", RigidTranslationHelper);
    }

    [Fact]
    public void T2GeneratedCreationOrder_NotInvolved()
    {
        var code = RigidTranslationHelper + NormalizeMethod + TranslateAssembly;
        Assert.DoesNotContain("new Line(", code);
        Assert.DoesNotContain("AppendEntity", code);
        Assert.DoesNotContain("WriteAtomic", code);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", code);
    }

    [Fact]
    public void MoveRotateUnchangedAndStillPrecede()
    {
        Assert.Contains("Equals(\"MOVE\", StringComparison.OrdinalIgnoreCase)", ProcessOwnerBody);
        Assert.Contains("Equals(\"ROTATE\", StringComparison.OrdinalIgnoreCase)", ProcessOwnerBody);
        Assert.True(
            ProcessOwnerBody.IndexOf("isRigidRoofTransform", StringComparison.Ordinal) <
            ProcessOwnerBody.IndexOf("TryAcceptLockedRigidTranslation", StringComparison.Ordinal));
    }

    [Fact]
    public void GripStretchRemainsAcceptedThroughSamePath()
    {
        // The existing GRIP_STRETCH path is unchanged — same helper, same gate,
        // same classifier. Grip acceptance is not regressed by the classic STRETCH
        // widening.
        Assert.Contains(
            "LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName)",
            RigidTranslationHelper);
        Assert.Contains("TryNormalizeRigidTranslation", RigidTranslationHelper);
    }
}
