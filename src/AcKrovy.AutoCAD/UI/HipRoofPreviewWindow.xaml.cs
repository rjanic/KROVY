using System.ComponentModel;
using System.Windows;
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
}

internal enum HipRoofPreviewDialogAction
{
    None = 0,
    Preview = 1,
    Cancel = 2,
}
