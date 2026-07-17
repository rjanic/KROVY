using AcKrovy.Core.Models;

namespace AcKrovy.AutoCAD.Settings;

/// <summary>
/// Používateľské pravidlá, ktoré určujú, na akú hladinu a farbu sa zaradí
/// inteligentný prvok ACAD KROVY. Nastavenia sa ukladajú lokálne pre Windows
/// účet; samotná hladina a jej farba sa vytvárajú/prenášajú priamo do DWG.
/// </summary>
internal sealed class ElementLayerProfile
{
    public int Version { get; set; } = 1;
    public List<ElementLayerStyle> Styles { get; set; } = [];

    public ElementLayerStyle GetStyle(TimberElementType type)
    {
        var stored = Styles.FirstOrDefault(style => style.ElementType == type);
        if (stored is not null)
        {
            return stored;
        }

        return CreateDefault().Styles.First(style => style.ElementType == type);
    }

    public ElementLayerProfile Normalize()
    {
        var defaults = CreateDefault();
        return new ElementLayerProfile
        {
            Version = Version <= 0 ? 1 : Version,
            Styles = Enum
                .GetValues<TimberElementType>()
                .Select(type =>
                {
                    var fallback = defaults.GetStyle(type);
                    var stored = Styles.FirstOrDefault(style => style.ElementType == type);

                    return new ElementLayerStyle
                    {
                        ElementType = type,
                        LayerName = string.IsNullOrWhiteSpace(stored?.LayerName)
                            ? fallback.LayerName
                            : stored.LayerName.Trim(),
                        ColorIndex = stored is { ColorIndex: >= 1 and <= 255 }
                            ? stored.ColorIndex
                            : fallback.ColorIndex,
                    };
                })
                .ToList(),
        };
    }

    public static ElementLayerProfile CreateDefault() => new()
    {
        Styles =
        [
            new ElementLayerStyle(TimberElementType.Rafter, "KROKVA", 2),
            new ElementLayerStyle(TimberElementType.WallPlate, "POMURNICA", 30),
            new ElementLayerStyle(TimberElementType.Purlin, "VAZNICA", 4),
            new ElementLayerStyle(TimberElementType.Post, "STLPIK", 3),
            new ElementLayerStyle(TimberElementType.CollarTie, "KLIESTINA", 5),
            new ElementLayerStyle(TimberElementType.Brace, "VZPERA", 1),
            new ElementLayerStyle(TimberElementType.TieBeam, "VAZNY_TRAM", 6),
        ],
    };
}

internal sealed class ElementLayerStyle
{
    public ElementLayerStyle()
    {
    }

    public ElementLayerStyle(TimberElementType elementType, string layerName, int colorIndex)
    {
        ElementType = elementType;
        LayerName = layerName;
        ColorIndex = colorIndex;
    }

    public TimberElementType ElementType { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
}
