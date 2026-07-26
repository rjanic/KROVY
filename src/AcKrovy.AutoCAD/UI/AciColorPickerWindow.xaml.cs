using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Localization;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AcKrovy.AutoCAD.UI;

public partial class AciColorPickerWindow : Window, INotifyPropertyChanged
{
    private readonly IReadOnlyList<LayerColorOption> _options;
    private bool _syncingInput;
    private bool _syncingSelection;
    private int _pendingAciIndex;

    internal AciColorPickerWindow(
        int originalAciIndex,
        IReadOnlyList<LayerColorOption> options,
        SettingsTheme theme)
    {
        if (!AciColorPickerRules.IsValid(originalAciIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(originalAciIndex));
        }

        OriginalAciIndex = originalAciIndex;
        SelectedAciIndex = originalAciIndex;
        _pendingAciIndex = originalAciIndex;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        InitializeComponent();
        DataContext = this;
        ApplyTheme(theme);
        RefreshOptionGroups();
        Loaded += (_, _) =>
        {
            IndexInput.Focus();
            IndexInput.SelectAll();
            ScrollPendingIntoView();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int OriginalAciIndex { get; }
    public int SelectedAciIndex { get; private set; }

    public int PendingAciIndex
    {
        get => _pendingAciIndex;
        internal set => SetPending(value);
    }

    public LayerColorOption PendingColor =>
        _options.First(option => option.Index == PendingAciIndex);

    private void RefreshOptionGroups()
    {
        BasicColorList.ItemsSource = _options.Where(option =>
            AciColorPickerRules.BasicIndices.Contains(option.Index));
        MainColorList.ItemsSource = _options.Where(option =>
            AciColorPickerRules.MainPaletteIndices.Contains(option.Index));
        GrayscaleColorList.ItemsSource = _options.Where(option =>
            AciColorPickerRules.GrayscaleIndices.Contains(option.Index));
        SyncInput();
    }

    private void ApplyTheme(SettingsTheme theme)
    {
        var source = theme == SettingsTheme.Dark
            ? "Design/SettingsColors.Dark.xaml"
            : "Design/SettingsColors.Light.xaml";
        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(
                $"/AcKrovy.AutoCAD;component/UI/{source}",
                UriKind.RelativeOrAbsolute),
        };
    }

    private void ColorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection ||
            e.AddedItems.Count == 0 ||
            e.AddedItems[0] is not LayerColorOption option)
        {
            return;
        }

        SetPending(option.Index);
    }

    private void ColorList_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var delta = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -AciColorPickerRules.MainPaletteColumns,
            Key.Down => AciColorPickerRules.MainPaletteColumns,
            _ => 0,
        };
        if (delta == 0)
        {
            return;
        }

        SetPending(Math.Clamp(PendingAciIndex + delta, 1, 255));
        ScrollPendingIntoView();
        e.Handled = true;
    }

    private void IndexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingInput)
        {
            return;
        }

        if (AciColorSelectionRules.TryParseLayerIndex(IndexInput.Text, out var index) &&
            _options.Any(option => option.Index == index))
        {
            ValidationText.Visibility = Visibility.Collapsed;
            SetPending(index);
            return;
        }

        ValidationText.Visibility = Visibility.Visible;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        if (!AciColorPickerRules.IsValid(PendingAciIndex) ||
            !AciColorSelectionRules.TryParseLayerIndex(IndexInput.Text, out var inputIndex) ||
            inputIndex != PendingAciIndex)
        {
            ValidationText.Visibility = Visibility.Visible;
            IndexInput.Focus();
            return;
        }

        SelectedAciIndex = PendingAciIndex;
        DialogResult = true;
    }

    private void Cancel()
    {
        SelectedAciIndex = OriginalAciIndex;
        DialogResult = false;
    }

    private void SetPending(int index)
    {
        if (!AciColorPickerRules.IsValid(index) ||
            !_options.Any(option => option.Index == index) ||
            _pendingAciIndex == index)
        {
            return;
        }

        _pendingAciIndex = index;
        OnPropertyChanged(nameof(PendingAciIndex));
        OnPropertyChanged(nameof(PendingColor));
        SyncInput();
    }

    private void SyncInput()
    {
        _syncingInput = true;
        IndexInput.Text = PendingAciIndex.ToString();
        _syncingInput = false;
        ValidationText.Visibility = Visibility.Collapsed;

        _syncingSelection = true;
        BasicColorList.SelectedItem = BasicColorList.Items.Cast<LayerColorOption>()
            .FirstOrDefault(option => option.Index == PendingAciIndex);
        MainColorList.SelectedItem = MainColorList.Items.Cast<LayerColorOption>()
            .FirstOrDefault(option => option.Index == PendingAciIndex);
        GrayscaleColorList.SelectedItem = GrayscaleColorList.Items.Cast<LayerColorOption>()
            .FirstOrDefault(option => option.Index == PendingAciIndex);
        _syncingSelection = false;
    }

    private void ScrollPendingIntoView()
    {
        var list = PendingAciIndex switch
        {
            <= 9 => BasicColorList,
            <= 249 => MainColorList,
            _ => GrayscaleColorList,
        };
        list.ScrollIntoView(PendingColor);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
