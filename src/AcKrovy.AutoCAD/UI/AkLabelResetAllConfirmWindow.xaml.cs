using System.Windows;
using System.Windows.Threading;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class AkLabelResetAllConfirmWindow : Window
{
    public AkLabelResetAllConfirmWindow(SettingsTheme theme)
    {
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);

        // XAML marks Cancel as IsDefault for safe Enter=Cancel UX, but a leftover
        // Enter from AutoCAD GetKeywords can fire that default the instant the
        // modal becomes active — before the user ever sees the dialog. Keep the
        // declarative default in XAML for design/contract, then arm it only after
        // the dispatcher goes idle.
        CancelButton.IsDefault = false;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                CancelButton.IsDefault = true;
                CancelButton.Focus();
            }));
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
