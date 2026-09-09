namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Freshness for generated rafter sets: compares persisted schema-1 layout identity
/// to the current full canonical layout signature (and, for legacy verbose values,
/// also to geometry-embedded prefixes used before compact fingerprints).
/// </summary>
public static class RoofGeneratedTimberFreshness
{
    private const string LayoutPrefix = "RAFTER_LAYOUT_V1;";
    private const string FaceLayoutPrefix = "ROOF_FACE_RAFTER_LAYOUT_V2;";
    private const string HipGeometryPrefix = "Hip;";

    /// <summary>
    /// Preferred freshness path: persisted identity vs current full layout signature.
    /// </summary>
    public static bool MatchesPersistedLayoutIdentity(
        string? persistedValue,
        string? currentFullCanonicalSignature) =>
        RoofGeneratedLayoutFingerprint.MatchesPersistedLayoutIdentity(
            persistedValue,
            currentFullCanonicalSignature);

    /// <summary>
    /// Legacy geometry-embed check for verbose persisted signatures, plus fingerprint
    /// short-circuit (fingerprints cannot be validated from geometry alone — callers that
    /// only have a geometry signature must recompute the full layout and use
    /// <see cref="MatchesPersistedLayoutIdentity"/>).
    /// </summary>
    public static bool IsLayoutCurrent(string? layoutSignature, string? geometrySignature)
    {
        if (string.IsNullOrWhiteSpace(layoutSignature) ||
            string.IsNullOrWhiteSpace(geometrySignature))
        {
            return false;
        }

        if (RoofGeneratedLayoutFingerprint.IsFingerprint(layoutSignature))
        {
            return false;
        }

        if (layoutSignature!.StartsWith(
                LayoutPrefix + geometrySignature + ";",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (geometrySignature!.StartsWith(HipGeometryPrefix, StringComparison.Ordinal) &&
            layoutSignature.StartsWith(FaceLayoutPrefix, StringComparison.Ordinal))
        {
            var topologySignature = geometrySignature.Substring(HipGeometryPrefix.Length);
            return layoutSignature.StartsWith(
                FaceLayoutPrefix + topologySignature + ";",
                StringComparison.Ordinal);
        }

        return false;
    }
}
