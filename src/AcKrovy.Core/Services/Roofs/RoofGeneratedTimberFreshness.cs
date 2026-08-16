namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Narrow Stage 6 freshness check: generated rafter layout signatures embed the
/// roof geometry signature and become stale after a supported roof resize.
/// </summary>
public static class RoofGeneratedTimberFreshness
{
    private const string LayoutPrefix = "RAFTER_LAYOUT_V1;";

    public static bool IsLayoutCurrent(string? layoutSignature, string? geometrySignature)
    {
        if (string.IsNullOrWhiteSpace(layoutSignature) ||
            string.IsNullOrWhiteSpace(geometrySignature))
        {
            return false;
        }

        return layoutSignature!.StartsWith(
            LayoutPrefix + geometrySignature + ";",
            StringComparison.Ordinal);
    }
}
