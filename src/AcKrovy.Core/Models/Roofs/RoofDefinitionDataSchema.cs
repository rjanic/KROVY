namespace AcKrovy.Core.Models.Roofs;

/// <summary>Version of the dedicated persisted roof-definition payload.</summary>
public static class RoofDefinitionDataSchema
{
    public const int LegacyAbsoluteVersion = 1;
    public const int TopologyVersion = 2;
    public const int HybridLifecycleVersion = 3;
    public const int DualSlopeVersion = 4;
    public const int CurrentVersion = 5;
}
