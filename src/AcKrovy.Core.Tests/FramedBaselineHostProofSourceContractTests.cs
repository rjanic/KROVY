using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedBaselineHostProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Commands_AreDebugOnlyWithCreateVerifyClean()
    {
        var commands = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AutoCadFramedBaselineProofCommands.cs")).Trim();

        Assert.StartsWith("#if DEBUG", commands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", commands, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_FRAMED_BASELINE_CREATE", commands);
        Assert.Contains("AK_DEV_FRAMED_BASELINE_VERIFY", commands);
        Assert.Contains("AK_DEV_FRAMED_BASELINE_CLEAN", commands);
    }

    [Fact]
    public void Service_UsesResolveSharedDefinitionAndPerInstanceAttribute()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadFramedBaselineProofService.cs"));

        Assert.Contains("EnsureResolved(", service);
        Assert.Contains("TimberItemLeaderBlockDefinitionRules.Resolve(", service);
        Assert.Contains("attribute.TextStyleId = style.TextStyleId", service);
        Assert.Contains("attribute.Height = attributeHeight", service);
        Assert.Contains("transaction.Abort()", service);
        Assert.Contains("DBMOD", service);
        Assert.DoesNotContain("EvaluateMeasuredTextWidth(", service);
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
