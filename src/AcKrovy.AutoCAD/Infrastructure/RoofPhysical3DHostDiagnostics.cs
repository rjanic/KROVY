#if DEBUG
using System.Globalization;
using System.Text.Json;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Opt-in, read-only HOST evidence. Never repairs ownership or changes a DWG.</summary>
internal static class RoofPhysical3DHostDiagnostics
{
    private static readonly Dictionary<Document, Tracker> Trackers = new();
    private static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        AcApp.DocumentManager.DocumentCreated += Created;
        AcApp.DocumentManager.DocumentToBeDestroyed += Destroyed;
        foreach (Document document in AcApp.DocumentManager) Attach(document);
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        AcApp.DocumentManager.DocumentCreated -= Created;
        AcApp.DocumentManager.DocumentToBeDestroyed -= Destroyed;
        foreach (var tracker in Trackers.Values) tracker.Dispose();
        Trackers.Clear();
    }

    public static void Toggle(Document document)
    {
        Attach(document);
        var tracker = Trackers[document];
        tracker.Enabled = !tracker.Enabled;
        Write(document, $"TRACE enabled={tracker.Enabled} build={typeof(RoofPhysical3DHostDiagnostics).Assembly.GetName().Version}");
    }

    public static void Audit(Document document, string phase = "manual")
    {
        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            Audit(document, transaction, phase);
        }
        catch (System.Exception ex) { Write(document, $"AUDIT_FAILED phase={phase} error={ex.GetType().Name}"); }
    }

    public static void Reconcile(Database database, Transaction transaction, string owner, string phase)
    {
        try
        {
            var tracker = Trackers.Values.FirstOrDefault(item => item.Enabled && item.Document.Database.UnmanagedObject == database.UnmanagedObject);
            if (tracker is not null) Audit(tracker.Document, transaction, $"reconcile-{phase}:owner={owner}");
        }
        catch (System.Exception ex) { AcKrovyDiagnostics.Info("ROOF_3D_AUDIT", $"AUDIT_FAILED phase={phase} error={ex.GetType().Name}"); }
    }

    public static void MaintenanceComplete(Document document, string command)
    {
        if (!Trackers.TryGetValue(document, out var tracker) || !tracker.Enabled ||
            command.ToUpperInvariant() is not ("COPY" or "MIRROR" or "ERASE")) return;
        Audit(document, "CommandEnded-after-maintenance:" + command.ToUpperInvariant());
    }

    public static void OwnerCounts(Database database, Transaction transaction, string owner, string phase)
    {
        var tracker = Trackers.Values.FirstOrDefault(item => item.Enabled && item.Document.Database.UnmanagedObject == database.UnmanagedObject);
        if (tracker is null) return;
        try { OwnerCounts(tracker.Document, transaction, owner, phase); }
        catch (System.Exception ex) { Write(tracker.Document, $"OWNER_COUNTS_FAILED phase={phase} owner={owner} error={ex.GetType().Name}"); }
    }

    public static void GroupMutation(Database database, string owner,
        string operation, string groupId, string memberId, string stage)
    {
        var tracker = Trackers.Values.FirstOrDefault(item => item.Enabled && item.Document.Database.UnmanagedObject == database.UnmanagedObject);
        if (tracker is null) return;
        Write(tracker.Document, $"GROUP_MUTATION owner={owner} operation={operation} groupId={groupId} memberId={memberId} stage={stage}");
    }

    public static void OwnerCounts(Document document, string owner, string phase)
    {
        if (!Trackers.TryGetValue(document, out var tracker) || !tracker.Enabled) return;
        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            OwnerCounts(document, transaction, owner, phase);
        }
        catch (System.Exception ex) { Write(document, $"OWNER_COUNTS_FAILED phase={phase} owner={owner} error={ex.GetType().Name}"); }
    }

    private static void OwnerCounts(Document document, Transaction transaction, string owner, string phase)
    {
        var database = document.Database;
        var children = new List<RoofPhysical3DGeneratedData>();
        var handles = new List<string>();
        var displayCount = 0;
        var sourceLive = false;
        var model = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            if (entity is Polyline && string.Equals(entity.Handle.ToString(), owner, StringComparison.OrdinalIgnoreCase))
                sourceLive = true;
            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
            if (physical is not null && string.Equals(physical.RoofOwnerReference, owner, StringComparison.OrdinalIgnoreCase))
            {
                children.Add(physical);
                handles.Add(entity.Handle.ToString());
            }
            if (string.Equals(RoofDisplayStore.Read(entity).OwnerReference, owner, StringComparison.OrdinalIgnoreCase)) displayCount++;
        }
        Write(document, $"OWNER_COUNTS phase={phase} owner={owner} sourceLive={sourceLive} faces={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.Face)} eaves={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.EaveEdge)} hips={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.HipEdge)} ridges={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.RidgeEdge)} total={children.Count} display={displayCount} uniqueKeys={children.Select(c => (c.Role, c.StructuralId)).Distinct().Count() == children.Count} signatures={string.Join("|", children.Select(c => c.GenerationSignature).Distinct())} handles={string.Join("|", handles)}");
        // Separate dictionary aliases/repeated listings from genuinely distinct groups.
        var dictionary = (DBDictionary)transaction.GetObject(database.GroupDictionaryId, OpenMode.ForRead);
        var listedGroups = new HashSet<ObjectId>();
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group) continue;
            var members = group.GetAllEntityIds();
            // During the first native ERASE the sole source slot can be absent.
            // Its exact canonical dictionary key still identifies the group to audit.
            var canonicalName = entry.Key.Equals("AK_ROOF_" + owner, StringComparison.OrdinalIgnoreCase);
            if (!canonicalName && !members.Any(id => !id.IsNull && string.Equals(id.Handle.ToString(), owner, StringComparison.OrdinalIgnoreCase))) continue;
            var firstListing = listedGroups.Add(entry.Value);
            Write(document, $"OWNER_GROUP phase={phase} owner={owner} name={entry.Key} groupId={entry.Value.Handle} firstListing={firstListing} canonicalName={canonicalName} members={members.Length} uniqueMembers={members.Distinct().Count()} sourceSlots={members.Count(id => !id.IsNull && string.Equals(id.Handle.ToString(), owner, StringComparison.OrdinalIgnoreCase))}");
            Write(document, $"GROUP_MEMBERS phase={phase} owner={owner} groupId={entry.Value.Handle} raw={string.Join("|", members.Select(id => id.IsNull ? "null" : id.Handle.ToString()))} unique={string.Join("|", members.Distinct().Select(id => id.IsNull ? "null" : id.Handle.ToString()))}");
        }
    }

    private static void Audit(Document document, Transaction transaction, string phase)
    {
        Write(document, $"AUDIT_BEGIN phase={phase}");
        var database = document.Database;
        var groups = new Dictionary<ObjectId, List<string>>();
        var dictionary = (DBDictionary)transaction.GetObject(database.GroupDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group) continue;
            foreach (var id in group.GetAllEntityIds())
            {
                if (!groups.TryGetValue(id, out var names)) groups[id] = names = new();
                names.Add(entry.Key);
            }
        }
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ownerReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in model)
        {
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.IsErased) continue;
            var physical = RoofPhysical3DGeneratedStore.Read(entity);
            var display = RoofDisplayStore.Read(entity);
            var definition = entity is Polyline ? RoofDefinitionStore.Read(entity).Data : null;
            var membership = groups.TryGetValue(id, out var names) ? string.Join(",", names) : "-";
            var common = $"phase={phase} child={entity.Handle} nativeType={entity.GetType().Name} dbOwner={entity.OwnerId.Handle} visible={entity.Visible} groups={membership}";
            if (physical.Exists)
            {
                Write(document, $"PHYSICAL {common} error={physical.Error} data={JsonSerializer.Serialize(physical.Data)} geometry={Geometry(entity)} raw={RawPhysical(entity)}");
                if (physical.Data is not null)
                {
                    var key = physical.Data.RoofOwnerReference + ":" + physical.Data.Role;
                    ownerReferences.Add(physical.Data.RoofOwnerReference);
                    counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }
            if (display.Exists)
            {
                if (display.OwnerReference is not null) ownerReferences.Add(display.OwnerReference);
                Write(document, $"DISPLAY {common} data={JsonSerializer.Serialize(display.Data)} owner={display.OwnerReference} translatedHandle={display.OwnerReferenceFromCloneHandle} geometry={Geometry(entity)}");
            }
            if (definition is not null)
            {
                ownerReferences.Add(entity.Handle.ToString());
                Write(document, $"OWNER {common} definition={JsonSerializer.Serialize(definition)} elevation={JsonSerializer.Serialize(RoofPhysicalElevationStore.Read(entity).Data)}");
            }
        }
        foreach (var count in counts.OrderBy(item => item.Key, StringComparer.Ordinal))
            Write(document, $"COUNT phase={phase} ownerRole={count.Key} count={count.Value}");
        if (Trackers.TryGetValue(document, out var tracker))
        {
            if (phase.StartsWith("CommandWillStart:", StringComparison.Ordinal))
                tracker.KnownOwners.UnionWith(ownerReferences);
            // Explicit zeros remain observable even if native ERASE removed the
            // source and every child before CommandEnded maintenance.
            ownerReferences.UnionWith(tracker.KnownOwners);
        }
        foreach (var owner in ownerReferences.OrderBy(value => value, StringComparer.Ordinal))
            OwnerCounts(document, transaction, owner, phase);
        Write(document, $"AUDIT_END phase={phase}");
    }

    private static string RawPhysical(Entity entity)
    {
        using var data = entity.GetXDataForApplication(RoofPhysical3DGeneratedStore.RegAppName);
        return data is null ? "-" : JsonSerializer.Serialize(data.AsArray().Select(value => new { code = value.TypeCode, value = Convert.ToString(value.Value, CultureInfo.InvariantCulture) }));
    }

    private static string Geometry(Entity entity) => entity switch
    {
        Line line => $"start={Point(line.StartPoint)};end={Point(line.EndPoint)}",
        Face face => string.Join(";", Enumerable.Range(0, 4).Select(i => $"v{i}={Point(face.GetVertexAt((short)i))}")),
        Polyline polyline => string.Join(";", Enumerable.Range(0, polyline.NumberOfVertices).Select(i => $"v{i}={Point(polyline.GetPoint3dAt(i))}")),
        _ => "-",
    };

    private static string Point(Point3d point) => FormattableString.Invariant($"({point.X:R},{point.Y:R},{point.Z:R})");
    private static void Write(Document document, string message)
    {
        try { document.Editor.WriteMessage("\nROOF_3D_AUDIT " + message); } catch { }
        AcKrovyDiagnostics.Info("ROOF_3D_AUDIT", message);
    }

    private static void Created(object? sender, DocumentCollectionEventArgs e) { if (e.Document is not null) Attach(e.Document); }
    private static void Destroyed(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is not null && Trackers.Remove(e.Document, out var tracker)) tracker.Dispose();
    }
    private static void Attach(Document document)
    {
        if (!Trackers.ContainsKey(document)) Trackers.Add(document, new Tracker(document));
    }

    private sealed class Tracker : IDisposable
    {
        public Document Document { get; }
        public bool Enabled { get; set; }
        public HashSet<string> KnownOwners { get; } = new(StringComparer.OrdinalIgnoreCase);
        private string _command = string.Empty;
        private bool Observe => Enabled && (_command is "COPY" or "MIRROR" or "ERASE" or "AK_ROOF_EDIT");
        public Tracker(Document document)
        {
            Document = document;
            document.CommandWillStart += WillStart;
            document.CommandEnded += Ended;
            document.CommandCancelled += Cancelled;
            document.CommandFailed += Cancelled;
            document.Database.BeginDeepCloneTranslation += Mapping;
        }
        private void WillStart(object? sender, CommandEventArgs e)
        {
            _command = e.GlobalCommandName.ToUpperInvariant();
            KnownOwners.Clear();
            if (Observe) Audit(Document, "CommandWillStart:" + _command);
        }
        private void Ended(object? sender, CommandEventArgs e)
        {
            // Registered before the production tracker: this is the native result,
            // before KROVY CommandEnded maintenance. Manual AUDIT records the final result.
            if (Observe) Audit(Document, "CommandEnded-before-maintenance:" + _command);
            _command = string.Empty;
        }
        private void Cancelled(object? sender, CommandEventArgs e) => _command = string.Empty;
        private void Mapping(object? sender, IdMappingEventArgs e)
        {
            if (!Observe) return; // In particular, zero DB access on U/UNDO/REDO/MREDO.
            if (_command is not ("COPY" or "MIRROR")) return;
            try
            {
                using var transaction = Document.Database.TransactionManager.StartTransaction();
                foreach (IdPair pair in e.IdMapping)
                {
                    if (!pair.IsCloned || pair.Key.IsNull || pair.Value.IsNull ||
                        transaction.GetObject(pair.Key, OpenMode.ForRead) is not Entity source ||
                        transaction.GetObject(pair.Value, OpenMode.ForRead) is not Entity clone) continue;
                    var physical = RoofPhysical3DGeneratedStore.Read(source);
                    var display = RoofDisplayStore.Read(source);
                    var owner = source is Polyline && RoofDefinitionStore.Read(source).Data is not null;
                    if (!physical.Exists && !display.Exists && !owner) continue;
                    Write(Document, $"MAP command={_command} context={e.IdMapping.DeepCloneContext} source={source.Handle} clone={clone.Handle} nativeType={clone.GetType().Name} sourcePhysical={JsonSerializer.Serialize(physical.Data)} clonePhysical={JsonSerializer.Serialize(RoofPhysical3DGeneratedStore.Read(clone).Data)} sourceDisplay={display.OwnerReference} cloneDisplay={RoofDisplayStore.Read(clone).OwnerReference} owner={owner}");
                }
            }
            catch (System.Exception ex) { Write(Document, $"MAP_FAILED command={_command} error={ex.GetType().Name}"); }
        }
        public void Dispose()
        {
            Document.CommandWillStart -= WillStart;
            Document.CommandEnded -= Ended;
            Document.CommandCancelled -= Cancelled;
            Document.CommandFailed -= Cancelled;
            Document.Database.BeginDeepCloneTranslation -= Mapping;
        }
    }
}
#endif
