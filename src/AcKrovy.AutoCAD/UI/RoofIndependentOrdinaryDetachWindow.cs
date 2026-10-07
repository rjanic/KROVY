using System.Windows;
using System.Windows.Controls;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Dedicated first geometric-edit confirmation with stable Slovak actions.</summary>
internal sealed class RoofIndependentOrdinaryDetachWindow : WoodDialogWindow
{
    private readonly System.Windows.Controls.CheckBox _doNotShowAgain = new();
    internal bool DoNotShowAgain => _doNotShowAgain.IsChecked == true;
    public RoofIndependentOrdinaryDetachWindow()
        : base(UiStrings.GetString("RoofOrdinaryDetachTitle"))
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 440;
        var content = new StackPanel { Margin = new Thickness(14) };
        var explanation = new TextBlock
        {
            Text = UiStrings.GetString("RoofOrdinaryDetachBody").Replace("\r\n", "\n"),
            TextWrapping = TextWrapping.Wrap,
        };
        explanation.SetResourceReference(TextBlock.StyleProperty, "SettingsDescriptionStyle");
        var information = new Border
        {
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(10),
            Child = explanation,
        };
        information.SetResourceReference(BackgroundProperty, "SettingsPanelBackgroundBrush");
        information.SetResourceReference(BorderBrushProperty, "SettingsBorderBrush");
        information.BorderThickness = new Thickness(1);
        content.Children.Add(information);
        _doNotShowAgain.Content = UiStrings.GetString("Warning_DoNotShowAgain");
        _doNotShowAgain.SetResourceReference(StyleProperty, "RoofGeometryCheckBoxStyle");
        _doNotShowAgain.Margin = new Thickness(8, 0, 8, 14);
        content.Children.Add(_doNotShowAgain);
        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var yes = new System.Windows.Controls.Button { Content = UiStrings.GetString("RoofOrdinaryDetachYes"), MinWidth = 78, IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0) };
        yes.Click += (_, _) => DialogResult = true;
        ApplyWoodButtonStyle(yes, primary: true);
        var no = new System.Windows.Controls.Button { Content = UiStrings.GetString("RoofOrdinaryDetachNo"), MinWidth = 78, IsCancel = true };
        no.Click += (_, _) => DialogResult = false;
        ApplyWoodButtonStyle(no, primary: false);
        actions.Children.Add(yes);
        actions.Children.Add(no);
        var actionSection = new Border
        {
            Padding = new Thickness(14),
            Child = actions,
        };
        actionSection.SetResourceReference(BackgroundProperty, "WoodOakDarkBrush");
        content.Children.Add(actionSection);
        Content = content;
    }
}
