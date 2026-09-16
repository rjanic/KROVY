using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Shared lock-protection scope for StructuralGenerated Hip/Valley members.
/// Ordinary generated rafters use a different store; these roles must follow the
/// same locked-roof restore policy once ownership is resolved.
/// </summary>
public static class RoofStructuralGeneratedLockRules
{
    public static bool IsLockProtectedRole(RoofStructuralRole role) =>
        role is RoofStructuralRole.Hip or RoofStructuralRole.Valley;
}
