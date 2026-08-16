using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
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

        // Native GROUP COPY often leaves a second AutoCAD group that still
        // contains the same semantic source. Canonical membership must be unique.
        DissociateOwnerFromForeignGroups(database, transaction, ownerId, name);
    }

    /// <summary>
    /// Removes the roof owner from every non-canonical GROUP and dissolves
    /// foreign groups that only held roof display topology for that owner.
    /// Prevents 2→3 group growth after display-tamper Rebuild.
    /// </summary>
    public static void DissociateOwnerFromForeignGroups(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string canonicalGroupName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (ownerId.IsNull || string.IsNullOrWhiteSpace(canonicalGroupName))
        {
            return;
        }

        if (!TryGetExistingGroupDictionary(
                database,
                transaction,
                OpenMode.ForWrite,
                out var dictionary) ||
            dictionary is null)
        {
            return;
        }

        var dissolveNames = new List<string>();
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (string.Equals(
                    entry.Key,
                    canonicalGroupName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group foreign)
            {
                continue;
            }

            var members = foreign.GetAllEntityIds();
            if (!members.Contains(ownerId))
            {
                continue;
            }

            foreign.UpgradeOpen();
            if (IsRoofOnlyGroupTopology(database, transaction, ownerId, members))
            {
                foreign.Clear();
                dissolveNames.Add(entry.Key);
            }
            else
            {
                foreign.Remove(ownerId);
            }
        }

        foreach (var dissolveName in dissolveNames)
        {
            if (dictionary.Contains(dissolveName))
            {
                dictionary.Remove(dissolveName);
            }
        }
    }

    private static bool IsRoofOnlyGroupTopology(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> members)
    {
        foreach (var memberId in members)
        {
            if (memberId == ownerId)
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    memberId,
                    OpenMode.ForRead,
                    out var member,
                    database) ||
                member is null)
            {
                // Erased / unavailable members are fine for dissolve.
                continue;
            }

            if (member is not Line || !RoofDisplayStore.Read(member).Exists)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Counts AutoCAD groups that contain the roof source and whether any of them
    /// is an exact 8-member roof topology (1 Polyline + 7 RoofDisplay Lines).
    /// </summary>
    public static bool TryCountRoofGroupsContainingOwner(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out int groupCount,
        out bool hasExactEightMemberRoofGroup)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        groupCount = 0;
        hasExactEightMemberRoofGroup = false;
        if (ownerId.IsNull)
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

        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group)
            {
                continue;
            }

            var members = group.GetAllEntityIds();
            if (!members.Contains(ownerId))
            {
                continue;
            }

            groupCount++;
            if (members.Length == ExpectedMemberCount &&
                members.Distinct().Count() == ExpectedMemberCount &&
                IsRoofOnlyGroupTopology(database, transaction, ownerId, members))
            {
                hasExactEightMemberRoofGroup = true;
            }
        }

        return groupCount > 0;
    }

    /// <summary>
    /// Strict structural erase candidates: GROUP that contains the exact semantic
    /// source plus seven RoofDisplay Lines with all required roles. Used only to
    /// erase stale copied display cache — not for semantic ownership adoption.
    /// Owner JSON / 1005 may be stale and are ignored for eligibility.
    /// </summary>
    public static bool TryCollectStrictStructuralDisplayEraseIds(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out IReadOnlyList<ObjectId> displayIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        displayIds = Array.Empty<ObjectId>();
        if (ownerId.IsNull)
        {
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null ||
            RoofDefinitionStore.Read(owner).Data is null)
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

        var candidateSets = new List<IReadOnlyList<string>>();
        var keyToId = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group)
            {
                continue;
            }

            var members = group.GetAllEntityIds();
            if (!members.Contains(ownerId))
            {
                continue;
            }

            if (!TryBuildForeignGroupObservations(
                    database,
                    transaction,
                    ownerId,
                    members,
                    keyToId,
                    out var observations))
            {
                continue;
            }

            if (!RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                    sourceHasValidRoofDefinition: true,
                    observations,
                    out var eraseKeys))
            {
                continue;
            }

            candidateSets.Add(eraseKeys);
        }

        if (!RoofDisplayForeignGroupEraseRules.TryResolveUniqueEraseMemberKeys(
                candidateSets,
                out var uniqueKeys))
        {
            return false;
        }

        var resolved = new List<ObjectId>(uniqueKeys.Count);
        foreach (var key in uniqueKeys)
        {
            if (!keyToId.TryGetValue(key, out var id) || id.IsNull)
            {
                return false;
            }

            resolved.Add(id);
        }

        displayIds = resolved;
        return displayIds.Count == ExpectedMemberCount - 1;
    }

    private static bool TryBuildForeignGroupObservations(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> members,
        Dictionary<string, ObjectId> keyToId,
        out List<RoofDisplayForeignGroupMemberObservation> observations)
    {
        observations = new List<RoofDisplayForeignGroupMemberObservation>(members.Count);
        foreach (var memberId in members)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    memberId,
                    OpenMode.ForRead,
                    out var member,
                    database) ||
                member is null)
            {
                return false;
            }

            var key = memberId.Handle.ToString();
            keyToId[key] = memberId;
            if (memberId == ownerId)
            {
                if (member is not Polyline ||
                    RoofDefinitionStore.Read(member).Data is null)
                {
                    return false;
                }

                observations.Add(new RoofDisplayForeignGroupMemberObservation(
                    key,
                    RoofDisplayForeignGroupMemberKind.SourcePolyline,
                    HasReadableRoofDisplayMetadata: false,
                    SchemaSupported: false,
                    Role: null));
                continue;
            }

            if (member is not Line)
            {
                observations.Add(new RoofDisplayForeignGroupMemberObservation(
                    key,
                    RoofDisplayForeignGroupMemberKind.Other,
                    false,
                    false,
                    null));
                continue;
            }

            var stored = RoofDisplayStore.Read(member);
            var schemaSupported =
                stored.Data is not null &&
                stored.Error == RoofDisplayDataDecodeError.None &&
                stored.Data.SchemaVersion > 0 &&
                stored.Data.SchemaVersion <= RoofDisplayDataSchema.CurrentVersion;
            observations.Add(new RoofDisplayForeignGroupMemberObservation(
                key,
                RoofDisplayForeignGroupMemberKind.DisplayLine,
                HasReadableRoofDisplayMetadata: stored.Data is not null,
                SchemaSupported: schemaSupported,
                Role: stored.Data?.Role));
        }

        return true;
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
