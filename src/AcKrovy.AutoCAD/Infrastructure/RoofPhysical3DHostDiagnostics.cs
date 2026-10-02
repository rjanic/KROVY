#if DEBUG
using System.Globalization;
using System.Text.Json;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only DEBUG HOST evidence, with automatic member command checkpoints and opt-in full audits.</summary>
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
            if (tracker is not null) OwnerCounts(tracker.Document, transaction, owner, $"reconcile-{phase}");
        }
        catch (System.Exception ex) { AcKrovyDiagnostics.Info("ROOF_3D_AUDIT", $"AUDIT_FAILED phase={phase} error={ex.GetType().Name}"); }
    }

    public static void MaintenanceComplete(Document document, string command)
    {
        if (!Trackers.TryGetValue(document, out var tracker)) return;
        if (LiveGeometryCommandRules.IsUndoRedoCommand(command)) return;
        tracker.ReportMemberCheckpoint(command, "after-maintenance");
        if (!tracker.Enabled) return;
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(command).ToUpperInvariant();
        foreach (var owner in tracker.KnownOwners)
            OwnerCounts(document, owner, "CommandEnded-after-maintenance:" + normalized);
    }

    public static void TimberRestoreFailure(Database database, Transaction transaction,
        string owner, RoofUnsupportedStretchTimberLineSnapshotData snapshot, string stage)
    {
        // Called only on an existing recovery failure. Observe the live entity; never
        // retry the write, change recovery policy, or infer a native structural edit.
        try
        {
            var id = database.GetObjectId(false, new Handle(long.Parse(snapshot.EntityHandle,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0);
            var entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
            var line = entity as Line;
            var metadata = new AutoCadTimberElementMetadataStore(transaction);
            var elementId = entity is not null && metadata.TryRead(entity, out var data)
                ? data?.ElementId : null;
            var geometryMatches = line is not null &&
                line.StartPoint.DistanceTo(new Point3d(snapshot.Start.X, snapshot.Start.Y, snapshot.Start.Z)) <= 1e-6 &&
                line.EndPoint.DistanceTo(new Point3d(snapshot.End.X, snapshot.End.Y, snapshot.End.Z)) <= 1e-6;
            AcKrovyDiagnostics.Info("ROOF_TIMBER_RESTORE_FAILURE_PROBE",
                $"owner={owner} handle={snapshot.EntityHandle} stage={stage}" +
                $" nativeType={entity?.GetType().Name ?? "missing"} erased={entity?.IsErased}" +
                $" geometryMatchesSnapshot={geometryMatches} snapshotElementId={snapshot.ElementId}" +
                $" liveElementId={elementId ?? "missing"}" +
                $" structural={JsonSerializer.Serialize(entity is null ? null : RoofStructuralGeneratedStore.Read(entity).Data)}" +
                $" liveGeometry={(entity is null ? "missing" : Geometry(entity))}");
        }
        catch (System.Exception ex)
        {
            var status = ex is Autodesk.AutoCAD.Runtime.Exception cad ? cad.ErrorStatus.ToString() : "-";
            AcKrovyDiagnostics.Info("ROOF_TIMBER_RESTORE_FAILURE_PROBE",
                $"owner={owner} handle={snapshot.EntityHandle} stage={stage}" +
                $" readProbeFailed={ex.GetType().Name} errorStatus={status}");
        }
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

    public static void PhysicalReconcile(Database database, string owner, int rebuilt, int removed,
        IReadOnlyCollection<string> keys)
    {
        var tracker = Trackers.Values.FirstOrDefault(item => item.Enabled &&
            item.Document.Database.UnmanagedObject == database.UnmanagedObject);
        if (tracker is null) return;
        Write(tracker.Document, $"PHYSICAL_RECONCILE owner={owner} phase=transaction rebuilt={rebuilt} removed={removed} keys={string.Join("|", keys)}");
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

    private static void OwnerCounts(Document document, Transaction transaction, string owner, string phase, bool deep = false)
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
            {
                sourceLive = true;
                if (deep)
                    Write(document, $"MEMBER_ROOF_SOURCE phase={phase} owner={owner} geometry={Geometry(entity)} definition={JsonSerializer.Serialize(RoofDefinitionStore.Read(entity).Data)}");
            }
            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
            if (physical is not null && string.Equals(physical.RoofOwnerReference, owner, StringComparison.OrdinalIgnoreCase))
            {
                children.Add(physical);
                handles.Add(entity.Handle.ToString());
                if (deep)
                    Write(document, $"MEMBER_PHYSICAL phase={phase} owner={owner} handle={entity.Handle} data={JsonSerializer.Serialize(physical)}");
            }
            if (string.Equals(RoofDisplayStore.Read(entity).OwnerReference, owner, StringComparison.OrdinalIgnoreCase)) displayCount++;
        }
        var roofSurfaceTotal = children.Count(c => c.Role is
            RoofPhysical3DGeneratedRole.Face or RoofPhysical3DGeneratedRole.EaveEdge or
            RoofPhysical3DGeneratedRole.HipEdge or RoofPhysical3DGeneratedRole.RidgeEdge);
        var hipRafterSolids = children.Count(c => c.Role ==
            RoofPhysical3DGeneratedRole.StructuralRafterSolid &&
            c.StructuralId.StartsWith("Hip|", StringComparison.Ordinal));
        var valleyRafterSolids = children.Count(c => c.Role ==
            RoofPhysical3DGeneratedRole.StructuralRafterSolid &&
            c.StructuralId.StartsWith("Valley|", StringComparison.Ordinal));
        Write(document, $"OWNER_COUNTS phase={phase} owner={owner} sourceLive={sourceLive} faces={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.Face)} eaves={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.EaveEdge)} hips={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.HipEdge)} ridges={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.RidgeEdge)} ordinarySolids={children.Count(c => c.Role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)} hipRafterSolids={hipRafterSolids} valleyRafterSolids={valleyRafterSolids} roofSurfaceTotal={roofSurfaceTotal} combinedPhysicalTotal={children.Count} total={children.Count} display={displayCount} uniqueKeys={children.Select(c => (c.Role, c.StructuralId)).Distinct().Count() == children.Count}");
        if (deep) Write(document, $"PHYSICAL_INVENTORY owner={owner} signatures={string.Join("|", children.Select(c => c.GenerationSignature).Distinct())} handles={string.Join("|", handles)}");
        foreach (var duplicate in children.Select((data, index) => (data, Handle: handles[index]))
                     .GroupBy(item => (item.data.Role, item.data.StructuralId)).Where(group => group.Count() > 1))
            Write(document, $"DUPLICATE_PHYSICAL_KEY owner={owner} role={duplicate.Key.Role} key={duplicate.Key.StructuralId} handles={string.Join("|", duplicate.Select(item => item.Handle))}");
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
            if (deep) Write(document, $"GROUP_MEMBERS phase={phase} owner={owner} groupId={entry.Value.Handle} raw={string.Join("|", members.Select(id => id.IsNull ? "null" : id.Handle.ToString()))} unique={string.Join("|", members.Distinct().Select(id => id.IsNull ? "null" : id.Handle.ToString()))}");
            var sourceId = model.Cast<ObjectId>().FirstOrDefault(id => !id.IsNull &&
                string.Equals(id.Handle.ToString(), owner, StringComparison.OrdinalIgnoreCase));
            if (!sourceId.IsNull && !sourceId.IsErased &&
                transaction.GetObject(sourceId, OpenMode.ForRead) is Polyline source &&
                RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(database, transaction, source, out var displayIds) &&
                RoofAssemblyGroupMemberCollector.TryCollect(database, transaction, sourceId, displayIds, out var collected) && collected is not null)
            {
                var expected = collected.MemberIds;
                var duplicates = RoofAssemblyGroupMembershipRules.CountDuplicates(members);
                var missing = RoofAssemblyGroupMembershipRules.CountMissing(members, expected);
                var foreign = RoofAssemblyGroupMembershipRules.CountForeign(members, expected);
                Write(document, $"GROUP_SUMMARY phase={phase} owner={owner} expected={expected.Count} actual={members.Length} duplicates={duplicates} missing={missing} foreign={foreign} canonical={canonicalName && firstListing && duplicates == 0 && missing == 0 && foreign == 0}");
            }
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
                    if (physical.Data.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    {
                        var parts = physical.Data.GenerationSignature.Split('|');
                        var width = 0d;
                        var height = 0d;
                        var mode = RoofStructuralHeightMode.Automatic;
                        var placementMode = "invalid";
                        var placementPayload = "-";
                        var valid = parts.Length >= 5 && parts[0] == "StructuralPhysical1" &&
                            double.TryParse(parts[1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out width) &&
                            double.TryParse(parts[2], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out height) &&
                            Enum.TryParse(parts[3], out mode);
                        if (valid && parts[4] == "Automatic" && parts.Length >= 7)
                        {
                            placementMode = "Automatic";
                            placementPayload = parts[5] + "|" + parts[6];
                        }
                        else if (valid && parts[4] == "Plan" && parts.Length >= 9)
                        {
                            placementMode = "Plan";
                            placementPayload = string.Join("|", parts.Skip(5).Take(4));
                        }
                        else
                            valid = false;
                        var timberType = physical.Data.StructuralId.StartsWith("Hip|",
                            StringComparison.Ordinal) ? "HipRafter" :
                            physical.Data.StructuralId.StartsWith("Valley|",
                                StringComparison.Ordinal) ? "ValleyRafter" : "invalid";
                        Write(document,
                            $"STRUCTURAL_SOLID phase={phase} owner={physical.Data.RoofOwnerReference} structuralKey={physical.Data.StructuralId} timberType={timberType} widthMm={(valid ? width.ToString("R", CultureInfo.InvariantCulture) : "invalid")} heightMode={(valid ? mode.ToString() : "invalid")} resolvedHeightMm={(valid ? height.ToString("R", CultureInfo.InvariantCulture) : "invalid")} placementMode={placementMode} placementPayload={placementPayload} handle={entity.Handle} metadataValid={valid}");
                    }
                }
            }
            if (entity is Line planLine)
            {
                var generated = RoofGeneratedTimberStore.Read(planLine).Data;
                var attached = RoofAttachedManualTimberStore.Read(planLine).Data;
                if (generated is not null || attached is not null)
                    Write(document, $"PLAN_MEMBER {common} geometry={Geometry(planLine)}" +
                        $" generated={JsonSerializer.Serialize(generated)} attached={JsonSerializer.Serialize(attached)}");
            }
            if (entity is Line structuralLine &&
                RoofStructuralGeneratedStore.Read(structuralLine).Data is { } structuralData)
                Write(document,
                    $"STRUCTURAL_REFERENCE phase={phase} owner={structuralData.RoofOwnerReference} structuralKey={structuralData.LogicalKey} handle={entity.Handle} start={Point(structuralLine.StartPoint)} end={Point(structuralLine.EndPoint)} planZZero={Math.Abs(structuralLine.StartPoint.Z) <= 1e-6 && Math.Abs(structuralLine.EndPoint.Z) <= 1e-6}");
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
            OwnerCounts(document, transaction, owner, phase, deep: true);
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
        Solid3d solid => DiagnosticSolidGeometry(solid),
        Face face => string.Join(";", Enumerable.Range(0, 4).Select(i => $"v{i}={Point(face.GetVertexAt((short)i))}")),
        Polyline polyline => string.Join(";", Enumerable.Range(0, polyline.NumberOfVertices).Select(i => $"v{i}={Point(polyline.GetPoint3dAt(i))}")),
        _ => "-",
    };

    // HOST evidence only. These measurements never enter semantic reconciliation
    // or a physical builder. Read after native completion, not inside solid edits.
    private static string DiagnosticSolidGeometry(Solid3d solid)
    {
        try
        {
            var mass = solid.MassProperties;
            return FormattableString.Invariant(
                $"center={Point(mass.Centroid)};volume={mass.Volume:R}");
        }
        catch (System.Exception ex)
        {
            return "solid-measurement-unavailable:" + ex.GetType().Name;
        }
    }

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
        private int _nativeEventCount;
        private readonly Dictionary<string, int> _nativeEventKinds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _nativeMemberReported = new(StringComparer.OrdinalIgnoreCase);
        private sealed record MemberEvidence(string Owner, string Identity, string Geometry,
            string? ElementId, string Metadata);
        private readonly Dictionary<string, MemberEvidence> _memberBaseline = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _memberMappings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousMemberMappings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _memberTouched = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Sequence, string Kind, string Handle, string NativeType)> _nativeEntityEvents = new();
        private string _memberCommand = string.Empty;
        // Narrow DEBUG evidence for this milestone is automatic, so the HOST request
        // needs no trace-toggle setup. These observations never route production edits.
        private bool ObserveMemberCheckpoint => _command is "BREAK" or "COPY" or "MIRROR" or "BREAKATPOINT" ||
            (_command is "MOVE" or "TRIM" or "EXTEND" or "ERASE" or "STRETCH" or "GRIP_STRETCH");
        private Dictionary<string, (string Owner, Point3d Center, double Volume)> _moveSolids =
            new(StringComparer.OrdinalIgnoreCase);
        private bool Observe => Enabled && (_command is "COPY" or "MIRROR" or "ERASE" or
            "MOVE" or "TRIM" or "EXTEND" or "BREAK" or "BREAKATPOINT" or "STRETCH" or
            "GRIP_STRETCH" or "AK_ROOF_EDIT");
        public Tracker(Document document)
        {
            Document = document;
            document.CommandWillStart += WillStart;
            document.CommandEnded += Ended;
            document.CommandCancelled += Cancelled;
            document.CommandFailed += Cancelled;
            document.Database.BeginDeepCloneTranslation += Mapping;
            document.Database.ObjectAppended += Appended;
            document.Database.ObjectModified += Modified;
            document.Database.ObjectErased += Erased;
        }
        private void WillStart(object? sender, CommandEventArgs e)
        {
            _command = LiveGeometryCommandRules.NormalizeCommandName(e.GlobalCommandName)
                .ToUpperInvariant();
            _nativeEventCount = 0;
            _nativeEventKinds.Clear();
            _nativeMemberReported.Clear();
            KnownOwners.Clear();
            ClearMemberCheckpoint();
            if (ObserveMemberCheckpoint)
            {
                _memberCommand = _command;
                foreach (var pair in CaptureMemberEvidence()) _memberBaseline.Add(pair.Key, pair.Value);
                Write(Document, $"MEMBER_CHECKPOINT_BEGIN command={_memberCommand} baselineMembers={_memberBaseline.Count}");
            }
            if (Enabled && (_command is "MOVE" or "GRIP_STRETCH"))
                Write(Document, $"TRACE_COMMAND raw={e.GlobalCommandName} normalized={_command} enabled=True");
            _moveSolids = _command == "MOVE" && Enabled
                ? CaptureOwnedSolidMass() : new(StringComparer.OrdinalIgnoreCase);
            if (Observe || ObserveMemberCheckpoint) Write(Document, $"NATIVE_BEGIN command={_command}");
        }
        private void Ended(object? sender, CommandEventArgs e)
        {
            // Registered before the production tracker: this is the native result,
            // before KROVY CommandEnded maintenance. Manual AUDIT records the final result.
            if (Observe || ObserveMemberCheckpoint)
            {
                Write(Document, $"NATIVE_END command={_command} eventCount={_nativeEventCount}");
                Write(Document, $"NATIVE_EVENT_SUMMARY command={_command} {string.Join(" ", _nativeEventKinds.Select(pair => pair.Key + "=" + pair.Value))}");
                ReportMemberCheckpoint(_command, "native-ended");
                if (_command == "MOVE") CompareOwnedSolidMass();
            }
            _command = string.Empty;
            _moveSolids.Clear();
        }
        private void Cancelled(object? sender, CommandEventArgs e)
        {
            if (Observe || ObserveMemberCheckpoint)
                Write(Document, $"NATIVE_CANCEL_OR_FAIL command={_command} eventCount={_nativeEventCount}");
            ClearMemberCheckpoint();
            _command = string.Empty;
            _moveSolids.Clear();
        }
        private Dictionary<string, (string Owner, Point3d Center, double Volume)>
            CaptureOwnedSolidMass()
        {
            var result = new Dictionary<string, (string, Point3d, double)>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                using var transaction = Document.Database.TransactionManager.StartTransaction();
                var model = (BlockTableRecord)transaction.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(Document.Database),
                    OpenMode.ForRead);
                foreach (ObjectId id in model)
                {
                    if (id.IsErased ||
                        transaction.GetObject(id, OpenMode.ForRead) is not Solid3d solid ||
                        RoofPhysical3DGeneratedStore.Read(solid).Data is not { } data)
                        continue;
                    try
                    {
                        var mass = solid.MassProperties;
                        result[solid.Handle.ToString()] =
                            (data.RoofOwnerReference, mass.Centroid, mass.Volume);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            }
            catch (System.Exception ex)
            {
                Write(Document, $"MOVE_SNAPSHOT_FAILED phase=before error={ex.GetType().Name}");
            }
            return result;
        }
        private void CompareOwnedSolidMass()
        {
            var after = CaptureOwnedSolidMass();
            var changed = 0;
            foreach (var pair in _moveSolids)
            {
                if (!after.TryGetValue(pair.Key, out var current)) continue;
                if (pair.Value.Center.DistanceTo(current.Center) <= 1e-6 &&
                    Math.Abs(pair.Value.Volume - current.Volume) <= 1e-6) continue;
                changed++;
                Write(Document,
                    $"MOVE_SOLID_DELTA owner={pair.Value.Owner} handle={pair.Key} beforeCenter={Point(pair.Value.Center)} afterCenter={Point(current.Center)} beforeVolume={pair.Value.Volume:R} afterVolume={current.Volume:R}");
            }
            Write(Document, $"MOVE_SOLID_SUMMARY before={_moveSolids.Count} after={after.Count} changed={changed} nativeEvents={_nativeEventCount}");
        }
        private void Appended(object? sender, ObjectEventArgs e) =>
            NativeEvent("ObjectAppended", e.DBObject);
        private void Modified(object? sender, ObjectEventArgs e) =>
            NativeEvent("ObjectModified", e.DBObject);
        private void Erased(object? sender, ObjectErasedEventArgs e) =>
            NativeEvent(e.Erased ? "ObjectErased" : "ObjectUnerased", e.DBObject);
        private void NativeEvent(string kind, DBObject entity)
        {
            if (!Observe && !ObserveMemberCheckpoint) return;
            _nativeEventCount++;
            _nativeEventKinds[kind] = _nativeEventKinds.GetValueOrDefault(kind) + 1;
            if (entity is Entity owned)
            {
                try
                {
                    var reference = RoofGeneratedTimberStore.Read(owned).Data?.RoofOwnerReference ??
                        RoofAttachedManualTimberStore.Read(owned).Data?.RoofOwnerReference ??
                        RoofPhysical3DGeneratedStore.Read(owned).Data?.RoofOwnerReference ??
                        RoofStructuralGeneratedStore.Read(owned).Data?.RoofOwnerReference ??
                        RoofDisplayStore.Read(owned).OwnerReference;
                    if (reference is not null) KnownOwners.Add(reference);
                    if (owned is Polyline && RoofDefinitionStore.Read(owned).Data is not null)
                        KnownOwners.Add(owned.Handle.ToString());
                }
                catch (System.Exception ex)
                {
                    Write(Document, $"NATIVE_OWNER_FAILED command={_command} kind={kind} error={ex.GetType().Name}");
                }
            }
            if (ObserveMemberCheckpoint && entity is (Line or Solid3d))
            {
                _memberTouched.Add(entity.Handle.ToString());
                // ObjectAppended can precede readable inherited ownership. Resolve
                // relevance at native completion, retaining the original sequence.
                _nativeEntityEvents.Add((_nativeEventCount, kind, entity.Handle.ToString(), entity.GetType().Name));
            }
            // Keep every structural transition, including later erase/unerase
            // events on a handle already reported as modified. A first-event-only
            // summary cannot prove native control flow for replacement or recovery.
            if (ObserveMemberCheckpoint && entity is Entity structuralEntity)
            {
                try
                {
                    var structural = RoofStructuralGeneratedStore.Read(structuralEntity).Data;
                    var physical = RoofPhysical3DGeneratedStore.Read(structuralEntity).Data;
                    if (structural is not null ||
                        physical?.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                        Write(Document, $"STRUCTURAL_NATIVE_EVENT command={_command} seq={_nativeEventCount}" +
                            $" kind={kind} handle={entity.Handle} nativeType={entity.GetType().Name}" +
                            $" representation={(structural is not null ? "Plan2D" : "Physical3D")}" +
                            $" owner={structural?.RoofOwnerReference ?? physical!.RoofOwnerReference}" +
                            $" identity={structural?.LogicalKey.ToString() ?? physical!.StructuralId}");
                }
                catch (System.Exception ex)
                {
                    Write(Document, $"STRUCTURAL_NATIVE_EVENT_FAILED command={_command}" +
                        $" seq={_nativeEventCount} kind={kind} handle={entity.Handle} error={ex.GetType().Name}");
                }
            }
            // One affected member record per handle; repeated events are summarized.
            if (entity is Line && _nativeMemberReported.Add(entity.Handle.ToString()))
            {
                Write(Document, $"NATIVE_EVENT command={_command} seq={_nativeEventCount} kind={kind} handle={entity.Handle} type={entity.GetType().Name}");
                if (entity is Line line)
                {
                    try
                    {
                        Write(Document, $"NATIVE_MEMBER command={_command} seq={_nativeEventCount}" +
                            $" handle={line.Handle} kind={kind} geometry={Geometry(line)}" +
                            $" generated={JsonSerializer.Serialize(RoofGeneratedTimberStore.Read(line).Data)}" +
                            $" attached={JsonSerializer.Serialize(RoofAttachedManualTimberStore.Read(line).Data)}" +
                            $" structural={JsonSerializer.Serialize(RoofStructuralGeneratedStore.Read(line).Data)}");
                    }
                    catch (System.Exception ex)
                    {
                        Write(Document, $"NATIVE_MEMBER_FAILED command={_command} seq={_nativeEventCount} handle={line.Handle} error={ex.GetType().Name}");
                    }
                }
            }
        }
        private void Mapping(object? sender, IdMappingEventArgs e)
        {
            if (!Observe && !ObserveMemberCheckpoint) return; // Zero DB access on U/UNDO/REDO/MREDO.
            if (_command is not ("COPY" or "MIRROR")) return;
            Write(Document, $"NATIVE_MAP command={_command} afterEvent={_nativeEventCount}");
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
                    var generated = RoofGeneratedTimberStore.Read(source).Data;
                    var attached = RoofAttachedManualTimberStore.Read(source).Data;
                    var structural = RoofStructuralGeneratedStore.Read(source).Data;
                    var owner = source is Polyline && RoofDefinitionStore.Read(source).Data is not null;
                    if (!physical.Exists && !display.Exists && !owner && generated is null && attached is null && structural is null) continue;
                    var reference = generated?.RoofOwnerReference ?? attached?.RoofOwnerReference ?? structural?.RoofOwnerReference ?? physical.Data?.RoofOwnerReference ?? display.OwnerReference;
                    if (reference is not null) KnownOwners.Add(reference);
                    if (owner) KnownOwners.Add(source.Handle.ToString());
                    Write(Document, $"MAP command={_command} context={e.IdMapping.DeepCloneContext} source={source.Handle} clone={clone.Handle} nativeType={clone.GetType().Name} sourcePhysical={JsonSerializer.Serialize(physical.Data)} clonePhysical={JsonSerializer.Serialize(RoofPhysical3DGeneratedStore.Read(clone).Data)} sourceDisplay={display.OwnerReference} cloneDisplay={RoofDisplayStore.Read(clone).OwnerReference} owner={owner}");
                    if (generated is not null || attached is not null || structural is not null ||
                        physical.Data?.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    {
                        var sourceHandle = source.Handle.ToString();
                        var cloneHandle = clone.Handle.ToString();
                        if (_memberMappings.TryGetValue(cloneHandle, out var previous) &&
                            !string.Equals(previous, sourceHandle, StringComparison.OrdinalIgnoreCase))
                            _ambiguousMemberMappings.Add(cloneHandle);
                        else _memberMappings[cloneHandle] = sourceHandle;
                        Write(Document, $"MEMBER_MAP command={_command} source={source.Handle} clone={clone.Handle}" +
                            $" generated={JsonSerializer.Serialize(generated)} attached={JsonSerializer.Serialize(attached)}" +
                            $" structural={JsonSerializer.Serialize(structural)} physical={JsonSerializer.Serialize(physical.Data)}");
                    }
                }
            }
            catch (System.Exception ex) { Write(Document, $"MAP_FAILED command={_command} error={ex.GetType().Name}"); }
        }
        private Dictionary<string, MemberEvidence> CaptureMemberEvidence()
        {
            var result = new Dictionary<string, MemberEvidence>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var transaction = Document.Database.TransactionManager.StartTransaction();
                var model = (BlockTableRecord)transaction.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(Document.Database), OpenMode.ForRead);
                var metadata = new AutoCadTimberElementMetadataStore(transaction);
                foreach (ObjectId id in model)
                {
                    if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                    if (entity is Solid3d solid && RoofPhysical3DGeneratedStore.Read(solid).Data is { } physical &&
                        physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    {
                        result.Add(solid.Handle.ToString(), new MemberEvidence(physical.RoofOwnerReference,
                            "PhysicalStructural:" + physical.StructuralId, Geometry(solid), null,
                            JsonSerializer.Serialize(new { physical })));
                        continue;
                    }
                    if (entity is not Line line) continue;
                    var generated = RoofGeneratedTimberStore.Read(line).Data;
                    var attached = RoofAttachedManualTimberStore.Read(line).Data;
                    var structural = RoofStructuralGeneratedStore.Read(line).Data;
                    var structuralManual = RoofStructuralAttachedManualStore.Read(line).Data;
                    if (generated is null && attached is null && structural is null && structuralManual is null) continue;
                    var owner = structuralManual?.RoofOwnerReference ?? structural?.RoofOwnerReference ??
                        generated?.RoofOwnerReference ?? attached!.RoofOwnerReference;
                    var identity = structuralManual is not null
                        ? RoofStructuralAttachedManualIdentityRules.PhysicalKey(structuralManual.ManualIdentity)
                        : structural is not null ? "Structural:" + structural.LogicalKey : generated is not null
                        ? "Generated:" + RoofPhysicalStretchRules.PhysicalMemberId(RoofGeneratedMemberKey.From(generated))
                        : RoofAttachedManualIdentityRules.PhysicalKey(attached!);
                    _ = metadata.TryRead(line, out var timber);
                    result.Add(line.Handle.ToString(), new MemberEvidence(owner, identity, Geometry(line),
                        timber?.ElementId, JsonSerializer.Serialize(new { generated, attached, structural, structuralManual })));
                }
            }
            catch (System.Exception ex)
            {
                Write(Document, $"MEMBER_CHECKPOINT_FAILED command={_memberCommand} stage=read error={ex.GetType().Name}");
            }
            return result;
        }
        public void ReportMemberCheckpoint(string command, string phase)
        {
            if (string.IsNullOrEmpty(_memberCommand) ||
                !string.Equals(LiveGeometryCommandRules.NormalizeCommandName(command), _memberCommand,
                    StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var current = CaptureMemberEvidence();
                if (phase == "native-ended")
                {
                    var structuralHandles = _memberBaseline.Concat(current)
                        .Where(pair => pair.Value.Identity.StartsWith("Structural:", StringComparison.Ordinal) ||
                            pair.Value.Identity.StartsWith("PhysicalStructural:", StringComparison.Ordinal))
                        .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var native in _nativeEntityEvents.Where(item => structuralHandles.Contains(item.Handle)))
                        Write(Document, $"STRUCTURAL_NATIVE_SEQUENCE command={_memberCommand} seq={native.Sequence}" +
                            $" kind={native.Kind} handle={native.Handle} nativeType={native.NativeType}");
                }
                var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Recovery can replace a touched solid with a new ObjectId after
                // native events have ended. Include those new semantic physical
                // representations so immediate recovery has measured final evidence.
                var addedStructuralBodies = current.Where(pair =>
                        pair.Value.Identity.StartsWith("PhysicalStructural:", StringComparison.Ordinal) &&
                        !_memberBaseline.ContainsKey(pair.Key)).Select(pair => pair.Key);
                foreach (var handle in _memberTouched.Concat(_memberMappings.Keys).Concat(_memberMappings.Values)
                             .Concat(addedStructuralBodies)
                             .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal))
                {
                    var ambiguous = _ambiguousMemberMappings.Contains(handle);
                    var mapped = !ambiguous && _memberMappings.TryGetValue(handle, out _);
                    var source = mapped ? _memberMappings[handle] : handle;
                    _memberBaseline.TryGetValue(source, out var before);
                    current.TryGetValue(handle, out var after);
                    if (before is null && after is null) continue;
                    if (!mapped && before == after) continue; // Unchanged member notifications are not edits.
                    var pairing = ambiguous ? "ambiguous-map" : mapped ? "native-map" :
                        _memberBaseline.ContainsKey(handle) ? "pre-existing" :
                        phase == "after-maintenance" && after?.Identity.StartsWith("PhysicalStructural:", StringComparison.Ordinal) == true
                            ? "maintenance-created" : "unmapped-new";
                    var sameIdentity = before is not null && after is not null &&
                        before.Owner == after.Owner && before.Identity == after.Identity;
                    if (before is not null) owners.Add(before.Owner);
                    if (after is not null) owners.Add(after.Owner);
                    KnownOwners.UnionWith(owners);
                    Write(Document, $"MEMBER_CHECKPOINT command={_memberCommand} phase={phase}" +
                        $" source={(before is null ? "unresolved" : source)} handle={handle} pairing={pairing}" +
                        $" sameIdentity={sameIdentity} before={JsonSerializer.Serialize(before)} after={JsonSerializer.Serialize(after)}");
                }
                ReportMemberOwnerCounts(owners, phase);
            }
            catch (System.Exception ex)
            {
                Write(Document, $"MEMBER_CHECKPOINT_FAILED command={_memberCommand} stage={phase} error={ex.GetType().Name}");
            }
            finally
            {
                if (phase == "after-maintenance") ClearMemberCheckpoint();
            }
        }
        private void ReportMemberOwnerCounts(IEnumerable<string> owners, string phase)
        {
            try
            {
                using var transaction = Document.Database.TransactionManager.StartTransaction();
                foreach (var owner in owners.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var ownerId = Document.Database.GetObjectId(false,
                        new Handle(long.Parse(owner, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0);
                    if (!ownerId.IsErased && transaction.GetObject(ownerId, OpenMode.ForRead) is Polyline source)
                        Write(Document, $"MEMBER_OWNER_CONTEXT command={_memberCommand} phase={phase} owner={owner}" +
                            $" definition={JsonSerializer.Serialize(RoofDefinitionStore.Read(source).Data)}" +
                            $" boundary={JsonSerializer.Serialize(RoofBoundaryIdentityStore.Read(source).Data)}" +
                            $" elevation={JsonSerializer.Serialize(RoofPhysicalElevationStore.Read(source).Data)}");
                    RoofPhysical3DHostDiagnostics.OwnerCounts(Document, transaction, owner,
                        "member-" + phase + ":" + _memberCommand);
                }
            }
            catch (System.Exception ex)
            {
                Write(Document, $"MEMBER_CHECKPOINT_FAILED command={_memberCommand} stage=owner-{phase} error={ex.GetType().Name}");
            }
        }
        private void ClearMemberCheckpoint()
        {
            _memberCommand = string.Empty;
            _memberBaseline.Clear();
            _memberMappings.Clear();
            _ambiguousMemberMappings.Clear();
            _memberTouched.Clear();
            _nativeEntityEvents.Clear();
        }
        public void Dispose()
        {
            ClearMemberCheckpoint();
            Document.CommandWillStart -= WillStart;
            Document.CommandEnded -= Ended;
            Document.CommandCancelled -= Cancelled;
            Document.CommandFailed -= Cancelled;
            Document.Database.BeginDeepCloneTranslation -= Mapping;
            Document.Database.ObjectAppended -= Appended;
            Document.Database.ObjectModified -= Modified;
            Document.Database.ObjectErased -= Erased;
        }
    }
}
#endif
