namespace AcKrovy.Cad.Abstractions.Layers;

public static class CadLinetypeNames
{
    public const string Continuous = "Continuous";
    public const string DashDot = "DASHDOT";

    public static IReadOnlyList<string> SupportedStandardNames { get; } =
        new[] { Continuous, DashDot };

    public static string Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? Continuous : name!.Trim();

    public static bool IsSupportedStandard(string? name) =>
        SupportedStandardNames.Any(candidate =>
            string.Equals(candidate, Normalize(name), StringComparison.OrdinalIgnoreCase));
}
