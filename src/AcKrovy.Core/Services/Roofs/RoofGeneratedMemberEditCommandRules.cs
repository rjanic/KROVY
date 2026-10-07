using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Native commands that participate in generated-timber lock/override evaluation.
/// Classic STRETCH is accepted when Unlocked and the final Line is representable.
/// </summary>
public static class RoofGeneratedMemberEditCommandRules
{
    public static bool IsAssemblySnapshotCommand(string? globalCommandName) =>
        IsGeneratedTimberEditCommand(globalCommandName) || IsJoinCommand(globalCommandName) || IsMirrorCommand(globalCommandName) ||
        LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName);

    public static bool IsGeneratedTimberEditCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) ||
               IsLengthenCommand(normalized) ||
               normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase) ||
               IsBreakCommand(normalized) ||
               normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("SCALE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedUnlockedGeneratedTimberCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("TRIM", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("EXTEND", StringComparison.OrdinalIgnoreCase) ||
               IsBreakCommand(normalized) ||
               normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ERASE", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Narrows the shared generated-member edit vocabulary for roof kinds that do not
    /// yet support AttachedManual split children. All geometry-override operations stay
    /// shared; Monopitch BREAK remains out of scope until its AttachedManual lifecycle is
    /// implemented.
    /// </summary>
    public static bool IsSupportedUnlockedGeneratedTimberCommand(
        string? globalCommandName,
        RoofKind roofKind)
    {
        _ = roofKind;
        return IsSupportedUnlockedGeneratedTimberCommand(globalCommandName);
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

    public static bool IsLengthenCommand(string? globalCommandName) =>
        LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
            .Equals("LENGTHEN", StringComparison.OrdinalIgnoreCase);

    public static bool IsJoinCommand(string? globalCommandName) =>
        LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
            .Equals("JOIN", StringComparison.OrdinalIgnoreCase);

    public static bool IsGripStretchCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Plan2D Ordinary edits share snapshot/build-state infrastructure across native commands.</summary>
    public static bool IsOrdinaryPlanGeometryEditCommand(string? globalCommandName) =>
        IsClassicStretch(globalCommandName) || IsGripStretchCommand(globalCommandName) ||
        IsTrimCommand(globalCommandName) || IsExtendCommand(globalCommandName) || IsBreakCommand(globalCommandName) ||
        IsRotateCommand(globalCommandName) || IsLengthenCommand(globalCommandName);

    public static bool IsBreakCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        // AutoCAD's one-point command has its own GlobalCommandName. Both commands
        // use the existing snapshot, split promotion and physical reconciliation.
        return normalized.Equals("BREAK", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BREAKATPOINT", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsScaleCommand(string? globalCommandName)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        return normalized.Equals("SCALE", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Accepted geometry edits share the transaction-scoped physical reconcile.
    /// BREAK selects the same reconciliation engine with cardinality changes;
    /// native COPY/MIRROR use their existing command-scoped ownership branch.
    /// </summary>
    public static bool RequiresOrdinaryPhysicalReconcile(string? globalCommandName) =>
        IsMoveCommand(globalCommandName) || IsEndpointTrimOrExtendCommand(globalCommandName) ||
        IsClassicStretch(globalCommandName) || IsGripStretchCommand(globalCommandName) ||
        IsBreakCommand(globalCommandName) || IsRotateCommand(globalCommandName);
}
