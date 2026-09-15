#if DEBUG
using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only, read-only trace of the explicit structural-rafter desired-state pipeline.
/// The records use owner handles, structural keys, physical boundary ids and geometry;
/// no collection index or ObjectId is treated as persisted identity.
/// </summary>
internal static class RoofAutomaticStructuralRafterTrace
{
    public static void WriteTopologyAndDesired(
        Editor? editor,
        string owner,
        RoofFootprintInput sourceInput,
        RoofBoundaryIdentity boundaryIdentity,
        HipRoofGeometry geometry,
        RoofStructuralEdgeResolutionResult resolution,
        RoofAutomaticStructuralRafterPlanResult plan,
        double sourceElevation)
    {
        if (editor is null)
        {
            return;
        }

        try
        {
            var vertices = sourceInput.Vertices ?? Array.Empty<RoofPoint2D>();
            WriteLine(editor,
                "ROOF_STRUCT_INPUT" +
                $" owner={Token(owner)}" +
                $" footprintVertices={Number(vertices.Count)}" +
                $" boundarySegments={Number(boundaryIdentity.PhysicalSegmentCount)}" +
                $" boundaryIds={Token(string.Join(',', boundaryIdentity.BoundaryEdgeIds))}" +
                $" winding={Token(boundaryIdentity.RawWinding.ToString())}" +
                $" pitch={Scalar(geometry.Topology.PitchDegrees)}" +
                $" sourceElevation={Scalar(sourceElevation)}" +
                $" ridgeTopology={Number(geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge))}" +
                $" hipTopology={Number(geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip))}" +
                $" valleyTopology={Number(geometry.Topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley))}" +
                $" eligibleHip={Number(resolution.Edges.Count(edge => edge.StructuralRole == RoofStructuralRole.Hip && edge.IsAutomaticStructuralTimberEligible))}" +
                $" eligibleValley={Number(resolution.Edges.Count(edge => edge.StructuralRole == RoofStructuralRole.Valley && edge.IsAutomaticStructuralTimberEligible))}");

            for (var index = 0; index < vertices.Count; index++)
            {
                var edgeId = index < boundaryIdentity.BoundaryEdgeIds.Count
                    ? boundaryIdentity.BoundaryEdgeIds[index].ToString(CultureInfo.InvariantCulture)
                    : "-";
                WriteLine(editor,
                    "ROOF_STRUCT_FOOTPRINT" +
                    $" owner={Token(owner)}" +
                    $" vertex={Number(index)}" +
                    $" boundaryEdgeId={edgeId}" +
                    $" point={Point(vertices[index], sourceElevation)}");
            }

            foreach (var edge in resolution.Edges.OrderBy(edge => edge.TopologyEdgeIndex))
            {
                WriteLine(editor,
                    "ROOF_STRUCT_TOPOLOGY" +
                    $" owner={Token(owner)}" +
                    $" role={Token(edge.StructuralRole.ToString())}" +
                    $" boundaryEdgeIdA={Number(edge.BoundaryEdgeIdA)}" +
                    $" boundaryEdgeIdB={Number(edge.BoundaryEdgeIdB)}" +
                    $" key={Token(edge.StructuralIdentity.ToString())}" +
                    $" origin={NullableNumber(edge.OriginatingBoundaryVertexIndex)}" +
                    $" directAnchor={NullableNumber(edge.PhysicalBoundaryAnchorVertexIndex)}" +
                    $" pathAnchor={NullableNumber(edge.PhysicalPathAnchorVertexIndex)}" +
                    $" eligibleForTimber={Boolean(edge.IsAutomaticStructuralTimberEligible)}" +
                    $" start={Point(edge.Segment3D.Start, sourceElevation)}" +
                    $" end={Point(edge.Segment3D.End, sourceElevation)}");
            }

            var resolutionByKey = resolution.Edges.ToDictionary(edge => edge.StructuralIdentity);
            foreach (var item in plan.Items.OrderBy(item => item.LogicalKey.ToString(), StringComparer.Ordinal))
            {
                resolutionByKey.TryGetValue(item.LogicalKey, out var edge);
                WriteLine(editor,
                    "ROOF_STRUCT_DESIRED" +
                    $" owner={Token(owner)}" +
                    $" key={Token(item.LogicalKey.ToString())}" +
                    $" timberType={Token(item.ElementType.ToString())}" +
                    $" pathAnchor={NullableNumber(edge?.PhysicalPathAnchorVertexIndex)}" +
                    $" start={Point(item.Segment3D.Start, sourceElevation)}" +
                    $" end={Point(item.Segment3D.End, sourceElevation)}");
            }

            WriteLine(editor,
                "ROOF_STRUCT_DESIRED_SUMMARY" +
                $" owner={Token(owner)}" +
                $" hip={Number(plan.Items.Count(item => item.ElementType == TimberElementType.HipRafter))}" +
                $" valley={Number(plan.Items.Count(item => item.ElementType == TimberElementType.ValleyRafter))}");
        }
        catch (Exception ex)
        {
            WriteLine(editor,
                "ROOF_STRUCT_TRACE_ERROR" +
                $" owner={Token(owner)} stage=topology-desired" +
                $" reason={Token(ex.GetType().Name + ":" + ex.Message)}");
        }
    }

    public static void WriteExistingSet(
        Editor? editor,
        Database database,
        Transaction transaction,
        string owner,
        IEnumerable<ObjectId> ids)
    {
        if (editor is null)
        {
            return;
        }

        foreach (var id in ids.OrderBy(id => id.Handle.Value))
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null)
                {
                    WriteLine(editor,
                        "ROOF_STRUCT_EXISTING" +
                        $" owner={Token(owner)} handle={Token(id.Handle.ToString())}" +
                        " key=- timberType=- start=- end=- elementId=- status=unreadable");
                    continue;
                }

                var structural = RoofStructuralGeneratedStore.Read(entity);
                var timberStore = new AutoCadTimberElementMetadataStore(transaction);
                _ = timberStore.TryRead(entity, out var timber);
                WriteLine(editor,
                    "ROOF_STRUCT_EXISTING" +
                    $" owner={Token(owner)}" +
                    $" handle={Token(entity.Handle.ToString())}" +
                    $" key={Token(structural.Data?.LogicalKey.ToString())}" +
                    $" timberType={Token(timber?.ElementType.ToString())}" +
                    $" start={Start(entity)}" +
                    $" end={End(entity)}" +
                    $" elementId={Token(timber?.ElementId)}" +
                    $" status={Token(ExistingStatus(entity, structural, timber))}");
            }
            catch (Exception ex)
            {
                WriteLine(editor,
                    "ROOF_STRUCT_EXISTING" +
                    $" owner={Token(owner)} handle={Token(id.Handle.ToString())}" +
                    $" key=- timberType=- start=- end=- elementId=- status={Token("error:" + ex.GetType().Name)}");
            }
        }
    }

    public static string Segment(Line line) => Segment(line.StartPoint, line.EndPoint);

    public static string Segment(Point3d start, Point3d end) =>
        Point(start) + "->" + Point(end);

    public static string Segment(
        RoofPoint3D start,
        RoofPoint3D end,
        double sourceElevation) =>
        Point(start, sourceElevation) + "->" + Point(end, sourceElevation);

    public static void WriteReconcile(
        Editor? editor,
        string owner,
        RoofStructuralLogicalKey? key,
        string action,
        string? handleBefore,
        string? handleAfter,
        string? desiredSegment,
        string? existingSegment,
        string reason)
    {
        if (editor is null)
        {
            return;
        }

        WriteLine(editor,
            "ROOF_STRUCT_RECONCILE" +
            $" owner={Token(owner)}" +
            $" key={Token(key?.ToString())}" +
            $" action={Token(action)}" +
            $" handleBefore={Token(handleBefore)}" +
            $" handleAfter={Token(handleAfter)}" +
            $" desired={Token(desiredSegment)}" +
            $" existing={Token(existingSegment)}" +
            $" reason={Token(reason)}");
    }

    public static void WriteRejectedExistingAction(
        Editor? editor,
        Database database,
        Transaction transaction,
        string owner,
        ObjectId id,
        string action,
        string reason)
    {
        if (editor is null)
        {
            return;
        }

        try
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                WriteReconcile(
                    editor, owner, null, action, id.Handle.ToString(), null, null, null, reason);
                return;
            }

            var structural = RoofStructuralGeneratedStore.Read(entity).Data;
            WriteReconcile(
                editor,
                owner,
                structural?.LogicalKey,
                action,
                entity.Handle.ToString(),
                null,
                null,
                entity is Line line ? Segment(line) : null,
                reason);
        }
        catch (Exception ex)
        {
            WriteReconcile(
                editor,
                owner,
                null,
                action,
                id.Handle.ToString(),
                null,
                null,
                null,
                reason + ":trace-" + ex.GetType().Name);
        }
    }

    public static void WriteFinalSetAndSummary(
        Editor? editor,
        Database database,
        Transaction transaction,
        string owner,
        IEnumerable<ObjectId> ids,
        int staleRemoved,
        int created,
        int reused,
        int updated,
        bool groupCanonical,
        bool desiredEqualsFinal)
    {
        if (editor is null)
        {
            return;
        }

        var hip = 0;
        var valley = 0;
        foreach (var id in ids.OrderBy(id => id.Handle.Value))
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var line,
                        database) ||
                    line is null)
                {
                    continue;
                }

                var structural = RoofStructuralGeneratedStore.Read(line).Data;
                var timberStore = new AutoCadTimberElementMetadataStore(transaction);
                _ = timberStore.TryRead(line, out var timber);
                if (timber?.ElementType == TimberElementType.HipRafter)
                {
                    hip++;
                }
                else if (timber?.ElementType == TimberElementType.ValleyRafter)
                {
                    valley++;
                }

                WriteLine(editor,
                    "ROOF_STRUCT_FINAL" +
                    $" owner={Token(owner)}" +
                    $" key={Token(structural?.LogicalKey.ToString())}" +
                    $" type={Token(timber?.ElementType.ToString())}" +
                    $" handle={Token(line.Handle.ToString())}" +
                    $" start={Point(line.StartPoint)}" +
                    $" end={Point(line.EndPoint)}");
            }
            catch (Exception ex)
            {
                WriteLine(editor,
                    "ROOF_STRUCT_TRACE_ERROR" +
                    $" owner={Token(owner)} stage=final" +
                    $" handle={Token(id.Handle.ToString())}" +
                    $" reason={Token(ex.GetType().Name + ":" + ex.Message)}");
            }
        }

        WriteLine(editor,
            "ROOF_STRUCT_FINAL_SUMMARY" +
            $" owner={Token(owner)}" +
            $" hip={Number(hip)} valley={Number(valley)}" +
            $" staleRemoved={Number(staleRemoved)}" +
            $" created={Number(created)} reused={Number(reused)} updated={Number(updated)}" +
            $" groupCanonical={Boolean(groupCanonical)}" +
            $" desiredEqualsFinal={Boolean(desiredEqualsFinal)}");
    }

    private static string ExistingStatus(
        Entity entity,
        RoofStructuralGeneratedStoreReadResult structural,
        TimberElementData? timber)
    {
        if (entity is not Line)
        {
            return "not-line";
        }

        if (structural.Data is null)
        {
            return "structural-" + structural.Error;
        }

        return timber is null ? "timber-unreadable" : "ok";
    }

    private static string Start(Entity entity) => entity is Line line ? Point(line.StartPoint) : "-";
    private static string End(Entity entity) => entity is Line line ? Point(line.EndPoint) : "-";

    private static string Point(RoofPoint2D point, double elevation) =>
        $"({Scalar(point.X)},{Scalar(point.Y)},{Scalar(elevation)})";

    private static string Point(RoofPoint3D point, double elevation) =>
        $"({Scalar(point.X)},{Scalar(point.Y)},{Scalar(point.Z + elevation)})";

    private static string Point(Point3d point) =>
        $"({Scalar(point.X)},{Scalar(point.Y)},{Scalar(point.Z)})";

    private static string Scalar(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string NullableNumber(int? value) => value.HasValue ? Number(value.Value) : "-";
    private static string Boolean(bool value) => value ? "true" : "false";

    private static void WriteLine(Editor editor, string line)
    {
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
#endif
