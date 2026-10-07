using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;

using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Separator = System.Windows.Controls.Separator;
using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Výškové osadenie — elevation seating editor for one Ordinary rafter.
/// Displays SH/OS/VH reference selector, lower/upper Z, slope, and calculation mode.
/// Follows the approved KROVY wood WPF style (WoodDialogWindow base).
/// No raw MessageBox. Uses WoodDialogWindow resources.
/// </summary>
internal sealed class RoofOrdinaryElevationWindow : WoodDialogWindow
{
    private readonly RoofOrdinaryElevationViewModel _vm;
    private TextBox? _lowerZBox;
    private TextBox? _upperZBox;
    private TextBox? _slopeBox;

    public RoofOrdinaryElevationWindow(RoofOrdinaryElevationViewModel viewModel)
        : base(UiStrings.GetString("RoofOrdinaryElevation_Title"))
    {
        _vm = viewModel;
        DataContext = _vm;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 480;
        Content = BuildContent();
        _vm.PropertyChanged += (_, _) => UpdateEditableStates();
        UpdateEditableStates();
    }

    // -------------------------------------------------------------------------
    // Result
    // -------------------------------------------------------------------------

    internal bool Accepted => DialogResult == true;

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(14), MinWidth = 440 };

        // Section header: Výškové osadenie
        var header = new TextBlock
        {
            Text = UiStrings.GetString("RoofOrdinaryElevation_Section"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        header.SetResourceReference(TextBlock.StyleProperty, "SettingsLabelStyle");
        root.Children.Add(header);

        // Panel body
        var panel = new Border
        {
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(10),
        };
        panel.SetResourceReference(BackgroundProperty, "SettingsPanelBackgroundBrush");
        panel.SetResourceReference(BorderBrushProperty, "SettingsBorderBrush");
        panel.BorderThickness = new Thickness(1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var inner = new StackPanel();

        // Reference selector: Referencia [ SH ] [ OS ] [ VH ]
        inner.Children.Add(BuildReferenceRow());
        inner.Children.Add(Separator());

        // Lower Z row
        inner.Children.Add(BuildZRow(
            labelKey: "RoofOrdinaryElevation_LowerZ",
            binding: nameof(RoofOrdinaryElevationViewModel.LowerZText),
            editableBinding: nameof(RoofOrdinaryElevationViewModel.LowerZEditable),
            out _lowerZBox));

        // Upper Z row
        inner.Children.Add(BuildZRow(
            labelKey: "RoofOrdinaryElevation_UpperZ",
            binding: nameof(RoofOrdinaryElevationViewModel.UpperZText),
            editableBinding: nameof(RoofOrdinaryElevationViewModel.UpperZEditable),
            out _upperZBox));

        // Slope row
        inner.Children.Add(BuildZRow(
            labelKey: "RoofOrdinaryElevation_Slope",
            binding: nameof(RoofOrdinaryElevationViewModel.SlopeText),
            editableBinding: nameof(RoofOrdinaryElevationViewModel.SlopeEditable),
            out _slopeBox));

        inner.Children.Add(Separator());

        // Calculation mode
        inner.Children.Add(BuildCalcModeRow());

        panel.Child = inner;
        root.Children.Add(panel);

        // Action bar
        root.Children.Add(BuildActionBar());
        return root;
    }

    private StackPanel BuildReferenceRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = UiStrings.GetString("RoofOrdinaryElevation_Reference"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        label.SetResourceReference(TextBlock.StyleProperty, "SettingsLabelStyle");
        row.Children.Add(label);

        row.Children.Add(MakeReferenceButton("SH", nameof(RoofOrdinaryElevationViewModel.SH_Selected),
            UiStrings.GetString("RoofOrdinaryElevation_SH_Tooltip")));
        row.Children.Add(MakeReferenceButton("OS", nameof(RoofOrdinaryElevationViewModel.OS_Selected),
            UiStrings.GetString("RoofOrdinaryElevation_OS_Tooltip")));
        row.Children.Add(MakeReferenceButton("VH", nameof(RoofOrdinaryElevationViewModel.VH_Selected),
            UiStrings.GetString("RoofOrdinaryElevation_VH_Tooltip")));
        return row;
    }

    private Button MakeReferenceButton(string label, string bindingPath, string tooltip)
    {
        var btn = new Button
        {
            Content = label,
            MinWidth = 44,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = tooltip,
        };
        btn.SetBinding(Button.IsEnabledProperty, new Binding(bindingPath) { Source = _vm, Mode = BindingMode.OneWay, Converter = new System.Windows.Controls.BooleanToVisibilityConverter() });
        btn.SetResourceReference(StyleProperty, "SettingsSecondaryButtonStyle");
        // Clicking sets the binding property to true via code-behind click handler.
        btn.Click += (_, _) =>
        {
            if (label == "SH") _vm.SH_Selected = true;
            else if (label == "OS") _vm.OS_Selected = true;
            else _vm.VH_Selected = true;
        };
        return btn;
    }

    private StackPanel BuildZRow(string labelKey, string binding, string editableBinding, out TextBox box)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var label = new TextBlock
        {
            Text = UiStrings.GetString(labelKey),
            Width = 160,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.StyleProperty, "SettingsLabelStyle");
        row.Children.Add(label);
        box = new TextBox { Width = 120, Margin = new Thickness(0, 0, 8, 0) };
        box.SetBinding(TextBox.TextProperty,
            new Binding(binding) { Source = _vm, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        box.SetResourceReference(StyleProperty, "SettingsTextBoxStyle");
        row.Children.Add(box);
        return row;
    }

    private StackPanel BuildCalcModeRow()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var label = new TextBlock { Text = UiStrings.GetString("RoofOrdinaryElevation_CalcMode") };
        label.SetResourceReference(TextBlock.StyleProperty, "SettingsLabelStyle");
        stack.Children.Add(label);

        void AddModeButton(string key, string bindingPath)
        {
            var btn = new RadioButton
            {
                Content = UiStrings.GetString(key),
                Margin = new Thickness(0, 4, 0, 0),
                GroupName = "ElevationCalcMode",
            };
            btn.SetBinding(RadioButton.IsCheckedProperty,
                new Binding(bindingPath) { Source = _vm, Mode = BindingMode.TwoWay });
            btn.SetResourceReference(StyleProperty, "RoofGeometryCheckBoxStyle");
            stack.Children.Add(btn);
        }
        AddModeButton("RoofOrdinaryElevation_Mode_LowerUpper", nameof(RoofOrdinaryElevationViewModel.Mode_LowerUpper));
        AddModeButton("RoofOrdinaryElevation_Mode_LowerSlope", nameof(RoofOrdinaryElevationViewModel.Mode_LowerSlope));
        AddModeButton("RoofOrdinaryElevation_Mode_UpperSlope", nameof(RoofOrdinaryElevationViewModel.Mode_UpperSlope));
        return stack;
    }

    private UIElement BuildActionBar()
    {
        var bar = new Border { Padding = new Thickness(10) };
        bar.SetResourceReference(BackgroundProperty, "WoodOakDarkBrush");
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var apply = new Button { Content = UiStrings.GetString("RoofOrdinaryElevation_Apply"), MinWidth = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        apply.Click += (_, _) => DialogResult = true;
        ApplyWoodButtonStyle(apply, primary: true);
        var cancel = new Button { Content = UiStrings.GetString("RoofOrdinaryElevation_Cancel"), MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;
        ApplyWoodButtonStyle(cancel, primary: false);
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        bar.Child = actions;
        return bar;
    }

    private static Separator Separator() => new() { Margin = new Thickness(0, 8, 0, 8) };

    private void UpdateEditableStates()
    {
        if (_lowerZBox is not null) _lowerZBox.IsReadOnly = !_vm.LowerZEditable;
        if (_upperZBox is not null) _upperZBox.IsReadOnly = !_vm.UpperZEditable;
        if (_slopeBox is not null) _slopeBox.IsReadOnly = !_vm.SlopeEditable;
    }
}
