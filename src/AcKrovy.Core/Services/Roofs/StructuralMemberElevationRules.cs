using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral elevation calculations for structural members.
///
/// Canonical representation: AxisStartElevationMm / AxisEndElevationMm (OS / centerline).
/// Slope is always DERIVED from these two values and the plan length. Never persisted.
///
/// SH / OS / VH datum conversion uses the actual section HeightAxis Z component so that
/// the result is geometrically correct for pitched members. Do not use naive ±HeightMm/2.
///
/// ALL calculations use double-precision millimetres internally.
/// UI display is in metres with 3 decimal places via RoofRelativeElevationDatumRules.FormatMetres.
///
/// Plan2D invariant: plan axis Z is always 0. This class never touches or modifies plan entities.
/// </summary>
public static class StructuralMemberElevationRules
{
    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    public static bool IsValid(StructuralMemberElevationState? state) =>
        state is not null &&
        (state.SchemaVersion == StructuralMemberElevationStateSchema.CurrentVersion ||
         state.SchemaVersion == StructuralMemberElevationStateSchema.LegacyVersionWithoutCalculationMode) &&
        Enum.IsDefined(typeof(StructuralMemberElevationBehavior), state.Behavior) &&
        Enum.IsDefined(typeof(StructuralMemberElevationReferenceKind), state.DisplayReference) &&
        Enum.IsDefined(typeof(StructuralMemberElevationCalculationMode), state.CalculationMode) &&
        IsFinite(state.AxisStartElevationMm) &&
        IsFinite(state.AxisEndElevationMm) &&
        (state.Behavior != StructuralMemberElevationBehavior.UniformElevation ||
         state.AxisStartElevationMm == state.AxisEndElevationMm);

    // -------------------------------------------------------------------------
    // Section-frame-aware datum conversion
    //
    // The section HeightAxis is NOT necessarily world-vertical. For an ordinary rafter
    // at pitch θ, HeightAxisZ = cos(θ). We use HeightAxisZ (the Z component of the
    // normalized HeightAxis) to correctly convert between axis (OS) and face (SH/VH).
    //
    // Required round-trip: VH -> OS -> SH -> VH must preserve geometry exactly.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Z component of the HeightAxis unit vector for a rafter at the given pitch.
    /// For a horizontal member (pitch=0): HeightAxisZ = 1.0.
    /// For a 45° rafter: HeightAxisZ = cos(45°) ≈ 0.7071.
    /// For a 90° vertical member: HeightAxisZ = 0 (degenerate; not supported in V1).
    /// </summary>
    public static double HeightAxisZ(double pitchDegrees) =>
        Math.Cos(pitchDegrees * Math.PI / 180.0);

    /// <summary>Upper face (VH) axis Z from OS axis Z.</summary>
    public static double AxisToUpperFaceZ(double axisElevationMm, double heightMm, double heightAxisZ) =>
        axisElevationMm + (heightMm / 2.0) * heightAxisZ;

    /// <summary>Lower face (SH) axis Z from OS axis Z.</summary>
    public static double AxisToLowerFaceZ(double axisElevationMm, double heightMm, double heightAxisZ) =>
        axisElevationMm - (heightMm / 2.0) * heightAxisZ;

    /// <summary>OS axis Z from upper face (VH) Z.</summary>
    public static double UpperFaceToAxisZ(double upperFaceElevationMm, double heightMm, double heightAxisZ) =>
        upperFaceElevationMm - (heightMm / 2.0) * heightAxisZ;

    /// <summary>OS axis Z from lower face (SH) Z.</summary>
    public static double LowerFaceToAxisZ(double lowerFaceElevationMm, double heightMm, double heightAxisZ) =>
        lowerFaceElevationMm + (heightMm / 2.0) * heightAxisZ;

    /// <summary>
    /// Convert displayed elevation (in the current datum) to OS axis elevation.
    /// Changing datum must NOT change the physical geometry.
    /// </summary>
    public static double ToAxisElevationMm(
        double displayElevationMm,
        StructuralMemberElevationReferenceKind referenceKind,
        double heightMm,
        double heightAxisZ) => referenceKind switch
    {
        StructuralMemberElevationReferenceKind.OS => displayElevationMm,
        StructuralMemberElevationReferenceKind.VH => UpperFaceToAxisZ(displayElevationMm, heightMm, heightAxisZ),
        StructuralMemberElevationReferenceKind.SH => LowerFaceToAxisZ(displayElevationMm, heightMm, heightAxisZ),
        _ => throw new ArgumentOutOfRangeException(nameof(referenceKind)),
    };

    /// <summary>Convert OS axis elevation to display elevation in the given datum.</summary>
    public static double ToDisplayElevationMm(
        double axisElevationMm,
        StructuralMemberElevationReferenceKind referenceKind,
        double heightMm,
        double heightAxisZ) => referenceKind switch
    {
        StructuralMemberElevationReferenceKind.OS => axisElevationMm,
        StructuralMemberElevationReferenceKind.VH => AxisToUpperFaceZ(axisElevationMm, heightMm, heightAxisZ),
        StructuralMemberElevationReferenceKind.SH => AxisToLowerFaceZ(axisElevationMm, heightMm, heightAxisZ),
        _ => throw new ArgumentOutOfRangeException(nameof(referenceKind)),
    };

    // -------------------------------------------------------------------------
    // Slope calculations
    //
    // Plan projected XY length L is always used (not physical/slope length).
    // slope = atan(deltaZ / L)  range (-90°, 90°)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signed slope in degrees from start-to-end axis elevation delta and plan length.
    /// Positive = rising from Start to End. Range (-90°, 90°).
    /// </summary>
    public static double DeriveSlopeDegrees(double axisStartMm, double axisEndMm, double planLengthMm)
    {
        if (planLengthMm <= 0d) return 0d;
        return Math.Atan((axisEndMm - axisStartMm) / planLengthMm) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Derive AxisEndElevationMm from AxisStartElevationMm + slope + plan length.
    /// endZ = startZ + planLength * tan(slopeDegrees)
    /// </summary>
    public static double DeriveEndFromStartAndSlope(double axisStartMm, double planLengthMm, double slopeDegrees) =>
        axisStartMm + planLengthMm * Math.Tan(slopeDegrees * Math.PI / 180.0);

    /// <summary>
    /// Derive AxisStartElevationMm from AxisEndElevationMm + slope + plan length.
    /// startZ = endZ - planLength * tan(slopeDegrees)
    /// </summary>
    public static double DeriveStartFromEndAndSlope(double axisEndMm, double planLengthMm, double slopeDegrees) =>
        axisEndMm - planLengthMm * Math.Tan(slopeDegrees * Math.PI / 180.0);

    /// <summary>
    /// Absolute pitch (always non-negative) from slope degrees.
    /// Used for the Build State PitchDegrees field which is always non-negative.
    /// </summary>
    public static double AbsolutePitchDegrees(double slopeDegrees) => Math.Abs(slopeDegrees);

    // -------------------------------------------------------------------------
    // Eave elevation derivation (for Physical3D builder integration)
    //
    // EaveElevationMm (Build State) = upper face Z at the eave-side (lower-Z) endpoint.
    // This matches the existing AUTO rafter convention where upper face = roof plane.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Upper face Z at a given axis elevation, using HeightAxisZ.
    /// This is the value suitable for EaveElevationMm in the Build State.
    /// </summary>
    public static double DeriveEaveElevationMm(double axisAtEaveMm, double heightMm, double heightAxisZ) =>
        AxisToUpperFaceZ(axisAtEaveMm, heightMm, heightAxisZ);

    /// <summary>
    /// Returns true if the Start endpoint is the eave end (lower elevation = closer to wall plate).
    /// If both elevations are equal, Start is canonical eave end (deterministic).
    /// </summary>
    public static bool IsStartTheEaveEnd(double axisStartMm, double axisEndMm) =>
        axisStartMm <= axisEndMm;

    // -------------------------------------------------------------------------
    // Display formatting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Format slope magnitude in degrees for display: "45,00°" (SK) or "45.00°" (invariant).
    /// Always non-negative — fall direction is a separate semantic, never a signed slope.
    /// </summary>
    public static string FormatSlopeDegrees(double degrees, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        return Math.Abs(degrees).ToString("0.00", culture) + "°";
    }

    /// <summary>
    /// True when both axis endpoint elevations are equal within tolerance (horizontal member).
    /// Fall direction has no physical meaning in this state.
    /// </summary>
    public static bool IsHorizontal(double axisStartMm, double axisEndMm, double toleranceMm = 1e-9) =>
        Math.Abs(axisStartMm - axisEndMm) <= toleranceMm;

    /// <summary>
    /// True when Start→End is uphill, so the timber slope arrow must reverse to point downhill.
    /// Horizontal members keep the non-reversed default (no stale fall influence).
    /// </summary>
    public static bool ResolveIsSlopeDirectionReversedForDownhill(
        double axisStartMm, double axisEndMm, double toleranceMm = 1e-9) =>
        !IsHorizontal(axisStartMm, axisEndMm, toleranceMm) &&
        TimberSlopeDirectionRules.ResolveIsReversedForDownhillDisplay(
            axisStartMm, axisEndMm, toleranceMm);

    /// <summary>
    /// Parse a slope-degrees string ("45,00°" or "45.00°") back to degrees.
    /// Accepts both comma and dot decimal separators, with or without the ° suffix.
    /// </summary>
    public static bool TryParseSlopeDegrees(string? text, out double degrees)
    {
        degrees = 0d;
        var raw = (text ?? string.Empty).Trim().TrimEnd('°').Trim();
        if (raw.Length == 0) return false;
        var normalized = raw.Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (!IsFinite(value) || Math.Abs(value) >= 90d) return false;
        degrees = value;
        return true;
    }

    // -------------------------------------------------------------------------
    // State construction helpers
    // -------------------------------------------------------------------------

    /// <summary>Create elevation state for a sloped Ordinary rafter from OS axis elevations.</summary>
    public static StructuralMemberElevationState CreateSloped(
        double axisStartMm,
        double axisEndMm,
        StructuralMemberElevationReferenceKind displayReference = StructuralMemberElevationReferenceKind.OS,
        StructuralMemberElevationCalculationMode calculationMode =
            StructuralMemberElevationCalculationMode.LowerUpper) =>
        new(StructuralMemberElevationStateSchema.CurrentVersion,
            StructuralMemberElevationBehavior.SlopedEndpoints,
            axisStartMm, axisEndMm, displayReference, calculationMode);

    /// <summary>Create elevation state for a uniform (horizontal) member. Slope = 0.</summary>
    public static StructuralMemberElevationState CreateUniform(
        double axisElevationMm,
        StructuralMemberElevationReferenceKind displayReference = StructuralMemberElevationReferenceKind.OS,
        StructuralMemberElevationCalculationMode calculationMode =
            StructuralMemberElevationCalculationMode.LowerUpper) =>
        new(StructuralMemberElevationStateSchema.CurrentVersion,
            StructuralMemberElevationBehavior.UniformElevation,
            axisElevationMm, axisElevationMm, displayReference, calculationMode);

    /// <summary>
    /// Classic Plan2D edits (GRIP / STRETCH / LENGTHEN / MOVE / ROTATE): preserve the
    /// current absolute slope magnitude and the stationary/anchor endpoint elevation.
    /// Moved-endpoint Z is recalculated as ΔZ = planLength × tan(slopeMagnitude) with
    /// sign from the existing Start→End fall. CalculationMode is an AK_EDIT input mode
    /// only and must not drive classic-edit adaptation.
    /// </summary>
    public static bool TryAdaptForClassicPlanEdit(
        StructuralMemberElevationState current,
        RoofSegment3D previousPlan,
        RoofSegment3D newPlan,
        out StructuralMemberElevationState? adapted)
    {
        adapted = null;
        if (!IsValid(current)) return false;

        var previousPlanLengthMm = previousPlan.Start.DistanceTo(previousPlan.End);
        var newPlanLengthMm = newPlan.Start.DistanceTo(newPlan.End);
        if (previousPlanLengthMm <= 1e-7 || newPlanLengthMm <= 1e-7)
            return false;

        var change = RoofOrdinaryGripLifecycleRules.Classify(previousPlan, newPlan);
        if (change == RoofOrdinaryGripChange.None)
        {
            adapted = current with
            {
                SchemaVersion = StructuralMemberElevationStateSchema.CurrentVersion,
            };
            return true;
        }

        // Horizontal: keep both endpoint Z; slope stays 0; fall has no meaning.
        if (IsHorizontal(current.AxisStartElevationMm, current.AxisEndElevationMm))
        {
            adapted = current with
            {
                SchemaVersion = StructuralMemberElevationStateSchema.CurrentVersion,
                Behavior = StructuralMemberElevationBehavior.UniformElevation,
                AxisStartElevationMm = current.AxisStartElevationMm,
                AxisEndElevationMm = current.AxisEndElevationMm,
            };
            return IsValid(adapted);
        }

        // Signed Start→End slope encodes fall; display/store use absolute magnitude.
        var signedSlope = DeriveSlopeDegrees(
            current.AxisStartElevationMm, current.AxisEndElevationMm, previousPlanLengthMm);

        double newStart;
        double newEnd;
        switch (change)
        {
            case RoofOrdinaryGripChange.Middle:
                // MOVE: preserve XY vector / both Z / slope.
                newStart = current.AxisStartElevationMm;
                newEnd = current.AxisEndElevationMm;
                break;
            case RoofOrdinaryGripChange.End:
                // Preserve Start (anchor) Z + slope; recalculate End Z.
                newStart = current.AxisStartElevationMm;
                newEnd = DeriveEndFromStartAndSlope(newStart, newPlanLengthMm, signedSlope);
                break;
            case RoofOrdinaryGripChange.Start:
                // Preserve End (anchor) Z + slope; recalculate Start Z.
                newEnd = current.AxisEndElevationMm;
                newStart = DeriveStartFromEndAndSlope(newEnd, newPlanLengthMm, signedSlope);
                break;
            case RoofOrdinaryGripChange.Both:
                // ROTATE / non-rigid both-end edit: preserve Start Z + slope magnitude/fall.
                // Same plan length ⇒ End Z unchanged; length change scales ΔZ with tan(slope).
                newStart = current.AxisStartElevationMm;
                newEnd = DeriveEndFromStartAndSlope(newStart, newPlanLengthMm, signedSlope);
                break;
            default:
                return false;
        }

        adapted = current with
        {
            SchemaVersion = StructuralMemberElevationStateSchema.CurrentVersion,
            Behavior = IsHorizontal(newStart, newEnd)
                ? StructuralMemberElevationBehavior.UniformElevation
                : StructuralMemberElevationBehavior.SlopedEndpoints,
            AxisStartElevationMm = newStart,
            AxisEndElevationMm = newEnd,
        };
        return IsValid(adapted);
    }

    /// <summary>
    /// AK_EDIT calculation-mode adaptation after an explicit elevation dialog Apply.
    /// Not used by classic GRIP / STRETCH / LENGTHEN / MOVE / ROTATE — those call
    /// <see cref="TryAdaptForClassicPlanEdit"/>.
    /// </summary>
    public static bool TryAdaptForPlanChange(
        StructuralMemberElevationState current,
        double previousPlanLengthMm,
        double newPlanLengthMm,
        out StructuralMemberElevationState? adapted)
    {
        adapted = null;
        if (!IsValid(current) || previousPlanLengthMm <= 1e-7 || newPlanLengthMm <= 1e-7)
            return false;

        var startIsLower = IsStartTheEaveEnd(current.AxisStartElevationMm, current.AxisEndElevationMm);
        var lowerZ = startIsLower ? current.AxisStartElevationMm : current.AxisEndElevationMm;
        var upperZ = startIsLower ? current.AxisEndElevationMm : current.AxisStartElevationMm;
        var signedSlope = DeriveSlopeDegrees(
            current.AxisStartElevationMm, current.AxisEndElevationMm, previousPlanLengthMm);
        var absSlope = AbsolutePitchDegrees(signedSlope);

        double newStart;
        double newEnd;
        switch (current.CalculationMode)
        {
            case StructuralMemberElevationCalculationMode.LowerUpper:
                // Both semantic endpoint elevations stay fixed; slope follows new plan length.
                newStart = startIsLower ? lowerZ : upperZ;
                newEnd = startIsLower ? upperZ : lowerZ;
                break;
            case StructuralMemberElevationCalculationMode.LowerSlope:
                // Lower Z + absolute slope stay fixed; upper Z is recalculated.
                if (startIsLower)
                {
                    newStart = lowerZ;
                    newEnd = DeriveEndFromStartAndSlope(lowerZ, newPlanLengthMm, absSlope);
                }
                else
                {
                    newEnd = lowerZ;
                    newStart = DeriveStartFromEndAndSlope(lowerZ, newPlanLengthMm, -absSlope);
                }
                break;
            case StructuralMemberElevationCalculationMode.UpperSlope:
                // Upper Z + absolute slope stay fixed; lower Z is recalculated.
                if (startIsLower)
                {
                    newEnd = upperZ;
                    newStart = DeriveStartFromEndAndSlope(upperZ, newPlanLengthMm, absSlope);
                }
                else
                {
                    newStart = upperZ;
                    newEnd = DeriveEndFromStartAndSlope(upperZ, newPlanLengthMm, -absSlope);
                }
                break;
            default:
                return false;
        }

        adapted = current with
        {
            SchemaVersion = StructuralMemberElevationStateSchema.CurrentVersion,
            Behavior = IsHorizontal(newStart, newEnd)
                ? StructuralMemberElevationBehavior.UniformElevation
                : StructuralMemberElevationBehavior.SlopedEndpoints,
            AxisStartElevationMm = newStart,
            AxisEndElevationMm = newEnd,
        };
        return IsValid(adapted);
    }

    /// <summary>
    /// Pitch of a unit longitudinal axis from its Z component (degrees, absolute).
    /// </summary>
    public static double PitchDegreesFromLongitudinalAxis(RoofPoint3D longitudinalAxis)
    {
        var z = Math.Abs(longitudinalAxis.Z);
        if (z > 1d) z = 1d;
        return Math.Asin(z) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Target Elevation pitch, build-state pitch, and measured section-frame pitch must agree.
    /// </summary>
    public static bool ElevationPhysicalPitchesAgree(
        StructuralMemberElevationState elevation,
        RoofSegment3D plan,
        RoofOrdinaryPhysicalBuildState buildState,
        double toleranceDegrees = 0.05d)
    {
        if (!IsValid(elevation) || !RoofOrdinaryPhysicalBuildStateRules.IsValid(buildState))
            return false;
        var planLength = plan.Start.DistanceTo(plan.End);
        if (planLength <= 1e-7 || buildState.SectionFrame is null) return false;
        var target = AbsolutePitchDegrees(
            DeriveSlopeDegrees(elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, planLength));
        var stored = AbsolutePitchDegrees(buildState.PitchDegrees);
        var measured = PitchDegreesFromLongitudinalAxis(buildState.SectionFrame.LongitudinalAxis);
        return Math.Abs(target - stored) <= toleranceDegrees &&
               Math.Abs(target - measured) <= toleranceDegrees &&
               Math.Abs(stored - measured) <= toleranceDegrees;
    }

    // -------------------------------------------------------------------------
    // Derive elevation state from AUTO Build State (legacy resolution)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derive elevation state for an AUTO ordinary rafter from its stored Build State.
    /// The OS axis elevation at each plan endpoint is computed from EaveElevationMm,
    /// PitchDegrees, and the Anchor boundary roles.
    ///
    /// This is the "legacy resolution" path for existing AUTO members that do not yet
    /// have a persisted StructuralMemberElevationState.
    /// </summary>
    public static bool TryDeriveFromBuildState(
        RoofOrdinaryPhysicalBuildState state,
        RoofSegment3D plan,
        out StructuralMemberElevationState? elevation)
    {
        elevation = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.IsValid(state)) return false;
        var pitchRad = state.PitchDegrees * Math.PI / 180.0;
        var haz = Math.Cos(pitchRad); // HeightAxisZ = cos(pitch)
        var axisAtEave = UpperFaceToAxisZ(state.EaveElevationMm, state.HeightMm, haz);
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength <= 1e-7) return false;
        var tanPitch = Math.Tan(pitchRad);
        // Eave boundary determines which plan endpoint is the lower end.
        var eaveAtStart = state.Anchor.StartBoundaryRole == RoofRafterBoundaryRole.Eave;
        var axisStart = eaveAtStart
            ? axisAtEave
            : axisAtEave + planLength * tanPitch;
        var axisEnd = eaveAtStart
            ? axisAtEave + planLength * tanPitch
            : axisAtEave;
        elevation = CreateSloped(axisStart, axisEnd);
        return IsValid(elevation);
    }

    /// <summary>
    /// Derive OS axis elevations from measured upper-face Z at Plan2D Start and End.
    /// Used when a measured Physical3D package is available (Independent migration / BRep).
    /// Slope is derived from the upper-face delta (identical to axis delta for a prismatic section).
    /// Never invents a synthetic flat member when endpoints differ.
    /// </summary>
    public static bool TryDeriveFromUpperFaceEndpoints(
        double upperFaceStartZMm,
        double upperFaceEndZMm,
        double heightMm,
        double planLengthMm,
        StructuralMemberElevationReferenceKind displayReference,
        out StructuralMemberElevationState? elevation)
    {
        elevation = null;
        if (!IsFinite(upperFaceStartZMm) || !IsFinite(upperFaceEndZMm) ||
            !IsFinite(heightMm) || heightMm <= 0d ||
            !IsFinite(planLengthMm) || planLengthMm <= 1e-7)
            return false;
        var slopeDeg = DeriveSlopeDegrees(upperFaceStartZMm, upperFaceEndZMm, planLengthMm);
        var haz = HeightAxisZ(AbsolutePitchDegrees(slopeDeg));
        var axisStart = UpperFaceToAxisZ(upperFaceStartZMm, heightMm, haz);
        var axisEnd = UpperFaceToAxisZ(upperFaceEndZMm, heightMm, haz);
        elevation = CreateSloped(axisStart, axisEnd, displayReference);
        return IsValid(elevation);
    }

    /// <summary>
    /// Reject synthetic flat elevation when the member's known pitch is clearly non-zero.
    /// Existing AUTO/Independent members must never display invented 0/0/0 seating.
    /// </summary>
    public static bool IsPlausibleForKnownPitch(
        StructuralMemberElevationState state,
        double planLengthMm,
        double knownPitchDegrees,
        double slopeToleranceDegrees = 1.0d)
    {
        if (!IsValid(state) || planLengthMm <= 1e-7 || !IsFinite(knownPitchDegrees))
            return false;
        var derived = AbsolutePitchDegrees(
            DeriveSlopeDegrees(state.AxisStartElevationMm, state.AxisEndElevationMm, planLengthMm));
        var known = AbsolutePitchDegrees(knownPitchDegrees);
        // Known near-horizontal: derived may also be near zero.
        if (known < slopeToleranceDegrees)
            return derived < slopeToleranceDegrees * 2d;
        // Known pitched: derived must not be synthetic flat and must match within tolerance.
        return derived >= slopeToleranceDegrees &&
               Math.Abs(derived - known) <= slopeToleranceDegrees;
    }

    // -------------------------------------------------------------------------
    // Build State patching for Physical3D rebuild with custom elevation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produce a patched Build State that positions the physical member at the elevations
    /// specified by the elevation state. The plan XY axis and section dimensions are preserved.
    /// The section frame is updated to match the new slope.
    ///
    /// The patched state is suitable for RoofOrdinaryPhysicalBuildStateRules.TryBuild
    /// (used in RoofOrdinaryGripLifecycleService.Accept).
    ///
    /// V1 assumption: the structural orientation (eave / ridge boundary roles from the Anchor)
    /// is preserved. The eave end is always the lower-Z endpoint. Reversal of structural
    /// orientation when slope sign changes is deferred to V2.
    /// </summary>
    public static bool TryPatchBuildStateForElevation(
        RoofOrdinaryPhysicalBuildState source,
        RoofSegment3D plan,
        StructuralMemberElevationState elevation,
        out RoofOrdinaryPhysicalBuildState? patched)
    {
        patched = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.IsValid(source) || !IsValid(elevation)) return false;

        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength <= 1e-7) return false;

        var axisStart = elevation.AxisStartElevationMm;
        var axisEnd = elevation.AxisEndElevationMm;
        var deltaZ = axisEnd - axisStart;
        var slopeRad = Math.Atan(deltaZ / planLength);        // signed slope (Start→End)
        var pitchRad = Math.Abs(slopeRad);                    // absolute pitch
        var newPitchDeg = pitchRad * (180.0 / Math.PI);
        var newHeightAxisZ = Math.Cos(pitchRad);

        // Eave is the lower-elevation end. Eave upper face = our new EaveElevationMm.
        var eaveAtStart = IsStartTheEaveEnd(axisStart, axisEnd);
        var axisAtEave = eaveAtStart ? axisStart : axisEnd;
        var newEaveElevationMm = DeriveEaveElevationMm(axisAtEave, source.HeightMm, newHeightAxisZ);

        // Longitudinal axis: unit vector from eave toward ridge (uphill direction).
        var planDirX = dx / planLength;
        var planDirY = dy / planLength;
        // Sign: longitudinal goes from eave to ridge. If eave is at Start, direction = Start→End; else reversed.
        var dirSign = eaveAtStart ? 1.0 : -1.0;
        var lx = planDirX * Math.Cos(slopeRad) * dirSign;
        var ly = planDirY * Math.Cos(slopeRad) * dirSign;
        var lz = Math.Sin(Math.Abs(slopeRad)); // always rising toward ridge

        // Width axis: horizontal, perpendicular to plan direction (always Z=0).
        var wx = -planDirY * dirSign;
        var wy = planDirX * dirSign;
        // wz = 0 by construction

        // Height axis = L × W (pointing from lower face toward upper face; must have Z > 0).
        // H = L × W:
        // hx = ly * wz - lz * wy  = ly * 0 - lz * wy = -lz * wy
        // hy = lz * wx - lx * wz  = lz * wx
        // hz = lx * wy - ly * wx
        var hx = -lz * wy;
        var hy = lz * wx;
        var hz = lx * wy - ly * wx;

        // Ensure H.Z > 0 (upper face above lower face in world Z).
        if (hz < 0) { hx = -hx; hy = -hy; hz = -hz; }

        // Normalise (guard against floating-point drift — should already be unit vectors).
        var hLen = Math.Sqrt(hx * hx + hy * hy + hz * hz);
        if (hLen < 1e-9) return false;
        hx /= hLen; hy /= hLen; hz /= hLen;

        var newFrame = new RoofOrdinarySectionFrame(
            new RoofPoint3D(lx, ly, lz),
            new RoofPoint3D(wx, wy, 0.0),
            new RoofPoint3D(hx, hy, hz));

        if (!RoofOrdinarySectionTransportRules.IsValid(newFrame)) return false;

        patched = source with
        {
            Version = RoofOrdinaryPhysicalBuildState.CurrentVersion,
            PitchDegrees = newPitchDeg,
            EaveElevationMm = newEaveElevationMm,
            SectionFrame = newFrame,
        };
        return RoofOrdinaryPhysicalBuildStateRules.IsValid(patched);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
}
