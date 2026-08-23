using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract coverage for the LOCKED whole-roof GRIP_STRETCH pure translation
/// fix: ProcessOwner accepts a geometrically-proven rigid grip translation and
/// normalizes the snapshot assembly to snapshot + delta instead of restoring the
/// old pre-command coordinates. Pin: branch placement, classifier reuse, snapshot
/// idempotence, no metadata/definition/regeneration writes, MOVE/ROTATE/unlocked
/// non-regression, zero-DB and T2 boundaries.
/// </summary>
public sealed class RoofGripRigidTranslationSourceContractTests
{
    private static readonly string ManualEdit = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryDiag.cs");
    private static readonly string Rules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofRigidGroupTransformRules.cs");

    private static readonly string ProcessOwnerBody = RoofUxSourceContractText.Member(
        ManualEdit,
        "private static OwnerEditOutcome ProcessOwner(",
        "private static bool TryAcceptLockedGripRigidTranslation(");

    private static readonly string RigidTranslationHelper = RoofUxSourceContractText.Member(
        ManualEdit,
        "private static bool TryAcceptLockedGripRigidTranslation(",
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
    public void LockedGripTranslationBranch_RunsInsideUnsupportedUnlocked_BeforeOldRecovery()
    {
        // The rigid-translation branch lives in the locked/unsupported (!supportedUnlocked)
        // path and is evaluated BEFORE the old-position generated-only recovery.
        Assert.Contains("if (!supportedUnlocked)", ProcessOwnerBody);
        Assert.Contains("TryAcceptLockedGripRigidTranslation", ProcessOwnerBody);
        Assert.Contains("TryRecoverGeneratedMembersOnly", ProcessOwnerBody);
        Assert.True(
            ProcessOwnerBody.IndexOf("TryAcceptLockedGripRigidTranslation", StringComparison.Ordinal) <
            ProcessOwnerBody.IndexOf("TryRecoverGeneratedMembersOnly", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptedTranslation_ReturnsSkipped_WithoutOldPositionRecovery()
    {
        // Accepted path returns Skipped before the recovery call; the helper itself
        // never invokes the old-position restore.
        Assert.Contains("return OwnerEditOutcome.Skipped;", ProcessOwnerBody);
        Assert.Contains("return true;", RigidTranslationHelper);
        Assert.DoesNotContain("TryRecoverGeneratedMembersOnly", RigidTranslationHelper);
    }

    [Fact]
    public void GripTranslation_IsGatedOnGripStretchCommandAndModifiedSource()
    {
        Assert.Contains("LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName)", RigidTranslationHelper);
        Assert.Contains("sourceModified", RigidTranslationHelper);
        Assert.Contains(
            "RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry)",
            RigidTranslationHelper);
    }

    [Fact]
    public void ClassifierCalledWithSnapshotVsLiveSourceVertices()
    {
        Assert.Contains("entry.Assembly.RoofSource.Vertices", RigidTranslationHelper);
        Assert.Contains("RoofPolylineExtractor.Extract(owner)", RigidTranslationHelper);
        Assert.Contains("RoofRigidGroupTransformRules.TryClassifySourceOnlyTranslation", RigidTranslationHelper);
    }

    [Fact]
    public void PureTranslationClassifier_IsTheSourceOnlyCoreOfRigidGroupRules()
    {
        // Same class, same tolerance, same private geometry core — never a second
        // incompatible translation definition.
        Assert.Contains("ToleranceMm", Classifier);
        Assert.Contains("TryUniquePlanarTranslation", Classifier);
        Assert.Contains("SourceShapeRigidEquivalent", Classifier);
        Assert.Contains("Count != 4", Classifier);
    }

    [Fact]
    public void NormalizationWritesSnapshotPlusDelta_NotOldCoordinates()
    {
        Assert.Contains("TryNormalizeRigidTranslation", RigidTranslationHelper);
        Assert.Contains("TryTranslateAssembly", NormalizeMethod);
        Assert.Contains("TryRestoreTimberLines", NormalizeMethod);
        Assert.Contains("TryRestoreAnnotations", NormalizeMethod);
        Assert.Contains("TryEraseUnsnapshotGeneratedDuplicates", NormalizeMethod);
        // Delta is applied to the SNAPSHOT data (with-expressions over snapshot records),
        // making the target idempotent with respect to native AutoCAD displacement.
        Assert.Contains("deltaX", TranslateAssembly);
        Assert.Contains("deltaY", TranslateAssembly);
        Assert.Contains("timber with", TranslateAssembly);
        Assert.Contains("annotation with", TranslateAssembly);
    }

    [Fact]
    public void Normalization_NeverWritesSourceOrDefinitionOrAttachedManualMetadata()
    {
        Assert.Contains("OpenMode.ForRead", NormalizeMethod);
        Assert.DoesNotContain("RoofDefinitionStore.Write", NormalizeMethod);
        Assert.DoesNotContain("CreateAnchoredData", NormalizeMethod + TranslateAssembly);
        Assert.DoesNotContain("WriteAnchored", NormalizeMethod + TranslateAssembly);
        Assert.DoesNotContain("ReplayAnchoredChildrenForOwner", NormalizeMethod + TranslateAssembly);
    }

    [Fact]
    public void Normalization_NeverRegeneratesTimber()
    {
        Assert.DoesNotContain("TryReplaceForSupportedResize", NormalizeMethod + TranslateAssembly);
        Assert.DoesNotContain("Materialize", NormalizeMethod + TranslateAssembly);
        Assert.DoesNotContain("RoofGeneratedRafterSetService", NormalizeMethod + TranslateAssembly);
    }

    [Fact]
    public void ShapeChangingLockedGrip_StillUsesExistingRecovery()
    {
        Assert.Contains(
            "var recovered = RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(",
            ProcessOwnerBody);
        Assert.Contains("LockedRecovered", ProcessOwnerBody);
    }

    [Fact]
    public void OrdinaryMoveRotate_UnchangedAndStillPrecedes()
    {
        Assert.Contains("Equals(\"MOVE\", StringComparison.OrdinalIgnoreCase)", ProcessOwnerBody);
        Assert.Contains("Equals(\"ROTATE\", StringComparison.OrdinalIgnoreCase)", ProcessOwnerBody);
        Assert.True(
            ProcessOwnerBody.IndexOf("isRigidRoofTransform", StringComparison.Ordinal) <
            ProcessOwnerBody.IndexOf("TryAcceptLockedGripRigidTranslation", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlockedStretchPath_Untouched()
    {
        // The rigid branch is inside !supportedUnlocked; the supportedUnlocked accept
        // path (TryAcceptUnlockedEdits) is not modified.
        Assert.True(
            ProcessOwnerBody.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            ProcessOwnerBody.IndexOf("TryAcceptLockedGripRigidTranslation", StringComparison.Ordinal));
        Assert.Contains("TryAcceptUnlockedEdits", ManualEdit);
    }

    [Fact]
    public void NoBoundingBoxesOrNearestHeuristicsInNewCode()
    {
        var newCode = RigidTranslationHelper + NormalizeMethod + TranslateAssembly;
        Assert.DoesNotContain("GetBoundingBox", newCode);
        Assert.DoesNotContain("GeometricExtents", newCode);
        Assert.DoesNotContain("Extents", newCode);
        Assert.DoesNotContain("GetClosestPointTo", newCode);
    }

    [Fact]
    public void NoOwnTransactionOrLockInNewCode_ZeroDbBoundaryPreserved()
    {
        // Runs inside the caller's genuine-command transaction (CommandEnded lifecycle);
        // never deferred, never at a U/UNDO/REDO boundary.
        Assert.DoesNotContain("StartTransaction", RigidTranslationHelper);
        Assert.DoesNotContain("LockDocument", RigidTranslationHelper);
    }

    [Fact]
    public void T2GeneratedCreationOrder_NotInvolved()
    {
        var newCode = RigidTranslationHelper + NormalizeMethod + TranslateAssembly;
        Assert.DoesNotContain("new Line(", newCode);
        Assert.DoesNotContain("AppendEntity", newCode);
        Assert.DoesNotContain("WriteAtomic", newCode);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", newCode);
    }

    [Fact]
    public void FocusedDiagnostic_EmittedOnAcceptedTranslation_NotMisleadingProbe()
    {
        Assert.Contains("ROOF_RIGID_GRIP_TRANSLATION", Diag);
        Assert.Contains("WriteRigidGripTranslation", Diag + RigidTranslationHelper);
        Assert.DoesNotContain("generated-only-ok", RigidTranslationHelper);
    }

    [Fact]
    public void GroupSyncRemainsMembershipOnly()
    {
        Assert.DoesNotContain("GeometricExtents", RigidTranslationHelper + NormalizeMethod);
        Assert.DoesNotContain("RoofDisplayGroupService", NormalizeMethod + TranslateAssembly);
    }
}
