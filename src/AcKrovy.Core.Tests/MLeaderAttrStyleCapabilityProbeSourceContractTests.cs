using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MLeaderAttrStyleCapabilityProbeSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CapabilityProbe_IsDebugOnlyAndCoversApproachesAToH()
    {
        var commands = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AutoCadMLeaderAttrStyleCapabilityCommands.cs").Trim();
        var service = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadMLeaderAttrStyleCapabilityService.cs").Trim();

        Assert.StartsWith("#if DEBUG", commands, StringComparison.Ordinal);
        Assert.EndsWith("#endif", commands, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", service, StringComparison.Ordinal);
        Assert.EndsWith("#endif", service, StringComparison.Ordinal);

        Assert.Contains(
            "AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY",
            commands);
        Assert.Contains(
            "AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY",
            commands);
        Assert.Contains(
            "AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_CLEAN",
            commands);

        foreach (var approach in new[] { "A", "B", "C", "E", "F", "H" })
        {
            Assert.Contains($"case \"{approach}\":", service);
        }

        Assert.Contains("approach == \"D\"", service);
        Assert.Contains("approach == \"G\"", service);
        Assert.Contains("\"A\", \"B\", \"C\", \"D\", \"E\", \"F\", \"G\", \"H\"", service);

        Assert.Contains("sharedDefinitionCrosstalk", service);
        Assert.Contains("SAVE/CLOSE/REOPEN", service);
        Assert.Contains("GetSystemVariable(\"DBMOD\")", service);
        Assert.Contains("IsMTextAttribute", service);
        Assert.DoesNotContain(
            "AcKrovyItemLeaderBlockVariantService",
            service);
    }

    [Fact]
    public void CapabilityProbe_IsolatesVariantsAndSkipsMTextOnClassicAttrDef()
    {
        var service = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadMLeaderAttrStyleCapabilityService.cs");

        Assert.Contains("ExecuteApproachIsolated", service);
        Assert.Contains("NOT_APPLICABLE", service);
        Assert.Contains("PERSISTED", service);
        Assert.Contains("REVERTED_TO_DEFINITION", service);
        Assert.Contains("API_ERROR", service);
        Assert.Contains("PROVISIONAL VERDICT", service);
        Assert.Contains("capability matrix completed", service);
        Assert.Contains("IsMTextAttributeDefinition", service);
        Assert.Contains(
            "block attribute is a single-line AttributeDefinition",
            service);
        Assert.Contains("pre-SetBlockAttribute", service);
        Assert.Contains("ErrorStatus", service);
        Assert.Contains("continuing next variant", service);
        Assert.Contains("A–H SUMMARY", service);
        Assert.Contains(
            "set AttrDef style before MLeader create",
            service);
        Assert.Contains(
            "commit create, new transaction Get/modify/Set",
            service);

        // Setup diagnostics: STEP log + stack with file:line; A–H gated.
        Assert.Contains("STEP {stepId}", service);
        Assert.Contains("\"01\"", service);
        Assert.Contains("\"06.09\"", service);
        Assert.Contains("SetupDiagnosticsOnly", service);
        Assert.Contains("ReportSetupFailure", service);
        Assert.Contains("ProbeStepTracker", service);
        Assert.Contains("stack={exception}", service);
        Assert.Contains("CallerLineNumber", service);
        Assert.DoesNotContain("attribute.Justify =", service);
        Assert.DoesNotContain("attribute.AlignmentPoint", service);
        Assert.DoesNotContain("attribute.HorizontalMode", service);
        Assert.DoesNotContain("attribute.VerticalMode", service);
        Assert.DoesNotContain("attribute.AdjustAlignment", service);
        Assert.Contains("attribute.Position = Point3d.Origin", service);
        Assert.Contains("attribute.TextStyleId = baselineStyleId", service);

        // H must gate on true MText AttrDef; never force MText on classic.
        var hGateIndex = service.IndexOf(
            "approach == \"H\" && !isMTextAttributeDefinition",
            StringComparison.Ordinal);
        Assert.True(hGateIndex >= 0, "H must gate on IsMTextAttributeDefinition.");
        var hForceIndex = service.IndexOf(
            "attribute.IsMTextAttribute = true",
            StringComparison.Ordinal);
        Assert.True(hForceIndex > hGateIndex, "MText force must be after H gate.");
    }

    [Fact]
    public void ProductionSetter_DoesNotTemporarilyMutateAttributeDefinitionStyle()
    {
        var labels = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var setter = Member(
            labels,
            "private static void SetItemNumberBlockAttribute(");

        Assert.Contains("OpenMode.ForRead", setter);
        Assert.DoesNotContain("OpenMode.ForWrite", setter);
        Assert.DoesNotContain("attributeDefinition.TextStyleId =", setter);
        Assert.Contains("attribute.TextString = contents;", setter);
        Assert.Contains("attribute.Height = preparation.AttributeHeightMm;", setter);
        Assert.DoesNotContain("attribute.TextStyleId =", setter);
    }

    private static string Source(params string[] segments) =>
        Normalize(File.ReadAllText(Path.Combine([RepositoryRoot, .. segments])));

    private static string Member(string source, string declarationPrefix)
    {
        source = Normalize(source);
        declarationPrefix = Normalize(declarationPrefix);
        var start = source.IndexOf(declarationPrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Member not found: {declarationPrefix}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"Opening brace not found: {declarationPrefix}");
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }

        throw new InvalidOperationException(
            $"Closing brace not found: {declarationPrefix}");
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
