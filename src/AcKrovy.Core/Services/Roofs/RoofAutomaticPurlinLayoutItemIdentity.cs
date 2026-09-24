namespace AcKrovy.Core.Services.Roofs;

/// <summary>Creation and canonical validation for opaque purlin layout-row IDs.</summary>
public static class RoofAutomaticPurlinLayoutItemIdentity
{
    public static string Create() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reserved opaque id for the single WallPlate placement configuration row.
    /// It is never mixed into IntermediateItems.
    /// </summary>
    public const string WallPlatePlacementId = "00000000000000000000000000000001";

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Guid.TryParseExact(value, "N", out var id))
        {
            return false;
        }

        normalized = id.ToString("N");
        return true;
    }
}
