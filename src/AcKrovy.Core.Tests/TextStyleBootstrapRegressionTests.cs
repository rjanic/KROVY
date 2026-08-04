using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TextStyleBootstrapRegressionTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Regression01_TechnicalIdentityIsStable() =>
        Assert.Equal("AK_KROVY_TECHNICAL",
            TimberAnnotationTextStylePresetRules.TechnicalStyleName);

    [Fact]
    public void Regression02_TechnicalMapsToIsocp() =>
        Assert.Equal("isocp.shx",
            TimberAnnotationTextStylePresetRules.TechnicalFontFile);

    [Fact]
    public void Regression03_TechnicalDoesNotMapToLegacyIsoFonts()
    {
        Assert.DoesNotContain(
            TimberAnnotationTextStylePresetRules.TechnicalFontFile,
            new[] { "isoct.shx", "iso.shx" },
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Regression04_HostForcesAuditedIsocpFile() =>
        Assert.Contains(
            "TimberAnnotationTextStylePresetRules.TechnicalFontFile",
            PresetService());

    [Fact]
    public void Regression05_HostForcesTechnicalXScaleOne() =>
        Assert.Contains(
            "TimberAnnotationTextStylePresetRules.DefaultWidthFactor",
            PresetService());

    [Fact]
    public void Regression06_HostForcesTechnicalObliqueZero() =>
        Assert.Contains(
            "TimberAnnotationTextStylePresetRules.DefaultObliqueAngleDegrees",
            PresetService());

    [Fact]
    public void Regression07_HostClearsTechnicalBigFont() =>
        Assert.Contains("bigFontFileName: string.Empty", PresetService());

    [Fact]
    public void Regression08_AppOwnedStylesRemainVariableHeight() =>
        Assert.Contains("record.TextSize = 0d;", PresetService());

    [Fact]
    public void Regression09_TechnicalDefinitionUsesAuditedXScale() =>
        Assert.Equal(1d, TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Technical).WidthFactor);

    [Fact]
    public void Regression10_EnsureRehydratesIsoctOrIsoWithoutDuplicateCreation()
    {
        var source = PresetService();
        Assert.Contains("if (textStyleTable.Has(normalizedName))", source);
        Assert.Contains("ApplyDefinition(", source);
        Assert.Contains("record.FileName = fontFile;", source);
    }

    [Fact]
    public void Regression11_BootstrapRunsBeforeFirstAnnotationCatalogRead()
    {
        var source = Presentation();
        Assert.True(
            source.IndexOf("EnsureRequiredStyles(", StringComparison.Ordinal) <
            source.IndexOf("AutoCadTextStyleResolver.ReadCatalog(", StringComparison.Ordinal));
    }

    [Fact]
    public void Regression12_LegacyNullUsesClassicProductDefault() =>
        Assert.Contains(
            "TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings()",
            Presentation());

    [Fact]
    public void Regression13_ExplicitFallbackPrefersArialBeforeStandard()
    {
        var source = Resolver();
        Assert.True(
            source.IndexOf("TimberAnnotationTextStylePresetRules.ArialStyleName",
                source.IndexOf("ResolveExplicit(", StringComparison.Ordinal),
                StringComparison.Ordinal) <
            source.IndexOf("TimberAnnotationTextSettingsRules.DefaultTextStyleName",
                source.IndexOf("ResolveExplicit(", StringComparison.Ordinal),
                StringComparison.Ordinal));
    }

    [Fact]
    public void Regression14_DebugHostCommandsArePresent()
    {
        var source = Source("src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadTextSettingsProofCommands.cs");
        Assert.Contains("AK_DEV_TEXT_STYLE_AUDIT", source);
        Assert.Contains("AK_DEV_TEXT_FRESH_DRAWING_CREATE", source);
        Assert.Contains("AK_DEV_TEXT_FRESH_DRAWING_VERIFY", source);
    }

    [Fact]
    public void Regression15_TextStyleComboBoxesUseDisplayName()
    {
        var xaml = Source("src", "AcKrovy.AutoCAD", "UI",
            "LayerSettingsWindow.xaml");
        Assert.True(Count(xaml, "DisplayMemberPath=\"DisplayName\"") >= 6);
    }

    [Fact]
    public void Regression16_ArchitecturalMapsToArialNarrowWithStableIdentity()
    {
        var definition = TimberAnnotationTextStylePresetRules.GetBuiltIn(
            TimberAnnotationBuiltInTextStylePreset.Architectural);

        Assert.Equal("architectural", definition.StableId);
        Assert.Equal("AK_KROVY_ARCHITECTURAL", definition.AutoCadTextStyleName);
        Assert.Equal("Arial Narrow", definition.FontFile);
        Assert.Equal(1d, definition.WidthFactor);
        Assert.Equal(0d, definition.ObliqueAngleDegrees);
    }

    [Fact]
    public void Regression17_ArchitecturalUsesGenericArialFallbackHydration()
    {
        var source = PresetService();
        var proof = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadTextSettingsProofService.cs");
        var retiredFont = "Architects" + " Daughter";

        Assert.DoesNotContain("EnsureArchitectural", source);
        Assert.DoesNotContain(retiredFont, source);
        Assert.DoesNotContain(retiredFont, proof);
        Assert.Contains("TimberAnnotationTextStylePresetRules.ArialFontFile", source);
        Assert.Contains("if (textStyleTable.Has(normalizedName))", source);
        Assert.Contains("record.BigFontFileName =", source);
        Assert.Contains("laterRehydrateToArialNarrow=true", proof);
        Assert.Equal(
            "isocp.shx",
            TimberAnnotationTextStylePresetRules.TechnicalFontFile);
    }

    private static string PresetService() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadTextStylePresetService.cs");

    private static string Presentation() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadAnnotationPresentationContext.cs");

    private static string Resolver() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadTextStyleResolver.cs");

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Replace("\r", "\n");

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
