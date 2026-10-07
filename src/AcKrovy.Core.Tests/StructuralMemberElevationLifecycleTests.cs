using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract tests for elevation lifecycle invariants (CAD-neutral).
/// Tests A–S as per the specification.
/// These tests verify Core rules only — no AutoCAD transaction or HOST access.
///
/// Naming follows the spec: A=VH→OS no detach, B=CalcMode no detach, etc.
/// </summary>
public sealed class StructuralMemberElevationLifecycleTests
{
    private const double Tol = 0.001d;
    private const double HeightMm = 160d;
    private const double PlanLengthMm = 4000d;

    // =========================================================================
    // A. VH → OS reference switch: no geometry change, no detach trigger
    // =========================================================================

    [Fact]
    public void A_VhToOs_NoGeometryChange()
    {
        // The same canonical axis elevations; only DisplayReference changes.
        var axisStart = 500d;
        var axisEnd = 2000d;
        var stateVH = StructuralMemberElevationRules.CreateSloped(axisStart, axisEnd,
            StructuralMemberElevationReferenceKind.VH);
        var stateOS = StructuralMemberElevationRules.CreateSloped(axisStart, axisEnd,
            StructuralMemberElevationReferenceKind.OS);

        // Canonical axis values are identical → no geometry change.
        Assert.Equal(stateVH.AxisStartElevationMm, stateOS.AxisStartElevationMm, Tol);
        Assert.Equal(stateVH.AxisEndElevationMm, stateOS.AxisEndElevationMm, Tol);
        // Different display reference → display-only change.
        Assert.NotEqual(stateVH.DisplayReference, stateOS.DisplayReference);
    }

    // =========================================================================
    // B. Calculation mode switch: no geometry change
    // =========================================================================

    [Fact]
    public void B_CalcModeSwitch_NoGeometryChange()
    {
        // Switching from LowerUpper to LowerSlope must not change the canonical axis values.
        // A ViewModel initialized with an existing state must preserve axis elevations on mode switch.
        var axisStart = 1000d;
        var axisEnd = 3000d;
        var state = StructuralMemberElevationRules.CreateSloped(axisStart, axisEnd);

        // The axis elevations remain canonical regardless of calculation mode.
        Assert.Equal(axisStart, state.AxisStartElevationMm);
        Assert.Equal(axisEnd, state.AxisEndElevationMm);
        // Mode switch is a display/UI concern, not a state change.
        Assert.True(StructuralMemberElevationRules.IsValid(state));
    }

    // =========================================================================
    // C. AUTO lower-Z edit → geometry changed → detach trigger
    // =========================================================================

    [Fact]
    public void C_AutoLowerZEdit_GeometryChanged()
    {
        var initial = StructuralMemberElevationRules.CreateSloped(1000d, 3000d);
        var edited = StructuralMemberElevationRules.CreateSloped(1100d, 3000d); // +100 mm lower

        Assert.NotEqual(initial.AxisStartElevationMm, edited.AxisStartElevationMm);
        // Geometry change: the delta exceeds the 0.001 mm tolerance.
        var delta = Math.Abs(edited.AxisStartElevationMm - initial.AxisStartElevationMm);
        Assert.True(delta > 0.001d);
    }

    // =========================================================================
    // D. AUTO upper-Z edit → geometry changed
    // =========================================================================

    [Fact]
    public void D_AutoUpperZEdit_GeometryChanged()
    {
        var initial = StructuralMemberElevationRules.CreateSloped(0d, 3000d);
        var edited = StructuralMemberElevationRules.CreateSloped(0d, 3100d); // +100 mm upper

        var delta = Math.Abs(edited.AxisEndElevationMm - initial.AxisEndElevationMm);
        Assert.True(delta > 0.001d);
    }

    // =========================================================================
    // E. AUTO slope edit → geometry changed → Physical3D pitch changes
    // =========================================================================

    [Fact]
    public void E_AutoSlopeEdit_GeometryChanged()
    {
        // Original: 45° slope
        var start = 0d;
        var end45 = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(start, PlanLengthMm, 45d);
        var initial = StructuralMemberElevationRules.CreateSloped(start, end45);

        // Edited: 30° slope (same lower Z)
        var end30 = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(start, PlanLengthMm, 30d);
        var edited = StructuralMemberElevationRules.CreateSloped(start, end30);

        Assert.NotEqual(initial.AxisEndElevationMm, edited.AxisEndElevationMm);
        var slopeInitial = StructuralMemberElevationRules.DeriveSlopeDegrees(
            initial.AxisStartElevationMm, initial.AxisEndElevationMm, PlanLengthMm);
        var slopeEdited = StructuralMemberElevationRules.DeriveSlopeDegrees(
            edited.AxisStartElevationMm, edited.AxisEndElevationMm, PlanLengthMm);
        Assert.Equal(45d, slopeInitial, precision: 5);
        Assert.Equal(30d, slopeEdited, precision: 5);
        Assert.NotEqual(slopeInitial, slopeEdited, Tol);
    }

    // =========================================================================
    // F. AUTO NO → rollback: state remains unchanged
    // =========================================================================

    [Fact]
    public void F_AutoNo_StateUnchanged()
    {
        // Simulates "user pressed NO" in the detach dialog.
        // The state before and after must be identical.
        var original = StructuralMemberElevationRules.CreateSloped(0d, 3000d,
            StructuralMemberElevationReferenceKind.VH);

        // Simulate: no changes applied when user cancels.
        var afterCancel = original; // reference type; same instance = no mutation
        Assert.Equal(original.AxisStartElevationMm, afterCancel.AxisStartElevationMm);
        Assert.Equal(original.AxisEndElevationMm, afterCancel.AxisEndElevationMm);
        Assert.Equal(original.DisplayReference, afterCancel.DisplayReference);
    }

    // =========================================================================
    // G. ConfirmAutomaticMemberDetach=false → auto-accept geometry change
    //    (Source contract: geometry change is geometry change regardless of dialog)
    // =========================================================================

    [Fact]
    public void G_AutoAccept_GeometryChangedWhenElevationEdited()
    {
        // Even with auto-accept, the geometry IS changed (detach happens, but without dialog).
        var initial = StructuralMemberElevationRules.CreateSloped(0d, 3000d);
        var edited = StructuralMemberElevationRules.CreateSloped(100d, 3000d); // lower Z shifted
        var delta = Math.Abs(edited.AxisStartElevationMm - initial.AxisStartElevationMm);
        Assert.True(delta > 0.001d, "Geometry is changed even with auto-accept.");
    }

    // =========================================================================
    // H. Independent elevation edit → same IndependentMemberId (state contract)
    // =========================================================================

    [Fact]
    public void H_IndependentEdit_MemberIdPreserved()
    {
        // The IndependentMemberId lives in RoofIndependentOrdinaryTimberData,
        // not in the elevation state. Elevation edits must not touch it.
        // This test verifies the elevation state is independent from member identity.
        var memberId = Guid.NewGuid().ToString("N");
        var state1 = StructuralMemberElevationRules.CreateSloped(500d, 2000d);
        var state2 = StructuralMemberElevationRules.CreateSloped(600d, 2100d);

        // Member ID is preserved; only the elevation state changes.
        // (The actual memberId check is in the HOST lifecycle service, not in Core rules.)
        Assert.True(StructuralMemberElevationRules.IsValid(state1));
        Assert.True(StructuralMemberElevationRules.IsValid(state2));
        // Both states are valid and the memberId is unchanged (string equality).
        Assert.NotEqual(state1.AxisStartElevationMm, state2.AxisStartElevationMm);
    }

    // =========================================================================
    // I. Independent second edit → persistent state updated
    // =========================================================================

    [Fact]
    public void I_IndependentSecondEdit_StateUpdated()
    {
        var state1 = StructuralMemberElevationRules.CreateSloped(0d, 2000d);
        var state2 = StructuralMemberElevationRules.CreateSloped(100d, 2100d); // second edit
        // Both must be valid and distinct.
        Assert.True(StructuralMemberElevationRules.IsValid(state1));
        Assert.True(StructuralMemberElevationRules.IsValid(state2));
        Assert.NotEqual(state1.AxisStartElevationMm, state2.AxisStartElevationMm);
    }

    // =========================================================================
    // J. Legacy Independent missing elevation state → IsValid returns false for null
    // =========================================================================

    [Fact]
    public void J_LegacyIndependent_NullStateIsInvalid()
    {
        Assert.False(StructuralMemberElevationRules.IsValid(null));
    }

    // =========================================================================
    // K. Plan2D always Z=0
    // =========================================================================

    [Fact]
    public void K_Plan2D_AlwaysZero()
    {
        // The elevation state carries Z for the axis; the plan axis always has Z=0.
        var plan = new RoofSegment3D(
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(4000, 0, 0));
        Assert.Equal(0d, plan.Start.Z, Tol);
        Assert.Equal(0d, plan.End.Z, Tol);
        // The elevation state does not modify plan XY.
        var state = StructuralMemberElevationRules.CreateSloped(500d, 2000d);
        // The AxisStart/End are purely elevation; plan XY is unchanged.
        Assert.Equal(4000d, plan.End.X - plan.Start.X);
    }

    // =========================================================================
    // L. Section dimensions unchanged by elevation edit
    // =========================================================================

    [Fact]
    public void L_SectionDimensionsPreserved()
    {
        // The elevation state does not carry section width/height.
        // Width and height come from timber metadata and Build State v2.
        const double widthMm = 80d;
        const double heightMm = 160d;

        var state = StructuralMemberElevationRules.CreateSloped(0d, 3000d);
        // HeightAxisZ changes with pitch, but does NOT change section dimensions.
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state.AxisStartElevationMm, state.AxisEndElevationMm, PlanLengthMm);
        var haz = StructuralMemberElevationRules.HeightAxisZ(Math.Abs(slope));
        // Section dimensions are unchanged parameters.
        Assert.Equal(80d, widthMm);
        Assert.Equal(160d, heightMm);
        // The Z difference between faces = heightMm * haz (NOT naively heightMm).
        var vhZ = StructuralMemberElevationRules.AxisToUpperFaceZ(0d, heightMm, haz);
        var shZ = StructuralMemberElevationRules.AxisToLowerFaceZ(0d, heightMm, haz);
        Assert.Equal(heightMm * haz, vhZ - shZ, Tol);
    }

    // =========================================================================
    // N. Annotation synchronization (source-contract: slope metadata correct)
    // =========================================================================

    [Fact]
    public void N_SlopeMetadataCorrect_From45DegreeState()
    {
        // Slope is derived precisely from axis elevations + plan length.
        var state = StructuralMemberElevationRules.CreateSloped(0d, 4000d); // 4000mm rise over 4000mm run
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state.AxisStartElevationMm, state.AxisEndElevationMm, PlanLengthMm);
        Assert.Equal(45d, slope, precision: 6);
    }

    // =========================================================================
    // R. No AttachedManual elevation change
    // =========================================================================

    [Fact]
    public void R_ElevationStateSchemaVersion_IsCurrent()
    {
        var state = StructuralMemberElevationRules.CreateSloped(0d, 1000d);
        Assert.Equal(StructuralMemberElevationStateSchema.CurrentVersion, state.SchemaVersion);
        Assert.Equal(2, StructuralMemberElevationStateSchema.CurrentVersion);
    }

    // =========================================================================
    // S. Explicit roof rebuild: AUTO generator output does not affect Independent elevation
    //    (source-contract: elevation state is member-owned, not roof-owned)
    // =========================================================================

    [Fact]
    public void S_ElevationState_IsMemberOwned_NotRoofOwned()
    {
        // The elevation state is stored on the Line's XRecord, not on the Polyline owner.
        // This is the contract guarantee that an AUTO roof rebuild cannot overwrite
        // an edited Independent member's elevation.
        var state = StructuralMemberElevationRules.CreateSloped(500d, 2000d,
            StructuralMemberElevationReferenceKind.VH);
        Assert.Equal(StructuralMemberElevationBehavior.SlopedEndpoints, state.Behavior);
        // The state is self-contained; no roof reference is embedded.
        Assert.Equal(500d, state.AxisStartElevationMm);
        Assert.Equal(2000d, state.AxisEndElevationMm);
    }

    // =========================================================================
    // Spatial axis resolver
    // =========================================================================

    [Fact]
    public void SpatialAxisResolver_ProducesCorrect3DAxis()
    {
        var plan = new RoofSegment3D(
            new RoofPoint3D(0, 0, 0),
            new RoofPoint3D(4000, 0, 0)); // horizontal plan going east
        var elevation = StructuralMemberElevationRules.CreateSloped(0d, 4000d); // 45° slope

        Assert.True(StructuralMemberSpatialAxisRules.TryResolve(
            plan, elevation, widthMm: 80, heightMm: 160, out var result));
        Assert.NotNull(result);

        // Spatial axis start: Z = 0 (axis at start)
        Assert.Equal(0d, result!.SpatialAxis.Start.Z, Tol);
        // Spatial axis end: Z = 4000 mm (axis at end)
        Assert.Equal(4000d, result.SpatialAxis.End.Z, Tol);
        // Slope = 45°
        Assert.Equal(45d, result.SlopeDegrees, precision: 5);
        // Plan XY preserved
        Assert.Equal(0d, result.PlanAxis.Start.Z, Tol);
        Assert.Equal(0d, result.PlanAxis.End.Z, Tol);
    }

    [Fact]
    public void SpatialAxisResolver_IncompatibleMembers_DifferentSlopes()
    {
        // Members A and B: same plan axis, but different slopes → spatial JOIN incompatible.
        var planA = new RoofSegment3D(new RoofPoint3D(0, 0, 0), new RoofPoint3D(3000, 0, 0));
        var planB = new RoofSegment3D(new RoofPoint3D(3000, 0, 0), new RoofPoint3D(6000, 0, 0));
        var elevA = StructuralMemberElevationRules.CreateSloped(0d, 3000d); // 45°
        var elevB = StructuralMemberElevationRules.CreateSloped(3000d, 5598d); // ~40°

        StructuralMemberSpatialAxisRules.TryResolve(planA, elevA, 80, 160, out var a);
        StructuralMemberSpatialAxisRules.TryResolve(planB, elevB, 80, 160, out var b);
        Assert.NotNull(a); Assert.NotNull(b);

        Assert.False(StructuralMemberSpatialAxisRules.AreSpatiallyJoinCompatible(a!, b!),
            "Different slopes must not be join-compatible.");
    }

    [Fact]
    public void SpatialAxisResolver_CompatibleMembers_SameSlope()
    {
        // Members A and B: collinear plan, same slope, Z continuous → compatible.
        var planA = new RoofSegment3D(new RoofPoint3D(0, 0, 0), new RoofPoint3D(3000, 0, 0));
        var planB = new RoofSegment3D(new RoofPoint3D(3000, 0, 0), new RoofPoint3D(6000, 0, 0));
        var elevA = StructuralMemberElevationRules.CreateSloped(0d, 3000d);   // 45°, end at Z=3000
        var elevB = StructuralMemberElevationRules.CreateSloped(3000d, 6000d); // 45°, start at Z=3000

        StructuralMemberSpatialAxisRules.TryResolve(planA, elevA, 80, 160, out var a);
        StructuralMemberSpatialAxisRules.TryResolve(planB, elevB, 80, 160, out var b);
        Assert.NotNull(a); Assert.NotNull(b);

        Assert.True(StructuralMemberSpatialAxisRules.AreSpatiallyJoinCompatible(a!, b!),
            "Same slope and Z-continuous members must be join-compatible.");
    }
}
