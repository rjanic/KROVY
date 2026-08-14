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

        var dictionary = (DBDictionary)transaction.GetObject(
            database.GroupDictionaryId,
            OpenMode.ForRead);
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
