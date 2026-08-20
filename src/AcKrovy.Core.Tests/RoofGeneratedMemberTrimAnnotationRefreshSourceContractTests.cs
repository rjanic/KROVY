using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberTrimAnnotationRefreshSourceContractTests
{
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string AnnotationService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberAnnotationService.cs");
    private static readonly string Labels = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "ElementLabelService.cs");
    private static readonly string Slope = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeAnnotationService.cs");
    private static readonly string Arrow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeArrowService.cs");
    private static readonly string Angle = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeAngleTextService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");
    private static readonly string AkLabel = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "AkLabelCommandService.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs");

    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofGeneratedMemberKey HostRafterKey =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 3);

    [Fact]
    public void AcceptedUnlockedTrim_ReachesAnnotationRefresh_WithoutTreatingUpdateAsFailure()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        var refresh = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryRefreshAcceptedMemberAnnotations",
            "private static string ClassifyAnnotationKind");

        Assert.Contains("TryClassifyCollinearEndpointEdit", accept);
        Assert.Contains("ApplyAcceptedLineGeometry", accept);
        Assert.Contains("TryRefreshAcceptedMemberAnnotations", accept);
        Assert.Contains("TryRecalculateAcceptedMembers", accept);
        Assert.Contains("_ = TimberAnnotationService.EnsureForElement(", refresh);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", Manual);
        Assert.Contains("return true;", refresh);
        Assert.Contains("AK_LABEL counts as \"updated\"", refresh);
    }

    [Fact]
    public void GeneratedRafterAnnotations_AreLabelMLeader_SlopeArrowPolyline_AndSlopeAngleDbText()
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SlopeDegrees = 35d,
            AnnotationMode = TimberAnnotationMode.ItemNumberLeader,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(data);

        Assert.True(plan.EnsureLabel);
        Assert.True(plan.ReconcileSlopeArrow);
        Assert.True(plan.ShouldSlopeArrowExist);
        Assert.True(plan.ReconcileSlopeAngleText);
        Assert.True(plan.ShouldSlopeAngleTextExist);
        Assert.Equal(TimberSlopeGlyphKind.DirectionalArrow, TimberSlopeAnnotationRules.ResolveGlyphKind(data.ElementType, data.SlopeDegrees));
        Assert.Contains("ElementLabelService.UpsertForElement(", AnnotationService);
        Assert.Contains("SlopeAnnotationService.EnsureForElement(", AnnotationService);
        Assert.Contains("MLeader", Labels);
        Assert.Contains("if (glyph is Polyline arrow)", Arrow);
        Assert.Contains("DBText angleText;", Angle);
        Assert.Contains("new DBText()", Angle);
    }

    [Fact]
    public void ShortenedTimber_RefreshesLabelSlopeArrowAndSlopeAngleFromFinalLength()
    {
        const double hostTrimmedLengthMm = 716.997d;
        var label = TimberElementLabelPlacementCalculator.Calculate(
            0d, 0d, 0d, hostTrimmedLengthMm, 0d, hostTrimmedLengthMm / 2d, 180d);
        var leader = TimberLeaderPlacementCalculator.CalculateLinear(
            0d, 0d, 0d, hostTrimmedLengthMm, 0d, hostTrimmedLengthMm / 2d);
        var slopePlacement = TimberSlopeAnnotationPlacementCalculator.Calculate(hostTrimmedLengthMm, null);
        var arrow = TimberSlopeArrowCalculator.Calculate(
            0d, 0d, 0d, hostTrimmedLengthMm, 0d, slopePlacement.AnchorDistanceMm, false);

        Assert.Equal(180d, Distance(0d, 0d, 0d, hostTrimmedLengthMm, label.X, label.Y), 6);
        Assert.Equal(0d, leader.AnchorX, 6);
        Assert.InRange(slopePlacement.AnchorDistanceMm, 0d, hostTrimmedLengthMm);
        Assert.True(Math.Abs(arrow.TipY - arrow.TailY) > 1d);
        Assert.Contains("GetPlanLengthMm(sourceEntity)", Labels);
        Assert.Contains("preferredGeometry.LengthMm", Slope);
        Assert.Contains("SlopeArrowService.UpsertForElement(", Slope);
        Assert.Contains("SlopeAngleTextService.UpsertForElement(", Slope);
    }

    [Fact]
    public void HostAggressiveTrimLength_RemainsValidAndDrivesReportMeasurement()
    {
        var canonical = new RoofGeneratedMemberGeometry(new(34952.068d, 20367.29d, 0d), new(34952.068d, 17608.645d, 0d));
        var observed = new RoofGeneratedMemberGeometry(new(34952.068d, 20367.29d, 0d), new(34952.068d, 19650.293d, 0d));
        Assert.Equal(2758.645d, canonical.LengthMm, 3);
        Assert.Equal(716.997d, observed.LengthMm, 3);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, observed, ZUp, out var startDelta, out var endDelta, out var accepted, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(0d, startDelta, 3);
        Assert.Equal(-2041.648d, endDelta, 3);
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, HostRafterKey, "K4", startDelta, endDelta);
        Assert.NotNull(composed);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(accepted, replayed));
        Assert.Equal(716.997d, replayed.LengthMm, 3);

        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K4",
            SlopeDegrees = 35d,
        };
        var measurement = TimberCalculator.Measure(data, 716.997d);
        Assert.Equal("K4", measurement.Data.ElementId);
        Assert.Equal(716.997d, measurement.PlanLengthMm);
        Assert.True(measurement.ActualLengthMm > measurement.PlanLengthMm);
    }

    [Fact]
    public void SameTimberIdentity_AndLiveSourceHandle_RemainOnAcceptedTrim()
    {
        Assert.DoesNotContain("entity.Erase();", Manual);
        Assert.DoesNotContain("TryReplaceForSupportedResize", Manual);
        Assert.Contains("sourceEntity.Handle.ToString()", AnnotationService);
        Assert.Contains("SourceHandle = sourceHandle", Labels);
        Assert.Contains("OpenMode.ForWrite", Manual);
        Assert.Contains("RoofGeneratedTimberStore.Read(line)", Manual);
        Assert.Contains("timberData.ElementId", Manual);
    }

    [Fact]
    public void AnnotationRefresh_DoesNotInventDuplicatesOrSkipOwnedFamilies()
    {
        Assert.Contains("DeleteDuplicateLabelsForExistingSourceHandles", Labels);
        Assert.Contains("DeleteDuplicateArrowsForExistingSourceHandles", Arrow);
        Assert.Contains("DeleteDuplicateTextsForExistingSourceHandles", Angle);
        Assert.Contains("plan.EnsureLabel && ElementLabelService.UpsertForElement(", AnnotationService);
        Assert.Contains("SlopeAnnotationService.EnsureForElement(", AnnotationService);
        Assert.Contains("new MLeader", Labels);
        Assert.DoesNotContain("new MLeader", Manual);
    }

    [Fact]
    public void OverridePersistence_HappensOnlyAfterSuccessfulRefresh()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static bool TryRecalculateAcceptedMembers");
        Assert.True(
            accept.IndexOf("TryRecalculateAcceptedMembers", StringComparison.Ordinal) <
            accept.LastIndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal));
        Assert.True(
            accept.IndexOf("if (!TryRecalculateAcceptedMembers", StringComparison.Ordinal) <
            accept.LastIndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal));
        Assert.Contains("annotation-refresh-failure", accept);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.True(
            Manual.IndexOf("WriteUnlockedReject", StringComparison.Ordinal) <
            Manual.LastIndexOf("OwnerEditOutcome.UnsupportedRecovered", StringComparison.Ordinal));
    }

    [Fact]
    public void RealAnnotationException_EmitsOneDebugFailLine()
    {
        Assert.Contains("ROOF_MANUAL_EDIT_ANNOTATION_FAIL", Diag);
        Assert.Contains("WriteAnnotationFail(", Diag);
        Assert.Contains("command=", Diag);
        Assert.Contains("timber=", Diag);
        Assert.Contains("annotation=", Diag);
        Assert.Contains("kind=", Diag);
        Assert.Contains("stage=", Diag);
        Assert.Contains("reason=", Diag);
        Assert.Contains("exception=", Diag);
        Assert.Contains("status=", Diag);
        Assert.Contains("catch (System.Exception ex)", Manual);
        Assert.Contains("WriteAnnotationRefreshFail(", Manual);
    }

    [Fact]
    public void LockedTrim_StillGoesThroughGeneratedOnlyRecovery()
    {
        Assert.Contains("if (!supportedUnlocked)", Manual);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Manual);
        Assert.True(
            Manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            Manual.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal));
        Assert.Contains("Command_Roof_LockedNotificationTitle", Manual);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Recovery);
    }

    [Fact]
    public void LiveRefreshAndAkLabel_KeepExistingEnsureBooleanContract()
    {
        Assert.Contains("TimberAnnotationService.EnsureForElement(", Live);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", Live);
        Assert.Contains("var ensured = TimberAnnotationService.EnsureForElement(", AkLabel);
        Assert.Contains("created++;", AkLabel);
        Assert.Contains("updated++;", AkLabel);
        Assert.Contains("PreserveEditState", Resize);
        Assert.DoesNotContain("if (!TimberAnnotationService.EnsureForElement", Resize);
    }

    [Fact]
    public void Schema3_AndUndoRedoGuards_RemainUnchanged()
    {
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Live);
        Assert.Contains("public static bool IsUndoRedoCommand(", CommandRules);
        Assert.DoesNotContain("SendStringToExecute", Manual);
        Assert.DoesNotContain("new Timer", Manual);
    }

    private static double Distance(
        double startX,
        double startY,
        double endX,
        double endY,
        double x,
        double y)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        return Math.Abs(((x - startX) * dy) - ((y - startY) * dx)) / length;
    }
}
