using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Deterministic plane-local basis and rigid/endpoint override math.</summary>
public static class RoofGeneratedMemberOverrideMath
{
    public const double LengthToleranceMm = 0.01d;
    public const double AngleToleranceRadians = 1e-9d;
    public const double OffPlaneRejectToleranceMm = 1.0d;

    public static bool TryCreateBasis(
        RoofGeneratedMemberGeometry canonical,
        RoofPoint3D planeNormal,
        out RoofPlaneBasis basis)
    {
        basis = default;
        if (!IsFinite(canonical.Start) ||
            !IsFinite(canonical.End) ||
            !IsFinite(planeNormal))
        {
            return false;
        }

        var along = Subtract(canonical.End, canonical.Start);
        var alongLength = Length(along);
        if (alongLength <= LengthToleranceMm)
        {
            return false;
        }

        var normalLength = Length(planeNormal);
        if (normalLength <= LengthToleranceMm)
        {
            return false;
        }

        var axisU = Scale(along, 1d / alongLength);
        var axisW = Scale(planeNormal, 1d / normalLength);
        var axisV = Cross(axisW, axisU);
        var vLength = Length(axisV);
        if (vLength <= LengthToleranceMm)
        {
            return false;
        }

        axisV = Scale(axisV, 1d / vLength);
        axisW = Cross(axisU, axisV);
        var wLength = Length(axisW);
        if (wLength <= LengthToleranceMm)
        {
            return false;
        }

        axisW = Scale(axisW, 1d / wLength);
        basis = new RoofPlaneBasis(canonical.Start, axisU, axisV, axisW);
        return true;
    }

    public static bool TryApply(
        RoofGeneratedMemberGeometry canonical,
        RoofPoint3D planeNormal,
        RoofGeneratedMemberOverride? overrideData,
        out RoofGeneratedMemberGeometry result)
    {
        result = canonical;
        if (overrideData is null || overrideData.Suppressed)
        {
            return overrideData is null;
        }

        if (!TryCreateBasis(canonical, planeNormal, out var basis))
        {
            return false;
        }

        var rotatedEnd = Add(
            canonical.Start,
            RotateAroundAxis(
                Subtract(canonical.End, canonical.Start),
                basis.AxisW,
                overrideData.RotationRadians));
        var translation = Add(
            Scale(basis.AxisU, overrideData.AlongMm),
            Scale(basis.AxisV, overrideData.LateralMm));
        var rigidStart = Add(canonical.Start, translation);
        var rigidEnd = Add(rotatedEnd, translation);
        var transformed = new RoofGeneratedMemberGeometry(rigidStart, rigidEnd);
        if (transformed.LengthMm <= LengthToleranceMm)
        {
            return false;
        }

        if (!TryCreateBasis(transformed, planeNormal, out var transformedBasis))
        {
            return false;
        }

        var start = Add(
            rigidStart,
            Scale(transformedBasis.AxisU, -overrideData.StartOffsetMm));
        var end = Add(
            rigidEnd,
            Scale(transformedBasis.AxisU, overrideData.EndOffsetMm));
        result = new RoofGeneratedMemberGeometry(start, end);
        return result.LengthMm > LengthToleranceMm &&
               IsOnPlane(result.Start, basis) &&
               IsOnPlane(result.End, basis);
    }

    public static bool TryClassify(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        RoofGeneratedMemberKey key,
        string? reservedElementId,
        out RoofGeneratedMemberOverride? overrideData)
    {
        overrideData = null;
        if (!TryCreateBasis(canonical, planeNormal, out var basis) ||
            !IsOnPlane(observed.Start, basis) ||
            !IsOnPlane(observed.End, basis) ||
            observed.LengthMm <= LengthToleranceMm)
        {
            return false;
        }

        if (Math.Abs(observed.LengthMm - canonical.LengthMm) <= LengthToleranceMm)
        {
            return TryComposeRigidKeepingEndpointOffsets(
                canonical,
                observed,
                planeNormal,
                key,
                existing: null,
                reservedElementId,
                out overrideData,
                out _);
        }

        var logical = AlignObservedToCanonical(observed, basis.AxisU);
        var alongStart = Dot(Subtract(logical.Start, canonical.Start), basis.AxisU);
        var alongEnd = Dot(Subtract(logical.End, canonical.End), basis.AxisU);
        var lateralStart = Dot(Subtract(logical.Start, canonical.Start), basis.AxisV);
        var lateralEnd = Dot(Subtract(logical.End, canonical.End), basis.AxisV);
        var directionRotation = SignedAngleAround(
            Subtract(canonical.End, canonical.Start),
            Subtract(logical.End, logical.Start),
            basis.AxisW);
        RoofGeneratedMemberOverride candidate;
        if (Math.Abs(directionRotation) <= AngleToleranceRadians &&
            Math.Abs(lateralStart - lateralEnd) <= LengthToleranceMm)
        {
            double along;
            double startOffset;
            double endOffset;
            if (Math.Abs(alongStart - alongEnd) <= LengthToleranceMm)
            {
                along = alongStart;
                startOffset = 0d;
                endOffset = 0d;
            }
            else
            {
                along = 0d;
                startOffset = -alongStart;
                endOffset = alongEnd;
            }

            candidate = new RoofGeneratedMemberOverride(
                key,
                false,
                along,
                lateralStart,
                0d,
                startOffset,
                endOffset,
                reservedElementId);
        }
        else
        {
            var rotatedEnd = Add(
                canonical.Start,
                RotateAroundAxis(
                    Subtract(canonical.End, canonical.Start),
                    basis.AxisW,
                    directionRotation));
            var translation = Subtract(logical.Start, canonical.Start);
            var along = Dot(translation, basis.AxisU);
            var lateral = Dot(translation, basis.AxisV);
            var normalComponent = Dot(translation, basis.AxisW);
            if (Math.Abs(normalComponent) > LengthToleranceMm)
            {
                return false;
            }

            var rigid = new RoofGeneratedMemberGeometry(
                Add(canonical.Start, translation),
                Add(rotatedEnd, translation));
            if (rigid.LengthMm <= LengthToleranceMm ||
                !TryCreateBasis(rigid, planeNormal, out var rigidBasis))
            {
                return false;
            }

            candidate = new RoofGeneratedMemberOverride(
                key,
                false,
                along,
                lateral,
                directionRotation,
                Dot(Subtract(rigid.Start, logical.Start), rigidBasis.AxisU),
                Dot(Subtract(logical.End, rigid.End), rigidBasis.AxisU),
                reservedElementId);
        }

        var normalized = Normalize(candidate);
        if (normalized is null)
        {
            overrideData = null;
            return true;
        }

        if (!TryApply(canonical, planeNormal, normalized, out var replayed) ||
            !GeometryEquals(replayed, logical))
        {
            return false;
        }

        overrideData = normalized;
        return true;
    }

    /// <summary>
    /// Solves <c>RotationRadians + AlongMm + LateralMm</c> from canonical body to
    /// the observed final Line while preserving existing Start/End offsets.
    /// Inverse of <see cref="TryApply"/>: rotate around canonical Start about
    /// AxisW, then translate in the unrotated canonical U/V basis, then apply
    /// endpoint offsets along the transformed AxisU.
    /// </summary>
    public static bool TryComposeRigidKeepingEndpointOffsets(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        RoofGeneratedMemberKey key,
        RoofGeneratedMemberOverride? existing,
        string? reservedElementId,
        out RoofGeneratedMemberOverride? overrideData,
        out RoofGeneratedMemberComposeFailure? failure)
    {
        overrideData = null;
        failure = null;
        var existingRotation = existing?.RotationRadians ?? 0d;
        var existingAlong = existing?.AlongMm ?? 0d;
        var existingLateral = existing?.LateralMm ?? 0d;
        var existingStart = existing?.StartOffsetMm ?? 0d;
        var existingEnd = existing?.EndOffsetMm ?? 0d;
        var reserved = existing?.ReservedElementId ?? reservedElementId;
        var candidateRotation = existingRotation;
        var candidateAlong = existingAlong;
        var candidateLateral = existingLateral;
        var replay = default(RoofGeneratedMemberGeometry);
        var hasReplay = false;
        var maxError = -1d;

        RoofGeneratedMemberComposeFailure Fail(string stage, string reason) =>
            new(
                stage,
                reason,
                canonical,
                observed,
                existingRotation,
                existingAlong,
                existingLateral,
                existingStart,
                existingEnd,
                candidateRotation,
                candidateAlong,
                candidateLateral,
                replay,
                hasReplay,
                maxError);

        if (existing is { Suppressed: true })
        {
            failure = Fail("suppressed", "suppressed-override");
            return false;
        }

        if (!TryCreateBasis(canonical, planeNormal, out var basis))
        {
            failure = Fail("basis-failed", "basis-failed");
            return false;
        }

        var projected = new RoofGeneratedMemberGeometry(
            ProjectOntoPlane(observed.Start, basis),
            ProjectOntoPlane(observed.End, basis));
        if (projected.LengthMm <= LengthToleranceMm)
        {
            failure = Fail("invalid-length", "invalid-zero-length");
            return false;
        }

        var observedDir = Subtract(projected.End, projected.Start);
        var observedAxisU = Scale(observedDir, 1d / Length(observedDir));
        var targetRigidStart = Add(projected.Start, Scale(observedAxisU, existingStart));
        var targetRigidEnd = Subtract(projected.End, Scale(observedAxisU, existingEnd));
        var targetBody = new RoofGeneratedMemberGeometry(targetRigidStart, targetRigidEnd);
        if (Math.Abs(targetBody.LengthMm - canonical.LengthMm) > LengthToleranceMm)
        {
            failure = Fail("body-length-mismatch", "canonical-final-length-mismatch");
            return false;
        }

        candidateRotation = SignedAngleAround(
            Subtract(canonical.End, canonical.Start),
            Subtract(targetRigidEnd, targetRigidStart),
            basis.AxisW);
        var rotatedEnd = Add(
            canonical.Start,
            RotateAroundAxis(
                Subtract(canonical.End, canonical.Start),
                basis.AxisW,
                candidateRotation));
        var translation = Subtract(targetRigidStart, canonical.Start);
        var normalComponent = Dot(translation, basis.AxisW);
        if (Math.Abs(normalComponent) > LengthToleranceMm)
        {
            failure = Fail("off-plane-translation", "off-plane-result");
            return false;
        }

        candidateAlong = Dot(translation, basis.AxisU);
        candidateLateral = Dot(translation, basis.AxisV);
        var expectedRigidEnd = Add(rotatedEnd, translation);
        var rotationError = expectedRigidEnd.DistanceTo(targetRigidEnd);
        if (rotationError > LengthToleranceMm)
        {
            maxError = rotationError;
            failure = Fail("rotation-solve", "rotation-replay-mismatch");
            return false;
        }

        var candidate = new RoofGeneratedMemberOverride(
            key,
            false,
            candidateAlong,
            candidateLateral,
            candidateRotation,
            existingStart,
            existingEnd,
            reserved);
        var normalized = Normalize(candidate);
        if (normalized is null)
        {
            overrideData = null;
            return true;
        }

        if (!TryApply(canonical, planeNormal, normalized, out replay))
        {
            failure = Fail("apply-failed", "override-composition-failure");
            return false;
        }

        hasReplay = true;
        maxError = MaxEndpointErrorMm(replay, projected);
        if (maxError > LengthToleranceMm)
        {
            failure = Fail("replay-mismatch", "override-composition-failure");
            return false;
        }

        overrideData = normalized;
        return true;
    }

    public static double MaxEndpointErrorMm(
        RoofGeneratedMemberGeometry left,
        RoofGeneratedMemberGeometry right) =>
        Math.Max(left.Start.DistanceTo(right.Start), left.End.DistanceTo(right.End));

    public static bool TryClassifyCollinearEndpointEdit(
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        out double startOffsetDeltaMm,
        out double endOffsetDeltaMm,
        out RoofGeneratedMemberGeometry acceptedObserved,
        out RoofGeneratedMemberManualEditReason reason)
    {
        startOffsetDeltaMm = 0d;
        endOffsetDeltaMm = 0d;
        acceptedObserved = observed;
        reason = RoofGeneratedMemberManualEditReason.BasisFailed;
        if (!TryCreateBasis(baseline, planeNormal, out var basis))
        {
            return false;
        }

        var projected = new RoofGeneratedMemberGeometry(
            ProjectOntoPlane(observed.Start, basis),
            ProjectOntoPlane(observed.End, basis));
        if (projected.LengthMm <= LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.InvalidLength;
            acceptedObserved = projected;
            return false;
        }

        var logical = AlignObservedToCanonical(projected, basis.AxisU);
        acceptedObserved = logical;
        var startLateral = Math.Abs(Dot(Subtract(logical.Start, baseline.Start), basis.AxisV));
        var endLateral = Math.Abs(Dot(Subtract(logical.End, baseline.Start), basis.AxisV));
        if (startLateral > LengthToleranceMm || endLateral > LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.NonCollinear;
            return false;
        }

        if (!IsOnPlane(logical.Start, basis) || !IsOnPlane(logical.End, basis))
        {
            reason = RoofGeneratedMemberManualEditReason.OffPlane;
            return false;
        }

        var alongStart = Dot(Subtract(logical.Start, baseline.Start), basis.AxisU);
        var alongEnd = Dot(Subtract(logical.End, baseline.End), basis.AxisU);
        var startMoved = Math.Abs(alongStart) > LengthToleranceMm;
        var endMoved = Math.Abs(alongEnd) > LengthToleranceMm;
        if (startMoved && endMoved)
        {
            reason = RoofGeneratedMemberManualEditReason.BothEndpointsChanged;
            return false;
        }

        if (!startMoved && !endMoved)
        {
            reason = RoofGeneratedMemberManualEditReason.NeitherEndpointChanged;
            acceptedObserved = baseline;
            return true;
        }

        startOffsetDeltaMm = startMoved ? -alongStart : 0d;
        endOffsetDeltaMm = endMoved ? alongEnd : 0d;
        var delta = new RoofGeneratedMemberOverride(
            default,
            false,
            0d,
            0d,
            0d,
            startOffsetDeltaMm,
            endOffsetDeltaMm);
        if (!TryApply(baseline, planeNormal, delta, out var replayed) ||
            !GeometryEquals(replayed, logical))
        {
            reason = RoofGeneratedMemberManualEditReason.ReplayFailed;
            return false;
        }

        acceptedObserved = replayed;
        reason = RoofGeneratedMemberManualEditReason.Accepted;
        return true;
    }

    public static bool TryClassifyPureTranslation(
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        out RoofPoint3D worldTranslation,
        out RoofGeneratedMemberGeometry acceptedObserved,
        out RoofGeneratedMemberManualEditReason reason)
    {
        worldTranslation = new RoofPoint3D(0d, 0d, 0d);
        acceptedObserved = observed;
        reason = RoofGeneratedMemberManualEditReason.BasisFailed;
        if (!TryCreateBasis(baseline, planeNormal, out var basis))
        {
            return false;
        }

        if (!TryProjectObserved(observed, basis, out var logical, out reason))
        {
            acceptedObserved = logical;
            return false;
        }

        acceptedObserved = logical;
        if (Math.Abs(logical.LengthMm - baseline.LengthMm) > LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.LengthChanged;
            return false;
        }

        var startDelta = Subtract(logical.Start, baseline.Start);
        var endDelta = Subtract(logical.End, baseline.End);
        if (Length(Subtract(startDelta, endDelta)) > LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.NotPureTranslation;
            return false;
        }

        worldTranslation = startDelta;
        if (Length(worldTranslation) <= LengthToleranceMm)
        {
            acceptedObserved = baseline;
            worldTranslation = new RoofPoint3D(0d, 0d, 0d);
            reason = RoofGeneratedMemberManualEditReason.NeitherEndpointChanged;
            return true;
        }

        reason = RoofGeneratedMemberManualEditReason.Accepted;
        return true;
    }

    public static bool TryClassifyRigidEqualLength(
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        out RoofGeneratedMemberGeometry acceptedObserved,
        out RoofGeneratedMemberManualEditReason reason)
    {
        acceptedObserved = observed;
        reason = RoofGeneratedMemberManualEditReason.BasisFailed;
        if (!TryCreateBasis(baseline, planeNormal, out var basis))
        {
            return false;
        }

        if (!TryProjectObserved(
                observed,
                basis,
                out var logical,
                out reason,
                alignLogicalEndpoints: false))
        {
            acceptedObserved = logical;
            return false;
        }

        acceptedObserved = logical;
        if (Math.Abs(logical.LengthMm - baseline.LengthMm) > LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.LengthChanged;
            return false;
        }

        if (GeometryEquals(logical, baseline))
        {
            acceptedObserved = baseline;
            reason = RoofGeneratedMemberManualEditReason.NeitherEndpointChanged;
            return true;
        }

        reason = RoofGeneratedMemberManualEditReason.Accepted;
        return true;
    }

    public static bool TryDecomposeInPlane(
        RoofGeneratedMemberGeometry canonical,
        RoofPoint3D planeNormal,
        RoofPoint3D worldVector,
        out double alongMm,
        out double lateralMm)
    {
        alongMm = 0d;
        lateralMm = 0d;
        if (!TryCreateBasis(canonical, planeNormal, out var basis))
        {
            return false;
        }

        alongMm = Dot(worldVector, basis.AxisU);
        lateralMm = Dot(worldVector, basis.AxisV);
        return Math.Abs(Dot(worldVector, basis.AxisW)) <= OffPlaneRejectToleranceMm;
    }

    public static RoofGeneratedMemberOverride? ComposeTranslation(
        RoofGeneratedMemberOverride? existing,
        RoofGeneratedMemberKey key,
        string? reservedElementId,
        double alongDeltaMm,
        double lateralDeltaMm)
    {
        if (existing is { Suppressed: true })
        {
            return existing;
        }

        var composed = new RoofGeneratedMemberOverride(
            key,
            false,
            (existing?.AlongMm ?? 0d) + alongDeltaMm,
            (existing?.LateralMm ?? 0d) + lateralDeltaMm,
            existing?.RotationRadians ?? 0d,
            existing?.StartOffsetMm ?? 0d,
            existing?.EndOffsetMm ?? 0d,
            existing?.ReservedElementId ?? reservedElementId);
        return Normalize(composed);
    }

    public static RoofGeneratedMemberGeometry NormalizeToBasis(
        RoofGeneratedMemberGeometry observed,
        RoofPlaneBasis basis,
        out double maxZDelta)
    {
        var startOff = Math.Abs(Dot(Subtract(observed.Start, basis.Origin), basis.AxisW));
        var endOff = Math.Abs(Dot(Subtract(observed.End, basis.Origin), basis.AxisW));
        maxZDelta = Math.Max(startOff, endOff);

        var projected = new RoofGeneratedMemberGeometry(
            ProjectOntoPlane(observed.Start, basis),
            ProjectOntoPlane(observed.End, basis));

        return projected;
    }

    public static bool TryProjectObserved(
        RoofGeneratedMemberGeometry observed,
        RoofPlaneBasis basis,
        out RoofGeneratedMemberGeometry logical,
        out RoofGeneratedMemberManualEditReason reason,
        bool alignLogicalEndpoints = true)
    {
        logical = observed;
        reason = RoofGeneratedMemberManualEditReason.Accepted;
        var startOff = Math.Abs(Dot(Subtract(observed.Start, basis.Origin), basis.AxisW));
        var endOff = Math.Abs(Dot(Subtract(observed.End, basis.Origin), basis.AxisW));
        if (startOff > OffPlaneRejectToleranceMm || endOff > OffPlaneRejectToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.OffPlane;
            return false;
        }

        var projected = new RoofGeneratedMemberGeometry(
            ProjectOntoPlane(observed.Start, basis),
            ProjectOntoPlane(observed.End, basis));
        if (projected.LengthMm <= LengthToleranceMm)
        {
            reason = RoofGeneratedMemberManualEditReason.InvalidLength;
            logical = projected;
            return false;
        }

        logical = alignLogicalEndpoints
            ? AlignObservedToCanonical(projected, basis.AxisU)
            : projected;
        return true;
    }

    public static RoofGeneratedMemberOverride? ComposeEndpointOffsets(
        RoofGeneratedMemberOverride? existing,
        RoofGeneratedMemberKey key,
        string? reservedElementId,
        double startOffsetDeltaMm,
        double endOffsetDeltaMm)
    {
        if (existing is { Suppressed: true })
        {
            return existing;
        }

        var composed = new RoofGeneratedMemberOverride(
            key,
            false,
            existing?.AlongMm ?? 0d,
            existing?.LateralMm ?? 0d,
            existing?.RotationRadians ?? 0d,
            (existing?.StartOffsetMm ?? 0d) + startOffsetDeltaMm,
            (existing?.EndOffsetMm ?? 0d) + endOffsetDeltaMm,
            existing?.ReservedElementId ?? reservedElementId);
        return Normalize(composed);
    }

    public static RoofPoint3D ProjectOntoPlane(RoofPoint3D point, RoofPlaneBasis basis) =>
        Subtract(point, Scale(basis.AxisW, Dot(Subtract(point, basis.Origin), basis.AxisW)));

    public static string ToReasonToken(RoofGeneratedMemberManualEditReason reason) =>
        reason switch
        {
            RoofGeneratedMemberManualEditReason.None => "none",
            RoofGeneratedMemberManualEditReason.Accepted => "accepted",
            RoofGeneratedMemberManualEditReason.NeitherEndpointChanged => "neither-endpoint-changed",
            RoofGeneratedMemberManualEditReason.OffPlane => "off-plane-result",
            RoofGeneratedMemberManualEditReason.InvalidLength => "invalid-zero-length",
            RoofGeneratedMemberManualEditReason.NonCollinear => "non-collinear-result",
            RoofGeneratedMemberManualEditReason.BothEndpointsChanged => "both-endpoints-changed",
            RoofGeneratedMemberManualEditReason.BasisFailed => "basis-failed",
            RoofGeneratedMemberManualEditReason.ReplayFailed => "override-composition-failure",
            RoofGeneratedMemberManualEditReason.CompositionFailed => "override-composition-failure",
            RoofGeneratedMemberManualEditReason.LengthChanged => "length-changed",
            RoofGeneratedMemberManualEditReason.DirectionChanged => "direction-changed",
            RoofGeneratedMemberManualEditReason.NotPureTranslation => "not-pure-translation",
            RoofGeneratedMemberManualEditReason.UnsupportedGrip => "unsupported-grip",
            RoofGeneratedMemberManualEditReason.UnrepresentableStretch => "unrepresentable-stretch",
            _ => "classify-failed",
        };

    public static RoofGeneratedMemberOverride? Normalize(RoofGeneratedMemberOverride? overrideData)
    {
        if (overrideData is null)
        {
            return null;
        }

        if (overrideData.Suppressed)
        {
            return new RoofGeneratedMemberOverride(
                overrideData.Key,
                true,
                0d,
                0d,
                0d,
                0d,
                0d,
                overrideData.ReservedElementId);
        }

        var along = SnapZero(overrideData.AlongMm);
        var lateral = SnapZero(overrideData.LateralMm);
        var rotation = SnapAngle(overrideData.RotationRadians);
        var start = SnapZero(overrideData.StartOffsetMm);
        var end = SnapZero(overrideData.EndOffsetMm);
        if (along == 0d &&
            lateral == 0d &&
            rotation == 0d &&
            start == 0d &&
            end == 0d)
        {
            return null;
        }

        return new RoofGeneratedMemberOverride(
            overrideData.Key,
            false,
            along,
            lateral,
            rotation,
            start,
            end,
            overrideData.ReservedElementId);
    }

    public static bool GeometryEquals(
        RoofGeneratedMemberGeometry left,
        RoofGeneratedMemberGeometry right) =>
        left.Start.DistanceTo(right.Start) <= LengthToleranceMm &&
        left.End.DistanceTo(right.End) <= LengthToleranceMm;

    public static RoofGeneratedMemberGeometry AlignObservedToCanonical(
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D canonicalUnit)
    {
        var observedDir = Subtract(observed.End, observed.Start);
        return Dot(observedDir, canonicalUnit) >= 0d
            ? observed
            : new RoofGeneratedMemberGeometry(observed.End, observed.Start);
    }

    public static bool IsOnPlane(RoofPoint3D point, RoofPlaneBasis basis) =>
        Math.Abs(Dot(Subtract(point, basis.Origin), basis.AxisW)) <= LengthToleranceMm;

    private static double SnapZero(double value) =>
        Math.Abs(value) <= LengthToleranceMm ? 0d : value;

    private static double SnapAngle(double radians)
    {
        var wrapped = WrapAngle(radians);
        return Math.Abs(wrapped) <= AngleToleranceRadians ? 0d : wrapped;
    }

    private static double WrapAngle(double radians)
    {
        var wrapped = Math.IEEERemainder(radians, Math.PI * 2d);
        if (wrapped > Math.PI)
        {
            wrapped -= Math.PI * 2d;
        }
        else if (wrapped < -Math.PI)
        {
            wrapped += Math.PI * 2d;
        }

        return wrapped;
    }

    private static double SignedAngleAround(
        RoofPoint3D from,
        RoofPoint3D to,
        RoofPoint3D axis)
    {
        var fromLength = Length(from);
        var toLength = Length(to);
        if (fromLength <= LengthToleranceMm || toLength <= LengthToleranceMm)
        {
            return 0d;
        }

        var a = Scale(from, 1d / fromLength);
        var b = Scale(to, 1d / toLength);
        var sin = Dot(Cross(a, b), axis);
        var cos = Dot(a, b);
        return Math.Atan2(sin, cos);
    }

    private static RoofPoint3D RotateAroundAxis(RoofPoint3D vector, RoofPoint3D axis, double radians)
    {
        if (Math.Abs(radians) <= AngleToleranceRadians)
        {
            return vector;
        }

        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var alongAxis = Scale(axis, Dot(vector, axis));
        var rejection = Subtract(vector, alongAxis);
        var rotated = Add(
            Add(Scale(rejection, cos), Scale(Cross(axis, vector), sin)),
            alongAxis);
        return rotated;
    }

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static RoofPoint3D Cross(RoofPoint3D left, RoofPoint3D right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static double Length(RoofPoint3D vector) =>
        Math.Sqrt(Dot(vector, vector));

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
