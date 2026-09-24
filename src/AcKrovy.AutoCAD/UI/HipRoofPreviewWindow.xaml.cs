using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class HipRoofPreviewWindow : Window
{
    private bool _closed;

    internal HipRoofPreviewWindow(HipRoofPreviewViewModel viewModel, SettingsTheme theme)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        DataContext = viewModel;
    }

    internal HipRoofPreviewViewModel ViewModel { get; }
    internal HipRoofPreviewDialogAction RequestedAction { get; private set; }
    internal bool IsClosed => _closed;

    internal void PrepareForInteraction() => RequestedAction = HipRoofPreviewDialogAction.None;

    /// <summary>
    /// Focuses the pitch field and selects its current value so the user can type a
    /// replacement angle immediately after a recoverable Apply validation failure.
    /// </summary>
    internal void FocusSlopeInput()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!SlopeTextBox.IsVisible)
                {
                    return;
                }

                _ = SlopeTextBox.Focus();
                SlopeTextBox.SelectAll();
            }));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        RequestedAction = HipRoofPreviewDialogAction.Cancel;
        _closed = true;
        base.OnClosing(e);
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanPreview)
        {
            RequestedAction = HipRoofPreviewDialogAction.Preview;
            Hide();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RequestedAction = HipRoofPreviewDialogAction.Cancel;
        Hide();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanApply)
        {
            RequestedAction = HipRoofPreviewDialogAction.Apply;
            Hide();
        }
    }
}

internal enum HipRoofPreviewDialogAction
{
    None = 0,
    Preview = 1,
    Apply = 2,
    Cancel = 3,
}
