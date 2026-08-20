using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberTrimTargetedRecalcSourceContractTests
{
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Labels = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "ElementLabelService.cs");
    private static readonly string Identity = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberElementItemIdentityService.cs");
    private static readonly string Commands = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
    private static readonly string Recalc = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberTargetedRecalcService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);

    [Fact]
    public void TrimAndExtend_UseSharedAkRecalcPipeline_ForChangedIdsOnly()
    {
        var accept = AcceptBody();
        var recalc = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryRecalculateAcceptedMembers",
            "private static bool TryRefreshAcceptedMemberAnnotations");
        Assert.Contains("IsEndpointTrimOrExtendCommand(", accept);
        Assert.Contains("changedRecalcItems.Add(", accept);
        Assert.Contains("RequiresRecalculation", accept);
        Assert.Contains("TryRecalculateAcceptedMembers", accept);
        Assert.Contains("RoofGeneratedMemberTargetedRecalcService.TryRecalculate", recalc);
        Assert.Contains("ElementLabelService.UpdateInCurrentTransaction", Recalc);
        Assert.Contains("SynchronizeElementIdsDetailed", Labels);
        Assert.Contains("TimberAnnotationService.EnsureForElement", Labels);
        Assert.DoesNotContain("RecalculateAll()", Manual);
        Assert.DoesNotContain("RecalculateAll()", Recalc);
        Assert.DoesNotContain("SendStringToExecute", Manual);
        Assert.DoesNotContain("SendStringToExecute", Recalc);
        Assert.DoesNotContain("new Timer", Manual);
        Assert.DoesNotContain("ObjectOverrule", Manual);
        Assert.DoesNotContain("DatabaseReactor", Manual);
        Assert.DoesNotContain("FindAllTimberElements", Manual);
        Assert.DoesNotContain("UpdateAll(", Manual);
        Assert.DoesNotContain("UpdateAll(", Recalc);
        Assert.DoesNotContain("entity.Erase();", Manual);
        Assert.DoesNotContain("TryReplaceForSupportedResize", Manual);
    }

    [Fact]
    public void AkRecalc_AndEndpointEdits_ShareUpdateInCurrentTransaction()
    {
        var recalcAll = RoofUxSourceContractText.Member(
            Commands,
            "public void RecalculateAll()",
            "private static void AssignWithPresetType");
        Assert.Contains("TimberElementMeasurer.Measure(", recalcAll);
        Assert.Contains("ElementLabelService.UpdateAll(", recalcAll);
        Assert.Contains("UpdateInCurrentTransaction(", Labels);
        Assert.Contains("SynchronizeElementIdsDetailed(", Labels);
        Assert.True(
            Labels.IndexOf("previousElementIdById = ReadElementIds", StringComparison.Ordinal) <
            Labels.IndexOf("TimberAnnotationService.EnsureForElement(", StringComparison.Ordinal));
        Assert.True(
            Labels.IndexOf("SynchronizeElementIdsDetailed(", StringComparison.Ordinal) <
            Labels.IndexOf("TimberAnnotationService.EnsureForElement(", StringComparison.Ordinal));
        Assert.Contains("ReadCurrentMeasurements", Identity);
        Assert.Contains("TimberElementMeasurer.Measure(", Identity);
        Assert.True(
            Identity.IndexOf("ReadCurrentMeasurements(", StringComparison.Ordinal) <
            Identity.IndexOf("metadataStore.Write(", StringComparison.Ordinal));
        Assert.Contains("AssignElementIds", Identity);
        Assert.Contains("IsChanged: targetSet.Contains(entry.Id)", Identity);
    }

    [Fact]
    public void RecalculationRunsAfterAcceptedGeometry_AndBeforeDefinitionWrite()
    {
        var accept = AcceptBody();
        Assert.True(
            accept.IndexOf("ApplyAcceptedLineGeometry", StringComparison.Ordinal) <
            accept.IndexOf("changedRecalcItems.Add(", StringComparison.Ordinal));
        Assert.True(
            accept.IndexOf("changedRecalcItems.Add(", StringComparison.Ordinal) <
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal));
        Assert.True(
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal) <
            accept.LastIndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal));
        Assert.True(
            accept.IndexOf("if (isTargetedRecalc)", StringComparison.Ordinal) <
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal));
        Assert.Contains("IsTargetedRecalcCommand", accept);
    }

    [Fact]
    public void UntouchedMembers_AreNotPassedToSharedRecalc()
    {
        var accept = AcceptBody();
        var recalc = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryRecalculateAcceptedMembers",
            "private static bool TryRefreshAcceptedMemberAnnotations");
        Assert.Contains("if (!modifiedIds.Contains(id))", accept);
        Assert.Contains("RequiresRecalculation", accept);
        Assert.Contains("changedItems", recalc);
        Assert.DoesNotContain("generatedIds", recalc);
        Assert.DoesNotContain("FindByOwner", recalc);
    }

    [Fact]
    public void LockedTrim_DoesNotEnterTargetedRecalc()
    {
        Assert.True(
            Manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            Manual.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal));
        Assert.True(
            Manual.IndexOf("TryRecoverGeneratedMembersOnly", StringComparison.Ordinal) <
            Manual.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal));
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.DoesNotContain("TryRecalculateAcceptedMembers", RoofUxSourceContractText.Member(
            Manual,
            "if (!supportedUnlocked)",
            "var accept = TryAcceptUnlockedEdits("));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTrimCommand("STRETCH"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsExtendCommand("STRETCH"));
    }

    [Fact]
    public void RecalcFailure_AbortsOwnerAcceptance()
    {
        var accept = AcceptBody();
        var recalc = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryRecalculateAcceptedMembers",
            "private static bool TryRefreshAcceptedMemberAnnotations");
        Assert.Contains("targeted-recalc-failure", recalc);
        Assert.Contains("result.Skipped > 0", Recalc);
        Assert.Contains("ROOF_MANUAL_EDIT_RECALC_FAIL", Diag);
        Assert.Contains("ROOF_MANUAL_EDIT_RECALC command=", Diag);
        Assert.Contains("signatureGroupsChanged=", Diag);
        Assert.Contains("ROOF_MANUAL_EDIT_RECALC_ITEM", Diag);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.True(
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal) <
            accept.LastIndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiTrimInOneCommand_CollectsEachChangedMember()
    {
        var accept = AcceptBody();
        Assert.Contains("foreach (var id in generatedIds)", accept);
        Assert.Contains("modifiedIds.Contains(id)", accept);
        Assert.Contains("changedRecalcItems.Add(", accept);
        Assert.Contains("IsAssemblySnapshotCommand", RoofUxSourceContractText.Read(
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs"));
        Assert.Contains("TRIM", RoofUxSourceContractText.Read(
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs"));
        Assert.Contains("EXTEND", RoofUxSourceContractText.Read(
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs"));
        Assert.Contains("roofRelatedIds.Contains(id)", Live);
    }

    [Fact]
    public void OneChangedAmongMany_OnlyChangedRequiresRecalculation()
    {
        var canonical = Horizontal(2758.645d);
        var trimmed = new RoofGeneratedMemberGeometry(canonical.Start, new(716.997d, 0d, 0d));
        var untouched = canonical;
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, trimmed));
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, untouched));
        var changed = new[] { canonical, canonical, trimmed, canonical }
            .Count(item => RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, item));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void ThreeChangedMembers_AreAllSelected()
    {
        var canonical = Horizontal(2758.645d);
        var first = new RoofGeneratedMemberGeometry(canonical.Start, new(900d, 0d, 0d));
        var second = new RoofGeneratedMemberGeometry(canonical.Start, new(800d, 0d, 0d));
        var third = new RoofGeneratedMemberGeometry(canonical.Start, new(716.997d, 0d, 0d));
        var observed = new[] { first, canonical, second, canonical, third };
        var changed = observed.Count(item =>
            RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, item));
        Assert.Equal(3, changed);
    }

    [Fact]
    public void FinalPlanLength_UpdatesSlopeAwareCuttingLengthAndVolume()
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K3",
            SlopeDegrees = 35d,
        };
        var before = TimberCalculator.Measure(data, 2758.645d);
        var after = TimberCalculator.Measure(data, 716.997d);
        Assert.Equal("K3", after.Data.ElementId);
        Assert.Equal(716.997d, after.PlanLengthMm);
        Assert.True(after.ActualLengthMm < before.ActualLengthMm);
        Assert.True(after.CuttingLengthMm < before.CuttingLengthMm);
        Assert.True(after.VolumeM3 < before.VolumeM3);
        Assert.Equal(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(716.997d, 35d),
            after.ActualLengthMm,
            6);
        var second = TimberCalculator.Measure(data, 716.997d);
        Assert.Equal(after.CuttingLengthMm, second.CuttingLengthMm);
        Assert.Equal(after.VolumeM3, second.VolumeM3);
    }

    [Fact]
    public void ChangedCuttingLength_ReassignsOnlyTheTrimmedSignature_AndIsIdempotent()
    {
        var untouchedA = Measurement("K3", 2758.645d);
        var untouchedB = Measurement("K3", 2758.645d);
        var trimmed = Measurement("K3", 716.997d);
        var first = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(untouchedA, false),
            new TimberElementItemNumberingCandidate(untouchedB, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        Assert.Equal("K3", first[0].ElementId);
        Assert.Equal("K3", first[1].ElementId);
        Assert.NotEqual("K3", first[2].ElementId);
        var second = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(
                Measurement(first[0].ElementId, 2758.645d), false),
            new TimberElementItemNumberingCandidate(
                Measurement(first[1].ElementId, 2758.645d), false),
            new TimberElementItemNumberingCandidate(
                Measurement(first[2].ElementId, 716.997d), true),
        ]);
        Assert.Equal(first[0].ElementId, second[0].ElementId);
        Assert.Equal(first[1].ElementId, second[1].ElementId);
        Assert.Equal(first[2].ElementId, second[2].ElementId);
    }

    [Fact]
    public void LogicalKey_AndAcceptedOverride_RemainIndependentOfRecalc()
    {
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 3);
        var canonical = Horizontal(2758.645d);
        var observed = new RoofGeneratedMemberGeometry(canonical.Start, new(716.997d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, observed, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, key, "K3", startDelta, endDelta);
        Assert.Equal(key, composed!.Key);
        Assert.Equal("K3", composed.ReservedElementId);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Fact]
    public void AnnotationFamilies_RemainOnSharedEnsurePath()
    {
        Assert.Contains("SlopeAnnotationService.EnsureForElement(", RoofUxSourceContractText.Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberAnnotationService.cs"));
        Assert.Contains("if (glyph is Polyline arrow)", RoofUxSourceContractText.Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeArrowService.cs"));
        Assert.Contains("DBText angleText;", RoofUxSourceContractText.Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeAngleTextService.cs"));
        Assert.Contains("MLeader", Labels);
        Assert.Contains("DeleteDuplicateLabelsForExistingSourceHandles", Labels);
        Assert.Contains("new MLeader", Labels);
        Assert.DoesNotContain("new MLeader", Manual);
    }

    [Fact]
    public void NumberingSynchronization_HappensBeforeAnnotationEnsure()
    {
        Assert.True(
            Recalc.IndexOf("UpdateInCurrentTransaction(", StringComparison.Ordinal) <
            Recalc.IndexOf("WriteSuccess(", StringComparison.Ordinal) ||
            Recalc.Contains("ElementLabelService.UpdateInCurrentTransaction"));
        Assert.True(
            Labels.IndexOf("SynchronizeElementIdsDetailed(", StringComparison.Ordinal) <
            Labels.IndexOf("TimberAnnotationService.EnsureForElement(", StringComparison.Ordinal));
        Assert.Contains("refreshIds = distinctIds", Labels);
        Assert.Contains("Concat(sync.WrittenIds)", Labels);
        Assert.Contains("numberingTargets", Recalc);
        Assert.Contains("RequiresNumberingSynchronization", Recalc);
    }

    [Fact]
    public void IdentityWrites_AreLimitedToChangedAssignments()
    {
        Assert.Contains("IsChanged: targetSet.Contains(entry.Id)", Identity);
        Assert.Contains("metadataStore.Write(writableEntity, updatedData)", Identity);
        Assert.True(
            Identity.IndexOf("if (string.Equals(", StringComparison.Ordinal) <
            Identity.IndexOf("metadataStore.Write(writableEntity, updatedData)", StringComparison.Ordinal));
        Assert.DoesNotContain("entity.Erase();", Identity);
        Assert.DoesNotContain("entity.Erase();", Recalc);
    }

    private static string AcceptBody() =>
        RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));

    private static TimberElementMeasurement Measurement(string elementId, double planLengthMm)
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = elementId,
            SlopeDegrees = 35d,
        };
        return TimberCalculator.Measure(data, planLengthMm);
    }
}
