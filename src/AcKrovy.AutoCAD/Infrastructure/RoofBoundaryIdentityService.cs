using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofBoundaryIdentityService
{
    public static RoofBoundaryIdentityEnsureResult EnsureBoundaryIdentity(
        Database database,
        Transaction transaction,
        ObjectId sourceId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                sourceId,
                OpenMode.ForRead,
                out var source,
                database) ||
            source is null ||
            RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofBoundaryIdentityEnsureResult.Invalid(
                RoofBoundaryIdentityError.NotAuthoritativeSource);
        }

        var stored = RoofBoundaryIdentityStore.Read(source);
        if (stored.Data is not null)
        {
            return RoofBoundaryIdentityEnsureResult.Existing(stored.Data);
        }

        if (stored.Exists)
        {
            return RoofBoundaryIdentityEnsureResult.Invalid(stored.Error);
        }

        return CreateAndWriteForCurrentSource(source, transaction);
    }

    /// <summary>
    /// Whole-roof MIRROR rebind: the mirrored owner polyline is authoritative and
    /// carries a deep-cloned BoundaryIdentity whose RawWinding no longer matches.
    /// Recreate identity from the CURRENT mirrored footprint using the same
    /// CreateSequential path as first establish. Valid existing identity (COPY /
    /// translation) is left untouched. Malformed non-winding payloads stay fail-closed.
    /// Never mutates any other owner.
    /// </summary>
    public static RoofBoundaryIdentityEnsureResult RehomeForCurrentSource(
        Database database,
        Transaction transaction,
        ObjectId sourceId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                sourceId,
                OpenMode.ForRead,
                out var source,
                database) ||
            source is null ||
            RoofDefinitionStore.Read(source).Data is null)
        {
            return RoofBoundaryIdentityEnsureResult.Invalid(
                RoofBoundaryIdentityError.NotAuthoritativeSource);
        }

        var stored = RoofBoundaryIdentityStore.Read(source);
        if (stored.Data is not null)
        {
            // Already consistent with current winding — COPY / no-op.
            return RoofBoundaryIdentityEnsureResult.Existing(stored.Data);
        }

        if (stored.Exists &&
            stored.Error != RoofBoundaryIdentityError.CurrentRawWindingMismatch)
        {
            // Preserve Ensure fail-closed behavior for malformed payloads.
            return RoofBoundaryIdentityEnsureResult.Invalid(stored.Error);
        }

        return CreateAndWriteForCurrentSource(source, transaction);
    }

    private static RoofBoundaryIdentityEnsureResult CreateAndWriteForCurrentSource(
        Polyline source,
        Transaction transaction)
    {
        var normalized = RoofFootprintValidator.ValidateWithProvenance(
            RoofPolylineExtractor.Extract(source));
        if (!normalized.Validation.IsValid)
        {
            return RoofBoundaryIdentityEnsureResult.Invalid(
                RoofBoundaryIdentityError.InvalidFootprint);
        }

        var created = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation);
        if (!created.IsValid || created.Identity is null)
        {
            return RoofBoundaryIdentityEnsureResult.Invalid(created.Error);
        }

        source.UpgradeOpen();
        RoofBoundaryIdentityStore.Write(source, transaction, created.Identity);
        return RoofBoundaryIdentityEnsureResult.Created(created.Identity);
    }
}

internal enum RoofBoundaryIdentityEnsureStatus
{
    Created,
    Existing,
    Invalid,
}

internal sealed record RoofBoundaryIdentityEnsureResult(
    RoofBoundaryIdentityEnsureStatus Status,
    RoofBoundaryIdentity? Identity,
    RoofBoundaryIdentityError Error)
{
    public static RoofBoundaryIdentityEnsureResult Created(RoofBoundaryIdentity identity) =>
        new(RoofBoundaryIdentityEnsureStatus.Created, identity, RoofBoundaryIdentityError.None);

    public static RoofBoundaryIdentityEnsureResult Existing(RoofBoundaryIdentity identity) =>
        new(RoofBoundaryIdentityEnsureStatus.Existing, identity, RoofBoundaryIdentityError.None);

    public static RoofBoundaryIdentityEnsureResult Invalid(RoofBoundaryIdentityError error) =>
        new(RoofBoundaryIdentityEnsureStatus.Invalid, null, error);
}
