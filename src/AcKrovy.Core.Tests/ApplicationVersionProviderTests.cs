using System.Globalization;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ApplicationVersionProviderTests
{
    [Fact]
    public void DisplayVersion_ComesFromAssemblyMetadata_InCleanForm()
    {
        var displayVersion = ApplicationVersionProvider.DisplayVersion;

        Assert.Matches(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", displayVersion);
        Assert.DoesNotContain('+', displayVersion);
    }

    [Theory]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("1.2.3-beta.2+abc123", "1.2.3-beta.2")]
    [InlineData(" 1.2.3 ", "1.2.3")]
    public void NormalizeDisplayVersion_RemovesBuildMetadata(
        string informationalVersion,
        string expected)
    {
        Assert.Equal(
            expected,
            ApplicationVersionProvider.NormalizeDisplayVersion(informationalVersion, new Version(9, 9, 9, 9)));
    }

    [Fact]
    public void NormalizeDisplayVersion_FallsBackToThreePartAssemblyVersion()
    {
        Assert.Equal(
            "2.4.6",
            ApplicationVersionProvider.NormalizeDisplayVersion(null, new Version(2, 4, 6, 8)));
    }

    [Fact]
    public void NormalizeDisplayVersion_FallsBack_WhenInformationalVersionContainsOnlyMetadata()
    {
        Assert.Equal(
            "2.4.6",
            ApplicationVersionProvider.NormalizeDisplayVersion("+commit", new Version(2, 4, 6, 8)));
    }

    [Fact]
    public void NormalizeDisplayVersion_HasStableFallback_WhenMetadataIsUnavailable()
    {
        Assert.Equal("0.0.0", ApplicationVersionProvider.NormalizeDisplayVersion(null, null));
    }

    [Theory]
    [InlineData("sk")]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("pl")]
    [InlineData("fr")]
    public void StartupAndHelp_UseCentralVersion_InEverySupportedLanguage(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode);
        var version = ApplicationVersionProvider.DisplayVersion;

        var startup = UiStrings.Format(UiStrings.GetString("Message_PluginLoaded", culture), version);
        var help = UiStrings.Format(UiStrings.GetString("Help_CommandOverview", culture), version);

        Assert.Contains(version, startup);
        Assert.Contains(version, help);
        Assert.DoesNotContain("0.10.0", startup);
        Assert.DoesNotContain("0.10.0", help);
    }
}
