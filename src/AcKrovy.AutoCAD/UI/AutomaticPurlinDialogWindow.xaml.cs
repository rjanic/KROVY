using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using FormsScreen = System.Windows.Forms.Screen;

namespace AcKrovy.AutoCAD.UI;

public partial class AutomaticPurlinDialogWindow : Window
{
    private const double PreferredWidth = 900;
    private const double PreferredHeight = 760;
    private const double PreferredMaximumWidth = 1080;
    private const double PreferredMaximumHeight = 920;
    private const double PreferredMinimumWidth = 700;
    private const double PreferredMinimumHeight = 540;
    private const double WorkAreaMargin = 48;
    private const double SmallestUsableWidth = 520;
    private const double SmallestUsableHeight = 440;

    private bool _closed;

    internal AutomaticPurlinDialogWindow(
        AutomaticPurlinDialogViewModel viewModel,
        SettingsTheme theme)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        DataContext = viewModel;
        ViewModel.PreviewChanged += ViewModel_PreviewChanged;
        SourceInitialized += AutomaticPurlinDialogWindow_SourceInitialized;
        Loaded += Window_Loaded;
    }

    internal event EventHandler? PreviewRequested;
    internal event EventHandler<AutomaticPurlinApplyRequestedEventArgs>? ApplyRequested;
    internal AutomaticPurlinDialogViewModel ViewModel { get; }
    internal bool IsClosed => _closed;

    protected override void OnClosing(CancelEventArgs e)
    {
        _closed = true;
        ViewModel.PreviewChanged -= ViewModel_PreviewChanged;
        SourceInitialized -= AutomaticPurlinDialogWindow_SourceInitialized;
        Loaded -= Window_Loaded;
        base.OnClosing(e);
    }

    private void AutomaticPurlinDialogWindow_SourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= AutomaticPurlinDialogWindow_SourceInitialized;
        var workArea = ResolveWorkAreaInDeviceIndependentPixels();
        var availableWidth = Math.Max(SmallestUsableWidth, workArea.Width - WorkAreaMargin);
        var availableHeight = Math.Max(SmallestUsableHeight, workArea.Height - WorkAreaMargin);

        MaxWidth = Math.Min(PreferredMaximumWidth, availableWidth);
        MaxHeight = Math.Min(PreferredMaximumHeight, availableHeight);
        MinWidth = Math.Min(PreferredMinimumWidth, MaxWidth);
        MinHeight = Math.Min(PreferredMinimumHeight, MaxHeight);
        Width = Math.Min(PreferredWidth, MaxWidth);
        Height = Math.Min(PreferredHeight, MaxHeight);
    }

    private Rect ResolveWorkAreaInDeviceIndependentPixels()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var referenceHandle = helper.Owner != IntPtr.Zero ? helper.Owner : helper.Handle;
            var pixelArea = FormsScreen.FromHandle(referenceHandle).WorkingArea;
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = transform.Transform(
                new System.Windows.Point(pixelArea.Left, pixelArea.Top));
            var bottomRight = transform.Transform(
                new System.Windows.Point(pixelArea.Right, pixelArea.Bottom));
            return new Rect(topLeft, bottomRight);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        PreviewRequested?.Invoke(this, EventArgs.Empty);

    private void ViewModel_PreviewChanged(object? sender, EventArgs e) =>
        PreviewRequested?.Invoke(this, EventArgs.Empty);

    private void AddRowButton_Click(object sender, RoutedEventArgs e) => ViewModel.AddRow();

    private void RemoveRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { CommandParameter: AutomaticPurlinRowViewModel row })
        {
            ViewModel.RemoveRow(row);
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ForcePreview();

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBeginApply(out var layout, out var datum, out var previewPlan) ||
            layout is null ||
            datum is null ||
            previewPlan is null)
        {
            return;
        }

        if (ApplyRequested is null)
        {
            ViewModel.CompleteApplyFailure();
            return;
        }

        ApplyRequested.Invoke(
            this,
            new AutomaticPurlinApplyRequestedEventArgs(layout, datum, previewPlan));
    }

    internal void CompleteSuccessfulApply()
    {
        if (!_closed)
        {
            DialogResult = true;
        }
    }

    internal void CompleteFailedApply() => ViewModel.CompleteApplyFailure();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RidgeEnabledCheckBox_InteractionStarted(object sender, KeyboardFocusChangedEventArgs e) =>
        ViewModel.SetRidgeInteractionActive(true);

    private void RidgeEnabledCheckBox_InteractionStarted(object sender, System.Windows.Input.MouseEventArgs e) =>
        ViewModel.SetRidgeInteractionActive(true);

    private void RidgeEnabledCheckBox_InteractionEnded(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RidgeEnabledCheckBox.IsKeyboardFocusWithin || RidgeEnabledCheckBox.IsMouseOver)
        {
            return;
        }

        ViewModel.SetRidgeInteractionActive(false);
    }

    private void RidgeEnabledCheckBox_InteractionEnded(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (RidgeEnabledCheckBox.IsKeyboardFocusWithin || RidgeEnabledCheckBox.IsMouseOver)
        {
            return;
        }

        ViewModel.SetRidgeInteractionActive(false);
    }

    private void IntermediateRowsControl_InteractionStarted(object sender, KeyboardFocusChangedEventArgs e) =>
        ViewModel.SetIntermediateInteractionActive(true);

    private void IntermediateRowsControl_InteractionStarted(object sender, MouseButtonEventArgs e) =>
        ViewModel.SetIntermediateInteractionActive(true);

    private void IntermediateRowsControl_InteractionEnded(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IntermediateRowsControl.IsKeyboardFocusWithin)
        {
            return;
        }

        ViewModel.SetIntermediateInteractionActive(false);
    }
}

internal sealed class AutomaticPurlinApplyRequestedEventArgs(
    RoofAutomaticPurlinLayout layout,
    RoofRelativeElevationDatum datum,
    RoofAutomaticPurlinPlan previewPlan) : EventArgs
{
    internal RoofAutomaticPurlinLayout Layout { get; } = layout;

    internal RoofRelativeElevationDatum Datum { get; } = datum;

    internal RoofAutomaticPurlinPlan PreviewPlan { get; } = previewPlan;
}
