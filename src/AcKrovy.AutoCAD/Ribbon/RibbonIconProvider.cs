using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AcKrovy.AutoCAD.Ribbon;

/// <summary>
/// Načítava 16×16 a 32×32 PNG ikonky z priečinka vedľa doplnku.
/// Súbory sa kopírujú do výstupu buildu, aby fungovali aj po NETLOAD
/// a neskôr v .bundle inštalácii.
/// </summary>
internal static class RibbonIconProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string iconKey, int size)
    {
        var cacheKey = $"{iconKey}:{size}";
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            Cache[cacheKey] = null;
            return null;
        }

        var imagePath = Path.Combine(assemblyDirectory, "Resources", "Icons", $"ak_{iconKey}_{size}.png");
        if (!File.Exists(imagePath))
        {
            Cache[cacheKey] = null;
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new System.Uri(imagePath, System.UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        Cache[cacheKey] = image;
        return image;
    }
}
