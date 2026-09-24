using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Explicit user-driven S6A write boundary. No command lifecycle registration is
/// present: one invocation owns one document lock, one transaction and one commit.
/// Any failure before Commit disposes the transaction and rolls back identity,
/// children, owner metadata and GROUP changes together.
/// </summary>
internal static class RoofAutomaticPurlinProductionApplyService
{
    public static RoofAutomaticPurlinApplyResult Apply(
        Document document,
        ObjectId ownerId,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum datum,
        RoofAutomaticPurlinPlan previewPlan,
        TimberElementDefaultProfile defaultProfile,
        AcKrovy.Cad.Abstractions.Layers.ElementLayerProfile layerProfile,
        double rafterHeightMm)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(datum);
        ArgumentNullException.ThrowIfNull(previewPlan);
        ArgumentNullException.ThrowIfNull(defaultProfile);
        ArgumentNullException.ThrowIfNull(layerProfile);
        if (!double.IsFinite(rafterHeightMm) || rafterHeightMm <= 0d)
        {
            return RoofAutomaticPurlinApplyResult.Failure("invalid-authoritative-rafter-height");
        }

        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    document.Database) ||
                owner is null ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "authoritative-roof-required");
            }

            var ownerReference = owner.Handle.ToString();
            var storedLayout = RoofPurlinLayoutStore.Read(owner);
            if (storedLayout.Exists && storedLayout.Data is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "layout-" + storedLayout.Error,
                    ownerReference);
            }

            var storedDatum = RoofRelativeElevationDatumStore.Read(owner);
            if (storedDatum.Exists && storedDatum.Data is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "relative-elevation-datum-" + storedDatum.Error,
                    ownerReference);
            }

            var layoutValidation = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
            var datumValidation = RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                datum.ReferenceKind,
                datum.ReferenceRelativeElevationMm,
                datum.ReferenceLocalZMm);
            if (!layoutValidation.IsValid || layoutValidation.Layout is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "layout-" + layoutValidation.Error,
                    ownerReference);
            }

            if (!datumValidation.IsValid || datumValidation.Datum is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "relative-elevation-datum-" + datumValidation.Error,
                    ownerReference);
            }

            var existingState =
                RoofAutomaticPurlinMaterializationService.InspectExistingOwnerStateInTransaction(
                    document.Database,
                    transaction,
                    owner);
            if (!existingState.IsValid)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    existingState.Result,
                    ownerReference);
            }

            if (previewPlan.Items.Count == 0 && existingState.ExistingCount == 0)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    "empty-new-configuration",
                    ownerReference);
            }

            var preparation = RoofAutomaticPurlinMaterializationService.PrepareInTransaction(
                document,
                transaction,
                owner,
                out var preparationFailure);
            if (preparation is null)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    preparationFailure,
                    ownerReference);
            }

            var materialization =
                RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction(
                    document,
                    transaction,
                    owner,
                    preparation,
                    layoutValidation.Layout,
                    datumValidation.Datum,
                    defaultProfile,
                    layerProfile,
                    previewPlan,
                    includeWallPlates: layoutValidation.Layout.WallPlateEnabled,
                    authoritativeRafterHeightMm: rafterHeightMm);
            if (!materialization.IsSuccess)
            {
                return RoofAutomaticPurlinApplyResult.Failure(
                    materialization.Result,
                    ownerReference,
                    materialization);
            }

            var layoutWrite = DecideLayoutWrite(storedLayout, layoutValidation.Layout);
            var datumWrite = DecideDatumWrite(storedDatum, datumValidation.Datum);
            if (layoutWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged ||
                datumWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged)
            {
                if (!owner.IsWriteEnabled)
                {
                    owner.UpgradeOpen();
                }

                if (layoutWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged)
                {
                    RoofPurlinLayoutStore.Write(owner, transaction, layoutValidation.Layout);
                }

                if (datumWrite != RoofAutomaticPurlinOwnerWriteResult.Unchanged)
                {
                    RoofRelativeElevationDatumStore.Write(owner, transaction, datumValidation.Datum);
                }
            }

            var layoutReadback = RoofPurlinLayoutStore.Read(owner);
            var datumReadback = RoofRelativeElevationDatumStore.Read(owner);
            if (layoutReadback.Data is null ||
                !RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                    layoutReadback.Data,
                    layoutValidation.Layout) ||
                datumReadback.Data != datumValidation.Datum)
            {
#if DEBUG
                var writtenWp = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(
                    layoutValidation.Layout);
                var readWp = layoutReadback.Data is null
                    ? null
                    : RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(
                        layoutReadback.Data);
                System.Diagnostics.Debug.WriteLine(
                    "ROOF_PURLIN_APPLY_POSTCONDITION" +
                    $" owner={ownerReference}" +
                    $" layoutExists={(layoutReadback.Exists ? "1" : "0")}" +
                    $" layoutError={layoutReadback.Error}" +
                    $" writtenPlace={writtenWp.PlacementValueMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
                    $" writtenLowerEdge={layoutValidation.Layout.WallPlateLowerEdgeHeightMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
                    $" readPlace={(readWp?.PlacementValueMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-")}" +
                    $" readLowerEdge={(layoutReadback.Data?.WallPlateLowerEdgeHeightMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-")}" +
                    $" layoutEquivalent={(layoutReadback.Data is not null && RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(layoutReadback.Data, layoutValidation.Layout) ? "1" : "0")}" +
                    $" datumEquivalent={(datumReadback.Data == datumValidation.Datum ? "1" : "0")}" +
                    $" writtenDatumLocalZ={datumValidation.Datum.ReferenceLocalZMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
                    $" readDatumLocalZ={(datumReadback.Data?.ReferenceLocalZMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "-")}");
#endif
                return RoofAutomaticPurlinApplyResult.Failure(
                    "owner-metadata-postcondition-failed",
                    ownerReference,
                    materialization);
            }

            transaction.Commit();
            return RoofAutomaticPurlinApplyResult.Success(
                ownerReference,
                layoutWrite,
                datumWrite,
                materialization);
        }
        catch (System.Exception exception)
        {
            return RoofAutomaticPurlinApplyResult.Failure(
                exception.GetType().Name);
        }
    }

    private static RoofAutomaticPurlinOwnerWriteResult DecideLayoutWrite(
        RoofPurlinLayoutStoreReadResult stored,
        RoofAutomaticPurlinLayout desired) =>
        !stored.Exists
            ? RoofAutomaticPurlinOwnerWriteResult.Created
            : stored.Data is not null &&
              RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(stored.Data, desired)
                ? RoofAutomaticPurlinOwnerWriteResult.Unchanged
                : RoofAutomaticPurlinOwnerWriteResult.Updated;

    private static RoofAutomaticPurlinOwnerWriteResult DecideDatumWrite(
        RoofRelativeElevationDatumStoreReadResult stored,
        RoofRelativeElevationDatum desired) =>
        !stored.Exists
            ? RoofAutomaticPurlinOwnerWriteResult.Created
            : stored.Data == desired
                ? RoofAutomaticPurlinOwnerWriteResult.Unchanged
                : RoofAutomaticPurlinOwnerWriteResult.Updated;
}

internal enum RoofAutomaticPurlinOwnerWriteResult
{
    Created,
    Updated,
    Unchanged,
}

internal sealed record RoofAutomaticPurlinApplyResult(
    bool IsSuccess,
    string OwnerReference,
    RoofAutomaticPurlinOwnerWriteResult LayoutWrite,
    RoofAutomaticPurlinOwnerWriteResult DatumWrite,
    RoofAutomaticPurlinMaterializationResult Materialization,
    string Result)
{
    public static RoofAutomaticPurlinApplyResult Success(
        string ownerReference,
        RoofAutomaticPurlinOwnerWriteResult layoutWrite,
        RoofAutomaticPurlinOwnerWriteResult datumWrite,
        RoofAutomaticPurlinMaterializationResult materialization) =>
        new(true, ownerReference, layoutWrite, datumWrite, materialization, "ok");

    public static RoofAutomaticPurlinApplyResult Failure(
        string result,
        string ownerReference = "-",
        RoofAutomaticPurlinMaterializationResult? materialization = null) =>
        new(
            false,
            ownerReference,
            RoofAutomaticPurlinOwnerWriteResult.Unchanged,
            RoofAutomaticPurlinOwnerWriteResult.Unchanged,
            materialization ?? RoofAutomaticPurlinMaterializationResult.Failure(
                result,
                ownerReference),
            result);
}
