using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Native commands that participate in generated-timber lock/override evaluation.
/// Classic STRETCH is accepted when Unlocked and the final Line is representable.
/// </summary>
public static class RoofGeneratedMemberEditCommandRules
{
    public static bool IsAssemblySnapshotCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BREAK", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeneratedTimberEditCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BREAK", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedUnlockedGeneratedTimberCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BREAK", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsClassicStretch(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEndpointTrimOrExtendCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrimCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExtendCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMoveCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRotateCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMirrorCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGripStretchCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBreakCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("BREAK", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSplitCommand(string? globalCommandName) =>
        IsTrimCommand(globalCommandName) || IsBreakCommand(globalCommandName);

    public static bool IsEraseCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTargetedRecalcCommand(string? globalCommandName) =>
        IsEndpointTrimOrExtendCommand(globalCommandName) ||
        IsBreakCommand(globalCommandName) ||
        IsMoveCommand(globalCommandName) ||
        IsRotateCommand(globalCommandName) ||
        IsClassicStretch(globalCommandName) ||
        IsGripStretchCommand(globalCommandName);
}
