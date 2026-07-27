using System.Windows;
using AcKrovy.Core.Models;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

internal enum CsvExportSource
{
    PickFirst,
    ManualSelection,
    ModelSpace,
}

public partial class CsvExportWindow : Window
{
    internal CsvExportWindow(int pickFirstCount, SettingsTheme theme)
    {
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        PickFirstRadio.Content = UiStrings.Format(
            UiStrings.GetString("CsvExportWindow_PickFirstFormat"),
            pickFirstCount);
        PickFirstRadio.IsEnabled = pickFirstCount > 0;
        PickFirstRadio.IsChecked = pickFirstCount > 0;
        ManualSelectionRadio.IsChecked = pickFirstCount == 0;
    }

    internal CsvExportSource Source { get; private set; }
    internal TimberCsvExportMode Mode { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Source = PickFirstRadio.IsChecked == true
            ? CsvExportSource.PickFirst
            : ModelSpaceRadio.IsChecked == true
                ? CsvExportSource.ModelSpace
                : CsvExportSource.ManualSelection;
        Mode = SummarizedRadio.IsChecked == true
            ? TimberCsvExportMode.Summarized
            : TimberCsvExportMode.Individual;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
