using System.Security.Cryptography;
using System.Text;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Persistent semantic identity, independent of the current CAD binding.
/// Legacy identities migrate deterministically; reads never mint a random identity.</summary>
public static class RoofAttachedManualIdentityRules
{
    public static string Create() => Guid.NewGuid().ToString("N");

    public static string Resolve(RoofAttachedManualTimberData data)
    {
        if (data.SemanticIdentity is { } identity) return Guid.ParseExact(identity, "N").ToString("N");
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(
            "KROVY.AttachedManual.v4|" + data.RoofOwnerReference.ToUpperInvariant() + "|" + data.ChildIdentity.ToUpperInvariant()));
        return BitConverter.ToString(bytes).Replace("-", string.Empty)
            .Substring(0, 32).ToLowerInvariant();
    }

    public static RoofAttachedManualTimberData Upgrade(RoofAttachedManualTimberData data) =>
        data.AnchorGeneratedMemberKey is null || data.RelativeSegment is null ? data : data with
        {
            SchemaVersion = RoofAttachedManualTimberDataSchema.CurrentVersion,
            SemanticIdentity = Resolve(data),
        };

    public static string PhysicalKey(RoofAttachedManualTimberData data) =>
        "AttachedManual:" + Resolve(data);
}
