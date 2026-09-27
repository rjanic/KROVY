using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Document/command-local provenance. No DB writes, no persisted ObjectIds.</summary>
internal sealed class RoofNativeCloneSnapshot
{
    internal sealed record Owner(ObjectId Id, string Handle, ObjectId[] Required, ObjectId[] Disposable);
    internal sealed record Clone(Owner Source, ObjectId OwnerId, ObjectId[] Disposable,
        IReadOnlyDictionary<ObjectId, ObjectId> Mapping);
    private readonly HashSet<ObjectId> _preExisting = new();
    private readonly List<Owner> _owners = new();
    private readonly List<Dictionary<ObjectId, ObjectId>> _mappings = new();

    public void Clear()
    {
        _preExisting.Clear();
        _owners.Clear();
        _mappings.Clear();
    }

    public void Capture(Database database)
    {
        Clear();
        using var transaction = database.TransactionManager.StartTransaction();
        var model = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
        var children = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
        var disposable = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
        var owners = new List<(ObjectId Id, string Handle)>();
        foreach (ObjectId id in model)
        {
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            _preExisting.Add(id);
            if (entity is Polyline && RoofDefinitionStore.Read(entity).Data is not null)
                owners.Add((id, entity.Handle.ToString()));
            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
            var display = RoofDisplayStore.Read(entity);
            var reference = physical?.RoofOwnerReference ?? display.OwnerReference;
            if (!string.IsNullOrWhiteSpace(reference)) Add(disposable, reference, id);
            // Physical children can be omitted by native group COPY; regenerate them
            // canonically. The complete plan/timber assembly still distinguishes full
            // roof copies from source-only and partial member copies.
            var requiredOwner = display.OwnerReference ??
                RoofGeneratedTimberStore.Read(entity).Data?.RoofOwnerReference ??
                RoofStructuralGeneratedStore.Read(entity).Data?.RoofOwnerReference ??
                RoofAttachedManualTimberStore.Read(entity).Data?.RoofOwnerReference;
            if (!string.IsNullOrWhiteSpace(requiredOwner)) Add(children, requiredOwner, id);
        }
        foreach (var owner in owners)
            _owners.Add(new Owner(owner.Id, owner.Handle,
                children.GetValueOrDefault(owner.Handle)?.ToArray() ?? [],
                disposable.GetValueOrDefault(owner.Handle)?.ToArray() ?? []));
    }

    public void Observe(IdMapping mapping)
    {
        if (_owners.Count == 0) return;
        var batch = new Dictionary<ObjectId, ObjectId>();
        foreach (IdPair pair in mapping)
            if (pair.IsCloned && !pair.Key.IsNull && !pair.Value.IsNull)
                batch[pair.Key] = pair.Value;
        _mappings.Add(batch);
    }

    public IReadOnlyList<Clone> GetCompleteClones()
    {
        var result = new List<Clone>();
        foreach (var map in _mappings)
        foreach (var owner in _owners)
        {
            if (!RoofNativeCloneOwnershipRules.TryGetDisposableClones(owner.Id, owner.Required,
                    owner.Disposable, map, _preExisting, out var disposable)) continue;
            if (result.Any(clone => clone.OwnerId == map[owner.Id])) continue;
            var scoped = owner.Required.Concat(owner.Disposable).Append(owner.Id).Distinct()
                .Where(map.ContainsKey).ToDictionary(id => id, id => map[id]);
            if (scoped.Values.Distinct().Count() != scoped.Count) continue;
            result.Add(new Clone(owner, map[owner.Id], disposable.ToArray(), scoped));
        }
        return result;
    }

    private static void Add(Dictionary<string, List<ObjectId>> target, string owner, ObjectId id)
    {
        if (!target.TryGetValue(owner, out var ids)) target[owner] = ids = new List<ObjectId>();
        ids.Add(id);
    }
}
