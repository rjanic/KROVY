using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using AcKrovy.AutoCAD.Diagnostics;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class TimberAnnotationService
{
    public static bool EnsureForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        AutoCadAnnotationPresentationBatchContext presentationBatchContext,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm,
        bool copySourcePreservation = false,
        Action<AutoCadItemLeaderBlockVariantResult>? variantResultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(presentationBatchContext);
        if (!AutoCadDatabaseIdentity.IsSame(
                database,
                presentationBatchContext.Database))
        {
            throw new ArgumentException(
                "Annotation presentation batch belongs to a different database.",
                nameof(presentationBatchContext));
        }

        if (TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
            TimberAnnotationMode.NoAnnotations)
        {
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.01",
                "AnnotationMode=NoAnnotations → delete annotations and return false",
                sourceId: sourceEntity.ObjectId,
                sourceHandle: sourceEntity.Handle.ToString());
#endif
            var sourceHandle = sourceEntity.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceHandle);
            return false;
        }

#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.01",
            $"resolve metadata/settings mode={data.AnnotationMode} " +
            $"style={data.ItemNumberLeaderStyle} elementId={data.ElementId}");
#endif
        var presentationContext =
            presentationBatchContext.ResolveForElement(data);
        var annotationScaleContext =
            presentationContext.AnnotationScaleContext;
#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.02",
            $"resolve annotation denominator={annotationScaleContext.Denominator} " +
            $"scaleFactor={annotationScaleContext.ScaleFactor:R} " +
            $"itemPaper={presentationContext.FramedItemCodeText.PaperHeightMm:R} " +
            $"itemModel={presentationContext.FramedItemCodeText.ModelHeightMm:R} " +
            $"textStyle={presentationContext.FramedItemCodeText.ResolvedTextStyleName ?? "<none>"}");
#endif
        void ObserveVariant(AutoCadItemLeaderBlockVariantResult result)
        {
            variantResultObserver?.Invoke(result);
            if (!result.Succeeded)
            {
                AcKrovyDiagnostics.Warning(
                    "FramedItemLeaderVariant",
                    $"Kind={result.Kind}; Reason={result.DiagnosticReason}");
            }
        }

        var isRectangularFootprintPost =
            TimberPostFootprintMetadataRules.IsValidNewFootprintPost(data);
        var hasResolvedFootprintGeometry = PostFootprintRuntimeGeometryResolver.TryResolve(
            sourceEntity,
            data,
            out var footprintGeometry,
            out var footprintDimensions);
        if (hasResolvedFootprintGeometry &&
            sourceEntity is Polyline footprintPolyline &&
            footprintGeometry is not null &&
            footprintDimensions is not null)
        {
            var effectiveData = data with
            {
                WidthMm = footprintDimensions.WidthMm,
                HeightMm = footprintDimensions.HeightMm,
            };
            var footprintLabelCreated = ElementLabelService.UpsertForPostFootprint(
                database,
                transaction,
                footprintPolyline,
                effectiveData,
                footprintGeometry,
                annotationScaleContext,
                previousElementId,
                roundingStepMm,
                copySourcePreservation,
                presentationContext,
                presentationBatchContext.ItemLeaderVariantCatalog,
                ObserveVariant);
            SlopeAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceEntity.Handle.ToString());
            PostFootprintPerpendicularAnnotationService.UpsertForFootprint(
                database,
                transaction,
                footprintPolyline,
                footprintGeometry,
                annotationScaleContext,
                copySourcePreservation);
#if DEBUG
            if (!footprintLabelCreated)
            {
                AutoCadFramedG4HostDiagnostics.Fail(
                    "F.03",
                    "UpsertForPostFootprint returned false",
                    sourceId: sourceEntity.ObjectId,
                    sourceHandle: sourceEntity.Handle.ToString());
            }
            else
            {
                AutoCadFramedG4HostDiagnostics.Outcome(
                    "CREATED",
                    "post-footprint label path");
            }
#endif
            return footprintLabelCreated;
        }

        PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
            database,
            transaction,
            sourceEntity.Handle.ToString());
        var plan = TimberAnnotationRefreshPlanner.Create(data, isRectangularFootprintPost);
        if (!plan.EnsureLabel && !plan.ReconcileSlopeArrow && !plan.ReconcileSlopeAngleText)
        {
#if DEBUG
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.03",
                "plan.EnsureLabel=false AND no slope reconcile → return false",
                sourceId: sourceEntity.ObjectId,
                sourceHandle: sourceEntity.Handle.ToString());
#endif
            var sourceHandle = sourceEntity.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            return false;
        }

#if DEBUG
        AutoCadFramedG4HostDiagnostics.Step(
            "F.03",
            $"ensure leader EnsureLabel={plan.EnsureLabel} " +
            $"combined={TimberAnnotationModeRules.Normalize(data.AnnotationMode) == TimberAnnotationMode.DimensionsWithItemNumber}");
#endif
        var labelCreated = plan.EnsureLabel && ElementLabelService.UpsertForElement(
                database,
                transaction,
                sourceEntity,
                data,
                annotationScaleContext,
                previousElementId,
                roundingStepMm,
                copySourcePreservation,
                presentationContext,
                presentationBatchContext.ItemLeaderVariantCatalog,
                ObserveVariant);
        if (plan.ReconcileSlopeArrow && plan.ReconcileSlopeAngleText)
        {
            SlopeAnnotationService.EnsureForElement(
                database,
                transaction,
                sourceEntity,
                data,
                annotationScaleContext,
                presentationContext,
                copySourcePreservation);
        }

#if DEBUG
        if (!labelCreated)
        {
            AutoCadFramedG4HostDiagnostics.Fail(
                "F.09",
                "EnsureForElement result=false " +
                $"(plan.EnsureLabel={plan.EnsureLabel}; UpsertForElement returned false " +
                "or was skipped)",
                sourceId: sourceEntity.ObjectId,
                sourceHandle: sourceEntity.Handle.ToString(),
                expectedHeight: presentationContext.FramedItemCodeText.ModelHeightMm,
                textStyleId: presentationContext.FramedItemCodeText.ResolvedTextStyleId);
            AutoCadFramedG4HostDiagnostics.Outcome(
                "FAILED",
                "EnsureForElement returned false without exception");
        }
#endif
        return labelCreated;
    }

    public static void DeleteForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        ElementLabelService.DeleteLabelsForMissingSourceHandles(database, transaction, sourceHandles);
        SlopeAnnotationService.DeleteForMissingSourceHandles(database, transaction, sourceHandles);
        PostFootprintPerpendicularAnnotationService.DeleteForMissingSourceHandles(
            database,
            transaction,
            sourceHandles);
    }

    public static void DeleteInsertedWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> labelIds,
        IReadOnlyCollection<ObjectId> slopeArrowIds,
        IReadOnlyCollection<ObjectId> slopeAngleTextIds)
    {
        ElementLabelService.DeleteInsertedLabelsWithoutCurrentSourceHandles(database, transaction, labelIds);
        SlopeAnnotationService.DeleteInsertedWithoutCurrentSourceHandles(
            database,
            transaction,
            slopeArrowIds,
            slopeAngleTextIds);
        PostFootprintPerpendicularAnnotationService.DeleteInsertedWithoutCurrentSourceHandles(
            database,
            transaction,
            slopeArrowIds);
    }

    public static void DeleteDuplicatesForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        ElementLabelService.DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        SlopeAnnotationService.DeleteDuplicatesForExistingSourceHandles(database, transaction);
        PostFootprintPerpendicularAnnotationService.DeleteDuplicatesForExistingSourceHandles(
            database,
            transaction);
    }
}
