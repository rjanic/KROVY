using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class DimensionsLeaderProofSourceContractTests
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

        Assert.Contains("AK_DEV_DIMENSIONS_LEADER_TEXT_CREATE", Commands());
        Assert.Contains("AK_DEV_DIMENSIONS_LEADER_TEXT_VERIFY", Commands());
        Assert.Contains("AK_DEV_DIMENSIONS_LEADER_TEXT_CLEAN", Commands());
        Assert.DoesNotContain("AK_DEV_DIMENSIONS_LEADER_TEXT_INSPECT", Commands());
        Assert.DoesNotContain("public static void Inspect(", Service());
    }

    [Fact]
    public void Create_UsesRealProductionDimensionsLeaderPath()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("TimberAnnotationService.EnsureForElement(", create);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", create);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsLeader",
            Service());
        Assert.DoesNotContain("new MLeader()", create);
        Assert.Contains("transaction.Commit();", create);
        Assert.Contains("StartOpenCloseTransaction()", create);
        Assert.Contains("VerifyCore(", create);
        Assert.Contains("rolled back", create);
        Assert.Contains("No completed proof manifest", create);
        Assert.Contains("LabelAndDimensionModelHeight", create);
        Assert.Contains("index * 2500d", Service());
    }

    [Fact]
    public void Verify_IsReadOnlyAndReportsStyleParity()
    {
        var verify = Member(Service(), "public static void Verify(");
        var core = Member(Service(), "private static bool VerifyCore(");

        Assert.Contains("StartOpenCloseTransaction()", verify);
        Assert.DoesNotContain("OpenMode.ForWrite", verify + core);
        Assert.DoesNotContain("UpgradeOpen", verify + core);
        Assert.DoesNotContain("Commit();", verify + core);
        Assert.Contains("leader.TextStyleId", Service());
        Assert.Contains("content.TextStyleId", Service());
        Assert.Contains("NOT_TESTED", Service() + Policy());
        Assert.Contains("DBMOD", verify);
        Assert.Contains("RefreshToken", Policy());
    }

    [Fact]
    public void FailurePreservation_JudgesOnlyImmediatePrePostPrepareParity()
    {
        var failure = Member(Service(), "private static string RunFailurePreservation(");

        Assert.Contains("checkpoint: \"E1\"", failure);
        Assert.Contains("checkpoint: \"E2\"", failure);
        Assert.Contains(
            "AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(",
            failure);
        Assert.Contains("presentationContext: null", failure);
        Assert.Contains("E2 failure-preservation PASS", failure);
        Assert.Contains("TimberAnnotationService.EnsureForElement(", failure);

        var tryPrepare = failure.IndexOf(
            "AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(",
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
    public void ProofCoversLegacyExplicitDenominatorRefreshFailureAndStandaloneF()
    {
        var policy = Policy();
        var service = Service();

        Assert.Contains("\"A\"", policy);
        Assert.Contains("\"B\"", policy);
        Assert.Contains("\"C\"", policy);
        Assert.Contains("\"E\"", policy);
        Assert.Contains("\"F\"", policy);
        Assert.DoesNotContain("\"G\"", policy);
        Assert.DoesNotContain("\"G-L\"", policy);
        Assert.DoesNotContain("CombinedPlainItemRegression", policy);
        Assert.DoesNotContain("ReverseSourceOrientation", policy);
        Assert.DoesNotContain("CombinedLayoutDistanceMm", policy);
        Assert.DoesNotContain("CombinedDimensionsModelHeightMm", policy);

        Assert.Contains("DenominatorOverride: 50", policy);
        Assert.Contains("DenominatorOverride: 100", policy);
        Assert.Contains("ExpectRefreshSameObjectId: true", policy);
        Assert.Contains("IsFailurePreservationCase: true", policy);
        Assert.Contains("TextSettings: null", policy);
        Assert.Contains("3d,", policy);
        Assert.Contains("StandalonePlainItemRegression", policy);
        Assert.Equal(
            "A",
            ExtractPolicyConstant(policy, "RefreshToken"));

        Assert.DoesNotContain("ValidateCombinedPlainItemInventory(", service);
        Assert.DoesNotContain("virtualLanding", service);
        Assert.DoesNotContain("DumpCombinedPlainVirtualLanding(", service);
        Assert.DoesNotContain("FindCombinedPlainItemLeader(", service);
        Assert.DoesNotContain("DimensionsWithItemNumber", service);
    }

    [Fact]
    public void StandaloneCaseF_ExercisesPlainItemNumberLeaderOnly()
    {
        var service = Service();
        Assert.Contains("FindStandalonePlainItemLeader(", service);
        Assert.Contains("ItemNumberLeader", service);
        Assert.Contains("ValidateStandalonePlainItem(", service);
        Assert.Contains("ItemNumberModelHeight", service);
        Assert.DoesNotContain("DoglegLength", service);
        Assert.DoesNotContain("TimberCombinedDimensionTypographyRules", service);
    }

    [Theory]
    [InlineData(50, 135d)]
    public void StandalonePlainItemHeight_MatchesLegacyAtScale50(
        int denominator,
        double expectedModelHeightMm)
    {
        var paperHeight =
            TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm;
        Assert.Equal(
            expectedModelHeightMm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeight,
                denominator));
    }

    private static string ExtractPolicyConstant(string policy, string name)
    {
        var marker = $"public const string {name} = \"";
        var start = policy.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing constant {name}");
        start += marker.Length;
        var end = policy.IndexOf('"', start);
        Assert.True(end > start);
        return policy[start..end];
    }

    private static string Commands() =>
        Source("src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadDimensionsLeaderProofCommands.cs");

    private static string Policy() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadDimensionsLeaderProofPolicy.cs");

    private static string Service() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadDimensionsLeaderProofService.cs");

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
