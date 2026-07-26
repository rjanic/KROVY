using System.Globalization;

namespace AcKrovy.Localization;

public enum StatusBannerSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

/// <summary>Keeps status identity independent from its current translation.</summary>
public sealed class StatusBannerState
{
    private object[] _arguments = [];

    public bool IsVisible { get; private set; }
    public long Version { get; private set; }
    public string? ResourceKey { get; private set; }
    public IReadOnlyList<object> Arguments => _arguments;
    public StatusBannerSeverity Severity { get; private set; }

    public long Show(string resourceKey, StatusBannerSeverity severity, params object[] arguments)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("A resource key is required.", nameof(resourceKey));
        }

        ResourceKey = resourceKey;
        Severity = severity;
        _arguments = arguments?.ToArray() ?? [];
        IsVisible = true;
        return ++Version;
    }

    public bool TryHide(long version)
    {
        if (!IsVisible || version != Version)
        {
            return false;
        }

        IsVisible = false;
        return true;
    }

    public void Clear()
    {
        IsVisible = false;
        ResourceKey = null;
        _arguments = [];
        Version++;
    }

    public string Resolve(CultureInfo? culture = null)
    {
        if (!IsVisible || ResourceKey is null)
        {
            return string.Empty;
        }

        var format = UiStrings.GetString(ResourceKey, culture);
        return _arguments.Length == 0
            ? format
            : string.Format(culture ?? CultureInfo.CurrentUICulture, format, _arguments);
    }
}
