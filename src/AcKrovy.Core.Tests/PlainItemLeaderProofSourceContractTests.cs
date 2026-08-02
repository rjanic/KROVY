using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class PlainItemLeaderProofSourceContractTests
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

        Assert.Contains("AK_DEV_PLAIN_ITEM_TEXT_CREATE", Commands());
        Assert.Contains("AK_DEV_PLAIN_ITEM_TEXT_VERIFY", Commands());
        Assert.Contains("AK_DEV_PLAIN_ITEM_TEXT_CLEAN", Commands());
    }

    [Fact]
    public void Create_UsesRealProductionPlainItemLeaderPath()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("TimberAnnotationService.EnsureForElement(", create);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", create);
        Assert.Contains("AnnotationMode = TimberAnnotationMode.ItemNumberLeader", Service());
        Assert.Contains("ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain", Service());
        Assert.DoesNotContain("new MLeader()", create);
        Assert.Contains("transaction.Commit();", create);
        Assert.Contains("StartOpenCloseTransaction()", create);
        Assert.Contains("VerifyCore(", create);
        Assert.Contains("rolled back", create);
        Assert.Contains("No completed proof manifest", create);
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
    public void ProofCoversLegacyExplicitDenominatorRefreshAndFailure()
    {
        var policy = Policy();
        Assert.Contains("\"A\"", policy);
        Assert.Contains("\"B\"", policy);
        Assert.Contains("\"C\"", policy);
        Assert.Contains("\"E\"", policy);
        Assert.Contains("DenominatorOverride: 50", policy);
        Assert.Contains("DenominatorOverride: 100", policy);
        Assert.Contains("ExpectRefreshSameObjectId: true", policy);
        Assert.Contains("IsFailurePreservationCase: true", policy);
        Assert.Contains("TextSettings: null", policy);
        Assert.Contains("3d,", policy);
    }

    private static string Commands() =>
        Source("src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadPlainItemLeaderProofCommands.cs");

    private static string Policy() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadPlainItemLeaderProofPolicy.cs");

    private static string Service() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadPlainItemLeaderProofService.cs");

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
