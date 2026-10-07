using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Models;
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
    internal sealed record Member(LineBinding Binding, RoofGeneratedTimberData? Generated,
        RoofAttachedManualTimberData? Attached, TimberElementData? Timber);
    internal sealed record LineBinding(ObjectId Id, RoofGeneratedMemberGeometry Geometry);
    private readonly Dictionary<ObjectId, Member> _members = new();
    private readonly Dictionary<ObjectId, string> _sourceGeometry = new();
    private readonly HashSet<ObjectId> _physical = new();
    private readonly HashSet<ObjectId> _derivedPlans = new();
    private readonly HashSet<ObjectId> _consumedMemberClones = new();
    public bool IsMemberCloneConsumed(ObjectId id) => _consumedMemberClones.Contains(id);
    public void ConsumeMemberClones(IEnumerable<ObjectId> ids) => _consumedMemberClones.UnionWith(ids);
    public IReadOnlyDictionary<ObjectId, Member> Members => _members;
    public IReadOnlyCollection<ObjectId> PreExistingPhysical => _physical;
    public IReadOnlyList<IReadOnlyDictionary<ObjectId, ObjectId>> GetMappings() => _mappings;
    public IReadOnlyCollection<ObjectId> PreExistingIds => _preExisting;

    public IReadOnlyDictionary<ObjectId, ObjectId> GetMemberSourcesByClone() => _mappings
        .SelectMany(map => map).Where(pair => _members.ContainsKey(pair.Key))
        .GroupBy(pair => pair.Value).Where(group => group.Select(pair => pair.Key).Distinct().Count() == 1)
        .ToDictionary(group => group.Key, group => group.First().Key);

    public IReadOnlyCollection<ObjectId> GetPhysicalClones() => _mappings.SelectMany(map => map)
        .Where(pair => _physical.Contains(pair.Key)).Select(pair => pair.Value).Distinct().ToArray();
    public IReadOnlyCollection<ObjectId> GetDerivedClones() => _mappings.SelectMany(map => map)
        .Where(pair => _physical.Contains(pair.Key) || _derivedPlans.Contains(pair.Key))
        .Select(pair => pair.Value).Distinct().ToArray();
    public bool IsPreExisting(ObjectId id) => _preExisting.Contains(id);

    public bool IsSourceChanged(Polyline owner) => !_sourceGeometry.TryGetValue(owner.ObjectId, out var before) ||
        !string.Equals(before, SourceGeometry(owner), StringComparison.Ordinal);

    private static string SourceGeometry(Polyline owner) =>
        owner.Closed + ":" + owner.Elevation.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
        string.Join("|", Enumerable.Range(0, owner.NumberOfVertices).Select(index =>
            FormattableString.Invariant($"{owner.GetPoint3dAt(index).X:R},{owner.GetPoint3dAt(index).Y:R},{owner.GetPoint3dAt(index).Z:R},{owner.GetBulgeAt(index):R}")));

    public void Clear()
    {
        _preExisting.Clear();
        _owners.Clear();
        _mappings.Clear();
        _members.Clear();
        _sourceGeometry.Clear();
        _physical.Clear();
        _derivedPlans.Clear();
        _consumedMemberClones.Clear();
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
            if (entity is Polyline owner && RoofDefinitionStore.Read(entity).Data is not null)
            {
                owners.Add((id, entity.Handle.ToString()));
                _sourceGeometry[id] = SourceGeometry(owner);
            }
            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
            if (physical is not null) _physical.Add(id);
            var generated = RoofGeneratedTimberStore.Read(entity).Data;
            var attached = RoofAttachedManualTimberStore.Read(entity).Data;
            if (entity is Line line && (generated is not null || attached is not null))
            {
                _ = ElementDataStore.TryRead(line, transaction, out var timber);
                _members[id] = new Member(new LineBinding(id, new RoofGeneratedMemberGeometry(
                    new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                    new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z))), generated, attached, timber);
            }
            var display = RoofDisplayStore.Read(entity);
            if (display.Exists || RoofStructuralGeneratedStore.Read(entity).Data is not null)
                _derivedPlans.Add(id);
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
        if (_preExisting.Count == 0) return;
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
