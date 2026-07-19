namespace AcKrovy.Core.Models;

public sealed class TimberElementDefaultProfile
{
    public const double FactoryCuttingAllowanceMm = 100d;
    public const double MaxCuttingAllowanceMm = 10000d;

    public int Version { get; set; } = 1;
    public List<TimberElementDefaultStyle> Styles { get; set; } = new();

    public double GetCuttingAllowanceMm(TimberElementType type)
    {
        var stored = Styles.FirstOrDefault(style => style.ElementType == type);
        return stored is null
            ? GetFactoryCuttingAllowanceMm(type)
            : NormalizeCuttingAllowanceMm(stored.CuttingAllowanceMm);
    }

    public static double GetFactoryCuttingAllowanceMm(TimberElementType type) =>
        type switch
        {
            TimberElementType.Purlin => 200d,
            _ => FactoryCuttingAllowanceMm,
        };

    public TimberElementDefaultProfile Normalize()
    {
        return new TimberElementDefaultProfile
        {
            Version = Version <= 0 ? 1 : Version,
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type => new TimberElementDefaultStyle(type, GetCuttingAllowanceMm(type)))
                .ToList(),
        };
    }

    public static TimberElementDefaultProfile CreateDefault() => new()
    {
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type => new TimberElementDefaultStyle(type, GetFactoryCuttingAllowanceMm(type)))
                .ToList(),
    };

    private static double NormalizeCuttingAllowanceMm(double value) =>
        Math.Min(MaxCuttingAllowanceMm, Math.Max(0, value));
}

public sealed class TimberElementDefaultStyle
{
    public TimberElementDefaultStyle()
    {
    }

    public TimberElementDefaultStyle(TimberElementType elementType, double cuttingAllowanceMm)
    {
        ElementType = elementType;
        CuttingAllowanceMm = cuttingAllowanceMm;
    }

    public TimberElementType ElementType { get; set; }
    public double CuttingAllowanceMm { get; set; }
}
