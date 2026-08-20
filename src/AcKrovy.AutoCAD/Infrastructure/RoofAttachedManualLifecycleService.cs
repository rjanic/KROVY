using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofAttachedManualLifecycleService
{
    public static RoofAttachedManualTimberData CreateAnchoredData(
        string roofOwnerReference,
        string childIdentity,
        RoofGeneratedMemberKey anchorKey,
        Point3d anchorStart,
        Point3d anchorEnd,
        Point3d childStart,
        Point3d childEnd,
        RoofAttachedManualOrigin origin = RoofAttachedManualOrigin.Split)
    {
        if (!RoofAttachedManualRelativeGeometryRules.TryCapture(
                ToRoof(anchorStart),
                ToRoof(anchorEnd),
                ToRoof(childStart),
                ToRoof(childEnd),
                out var relative))
        {
            throw new InvalidOperationException("Attached-manual relative capture failed.");
        }

        return new RoofAttachedManualTimberData(
            RoofAttachedManualTimberDataSchema.CurrentVersion,
            roofOwnerReference,
            childIdentity,
            RoofTimberChildRole.AttachedManual,
            anchorKey,
            relative,
            origin);
    }

    public static bool TryFindGeneratedAnchorLine(
        Database database,
        Transaction transaction,
        string ownerReference,
        RoofGeneratedMemberKey anchorKey,
        out Line? anchorLine)
    {
        anchorLine = null;
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line);
            if (generated.Data is null)
            {
                continue;
            }

            if (RoofGeneratedMemberKey.From(generated.Data).Equals(anchorKey))
            {
                anchorLine = line;
                return true;
            }
        }

        return false;
    }

    public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner(
        Document document,
        Transaction transaction,
        string ownerReference,
        IReadOnlyDictionary<RoofGeneratedMemberKey, string>? oldAnchorHandleByKey = null,
        RoofAttachedManualOrigin? originFilter = null,
        IReadOnlyList<RoofPoint2D>? sourceFootprintVertices = null)
    {
        var replayed = 0;
        var dormant = 0;
        var reactivated = 0;
        var dormancyOutsideFootprint = 0;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var presentationBatch = AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        foreach (var attachedId in RoofAttachedManualTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    attachedId,
                    OpenMode.ForWrite,
                    out var childLine,
                    document.Database) ||
                childLine is null ||
                childLine.IsErased)
            {
                continue;
            }

            var stored = RoofAttachedManualTimberStore.Read(childLine);
            if (stored.Data is null)
            {
                continue;
            }

            if (originFilter is not null &&
                stored.Data.Origin != originFilter.Value)
            {
                continue;
            }

            if (stored.Data.AnchorGeneratedMemberKey is null ||
                stored.Data.RelativeSegment is null)
            {
                // A recognized-origin child with malformed metadata (missing anchor /
                // relative) must NOT be silently skipped — it would stay stale visible
                // outside the roof. Treat it as dormant: hide geometry, remove annotation,
                // retain metadata/identity. No silent fourth state.
                MakeCopyChildDormant(document, transaction, childLine);
                dormant++;
                continue;
            }

            var anchorKey = stored.Data.AnchorGeneratedMemberKey.Value;
            var anchorHandle = "-";
            if (!TryFindGeneratedAnchorLine(
                    document.Database,
                    transaction,
                    ownerReference,
                    anchorKey,
                    out var anchorLine) ||
                anchorLine is null)
            {
                // Exact anchor station no longer exists: the COPY child becomes
                // dormant. Geometry is hidden (persisted) and annotations removed;
                // owner/identity/Origin.Copy/anchor key/RelativeSegment stay intact.
                MakeCopyChildDormant(document, transaction, childLine);
                dormant++;
#if DEBUG
                RoofAttachedManualAnchorDiag.WriteReplay(
                    document.Editor,
                    childLine.Handle.ToString(),
                    RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(anchorKey),
                    anchorResolved: false,
                    "-",
                    "-",
                    "anchor-missing");
#endif
                continue;
            }

            anchorHandle = anchorLine.Handle.ToString();
            var oldAnchorHandle = "-";
            if (oldAnchorHandleByKey is not null &&
                oldAnchorHandleByKey.TryGetValue(anchorKey, out var mappedOld))
            {
                oldAnchorHandle = mappedOld;
            }

            if (!RoofAttachedManualRelativeGeometryRules.TryReplay(
                    ToRoof(anchorLine.StartPoint),
                    ToRoof(anchorLine.EndPoint),
                    stored.Data.RelativeSegment,
                    out var childStart,
                    out var childEnd))
            {
#if DEBUG
                RoofAttachedManualAnchorDiag.WriteReplay(
                    document.Editor,
                    childLine.Handle.ToString(),
                    RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(anchorKey),
                    anchorResolved: true,
                    oldAnchorHandle,
                    anchorHandle,
                    "replay-failed");
#endif
                continue;
            }

            // Source-resize spatial containment applies to BOTH Origin.Copy and
            // Origin.Split: anchor existence is necessary but not sufficient. The FINAL
            // replayed segment must lie fully inside/on the current source roof
            // footprint; if any meaningful part leaves it, the child goes dormant through
            // the SAME mechanism as a missing anchor. Both persistent origins share this
            // decision; their distinct edit/anchor/ERASE semantics elsewhere are
            // unchanged.
            if (sourceFootprintVertices is { Count: >= 3 } &&
                !RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
                    new RoofPoint2D(childStart.X, childStart.Y),
                    new RoofPoint2D(childEnd.X, childEnd.Y),
                    sourceFootprintVertices))
            {
                MakeCopyChildDormant(document, transaction, childLine);
                dormant++;
                dormancyOutsideFootprint++;
#if DEBUG
                RoofAttachedManualAnchorDiag.WriteReplay(
                    document.Editor,
                    childLine.Handle.ToString(),
                    RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(anchorKey),
                    anchorResolved: true,
                    oldAnchorHandle,
                    anchorHandle,
                    "outside-footprint");
#endif
                continue;
            }

            var wasDormant = !childLine.Visible;
            childLine.Visible = true;
            childLine.StartPoint = ToAcad(childStart);
            childLine.EndPoint = ToAcad(childEnd);
            if (metadataStore.TryRead(childLine, out var timberData) && timberData is not null)
            {
                _ = TimberAnnotationService.EnsureForElement(
                    document.Database,
                    transaction,
                    childLine,
                    timberData,
                    presentationBatch,
                    roundingStepMm: roundingStepMm);
            }

#if DEBUG
            RoofAttachedManualAnchorDiag.WriteReplay(
                document.Editor,
                childLine.Handle.ToString(),
                RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(anchorKey),
                anchorResolved: true,
                oldAnchorHandle,
                anchorHandle,
                "ok");
#endif
            replayed++;
            if (wasDormant)
            {
                reactivated++;
            }
        }

        return new RoofCopyReplayResult(replayed, dormant, reactivated, dormancyOutsideFootprint);
    }

    private static void MakeCopyChildDormant(
        Document document,
        Transaction transaction,
        Line childLine)
    {
        // Hide the geometry via the persisted DXF 60 visibility flag. The entity is
        // NOT erased, so its XData (owner / child identity / Origin / anchor key /
        // RelativeSegment) survives SAVE/REOPEN and U/REDO. Annotations are removed so
        // no stale label remains selectable. Shared by Origin.Copy and Origin.Split.
        childLine.Visible = false;
        TimberAnnotationService.DeleteForSourceHandle(
            document.Database,
            transaction,
            childLine.Handle.ToString());
    }

    public static void RefreshModifiedAttachedManualRelatives(
        Document document,
        Transaction transaction,
        string ownerReference,
        IReadOnlyCollection<ObjectId> modifiedIds,
        string? globalCommandName)
    {
        var reanchor = RoofGeneratedMemberEditCommandRules.IsMoveCommand(globalCommandName);

        foreach (var attachedId in RoofAttachedManualTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     ownerReference))
        {
            if (!modifiedIds.Contains(attachedId))
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    attachedId,
                    OpenMode.ForRead,
                    out var childLine,
                    document.Database) ||
                childLine is null)
            {
                continue;
            }

            var stored = RoofAttachedManualTimberStore.Read(childLine);
            if (stored.Data?.AnchorGeneratedMemberKey is null)
            {
                continue;
            }

            var anchorKey = stored.Data.AnchorGeneratedMemberKey.Value;
            var resolvedKey = anchorKey;
            var reanchored = false;
            var anchorStart = Point3d.Origin;
            var anchorEnd = Point3d.Origin;

            if (reanchor &&
                stored.Data.Origin == RoofAttachedManualOrigin.Copy &&
                TrySelectNearestCopyAnchor(
                    document.Database,
                    transaction,
                    ownerReference,
                    anchorKey,
                    childLine,
                    out var selected) &&
                selected is not null)
            {
                resolvedKey = selected.Key;
                reanchored = selected.Key != anchorKey;
                anchorStart = ToAcad(selected.Start);
                anchorEnd = ToAcad(selected.End);
            }
            else if (TryFindGeneratedAnchorLine(
                         document.Database,
                         transaction,
                         ownerReference,
                         anchorKey,
                         out var anchorLine) &&
                     anchorLine is not null)
            {
                anchorStart = anchorLine.StartPoint;
                anchorEnd = anchorLine.EndPoint;
            }
            else
            {
                continue;
            }

            var data = CreateAnchoredData(
                ownerReference,
                stored.Data.ChildIdentity,
                resolvedKey,
                anchorStart,
                anchorEnd,
                childLine.StartPoint,
                childLine.EndPoint,
                stored.Data.Origin);
            childLine.UpgradeOpen();
            RoofAttachedManualTimberStore.Write(childLine, transaction, data);
#if DEBUG
            if (reanchored)
            {
                WriteReanchorDiag(
                    document,
                    childLine.Handle.ToString(),
                    ownerReference,
                    anchorKey,
                    resolvedKey);
            }
            else
            {
                WriteAnchorDiag(document, childLine.Handle.ToString(), data, "ok");
            }
#endif
        }
    }

    private static bool TrySelectNearestCopyAnchor(
        Database database,
        Transaction transaction,
        string ownerReference,
        RoofGeneratedMemberKey currentAnchorKey,
        Line childLine,
        out RoofReanchorCandidate? selected)
    {
        selected = null;
        var candidates = new List<RoofReanchorCandidate>();
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line).Data;
            if (generated is null)
            {
                continue;
            }

            candidates.Add(new RoofReanchorCandidate(
                RoofGeneratedMemberKey.From(generated),
                ToRoof(line.StartPoint),
                ToRoof(line.EndPoint)));
        }

        selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            currentAnchorKey,
            candidates,
            ToRoof(childLine.StartPoint),
            ToRoof(childLine.EndPoint));
        return selected is not null;
    }

    public static void CapturePreResizeAnchorHandles(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference)
    {
        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
            return;
        }

        var map = new Dictionary<RoofGeneratedMemberKey, string>();
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line).Data;
            if (generated is null)
            {
                continue;
            }

            map[RoofGeneratedMemberKey.From(generated)] = line.Handle.ToString();
        }

        entry.SetPreResizeAnchorHandleByKey(map);
    }

    public static void WriteAnchored(
        Line childLine,
        Transaction transaction,
        RoofAttachedManualTimberData data)
    {
        RoofAttachedManualTimberStore.Write(childLine, transaction, data);
    }

#if DEBUG
    public static void WriteAnchorDiag(
        Document document,
        string handle,
        RoofAttachedManualTimberData data,
        string result)
    {
        if (data.AnchorGeneratedMemberKey is null || data.RelativeSegment is null)
        {
            return;
        }

        var key = data.AnchorGeneratedMemberKey.Value;
        var rel = data.RelativeSegment;
        RoofAttachedManualAnchorDiag.WriteAnchor(
            document.Editor,
            handle,
            data.RoofOwnerReference,
            RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(key),
            rel.U0Mm,
            rel.V0Mm,
            rel.U1Mm,
            rel.V1Mm,
            result);
    }

    private static void WriteReanchorDiag(
        Document document,
        string handle,
        string ownerReference,
        RoofGeneratedMemberKey oldAnchor,
        RoofGeneratedMemberKey newAnchor)
    {
        RoofAttachedManualAnchorDiag.WriteReanchor(
            document.Editor,
            handle,
            ownerReference,
            RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(oldAnchor),
            RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(newAnchor),
            "ok");
    }
#endif

    private static RoofPoint3D ToRoof(Point3d point) => new(point.X, point.Y, point.Z);

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);
}

internal sealed record RoofCopyReplayResult(
    int Replayed,
    int Dormant,
    int Reactivated,
    int DormantOutsideFootprint = 0);

#if DEBUG
internal static class RoofAttachedManualAnchorDiag
{
    public static void WriteAnchor(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string handle,
        string owner,
        string anchor,
        double u0,
        double v0,
        double u1,
        double v1,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ATTACHED_MANUAL_ANCHOR" +
            $" handle={handle}" +
            $" owner={owner}" +
            $" anchor={anchor}" +
            $" u0={u0.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" v0={v0.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" u1={u1.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" v1={v1.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteReplay(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string handle,
        string anchor,
        bool anchorResolved,
        string oldAnchorHandle,
        string newAnchorHandle,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ATTACHED_MANUAL_REPLAY" +
            $" handle={handle}" +
            $" anchor={anchor}" +
            $" anchorResolved={anchorResolved.ToString().ToLowerInvariant()}" +
            $" oldAnchorHandle={oldAnchorHandle}" +
            $" newAnchorHandle={newAnchorHandle}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteReanchor(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string handle,
        string owner,
        string oldAnchor,
        string newAnchor,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_ATTACHED_MANUAL_REANCHOR" +
            $" handle={handle}" +
            $" owner={owner}" +
            $" oldAnchor={oldAnchor}" +
            $" newAnchor={newAnchor}" +
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
