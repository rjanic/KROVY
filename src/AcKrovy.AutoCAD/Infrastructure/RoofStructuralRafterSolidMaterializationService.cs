using System.Globalization;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadRegion = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Adds a physical Solid3d representation to each existing logical Hip/Valley Line.
/// The Core polyhedron owns the geometry; this adapter only extrudes and clips it.
/// All writes stay in the caller's roof transaction.
/// </summary>
internal static class RoofStructuralRafterSolidMaterializationService
{
    private const double ToleranceMm = 1e-6;

    public static bool TryReconcileInTransaction(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry geometry,
        RoofStructuralEdgeResolutionResult resolution,
        Editor editor,
        out string failureReason)
    {
        failureReason = string.Empty;
        var ownerReference = owner.Handle.ToString();
        var elevation = RoofPhysicalElevationStore.Read(owner).Data;
        if (elevation?.Physical3DEnabled != true)
        {
            RoofPhysical3DMaterializationService.EraseStructuralRafterSolids(
                database, transaction, ownerReference);
            return true;
        }
        if (!resolution.IsValid ||
            !RoofOrdinaryRafterSolidMaterializationService.TryBuildExistingModelInTransaction(
                database, transaction, owner, geometry, out var ordinary) ||
            ordinary is null)
        {
            failureReason = "OrdinaryPhysicalModelUnavailable";
            return false;
        }
        var expectedOrdinaryKeys = ordinary.Members.Select(member => member.PhysicalIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var actualOrdinaryKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(transaction, id,
                    OpenMode.ForRead, out var entity, database) || entity is null)
                continue;
            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
            if (physical?.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid)
                continue;
            if (entity is not Solid3d || !actualOrdinaryKeys.Add(physical.StructuralId))
            {
                failureReason = "OrdinaryPhysicalSetInvalid";
                return false;
            }
        }
        if (!actualOrdinaryKeys.SetEquals(expectedOrdinaryKeys))
        {
            failureReason = "OrdinaryPhysicalSetMismatch";
            return false;
        }
        // Attached children do not drive the automatic structural profile recommendation.
        ordinary = ordinary with { Members = ordinary.Members.Where(member => member.AttachedManualIdentity is null).ToArray() };

        var structural = resolution.Edges.Where(edge =>
            edge.IsAutomaticStructuralTimberEligible).ToArray();
        if (!RoofStructuralUpperNodeMiterResolver.TryResolve(
                geometry.Topology, structural, out var miterPlanes,
                out failureReason))
            return false;
        var liveKeys = new HashSet<RoofStructuralLogicalKey>();
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id,
                    OpenMode.ForRead, out var line, database) || line is null ||
                RoofStructuralGeneratedStore.Read(line).Data is not { } data ||
                !metadata.TryRead(line, out TimberElementData? timber) || timber is null ||
                Math.Abs(timber.WidthMm - elevation.StructuralWidthMm) > ToleranceMm ||
                !liveKeys.Add(data.LogicalKey))
            {
                failureReason = "StructuralLineIdentityOrWidthMismatch";
                return false;
            }
        }
        if (!liveKeys.SetEquals(structural.Select(edge => edge.StructuralIdentity)))
        {
            failureReason = "StructuralLineSetMismatch";
            return false;
        }

        var bodies = new List<(RoofStructuralLogicalKey Key, RoofStructuralRafterPolyhedron Model)>();
        foreach (var edge in structural)
        {
            var request = new RoofStructuralRafterPolyhedronRequest(
                geometry.Topology, edge, elevation.ResolvedEaveRelativeElevationMm,
                elevation.StructuralWidthMm, elevation.StructuralHeightMode,
                elevation.StructuralHeightMode == RoofStructuralHeightMode.Explicit
                    ? elevation.StructuralExplicitHeightMm : null,
                ordinary.Members,
                miterPlanes.GetValueOrDefault(edge.TopologyEdgeIndex),
                elevation.LowerEndCutMode);
            if (!RoofStructuralRafterPolyhedronService.TryBuild(request,
                    out var body, out failureReason) || body is null)
                return false;
            bodies.Add((edge.StructuralIdentity, body));
        }

        // Construct all transient solids before touching the old set. A failure
        // rolls back the enclosing ordinary/structural/group transaction.
        var created = new List<(RoofStructuralLogicalKey Key,
            RoofStructuralRafterPolyhedron Model, Solid3d Solid)>();
        var appended = new HashSet<Solid3d>();
        try
        {
            foreach (var (key, body) in bodies)
                created.Add((key, body, CreateSolid(body)));

            RoofPhysical3DMaterializationService.EraseStructuralRafterSolids(
                database, transaction, ownerReference);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
            var layerProfile = ElementLayerProfileStore.Load();
            var visible = elevation.DisplayVisibility is
                RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;
            foreach (var (key, body, solid) in created)
            {
                var timberType = key.Role == RoofStructuralRole.Hip
                    ? TimberElementType.HipRafter : TimberElementType.ValleyRafter;
                var layerName = layerProfile.GetStyle(timberType).LayerName + "_3D";
                EnsureLayer(database, transaction, layerName);
                solid.SetDatabaseDefaults(database);
                solid.Layer = layerName;
                solid.Visible = visible;
                modelSpace.AppendEntity(solid);
                appended.Add(solid);
                transaction.AddNewlyCreatedDBObject(solid, true);
                var signature = string.Join("|",
                    "StructuralPhysical1",
                    elevation.StructuralWidthMm.ToString("R", CultureInfo.InvariantCulture),
                    body.Geometry.PhysicalVerticalHeightMm.ToString("R", CultureInfo.InvariantCulture),
                    elevation.StructuralHeightMode.ToString());
                RoofPhysical3DGeneratedStore.Write(solid, transaction,
                    RoofPhysical3DGeneratedDataRules.Create(ownerReference,
                        RoofPhysical3DGeneratedRole.StructuralRafterSolid,
                        key.ToString(), signature));
            }
            foreach (var (key, body) in bodies.Where(item =>
                         item.Model.Geometry.Warning ==
                         "ExplicitHeightBelowOrdinaryCutRequirement"))
            {
                editor.WriteMessage("\n" + UiStrings.Format(
                    UiStrings.GetString("RoofRafterWindow_StructuralHeightWarningFormat"),
                    key.ToString(),
                    body.Geometry.PhysicalVerticalHeightMm,
                    body.Geometry.RequiredAutomaticHeightMm) + "\n");
            }
            return true;
        }
        finally
        {
            foreach (var (_, _, solid) in created)
                if (!appended.Contains(solid)) solid.Dispose();
        }
    }

    private static Solid3d CreateSolid(RoofStructuralRafterPolyhedron model)
    {
        var expectedPrisms = model.Geometry.Role == RoofStructuralRole.Hip ? 1 : 2;
        if (model.ConvexHalves.Count != expectedPrisms)
            throw new InvalidOperationException("Structural body has an invalid prism count.");
        var halves = new List<Solid3d>(expectedPrisms);
        try
        {
            foreach (var half in model.ConvexHalves)
            {
                var vertices = half.SourcePrismVertices;
                if (vertices.Count != 8)
                    throw new InvalidOperationException("Structural half requires eight vertices.");
                using var top = new Line(Map(vertices[0]), Map(vertices[1]));
                using var side = new Line(Map(vertices[1]), Map(vertices[5]));
                using var bottom = new Line(Map(vertices[5]), Map(vertices[4]));
                using var center = new Line(Map(vertices[4]), Map(vertices[0]));
                var regions = CadRegion.CreateFromCurves(
                    new DBObjectCollection { top, side, bottom, center });
                try
                {
                    if (regions.Count != 1 || regions[0] is not CadRegion region)
                        throw new InvalidOperationException("Structural half profile is invalid.");
                    var solid = new Solid3d();
                    try
                    {
                        solid.CreateExtrudedSolid(region,
                            Map(vertices[2]) - Map(vertices[0]), new SweepOptions());
                        foreach (var plane in model.EaveClipPlanes)
                            SliceRetainingInside(solid, vertices, plane);
                        for (var index = 0; index < model.LowerEndClipPlanes.Count; index++)
                            SliceLowerEndPlane(solid, vertices, model, index);
                        if (model.UpperNodeMiterPlane is { } miter)
                            SliceRetainingInside(solid, vertices, miter);
                        for (var index = 0; index < model.RoofEnvelopeClipPlanes.Count; index++)
                        {
                            var prior = model.EaveClipPlanes
                                .Concat(model.LowerEndClipPlanes)
                                .Append(model.UpperNodeMiterPlane!)
                                .Concat(model.RoofEnvelopeClipPlanes.Take(index));
                            SliceRetainingAfterPrior(solid, vertices, prior,
                                model.RoofEnvelopeClipPlanes[index]);
                        }
                        if (model.UpperNodeMiterPlane is null)
                            SliceRetainingInside(solid, vertices, model.RidgeClipPlane);
                        halves.Add(solid);
                    }
                    catch
                    {
                        solid.Dispose();
                        throw;
                    }
                }
                finally
                {
                    foreach (DBObject region in regions) region.Dispose();
                }
            }
            if (halves.Count == 2)
                halves[0].BooleanOperation(BooleanOperationType.BoolUnite, halves[1]);
            var result = halves[0];
            halves.RemoveAt(0);
            return result;
        }
        finally
        {
            foreach (var half in halves) half.Dispose();
        }
    }

    private static void SliceRetainingInside(Solid3d solid,
        IReadOnlyList<RoofPoint3D> sourceVertices, RoofStructuralRafterClipPlane clip)
    {
        var normal = clip.RetainedNormal;
        var values = sourceVertices.Select(point =>
            (point.X - clip.Point.X) * normal.X +
            (point.Y - clip.Point.Y) * normal.Y +
            (point.Z - clip.Point.Z) * normal.Z).ToArray();
        if (values.Max() < -ToleranceMm)
            throw new InvalidOperationException("Structural clip removes a whole half.");
        if (values.Min() >= -ToleranceMm)
            return;
        using var plane = new Plane(Map(clip.Point),
            new Vector3d(normal.X, normal.Y, normal.Z));
        solid.Slice(plane, false);
    }

    private static void SliceLowerEndPlane(Solid3d solid,
        IReadOnlyList<RoofPoint3D> sourceVertices,
        RoofStructuralRafterPolyhedron model, int index)
    {
        var prior = model.EaveClipPlanes
            .Concat(model.LowerEndClipPlanes.Take(index));
        SliceRetainingAfterPrior(solid, sourceVertices, prior,
            model.LowerEndClipPlanes[index]);
    }

    private static void SliceRetainingAfterPrior(Solid3d solid,
        IReadOnlyList<RoofPoint3D> sourceVertices,
        IEnumerable<RoofStructuralRafterClipPlane> priorPlanes,
        RoofStructuralRafterClipPlane plane)
    {
        // A roof-plane or end plane may only touch the currently retained
        // body. Avoid asking the CAD kernel to slice by an external/tangent
        // plane, using the same Core construction sequence as the model.
        var prior = priorPlanes
            .Select(item => new RoofConvexPrismPlaneClipper.Plane(
                item.Point, item.RetainedNormal)).ToArray();
        if (!RoofConvexPrismPlaneClipper.TryClip(sourceVertices, prior,
                out var retained, allowNoOpPlanes: true) || retained is null)
            throw new InvalidOperationException("Structural preclip is invalid.");
        var distances = retained.Body.Select(point =>
            (point.X - plane.Point.X) * plane.RetainedNormal.X +
            (point.Y - plane.Point.Y) * plane.RetainedNormal.Y +
            (point.Z - plane.Point.Z) * plane.RetainedNormal.Z).ToArray();
        if (distances.Max() <= ToleranceMm)
            throw new InvalidOperationException("Structural plane removes a half.");
        if (distances.Min() >= -ToleranceMm) return;
        SliceRetainingInside(solid, sourceVertices, plane);
    }

    private static Point3d Map(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);

    private static void EnsureLayer(Database database, Transaction transaction, string name)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(name)) return;
        table.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name };
        table.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
    }
}
