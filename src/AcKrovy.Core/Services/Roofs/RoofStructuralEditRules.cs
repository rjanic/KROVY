using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofStructuralNativeAction { Unclaimed, AcceptPlan, RestorePlan, RebuildPhysical, RejectClone, AcceptManualClone }

/// <summary>
/// Product disposition for the structural Hip/Valley native-edit matrix.
/// Whole-roof COPY/MIRROR remain owned by the existing roof rebind path.
/// </summary>
public enum RoofStructuralCommandDisposition { Supported, ExplicitlyRejected, Deferred }

/// <summary>
/// Geometry-semantic class of an accepted Structural Plan override relative to the
/// automatic fold. Command names (TRIM/STRETCH/…) are not the primary materialization key.
/// </summary>
public enum RoofStructuralPlanEditClass
{
    Automatic,
    OffsetRigid,
    OnFoldSubsegment,
    OnFoldExtendedSegment,
    ArbitraryPlanLine,
    Suppressed,
}

public static class RoofStructuralEditRules
{
    private const double Tolerance = 1e-6;
    private const double PlanLateralToleranceMm = 1d;
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static bool IsValid(RoofStructuralEditState? state) =>
        state is { SchemaVersion: 1, Members: not null } &&
        state.Members.All(item => item is not null && item.LogicalKey is not null &&
            item.LogicalKey.Role is RoofStructuralRole.Hip or RoofStructuralRole.Valley &&
            item.LogicalKey.BoundaryEdgeIdA > 0 && item.LogicalKey.BoundaryEdgeIdB > 0 &&
            item.LogicalKey.BoundaryEdgeIdA < item.LogicalKey.BoundaryEdgeIdB &&
            IsFinite(item.OffsetXmm) && IsFinite(item.OffsetYmm) &&
            IsAbsolutePlanValid(item)) &&
        state.Members.Select(item => item.LogicalKey).Distinct().Count() == state.Members.Count;

    private static bool IsAbsolutePlanValid(RoofStructuralMemberEdit item)
    {
        var present = (item.PlanStartXmm is null ? 0 : 1) + (item.PlanStartYmm is null ? 0 : 1) +
                      (item.PlanEndXmm is null ? 0 : 1) + (item.PlanEndYmm is null ? 0 : 1);
        if (present == 0) return true;
        if (present != 4 || !item.HasAbsolutePlan) return false;
        var startX = item.PlanStartXmm!.Value;
        var startY = item.PlanStartYmm!.Value;
        var endX = item.PlanEndXmm!.Value;
        var endY = item.PlanEndYmm!.Value;
        if (!IsFinite(startX) || !IsFinite(startY) || !IsFinite(endX) || !IsFinite(endY)) return false;
        var dx = endX - startX;
        var dy = endY - startY;
        return Math.Sqrt(dx * dx + dy * dy) > Tolerance;
    }

    public static bool HasFirstClaimOpportunity(string? command) =>
        IsPlanAcceptCommand(command) ||
        IsPlanGeometryAcceptCommand(command) ||
        IsPlanRestoreCommand(command) ||
        IsManualCloneAcceptCommand(command) ||
        IsCloneRejectCommand(command) ||
        IsPhysicalRecoveryCommand(command);

    public static bool RequiresAssemblySnapshotCapture(string? command) =>
        HasFirstClaimOpportunity(command);

    public static bool IsPlanAcceptCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is "MOVE" or "ERASE";

    /// <summary>
    /// Unlocked Plan2D on-fold geometry overrides (STRETCH/TRIM/EXTEND/GRIP).
    /// ROTATE is RestorePlan: an arbitrary Plan rotation is not a valid Hip/Valley fold member.
    /// </summary>
    public static bool IsPlanGeometryAcceptCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is
            "STRETCH" or "GRIP_STRETCH" or "TRIM" or "EXTEND";

    public static bool IsPlanRestoreCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is
            "ROTATE" or "SCALE" or "BREAK" or "BREAKATPOINT" or
            "FILLET" or "CHAMFER" or "JOIN" or "OFFSET" or "EXPLODE";

    /// <summary>
    /// Unlocked COPY/MIRROR of Generated Structural Plan2D → AttachedManualStructural.
    /// Placement preserves the command's translation/reflection from final clone geometry.
    /// OFFSET stays RestorePlan: a 2D offset is not a rigid 3D transform.
    /// ARRAY* stays RejectClone.
    /// </summary>
    public static bool IsManualCloneAcceptCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is "COPY" or "MIRROR";

    public static bool IsCloneRejectCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is
            "ARRAY" or "ARRAYRECT" or "ARRAYPOLAR" or "ARRAYPATH";

    public static bool IsPlanFoundationCommand(string? command) =>
        IsPlanAcceptCommand(command);

    public static bool IsPhysicalRecoveryCommand(string? command) =>
        IsPlanAcceptCommand(command) ||
        IsPlanGeometryAcceptCommand(command) ||
        IsPlanRestoreCommand(command) ||
        IsManualCloneAcceptCommand(command) ||
        IsCloneRejectCommand(command);

    public static RoofStructuralNativeAction Classify(string? command, bool physical, RoofEditState editState)
    {
        if (IsManualCloneAcceptCommand(command))
            return editState == RoofEditState.Unlocked
                ? RoofStructuralNativeAction.AcceptManualClone
                : RoofStructuralNativeAction.RejectClone;
        if (IsCloneRejectCommand(command))
            return RoofStructuralNativeAction.RejectClone;
        if (physical)
            return IsPhysicalRecoveryCommand(command)
                ? RoofStructuralNativeAction.RebuildPhysical
                : RoofStructuralNativeAction.Unclaimed;
        // Locked Plan2D reject/geometry families: Locked generated-member guard owns restore.
        if (IsPlanGeometryAcceptCommand(command) || IsPlanRestoreCommand(command))
            return editState == RoofEditState.Locked
                ? RoofStructuralNativeAction.Unclaimed
                : IsPlanGeometryAcceptCommand(command)
                    ? RoofStructuralNativeAction.AcceptPlan
                    : RoofStructuralNativeAction.RestorePlan;
        if (!IsPlanAcceptCommand(command))
            return RoofStructuralNativeAction.Unclaimed;
        return editState == RoofEditState.Unlocked
            ? RoofStructuralNativeAction.AcceptPlan
            : RoofStructuralNativeAction.RestorePlan;
    }

    public static RoofStructuralCommandDisposition GetDisposition(string? command)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(command);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(normalized))
            return RoofStructuralCommandDisposition.Supported;
        if (normalized is "SAVE" or "CLOSE" or "OPEN" or "QSAVE" or "SAVEAS")
            return RoofStructuralCommandDisposition.Supported;
        if (IsManualCloneAcceptCommand(normalized))
            return RoofStructuralCommandDisposition.Supported;
        if (IsCloneRejectCommand(normalized))
            return RoofStructuralCommandDisposition.ExplicitlyRejected;
        if (IsPlanGeometryAcceptCommand(normalized) || IsPlanAcceptCommand(normalized))
            return RoofStructuralCommandDisposition.Supported;
        if (IsPlanRestoreCommand(normalized))
            return RoofStructuralCommandDisposition.ExplicitlyRejected;
        return RoofStructuralCommandDisposition.Deferred;
    }

    public static RoofStructuralAttachedManualCreationKind CreationKindForCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) switch
        {
            "MIRROR" => RoofStructuralAttachedManualCreationKind.Mirror,
            "OFFSET" => RoofStructuralAttachedManualCreationKind.Offset,
            "ARRAY" or "ARRAYRECT" or "ARRAYPOLAR" or "ARRAYPATH"
                => RoofStructuralAttachedManualCreationKind.Array,
            _ => RoofStructuralAttachedManualCreationKind.Copy,
        };

    public static bool IsStructuralHipValleyRole(RoofStructuralRole? role) =>
        role is RoofStructuralRole.Hip or RoofStructuralRole.Valley;

    public static RoofStructuralMemberEdit Get(RoofStructuralEditState state, RoofStructuralLogicalKey key)
    {
        if (!IsValid(state)) throw new ArgumentException("Invalid structural edit state.", nameof(state));
        return state.Members.SingleOrDefault(item => item.LogicalKey == key) ?? new(key, 0, 0, false);
    }

    public static RoofStructuralEditState Upsert(RoofStructuralEditState state, RoofStructuralMemberEdit edit)
    {
        if (!IsValid(state)) throw new ArgumentException("Invalid structural edit state.");
        var result = new RoofStructuralEditState(1, state.Members.Where(item => item.LogicalKey != edit.LogicalKey)
            .Append(edit).OrderBy(item => item.LogicalKey.ToString(), StringComparer.Ordinal).ToArray());
        if (!IsValid(state) || !IsValid(result)) throw new ArgumentException("Invalid structural edit state.");
        return result;
    }

    public static bool TryAcceptMove(RoofStructuralEditState state, RoofStructuralLogicalKey key,
        RoofSegment3D before, RoofSegment3D after, out RoofStructuralEditState accepted)
    {
        accepted = state;
        if (!IsValid(state) || Get(state, key).Suppressed) return false;
        var current = Get(state, key);
        // Absolute geometry overrides are authoritative; a pure MOVE of an absolute
        // member updates the absolute endpoints instead of composing Offset.
        if (current.HasAbsolutePlan)
            return TryAcceptPlanGeometry(state, key, after, out accepted);
        var points = new[] { before.Start, before.End, after.Start, after.End };
        if (points.Any(p => !IsFinite(p.X) || !IsFinite(p.Y) ||
                            !IsFinite(p.Z) || Math.Abs(p.Z) > Tolerance)) return false;
        var dx = after.Start.X - before.Start.X;
        var dy = after.Start.Y - before.Start.Y;
        if (Math.Abs(after.End.X - before.End.X - dx) > Tolerance ||
            Math.Abs(after.End.Y - before.End.Y - dy) > Tolerance || before.LengthMm <= Tolerance) return false;
        if (!IsFinite(current.OffsetXmm + dx) || !IsFinite(current.OffsetYmm + dy)) return false;
        accepted = Upsert(state, current with { OffsetXmm = current.OffsetXmm + dx, OffsetYmm = current.OffsetYmm + dy });
        return true;
    }

    /// <summary>
    /// Persists unlocked Plan2D geometry as absolute XY endpoints (Z forced to 0).
    /// Clears Offset so ApplyPlan is not double-applied. Without a canonical fold,
    /// only Z=0 / non-degenerate checks apply (legacy). Prefer the canonical overload.
    /// </summary>
    public static bool TryAcceptPlanGeometry(RoofStructuralEditState state, RoofStructuralLogicalKey key,
        RoofSegment3D after, out RoofStructuralEditState accepted) =>
        TryAcceptPlanGeometry(state, key, after, canonicalPlan: null, out accepted);

    /// <summary>
    /// Accepts on-fold / parallel-offset Plan geometry relative to the automatic fold.
    /// Arbitrary off-fold Plan (skew/rotate) is rejected. Exact canonical restore
    /// normalizes back to Automatic (no persistent Plan equal to automatic).
    /// </summary>
    public static bool TryAcceptPlanGeometry(RoofStructuralEditState state, RoofStructuralLogicalKey key,
        RoofSegment3D after, RoofSegment3D? canonicalPlan, out RoofStructuralEditState accepted)
    {
        accepted = state;
        if (!IsValid(state) || Get(state, key).Suppressed) return false;
        var points = new[] { after.Start, after.End };
        if (points.Any(p => !IsFinite(p.X) || !IsFinite(p.Y) ||
                            !IsFinite(p.Z) || Math.Abs(p.Z) > Tolerance)) return false;
        var dx = after.End.X - after.Start.X;
        var dy = after.End.Y - after.Start.Y;
        if (Math.Sqrt(dx * dx + dy * dy) <= Tolerance) return false;
        if (canonicalPlan is { } canonical)
        {
            var canXy = PlanXy(canonical);
            var afterXy = PlanXy(after);
            if (PlanEndpointsMatch(canXy, afterXy) || PlanEndpointsMatch(canXy, ReversePlan(afterXy)))
            {
                accepted = ClearOverride(state, key);
                return true;
            }

            var editClass = ClassifyPlanGeometry(canXy, afterXy);
            if (editClass is RoofStructuralPlanEditClass.ArbitraryPlanLine)
                return false;
        }

        var edit = new RoofStructuralMemberEdit(
            key, 0, 0, false,
            after.Start.X, after.Start.Y, after.End.X, after.End.Y);
        if (!IsValid(new(1, new[] { edit }))) return false;
        accepted = Upsert(state, edit);
        return true;
    }

    /// <summary>Removes Offset/Plan override for a key (Automatic). Suppression is separate.</summary>
    public static RoofStructuralEditState ClearOverride(RoofStructuralEditState state, RoofStructuralLogicalKey key)
    {
        if (!IsValid(state)) throw new ArgumentException("Invalid structural edit state.");
        var current = Get(state, key);
        if (current.Suppressed)
            return Upsert(state, new RoofStructuralMemberEdit(key, 0, 0, true));
        return new RoofStructuralEditState(1,
            state.Members.Where(item => item.LogicalKey != key).ToArray());
    }

    public static RoofStructuralPlanEditClass ClassifyMemberEdit(
        RoofSegment3D canonicalPlan, RoofStructuralMemberEdit edit)
    {
        if (edit.Suppressed) return RoofStructuralPlanEditClass.Suppressed;
        if (edit.HasAbsolutePlan)
            return ClassifyPlanGeometry(PlanXy(canonicalPlan), AbsolutePlanXy(edit));
        if (Math.Abs(edit.OffsetXmm) <= Tolerance && Math.Abs(edit.OffsetYmm) <= Tolerance)
            return RoofStructuralPlanEditClass.Automatic;
        return RoofStructuralPlanEditClass.OffsetRigid;
    }

    /// <summary>
    /// Classifies edited Plan XY against the automatic fold Plan XY (both Z=0).
    /// Parallel + near-fold → OnFold*; parallel + common lateral → OffsetRigid;
    /// otherwise ArbitraryPlanLine (not a valid Structural Hip/Valley fold member).
    /// </summary>
    public static RoofStructuralPlanEditClass ClassifyPlanGeometry(
        RoofSegment3D canonicalPlanXy, RoofSegment3D afterPlanXy)
    {
        var from = PlanXy(canonicalPlanXy);
        var to = PlanXy(afterPlanXy);
        if (from.LengthMm <= Tolerance || to.LengthMm <= Tolerance)
            return RoofStructuralPlanEditClass.ArbitraryPlanLine;
        if (PlanEndpointsMatch(from, to) || PlanEndpointsMatch(from, ReversePlan(to)))
            return RoofStructuralPlanEditClass.Automatic;
        var fromDirX = from.End.X - from.Start.X;
        var fromDirY = from.End.Y - from.Start.Y;
        var toDirX = to.End.X - to.Start.X;
        var toDirY = to.End.Y - to.Start.Y;
        var cross = fromDirX * toDirY - fromDirY * toDirX;
        var dot = fromDirX * toDirX + fromDirY * toDirY;
        var parallel = Math.Abs(cross) <= Tolerance * from.LengthMm * to.LengthMm &&
                       Math.Abs(dot) > Tolerance;
        if (!parallel) return RoofStructuralPlanEditClass.ArbitraryPlanLine;
        var t0 = PlanParameter(from, to.Start);
        var t1 = PlanParameter(from, to.End);
        var lat0 = PlanLateral(from, to.Start);
        var lat1 = PlanLateral(from, to.End);
        if (Math.Abs(lat0 - lat1) > PlanLateralToleranceMm)
            return RoofStructuralPlanEditClass.ArbitraryPlanLine;
        var onFold = Math.Abs(lat0) <= PlanLateralToleranceMm &&
                     Math.Abs(lat1) <= PlanLateralToleranceMm;
        var tLo = Math.Min(t0, t1);
        var tHi = Math.Max(t0, t1);
        if (!onFold) return RoofStructuralPlanEditClass.OffsetRigid;
        if (tLo < -1e-6 || tHi > 1 + 1e-6)
            return RoofStructuralPlanEditClass.OnFoldExtendedSegment;
        return RoofStructuralPlanEditClass.OnFoldSubsegment;
    }

    public static RoofSegment3D ResolvePlanSegment(RoofSegment3D canonical, RoofStructuralMemberEdit edit)
    {
        if (edit.HasAbsolutePlan)
        {
            return new(
                new(edit.PlanStartXmm!.Value, edit.PlanStartYmm!.Value, canonical.Start.Z),
                new(edit.PlanEndXmm!.Value, edit.PlanEndYmm!.Value, canonical.End.Z));
        }

        return Place(canonical, edit);
    }

    public static RoofSegment3D Place(RoofSegment3D segment, RoofStructuralMemberEdit edit) =>
        edit.HasAbsolutePlan ? ResolvePlanSegment(segment, edit) : new(Place(segment.Start, edit), Place(segment.End, edit));

    public static IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> ApplyPlan(
        IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> canonical, RoofStructuralEditState state)
    {
        if (!IsValid(state)) throw new ArgumentException("Invalid structural edit state.");
        return canonical.Where(item => !Get(state, item.LogicalKey).Suppressed)
            .Select(item => item with { Segment3D = ResolvePlanSegment(item.Segment3D, Get(state, item.LogicalKey)) }).ToArray();
    }

    private static RoofPoint3D Place(RoofPoint3D point, RoofStructuralMemberEdit edit) =>
        new(point.X + edit.OffsetXmm, point.Y + edit.OffsetYmm, point.Z);

    /// <summary>
    /// Replays accepted placement on freshly built CAD-neutral geometry. Absolute Plan
    /// overrides lift Plan XY onto the structural 3D axis (roof elevation) and remap
    /// the body in that axis frame — never an XY-only scale that collapses Z. Offset
    /// remains a pure XY translation. Never a Solid3d readback.
    /// </summary>
    public static RoofStructuralRafterPolyhedron Place(
        RoofStructuralRafterPolyhedron model,
        RoofSegment3D canonicalPlanSegment,
        RoofStructuralMemberEdit edit)
    {
        if (model.StructuralKey != edit.LogicalKey || edit.Suppressed ||
            !IsValid(new(1, new[] { edit }))) throw new ArgumentException("Invalid structural placement.");
        if (edit.HasAbsolutePlan)
        {
            _ = canonicalPlanSegment; // caller supplies plan authority; body UpperAxis is the 3D lift source
            return TransformAbsolutePlan(model, AbsolutePlanXy(edit));
        }

        RoofPoint3D Point(RoofPoint3D point) => Place(point, edit);
        return Map(model, Point);
    }

    /// <summary>Backward-compatible Offset-only placement when absolute plan is absent.</summary>
    public static RoofStructuralRafterPolyhedron Place(RoofStructuralRafterPolyhedron model, RoofStructuralMemberEdit edit)
    {
        if (edit.HasAbsolutePlan)
            throw new ArgumentException("Absolute plan placement requires the canonical plan segment.", nameof(edit));
        return Place(model, new RoofSegment3D(new(0, 0, 0), new(1, 0, 0)), edit);
    }

    public static string PhysicalSignatureToken(RoofStructuralMemberEdit edit) =>
        edit.HasAbsolutePlan
            ? string.Join("|", "Plan",
                edit.PlanStartXmm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                edit.PlanStartYmm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                edit.PlanEndXmm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                edit.PlanEndYmm!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            : string.Join("|", "Automatic",
                edit.OffsetXmm.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                edit.OffsetYmm.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    private static RoofSegment3D PlanXy(RoofSegment3D segment) =>
        new(new(segment.Start.X, segment.Start.Y, 0), new(segment.End.X, segment.End.Y, 0));

    private static RoofSegment3D AbsolutePlanXy(RoofStructuralMemberEdit edit) =>
        new(new(edit.PlanStartXmm!.Value, edit.PlanStartYmm!.Value, 0),
            new(edit.PlanEndXmm!.Value, edit.PlanEndYmm!.Value, 0));

    /// <summary>
    /// Plan2D stays Z=0. Physical derives elevation from the structural UpperAxis.
    /// On-fold (near fold line): extract/extend end stations. Parallel offset of an
    /// on-fold subsegment (MOVE then TRIM): extract then XY-translate. Skew/rotate
    /// off-fold is rejected at accept time and must not reach Place.
    /// </summary>
    private static RoofStructuralRafterPolyhedron TransformAbsolutePlan(
        RoofStructuralRafterPolyhedron model, RoofSegment3D toPlan)
    {
        var from3d = model.Geometry.UpperAxis;
        if (from3d.LengthMm <= Tolerance || toPlan.LengthMm <= Tolerance)
            throw new ArgumentException("Plan transform requires non-zero axes.");
        var fromPlan = PlanXy(from3d);
        if (PlanEndpointsMatch(fromPlan, toPlan) || PlanEndpointsMatch(fromPlan, ReversePlan(toPlan)))
            return model;
        var t0 = PlanParameter(fromPlan, toPlan.Start);
        var t1 = PlanParameter(fromPlan, toPlan.End);
        var fromDirX = fromPlan.End.X - fromPlan.Start.X;
        var fromDirY = fromPlan.End.Y - fromPlan.Start.Y;
        var toDirX = toPlan.End.X - toPlan.Start.X;
        var toDirY = toPlan.End.Y - toPlan.Start.Y;
        var fromPlanLen = fromPlan.LengthMm;
        var cross = fromDirX * toDirY - fromDirY * toDirX;
        var dot = fromDirX * toDirX + fromDirY * toDirY;
        var parallel = Math.Abs(cross) <= Tolerance * fromPlanLen * toPlan.LengthMm &&
                       Math.Abs(dot) > Tolerance;
        if (!parallel)
            throw new ArgumentException("Arbitrary off-fold Plan is not a valid structural placement.");
        var lat0 = PlanLateral(fromPlan, toPlan.Start);
        var lat1 = PlanLateral(fromPlan, toPlan.End);
        if (Math.Abs(lat0 - lat1) > PlanLateralToleranceMm)
            throw new ArgumentException("Skew Plan is not a valid structural placement.");
        var tLo = Math.Min(t0, t1);
        var tHi = Math.Max(t0, t1);
        if (tHi - tLo <= Tolerance / Math.Max(from3d.LengthMm, 1))
            throw new ArgumentException("Plan subsegment length is degenerate.");
        var onFold = Math.Abs(lat0) <= PlanLateralToleranceMm &&
                     Math.Abs(lat1) <= PlanLateralToleranceMm;
        RoofStructuralRafterPolyhedron onAxis;
        if (tLo < -1e-6 || tHi > 1 + 1e-6)
        {
            var to3d = new RoofSegment3D(Lerp(from3d, t0), Lerp(from3d, t1));
            onAxis = RemapAxisFrame(model, from3d, to3d);
        }
        else
            onAxis = ExtractAxisSubsegment(model, from3d, t0, t1);

        if (onFold) return onAxis;
        // Parallel OffsetRigid absolute Plan: keep shortened/extended timber, then translate.
        var onStart = Lerp(fromPlan, t0);
        var dx = toPlan.Start.X - onStart.X;
        var dy = toPlan.Start.Y - onStart.Y;
        if (Math.Abs(dx) <= Tolerance && Math.Abs(dy) <= Tolerance) return onAxis;
        return Map(onAxis, point => new(point.X + dx, point.Y + dy, point.Z));
    }

    /// <summary>
    /// Shorten an on-fold timber by editing only moved end stations.
    /// Polyhedron UpperAxis is eave(boundary)→ridge(upper). Plan Start/End labels
    /// are classified by parameter on that axis — never by Line endpoint order alone.
    /// Source prism stays at roof elevation; CreateSolid slices the remnant.
    /// </summary>
    private static RoofStructuralRafterPolyhedron ExtractAxisSubsegment(
        RoofStructuralRafterPolyhedron model, RoofSegment3D from3d, double tStart, double tEnd)
    {
        const double paramEps = 1e-6;
        // from3d.Start = eave/boundary station; from3d.End = ridge/upper station.
        var to3d = new RoofSegment3D(Lerp(from3d, tStart), Lerp(from3d, tEnd));
        var frame = BuildFrame(to3d);
        var tLo = Math.Min(tStart, tEnd);
        var tHi = Math.Max(tStart, tEnd);
        var eaveEdited = tLo > paramEps;
        var ridgeEdited = tHi < 1d - paramEps;
        var lower = model.LowerEndClipPlanes;
        var ridge = model.RidgeClipPlane;
        var miter = model.UpperNodeMiterPlane;
        var envelope = model.RoofEnvelopeClipPlanes;
        if (eaveEdited)
        {
            var station = Lerp(from3d, tLo);
            var retainTowardTimber = tStart <= tEnd
                ? frame.Dir
                : new RoofPoint3D(-frame.Dir.X, -frame.Dir.Y, -frame.Dir.Z);
            // Keep upward bottom-trim; replace the along-axis eave end cut only.
            var bottoms = model.LowerEndClipPlanes.Where(IsUpwardHorizontal).ToArray();
            lower = new[] { new RoofStructuralRafterClipPlane(station, retainTowardTimber) }
                .Concat(bottoms).ToArray();
        }

        if (ridgeEdited)
        {
            // Edited upper station is no longer at the ridge join — drop obsolete
            // ridge miter/envelope and terminate with a longitudinal end plane.
            var station = Lerp(from3d, tHi);
            var retainTowardTimber = tStart <= tEnd
                ? new RoofPoint3D(-frame.Dir.X, -frame.Dir.Y, -frame.Dir.Z)
                : frame.Dir;
            ridge = new RoofStructuralRafterClipPlane(station, retainTowardTimber);
            miter = null;
            envelope = Array.Empty<RoofStructuralRafterClipPlane>();
        }

        RoofSegment3D? Sub(RoofSegment3D? axis) =>
            axis is null ? null : new(Lerp(axis, tStart), Lerp(axis, tEnd));
        return model with
        {
            Geometry = model.Geometry with
            {
                UpperAxis = to3d,
                UpperLeftEdge = Sub(model.Geometry.UpperLeftEdge),
                UpperRightEdge = Sub(model.Geometry.UpperRightEdge),
                PhysicalTopAxis = Sub(model.Geometry.PhysicalTopAxis),
            },
            LowerEndClipPlanes = lower,
            RidgeClipPlane = ridge,
            UpperNodeMiterPlane = miter,
            RoofEnvelopeClipPlanes = envelope,
        };
    }

    private static bool PlanEndpointsMatch(RoofSegment3D a, RoofSegment3D b) =>
        Math.Abs(a.Start.X - b.Start.X) <= Tolerance &&
        Math.Abs(a.Start.Y - b.Start.Y) <= Tolerance &&
        Math.Abs(a.End.X - b.End.X) <= Tolerance &&
        Math.Abs(a.End.Y - b.End.Y) <= Tolerance;

    private static RoofSegment3D ReversePlan(RoofSegment3D segment) =>
        new(segment.End, segment.Start);

    private static double PlanLateral(RoofSegment3D plan, RoofPoint3D point)
    {
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= Tolerance) return 0;
        return ((point.X - plan.Start.X) * -dy + (point.Y - plan.Start.Y) * dx) / len;
    }

    private static bool IsUpwardHorizontal(RoofStructuralRafterClipPlane plane) =>
        Math.Abs(plane.RetainedNormal.X) <= Tolerance &&
        Math.Abs(plane.RetainedNormal.Y) <= Tolerance &&
        Math.Abs(plane.RetainedNormal.Z - 1d) <= Tolerance;

    private static RoofStructuralRafterPolyhedron RemapAxisFrame(
        RoofStructuralRafterPolyhedron model, RoofSegment3D from3d, RoofSegment3D to3d)
    {
        var from = BuildFrame(from3d);
        var to = BuildFrame(to3d);
        var scale = to3d.LengthMm / from3d.LengthMm;
        RoofPoint3D MapPoint(RoofPoint3D point)
        {
            var relX = point.X - from3d.Start.X;
            var relY = point.Y - from3d.Start.Y;
            var relZ = point.Z - from3d.Start.Z;
            var along = relX * from.Dir.X + relY * from.Dir.Y + relZ * from.Dir.Z;
            var side = relX * from.Side.X + relY * from.Side.Y + relZ * from.Side.Z;
            var up = relX * from.Up.X + relY * from.Up.Y + relZ * from.Up.Z;
            return new(
                to3d.Start.X + (along * scale) * to.Dir.X + side * to.Side.X + up * to.Up.X,
                to3d.Start.Y + (along * scale) * to.Dir.Y + side * to.Side.Y + up * to.Up.Y,
                to3d.Start.Z + (along * scale) * to.Dir.Z + side * to.Side.Z + up * to.Up.Z);
        }

        var mapped = Map(model, MapPoint);
        return mapped with { Geometry = mapped.Geometry with { UpperAxis = to3d } };
    }

    private static double PlanParameter(RoofSegment3D plan, RoofPoint3D point)
    {
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= Tolerance * Tolerance) return 0;
        return ((point.X - plan.Start.X) * dx + (point.Y - plan.Start.Y) * dy) / lenSq;
    }

    private static RoofPoint3D Lerp(RoofSegment3D segment, double t) =>
        new(
            segment.Start.X + (segment.End.X - segment.Start.X) * t,
            segment.Start.Y + (segment.End.Y - segment.Start.Y) * t,
            segment.Start.Z + (segment.End.Z - segment.Start.Z) * t);

    private static (RoofPoint3D Dir, RoofPoint3D Side, RoofPoint3D Up) BuildFrame(RoofSegment3D axis)
    {
        var len = axis.LengthMm;
        var dir = new RoofPoint3D(
            (axis.End.X - axis.Start.X) / len,
            (axis.End.Y - axis.Start.Y) / len,
            (axis.End.Z - axis.Start.Z) / len);
        // Prefer a horizontal-ish side vector so cross-section width stays in plan.
        var side = Cross(dir, new(0, 0, 1));
        if (Length(side) <= Tolerance)
            side = Cross(dir, new(0, 1, 0));
        side = Normalize(side);
        var up = Normalize(Cross(side, dir));
        // Re-orthogonalize side against dir/up for stability on steep hips.
        side = Normalize(Cross(dir, up));
        return (dir, side, up);
    }

    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double Length(RoofPoint3D v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static RoofPoint3D Normalize(RoofPoint3D v)
    {
        var len = Length(v);
        if (len <= Tolerance) throw new ArgumentException("Degenerate structural axis frame.");
        return new(v.X / len, v.Y / len, v.Z / len);
    }

    private static RoofStructuralRafterPolyhedron Map(
        RoofStructuralRafterPolyhedron model, Func<RoofPoint3D, RoofPoint3D> Point)
    {
        RoofSegment3D Segment(RoofSegment3D segment) => new(Point(segment.Start), Point(segment.End));
        RoofSegment3D? Optional(RoofSegment3D? segment) => segment is null ? null : Segment(segment);
        RoofStructuralRafterClipPlane Plane(RoofStructuralRafterClipPlane plane) => plane with { Point = Point(plane.Point) };
        return model with
        {
            Geometry = model.Geometry with
            {
                UpperAxis = Segment(model.Geometry.UpperAxis),
                UpperLeftEdge = Optional(model.Geometry.UpperLeftEdge),
                UpperRightEdge = Optional(model.Geometry.UpperRightEdge),
                PhysicalTopAxis = Optional(model.Geometry.PhysicalTopAxis),
                BodyVertices = model.Geometry.BodyVertices.Select(Point).ToArray(),
                Faces = model.Geometry.Faces.Select(face => face with { Vertices = face.Vertices.Select(Point).ToArray() }).ToArray(),
            },
            ConvexHalves = model.ConvexHalves.Select(half => half with
            {
                SourcePrismVertices = half.SourcePrismVertices.Select(Point).ToArray(),
                ClippedBodyVertices = half.ClippedBodyVertices.Select(Point).ToArray(),
                ClippedTopFaceVertices = half.ClippedTopFaceVertices.Select(Point).ToArray(),
            }).ToArray(),
            EaveClipPlanes = model.EaveClipPlanes.Select(Plane).ToArray(),
            RidgeClipPlane = Plane(model.RidgeClipPlane),
            UpperNodeMiterPlane = model.UpperNodeMiterPlane is { } miter ? Plane(miter) : null,
            LowerEndClipPlanes = model.LowerEndClipPlanes.Select(Plane).ToArray(),
            RoofEnvelopeClipPlanes = model.RoofEnvelopeClipPlanes.Select(Plane).ToArray(),
        };
    }
}
