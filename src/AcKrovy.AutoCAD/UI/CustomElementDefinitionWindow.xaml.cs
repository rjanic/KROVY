using System.Windows;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using WpfMessageBox = System.Windows.MessageBox;

namespace AcKrovy.AutoCAD.UI;

public partial class CustomElementDefinitionWindow : Window
{
    private readonly IReadOnlyList<CustomElementDefinition> _definitions;

    internal CustomElementDefinition? SelectedDefinition { get; private set; }
    internal bool CreatedNewDefinition { get; private set; }

    internal CustomElementDefinitionWindow(
        IReadOnlyList<CustomElementDefinition> definitions)
    {
        InitializeComponent();
        _definitions = definitions;
        ExistingDefinitionComboBox.ItemsSource = definitions
            .Select(definition => new DefinitionOption(
                definition,
                $"{definition.Name} ({definition.Prefix})"))
            .ToList();

        if (definitions.Count > 0)
        {
            ExistingDefinitionComboBox.SelectedIndex = 0;
            UseExistingRadioButton.IsChecked = true;
        }
        else
        {
            CreateNewRadioButton.IsChecked = true;
            UseExistingRadioButton.IsEnabled = false;
        }

        UpdateMode();
    }

    private void ModeChanged(object sender, RoutedEventArgs e) => UpdateMode();

    private void UpdateMode()
    {
        if (!IsInitialized)
        {
            return;
        }

        var createNew = CreateNewRadioButton.IsChecked == true;
        ExistingDefinitionComboBox.IsEnabled = !createNew && _definitions.Count > 0;
        NameTextBox.IsEnabled = createNew;
        PrefixTextBox.IsEnabled = createNew;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CreateNewRadioButton.IsChecked != true)
        {
            SelectedDefinition =
                (ExistingDefinitionComboBox.SelectedItem as DefinitionOption)?.Definition;
            if (SelectedDefinition is null)
            {
                ShowValidation("CustomWindow_SelectDefinition");
                return;
            }

            DialogResult = true;
            return;
        }

        try
        {
            var created = CustomElementDefinitionRules.Create(
                NameTextBox.Text,
                PrefixTextBox.Text);
            _ = CustomElementDefinitionCatalogRules.Normalize(
                _definitions.Append(created));
            SelectedDefinition = created;
            CreatedNewDefinition = true;
            DialogResult = true;
        }
        catch (ArgumentException)
        {
            ShowValidation("CustomWindow_InvalidDefinition");
        }
    }

    private static void ShowValidation(string key) =>
        WpfMessageBox.Show(
            UiStrings.GetString(key, AppLanguageService.CurrentUiCulture),
            UiStrings.GetString("Message_DialogTitle", AppLanguageService.CurrentUiCulture),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record DefinitionOption(
        CustomElementDefinition Definition,
        string DisplayName);
}
