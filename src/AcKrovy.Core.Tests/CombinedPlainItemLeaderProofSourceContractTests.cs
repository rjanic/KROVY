using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class CombinedPlainItemLeaderProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CommandsPolicyAndService_AreEntirelyDebugOnly()
    {
        foreach (var source in new[] { Commands(), Policy(), Service() })
        {
            var trimmed = source.Trim();
            Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
            Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        }

        Assert.Contains("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_CREATE", Commands());
        Assert.Contains("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_VERIFY", Commands());
        Assert.Contains("AK_DEV_COMBINED_PLAIN_ITEM_TEXT_CLEAN", Commands());
    }

    [Fact]
    public void Create_UsesRealProductionCombinedAndStandalonePaths()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("TimberAnnotationService.EnsureForElement(", create);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", create);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsWithItemNumber",
            Service());
        Assert.Contains(
            "IsStandaloneRegressionCase",
            Service());
        Assert.Contains(
            "TimberAnnotationMode.ItemNumberLeader",
            Service());
        Assert.Contains("ItemNumberLeaderStyle.Plain", Service());
        Assert.DoesNotContain("new MLeader()", create);
        Assert.Contains("transaction.Commit();", create);
        Assert.Contains("rolled back", create);
        Assert.Contains("No completed proof manifest", create);
    }

    [Fact]
    public void FailurePreservation_JudgesOnlyImmediatePrePostPrepareParity()
    {
        var failure = Member(Service(), "private static string RunFailurePreservation(");

        Assert.Contains("E0", failure);
        Assert.Contains("E1", failure);
        Assert.Contains("E2", failure);
        Assert.Contains("E3", failure);
        Assert.Contains("AssertCompositeUnchanged(", failure);
        Assert.Contains("checkpoint: \"E1\"", failure);
        Assert.Contains("checkpoint: \"E2\"", failure);
        Assert.Contains(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            failure);
        Assert.Contains("presentationContext: null", failure);
        Assert.Contains("TimberAnnotationService.EnsureForElement(", failure);
        Assert.Contains("create-before-erase OK", failure);
        Assert.Contains("judged solely by E1→E2", failure);

        var tryPrepare = failure.IndexOf(
            "AutoCadPlainItemLeaderPresentationPolicy.TryPrepare(",
            StringComparison.Ordinal);
        var assertE2 = failure.IndexOf(
            "checkpoint: \"E2\"",
            StringComparison.Ordinal);
        var recoveryEnsure = failure.IndexOf(
            "TimberAnnotationService.EnsureForElement(",
            StringComparison.Ordinal);
        Assert.True(tryPrepare >= 0 && assertE2 >= 0 && recoveryEnsure >= 0);
        Assert.True(tryPrepare < assertE2 && assertE2 < recoveryEnsure);
    }

    [Fact]
    public void Verify_IsReadOnlyAndChecksBothCompositeComponents()
    {
        var verify = Member(Service(), "private static bool VerifyCore(");

        Assert.DoesNotContain("OpenMode.ForWrite", verify);
        Assert.DoesNotContain("UpgradeOpen", verify);
        Assert.DoesNotContain("Commit();", verify);
        Assert.Contains("FindCombinedPlainItemLeader(", verify);
        Assert.Contains("FindCombinedDimensionsMText(", verify);
        Assert.Contains("FindStandalonePlainItemLeader(", verify);
        Assert.Contains("NOT_TESTED", Service() + Policy());
        Assert.Contains("DBMOD", Service());
        Assert.Contains("DimensionsModelHeightMm", Policy());
    }

    [Fact]
    public void ProofCoversCombinedExplicitDenominatorRefreshFailureAndStandalone()
    {
        var policy = Policy();
        Assert.Contains("\"A\"", policy);
        Assert.Contains("\"B\"", policy);
        Assert.Contains("\"C\"", policy);
        Assert.Contains("\"E\"", policy);
        Assert.Contains("\"F\"", policy);
        Assert.Contains("DenominatorOverride: 50", policy);
        Assert.Contains("DenominatorOverride: 100", policy);
        Assert.Contains("ExpectRefreshSameObjectId: true", policy);
        Assert.Contains("IsFailurePreservationCase: true", policy);
        Assert.Contains("IsStandaloneRegressionCase: true", policy);
        Assert.Contains("3d,", policy);
    }

    private static string Commands() =>
        Source("src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadCombinedPlainItemLeaderProofCommands.cs");

    private static string Policy() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadCombinedPlainItemLeaderProofPolicy.cs");

    private static string Service() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadCombinedPlainItemLeaderProofService.cs");

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
    }
}
