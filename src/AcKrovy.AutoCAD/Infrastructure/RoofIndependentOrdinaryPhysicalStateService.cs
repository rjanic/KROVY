using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>One persistence boundary for every completed Independent package.
/// Reconstruction is read-only; the caller's accepted transaction owns the write.</summary>
internal static class RoofIndependentOrdinaryPhysicalStateService
{
    internal static void Persist(Line line, Transaction transaction, RoofOrdinaryPhysicalBuildState state)
    {
        var identity = RoofIndependentOrdinaryTimberStore.Read(line);
        var plan = Axis(line);
        if (identity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } ||
            RoofGeneratedTimberStore.Read(line).Data is not null ||
            RoofAttachedManualTimberStore.Read(line).Data is not null ||
            !new AutoCadTimberElementMetadataStore(transaction).TryRead(line, out var timber) || timber is null ||
            state.WidthMm != timber.WidthMm || state.HeightMm != timber.HeightMm ||
            !RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(state, plan, out var complete))
            throw new InvalidOperationException("Independent Ordinary complete physical state required.");
        RoofOrdinaryPhysicalBuildStateStore.Write(line, transaction, complete!);
        if (RoofIndependentOrdinaryTimberStore.Read(line) != identity)
            throw new InvalidOperationException("Independent Ordinary physical state changed persistent identity.");
    }

    internal static bool TryMigrate(Database database, Transaction transaction, Line line, Solid3d? solid,
        Polyline? owner, TimberElementData timber, out RoofOrdinaryPhysicalBuildState? state,
        RoofOrdinaryPhysicalBuildStateTrace? trace = null)
    {
        state = null;
        var identity = RoofIndependentOrdinaryTimberStore.Read(line);
        bool Fail(string reason)
        { trace?.Fail("IndependentPackageMigration:" + reason); return false; }
        if (identity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } ||
            !RoofIndependentOrdinaryTimberDataCodec.IsValid(identity)) return Fail("IndependentIdentityUnavailable");
        // Corrupt/unsupported records are not silently replaced with new guesses.
        if (RoofOrdinaryPhysicalBuildStateStore.HasRecord(line, transaction)) return Fail("ExistingXRecordInvalid");
        if (solid is null || RoofIndependentOrdinaryTimberStore.Read(solid) is not
            { EntityRole: RoofIndependentOrdinaryEntityRole.PhysicalSolid } paired ||
            paired.IndependentMemberId != identity.IndependentMemberId ||
            RoofGeneratedTimberStore.Read(line).Data is not null ||
            RoofPhysical3DGeneratedStore.Read(solid).Data is not null ||
            RoofAttachedManualTimberStore.Read(line).Data is not null ||
            RoofAttachedManualTimberStore.Read(solid).Data is not null) return Fail("UniqueIndependentPhysicalPairUnavailable");
        var plan = Axis(line);
        if (!RoofOrdinaryPhysicalSectionFrameReader.TryReadIndependent(solid, plan, timber.WidthMm,
                timber.HeightMm, out var measured, out var frameReason) || measured is null) return Fail(frameReason);
        trace?.Add("migrationMeasuredFrame", $"L:{RoofOrdinaryPhysicalBuildStateTrace.Point(measured.Frame.LongitudinalAxis)}," +
            $"W:{RoofOrdinaryPhysicalBuildStateTrace.Point(measured.Frame.WidthAxis)},H:{RoofOrdinaryPhysicalBuildStateTrace.Point(measured.Frame.HeightAxis)}");
        trace?.Add("migrationUpperAxisPoint", RoofOrdinaryPhysicalBuildStateTrace.Point(Point(measured.UpperAxisPoint)));
        trace?.Add("migrationMeasuredWidthMm", measured.WidthMm);
        trace?.Add("migrationMeasuredHeightMm", measured.HeightMm);
        trace?.Add("migrationTopVertexCount", measured.TopVertices.Count);
        RoofPoint3D[] vertices;
        try { vertices = RoofOrdinaryGripLifecycleService.ReadSolidVertices(solid).Select(Point).ToArray(); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) { return Fail("PhysicalVerticesUnavailable:" + ex.ErrorStatus); }
        trace?.Add("migrationBodyVertexCount", vertices.Length);
        var key = identity.SourceGeneratedMemberKey ?? new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 0);
        RoofOrdinaryPhysicalBuildState? context = null;
        // Owner data supplies optional cuts/plane context only; no live AUTO
        // inventory, station or rigid provenance translation is consulted.
        if (owner is not null)
            TryReadRetainedContext(database, transaction, owner, key, plan, timber, out context);
        trace?.Add("migrationRetainedContextAvailable", context is not null);
        if (!RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(plan, timber.WidthMm, timber.HeightMm,
                measured.Frame, Point(measured.UpperAxisPoint), measured.TopVertices.Select(Point).ToArray(),
                vertices, key, context, out state, out var reason)) return Fail(reason);
        trace?.Add("buildStateSource", "migrated_member_package");
        trace?.Add("migrationResolution", reason);
        trace?.Add("migrationPrepared", true);
        trace?.Add("migrationPersisted", false);
        trace?.Add("sectionFrameResolved", true);
        trace?.Add("physicalContextResolved", true);
        trace?.State(state);
        return true;
    }

    private static bool TryReadRetainedContext(Database database, Transaction transaction, Polyline owner,
        RoofGeneratedMemberKey key, RoofSegment3D plan, TimberElementData timber,
        out RoofOrdinaryPhysicalBuildState? context)
    {
        context = null;
        try
        {
            var input = RoofPolylineExtractor.Extract(owner);
            var footprint = RoofFootprintValidator.Validate(input).Footprint;
            var definition = RoofDefinitionStore.Read(owner).Data;
            var elevation = RoofPhysicalElevationStore.Read(owner).Data;
            if (footprint is null || definition is null || elevation is null) return false;
            var geometry = RoofDefinitionPersistence.Restore(input, footprint, definition).Geometry;
            RoofTopology? topology = geometry is HipRoofGeometry hip ? hip.Topology : null;
            if (geometry is SimpleGableRoofGeometry gable)
                SimpleGableRoofTopologyAdapter.TryCreate(gable, out topology, out _);
            if (topology is null) return false;
            // A semantic anchor from current Plan, independent of generator stations.
            var first = topology.Faces.First();
            var seed = new RoofFaceRafterSegment(first.SourceEdgeIndex, first.SourceEdgeIndex, key.StationIndex,
                0, 0, new(plan.Start.X, plan.Start.Y), new(plan.End.X, plan.End.Y),
                RoofRafterBoundaryRole.Free, RoofRafterBoundaryRole.Free, plan.Start.DistanceTo(plan.End));
            var anchor = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(topology, seed, plan);
            var sources = geometry is HipRoofGeometry roof
                ? RoofOrdinaryRafterSolidMaterializationService.ResolveStructuralSources(database, transaction, owner, roof, false)
                : Array.Empty<RoofStructuralRafterTrimSource>();
            if (sources is null) return false;
            context = RoofOrdinaryPhysicalBuildStateRules.Capture(topology, anchor, key,
                elevation.ResolvedEaveRelativeElevationMm, timber.WidthMm, timber.HeightMm,
                new(elevation.LowerEndCutMode, elevation.RidgeJoinMode), sources, plan);
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
    }

    internal static RoofSegment3D Axis(Line line) => new(Point(line.StartPoint), Point(line.EndPoint));
    private static RoofPoint3D Point(Point3d point) => new(point.X, point.Y, point.Z);
}
