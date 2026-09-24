using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AcKrovy.AutoCAD.UI;

internal enum AutomaticPurlinEditorTabKind
{
    WallPlate = 1,
    Ridge = 2,
    Intermediate = 3,
}

/// <summary>
/// Presentation-only tab descriptor for the Automatic-Purlin editor strip.
/// Does not own layout, validation rules, or technical calculations.
/// </summary>
internal sealed class AutomaticPurlinEditorTabViewModel : INotifyPropertyChanged
{
    private string _title;
    private bool _hasError;
    private string _automationName;

    public AutomaticPurlinEditorTabViewModel(
        AutomaticPurlinEditorTabKind kind,
        AutomaticPurlinDialogViewModel dialog,
        AutomaticPurlinRowViewModel? intermediateRow,
        string title,
        bool hasError,
        string automationName)
    {
        Kind = kind;
        Dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        IntermediateRow = intermediateRow;
        if (kind == AutomaticPurlinEditorTabKind.Intermediate && intermediateRow is null)
        {
            throw new ArgumentNullException(nameof(intermediateRow));
        }

        _title = title ?? string.Empty;
        _hasError = hasError;
        _automationName = automationName ?? title ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AutomaticPurlinEditorTabKind Kind { get; }
    public AutomaticPurlinDialogViewModel Dialog { get; }
    public AutomaticPurlinRowViewModel? IntermediateRow { get; }

    /// <summary>
    /// Content host for the editor template: dialog for WallPlate/Ridge, row for Intermediate.
    /// </summary>
    public object ContentHost => Kind == AutomaticPurlinEditorTabKind.Intermediate
        ? IntermediateRow!
        : Dialog;

    /// <summary>
    /// Technical-metrics host exposing RoofPlane/Top/Center/Bottom relative texts and tooltips.
    /// </summary>
    public object MetricsHost => Kind switch
    {
        AutomaticPurlinEditorTabKind.WallPlate => Dialog.WallPlateRow,
        AutomaticPurlinEditorTabKind.Ridge => Dialog.RidgeTechnicalSummary,
        _ => IntermediateRow!,
    };

    public string Title
    {
        get => _title;
        private set
        {
            if (string.Equals(_title, value, StringComparison.Ordinal))
            {
                return;
            }

            _title = value;
            OnPropertyChanged();
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
            {
                return;
            }

            _hasError = value;
            OnPropertyChanged();
        }
    }

    public string AutomationName
    {
        get => _automationName;
        private set
        {
            if (string.Equals(_automationName, value, StringComparison.Ordinal))
            {
                return;
            }

            _automationName = value;
            OnPropertyChanged();
        }
    }

    public void UpdatePresentation(string title, bool hasError, string automationName)
    {
        Title = title;
        HasError = hasError;
        AutomationName = automationName;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
