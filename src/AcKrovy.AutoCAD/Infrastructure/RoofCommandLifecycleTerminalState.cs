using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Command-scoped terminal outcomes for roof lifecycle handling.  This is runtime
/// state only: it is cleared at every native command boundary and is never persisted
/// to the DWG.
/// </summary>
internal static class RoofCommandLifecycleTerminalState
{
    private static readonly HashSet<ObjectId> HandledOwners = new();
    private static readonly HashSet<ObjectId> TerminalOwners = new();

    internal static HashSet<ObjectId> Owners => HandledOwners;

    internal static void BeginCommand()
    {
        HandledOwners.Clear();
        TerminalOwners.Clear();
    }

    internal static void EndCommand()
    {
        HandledOwners.Clear();
        TerminalOwners.Clear();
    }

    internal static bool IsHandled(ObjectId ownerId) =>
        !ownerId.IsNull && TerminalOwners.Contains(ownerId);

    internal static void MarkHandled(ObjectId ownerId)
    {
        if (!ownerId.IsNull)
            TerminalOwners.Add(ownerId);
    }
}
