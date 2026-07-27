using System.Threading;
using System.Windows;
using System.Windows.Media;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class ProductivityWindowsSmokeTests
{
    [Fact]
    public void ProductivityWindows_LoadCompiledBamlInLightAndDarkThemes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppLanguageService.Apply("en");
                var seed = new TimberElementSnapshot(
                    new TimberElementData
                    {
                        SchemaVersion = TimberElementDataSchema.CurrentVersion,
                        ElementId = "K1",
                        ElementType = TimberElementType.Rafter,
                        WidthMm = 80,
                        HeightMm = 160,
                        Material = "Smrek C24",
                    },
                    5000);

                foreach (var theme in new[] { SettingsTheme.Light, SettingsTheme.Dark })
                {
                    var select = new SelectSimilarWindow(seed, theme)
                    {
                        Left = -30000,
                        Top = -30000,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                    };
                    select.Show();
                    select.UpdateLayout();
                    Assert.True(select.ElementTypeCheckBox.IsChecked);
                    Assert.True(select.CrossSectionCheckBox.IsChecked);
                    Assert.True(select.MaterialCheckBox.IsChecked);
                    Assert.False(select.ElementIdCheckBox.IsChecked);
                    Assert.False(select.CuttingLengthCheckBox.IsChecked);
                    Assert.NotNull(select.Background);
                    Assert.NotEqual(
                        "SelectSimilarWindow_Title",
                        select.Title);
                    select.Close();

                    var export = new CsvExportWindow(3, theme)
                    {
                        Left = -30000,
                        Top = -30000,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                    };
                    export.Show();
                    export.UpdateLayout();
                    Assert.True(export.PickFirstRadio.IsEnabled);
                    Assert.True(export.PickFirstRadio.IsChecked);
                    Assert.Contains("3", export.PickFirstRadio.Content?.ToString());
                    Assert.NotNull(export.Background);
                    export.Close();

                    var diagnostics = new DiagnosticsWindow(
                        [new DiagnosticsInfoRow("Version", "0.19.0")],
                        [new DiagnosticsInfoRow("settings.json", "Loaded")],
                        ["12:00 [Information] Test"],
                        "summary",
                        System.IO.Path.GetTempPath(),
                        theme)
                    {
                        Left = -30000,
                        Top = -30000,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                    };
                    diagnostics.Show();
                    diagnostics.UpdateLayout();
                    Assert.NotNull(diagnostics.Background);
                    Assert.NotEqual("DiagnosticsWindow_Title", diagnostics.Title);
                    diagnostics.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF productivity smoke test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void DiagnosticsWindow_LoadsAllLanguagesWithScrollableContentAndFixedFooter()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var languageCode in new[] { "sk", "cs", "en", "de", "pl", "fr" })
                {
                    AppLanguageService.Apply(languageCode);
                    var diagnostics = new DiagnosticsWindow(
                        Enumerable.Range(1, 8)
                            .Select(index => new DiagnosticsInfoRow(
                                $"Label {index}",
                                $"Value {index}"))
                            .ToArray(),
                        Enumerable.Range(1, 5)
                            .Select(index => new DiagnosticsInfoRow(
                                $"settings-{index}.json",
                                $"State {index}"))
                            .ToArray(),
                        Enumerable.Range(1, 12)
                            .Select(index => $"{index:00}:00 [Information] Event {index}")
                            .ToArray(),
                        "summary",
                        System.IO.Path.GetTempPath(),
                        SettingsTheme.Light)
                    {
                        Left = -30000,
                        Top = -30000,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                    };

                    diagnostics.Show();
                    diagnostics.UpdateLayout();

                    Assert.NotEqual("DiagnosticsWindow_Title", diagnostics.Title);
                    Assert.Equal(SizeToContent.Height, diagnostics.SizeToContent);
                    Assert.Equal(
                        System.Windows.Controls.ScrollBarVisibility.Auto,
                        diagnostics.DiagnosticsContentScrollViewer.VerticalScrollBarVisibility);
                    Assert.False(IsDescendantOf(
                        diagnostics.DiagnosticsFooterActions,
                        diagnostics.DiagnosticsContentScrollViewer));
                    Assert.True(diagnostics.DiagnosticsFooterActions.ActualHeight > 0);
                    Assert.True(diagnostics.MaxWidth >= diagnostics.MinWidth);
                    Assert.True(diagnostics.MaxHeight >= diagnostics.MinHeight);
                    Assert.True(diagnostics.ActualHeight <= diagnostics.MaxHeight + 1);

                    diagnostics.SizeToContent = SizeToContent.Manual;
                    diagnostics.Height = diagnostics.MinHeight;
                    diagnostics.UpdateLayout();
                    Assert.True(
                        diagnostics.DiagnosticsContentScrollViewer.ScrollableHeight > 0);
                    Assert.True(diagnostics.DiagnosticsFooterActions.ActualHeight > 0);

                    diagnostics.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Diagnostics localization smoke test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void DiagnosticsSupportSummary_UsesExistingSanitizerAndLocalAppDataToken()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var realLogPath = System.IO.Path.Combine(
            localApplicationData,
            "ACAD_KROVY",
            "Logs");

        var summary = DiagnosticsSupportSummaryBuilder.Build(
            [new DiagnosticsInfoRow("Log folder", realLogPath)],
            [new DiagnosticsInfoRow("settings.json", "Loaded")],
            ["12:00 [Information] Test"],
            "Settings",
            "Events");

        Assert.Contains("%LOCALAPPDATA%", summary);
        Assert.DoesNotContain(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Environment.UserName,
            summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CommandStarted")]
    [InlineData("CommandCompleted")]
    public void RecentEventFormatter_KeepsInvariantCommandNameWithoutStackTrace(string eventName)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var command = DiagnosticsRecentEventFormatter.Format(
            new DiagnosticEvent(
                DateTimeOffset.Now,
                DiagnosticLevel.Information,
                eventName,
                "Invariant technical message.",
                CommandName: "AK_EDIT",
                StackTrace: "sensitive stack"),
            culture);

        Assert.Contains("AK_EDIT", command);
        Assert.DoesNotContain("sensitive stack", command);
    }

    [Theory]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void RecentEventFormatter_LocalizesKnownSettingsLoadAndSaveEvents(string cultureName)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        var subjects = new[]
        {
            (SettingsConfigurationSubject.ApplicationLanguage, "DiagnosticsEvent_SubjectApplicationLanguage"),
            (SettingsConfigurationSubject.SettingsUiPreferences, "DiagnosticsEvent_SubjectSettingsUiPreferences"),
            (SettingsConfigurationSubject.LayerProfile, "DiagnosticsEvent_SubjectLayerProfile"),
            (SettingsConfigurationSubject.TimberDefaults, "DiagnosticsEvent_SubjectTimberDefaults"),
            (SettingsConfigurationSubject.CustomElementDefinitions, "DiagnosticsEvent_SubjectCustomElementDefinitions"),
        };
        var actions = new[]
        {
            (SettingsConfigurationAction.Loaded, "DiagnosticsWindow_StateLoaded", "Settings file loaded."),
            (SettingsConfigurationAction.Saved, "DiagnosticsEvent_ActionSaved", "Settings file saved."),
        };

        foreach (var (subject, subjectKey) in subjects)
        {
            var expectedSubject = UiStrings.GetString(subjectKey, culture);
            Assert.NotEqual(subjectKey, expectedSubject);
            foreach (var (action, actionKey, technicalMessage) in actions)
            {
                var expectedAction = UiStrings.GetString(actionKey, culture);
                Assert.NotEqual(actionKey, expectedAction);
                var formatted = DiagnosticsRecentEventFormatter.Format(
                    new DiagnosticEvent(
                        DateTimeOffset.Now,
                        DiagnosticLevel.Information,
                        "SettingsConfiguration",
                        $"{subject}: {action}. {technicalMessage}",
                        StackTrace: @"C:\Users\private-user\secret stack")
                    {
                        SettingsConfiguration = new SettingsConfigurationDetail(subject, action),
                    },
                    culture);

                Assert.Contains(expectedSubject, formatted, StringComparison.Ordinal);
                Assert.Contains(expectedAction, formatted, StringComparison.Ordinal);
                Assert.DoesNotContain("Settings file", formatted, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\Users\", formatted, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret stack", formatted, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RecentEventFormatter_UnknownEventDoesNotExposeMessageOrStackTrace()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var unknown = DiagnosticsRecentEventFormatter.Format(
            new DiagnosticEvent(
                DateTimeOffset.Now,
                DiagnosticLevel.Warning,
                "UnknownDiagnosticEvent",
                @"C:\Users\private-user\secret.dwg",
                StackTrace: "sensitive stack"),
            culture);

        Assert.Contains("UnknownDiagnosticEvent", unknown);
        Assert.DoesNotContain(@"C:\Users\", unknown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.dwg", unknown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive stack", unknown, StringComparison.Ordinal);
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = VisualTreeHelper.GetParent(child);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
