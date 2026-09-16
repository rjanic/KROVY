using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedOwnershipInvariantRulesTests
{
    private const string Owner = "291B";

    [Theory]
    [InlineData(61, 10, 71)]
    [InlineData(69, 10, 79)]
    [InlineData(67, 10, 77)]
    public void OrdinaryPlusStructural_AggregatesToOkPhysicalCount(
        int ordinary,
        int structural,
        int expectedPhysical)
    {
        var classes = Enumerable
            .Repeat(RoofGeneratedOwnershipInvariantRules.CandidateClass.OrdinaryGenerated, ordinary)
            .Concat(Enumerable.Repeat(
                RoofGeneratedOwnershipInvariantRules.CandidateClass.StructuralGenerated,
                structural));
        var snapshot = RoofGeneratedOwnershipInvariantRules.Aggregate(classes);

        Assert.Equal(expectedPhysical, snapshot.PhysicalTimberCandidates);
        Assert.Equal(ordinary, snapshot.OrdinaryGenerated);
        Assert.Equal(structural, snapshot.StructuralGenerated);
        Assert.Equal(0, snapshot.InvalidOrdinaryOwnership);
        Assert.Equal(0, snapshot.InvalidStructuralOwnership);
        Assert.Equal(0, snapshot.OrphanPhysicalTimber);
        Assert.True(snapshot.IsOk);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void ValidStructuralHipValley_IsNotMissingOrdinaryMetadata(RoofStructuralRole role)
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: true,
            structuralRole: role,
            structuralOwnerReference: Owner,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.StructuralGenerated,
            classification);
    }

    [Fact]
    public void OrdinaryMissingOwnershipMetadata_FailsAsOrphan()
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: false,
            structuralRole: RoofStructuralRole.Undefined,
            structuralOwnerReference: null,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.OrphanPhysicalTimber,
            classification);
        var snapshot = RoofGeneratedOwnershipInvariantRules.Aggregate([classification]);
        Assert.False(snapshot.IsOk);
        Assert.Equal(1, snapshot.OrphanPhysicalTimber);
    }

    [Fact]
    public void StructuralMissingOwnerMetadata_FailsAsInvalidStructural()
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: true,
            structuralRole: RoofStructuralRole.Hip,
            structuralOwnerReference: null,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.InvalidStructuralOwnership,
            classification);
        Assert.False(RoofGeneratedOwnershipInvariantRules.Aggregate([classification]).IsOk);
    }

    [Fact]
    public void WrongOwnerStructuralMember_Fails()
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: true,
            structuralRole: RoofStructuralRole.Valley,
            structuralOwnerReference: "OTHER",
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.InvalidStructuralOwnership,
            classification);
    }

    [Fact]
    public void WrongOwnerOrdinaryMember_Fails()
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: true,
            ordinaryOwnerReference: "OTHER",
            hasStructuralGeneratedOwnership: false,
            structuralRole: RoofStructuralRole.Undefined,
            structuralOwnerReference: null,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.InvalidOrdinaryOwnership,
            classification);
    }

    [Fact]
    public void GroupMembershipAlone_DoesNotValidateOrphanTimber()
    {
        // Being counted as a physical candidate still requires an ownership class.
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: false,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: false,
            structuralRole: RoofStructuralRole.Undefined,
            structuralOwnerReference: null,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.OrphanPhysicalTimber,
            classification);
        Assert.False(RoofGeneratedOwnershipInvariantRules.Aggregate([classification]).IsOk);
    }

    [Fact]
    public void AttachedManual_IsExcludedFromPhysicalGeneratedCandidates()
    {
        var classification = RoofGeneratedOwnershipInvariantRules.Classify(
            hasGenericTimberMetadata: true,
            hasAttachedManualOwnership: true,
            hasOrdinaryGeneratedOwnership: false,
            ordinaryOwnerReference: null,
            hasStructuralGeneratedOwnership: false,
            structuralRole: RoofStructuralRole.Undefined,
            structuralOwnerReference: null,
            hasAutomaticPurlinOwnership: false,
            automaticPurlinOwnerReference: null,
            expectedOwnerReference: Owner);

        Assert.Equal(
            RoofGeneratedOwnershipInvariantRules.CandidateClass.AttachedManual,
            classification);
        var snapshot = RoofGeneratedOwnershipInvariantRules.Aggregate([classification]);
        Assert.Equal(0, snapshot.PhysicalTimberCandidates);
        Assert.True(snapshot.IsOk);
    }

    [Fact]
    public void HostDiagSourceContract_ReportsSeparateOwnershipClasses()
    {
        var redoDiag = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRedoStateDiag.cs");
        var invariant = Segment(
            redoDiag,
            "public static void CaptureOwnershipInvariant",
            "editor.WriteMessage");

        Assert.Contains("RoofGeneratedOwnershipInvariantRules.Classify", invariant);
        Assert.Contains("RoofStructuralGeneratedStore.Read", invariant);
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.Read", invariant);
        Assert.Contains("ordinaryGenerated=", redoDiag);
        Assert.Contains("structuralGenerated=", redoDiag);
        Assert.Contains("invalidOrdinaryOwnership=", redoDiag);
        Assert.Contains("invalidStructuralOwnership=", redoDiag);
        Assert.DoesNotContain("missingGeneratedMetadata=", redoDiag);
    }

    private static string Segment(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0, "Missing start: " + start);
        Assert.True(last > first, "Missing end: " + end);
        return source[first..last];
    }

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine([root, .. path]));
    }
}
