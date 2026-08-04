using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class AnnotationTextHeightValidationMessagesTests
{
    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode, 1.0d, 3.5d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 1.0d, 10.0d)]
    [InlineData(TimberAnnotationTextRole.Slope, 1.0d, 5.0d)]
    public void Ranges_MatchDomainConstants(
        TimberAnnotationTextRole role,
        double expectedMin,
        double expectedMax)
    {
        Assert.Equal(expectedMin, AnnotationTextHeightValidationMessages.GetMinimum(role));
        Assert.Equal(expectedMax, AnnotationTextHeightValidationMessages.GetMaximum(role));
        Assert.Equal(
            TimberAnnotationTextSettingsRules.GetMinimumPaperHeightMm(role),
            AnnotationTextHeightValidationMessages.GetMinimum(role));
        Assert.Equal(
            TimberAnnotationTextSettingsRules.GetMaximumPaperHeightMm(role),
            AnnotationTextHeightValidationMessages.GetMaximum(role));
    }

    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode, 1.0d)]
    [InlineData(TimberAnnotationTextRole.ItemCode, 3.5d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 1.0d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 10.0d)]
    [InlineData(TimberAnnotationTextRole.Slope, 1.0d)]
    [InlineData(TimberAnnotationTextRole.Slope, 5.0d)]
    public void Domain_AcceptsExactMinAndMax(
        TimberAnnotationTextRole role,
        double value) =>
        Assert.True(TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(role, value));

    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode, 0.999d)]
    [InlineData(TimberAnnotationTextRole.ItemCode, 3.7d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 0.999d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 10.1d)]
    [InlineData(TimberAnnotationTextRole.Slope, 0.999d)]
    [InlineData(TimberAnnotationTextRole.Slope, 5.1d)]
    [InlineData(TimberAnnotationTextRole.ItemCode, -1d)]
    public void Domain_RejectsOutOfRange(
        TimberAnnotationTextRole role,
        double value) =>
        Assert.False(TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(role, value));

    [Fact]
    public void SlovakCulture_UsesCommaInMessages()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        var allowed = AnnotationTextHeightValidationMessages.FormatAllowedRange(
            TimberAnnotationTextRole.ItemCode,
            culture);
        var inline = AnnotationTextHeightValidationMessages.FormatInlineError(
            TimberAnnotationTextRole.ItemCode,
            culture);
        var save = AnnotationTextHeightValidationMessages.FormatSaveError(
            TimberAnnotationTextRole.ItemCode,
            culture);

        Assert.Contains("1,0", allowed, StringComparison.Ordinal);
        Assert.Contains("3,5", allowed, StringComparison.Ordinal);
        Assert.Contains("1,0", inline, StringComparison.Ordinal);
        Assert.Contains("3,5", inline, StringComparison.Ordinal);
        Assert.Contains("1,0", save, StringComparison.Ordinal);
        Assert.Contains("3,5", save, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0", allowed, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishCulture_UsesDotInMessages()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var allowed = AnnotationTextHeightValidationMessages.FormatAllowedRange(
            TimberAnnotationTextRole.ItemCode,
            culture);
        var dimension = AnnotationTextHeightValidationMessages.FormatSaveError(
            TimberAnnotationTextRole.Dimension,
            culture);
        var slope = AnnotationTextHeightValidationMessages.FormatSaveError(
            TimberAnnotationTextRole.Slope,
            culture);

        Assert.Equal("Allowed range: 1.0–3.5 mm", allowed);
        Assert.Contains("1.0", dimension, StringComparison.Ordinal);
        Assert.Contains("10.0", dimension, StringComparison.Ordinal);
        Assert.Contains("1.0", slope, StringComparison.Ordinal);
        Assert.Contains("5.0", slope, StringComparison.Ordinal);
        Assert.Equal(
            "Main label height must be between 1.0 and 3.5 mm.",
            AnnotationTextHeightValidationMessages.FormatSaveError(
                TimberAnnotationTextRole.ItemCode,
                culture));
        Assert.Equal(
            "Dimension label height must be between 1.0 and 10.0 mm.",
            dimension);
        Assert.Equal(
            "Slope text height must be between 1.0 and 5.0 mm.",
            slope);
    }

    [Fact]
    public void Messages_DoNotHardcodeRangesOutsideDomainConstants()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "src",
                "AcKrovy.Localization",
                "AnnotationTextHeightValidationMessages.cs"));

        Assert.Contains("GetMinimumPaperHeightMm", source);
        Assert.Contains("GetMaximumPaperHeightMm", source);
        Assert.DoesNotContain("3.5d", source);
        Assert.DoesNotContain("10.0d", source);
        Assert.DoesNotContain("5.0d", source);
    }

    [Fact]
    public void AnnotationTextUi_UsesRoleSpecificSaveErrorsNotGenericInvalidHeight()
    {
        var code = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "src",
                "AcKrovy.AutoCAD",
                "UI",
                "LayerSettingsWindow.AnnotationText.cs"));

        Assert.Contains("AnnotationTextHeightValidationMessages.FormatSaveError", code);
        Assert.Contains("AnnotationTextHeightValidationMessages.FormatInlineError", code);
        Assert.Contains("AnnotationTextHeightValidationMessages.FormatAllowedRange", code);
        Assert.Contains("HasAnyInvalidAnnotationPaperHeight", code);
        var saveMethod = code[
            code.IndexOf("TryBuildPendingAnnotationTextSettings", StringComparison.Ordinal)..
            code.IndexOf("private string ResolvePendingStyleName", StringComparison.Ordinal)];
        Assert.DoesNotContain("InvalidHeight", saveMethod, StringComparison.Ordinal);
        Assert.Contains("FormatSaveError", saveMethod, StringComparison.Ordinal);
        Assert.Contains("FormatNumericRequired", saveMethod, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void AllCultures_ResolveNewKeysWithTwoPlaceholders(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        foreach (var role in Enum.GetValues<TimberAnnotationTextRole>())
        {
            var allowed = AnnotationTextHeightValidationMessages.FormatAllowedRange(role, culture);
            var inline = AnnotationTextHeightValidationMessages.FormatInlineError(role, culture);
            var save = AnnotationTextHeightValidationMessages.FormatSaveError(role, culture);
            Assert.DoesNotContain("SettingsWindow_AnnotationText_", allowed, StringComparison.Ordinal);
            Assert.DoesNotContain("SettingsWindow_AnnotationText_", inline, StringComparison.Ordinal);
            Assert.DoesNotContain("SettingsWindow_AnnotationText_", save, StringComparison.Ordinal);
            Assert.Contains(
                AnnotationTextHeightValidationMessages.FormatHeight(
                    AnnotationTextHeightValidationMessages.GetMinimum(role),
                    culture),
                allowed,
                StringComparison.Ordinal);
            Assert.Contains(
                AnnotationTextHeightValidationMessages.FormatHeight(
                    AnnotationTextHeightValidationMessages.GetMaximum(role),
                    culture),
                save,
                StringComparison.Ordinal);
        }

        Assert.False(string.IsNullOrWhiteSpace(
            AnnotationTextHeightValidationMessages.FormatNumericRequired(culture)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
