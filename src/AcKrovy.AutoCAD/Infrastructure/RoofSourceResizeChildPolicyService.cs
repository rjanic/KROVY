using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// After a source SupportedResize, incidental native STRETCH on owned children must not
/// become manual edits. Generated members are rebuilt by the resize recipe. AttachedManual
/// Copy and Split children are anchor-replayed against their rebuilt Generated anchor:
/// they follow it while the exact anchor survives, and go dormant (persisted, hidden) when
/// that exact anchor temporarily disappears — never permanently deleted by a temporary
/// footprint shrink. Copy and Split keep distinct Origin semantics but share the same
/// anchored replay/dormancy lifecycle.
/// </summary>
internal static class RoofSourceResizeChildPolicyService
{
    public sealed record ApplyResult(
        int GeneratedRebuilt,
        int GeneratedOverridesReplayed,
        int AttachedManualKeptInPlace,
        int AttachedManualDeletedOutside,
        int AttachedManualCopyReplayed,
        int AttachedManualCopyDormant,
        int AttachedManualCopyReactivated,
        int AttachedManualSplitReplayed,
        int AttachedManualSplitDormant,
        int AttachedManualSplitReactivated,
        int AttachedManualCopyDormantOutsideFootprint,
        int AttachedManualSplitDormantOutsideFootprint);

    public static ApplyResult Apply(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofGeneratedRafterSetService.ReplacementOutcome rafterOutcome,
        int generatedMemberCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);

        var ownerReference = owner.Handle.ToString();

        // Current source footprint — the ONLY spatial authority for Origin.Copy and
        // Origin.Split containment after exact-anchor replay (never Group/annotation
        // extents).
        IReadOnlyList<RoofPoint2D>? sourceFootprintVertices = null;
        {
            var footprintInput = RoofPolylineExtractor.Extract(owner);
            var footprintValidation = RoofFootprintValidator.Validate(footprintInput);
            if (footprintValidation.IsValid && footprintValidation.Footprint is not null)
            {
                sourceFootprintVertices = footprintValidation.Footprint.Vertices;
            }
        }

        var generatedRebuilt = rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.Replaced
            ? generatedMemberCount
            : 0;
        var overridesReplayed = generatedRebuilt > 0
            ? CountPersistedOverrides(owner)
            : 0;

        // COPY- and Split-origin AttachedManual children follow their rebuilt generated
        // anchor and are validated against the current source footprint after replay.
        // Split/BREAK fragments are ALSO persistent roof children: they anchor-replay
        // against the rebuilt Generated anchor, and go dormant (never permanently
        // deleted) when their exact anchor disappears OR the replayed segment leaves the
        // current footprint. Same anchored lifecycle as Copy, distinct Origin semantics.
        var copyReplayed = 0;
        var copyDormant = 0;
        var copyReactivated = 0;
        var copyDormantOutsideFootprint = 0;
        var splitReplayed = 0;
        var splitDormant = 0;
        var splitReactivated = 0;
        var splitDormantOutsideFootprint = 0;
        if (generatedRebuilt > 0)
        {
            var copyReplay = RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner(
                document,
                transaction,
                ownerReference,
                oldAnchorHandleByKey: null,
                originFilter: RoofAttachedManualOrigin.Copy,
                sourceFootprintVertices: sourceFootprintVertices);
            copyReplayed = copyReplay.Replayed;
            copyDormant = copyReplay.Dormant;
            copyReactivated = copyReplay.Reactivated;
            copyDormantOutsideFootprint = copyReplay.DormantOutsideFootprint;

            var splitReplay = RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner(
                document,
                transaction,
                ownerReference,
                oldAnchorHandleByKey: null,
                originFilter: RoofAttachedManualOrigin.Split,
                sourceFootprintVertices: sourceFootprintVertices);
            splitReplayed = splitReplay.Replayed;
            splitDormant = splitReplay.Dormant;
            splitReactivated = splitReplay.Reactivated;
            splitDormantOutsideFootprint = splitReplay.DormantOutsideFootprint;
        }

        var (attachedKept, attachedDeleted) = ApplyAttachedManualResizePolicy(
            document,
            transaction,
            owner,
            ownerReference);

#if DEBUG
        RoofSourceResizeChildPolicyDiag.WriteSummary(
            document.Editor,
            ownerReference,
            generatedRebuilt,
            overridesReplayed,
            attachedKept,
            attachedDeleted,
            copyReplayed,
            copyDormant,
            copyReactivated,
            splitReplayed,
            splitDormant,
            splitReactivated,
            copyDormantOutsideFootprint,
            splitDormantOutsideFootprint,
            result: "ok");
#endif

        _ = RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, owner.ObjectId);

        return new ApplyResult(
            generatedRebuilt,
            overridesReplayed,
            attachedKept,
            attachedDeleted,
            copyReplayed,
            copyDormant,
            copyReactivated,
            splitReplayed,
            splitDormant,
            splitReactivated,
            copyDormantOutsideFootprint,
            splitDormantOutsideFootprint);
    }

    private static int CountPersistedOverrides(Polyline owner)
    {
        var definition = RoofDefinitionStore.Read(owner).Data;
        return definition?.Overrides?.Count ?? 0;
    }

    /// <summary>
    /// Non-rigid SupportedResize keep/delete policy for AttachedManual children.
    /// Uses the pre-command snapshot geometry (never the crossing-window-deformed
    /// current geometry) and evaluates containment against the FINAL resized footprint.
    /// </summary>
    private static (int Kept, int Deleted) ApplyAttachedManualResizePolicy(
        Document document,
        Transaction transaction,
        Polyline owner,
        string ownerReference)
    {
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            // Conservative: no valid footprint to evaluate against, keep everything.
            return (0, 0);
        }

        var finalFootprintVertices = validation.Footprint.Vertices;
        var snapshotByHandle =
            new Dictionary<string, RoofUnsupportedStretchTimberLineSnapshotData>(
                StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> snapshotAnnotations =
            Array.Empty<RoofUnsupportedStretchAnnotationSnapshotData>();
        if (RoofUnsupportedStretchRecoverySnapshotService.TryGet(owner.ObjectId, out var entry))
        {
            foreach (var timber in entry.Assembly.TimberLines)
            {
                snapshotByHandle[timber.EntityHandle] = timber;
            }

            snapshotAnnotations = entry.Assembly.Annotations;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var presentationBatch = AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        var kept = 0;
        var deleted = 0;

        foreach (var attachedId in RoofAttachedManualTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    attachedId,
                    OpenMode.ForWrite,
                    out var line,
                    document.Database) ||
                line is null ||
                line.IsErased)
            {
                continue;
            }

            // COPY- and Split-origin children were already anchor-replayed above; the
            // keep-in-place/delete-outside rule applies only to non-anchored legacy
            // children (none in the current Copy/Split model).
            var origin = RoofAttachedManualTimberStore.Read(line).Data?.Origin;
            if (origin == RoofAttachedManualOrigin.Copy ||
                origin == RoofAttachedManualOrigin.Split)
            {
                continue;
            }

            var handle = line.Handle.ToString();

            // Exact pre-command geometry — never the crossing-window-deformed current
            // geometry. Fall back to current geometry when the child has no snapshot.
            var preStart = line.StartPoint;
            var preEnd = line.EndPoint;
            if (snapshotByHandle.TryGetValue(handle, out var snapshot))
            {
                preStart = ToAcad(snapshot.Start);
                preEnd = ToAcad(snapshot.End);
            }

            var insideFinalFootprint = RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
                new RoofPoint2D(preStart.X, preStart.Y),
                new RoofPoint2D(preEnd.X, preEnd.Y),
                finalFootprintVertices);

            if (insideFinalFootprint)
            {
                // CASE A: keep exactly at pre-STRETCH WCS geometry.
                line.StartPoint = preStart;
                line.EndPoint = preEnd;
                if (metadataStore.TryRead(line, out var timberData) && timberData is not null)
                {
                    _ = TimberAnnotationService.EnsureForElement(
                        document.Database,
                        transaction,
                        line,
                        timberData,
                        presentationBatch,
                        roundingStepMm: roundingStepMm);
                }

                kept++;
#if DEBUG
                RoofAttachedManualResizePolicyDiag.Write(
                    document.Editor,
                    ownerReference,
                    handle,
                    insideFinalFootprint: true,
                    "keep-in-place",
                    result: "ok");
#endif
            }
            else
            {
                // CASE B: permanently delete the child, its annotations and metadata.
                // Detach the child + its annotation family from the GROUP BEFORE erasing
                // so native U can reverse the erase without re-adding an erased ObjectId
                // to the group (eInvalidInput). Erasing the Line also removes its XData.
                var annotationsRemoved = CountAnnotationsForHandle(snapshotAnnotations, handle);
                var detachedTimber = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
                    document.Database,
                    transaction,
                    owner.ObjectId,
                    [attachedId]);
                TimberAnnotationService.DeleteForSourceHandle(
                    document.Database,
                    transaction,
                    handle);
                line.Erase();
                deleted++;
#if DEBUG
                RoofAttachedManualResizePolicyDiag.Write(
                    document.Editor,
                    ownerReference,
                    handle,
                    insideFinalFootprint: false,
                    "delete-outside",
                    result: "ok");
                RoofAttachedManualResizePolicyDiag.WriteRemoved(
                    document.Editor,
                    ownerReference,
                    handle,
                    "outside-final-footprint",
                    annotationsRemoved,
                    result: "ok");
                RoofResizeEraseDiag.Write(
                    document.Editor,
                    handle,
                    "Timber",
                    groupMemberBefore: detachedTimber > 0,
                    result: "ok");
#endif
            }
        }

        return (kept, deleted);
    }

    private static int CountAnnotationsForHandle(
        IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> annotations,
        string sourceHandle)
    {
        var count = 0;
        foreach (var annotation in annotations)
        {
            if (string.Equals(
                    annotation.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static Point3d ToAcad(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);
}

#if DEBUG
internal static class RoofAttachedManualResizePolicyDiag
{
    public static void Write(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        string handle,
        bool insideFinalFootprint,
        string action,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ATTACHED_MANUAL_RESIZE_POLICY" +
            $" owner={owner}" +
            $" handle={handle}" +
            $" insideFinalFootprint={insideFinalFootprint.ToString().ToLowerInvariant()}" +
            $" action={action}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteRemoved(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        string handle,
        string reason,
        int annotationsRemoved,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ATTACHED_MANUAL_REMOVED" +
            $" owner={owner}" +
            $" handle={handle}" +
            $" reason={reason}" +
            $" annotationsRemoved={annotationsRemoved.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }
}

internal static class RoofSourceResizeChildPolicyDiag
{
    public static void WriteSummary(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        int generatedRebuilt,
        int generatedOverridesReplayed,
        int attachedManualKeptInPlace,
        int attachedManualDeletedOutside,
        int attachedManualCopyReplayed,
        int attachedManualCopyDormant,
        int attachedManualCopyReactivated,
        int attachedManualSplitReplayed,
        int attachedManualSplitDormant,
        int attachedManualSplitReactivated,
        int attachedManualCopyDormantOutsideFootprint,
        int attachedManualSplitDormantOutsideFootprint,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_SOURCE_RESIZE_CHILD_POLICY" +
            $" owner={owner}" +
            $" generatedRebuilt={generatedRebuilt.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" generatedOverridesReplayed={generatedOverridesReplayed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualKeptInPlace={attachedManualKeptInPlace.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualDeletedOutside={attachedManualDeletedOutside.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualCopyReplayed={attachedManualCopyReplayed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualCopyDormant={attachedManualCopyDormant.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualCopyReactivated={attachedManualCopyReactivated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualSplitReplayed={attachedManualSplitReplayed.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualSplitDormant={attachedManualSplitDormant.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualSplitReactivated={attachedManualSplitReactivated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualCopyDormantOutsideFootprint={attachedManualCopyDormantOutsideFootprint.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManualSplitDormantOutsideFootprint={attachedManualSplitDormantOutsideFootprint.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" policyBoundary=source-polyline" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }
}

internal static class RoofResizeEraseDiag
{
    public static void Write(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string handle,
        string kind,
        bool groupMemberBefore,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_RESIZE_ERASE" +
            $" handle={handle}" +
            $" kind={kind}" +
            $" groupMemberBefore={groupMemberBefore.ToString().ToLowerInvariant()}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }
}
#endif
