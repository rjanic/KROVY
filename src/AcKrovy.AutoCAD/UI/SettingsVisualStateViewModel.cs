using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public sealed class SettingsVisualStateViewModel : INotifyPropertyChanged
{
    private CultureInfo _culture;
    private ObservableCollection<SettingsNavigationOption> _navigationItems = [];
    private SettingsWindowTabKind _selectedSection;
    private SettingsTheme _selectedTheme;
    private SettingsFormState _formState;

    public SettingsVisualStateViewModel(
        CultureInfo culture,
        SettingsWindowTabKind selectedSection,
        SettingsTheme selectedTheme)
    {
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _selectedSection = SettingsFashionLookRules.NormalizeSection(selectedSection);
        _selectedTheme = SettingsFashionLookRules.NormalizeTheme(selectedTheme);
        RefreshLocalization(culture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SectionChanged;
    public event EventHandler? ThemeChanged;

    public string ApplicationVersionLabel => $"ACAD KROVY {ApplicationVersionProvider.DisplayVersion}";

    public ObservableCollection<SettingsNavigationOption> NavigationItems
    {
        get => _navigationItems;
        private set
        {
            _navigationItems = value;
            OnPropertyChanged();
        }
    }

    public SettingsWindowTabKind SelectedSection
    {
        get => _selectedSection;
        set
        {
            var normalized = SettingsFashionLookRules.NormalizeSection(value);
            if (_selectedSection == normalized)
            {
                return;
            }

            _selectedSection = normalized;
            OnPropertyChanged();
            SectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SettingsTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var normalized = SettingsFashionLookRules.NormalizeTheme(value);
            if (_selectedTheme == normalized)
            {
                return;
            }

            _selectedTheme = normalized;
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FormStateText => UiStrings.GetString(
        SettingsFashionLookRules.FormStateResourceKey(_formState),
        _culture);
    public SettingsFormState CurrentFormState => _formState;

    public void SetFormState(SettingsFormState state)
    {
        _formState = state;
        OnPropertyChanged(nameof(CurrentFormState));
        OnPropertyChanged(nameof(FormStateText));
    }

    public void RefreshLocalization(CultureInfo culture)
    {
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
        NavigationItems = new ObservableCollection<SettingsNavigationOption>(
            SettingsFashionLookRules.NavigationSections.Select(section =>
                new SettingsNavigationOption(
                    section,
                    UiStrings.GetString(SectionResourceKey(section), culture))));
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(FormStateText));
    }

    private static string SectionResourceKey(SettingsWindowTabKind section) =>
        section switch
        {
            SettingsWindowTabKind.Manufacturing => "SettingsWindow_Manufacturing_Tab",
            SettingsWindowTabKind.Annotation => "SettingsWindow_Annotation_Tab",
            SettingsWindowTabKind.Language => "SettingsWindow_Language_Tab",
            _ => "SettingsWindow_Layers_Tab",
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record SettingsNavigationOption(
    SettingsWindowTabKind Section,
    string DisplayName);
