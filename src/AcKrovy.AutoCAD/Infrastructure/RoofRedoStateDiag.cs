#if DEBUG
using System.Text;
using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Read-only DEBUG state snapshots for the SupportedResize lifecycle. Runs ONLY inside
/// the existing resize transaction during a genuine STRETCH command; it never opens its
/// own transaction and MUST NOT be invoked from the U/UNDO/REDO/MREDO command boundary
/// (zero database access is required there to preserve the native REDO stack).
/// </summary>
internal static class RoofRedoStateDiag
{
    public static void Capture(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference,
        string checkpoint)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
            if (editor is null)
            {
                return;
            }

            var generated = RoofGeneratedTimberStore.FindByOwner(
                database,
                transaction,
                ownerReference);
            var attached = RoofAttachedManualTimberStore.FindByOwner(
                database,
                transaction,
                ownerReference);

            var stations = new List<string>();
            foreach (var id in generated)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                {
                    continue;
                }

                var stored = RoofGeneratedTimberStore.Read(entity);
                if (stored.Data is null)
                {
                    continue;
                }

                stations.Add(RoofGeneratedMemberKey.From(stored.Data).ToString());
            }

            var distinctStations = stations.Distinct(System.StringComparer.Ordinal).Count();
            var uniqueStations = stations.Count > 0 && distinctStations == stations.Count;
            var duplicateKeyCount = stations.Count - distinctStations;

            var groupMemberCount = 0;
            var generatedInGroup = 0;
            var annotationsInGroup = 0;
            if (ownerId != ObjectId.Null &&
                RoofDisplayGroupService.TryOpenCanonicalGroup(
                    database,
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var group) &&
                group is not null)
            {
                foreach (var memberId in group.GetAllEntityIds())
                {
                    groupMemberCount++;
                    if (transaction.GetObject(memberId, OpenMode.ForRead, false) is not Entity member)
                    {
                        continue;
                    }

                    if (RoofGeneratedTimberStore.Read(member).Data is not null)
                    {
                        generatedInGroup++;
                    }
                    else if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(member, out _))
                    {
                        annotationsInGroup++;
                    }
                }
            }

            var line = new StringBuilder();
            line.Append("ROOF_REDO_STATE");
            line.Append(" owner=").Append(ownerReference);
            line.Append(" checkpoint=").Append(checkpoint);
            line.Append(" generatedCount=").Append(generated.Count);
            line.Append(" uniqueStations=").Append(uniqueStations ? "true" : "false");
            line.Append(" duplicateKeyCount=").Append(duplicateKeyCount);
            line.Append(" attachedManualCount=").Append(attached.Count);
            line.Append(" groupMemberCount=").Append(groupMemberCount);
            line.Append(" generatedInGroup=").Append(generatedInGroup);
            line.Append(" annotationsInGroup=").Append(annotationsInGroup);
            line.Append(" result=").Append(generated.Count > 0 && uniqueStations ? "ok" : "inconsistent");
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void TraceTxn(string transactionName, string phase)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
            if (editor is null)
            {
                return;
            }

            editor.WriteMessage(
                "\nROOF_RESIZE_TXN" +
                " txn=" + transactionName +
                " phase=" + phase);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Read-only ownership invariant emitted at the start of a genuine SupportedResize,
    /// before any mutation. Detects generic Timber entities in the roof group that have
    /// lost their RoofGenerated metadata (the orphan pattern after a U/REDO cycle).
    /// Diagnostic only — never repairs.
    /// </summary>
    public static void CaptureOwnershipInvariant(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference)
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
            if (editor is null)
            {
                return;
            }

            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            var generatedByOwner = RoofGeneratedTimberStore.FindByOwner(
                database,
                transaction,
                ownerReference).Count;
            var groupTimberCandidates = 0;
            var missingGeneratedMetadata = 0;
            var orphanHandles = new List<string>();
            if (ownerId != ObjectId.Null &&
                RoofDisplayGroupService.TryOpenCanonicalGroup(
                    database,
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var group) &&
                group is not null)
            {
                foreach (var memberId in group.GetAllEntityIds())
                {
                    if (transaction.GetObject(memberId, OpenMode.ForRead, false) is not Entity member)
                    {
                        continue;
                    }

                    if (!metadataStore.TryRead(member, out var timber) || timber is null)
                    {
                        continue;
                    }

                    // A valid AttachedManual child legitimately carries generic Timber
                    // metadata without Generated metadata — exclude it from the orphan
                    // detection so it is not misreported as a lost Generated member.
                    if (RoofAttachedManualTimberStore.Read(member).Data is not null)
                    {
                        continue;
                    }

                    groupTimberCandidates++;
                    var generated = RoofGeneratedTimberStore.Read(member);
                    if (generated.Data is null)
                    {
                        missingGeneratedMetadata++;
                        if (orphanHandles.Count < 8)
                        {
                            orphanHandles.Add(member.Handle.ToString());
                        }
                    }
                }
            }

            editor.WriteMessage(
                "\nROOF_GENERATED_OWNERSHIP_INVARIANT" +
                " owner=" + ownerReference +
                " physicalTimberCandidates=" + groupTimberCandidates +
                " generatedByOwner=" + generatedByOwner +
                " groupTimberCandidates=" + groupTimberCandidates +
                " missingGeneratedMetadata=" + missingGeneratedMetadata +
                " orphanHandles=" + string.Join(",", orphanHandles) +
                " result=" + (missingGeneratedMetadata == 0 ? "ok" : "failure"));
        }
        catch
        {
        }
    }
}
#endif
