using System.Globalization;

namespace AcKrovy.Localization;

public static class TimberMaterialDisplayNameProvider
{
    public static string GetDisplayName(
        string? storedMaterial,
        CultureInfo? culture = null)
    {
        if (storedMaterial is null)
        {
            return string.Empty;
        }

        return TimberMaterialCatalog.TryGetItem(storedMaterial, out var item)
            ? UiStrings.GetString(item!.DisplayResourceKey, culture)
            : storedMaterial;
    }

    public static IReadOnlyList<TimberMaterialDisplayOption> GetOptions(
        string? currentStoredMaterial,
        CultureInfo? culture = null)
    {
        var options = TimberMaterialCatalog.Items
            .Select(item => new TimberMaterialDisplayOption(
                item.StoredValue,
                UiStrings.GetString(item.DisplayResourceKey, culture),
                isCatalogItem: true))
            .ToList();

        if (!string.IsNullOrEmpty(currentStoredMaterial) &&
            !TimberMaterialCatalog.TryGetItem(currentStoredMaterial, out _))
        {
            options.Add(new TimberMaterialDisplayOption(
                currentStoredMaterial!,
                currentStoredMaterial!,
                isCatalogItem: false));
        }

        return options;
    }
}

public sealed class TimberMaterialDisplayOption
{
    public TimberMaterialDisplayOption(
        string storedValue,
        string displayName,
        bool isCatalogItem)
    {
        StoredValue = storedValue;
        DisplayName = displayName;
        IsCatalogItem = isCatalogItem;
    }

    public string StoredValue { get; }
    public string DisplayName { get; }
    public bool IsCatalogItem { get; }
}
