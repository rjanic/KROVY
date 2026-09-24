using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using FormsScreen = System.Windows.Forms.Screen;

namespace AcKrovy.AutoCAD.UI;

public partial class AutomaticPurlinDialogWindow : Window
{
    private const double PreferredWidth = 1700;
    private const double PreferredHeight = 820;
    private const double PreferredMaximumWidth = 1800;
    private const double PreferredMaximumHeight = 980;
    private const double PreferredMinimumWidth = 1400;
    private const double PreferredMinimumHeight = 680;
    private const double WorkAreaMargin = 48;
    private const double SmallestUsableWidth = 720;
    private const double SmallestUsableHeight = 480;

    private bool _closed;
    private readonly SettingsTheme _theme;
    private bool _hasExplicitRestoreBounds;
    private System.Windows.Controls.Button? _pinnedTechnicalMetricButton;

    internal AutomaticPurlinDialogWindow(
        AutomaticPurlinDialogViewModel viewModel,
        SettingsTheme theme)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _theme = theme;
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        DataContext = viewModel;
        ViewModel.PreviewChanged += ViewModel_PreviewChanged;
        ViewModel.EditorTabSelectionChanged += ViewModel_EditorTabSelectionChanged;
        SourceInitialized += AutomaticPurlinDialogWindow_SourceInitialized;
        Loaded += Window_Loaded;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
    }

    internal event EventHandler? PreviewRequested;
    internal event EventHandler<AutomaticPurlinApplyRequestedEventArgs>? ApplyRequested;
    internal AutomaticPurlinDialogViewModel ViewModel { get; }
    internal bool IsClosed => _closed;
    internal bool IsSuspendedForCadPreview { get; private set; }
    internal bool IsSuspendedForRafterPick { get; private set; }
    internal bool IsSuspendedForManualDialogRafterPick { get; private set; }
    internal Rect? SavedRestoreBounds { get; private set; }
    internal SettingsTheme Theme => _theme;

    /// <summary>
    /// Active intermediate editor scroll viewer (presentation helper for tests/HOST).
    /// </summary>
    internal ScrollViewer? DialogScrollViewer =>
        FindTaggedDescendant<ScrollViewer>(ElementEditorsTabControl, "DialogScrollViewer");

    /// <summary>
    /// Active intermediate editor host (presentation helper for tests/HOST).
    /// </summary>
    internal FrameworkElement? IntermediateRowsControl =>
        FindTaggedDescendant<FrameworkElement>(ElementEditorsTabControl, "IntermediateRowsControl");

    internal bool IsTechnicalTooltipPinned =>
        PinnedTechnicalTooltipPopup.IsOpen;

    internal void ApplyRestoreBounds(Rect bounds)
    {
        _hasExplicitRestoreBounds = true;
        Left = bounds.X;
        Top = bounds.Y;
        Width = Math.Max(PreferredMinimumWidth, bounds.Width);
        Height = Math.Max(PreferredMinimumHeight, bounds.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closed = true;
        ClosePinnedTechnicalTooltip();
        RoofSectionView.RafterLabelClicked -= RoofSectionView_RafterLabelClicked;
        ViewModel.PreviewChanged -= ViewModel_PreviewChanged;
        ViewModel.EditorTabSelectionChanged -= ViewModel_EditorTabSelectionChanged;
        SourceInitialized -= AutomaticPurlinDialogWindow_SourceInitialized;
        Loaded -= Window_Loaded;
        PreviewKeyDown -= Window_PreviewKeyDown;
        PreviewMouseDown -= Window_PreviewMouseDown;
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
        if (_hasExplicitRestoreBounds)
        {
            Width = Math.Min(Width, MaxWidth);
            Height = Math.Min(Height, MaxHeight);
            return;
        }

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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RoofSectionView.RafterLabelClicked += RoofSectionView_RafterLabelClicked;
        PreviewRequested?.Invoke(this, EventArgs.Empty);
        if (ViewModel.TryConsumeManualDialogReopen(out var reopen))
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() => OpenManualRafterDimensionsDialog(reopen)));
        }
    }

    private void RoofSectionView_RafterLabelClicked(object? sender, EventArgs e)
    {
        // Always open Rozmery krokvy. PreferCad must NOT bypass into a raw CAD pick:
        // that path called TryApplySelectedRafterDimensions for ANY roof and overwrote
        // current-roof host actual (empty footer, no conflict after other-roof pick).
        // Vybrať z výkresu inside the manual dialog owns ownership-aware classification.
        OpenManualRafterDimensionsDialog();
    }

    private void EnterRafterDimensionsButton_Click(object sender, RoutedEventArgs e) =>
        OpenManualRafterDimensionsDialog();

    private void ConfirmStoredManualRafterButton_Click(object sender, RoutedEventArgs e) =>
        _ = ViewModel.TryConfirmStoredManualAfterMissingActual();

    private void ResolveRafterSourceConflictButton_Click(object sender, RoutedEventArgs e) =>
        OpenRafterSourceConflictDialog();

    private void OpenManualRafterDimensionsDialog(
        AutomaticPurlinManualRafterDialogSession? reopen = null)
    {
        var seedWidth = reopen?.SeedWidthMm ?? ViewModel.RafterWidthMm;
        var seedHeight = reopen?.SeedHeightMm ?? ViewModel.RafterHeightMm;
        var dialog = new AutomaticPurlinManualRafterDialogWindow(
            seedWidth,
            seedHeight,
            AppLanguageService.CurrentUiCulture,
            _theme)
        {
            Owner = this,
        };
        var confirmed = dialog.ShowDialog() == true;
        if (dialog.IsSuspendedForCadPick)
        {
            ViewModel.BeginManualDialogCadPick(
                dialog.PreserveWidthMm,
                dialog.PreserveHeightMm);
            SavedRestoreBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            IsSuspendedForManualDialogRafterPick = true;
            Close();
            return;
        }

        if (!confirmed)
        {
            ViewModel.ClearManualDialogSession();
            return;
        }

        if (ViewModel.ShouldAdoptPickedRafterAsSelected(
                dialog.ConfirmedWidthMm,
                dialog.ConfirmedHeightMm))
        {
            _ = ViewModel.TryApplySelectedRafterDimensions(
                dialog.ConfirmedWidthMm,
                dialog.ConfirmedHeightMm);
        }
        else
        {
            _ = ViewModel.TryApplyManualRafterDimensions(
                dialog.ConfirmedWidthMm,
                dialog.ConfirmedHeightMm);
        }

        ViewModel.ClearManualDialogSession();
    }

    private void OpenRafterSourceConflictDialog()
    {
        if (!ViewModel.ShowRafterSourceConflictWarning ||
            ViewModel.RecoverableManualRafterWidthMm is not { } manualWidth ||
            ViewModel.RecoverableManualRafterHeightMm is not { } manualHeight)
        {
            return;
        }

        var dialog = new AutomaticPurlinRafterSourceConflictDialogWindow(
            manualWidth,
            manualHeight,
            ViewModel.ConflictActualRafterWidthMm,
            ViewModel.ConflictActualRafterHeightMm,
            AppLanguageService.CurrentUiCulture,
            _theme)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = dialog.Choice switch
        {
            AutomaticPurlinRafterSourceConflictChoice.KeepManual =>
                ViewModel.TryResolveRafterSourceConflictKeepManual(),
            AutomaticPurlinRafterSourceConflictChoice.UseActual =>
                ViewModel.TryResolveRafterSourceConflictUseActual(),
            _ => false,
        };
    }

    private void ViewModel_PreviewChanged(object? sender, EventArgs e) =>
        PreviewRequested?.Invoke(this, EventArgs.Empty);

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePinnedTechnicalTooltip();
        ViewModel.AddRow();
    }

    private void RemoveRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            { CommandParameter: AutomaticPurlinRowViewModel row })
        {
            ClosePinnedTechnicalTooltip();
            ViewModel.RemoveRow(row);
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ForcePreview();
        if (!ViewModel.CanPreview)
        {
            return;
        }

        SavedRestoreBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        IsSuspendedForCadPreview = true;
        Close();
    }

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
            new AutomaticPurlinApplyRequestedEventArgs(
                layout,
                datum,
                previewPlan,
                ViewModel.RafterWidthMm,
                ViewModel.RafterHeightMm));
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
        if (sender is FrameworkElement element &&
            (element.IsKeyboardFocusWithin || element.IsMouseOver))
        {
            return;
        }

        ViewModel.SetRidgeInteractionActive(false);
    }

    private void RidgeEnabledCheckBox_InteractionEnded(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement element &&
            (element.IsKeyboardFocusWithin || element.IsMouseOver))
        {
            return;
        }

        ViewModel.SetRidgeInteractionActive(false);
    }

    private void IntermediateEditor_InteractionStarted(object sender, KeyboardFocusChangedEventArgs e) =>
        ViewModel.SetIntermediateInteractionActive(true);

    private void IntermediateEditor_InteractionStarted(object sender, MouseButtonEventArgs e) =>
        ViewModel.SetIntermediateInteractionActive(true);

    private void IntermediateEditor_InteractionEnded(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsKeyboardFocusWithin)
        {
            return;
        }

        ViewModel.SetIntermediateInteractionActive(false);
    }

    private void ViewModel_EditorTabSelectionChanged(object? sender, EventArgs e) =>
        ClosePinnedTechnicalTooltip();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && PinnedTechnicalTooltipPopup.IsOpen)
        {
            ClosePinnedTechnicalTooltip();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!PinnedTechnicalTooltipPopup.IsOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            (IsDescendantOf(source, PinnedTechnicalTooltipPopup.Child) ||
             (_pinnedTechnicalMetricButton is not null &&
              IsDescendantOf(source, _pinnedTechnicalMetricButton))))
        {
            return;
        }

        ClosePinnedTechnicalTooltip();
    }

    private void TechnicalMetricButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        var tooltip = ResolveTechnicalTooltip(button);
        if (tooltip is null)
        {
            return;
        }

        if (PinnedTechnicalTooltipPopup.IsOpen &&
            ReferenceEquals(_pinnedTechnicalMetricButton, button))
        {
            ClosePinnedTechnicalTooltip();
            return;
        }

        if (_pinnedTechnicalMetricButton is not null)
        {
            ToolTipService.SetIsEnabled(_pinnedTechnicalMetricButton, true);
        }

        _pinnedTechnicalMetricButton = button;
        ToolTipService.SetIsEnabled(button, false);
        PinnedTechnicalTooltipPresenter.Content = tooltip;
        PinnedTechnicalTooltipPopup.PlacementTarget = button;
        PinnedTechnicalTooltipPopup.IsOpen = true;
    }

    private static AutomaticPurlinElevationTooltipViewModel? ResolveTechnicalTooltip(
        System.Windows.Controls.Button button)
    {
        if (button.ToolTip is AutomaticPurlinElevationTooltipViewModel fromToolTip)
        {
            return fromToolTip;
        }

        var host = button.DataContext;
        var tag = button.Tag as string;
        return (host, tag) switch
        {
            (AutomaticPurlinRowViewModel row, "RoofPlane") => row.RoofPlaneTooltip,
            (AutomaticPurlinRowViewModel row, "Top") => row.TopTooltip,
            (AutomaticPurlinRowViewModel row, "Center") => row.CenterTooltip,
            (AutomaticPurlinRowViewModel row, "Bottom") => row.BottomTooltip,
            (AutomaticPurlinTechnicalSummaryViewModel summary, "RoofPlane") => summary.RoofPlaneTooltip,
            (AutomaticPurlinTechnicalSummaryViewModel summary, "Top") => summary.TopTooltip,
            (AutomaticPurlinTechnicalSummaryViewModel summary, "Center") => summary.CenterTooltip,
            (AutomaticPurlinTechnicalSummaryViewModel summary, "Bottom") => summary.BottomTooltip,
            _ => null,
        };
    }

    private void ClosePinnedTechnicalTooltip()
    {
        if (_pinnedTechnicalMetricButton is not null)
        {
            ToolTipService.SetIsEnabled(_pinnedTechnicalMetricButton, true);
            _pinnedTechnicalMetricButton = null;
        }

        PinnedTechnicalTooltipPopup.IsOpen = false;
        PinnedTechnicalTooltipPresenter.Content = null;
    }

    private static T? FindTaggedDescendant<T>(DependencyObject? root, string tag)
        where T : FrameworkElement
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed &&
                string.Equals(typed.Tag as string, tag, StringComparison.Ordinal))
            {
                return typed;
            }

            var nested = FindTaggedDescendant<T>(child, tag);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}

internal sealed class AutomaticPurlinApplyRequestedEventArgs(
    RoofAutomaticPurlinLayout layout,
    RoofRelativeElevationDatum datum,
    RoofAutomaticPurlinPlan previewPlan,
    double rafterWidthMm,
    double rafterHeightMm) : EventArgs
{
    internal RoofAutomaticPurlinLayout Layout { get; } = layout;

    internal RoofRelativeElevationDatum Datum { get; } = datum;

    internal RoofAutomaticPurlinPlan PreviewPlan { get; } = previewPlan;

    internal double RafterWidthMm { get; } = rafterWidthMm;

    internal double RafterHeightMm { get; } = rafterHeightMm;
}
