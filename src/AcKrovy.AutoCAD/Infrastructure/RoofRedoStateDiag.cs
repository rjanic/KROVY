#if DEBUG
using System.Text;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
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

            var stationKeys = new List<RoofGeneratedMemberKey>();
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

                stationKeys.Add(RoofGeneratedMemberKey.From(stored.Data));
            }

            var distinctStations = stationKeys.Distinct().Count();
            var uniqueStations = distinctStations == stationKeys.Count;
            var duplicateKeyCount = stationKeys.Count - distinctStations;
            var completeStationMetadata = generated.Count == stationKeys.Count;
            var generatedSetConsistent = RoofGeneratedTimberOwnershipRules.IsConsistentOptionalSet(
                generated.Count,
                stationKeys);

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
            line.Append(" readableStationCount=").Append(stationKeys.Count);
            line.Append(" completeStationMetadata=").Append(completeStationMetadata ? "true" : "false");
            line.Append(" uniqueStations=").Append(uniqueStations ? "true" : "false");
            line.Append(" duplicateKeyCount=").Append(duplicateKeyCount);
            line.Append(" attachedManualCount=").Append(attached.Count);
            line.Append(" groupMemberCount=").Append(groupMemberCount);
            line.Append(" generatedInGroup=").Append(generatedInGroup);
            line.Append(" annotationsInGroup=").Append(annotationsInGroup);
            line.Append(" result=").Append(generatedSetConsistent ? "ok" : "inconsistent");
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

    public static void TraceHipResize(
        Autodesk.AutoCAD.EditorInput.Editor editor,
        string ownerReference,
        string? globalCommandName,
        int vertexCount,
        IReadOnlyList<RoofDisplayEdge> edges)
    {
        try
        {
            editor.WriteMessage(
                "\nROOF_HIP_LIVE_RESIZE" +
                " owner=" + ownerReference +
                " command=" + LiveGeometryCommandRules.NormalizeCommandName(globalCommandName) +
                " vertices=" + vertexCount +
                " ridge=" + edges.Count(edge => HipRoofWireframe.IsRidgeRole(edge.Role)) +
                " hip=" + edges.Count(edge =>
                    edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47) +
                " valley=" + edges.Count(edge =>
                    edge.Role is >= RoofDisplayEdgeRole.HipValley00 and <= RoofDisplayEdgeRole.HipValley31));
        }
        catch
        {
        }
    }

    /// <summary>
    /// Read-only ownership invariant emitted at the start of a genuine SupportedResize,
    /// before any mutation. Validates that physical timber candidates resolve to a
    /// supported generated ownership class (ordinary Generated, StructuralGenerated
    /// Hip/Valley, or AutomaticPurlin) or AttachedManual. Diagnostic only — never repairs.
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
            var structuralByOwner = RoofStructuralGeneratedStore.FindByOwner(
                database,
                transaction,
                ownerReference).Count;
            var classifications = new List<RoofGeneratedOwnershipInvariantRules.CandidateClass>();
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

                    var hasTimber = metadataStore.TryRead(member, out var timber) && timber is not null;
                    var attached = RoofAttachedManualTimberStore.Read(member).Data;
                    var ordinary = RoofGeneratedTimberStore.Read(member).Data;
                    var structural = RoofStructuralGeneratedStore.Read(member).Data;
                    var purlin = RoofAutomaticPurlinGeneratedStore.Read(member).Data;
                    var classification = RoofGeneratedOwnershipInvariantRules.Classify(
                        hasGenericTimberMetadata: hasTimber,
                        hasAttachedManualOwnership: attached is not null,
                        hasOrdinaryGeneratedOwnership: ordinary is not null,
                        ordinaryOwnerReference: ordinary?.RoofOwnerReference,
                        hasStructuralGeneratedOwnership: structural is not null,
                        structuralRole: structural?.StructuralRole ?? RoofStructuralRole.Undefined,
                        structuralOwnerReference: structural?.RoofOwnerReference,
                        hasAutomaticPurlinOwnership: purlin is not null,
                        automaticPurlinOwnerReference: purlin?.RoofOwnerReference,
                        expectedOwnerReference: ownerReference);
                    if (classification == RoofGeneratedOwnershipInvariantRules.CandidateClass.IgnoreNonTimber)
                    {
                        continue;
                    }

                    classifications.Add(classification);
                    if ((classification is
                            RoofGeneratedOwnershipInvariantRules.CandidateClass.InvalidOrdinaryOwnership or
                            RoofGeneratedOwnershipInvariantRules.CandidateClass.InvalidStructuralOwnership or
                            RoofGeneratedOwnershipInvariantRules.CandidateClass.OrphanPhysicalTimber) &&
                        orphanHandles.Count < 8)
                    {
                        orphanHandles.Add(member.Handle.ToString());
                    }
                }
            }

            var snapshot = RoofGeneratedOwnershipInvariantRules.Aggregate(classifications);
            editor.WriteMessage(
                "\nROOF_GENERATED_OWNERSHIP_INVARIANT" +
                " owner=" + ownerReference +
                " physicalTimberCandidates=" + snapshot.PhysicalTimberCandidates +
                " ordinaryGenerated=" + snapshot.OrdinaryGenerated +
                " structuralGenerated=" + snapshot.StructuralGenerated +
                " automaticPurlin=" + snapshot.AutomaticPurlin +
                " generatedByOwner=" + generatedByOwner +
                " structuralByOwner=" + structuralByOwner +
                " groupTimberCandidates=" + snapshot.PhysicalTimberCandidates +
                " invalidOrdinaryOwnership=" + snapshot.InvalidOrdinaryOwnership +
                " invalidStructuralOwnership=" + snapshot.InvalidStructuralOwnership +
                " orphanHandles=" + (orphanHandles.Count == 0 ? "-" : string.Join(",", orphanHandles)) +
                " result=" + (snapshot.IsOk ? "ok" : "failure"));
        }
        catch
        {
        }
    }
}
#endif
