using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Single acknowledgement warning for an attempted direct Physical3D edit.</summary>
internal sealed class RoofPhysical3DWarningWindow : WoodDialogWindow
{
    private readonly System.Windows.Controls.CheckBox _doNotShowAgain = new();
    internal bool DoNotShowAgain => _doNotShowAgain.IsChecked == true;
    public RoofPhysical3DWarningWindow()
        : base(UiStrings.GetString("RoofOrdinaryPhysical3DWarningTitle"))
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 460;

        var content = new WpfStackPanel { Margin = new Thickness(14) };
        var body = new WpfTextBlock
        {
            Text = UiStrings.GetString("Command_Roof_DerivedPhysicalMoveRejected"),
            TextWrapping = TextWrapping.Wrap,
        };
        body.SetResourceReference(WpfTextBlock.StyleProperty, "SettingsDescriptionStyle");
        var information = new System.Windows.Controls.Border
        {
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(10),
            Child = body,
        };
        information.SetResourceReference(BackgroundProperty, "SettingsPanelBackgroundBrush");
        information.SetResourceReference(BorderBrushProperty, "SettingsBorderBrush");
        information.BorderThickness = new Thickness(1);
        content.Children.Add(information);
        _doNotShowAgain.Content = UiStrings.GetString("Warning_DoNotShowAgain");
        _doNotShowAgain.SetResourceReference(StyleProperty, "RoofGeometryCheckBoxStyle");
        _doNotShowAgain.Margin = new Thickness(8, 0, 8, 14);
        content.Children.Add(_doNotShowAgain);

        var actions = new WpfStackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var ok = new WpfButton
        {
            Content = UiStrings.GetString("RoofOrdinaryPhysical3DWarningOk"),
            MinWidth = 92,
            IsDefault = true,
            IsCancel = true,
        };
        ApplyWoodButtonStyle(ok, primary: true);
        ok.Click += (_, _) => DialogResult = true;
        actions.Children.Add(ok);
        var actionSection = new System.Windows.Controls.Border
        {
            Padding = new Thickness(14),
            Child = actions,
        };
        actionSection.SetResourceReference(BackgroundProperty, "WoodOakDarkBrush");
        content.Children.Add(actionSection);
        Content = content;
    }
}
