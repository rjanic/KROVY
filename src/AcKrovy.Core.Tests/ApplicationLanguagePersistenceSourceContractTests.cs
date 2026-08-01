using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ApplicationLanguagePersistenceSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ApplicationLanguageLoad_DoesNotCallSave()
    {
        var store = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Settings",
            "AppLanguageSettingsStore.cs");
        var load = Segment(
            store,
            "public static AppLanguageSettings Load()",
            "public static void Save");

        Assert.Contains("AcKrovyDiagnostics.Settings.Load(", load);
        Assert.Equal(
            1,
            CountOccurrences(load, "AcKrovyDiagnostics.Settings.Load("));
        Assert.DoesNotContain("AcKrovyDiagnostics.Settings.Save(", load);
    }

    [Fact]
    public void ApplicationLanguageLoad_UsesWindowsUiLanguageOnlyForMissingSettings()
    {
        var store = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Settings",
            "AppLanguageSettingsStore.cs");
        var load = Segment(
            store,
            "public static AppLanguageSettings Load()",
            "public static void Save");

        Assert.Contains(
            "result.Status.State == SettingsFileState.Missing",
            load);
        Assert.Contains("CultureInfo.InstalledUICulture", load);
        Assert.Contains(
            "AppLanguageService.ResolveFirstRunLanguageCode(",
            load);
        Assert.Contains(": result.Value", load);
        Assert.DoesNotContain("SettingsFileState.CorruptBackupCreated", load);
        Assert.DoesNotContain("SettingsFileState.CorruptBackupFailed", load);
    }

    [Fact]
    public void PluginInitialization_LoadsAndAppliesWithoutPersisting()
    {
        var plugin = Source(
            "src",
            "AcKrovy.AutoCAD",
            "PluginEntry.cs");
        var initialize = Segment(
            plugin,
            "public void Initialize()",
            "public void Terminate()");

        Assert.Contains("AppLanguageSettingsStore.Load()", initialize);
        Assert.Contains("AppLanguageService.Apply(languageSettings.LanguageCode)", initialize);
        Assert.DoesNotContain("AppLanguageSettingsStore.Save", initialize);
    }

    [Fact]
    public void GenericSettingsApply_DoesNotPersistOrRefreshLanguage()
    {
        var commands = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");
        var apply = Segment(
            commands,
            "private static SettingsApplyResponse ApplySettingsFromWindow(",
            "private static SettingsApplyResponse SettingsResponse(");

        Assert.DoesNotContain("AppLanguageSettingsStore.Save", apply);
        Assert.DoesNotContain("AppLanguageService.Apply", apply);
        Assert.DoesNotContain("RebuildLocalizedUi", apply);
        Assert.DoesNotContain("RefreshLocalizedContent", apply);
    }

    [Fact]
    public void LanguageSelectionHandler_IsDeclaredExactlyOnceAndBindingIsOneWay()
    {
        var xamlPath = Path.Combine(
            RepositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var selector = document
            .Descendants(presentation + "ListBox")
            .Single(element =>
                (string?)element.Attribute(x + "Name") == "LanguageSelector");
        var code = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");

        Assert.Equal(
            "LanguageSelector_SelectionChanged",
            (string?)selector.Attribute("SelectionChanged"));
        Assert.Contains(
            "Mode=OneWay",
            (string?)selector.Attribute("SelectedValue"));
        Assert.Equal(
            1,
            CountOccurrences(
                code,
                "private void LanguageSelector_SelectionChanged("));
        Assert.DoesNotContain("LanguageSelector.SelectionChanged +=", code);
    }

    [Fact]
    public void SelectedLanguagePropertySetter_HasNoPersistenceOrRuntimeSideEffects()
    {
        var code = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");
        var property = Segment(
            code,
            "public string SelectedLanguageCode",
            "public TimberAnnotationMode SelectedAnnotationMode");

        Assert.DoesNotContain("AppLanguageSettingsStore", property);
        Assert.DoesNotContain("AppLanguageService.Apply", property);
        Assert.DoesNotContain("TryApplyUserSelection", property);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        source = NormalizeLineEndings(source);
        start = NormalizeLineEndings(start);
        end = NormalizeLineEndings(end);

        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

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
