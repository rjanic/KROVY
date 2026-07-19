using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow : Window
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");

    public ObservableCollection<LayerSettingsRow> Rows { get; } = [];
    public ObservableCollection<ElementDefaultSettingsRow> DefaultRows { get; } = [];
    public IReadOnlyList<LayerColorOption> ColorOptions { get; } = LayerColorOption.CreateDefaults();

    internal ElementLayerProfile? Profile { get; private set; }
    internal TimberElementDefaultProfile? DefaultProfile { get; private set; }
    internal bool ApplyToExistingElements { get; private set; }
    internal CuttingAllowanceApplyMode CuttingAllowanceApplyMode { get; private set; }

    internal LayerSettingsWindow(ElementLayerProfile profile, TimberElementDefaultProfile defaultProfile)
    {
        InitializeComponent();
        DataContext = this;
        ReplaceRows(profile.Normalize());
        ReplaceDefaultRows(defaultProfile.Normalize());
        StylesDataGrid.ItemsSource = Rows;
        DefaultsDataGrid.ItemsSource = DefaultRows;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        ReplaceRows(ElementLayerProfile.CreateDefault());
        ReplaceDefaultRows(TimberElementDefaultProfile.CreateDefault());
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: false, CuttingAllowanceApplyMode.NewElementsOnly);
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: true, CuttingAllowanceApplyMode.AllElements);
    }

    private void SaveAndApplySelected_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: true, CuttingAllowanceApplyMode.SelectedElements);
    }

    private void SaveNewElementsOnly_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: false, CuttingAllowanceApplyMode.NewElementsOnly);
    }

    private void Save(bool applyToExistingElements, CuttingAllowanceApplyMode cuttingAllowanceApplyMode)
    {
        StylesDataGrid.CommitEdit();
        StylesDataGrid.CommitEdit();
        DefaultsDataGrid.CommitEdit();
        DefaultsDataGrid.CommitEdit();

        if (!TryBuildProfile(out var profile) ||
            !TryBuildDefaultProfile(out var defaultProfile))
        {
            return;
        }

        Profile = profile;
        DefaultProfile = defaultProfile;
        ApplyToExistingElements = applyToExistingElements;
        CuttingAllowanceApplyMode = cuttingAllowanceApplyMode;
        DialogResult = true;
    }

    private bool TryBuildProfile(out ElementLayerProfile profile)
    {
        var styles = new List<ElementLayerStyle>();
        var occupiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            if (!LayerNameValidator.TryValidate(row.LayerName, out var layerName, out var error))
            {
                WpfMessageBox.Show(
                    $"{row.ElementLabel}: {error}",
                    "ACAD KROVY",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = ElementLayerProfile.CreateDefault();
                return false;
            }

            if (!occupiedNames.Add(layerName))
            {
                WpfMessageBox.Show(
                    $"Hladina „{layerName}“ je zadaná viackrát. Každý typ prvku musí mať vlastnú hladinu.",
                    "ACAD KROVY",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = ElementLayerProfile.CreateDefault();
                return false;
            }

            styles.Add(new ElementLayerStyle(row.ElementType, layerName, row.SelectedColor.Index));
        }

        profile = new ElementLayerProfile
        {
            Styles = styles,
        };
        return true;
    }

    private bool TryBuildDefaultProfile(out TimberElementDefaultProfile profile)
    {
        var styles = new List<TimberElementDefaultStyle>();

        foreach (var row in DefaultRows)
        {
            if (!TryReadNonNegativeNumber(row.CuttingAllowanceMmText, out var cuttingAllowanceMm))
            {
                WpfMessageBox.Show(
                    $"{row.ElementLabel}: prídavok na rez musí byť nezáporné číslo v milimetroch, najviac {TimberElementDefaultProfile.MaxCuttingAllowanceMm:0}.",
                    "ACAD KROVY",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = TimberElementDefaultProfile.CreateDefault();
                return false;
            }

            styles.Add(new TimberElementDefaultStyle(row.ElementType, cuttingAllowanceMm));
        }

        profile = new TimberElementDefaultProfile
        {
            Styles = styles,
        }.Normalize();
        return true;
    }

    private void ReplaceRows(ElementLayerProfile profile)
    {
        Rows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            var style = profile.GetStyle(type);
            var color = ColorOptions.FirstOrDefault(option => option.Index == style.ColorIndex)
                ?? ColorOptions.First(option => option.Index == 8);
            Rows.Add(new LayerSettingsRow(type, TimberElementLabels.ToSlovak(type), style.LayerName, color));
        }
    }

    private void ReplaceDefaultRows(TimberElementDefaultProfile profile)
    {
        DefaultRows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            DefaultRows.Add(new ElementDefaultSettingsRow(
                type,
                TimberElementLabels.ToSlovak(type),
                Format(profile.GetCuttingAllowanceMm(type))));
        }
    }

    private static bool TryReadNonNegativeNumber(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, SlovakCulture, out value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value >= 0 &&
                value <= TimberElementDefaultProfile.MaxCuttingAllowanceMm;
        }

        value = 0;
        return false;
    }

    private static string Format(double value) =>
        value.ToString("0.###", SlovakCulture);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

internal enum CuttingAllowanceApplyMode
{
    NewElementsOnly,
    SelectedElements,
    AllElements,
}

public sealed class LayerSettingsRow : INotifyPropertyChanged
{
    private string _layerName;
    private LayerColorOption _selectedColor;

    public LayerSettingsRow(TimberElementType elementType, string elementLabel, string layerName, LayerColorOption selectedColor)
    {
        ElementType = elementType;
        ElementLabel = elementLabel;
        _layerName = layerName;
        _selectedColor = selectedColor;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel { get; }

    public string LayerName
    {
        get => _layerName;
        set
        {
            if (_layerName == value)
            {
                return;
            }

            _layerName = value;
            OnPropertyChanged();
        }
    }

    public LayerColorOption SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (Equals(_selectedColor, value))
            {
                return;
            }

            _selectedColor = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ElementDefaultSettingsRow : INotifyPropertyChanged
{
    private string _cuttingAllowanceMmText;

    public ElementDefaultSettingsRow(TimberElementType elementType, string elementLabel, string cuttingAllowanceMmText)
    {
        ElementType = elementType;
        ElementLabel = elementLabel;
        _cuttingAllowanceMmText = cuttingAllowanceMmText;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel { get; }

    public string CuttingAllowanceMmText
    {
        get => _cuttingAllowanceMmText;
        set
        {
            if (_cuttingAllowanceMmText == value)
            {
                return;
            }

            _cuttingAllowanceMmText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record LayerColorOption(int Index, string Label, MediaBrush Brush)
{
    public static IReadOnlyList<LayerColorOption> CreateDefaults() =>
    [
        Create(1, "Červená (1)", "#FF0000"),
        Create(2, "Žltá (2)", "#FFFF00"),
        Create(3, "Zelená (3)", "#00CC00"),
        Create(4, "Azúrová (4)", "#00CCCC"),
        Create(5, "Modrá (5)", "#3366FF"),
        Create(6, "Purpurová (6)", "#CC00CC"),
        Create(30, "Oranžová (30)", "#FF7F00"),
        Create(8, "Sivá (8)", "#777777"),
        Create(9, "Svetlosivá (9)", "#B5B5B5"),
    ];

    private static LayerColorOption Create(int index, string label, string hex)
    {
        var brush = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return new LayerColorOption(index, label, brush);
    }
}
