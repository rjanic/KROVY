using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofStructuralNativeAction { Unclaimed, AcceptPlan, RestorePlan, RebuildPhysical, RejectClone }

/// <summary>
/// Product disposition for the structural Hip/Valley native-edit matrix.
/// Whole-roof COPY/MIRROR remain owned by the existing roof rebind path.
/// </summary>
public enum RoofStructuralCommandDisposition { Supported, ExplicitlyRejected, Deferred }

public static class RoofStructuralEditRules
{
    private const double Tolerance = 1e-6;
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static bool IsValid(RoofStructuralEditState? state) =>
        state is { SchemaVersion: 1, Members: not null } &&
        state.Members.All(item => item is not null && item.LogicalKey is not null &&
            item.LogicalKey.Role is RoofStructuralRole.Hip or RoofStructuralRole.Valley &&
            item.LogicalKey.BoundaryEdgeIdA > 0 && item.LogicalKey.BoundaryEdgeIdB > 0 &&
            item.LogicalKey.BoundaryEdgeIdA < item.LogicalKey.BoundaryEdgeIdB &&
            IsFinite(item.OffsetXmm) && IsFinite(item.OffsetYmm)) &&
        state.Members.Select(item => item.LogicalKey).Distinct().Count() == state.Members.Count;

    public static bool HasFirstClaimOpportunity(string? command) =>
        IsPlanAcceptCommand(command) ||
        IsPlanRestoreCommand(command) ||
        IsCloneRejectCommand(command) ||
        IsPhysicalRecoveryCommand(command);

    public static bool RequiresAssemblySnapshotCapture(string? command) =>
        HasFirstClaimOpportunity(command);

    public static bool IsPlanAcceptCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is "MOVE" or "ERASE";

    public static bool IsPlanRestoreCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is
            "ROTATE" or "SCALE" or "STRETCH" or "GRIP_STRETCH" or
            "TRIM" or "EXTEND" or "BREAK" or "BREAKATPOINT" or
            "FILLET" or "CHAMFER" or "JOIN" or "OFFSET" or "EXPLODE";

    public static bool IsCloneRejectCommand(string? command) =>
        LiveGeometryCommandRules.NormalizeCommandName(command) is
            "COPY" or "MIRROR" or
            "ARRAY" or "ARRAYRECT" or "ARRAYPOLAR" or "ARRAYPATH";

    public static bool IsPlanFoundationCommand(string? command) =>
        IsPlanAcceptCommand(command);

    public static bool IsPhysicalRecoveryCommand(string? command) =>
        IsPlanAcceptCommand(command) ||
        IsPlanRestoreCommand(command) ||
        IsCloneRejectCommand(command);

    public static RoofStructuralNativeAction Classify(string? command, bool physical, RoofEditState editState)
    {
        if (IsCloneRejectCommand(command))
            return RoofStructuralNativeAction.RejectClone;
        if (physical)
            return IsPhysicalRecoveryCommand(command)
                ? RoofStructuralNativeAction.RebuildPhysical
                : RoofStructuralNativeAction.Unclaimed;
        // Locked Plan2D reject-family (STRETCH/ROTATE/BREAK/…): the existing Locked
        // generated-member guard is the sole restore owner. Claiming here runs an
        // intermediate semantic reconcile + GROUP sync that can fail and emit a
        // false user error before that guard recovers the same roof.
        if (IsPlanRestoreCommand(command))
            return editState == RoofEditState.Locked
                ? RoofStructuralNativeAction.Unclaimed
                : RoofStructuralNativeAction.RestorePlan;
        if (!IsPlanAcceptCommand(command))
            return RoofStructuralNativeAction.Unclaimed;
        return editState == RoofEditState.Unlocked
            ? RoofStructuralNativeAction.AcceptPlan
            : RoofStructuralNativeAction.RestorePlan;
    }

    /// <summary>
    /// Individual generated Structural Hip/Valley members have no independent clone
    /// identity. Whole-roof COPY/MIRROR stay Supported via existing rebind, not here.
    /// </summary>
    public static RoofStructuralCommandDisposition GetDisposition(string? command)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(command);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(normalized))
            return RoofStructuralCommandDisposition.Supported;
        if (normalized is "SAVE" or "CLOSE" or "OPEN" or "QSAVE" or "SAVEAS")
            return RoofStructuralCommandDisposition.Supported;
        if (IsCloneRejectCommand(normalized))
            return RoofStructuralCommandDisposition.ExplicitlyRejected;
        if (IsPlanRestoreCommand(normalized))
            return RoofStructuralCommandDisposition.ExplicitlyRejected;
        if (IsPlanAcceptCommand(normalized))
            return RoofStructuralCommandDisposition.Supported;
        return RoofStructuralCommandDisposition.Deferred;
    }

    /// <summary>
    /// Ordinary automatic rafters use Generated/AttachedManual identity. Structural
    /// claim must never treat a non-Hip/Valley structural role as a candidate.
    /// </summary>
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
        var points = new[] { before.Start, before.End, after.Start, after.End };
        if (points.Any(p => !IsFinite(p.X) || !IsFinite(p.Y) ||
                            !IsFinite(p.Z) || Math.Abs(p.Z) > Tolerance)) return false;
        var dx = after.Start.X - before.Start.X;
        var dy = after.Start.Y - before.Start.Y;
        if (Math.Abs(after.End.X - before.End.X - dx) > Tolerance ||
            Math.Abs(after.End.Y - before.End.Y - dy) > Tolerance || before.LengthMm <= Tolerance) return false;
        var current = Get(state, key);
        if (!IsFinite(current.OffsetXmm + dx) || !IsFinite(current.OffsetYmm + dy)) return false;
        accepted = Upsert(state, current with { OffsetXmm = current.OffsetXmm + dx, OffsetYmm = current.OffsetYmm + dy });
        return true;
    }

    public static RoofSegment3D Place(RoofSegment3D segment, RoofStructuralMemberEdit edit) =>
        new(Place(segment.Start, edit), Place(segment.End, edit));

    public static IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> ApplyPlan(
        IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> canonical, RoofStructuralEditState state)
    {
        if (!IsValid(state)) throw new ArgumentException("Invalid structural edit state.");
        return canonical.Where(item => !Get(state, item.LogicalKey).Suppressed)
            .Select(item => item with { Segment3D = Place(item.Segment3D, Get(state, item.LogicalKey)) }).ToArray();
    }

    private static RoofPoint3D Place(RoofPoint3D point, RoofStructuralMemberEdit edit) =>
        new(point.X + edit.OffsetXmm, point.Y + edit.OffsetYmm, point.Z);

    /// <summary>
    /// Replays accepted placement on freshly built CAD-neutral geometry. Profile,
    /// heights, source-face context and cut normals remain canonical. This is never
    /// a transform/readback of the edited host Solid3d.
    /// </summary>
    public static RoofStructuralRafterPolyhedron Place(RoofStructuralRafterPolyhedron model, RoofStructuralMemberEdit edit)
    {
        if (model.StructuralKey != edit.LogicalKey || edit.Suppressed ||
            !IsValid(new(1, new[] { edit }))) throw new ArgumentException("Invalid structural placement.");
        RoofPoint3D Point(RoofPoint3D point) => Place(point, edit);
        RoofSegment3D Segment(RoofSegment3D segment) => Place(segment, edit);
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
