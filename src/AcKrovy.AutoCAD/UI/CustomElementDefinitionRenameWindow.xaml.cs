using System.Windows;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using WpfMessageBox = System.Windows.MessageBox;

namespace AcKrovy.AutoCAD.UI;

public partial class CustomElementDefinitionRenameWindow : Window
{
    private readonly CustomElementDefinition _definition;

    internal CustomElementDefinition? RenamedDefinition { get; private set; }

    internal CustomElementDefinitionRenameWindow(
        CustomElementDefinition definition)
    {
        InitializeComponent();
        _definition = CustomElementDefinitionRules.Normalize(definition);
        NameTextBox.Text = _definition.Name;
        NameTextBox.SelectAll();
        NameTextBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RenamedDefinition = CustomElementDefinitionRenameRules.Rename(
                _definition,
                NameTextBox.Text);
            DialogResult = true;
        }
        catch (ArgumentException)
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "CustomRenameWindow_InvalidName",
                    AppLanguageService.CurrentUiCulture),
                UiStrings.GetString(
                    "Message_DialogTitle",
                    AppLanguageService.CurrentUiCulture),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
