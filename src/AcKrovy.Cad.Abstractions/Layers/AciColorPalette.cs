namespace AcKrovy.Cad.Abstractions.Layers;

/// <summary>
/// Deterministic CAD-neutral preview representation of AutoCAD Color Index 1–255.
/// The persisted value remains the ACI index; RGB is presentation-only.
/// </summary>
public static class AciColorPalette
{
    private static readonly AciRgb[] HueBases =
    [
        new(255, 0, 0),
        new(255, 63, 0),
        new(255, 127, 0),
        new(255, 191, 0),
        new(255, 255, 0),
        new(191, 255, 0),
        new(127, 255, 0),
        new(63, 255, 0),
        new(0, 255, 0),
        new(0, 255, 63),
        new(0, 255, 127),
        new(0, 255, 191),
        new(0, 255, 255),
        new(0, 191, 255),
        new(0, 127, 255),
        new(0, 63, 255),
        new(0, 0, 255),
        new(63, 0, 255),
        new(127, 0, 255),
        new(191, 0, 255),
        new(255, 0, 255),
        new(255, 0, 191),
        new(255, 0, 127),
        new(255, 0, 63),
    ];

    private static readonly double[] ValueFactors = [1d, 165d / 255d, 127d / 255d, 76d / 255d, 38d / 255d];
    private static readonly byte[] Grays = [51, 80, 105, 130, 190, 255];

    public static IReadOnlyList<int> Indices { get; } =
        Enumerable.Range(1, 255).ToArray();

    public static bool IsLayerColorIndex(int index) => index is >= 1 and <= 255;

    public static bool TryGetRgb(int index, out AciRgb rgb)
    {
        if (!IsLayerColorIndex(index))
        {
            rgb = default;
            return false;
        }

        rgb = GetRgb(index);
        return true;
    }

    public static AciRgb GetRgb(int index)
    {
        if (!IsLayerColorIndex(index))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Layer ACI index must be from 1 to 255.");
        }

        return index switch
        {
            1 => new AciRgb(255, 0, 0),
            2 => new AciRgb(255, 255, 0),
            3 => new AciRgb(0, 255, 0),
            4 => new AciRgb(0, 255, 255),
            5 => new AciRgb(0, 0, 255),
            6 => new AciRgb(255, 0, 255),
            7 => new AciRgb(255, 255, 255),
            8 => new AciRgb(128, 128, 128),
            9 => new AciRgb(192, 192, 192),
            >= 250 => Gray(index),
            _ => Hue(index),
        };
    }

    private static AciRgb Gray(int index)
    {
        var value = Grays[index - 250];
        return new AciRgb(value, value, value);
    }

    private static AciRgb Hue(int index)
    {
        var hueIndex = (index - 10) / 10;
        var shade = (index - 10) % 10;
        var baseColor = HueBases[hueIndex];
        var factor = ValueFactors[shade / 2];
        var red = Scale(baseColor.Red, factor);
        var green = Scale(baseColor.Green, factor);
        var blue = Scale(baseColor.Blue, factor);
        if (shade % 2 == 1)
        {
            var maximum = Scale(255, factor);
            red = Tint(red, maximum);
            green = Tint(green, maximum);
            blue = Tint(blue, maximum);
        }

        return new AciRgb(red, green, blue);
    }

    private static byte Scale(byte value, double factor) =>
        checked((byte)Math.Round(value * factor, MidpointRounding.AwayFromZero));

    private static byte Tint(byte value, byte maximum) =>
        checked((byte)Math.Round((value + maximum) / 2d, MidpointRounding.AwayFromZero));
}

public readonly struct AciRgb : IEquatable<AciRgb>
{
    public AciRgb(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public bool Equals(AciRgb other) =>
        Red == other.Red && Green == other.Green && Blue == other.Blue;

    public override bool Equals(object? obj) => obj is AciRgb other && Equals(other);

    public override int GetHashCode() => (Red << 16) | (Green << 8) | Blue;
}
