using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public sealed class TimberElementDefaultProfile
{
    public const int CurrentVersion = 3;

    /// <summary>
    /// Last profile version whose <see cref="DefaultAnnotationTextSettings"/>
    /// used one shared text-style name. Still readable without rewriting.
    /// </summary>
    public const int SharedAnnotationTextStyleVersion = 2;

    public const double FactoryCuttingAllowanceMm = 100d;
    public const double FactoryCuttingLengthRoundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm;
    public const double MaxCuttingAllowanceMm = 10000d;
    public const double MaxCuttingLengthRoundingStepMm = 10000d;

    public int Version { get; set; } = CurrentVersion;
    public double CuttingLengthRoundingStepMm { get; set; } = FactoryCuttingLengthRoundingStepMm;
    public TimberAnnotationMode DefaultAnnotationMode { get; set; } = TimberAnnotationMode.FullLabel;
    public ItemNumberLeaderStyle DefaultItemNumberLeaderStyle { get; set; } = ItemNumberLeaderStyle.Plain;
    public int AnnotationScaleDenominator { get; set; } = TimberAnnotationScaleRules.DefaultDenominator;
    public TimberAnnotationTextSettings? DefaultAnnotationTextSettings { get; set; }
    public List<TimberElementDefaultStyle> Styles { get; set; } = new();

    public double GetCuttingLengthRoundingStepMm() =>
        NormalizeCuttingLengthRoundingStepMm(CuttingLengthRoundingStepMm);

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
            Version = Version <= 0 ? CurrentVersion : Version,
            CuttingLengthRoundingStepMm = GetCuttingLengthRoundingStepMm(),
            DefaultAnnotationMode = TimberAnnotationModeRules.Normalize(DefaultAnnotationMode),
            DefaultItemNumberLeaderStyle =
                ItemNumberLeaderStyleRules.Normalize(DefaultItemNumberLeaderStyle),
            AnnotationScaleDenominator = TimberAnnotationScaleRules.NormalizeDenominator(
                AnnotationScaleDenominator),
            DefaultAnnotationTextSettings =
                TimberAnnotationTextSettingsRules.NormalizeStored(
                    DefaultAnnotationTextSettings),
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type => new TimberElementDefaultStyle(type, GetCuttingAllowanceMm(type)))
                .ToList(),
        };
    }

    /// <summary>
    /// Mirrors the metadata rule that a stored version is upgraded only when the
    /// profile is written back, never during a load.
    /// </summary>
    public TimberElementDefaultProfile PrepareForWrite()
    {
        var normalized = Normalize();
        normalized.Version = CurrentVersion;
        return normalized;
    }

    public static TimberElementDefaultProfile CreateDefault() => new()
    {
            DefaultAnnotationMode = TimberAnnotationMode.FullLabel,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            AnnotationScaleDenominator = TimberAnnotationScaleRules.DefaultDenominator,
            DefaultAnnotationTextSettings = TimberAnnotationTextSettingsRules.Default,
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type => new TimberElementDefaultStyle(type, GetFactoryCuttingAllowanceMm(type)))
                .ToList(),
    };

    private static double NormalizeCuttingAllowanceMm(double value) =>
        Math.Min(MaxCuttingAllowanceMm, Math.Max(0, value));

    private static double NormalizeCuttingLengthRoundingStepMm(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return FactoryCuttingLengthRoundingStepMm;
        }

        return Math.Min(MaxCuttingLengthRoundingStepMm, Math.Round(value));
    }
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
