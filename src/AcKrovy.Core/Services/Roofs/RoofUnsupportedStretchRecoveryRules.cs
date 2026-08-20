using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Pure eligibility and post-restore validation for Unsupported STRETCH Auto-Recovery.
/// Does not invent geometry — callers supply the exact pre-command snapshot.
/// </summary>
public static class RoofUnsupportedStretchRecoveryRules
{
    public const double VertexToleranceMm = 0.01d;
    public const double NormalTolerance = 0.000001d;

    public static bool IsRecoveryCommand(string? globalCommandName) =>
        LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName);

    public static bool IsEligibleSnapshot(RoofUnsupportedStretchSourceSnapshotData? snapshot)
    {
        if (snapshot is null ||
            string.IsNullOrWhiteSpace(snapshot.OwnerHandle) ||
            snapshot.Vertices is null ||
            snapshot.Vertices.Count != 4 ||
            !snapshot.IsClosed)
        {
            return false;
        }

        if (!IsFinite(snapshot.ElevationMm) ||
            !IsFinite(snapshot.NormalX) ||
            !IsFinite(snapshot.NormalY) ||
            !IsFinite(snapshot.NormalZ))
        {
            return false;
        }

        var normalLength = Math.Sqrt(
            snapshot.NormalX * snapshot.NormalX +
            snapshot.NormalY * snapshot.NormalY +
            snapshot.NormalZ * snapshot.NormalZ);
        if (normalLength <= NormalTolerance)
        {
            return false;
        }

        foreach (var vertex in snapshot.Vertices)
        {
            if (!IsFinite(vertex.X) || !IsFinite(vertex.Y))
            {
                return false;
            }
        }

        var input = new RoofFootprintInput(snapshot.Vertices, true, false, true);
        var validation = RoofFootprintValidator.Validate(input);
        return validation.IsValid && validation.Footprint is not null;
    }

    public static bool IsEligibleAssembly(RoofUnsupportedStretchAssemblySnapshotData? assembly)
    {
        if (assembly is null || !IsEligibleSnapshot(assembly.RoofSource))
        {
            return false;
        }

        var timberHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timberSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var timber in assembly.TimberLines ?? Array.Empty<RoofUnsupportedStretchTimberLineSnapshotData>())
        {
            if (timber is null ||
                string.IsNullOrWhiteSpace(timber.EntityHandle) ||
                string.IsNullOrWhiteSpace(timber.ElementId) ||
                string.IsNullOrWhiteSpace(timber.SourceHandle) ||
                !IsFinitePoint(timber.Start) ||
                !IsFinitePoint(timber.End) ||
                !timberHandles.Add(timber.EntityHandle))
            {
                return false;
            }

            timberSourceHandles.Add(timber.SourceHandle);
        }

        var annotationHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var annotation in assembly.Annotations ??
                 Array.Empty<RoofUnsupportedStretchAnnotationSnapshotData>())
        {
            if (annotation is null ||
                string.IsNullOrWhiteSpace(annotation.EntityHandle) ||
                string.IsNullOrWhiteSpace(annotation.SourceHandle) ||
                annotation.Kind == RoofUnsupportedStretchAnnotationKind.Unknown ||
                !annotationHandles.Add(annotation.EntityHandle))
            {
                return false;
            }

            // Annotations must belong to one of the snapshotted timber sources.
            if (timberSourceHandles.Count > 0 &&
                !timberSourceHandles.Contains(annotation.SourceHandle))
            {
                return false;
            }
        }

        return true;
    }

    public static bool CanAttemptRecovery(
        string? globalCommandName,
        RoofUnsupportedStretchSourceSnapshotData? snapshot,
        string liveOwnerHandle,
        RoofSourceChangeKind liveKind)
    {
        if (!IsRecoveryCommand(globalCommandName) ||
            liveKind != RoofSourceChangeKind.Unsupported ||
            !IsEligibleSnapshot(snapshot) ||
            string.IsNullOrWhiteSpace(liveOwnerHandle))
        {
            return false;
        }

        return string.Equals(
            snapshot!.OwnerHandle,
            liveOwnerHandle,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanAttemptAssemblyRecovery(
        string? globalCommandName,
        RoofUnsupportedStretchAssemblySnapshotData? assembly,
        string liveOwnerHandle,
        RoofSourceChangeKind liveKind) =>
        IsEligibleAssembly(assembly) &&
        CanAttemptRecovery(
            globalCommandName,
            assembly!.RoofSource,
            liveOwnerHandle,
            liveKind);

    public static bool RestoredMatchesSnapshot(
        IReadOnlyList<RoofPoint2D>? liveVertices,
        bool liveClosed,
        RoofUnsupportedStretchSourceSnapshotData snapshot)
    {
        if (!IsEligibleSnapshot(snapshot) ||
            liveVertices is null ||
            liveVertices.Count != snapshot.Vertices.Count ||
            liveClosed != snapshot.IsClosed)
        {
            return false;
        }

        for (var i = 0; i < liveVertices.Count; i++)
        {
            if (liveVertices[i].DistanceTo(snapshot.Vertices[i]) > VertexToleranceMm)
            {
                return false;
            }
        }

        return true;
    }

    public static bool PointsEqual(RoofPoint3D left, RoofPoint3D right) =>
        left.DistanceTo(right) <= VertexToleranceMm;

    /// <summary>
    /// KROVY framed MLeaders are one logical leader + one logical leader-line.
    /// Transient AutoCAD index identity may change after native STRETCH; that alone
    /// is recoverable. Multiple leaders/lines after normalize is incompatible.
    /// </summary>
    public static bool IsEligibleMLeaderTopology(int leaderIndex, int leaderLineIndex) =>
        leaderIndex >= 0 && leaderLineIndex >= 0;

    public static bool IsRecoverableMLeaderTopology(
        int liveLeaderCount,
        int liveLeaderLineCount,
        RoofUnsupportedStretchMLeaderContentKind snapshotContent,
        RoofUnsupportedStretchMLeaderContentKind liveContent)
    {
        if (liveLeaderCount != 1 || liveLeaderLineCount != 1)
        {
            return false;
        }

        if (snapshotContent == RoofUnsupportedStretchMLeaderContentKind.Unknown ||
            liveContent == RoofUnsupportedStretchMLeaderContentKind.Unknown)
        {
            return true;
        }

        return snapshotContent == liveContent;
    }

    /// <summary>
    /// Snapshot leader/line indexes are informational. Live indexes may differ after
    /// native STRETCH; do not treat index inequality alone as incompatible.
    /// </summary>
    public static bool IsIndexOnlyTopologyDrift(
        int? snapshotLeaderIndex,
        int? snapshotLineIndex,
        int liveLeaderIndex,
        int liveLineIndex) =>
        (snapshotLeaderIndex is { } snapLeader && snapLeader != liveLeaderIndex) ||
        (snapshotLineIndex is { } snapLine && snapLine != liveLineIndex);

    /// <summary>
    /// Mirrors <see cref="TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg"/> for
    /// recovery eligibility of snapshotted dogleg vectors/lengths.
    /// </summary>
    public static bool CanRestoreMLeaderDogleg(
        bool? enableDogleg,
        RoofPoint3D? doglegDirection,
        double? doglegLengthMm) =>
        enableDogleg == true &&
        doglegDirection is { } direction &&
        doglegLengthMm is { } length &&
        TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
            length,
            direction.X,
            direction.Y,
            out _,
            out _);

    public static bool IsAcceptableRestoredClassification(RoofSourceChangeKind kind) =>
        kind == RoofSourceChangeKind.RigidEquivalent;

    public static bool AllOwnersRecoverable(
        string? globalCommandName,
        IReadOnlyList<(string OwnerHandle, RoofSourceChangeKind Kind)> unsupportedOwners,
        Func<string, RoofUnsupportedStretchAssemblySnapshotData?> tryGetAssembly)
    {
        if (!IsRecoveryCommand(globalCommandName) ||
            unsupportedOwners is null ||
            unsupportedOwners.Count == 0 ||
            tryGetAssembly is null)
        {
            return false;
        }

        foreach (var owner in unsupportedOwners)
        {
            if (!CanAttemptAssemblyRecovery(
                    globalCommandName,
                    tryGetAssembly(owner.OwnerHandle),
                    owner.OwnerHandle,
                    owner.Kind))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinitePoint(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
}
