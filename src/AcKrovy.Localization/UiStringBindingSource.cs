using System.ComponentModel;
using System.Globalization;

namespace AcKrovy.Localization;

public sealed class UiStringBindingSource : INotifyPropertyChanged
{
    public static UiStringBindingSource Shared { get; } = new();

    private CultureInfo? _culture;

    public CultureInfo? Culture
    {
        get => _culture;
        set
        {
            if (string.Equals(_culture?.Name, value?.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _culture = value;
            Refresh();
        }
    }

    public string this[string resourceKey] => UiStrings.GetString(resourceKey, Culture);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
