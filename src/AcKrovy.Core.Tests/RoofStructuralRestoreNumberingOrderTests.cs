using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralRestoreNumberingOrderTests
{
    private static string Read(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs"));
    }

    [Theory]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    public void AcceptedOrdinaryEdit_RestoresStructuralBeforeAnyRecalculation(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand(command));
        var code = Read("RoofGeneratedMemberManualEditService");
        var start = code.IndexOf("private static OwnerEditOutcome ProcessOwner", StringComparison.Ordinal);
        var end = code.IndexOf("private static bool TryAcceptLockedRigidTranslation", start, StringComparison.Ordinal);
        var method = code[start..end];
        var restore = method.IndexOf("TryRestoreStructuralHipValleyMembersOnly", StringComparison.Ordinal);
        var accept = method.IndexOf("var accept = TryAcceptUnlockedEdits", StringComparison.Ordinal);
        Assert.True(restore >= 0 && restore < accept,
            "Structural snapshot validation must precede the accepted edit's numbering refresh.");
        Assert.DoesNotContain("TryRestoreStructuralHipValleyMembersOnly", method[accept..]);
        Assert.Contains("if (!RoofUnsupportedStretchRecoveryService.TryRestoreStructuralHipValleyMembersOnly", method[..accept]);
        Assert.Contains("throw new OrdinaryPhysicalReconcileException(\"Structural Hip/Valley snapshot restoration failed.\")", method[..accept]);
        Assert.Contains("TryRecalculateAcceptedMembers", code);
        Assert.Contains("SyncReservedElementIdsAfterRecalc", code);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", method[accept..]);
        Assert.Contains("transaction.Commit()", method[accept..]);
    }

    [Theory]
    [InlineData(TimberElementType.HipRafter, RoofStructuralRole.Hip)]
    [InlineData(TimberElementType.ValleyRafter, RoofStructuralRole.Valley)]
    public void MatchingStructuralSignatures_ShareItemNumber_WithoutChangingSemanticKeysOrGeometry(
        TimberElementType type, RoofStructuralRole role)
    {
        var prefix = type == TimberElementType.HipRafter ? "NK" : "UK";
        var structural = Enumerable.Range(1, 4).Select(i => new RoofStructuralGeneratedData(1, "AB", role, i, i + 4)).ToArray();
        var axes = Enumerable.Range(1, 4).Select(i => new RoofSegment3D(new(i * 100, 0, 0), new(i * 100, 3000, 0))).ToArray();
        var candidates = Enumerable.Range(1, 4).Select(i => new TimberElementItemNumberingCandidate(
            new TimberElementMeasurement(new TimberElementData { ElementId = prefix + i, ElementType = type,
                WidthMm = 100, HeightMm = 160 }, 3000, 3000, 3000, 0.048), false)).ToArray();
        var assignments = TimberElementItemNumbering.AssignElementIds(candidates);
        Assert.Equal(Enumerable.Repeat(prefix + "1", 4), assignments.Select(a => a.ElementId));
        Assert.NotEqual(candidates[1].Measurement.Data.ElementId, assignments[1].ElementId);
        var after = structural.Zip(axes).Zip(assignments, (member, assignment) =>
            (member.First.LogicalKey, member.First.RoofOwnerReference, member.Second, assignment.ElementId)).ToArray();
        Assert.Equal(structural.Select(s => s.LogicalKey), after.Select(s => s.LogicalKey));
        Assert.Equal(4, after.Select(s => s.LogicalKey).Distinct().Count());
        Assert.All(after, member => Assert.Equal("AB", member.RoofOwnerReference));
        Assert.Equal(axes, after.Select(s => s.Second));
    }

    [Fact]
    public void StructuralRestore_StillProtectsNativeGeometryErasureAndMetadata()
    {
        var code = Read("RoofUnsupportedStretchRecoveryService");
        Assert.Contains("!string.Equals(data.ElementId, timber.ElementId, StringComparison.Ordinal)", code);
        Assert.Contains("line.StartPoint = ToAcad(timber.Start)", code);
        Assert.Contains("line.EndPoint = ToAcad(timber.End)", code);
        Assert.Contains("line.Erase(false)", code);
        Assert.Contains("TryEraseUnsnapshotStructuralDuplicates", code);
        Assert.Contains("RoofStructuralGeneratedLockRules.IsLockProtectedRole", code);
    }
}
