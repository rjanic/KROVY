using System.Security.Cryptography;
using System.Text;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Compact deterministic layout identity for persisted generated-timber metadata.
/// Full canonical layout signatures remain in-memory for Core/diagnostics; only the
/// fingerprint is written into schema-1 <c>LayoutSignature</c> XData.
/// </summary>
public static class RoofGeneratedLayoutFingerprint
{
    public const string Prefix = "RF2-SHA256:";
    public const int HexLength = 64;
    public const int PersistedIdentityLength = 11 + HexLength;

    public static string Compute(string fullCanonicalSignature)
    {
        if (string.IsNullOrEmpty(fullCanonicalSignature))
        {
            throw new ArgumentException(
                "Canonical layout signature is required.",
                nameof(fullCanonicalSignature));
        }

        var bytes = Encoding.UTF8.GetBytes(fullCanonicalSignature);
        byte[] hash;
        using (var sha = SHA256.Create())
        {
            hash = sha.ComputeHash(bytes);
        }

        return Prefix + ToLowerHex(hash);
    }

    public static bool IsFingerprint(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value!.StartsWith(Prefix, StringComparison.Ordinal) &&
        value.Length == PersistedIdentityLength &&
        IsLowerHex(value, Prefix.Length);

    /// <summary>
    /// Value written into schema-1 generated metadata. Always compact.
    /// </summary>
    public static string ToPersistedIdentity(string fullCanonicalSignature) =>
        Compute(fullCanonicalSignature);

    /// <summary>
    /// Unified comparison for persisted schema-1 layout identity vs current full signature.
    /// Supports new fingerprints and legacy verbose full signatures without migration-on-open.
    /// </summary>
    public static bool MatchesPersistedLayoutIdentity(
        string? persistedValue,
        string? currentFullCanonicalSignature)
    {
        if (string.IsNullOrWhiteSpace(persistedValue) ||
            string.IsNullOrWhiteSpace(currentFullCanonicalSignature))
        {
            return false;
        }

        if (IsFingerprint(persistedValue))
        {
            return string.Equals(
                persistedValue,
                Compute(currentFullCanonicalSignature!),
                StringComparison.Ordinal);
        }

        // Legacy: exact full-signature equality, or fingerprint(legacy)==fingerprint(current).
        if (string.Equals(
                persistedValue,
                currentFullCanonicalSignature,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            Compute(persistedValue!),
            Compute(currentFullCanonicalSignature!),
            StringComparison.Ordinal);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            chars[index * 2] = ToLowerHexNibble(value >> 4);
            chars[(index * 2) + 1] = ToLowerHexNibble(value & 0xF);
        }

        return new string(chars);
    }

    private static char ToLowerHexNibble(int value) =>
        (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));

    private static bool IsLowerHex(string value, int start)
    {
        for (var index = start; index < value.Length; index++)
        {
            var c = value[index];
            if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
