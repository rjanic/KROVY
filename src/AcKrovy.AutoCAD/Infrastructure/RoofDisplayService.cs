using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only discovery and atomic rebuild operations for disposable roof display lines.</summary>
internal static class RoofDisplayService
{
    internal const string LayerName = "KROV_STRECHA";
    internal const int LayerColorIndex = 1;
    internal const string RidgeLayerName = "KROV_STRECHA_HREBEN";
    internal const int RidgeLayerColorIndex = 1;

    public static RoofDisplayInspection Inspect(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string generationSignature)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        var records = ScanModelSpaceDisplayChildren(database, transaction);
        var direct = records
            .Where(record => string.Equals(
                record.Stored.OwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validation = RoofDisplayValidator.Validate(
            ownerReference,
            expectedEdges,
            generationSignature,
            direct.Select(record => record.Observation).ToArray());
        var childIds = direct.Select(record => record.Id).ToList();
        if (validation.State == RoofDisplayState.Missing)
        {
            var liveForeignOwners = CollectLiveForeignRoofOwners(
                database,
                transaction,
                ownerId,
                ownerReference,
                records);
            var transferredCandidates = records
                .Where(record => !string.Equals(
                    record.Stored.OwnerReference,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
                .Select(record => record.Observation)
                .ToArray();
            if (RoofTransferredDisplayAssociation.TryMatch(
                    ownerReference,
                    expectedEdges,
                    generationSignature,
                    transferredCandidates,
                    liveForeignOwners,
                    out var match))
            {
                validation = match.Validation;
                childIds = records
                    .Where(record => string.Equals(
                        record.Stored.OwnerReference,
                        match.StoredOwnerReference,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(record => record.Id)
                    .ToList();
            }
        }

        var group = RoofDisplayGroupService.Inspect(
            database,
            transaction,
            ownerId,
            childIds);
        return new RoofDisplayInspection(validation, group, childIds);
    }

    public static bool TryResolveTransferredOwner(
        Database database,
        Transaction transaction,
        ObjectId selectedChildId,
        string storedOwnerReference,
        out ObjectId transferredOwnerId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        transferredOwnerId = ObjectId.Null;
        if (selectedChildId.IsNull || string.IsNullOrWhiteSpace(storedOwnerReference))
        {
            return false;
        }

        var records = ScanModelSpaceDisplayChildren(database, transaction)
            .Where(record => string.Equals(
                record.Stored.OwnerReference,
                storedOwnerReference,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (records.Count == 0 || records.All(record => record.Id != selectedChildId))
        {
            return false;
        }

        var observations = records.Select(record => record.Observation).ToArray();
        var matches = new HashSet<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var polyline,
                    database) || polyline is null)
            {
                continue;
            }

            if (!TryGetExpectedDisplay(polyline, out var edges, out var signature))
            {
                continue;
            }

            var ownerReference = polyline.Handle.ToString();
            if (!RoofTransferredDisplayAssociation.TryMatch(
                    ownerReference,
                    edges,
                    signature,
                    observations,
                    Array.Empty<string>(),
                    out var match) ||
                !match.Validation.IsCurrent ||
                !string.Equals(
                    match.StoredOwnerReference,
                    storedOwnerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches.Add(id);
        }

        if (matches.Count != 1)
        {
            return false;
        }

        transferredOwnerId = matches.Single();
        return true;
    }

    public static bool Rebuild(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string generationSignature)
    {
        var inspection = Inspect(
            database,
            transaction,
            ownerId,
            ownerReference,
            expectedEdges,
            generationSignature);
        if (inspection.Validation.IsCurrent)
        {
            foreach (var childId in inspection.ChildIds)
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        childId,
                        OpenMode.ForWrite,
                        out var currentLine,
                        database) || currentLine is null)
                {
                    return false;
                }
                var currentData = RoofDisplayStore.Read(currentLine).Data;
                if (currentData is null)
                {
                    return false;
                }
                ApplyDisplayLayer(database, transaction, currentLine, currentData.Role);
            }
            RoofDisplayGroupService.EnsureGroup(
                database,
                transaction,
                ownerId,
                inspection.ChildIds);
            return true;
        }
        if (inspection.Validation.Issues.HasFlag(
                RoofDisplayValidationIssue.UnsupportedFutureSchema))
        {
            return false;
        }

        foreach (var childId in inspection.ChildIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    childId,
                    OpenMode.ForWrite,
                    out var child,
                    database) || child is null)
            {
                continue;
            }

            var stored = RoofDisplayStore.Read(child);
            if (stored.Exists && string.Equals(
                    stored.OwnerReference,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                child.Erase();
            }
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var newChildIds = new List<ObjectId>(SimpleGableRoofWireframe.EdgeCount);
        foreach (var edge in expectedEdges.OrderBy(edge => edge.Role))
        {
            var line = new Line(MapPoint(edge.Segment.Start), MapPoint(edge.Segment.End));
            line.SetDatabaseDefaults(database);
            ApplyDisplayLayer(database, transaction, line, edge.Role);
            var childId = modelSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
            newChildIds.Add(childId);
            RoofDisplayStore.Write(
                line,
                transaction,
                new RoofDisplayData(
                    RoofDisplayDataSchema.CurrentVersion,
                    ownerReference,
                    edge.Role,
                    generationSignature));
        }

        RoofDisplayGroupService.EnsureGroup(
            database,
            transaction,
            ownerId,
            newChildIds);

        return true;
    }

    private static RoofPoint3D MapPoint(Point3d point) =>
        new(point.X, point.Y, point.Z);

    private static Point3d MapPoint(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);

    private static void ApplyDisplayLayer(
        Database database,
        Transaction transaction,
        Line line,
        RoofDisplayEdgeRole role)
    {
        var isRidge = role == RoofDisplayEdgeRole.Ridge;
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            line,
            isRidge ? RidgeLayerName : LayerName,
            isRidge ? RidgeLayerColorIndex : LayerColorIndex);
        line.LinetypeId = database.ByLayerLinetype;
        line.LinetypeScale = 1d;
        line.LineWeight = LineWeight.ByLayer;
    }

    private static List<ScannedDisplayChild> ScanModelSpaceDisplayChildren(
        Database database,
        Transaction transaction)
    {
        var records = new List<ScannedDisplayChild>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) || entity is null)
            {
                continue;
            }

            var stored = RoofDisplayStore.Read(entity);
            if (!stored.Exists)
            {
                continue;
            }

            var segment = entity is Line line
                ? new RoofSegment3D(MapPoint(line.StartPoint), MapPoint(line.EndPoint))
                : new RoofSegment3D(
                    new RoofPoint3D(double.NaN, double.NaN, double.NaN),
                    new RoofPoint3D(double.NaN, double.NaN, double.NaN));
            records.Add(new ScannedDisplayChild(
                id,
                stored,
                new RoofDisplayObservation(
                    stored.OwnerReference,
                    stored.Data,
                    stored.Error,
                    segment,
                    entity is Line)));
        }

        return records;
    }

    private static HashSet<string> CollectLiveForeignRoofOwners(
        Database database,
        Transaction transaction,
        ObjectId selectedOwnerId,
        string selectedOwnerReference,
        IReadOnlyList<ScannedDisplayChild> records)
    {
        var liveForeignOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Stored.OwnerReference) ||
                string.Equals(
                    record.Stored.OwnerReference,
                    selectedOwnerReference,
                    StringComparison.OrdinalIgnoreCase) ||
                liveForeignOwners.Contains(record.Stored.OwnerReference) ||
                !IsLiveForeignRoofOwner(
                    database,
                    transaction,
                    selectedOwnerId,
                    record.Stored.OwnerReference))
            {
                continue;
            }

            liveForeignOwners.Add(record.Stored.OwnerReference);
        }

        return liveForeignOwners;
    }

    private static bool IsLiveForeignRoofOwner(
        Database database,
        Transaction transaction,
        ObjectId selectedOwnerId,
        string storedOwnerReference)
    {
        if (!long.TryParse(
                storedOwnerReference,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) ||
            handleValue <= 0)
        {
            return false;
        }

        ObjectId candidateId;
        try
        {
            candidateId = database.GetObjectId(false, new Handle(handleValue), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        if (candidateId == selectedOwnerId)
        {
            return false;
        }

        return AutoCadObjectIdAccess.TryGetObject<Polyline>(
                   transaction,
                   candidateId,
                   OpenMode.ForRead,
                   out var polyline,
                   database) &&
               polyline is not null &&
               RoofDefinitionStore.Read(polyline).Data is not null;
    }

    private static bool TryGetExpectedDisplay(
        Polyline owner,
        out IReadOnlyList<RoofDisplayEdge> edges,
        out string signature)
    {
        edges = Array.Empty<RoofDisplayEdge>();
        signature = string.Empty;
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        var stored = RoofDefinitionStore.Read(owner);
        if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
        {
            return false;
        }

        var restored = RoofDefinitionPersistence.Restore(
            input,
            validation.Footprint,
            stored.Data);
        if (!restored.IsValid || restored.Geometry is null)
        {
            return false;
        }

        edges = SimpleGableRoofWireframe.Create(
            restored.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        return true;
    }

    private readonly record struct ScannedDisplayChild(
        ObjectId Id,
        RoofDisplayStoreReadResult Stored,
        RoofDisplayObservation Observation);
}

internal sealed record RoofDisplayInspection(
    RoofDisplayValidationResult Validation,
    RoofDisplayGroupInspection Group,
    IReadOnlyList<ObjectId> ChildIds)
{
    public RoofDisplayLifecycleKind Lifecycle =>
        RoofDisplayLifecycleClassifier.Classify(Validation, Group.IsCurrent);
}
