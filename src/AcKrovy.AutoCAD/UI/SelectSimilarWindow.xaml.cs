using System.Globalization;
using System.Windows;
using AcKrovy.Core.Models;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class SelectSimilarWindow : Window
{
    internal SelectSimilarWindow(
        TimberElementSnapshot seed,
        SettingsTheme theme)
    {
        ArgumentNullException.ThrowIfNull(seed);
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);

        var defaults = TimberElementSimilarityCriteria.CreateDefault(seed);
        ElementTypeCheckBox.IsChecked = defaults.MatchElementType;
        CrossSectionCheckBox.IsChecked = defaults.MatchCrossSection;
        MaterialCheckBox.IsChecked = defaults.MatchMaterial;
        ElementIdCheckBox.IsChecked = defaults.MatchElementId;
        CuttingLengthCheckBox.IsChecked = defaults.MatchCuttingLength;
        CustomDefinitionCheckBox.IsChecked = defaults.MatchCustomElementTypeId;
        CustomDefinitionCheckBox.IsEnabled =
            seed.Data.ElementType == TimberElementType.Custom;
        UpdateToleranceState();
    }

    internal TimberElementSimilarityCriteria? Criteria { get; private set; }

    private void CuttingLengthCheckBox_Changed(object sender, RoutedEventArgs e) =>
        UpdateToleranceState();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(
                ToleranceTextBox.Text,
                NumberStyles.Float,
                AppLanguageService.CurrentUiCulture,
                out var tolerance) ||
            !double.IsFinite(tolerance) ||
            tolerance < 0d)
        {
            ValidationText.Visibility = Visibility.Visible;
            ToleranceTextBox.Focus();
            ToleranceTextBox.SelectAll();
            return;
        }

        Criteria = new TimberElementSimilarityCriteria
        {
            MatchElementType = ElementTypeCheckBox.IsChecked == true,
            MatchCrossSection = CrossSectionCheckBox.IsChecked == true,
            MatchMaterial = MaterialCheckBox.IsChecked == true,
            MatchElementId = ElementIdCheckBox.IsChecked == true,
            MatchCuttingLength = CuttingLengthCheckBox.IsChecked == true,
            MatchCustomElementTypeId = CustomDefinitionCheckBox.IsChecked == true,
            CuttingLengthToleranceMm = tolerance,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UpdateToleranceState()
    {
        if (!IsInitialized)
        {
            return;
        }

        ToleranceTextBox.IsEnabled = CuttingLengthCheckBox.IsChecked == true;
        ValidationText.Visibility = Visibility.Collapsed;
    }
}
