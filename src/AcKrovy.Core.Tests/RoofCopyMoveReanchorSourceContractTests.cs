using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofCopyMoveReanchorSourceContractTests
{
    private static readonly string Lifecycle = Read("RoofAttachedManualLifecycleService.cs");
    private static readonly string ManualEdit = Read("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Reanchor = Read("RoofAttachedManualReanchorRules.cs");

    [Fact]
    public void RefreshMethod_IsMoveGated_AndCopyOriginGated()
    {
        var method = Segment(
            Lifecycle,
            "public static void RefreshModifiedAttachedManualRelatives",
            "private static bool TrySelectNearestCopyAnchor");
        Assert.Contains("IsMoveCommand(globalCommandName)", method);
        Assert.Contains("stored.Data.Origin == RoofAttachedManualOrigin.Copy", method);
    }

    [Fact]
    public void NearestSelector_FiltersKindAndFace_AndTieBreaksByStationIndex()
    {
        Assert.Contains("MemberKind != currentAnchorKey.MemberKind", Reanchor);
        Assert.Contains("RoofFace != currentAnchorKey.RoofFace", Reanchor);
        Assert.Contains("StationIndex < best.Key.StationIndex", Reanchor);
    }

    [Fact]
    public void NearestSelector_UsesLateralStationDirection_NotEndpointDistance()
    {
        Assert.Contains("(relative.V0Mm + relative.V1Mm) / 2d", Reanchor);
        Assert.Contains("Math.Abs(midV)", Reanchor);
    }

    [Fact]
    public void Reanchor_EmitsCompactDiagnostic()
    {
        Assert.Contains("ROOF_ATTACHED_MANUAL_REANCHOR", Lifecycle);
        Assert.Contains("oldAnchor", Lifecycle);
        Assert.Contains("newAnchor", Lifecycle);
    }

    [Fact]
    public void ReplayPath_UsesExactPersistedAnchor_NoNearestRemap()
    {
        var replay = Segment(
            Lifecycle,
            "public static RoofCopyReplayResult ReplayAnchoredChildrenForOwner",
            "private static void MakeCopyChildDormant");
        Assert.Contains("stored.Data.AnchorGeneratedMemberKey.Value", replay);
        Assert.Contains("TryFindGeneratedAnchorLine", replay);
        Assert.DoesNotContain("SelectNearestAnchor", replay);
    }

    [Fact]
    public void MoveCallSite_PassesGlobalCommandName()
    {
        var call = Segment(
            ManualEdit,
            "RoofAttachedManualLifecycleService.RefreshModifiedAttachedManualRelatives(",
            "RefreshModifiedAttachedManualNumberingAndAnnotations(");
        Assert.Contains("globalCommandName", call);
    }

    [Fact]
    public void NonMoveEdits_PreserveAnchor_OnlyMoveTriggersReanchor()
    {
        // Re-anchoring is driven solely by the MOVE classification; TRIM/EXTEND/ROTATE/GRIP
        // never reach the nearest-anchor selector because the flag is MOVE-only.
        Assert.Contains("var reanchor = RoofGeneratedMemberEditCommandRules.IsMoveCommand(globalCommandName);", Lifecycle);
        Assert.Contains("if (reanchor &&", Lifecycle);
    }

    private static string Read(string fileName)
    {
        var infra = Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);
        if (File.Exists(infra))
        {
            return File.ReadAllText(infra);
        }

        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AcKrovy.Core", "Services", "Roofs", fileName));
    }

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

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
