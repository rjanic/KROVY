using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Re-initializes same-DWG native COPY clones of COPY-origin AttachedManual children.
/// AutoCAD clones the source's AttachedManual XData verbatim, so a copy would otherwise
/// keep the source's ChildIdentity and stale RelativeSegment and later replay on top of
/// its source. Each clone is re-captured from its FINAL WCS geometry with a fresh
/// ChildIdentity (clone handle) and a RelativeSegment computed against a deterministic
/// compatible Generated anchor.
/// </summary>
internal static class RoofAttachedManualCopyCloneReinitializeService
{
    public static void Process(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName) ||
            appendedTimberIds.Count == 0)
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var wrote = false;
                foreach (var id in appendedTimberIds)
                {
                    if (id.IsNull ||
                        id.IsErased ||
                        !AutoCadObjectIdAccess.TryGetObject<Line>(
                            transaction,
                            id,
                            OpenMode.ForWrite,
                            out var cloneLine,
                            document.Database) ||
                        cloneLine is null)
                    {
                        continue;
                    }

                    var attached = RoofAttachedManualTimberStore.Read(cloneLine);
                    if (attached.Data is null ||
                        attached.Data.Origin != RoofAttachedManualOrigin.Copy ||
                        attached.Data.AnchorGeneratedMemberKey is null)
                    {
                        // Not a COPY-origin AttachedManual clone (generic timber, Generated
                        // clone, or Split-origin child): out of scope for this path.
                        continue;
                    }

                    var ownerReference = attached.Data.RoofOwnerReference;
                    var oldAnchorKey = attached.Data.AnchorGeneratedMemberKey.Value;
                    var sourceIdentity = attached.Data.ChildIdentity;

                    if (!TryReinitializeClone(
                            document,
                            transaction,
                            cloneLine,
                            ownerReference,
                            oldAnchorKey,
                            out var newAnchorKey,
                            out var result))
                    {
#if DEBUG
                        WriteCopyDiag(
                            document,
                            sourceIdentity,
                            cloneLine.Handle.ToString(),
                            ownerReference,
                            oldAnchorKey,
                            newAnchorKey,
                            result);
#endif
                        continue;
                    }

                    wrote = true;
#if DEBUG
                    WriteCopyDiag(
                        document,
                        sourceIdentity,
                        cloneLine.Handle.ToString(),
                        ownerReference,
                        oldAnchorKey,
                        newAnchorKey,
                        "ok");
#endif
                    _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
                        document,
                        transaction,
                        ownerReference);
                }

                if (wrote)
                {
                    transaction.Commit();
                }
            }
        }
        catch (System.Exception)
        {
            // Silent internal maintenance — do not break native COPY UX.
        }
    }

    private static bool TryReinitializeClone(
        Document document,
        Transaction transaction,
        Line cloneLine,
        string ownerReference,
        RoofGeneratedMemberKey inheritedAnchorKey,
        out RoofGeneratedMemberKey newAnchorKey,
        out string result)
    {
        newAnchorKey = inheritedAnchorKey;
        result = "-";

        var candidates = new List<RoofReanchorCandidate>();
        foreach (var genId in RoofGeneratedTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    genId,
                    OpenMode.ForRead,
                    out var genLine,
                    document.Database) ||
                genLine is null)
            {
                continue;
            }

            var genData = RoofGeneratedTimberStore.Read(genLine).Data;
            if (genData is null)
            {
                continue;
            }

            candidates.Add(new RoofReanchorCandidate(
                RoofGeneratedMemberKey.From(genData),
                ToRoof(genLine.StartPoint),
                ToRoof(genLine.EndPoint)));
        }

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            inheritedAnchorKey.MemberKind,
            candidates,
            ToRoof(cloneLine.StartPoint),
            ToRoof(cloneLine.EndPoint));

        if (selected is null)
        {
            result = "no-compatible-anchor";
            return false;
        }

        newAnchorKey = selected.Key;
        var attachedData = RoofAttachedManualLifecycleService.CreateAnchoredData(
            ownerReference,
            cloneLine.Handle.ToString(),
            selected.Key,
            ToAcad(selected.Start),
            ToAcad(selected.End),
            cloneLine.StartPoint,
            cloneLine.EndPoint,
            RoofAttachedManualOrigin.Copy);
        RoofAttachedManualLifecycleService.WriteAnchored(cloneLine, transaction, attachedData);
        return true;
    }

    private static RoofPoint3D ToRoof(Point3d point) => new(point.X, point.Y, point.Z);

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);

#if DEBUG
    private static void WriteCopyDiag(
        Document document,
        string source,
        string clone,
        string owner,
        RoofGeneratedMemberKey oldSourceAnchor,
        RoofGeneratedMemberKey newCloneAnchor,
        string result)
    {
        try
        {
            document.Editor?.WriteMessage(
                "\nROOF_ATTACHED_MANUAL_COPY" +
                $" source={source}" +
                $" clone={clone}" +
                $" owner={owner}" +
                $" oldSourceAnchor={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(oldSourceAnchor)}" +
                $" newCloneAnchor={RoofAttachedManualRelativeGeometryRules.FormatAnchorKey(newCloneAnchor)}" +
                $" relativeCaptured={(result == "ok" ? "true" : "false")}" +
                $" result={result}");
        }
        catch
        {
        }
    }
#endif
}
