namespace AcKrovy.Localization;

public sealed class TimberMaterialCatalogItem
{
    public TimberMaterialCatalogItem(
        string storedValue,
        string displayResourceKey)
    {
        StoredValue = storedValue;
        DisplayResourceKey = displayResourceKey;
    }

    public string StoredValue { get; }
    public string DisplayResourceKey { get; }
}

public static class TimberMaterialCatalog
{
    public const string SpruceC24 = "Smrek C24";
    public const string SpruceC16 = "Smrek C16";
    public const string LarchC30 = "Smrekovec C30";
    public const string KvhC24Nsi = "KVH C24 NSi";
    public const string KvhC24Si = "KVH C24 Si";
    public const string BshGl24h = "BSH GL24h";

    private static readonly IReadOnlyList<TimberMaterialCatalogItem> CatalogItems =
    [
        new(SpruceC24, "Material_Catalog_SpruceC24"),
        new(SpruceC16, "Material_Catalog_SpruceC16"),
        new(LarchC30, "Material_Catalog_LarchC30"),
        new(KvhC24Nsi, "Material_Catalog_KvhC24Nsi"),
        new(KvhC24Si, "Material_Catalog_KvhC24Si"),
        new(BshGl24h, "Material_Catalog_BshGl24h"),
    ];

    public static IReadOnlyList<TimberMaterialCatalogItem> Items => CatalogItems;

    public static bool TryGetItem(
        string? storedValue,
        out TimberMaterialCatalogItem? item)
    {
        item = CatalogItems.FirstOrDefault(candidate => string.Equals(
            candidate.StoredValue,
            storedValue,
            StringComparison.Ordinal));
        return item is not null;
    }
}
