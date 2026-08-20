using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberUnlockedEditSourceContractTests
{
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Recalc = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberTargetedRecalcService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Replacement = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs");

    [Fact]
    public void MoveRotateGrip_ClassifyAgainstAcceptedBaseline_InCanonicalLocalBasis()
    {
        var classify = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryClassifyAcceptedMemberEdit");
        Assert.Contains("TryCreateBasis", classify);
        Assert.Contains("NormalizeToBasis", classify);
        Assert.Contains("WriteNormalize", classify);
        Assert.Contains("TryClassifyAcceptedMemberEdit", classify);
        Assert.Contains("canonical", classify);
        Assert.Contains("baseline", classify);
        Assert.DoesNotContain("SendStringToExecute", classify);
        Assert.DoesNotContain("entity.Erase();", classify);
    }

    [Fact]
    public void Rotate_ComposesRigidInverseOfTryApply_AndKeepsEndpointOffsets()
    {
        var classify = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryClassifyAcceptedMemberEdit",
            "private static bool TryRestoreLiveTimberAnnotations");
        Assert.Contains("TryClassifyRigidEqualLength", classify);
        Assert.Contains("TryComposeRigidKeepingEndpointOffsets", classify);
        Assert.Contains("WriteComposeFail", classify);
        Assert.Contains("existing", classify);
        Assert.Contains("ROOF_MANUAL_EDIT_COMPOSE_FAIL", Diag);
        Assert.Contains("stage={Token(stage)}", Diag);
        Assert.Contains("candidateRotation", Diag);
        Assert.DoesNotContain("SendStringToExecute", classify);
        Assert.DoesNotContain("entity.Erase();", classify);
    }

    [Fact]
    public void MixedTimberAndAnnotationSelection_ClassifiesGeneratedTimberOnly()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryClassifyAcceptedMemberEdit");
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", accept);
        Assert.Contains("modifiedIds.Contains(id)", accept);
        Assert.Contains("NormalizeToBasis", accept);
        Assert.Contains("TryClassifyAcceptedMemberEdit", accept);
        Assert.DoesNotContain("TimberAnnotationStore.FindBySource", accept);
        Assert.DoesNotContain("entity.Erase();", accept);
    }

    [Fact]
    public void AcceptedGeometryEdits_ReuseTargetedRecalc_AndSkipNumberingWhenSignatureUnchanged()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        Assert.Contains("IsTargetedRecalcCommand", accept);
        Assert.Contains("RequiresRecalculation", accept);
        Assert.Contains("TryRecalculateAcceptedMembers", accept);
        Assert.Contains("RequiresNumberingSynchronization", Recalc);
        Assert.Contains("numberingTargets", Recalc);
        Assert.Contains("UpdateInCurrentTransaction", Recalc);
        Assert.DoesNotContain("RecalculateAll()", Manual);
        Assert.DoesNotContain("RecalculateAll()", Recalc);
        Assert.DoesNotContain("FindAllTimberElements", Manual);
        Assert.DoesNotContain("UpdateAll(", Recalc);
    }

    [Fact]
    public void Erase_UsesCommandSnapshot_AndPersistsSuppression()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        Assert.Contains("IsEraseCommand", accept);
        Assert.Contains("snapshot.Assembly.TimberLines", accept);
        Assert.Contains("TryResolveErasedMemberKey", accept);
        Assert.Contains("RoofGeneratedMemberOverride.Suppress", accept);
        Assert.Contains("DeleteAnnotationsForHandle", accept);
        Assert.Contains("HasErasedOwnedAnnotation", accept);
        Assert.Contains("TryRestoreLiveTimberAnnotations", accept);
        Assert.Contains("action=suppress", Diag);
        Assert.Contains("ROOF_MANUAL_EDIT_ACCEPT", Diag);
        Assert.Contains("IsEraseCommand(globalCommandName)", Resize);
        Assert.Contains("HasErasedOwnedGeneratedAnnotation", Resize);
        Assert.Contains("HasErasedGeneratedTimber", Resize);
        Assert.DoesNotContain("RenumberElementIdsByCuttingLength", accept);
    }

    [Fact]
    public void LockedGeneratedEdits_RecoverWithoutOverrideWrite()
    {
        Assert.Contains("if (!supportedUnlocked)", Manual);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.Contains("TryUnEraseAndRestore", Manual);
        Assert.True(
            Manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            Manual.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal));
        Assert.Contains("Command_Roof_LockedNotificationTitle", Manual);
        Assert.Contains("IsClassicStretch(globalCommandName)", Manual);
        Assert.Contains("classic-stretch-locked", Manual);
    }

    [Fact]
    public void SuppressionSurvivesSupportedResizeMaterialize()
    {
        Assert.Contains("out var suppressed", Replacement);
        Assert.Contains("suppressed ||", Replacement);
        Assert.Contains("TryApplyToLayout", Replacement);
        Assert.Contains("PreserveEditState", Resize);
        Assert.Contains("ReservedElementId", Replacement);
        Assert.DoesNotContain("Nearest", Replacement);
        Assert.DoesNotContain("nearest", Replacement);
    }

    [Fact]
    public void UndoRedo_AndForbiddenHostMechanisms_RemainClosed()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Live);
        Assert.DoesNotContain("SendStringToExecute", Manual + Resize + Recalc);
        Assert.DoesNotContain("new Timer", Manual);
        Assert.DoesNotContain("ObjectOverrule", Manual);
        Assert.DoesNotContain("DatabaseReactor", Manual);
        Assert.DoesNotContain("BeginDeepClone", Manual);
        Assert.Contains("IsAssemblySnapshotCommand", CommandRules);
        Assert.Contains("ERASE", CommandRules);
        Assert.Contains("GRIP_STRETCH", CommandRules);
        Assert.Contains("MOVE", CommandRules);
        Assert.Contains("ROTATE", CommandRules);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Fact]
    public void AnnotationOnlyErase_DoesNotSuppressMember()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        Assert.True(
            accept.IndexOf("HasErasedOwnedAnnotation", StringComparison.Ordinal) <
            accept.IndexOf("RoofGeneratedMemberOverride.Suppress", StringComparison.Ordinal));
        Assert.Contains("TryIsLiveTimber", accept);
        Assert.Contains("annotation-restore", accept);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", Manual);
    }
}
