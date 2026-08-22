using System.ComponentModel;
using System.Windows;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class GableRoofGeometryWindow : Window
{
    private bool _closed;

    internal GableRoofGeometryWindow(
        GableRoofGeometryViewModel viewModel,
        SettingsTheme theme)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        DataContext = viewModel;
    }

    internal GableRoofGeometryViewModel ViewModel { get; }

    internal GableRoofGeometryDialogAction RequestedAction { get; private set; }

    internal bool IsClosed => _closed;

    internal void PrepareForInteraction() => RequestedAction = GableRoofGeometryDialogAction.None;

    protected override void OnClosing(CancelEventArgs e)
    {
        RequestedAction = GableRoofGeometryDialogAction.Cancel;
        _closed = true;
        base.OnClosing(e);
    }

    private void PickRidgeDirectionButton_Click(object sender, RoutedEventArgs e) =>
        Request(GableRoofGeometryDialogAction.PickRidgeDirection);

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanPreview)
        {
            Request(GableRoofGeometryDialogAction.Preview);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanApply)
        {
            Request(GableRoofGeometryDialogAction.Apply);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        Request(GableRoofGeometryDialogAction.Cancel);

    private void Request(GableRoofGeometryDialogAction action)
    {
        RequestedAction = action;
        Hide();
    }
}

internal enum GableRoofGeometryDialogAction
{
    None = 0,
    PickRidgeDirection = 1,
    Preview = 2,
    Apply = 3,
    Cancel = 4,
}
