using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class DiagnosticsWindowSourceContractTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string XamlPath = Path.Combine(
        Root,
        "src",
        "AcKrovy.AutoCAD",
        "UI",
        "DiagnosticsWindow.xaml");
    private static readonly string CodePath = Path.Combine(
        Root,
        "src",
        "AcKrovy.AutoCAD",
        "UI",
        "DiagnosticsWindow.xaml.cs");

    [Fact]
    public void DiagnosticsWindow_DoesNotShareSettingsGeometryPersistence()
    {
        var code = File.ReadAllText(CodePath);
        var command = File.ReadAllText(Path.Combine(
            Root,
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var showDiagnostics = Segment(
            command,
            "private static void ShowDiagnostics()",
            "private static string LocalizeSettingsState");

        Assert.DoesNotContain("SettingsUiPreferencesStore.Save", code);
        Assert.DoesNotContain("settings-ui.json", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preferences.Width", showDiagnostics);
        Assert.DoesNotContain("preferences.Height", showDiagnostics);
        Assert.DoesNotContain("preferences.Left", showDiagnostics);
        Assert.DoesNotContain("preferences.Top", showDiagnostics);
        Assert.Contains("preferences.Theme", showDiagnostics);
    }

    [Fact]
    public void DiagnosticsWindow_HasScrollableMiddleAndFooterOutsideIt()
    {
        var document = XDocument.Load(XamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var scrollViewer = Assert.Single(
            document.Descendants(presentation + "ScrollViewer"),
            element =>
                (string?)element.Attribute(x + "Name") ==
                "DiagnosticsContentScrollViewer");
        var footer = Assert.Single(
            document.Descendants(presentation + "WrapPanel"),
            element =>
                (string?)element.Attribute(x + "Name") ==
                "DiagnosticsFooterActions");

        Assert.Equal("Auto", (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"));
        Assert.DoesNotContain(footer, scrollViewer.DescendantsAndSelf());
        Assert.Equal("2", (string?)footer.Attribute(presentation + "Grid.Row") ??
            (string?)footer.Attribute("Grid.Row"));
    }

    [Fact]
    public void DiagnosticsWindow_UsesNaturalBoundedSize()
    {
        var document = XDocument.Load(XamlPath);
        var window = document.Root!;

        Assert.Equal("Height", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Height"));
        Assert.NotNull(window.Attribute("MinWidth"));
        Assert.NotNull(window.Attribute("MinHeight"));
        Assert.NotNull(window.Attribute("MaxWidth"));
        Assert.NotNull(window.Attribute("MaxHeight"));
        Assert.Equal("CenterOwner", (string?)window.Attribute("WindowStartupLocation"));
    }

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
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "AcKrovy.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
