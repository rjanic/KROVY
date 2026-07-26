using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Localization;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AcKrovy.AutoCAD.UI;

public partial class AciColorPicker : WpfUserControl
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options),
        typeof(IEnumerable),
        typeof(AciColorPicker),
        new PropertyMetadata(null, OptionsChanged));

    public static readonly DependencyProperty SelectedAciIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedAciIndex),
            typeof(int),
            typeof(AciColorPicker),
            new FrameworkPropertyMetadata(
                1,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                SelectedAciIndexChanged));

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(LayerColorOption),
        typeof(AciColorPicker),
        new PropertyMetadata(null));

    public AciColorPicker() =>
        InitializeComponent();

    public IEnumerable? Options
    {
        get => (IEnumerable?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public int SelectedAciIndex
    {
        get => (int)GetValue(SelectedAciIndexProperty);
        set => SetValue(SelectedAciIndexProperty, value);
    }

    public LayerColorOption? SelectedColor
    {
        get => (LayerColorOption?)GetValue(SelectedColorProperty);
        private set => SetValue(SelectedColorProperty, value);
    }

    private IReadOnlyList<LayerColorOption> AvailableOptions =>
        Options?.Cast<LayerColorOption>().ToArray() ?? [];

    private static void OptionsChanged(
        DependencyObject owner,
        DependencyPropertyChangedEventArgs e) =>
        ((AciColorPicker)owner).RefreshSelectedColor();

    private static void SelectedAciIndexChanged(
        DependencyObject owner,
        DependencyPropertyChangedEventArgs e) =>
        ((AciColorPicker)owner).RefreshSelectedColor();

    private void RefreshSelectedColor() =>
        SelectedColor = AvailableOptions.FirstOrDefault(option =>
            option.Index == SelectedAciIndex);

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AciColorPickerRules.IsValid(SelectedAciIndex))
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var theme = owner is LayerSettingsWindow settings
            ? settings.Visual.SelectedTheme
            : SettingsTheme.Light;
        var options = AvailableOptions.Count > 0
            ? AvailableOptions
            : LayerColorOption.CreateAll(AppLanguageService.CurrentUiCulture);
        var dialog = new AciColorPickerWindow(
            SelectedAciIndex,
            options,
            theme)
        {
            Owner = owner,
        };

        if (dialog.ShowDialog() == true)
        {
            SetCurrentValue(SelectedAciIndexProperty, dialog.SelectedAciIndex);
            BindingOperations.GetBindingExpression(
                this,
                SelectedAciIndexProperty)?.UpdateSource();
        }

        OpenButton.Focus();
    }
}
