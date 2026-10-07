using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Reusable image-choice card for elevation (and future timber-type) selectors.
/// Real PNGs are resolved later via <see cref="ElevationImageChoiceCatalog.TryResolveImage"/>.
/// Recommended final PNG size: 80×80 px.
/// </summary>
public sealed class ElevationImageChoiceItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private ImageSource? _image;

    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Stable key for future PNG packs, e.g. Elevation.Ordinary.Reference.SH.</summary>
    public required string ImageKey { get; init; }
    /// <summary>Typed payload (reference kind or calculation mode).</summary>
    public required object Payload { get; init; }

    public ImageSource? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public bool HasImage => Image is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
