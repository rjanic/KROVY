using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofManualIdentitySyncSourceContractTests
{
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string RafterSet = Read("RoofGeneratedRafterSetService.cs");
    private static readonly string Diag = Read("RoofGeneratedMemberManualEditDiag.cs");

    [Fact]
    public void ReservationSync_RunsAfterRecalc_InSharedAcceptedEditPath()
    {
        // Recalc (reconcile ElementId) must run BEFORE the reservation sync, and both live
        // in the shared ProcessOwners accepted-edit path (BREAK, split-TRIM, TRIM, EXTEND,
        // endpoint GRIP_STRETCH) — not a BREAK-only branch.
        var recalc = ManualEdit.IndexOf("TryRecalculateAcceptedMembers(", StringComparison.Ordinal);
        var sync = ManualEdit.IndexOf("SyncReservedElementIdsAfterRecalc(", StringComparison.Ordinal);
        Assert.True(recalc >= 0, "recalc call not found");
        Assert.True(sync > recalc, "reservation sync must run AFTER recalc");
    }

    [Fact]
    public void ReservationSync_UpdatesReservedElementId_ToFinalReconciledId()
    {
        // The sync upserts the override with ReservedElementId = the FINAL ElementId read
        // from the reconciled timber metadata, so the rebuild cannot replay a stale
        // pre-recalc number.
        Assert.Contains("current with { ReservedElementId = finalElementId }", ManualEdit);
        Assert.Contains("SyncReservedElementIdsAfterRecalc", ManualEdit);
        Assert.Contains("finalElementId = finalData.ElementId", ManualEdit);
    }

    [Fact]
    public void ReservationSync_GatedToActualNumberingChange()
    {
        // Only renumbering edits (old signature != new signature) touch the reservation;
        // geometry-only edits keep their existing number.
        Assert.Contains("RequiresNumberingSynchronization", ManualEdit);
    }

    [Fact]
    public void ReservationSync_SkipsWhenAlreadyConsistent()
    {
        // No-op when the reservation already equals the final ElementId.
        Assert.Contains("current.ReservedElementId", ManualEdit);
        Assert.Contains("finalElementId,", ManualEdit);
    }

    [Fact]
    public void Rebuild_ForcesElementIdFromReserved_SoSyncPreventsCollision()
    {
        // The rebuild assigns the override's ReservedElementId to the recreated member.
        // A synced reservation therefore keeps distinct signatures in distinct item groups.
        Assert.Contains("memberData with { ElementId = overrideData.ReservedElementId }", RafterSet);
    }

    [Fact]
    public void IdentitySync_Diagnostic_EmitsOldAndFinalReservation()
    {
        Assert.Contains("WriteIdentitySync", ManualEdit);
        Assert.Contains("ROOF_MANUAL_IDENTITY_SYNC", Diag);
        Assert.Contains("oldReserved=", Diag);
        Assert.Contains("finalElementId=", Diag);
        Assert.Contains("newReserved=", Diag);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            fileName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
