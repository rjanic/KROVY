namespace AcKrovy.Core.Services.Roofs;

/// <summary>Creation and canonical validation for opaque purlin layout-row IDs.</summary>
public static class RoofAutomaticPurlinLayoutItemIdentity
{
    public static string Create() => Guid.NewGuid().ToString("N");

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
