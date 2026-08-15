using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Maintains a recoverable AutoCAD GROUP as a convenience layer for one roof.</summary>
internal static class RoofDisplayGroupService
{
    internal const string GroupNamePrefix = "AK_ROOF_";
    internal const int ExpectedMemberCount = 8;

    public static RoofDisplayGroupInspection Inspect(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> childIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (childIds.Count != ExpectedMemberCount - 1)
        {
            return RoofDisplayGroupInspection.MissingOrDamaged;
        }

        if (!TryGetExistingGroupDictionary(
                database,
                transaction,
                OpenMode.ForRead,
                out var dictionary) ||
            dictionary is null)
        {
            return RoofDisplayGroupInspection.MissingOrDamaged;
        }

        var name = BuildGroupName(transaction, ownerId);
        if (!dictionary.Contains(name) ||
            transaction.GetObject(dictionary.GetAt(name), OpenMode.ForRead) is not Group group)
        {
            return RoofDisplayGroupInspection.MissingOrDamaged;
        }

        var expected = new HashSet<ObjectId>(childIds) { ownerId };
        var actual = group.GetAllEntityIds();
        return group.Selectable &&
               actual.Length == ExpectedMemberCount &&
               actual.Distinct().Count() == ExpectedMemberCount &&
               expected.SetEquals(actual)
            ? new RoofDisplayGroupInspection(true, name)
            : new RoofDisplayGroupInspection(false, name);
    }

    public static void EnsureGroup(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> childIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (childIds.Count != ExpectedMemberCount - 1 ||
            childIds.Distinct().Count() != ExpectedMemberCount - 1 ||
            childIds.Contains(ownerId))
        {
            throw new ArgumentException("A roof group requires one owner and seven unique display children.", nameof(childIds));
        }

        var dictionary = (DBDictionary)transaction.GetObject(
            database.GroupDictionaryId,
            OpenMode.ForRead);
        var name = BuildGroupName(transaction, ownerId);
        Group group;
        if (dictionary.Contains(name))
        {
            group = (Group)transaction.GetObject(dictionary.GetAt(name), OpenMode.ForWrite);
            group.Clear();
            group.Selectable = true;
        }
        else
        {
            dictionary.UpgradeOpen();
            group = new Group(string.Empty, selectable: true);
            dictionary.SetAt(name, group);
            transaction.AddNewlyCreatedDBObject(group, true);
        }

        group.Append(ownerId);
        foreach (var childId in childIds)
        {
            group.Append(childId);
        }
    }

    public static void CreateGroupFromExistingValidatedDisplay(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> childIds) =>
        EnsureGroup(database, transaction, ownerId, childIds);

    public static bool TryResolveLegacyCopiedOwner(
        Database database,
        Transaction transaction,
        ObjectId selectedChildId,
        string storedOwnerReference,
        out ObjectId copiedOwnerId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        copiedOwnerId = ObjectId.Null;
        if (selectedChildId.IsNull || string.IsNullOrWhiteSpace(storedOwnerReference))
        {
            return false;
        }

        if (!TryGetExistingGroupDictionary(
                database,
                transaction,
                OpenMode.ForRead,
                out var dictionary) ||
            dictionary is null)
        {
            return false;
        }

        var candidates = new HashSet<ObjectId>();
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group ||
                !group.Selectable)
            {
                continue;
            }

            var members = group.GetAllEntityIds();
            if (members.Length != ExpectedMemberCount ||
                members.Distinct().Count() != ExpectedMemberCount ||
                !members.Contains(selectedChildId) ||
                !TryInspectLegacyCopiedGroup(
                    database,
                    transaction,
                    members,
                    storedOwnerReference,
                    out var candidateOwnerId))
            {
                continue;
            }

            candidates.Add(candidateOwnerId);
        }

        if (candidates.Count != 1)
        {
            return false;
        }

        copiedOwnerId = candidates.Single();
        return true;
    }

    private static bool TryInspectLegacyCopiedGroup(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> members,
        string storedOwnerReference,
        out ObjectId copiedOwnerId)
    {
        copiedOwnerId = ObjectId.Null;
        var displayRoles = new HashSet<RoofDisplayEdgeRole>();
        string? generationSignature = null;
        foreach (var memberId in members)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    memberId,
                    OpenMode.ForRead,
                    out var member,
                    database) || member is null)
            {
                return false;
            }

            if (member is Polyline)
            {
                if (!copiedOwnerId.IsNull || RoofDefinitionStore.Read(member).Data is null)
                {
                    return false;
                }

                copiedOwnerId = memberId;
                continue;
            }

            if (member is not Line)
            {
                return false;
            }

            var display = RoofDisplayStore.Read(member);
            if (display.Data is null ||
                !string.Equals(
                    display.Data.OwnerReference,
                    storedOwnerReference,
                    StringComparison.OrdinalIgnoreCase) ||
                !displayRoles.Add(display.Data.Role))
            {
                return false;
            }

            generationSignature ??= display.Data.GenerationSignature;
            if (!string.Equals(
                    generationSignature,
                    display.Data.GenerationSignature,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (copiedOwnerId.IsNull || displayRoles.Count != ExpectedMemberCount - 1)
        {
            return false;
        }

        var copiedOwnerReference = ((Entity)transaction.GetObject(
            copiedOwnerId,
            OpenMode.ForRead)).Handle.ToString();
        return !string.Equals(
            copiedOwnerReference,
            storedOwnerReference,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetExistingGroupDictionary(
        Database database,
        Transaction transaction,
        OpenMode mode,
        out DBDictionary? dictionary)
    {
        dictionary = null;
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains("ACAD_GROUP"))
        {
            return false;
        }

        dictionary = (DBDictionary)transaction.GetObject(
            namedObjects.GetAt("ACAD_GROUP"),
            mode);
        return true;
    }

    private static string BuildGroupName(Transaction transaction, ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner) || owner is null)
        {
            throw new InvalidOperationException("Roof group owner is unavailable.");
        }

        return GroupNamePrefix + owner.Handle.ToString().ToUpperInvariant();
    }
}

internal sealed record RoofDisplayGroupInspection(bool IsCurrent, string? GroupName)
{
    public static RoofDisplayGroupInspection MissingOrDamaged { get; } = new(false, null);
}
