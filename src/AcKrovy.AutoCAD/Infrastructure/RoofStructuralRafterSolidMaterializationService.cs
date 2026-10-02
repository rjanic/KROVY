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
                database, transaction, owner, geometry, out var ordinary,
                structuralReconcilePending: true) ||
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

        var editState = RoofStructuralEditStateStore.Read(owner, transaction);
        var allStructural = resolution.Edges.Where(edge => edge.IsAutomaticStructuralTimberEligible).ToArray();
        var structural = allStructural.Where(edge =>
            !RoofStructuralEditRules.Get(editState, edge.StructuralIdentity).Suppressed).ToArray();
        if (!RoofStructuralUpperNodeMiterResolver.TryResolve(
                geometry.Topology, allStructural, out var miterPlanes,
                out failureReason))
            return false;
        var liveKeys = new HashSet<RoofStructuralLogicalKey>();
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id,
                    OpenMode.ForRead, out var line, database) || line is null ||
                RoofStructuralAttachedManualStore.Read(line).Data is not null)
                continue;
            if (RoofStructuralGeneratedStore.Read(line).Data is not { } data ||
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

        var manuals = new List<(RoofStructuralAttachedManualData Data, Line Plan)>();
        var manualIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofStructuralAttachedManualStore.FindByOwner(
                     database, transaction, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id,
                    OpenMode.ForRead, out var line, database) || line is null ||
                RoofStructuralAttachedManualStore.Read(line).Data is not { } manual ||
                !manualIdentities.Add(manual.ManualIdentity) ||
                Math.Abs(line.StartPoint.Z) > ToleranceMm ||
                Math.Abs(line.EndPoint.Z) > ToleranceMm)
            {
                failureReason = "StructuralAttachedManualSetInvalid";
                return false;
            }
            manuals.Add((manual, line));
        }

        var bodies = new List<(string PhysicalId, RoofStructuralRafterPolyhedron Model)>();
        var editStateLookup = editState;
        var canonicalByKey = RoofAutomaticStructuralRafterPlanner.Create(resolution, TimberElementDefaultProfileStore.Load(),
            RoofPhysicalElevationRules.ToState(elevation, geometry.RiseMm)).Items
            .ToDictionary(item => item.LogicalKey);
        // Prefer already-resolved live plan geometry for absolute placement when present.
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
            var edit = RoofStructuralEditRules.Get(editStateLookup, edge.StructuralIdentity);
            if (!canonicalByKey.TryGetValue(edge.StructuralIdentity, out var canonicalItem))
            {
                failureReason = "StructuralCanonicalPlanMissing";
                return false;
            }
            bodies.Add((edge.StructuralIdentity.ToString(),
                RoofStructuralEditRules.Place(body, canonicalItem.Segment3D, edit)));
        }

        foreach (var (manual, plan) in manuals)
        {
            if (!TryResolveManualPlacement(
                    manual, plan, transaction, allStructural, geometry, elevation, ordinary,
                    miterPlanes, canonicalByKey, editStateLookup, editor, out var placement,
                    out failureReason) ||
                !RoofStructuralManualPlacementRules.TryBuildPrism(
                    manual.SourceRole, manual.WidthMm, placement!, out var manualBody, out failureReason) ||
                manualBody is null)
                return false;
            bodies.Add((RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity), manualBody));
        }

        // Construct all transient solids before touching the old set. A failure
        // rolls back the enclosing ordinary/structural/group transaction.
        var created = new List<(string PhysicalId,
            RoofStructuralRafterPolyhedron Model, Solid3d Solid)>();
        var appended = new HashSet<Solid3d>();
        try
        {
            foreach (var (physicalId, body) in bodies)
                created.Add((physicalId, body, CreateSolid(body)));

            var priorStructuralIds = RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, ownerReference)
                .Where(id => AutoCadObjectIdAccess.TryGetObject<Entity>(transaction, id, OpenMode.ForRead,
                    out var entity, database) && entity is not null &&
                    RoofPhysical3DGeneratedStore.Read(entity).Data?.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                .ToArray();
            RoofAssemblyGroupSyncService.DetachMembersBeforeErase(database, transaction, owner.ObjectId, priorStructuralIds);
            RoofPhysical3DMaterializationService.EraseStructuralRafterSolids(
                database, transaction, ownerReference);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
            var layerProfile = ElementLayerProfileStore.Load();
            var visible = elevation.DisplayVisibility is
                RoofPhysicalDisplayVisibility.Both or RoofPhysicalDisplayVisibility.Model3D;
            foreach (var (physicalId, body, solid) in created)
            {
                var isManual = RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(physicalId);
                var timberType = body.Geometry.Role == RoofStructuralRole.Hip
                    ? TimberElementType.HipRafter : TimberElementType.ValleyRafter;
                var layerName = layerProfile.GetStyle(timberType).LayerName + "_3D";
                EnsureLayer(database, transaction, layerName);
                solid.SetDatabaseDefaults(database);
                solid.Layer = layerName;
                solid.Visible = visible;
                modelSpace.AppendEntity(solid);
                appended.Add(solid);
                transaction.AddNewlyCreatedDBObject(solid, true);
                string signature;
                if (isManual)
                {
                    var manual = manuals.Select(item => item.Data).First(item =>
                        RoofStructuralAttachedManualIdentityRules.PhysicalKey(item.ManualIdentity) == physicalId);
                    signature = string.Join("|",
                        "StructuralPhysical1",
                        manual.WidthMm.ToString("R", CultureInfo.InvariantCulture),
                        body.Geometry.PhysicalVerticalHeightMm.ToString("R", CultureInfo.InvariantCulture),
                        manual.HeightMode.ToString(),
                        "Manual",
                        manual.ManualIdentity,
                        manual.SourceLogicalKey.ToString());
                }
                else
                {
                    var edit = RoofStructuralEditRules.Get(editState, body.StructuralKey);
                    signature = string.Join("|",
                        "StructuralPhysical1",
                        elevation.StructuralWidthMm.ToString("R", CultureInfo.InvariantCulture),
                        body.Geometry.PhysicalVerticalHeightMm.ToString("R", CultureInfo.InvariantCulture),
                        elevation.StructuralHeightMode.ToString(),
                        RoofStructuralEditRules.PhysicalSignatureToken(edit));
                }
                RoofPhysical3DGeneratedStore.Write(solid, transaction,
                    RoofPhysical3DGeneratedDataRules.Create(ownerReference,
                        RoofPhysical3DGeneratedRole.StructuralRafterSolid,
                        physicalId, signature));
            }
            foreach (var (physicalId, body) in bodies.Where(item =>
                         item.Model.Geometry.Warning ==
                         "ExplicitHeightBelowOrdinaryCutRequirement"))
            {
                editor.WriteMessage("\n" + UiStrings.Format(
                    UiStrings.GetString("RoofRafterWindow_StructuralHeightWarningFormat"),
                    physicalId,
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

    private static bool TryResolveManualPlacement(
        RoofStructuralAttachedManualData manual,
        Line plan,
        Transaction transaction,
        IReadOnlyList<ResolvedRoofStructuralEdge> allStructural,
        HipRoofGeometry geometry,
        RoofPhysicalElevationData elevation,
        RoofAutomaticRafterPhysicalModel ordinary,
        IReadOnlyDictionary<int, RoofStructuralRafterClipPlane> miterPlanes,
        IReadOnlyDictionary<RoofStructuralLogicalKey, RoofAutomaticStructuralRafterPlanItem> canonicalByKey,
        RoofStructuralEditState editState,
        Editor editor,
        out RoofStructuralManualPlacement? placement,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (manual.Placement is { } stored)
        {
            placement = stored;
            return true;
        }

        placement = null;
        var edge = allStructural.FirstOrDefault(item =>
            item.StructuralIdentity == manual.SourceLogicalKey);
        if (edge is null || !edge.IsAutomaticStructuralTimberEligible ||
            !canonicalByKey.TryGetValue(manual.SourceLogicalKey, out var canonicalItem))
        {
            failureReason = "StructuralManualSourceFoldMissing";
            return false;
        }

        var request = new RoofStructuralRafterPolyhedronRequest(
            geometry.Topology, edge, elevation.ResolvedEaveRelativeElevationMm,
            manual.WidthMm, manual.HeightMode,
            manual.HeightMode == RoofStructuralHeightMode.Explicit
                ? manual.ExplicitHeightMm : null,
            ordinary.Members,
            miterPlanes.GetValueOrDefault(edge.TopologyEdgeIndex),
            elevation.LowerEndCutMode);
        if (!RoofStructuralRafterPolyhedronService.TryBuild(request, out var body, out failureReason) ||
            body is null)
            return false;
        RoofStructuralRafterPolyhedron placed;
        try
        {
            placed = RoofStructuralEditRules.Place(
                body, canonicalItem.Segment3D,
                RoofStructuralEditRules.Get(editState, manual.SourceLogicalKey));
        }
        catch (ArgumentException)
        {
            failureReason = "StructuralManualSourcePlaceFailed";
            return false;
        }
        if (!RoofStructuralManualPlacementRules.TryCaptureFrame(placed, out var frame) || frame is null)
        {
            failureReason = "StructuralManualFrameCaptureFailed";
            return false;
        }

        var applied = RoofStructuralEditRules.ApplyPlan(new[] { canonicalItem }, editState).Single();
        // Native COPY clones the live Generated Plan Line. Match against that Line
        // (not only ApplyPlan) so RigidCopy survives small planner/line deltas.
        var sourcePlan = new RoofSegment3D(
            new(applied.Segment3D.Start.X, applied.Segment3D.Start.Y, 0),
            new(applied.Segment3D.End.X, applied.Segment3D.End.Y, 0));
        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                     plan.Database, transaction, manual.RoofOwnerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                    out var sourceLine, plan.Database) || sourceLine is null)
                continue;
            if (RoofStructuralGeneratedStore.Read(sourceLine).Data is not { } generated ||
                generated.LogicalKey != manual.SourceLogicalKey)
                continue;
            sourcePlan = new RoofSegment3D(
                new(sourceLine.StartPoint.X, sourceLine.StartPoint.Y, 0),
                new(sourceLine.EndPoint.X, sourceLine.EndPoint.Y, 0));
            break;
        }
        var copiedPlan = new RoofSegment3D(
            new(plan.StartPoint.X, plan.StartPoint.Y, 0),
            new(plan.EndPoint.X, plan.EndPoint.Y, 0));
        if (manual.CreationKind == RoofStructuralAttachedManualCreationKind.Mirror)
        {
            if (!RoofStructuralManualPlacementRules.TryReflectFrame(frame, sourcePlan, copiedPlan, out placement))
            {
                failureReason = "StructuralManualReflectionInvalid";
                return false;
            }
        }
        else
        {
            if (!RoofStructuralManualPlacementRules.TryMatchRigidPlanCopy(
                    sourcePlan, copiedPlan, out var dx, out var dy))
            {
                failureReason = "StructuralManualPlacementInvalid";
                return false;
            }
            placement = RoofStructuralManualPlacementRules.Translate(frame, dx, dy, 0);
        }

        if (placement is null)
        {
            failureReason = "StructuralManualPlacementInvalid";
            return false;
        }
        var persisted = RoofStructuralAttachedManualDataRules.WithPlacement(manual, placement);
        if (persisted.Data is null)
        {
            failureReason = persisted.Error.ToString();
            return false;
        }
        if (!plan.IsWriteEnabled) plan.UpgradeOpen();
        RoofStructuralAttachedManualStore.Write(plan, transaction, persisted.Data);
#if DEBUG
        editor.WriteMessage(
            $"\nROOF_STRUCT_MANUALIZE_PLACEMENT manualId={manual.ManualIdentity} placementMode={(manual.CreationKind == RoofStructuralAttachedManualCreationKind.Mirror ? "RigidMirror" : "RigidCopy")}" +
            $" planStart={plan.StartPoint.X.ToString("R", CultureInfo.InvariantCulture)},{plan.StartPoint.Y.ToString("R", CultureInfo.InvariantCulture)}" +
            $" planEnd={plan.EndPoint.X.ToString("R", CultureInfo.InvariantCulture)},{plan.EndPoint.Y.ToString("R", CultureInfo.InvariantCulture)}" +
            $" physicalStart={placement.AxisStartX.ToString("R", CultureInfo.InvariantCulture)},{placement.AxisStartY.ToString("R", CultureInfo.InvariantCulture)},{placement.AxisStartZ.ToString("R", CultureInfo.InvariantCulture)}" +
            $" physicalEnd={placement.AxisEndX.ToString("R", CultureInfo.InvariantCulture)},{placement.AxisEndY.ToString("R", CultureInfo.InvariantCulture)},{placement.AxisEndZ.ToString("R", CultureInfo.InvariantCulture)}\n");
#else
        _ = editor;
#endif
        return true;
    }

    private static Solid3d CreateSolid(RoofStructuralRafterPolyhedron model)
    {
        var expectedPrisms = model.ConvexHalves.Count;
        if (expectedPrisms is not (1 or 2) ||
            (model.Geometry.Role == RoofStructuralRole.Hip && expectedPrisms != 1))
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
                            // Never Append a null miter into the prior sequence — ridge-side
                            // Plan overrides clear UpperNodeMiterPlane while envelope planes
                            // may still be present until Core clears them too.
                            IEnumerable<RoofStructuralRafterClipPlane> prior =
                                model.EaveClipPlanes.Concat(model.LowerEndClipPlanes);
                            if (model.UpperNodeMiterPlane is { } priorMiter)
                                prior = prior.Append(priorMiter);
                            prior = prior.Concat(model.RoofEnvelopeClipPlanes.Take(index));
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
