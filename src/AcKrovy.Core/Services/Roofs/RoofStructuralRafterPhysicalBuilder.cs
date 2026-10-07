using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Builds one CAD-neutral Hip or Valley body from its topology fold and actual ordinary cuts.</summary>
public static class RoofStructuralRafterPhysicalBuilder
{
    private const double Tolerance = 1e-6;

    public static bool TryBuild(
        RoofTopology? topology,
        int topologyEdgeIndex,
        double resolvedEaveElevationMm,
        double widthMm,
        double? explicitVerticalHeightMm,
        IReadOnlyList<RoofAutomaticRafterPhysicalMember>? ordinaryMembers,
        out RoofStructuralRafterPhysicalModel? model,
        out string failureReason) => TryBuild(topology, topologyEdgeIndex,
            resolvedEaveElevationMm, widthMm, explicitVerticalHeightMm,
            ordinaryMembers, LowerEndCutMode.Vertical, null, out model, out failureReason);

    public static bool TryBuild(
        RoofTopology? topology,
        int topologyEdgeIndex,
        double resolvedEaveElevationMm,
        double widthMm,
        double? explicitVerticalHeightMm,
        IReadOnlyList<RoofAutomaticRafterPhysicalMember>? ordinaryMembers,
        RoofStructuralRafterClipPlane? upperNodeMiterPlane,
        out RoofStructuralRafterPhysicalModel? model,
        out string failureReason) => TryBuild(topology, topologyEdgeIndex,
            resolvedEaveElevationMm, widthMm, explicitVerticalHeightMm,
            ordinaryMembers, LowerEndCutMode.Vertical, upperNodeMiterPlane,
            out model, out failureReason);

    public static bool TryBuild(
        RoofTopology? topology,
        int topologyEdgeIndex,
        double resolvedEaveElevationMm,
        double widthMm,
        double? explicitVerticalHeightMm,
        IReadOnlyList<RoofAutomaticRafterPhysicalMember>? ordinaryMembers,
        LowerEndCutMode lowerEndCutMode,
        RoofStructuralRafterClipPlane? upperNodeMiterPlane,
        out RoofStructuralRafterPhysicalModel? model,
        out string failureReason)
    {
        model = null;
        failureReason = "InvalidStructuralInput";
        if (topology is null || ordinaryMembers is null ||
            topologyEdgeIndex < 0 || topologyEdgeIndex >= topology.Edges.Count ||
            !Enum.IsDefined(typeof(LowerEndCutMode), lowerEndCutMode) ||
            !Finite(resolvedEaveElevationMm) || !Finite(widthMm) || widthMm <= Tolerance ||
            (explicitVerticalHeightMm.HasValue &&
             (!Finite(explicitVerticalHeightMm.Value) || explicitVerticalHeightMm.Value <= Tolerance)))
            return false;

        var edge = topology.Edges[topologyEdgeIndex];
        if (edge.Kind is not (RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley) ||
            edge.FaceIndices.Count != 2 || edge.FaceIndices[0] == edge.FaceIndices[1])
        {
            failureReason = "UnsupportedStructuralEdge";
            return false;
        }
        var role = edge.Kind == RoofTopologyEdgeKind.Hip
            ? RoofStructuralRole.Hip : RoofStructuralRole.Valley;
        var boundaryAtStart = edge.StartNodeIndex < topology.BoundaryVertexCount;
        var boundaryAtEnd = edge.EndNodeIndex < topology.BoundaryVertexCount;
        if (boundaryAtStart == boundaryAtEnd)
        {
            failureReason = "StructuralEaveAnchorUnresolved";
            return false;
        }
        var localAxis = topology.Segment(edge);
        var localStart = boundaryAtStart ? localAxis.Start : localAxis.End;
        var localEnd = boundaryAtStart ? localAxis.End : localAxis.Start;
        var dx = localEnd.X - localStart.X;
        var dy = localEnd.Y - localStart.Y;
        var run = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(run) || run <= Tolerance ||
            !Finite(localStart) || !Finite(localEnd))
        {
            failureReason = "StructuralAxisDegenerate";
            return false;
        }
        var along = new RoofPoint3D(dx / run, dy / run, 0d);
        var left = new RoofPoint3D(-along.Y, along.X, 0d);
        var sourceFaces = new RoofTopologyFace[2];
        var faceNormals = new RoofFaceUnitNormal[2];
        var faceOrigins = new RoofPoint3D[2];
        var sideFaces = new RoofTopologyFace?[2]; // [0] left, [1] right
        var sideNormals = new RoofFaceUnitNormal[2];
        var sideOrigins = new RoofPoint3D[2];
        for (var i = 0; i < 2; i++)
        {
            var sourceIndex = edge.FaceIndices[i];
            var face = topology.Faces.SingleOrDefault(item => item.SourceEdgeIndex == sourceIndex);
            if (face is null || !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, face, out var normal))
            {
                failureReason = "AdjacentRoofPlaneUnresolved";
                return false;
            }
            var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
            var centroidX = face.BoundaryNodeIndices.Average(index => topology.Nodes[index].X);
            var centroidY = face.BoundaryNodeIndices.Average(index => topology.Nodes[index].Y);
            var side = left.X * (centroidX - localStart.X) +
                       left.Y * (centroidY - localStart.Y);
            if (!Finite(side) || Math.Abs(side) <= Tolerance)
            {
                failureReason = "AdjacentRoofSideAmbiguous";
                return false;
            }
            var slot = side > 0d ? 0 : 1;
            if (sideFaces[slot] is not null)
            {
                failureReason = "AdjacentRoofSideAmbiguous";
                return false;
            }
            sourceFaces[i] = face;
            faceNormals[i] = normal;
            faceOrigins[i] = origin;
            sideFaces[slot] = face;
            sideNormals[slot] = normal;
            sideOrigins[slot] = origin;
        }
        if (sideFaces[0] is null || sideFaces[1] is null)
        {
            failureReason = "AdjacentRoofSideAmbiguous";
            return false;
        }

        double HeightAt(int slot, double x, double y) =>
            resolvedEaveElevationMm + sideOrigins[slot].Z -
            (sideNormals[slot].X * (x - sideOrigins[slot].X) +
             sideNormals[slot].Y * (y - sideOrigins[slot].Y)) / sideNormals[slot].Z;
        var startZ0 = HeightAt(0, localStart.X, localStart.Y);
        var startZ1 = HeightAt(1, localStart.X, localStart.Y);
        var endZ0 = HeightAt(0, localEnd.X, localEnd.Y);
        var endZ1 = HeightAt(1, localEnd.X, localEnd.Y);
        if (!Finite(startZ0) || !Finite(startZ1) || !Finite(endZ0) || !Finite(endZ1) ||
            Math.Abs(startZ0 - startZ1) > Tolerance ||
            Math.Abs(endZ0 - endZ1) > Tolerance ||
            Math.Abs(startZ0 - (localStart.Z + resolvedEaveElevationMm)) > Tolerance ||
            Math.Abs(endZ0 - (localEnd.Z + resolvedEaveElevationMm)) > Tolerance)
        {
            failureReason = "AdjacentRoofPlanesDoNotIntersectOnAxis";
            return false;
        }
        var axis = new RoofSegment3D(
            new RoofPoint3D(localStart.X, localStart.Y, startZ0),
            new RoofPoint3D(localEnd.X, localEnd.Y, endZ0));

        var eavePlanes = new List<(RoofPoint3D Point, RoofPoint3D Normal)>();
        foreach (var face in sideFaces)
        {
            var matches = topology.Edges.Where(candidate =>
                candidate.Kind == RoofTopologyEdgeKind.Eave &&
                candidate.FaceIndices.Contains(face!.SourceEdgeIndex) &&
                (candidate.StartNodeIndex == (boundaryAtStart ? edge.StartNodeIndex : edge.EndNodeIndex) ||
                 candidate.EndNodeIndex == (boundaryAtStart ? edge.StartNodeIndex : edge.EndNodeIndex)))
                .ToArray();
            if (matches.Length != 1)
            {
                failureReason = "AdjacentEaveBoundaryUnresolved";
                return false;
            }
            var eave = topology.Segment(matches[0]);
            var ex = eave.End.X - eave.Start.X;
            var ey = eave.End.Y - eave.Start.Y;
            var length = Math.Sqrt(ex * ex + ey * ey);
            if (!Finite(length) || length <= Tolerance)
            {
                failureReason = "AdjacentEaveBoundaryDegenerate";
                return false;
            }
            var inward = new RoofPoint3D(-ey / length, ex / length, 0d);
            var interior = face!.BoundaryNodeIndices
                .Select(index => topology.Nodes[index])
                .FirstOrDefault(point => Math.Abs(Cross2(ex, ey,
                    point.X - eave.Start.X, point.Y - eave.Start.Y)) > Tolerance);
            if (interior == default)
            {
                failureReason = "AdjacentEaveBoundaryDegenerate";
                return false;
            }
            if (Dot(Subtract(interior, eave.Start), inward) < 0d)
                inward = new RoofPoint3D(-inward.X, -inward.Y, 0d);
            eavePlanes.Add((eave.Start, inward));
        }

        var expectedBoundaryRole = role == RoofStructuralRole.Hip
            ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley;
        // Index-only TopologyEdgeIndex is not enough: accepted MOVE can translate a
        // StructuralCut off the Hip/Valley side. Non-contacting overrides are skipped;
        // on-edge but off-side cuts remain HardFailure.
        if (!RoofStructuralOrdinaryContactRules.TrySelectContacts(
                ordinaryMembers,
                axis,
                topologyEdgeIndex,
                expectedBoundaryRole,
                widthMm,
                out var contactingOrdinaries,
                out failureReason))
            return false;

        var requiredHeight = double.NegativeInfinity;
        foreach (var ordinary in contactingOrdinaries)
        {
            var cut = ordinary.StructuralCut!;
            var slot = sideFaces[0]!.SourceEdgeIndex == ordinary.SourceFaceIndex ? 0 :
                sideFaces[1]!.SourceEdgeIndex == ordinary.SourceFaceIndex ? 1 : -1;
            if (slot < 0)
            {
                failureReason = "OrdinaryCutFaceMismatch";
                return false;
            }
            var sideNormal = slot == 0 ? left : new RoofPoint3D(-left.X, -left.Y, 0d);
            var span = cut.CutFaceVertices.Max(point => point.Z) -
                       cut.CutFaceVertices.Min(point => point.Z);
            // The Hip top is one chord between the two roof-plane contacts.
            // Its side height follows that side's plane, so the required
            // vertical timber depth is measured at each actual cut vertex.
            // Valley retains the existing folded-top construction.
            var demand = role == RoofStructuralRole.Hip
                ? cut.CutFaceVertices.Max(point =>
                    HeightAt(slot, point.X, point.Y) - point.Z)
                : span + axis.Start.Z - HeightAt(slot,
                    axis.Start.X + sideNormal.X * widthMm / 2d,
                    axis.Start.Y + sideNormal.Y * widthMm / 2d);
            if (!Finite(demand) || demand <= Tolerance)
            {
                failureReason = "AutomaticHeightInvalid";
                return false;
            }
            requiredHeight = Math.Max(requiredHeight, demand);
        }
        if (!Finite(requiredHeight) && !explicitVerticalHeightMm.HasValue)
        {
            failureReason = "AutomaticHeightNeedsOrdinaryCuts";
            return false;
        }
        if (!Finite(requiredHeight))
            requiredHeight = 0d;
        var height = explicitVerticalHeightMm ?? requiredHeight;
        var warning = explicitVerticalHeightMm.HasValue
            ? height + Tolerance < requiredHeight
                ? "ExplicitHeightBelowOrdinaryCutRequirement"
                : "ExplicitHeightOverridesAutomatic"
            : null;

        RoofPoint3D At(RoofPoint3D center, int slot) =>
            new(center.X + left.X * (slot == 0 ? 1d : -1d) * widthMm / 2d,
                center.Y + left.Y * (slot == 0 ? 1d : -1d) * widthMm / 2d,
                HeightAt(slot,
                    center.X + left.X * (slot == 0 ? 1d : -1d) * widthMm / 2d,
                    center.Y + left.Y * (slot == 0 ? 1d : -1d) * widthMm / 2d));
        RoofPoint3D TopMidpoint(RoofPoint3D center)
        {
            var l = At(center, 0);
            var r = At(center, 1);
            return new RoofPoint3D(center.X, center.Y, (l.Z + r.Z) / 2d);
        }
        var physicalTopAxis = role == RoofStructuralRole.Hip
            ? new RoofSegment3D(TopMidpoint(axis.Start), TopMidpoint(axis.End))
            : axis;
        var bodyEnd = axis.End;
        if (upperNodeMiterPlane is not null)
        {
            if (!RoofStructuralUpperNodeMiterResolver.TryRequiredOverrunMm(
                    axis, widthMm, upperNodeMiterPlane, out var overrunMm))
            {
                failureReason = "UpperNodeMiterOverrunInvalid";
                return false;
            }
            bodyEnd = new RoofPoint3D(
                axis.End.X + along.X * overrunMm,
                axis.End.Y + along.Y * overrunMm,
                axis.End.Z + (axis.End.Z - axis.Start.Z) * overrunMm / run);
        }
        var bodyStart = axis.Start;
        if (lowerEndCutMode != LowerEndCutMode.Horizontal &&
            !TryResolveEaveSourceStart(axis, widthMm,
                eavePlanes.Select(plane => new RoofStructuralRafterClipPlane(
                    plane.Point, plane.Normal)).ToArray(), out bodyStart))
        {
            failureReason = "EaveSourceOverrunInvalid";
            return false;
        }
        var sl = At(bodyStart, 0);
        var sr = At(bodyStart, 1);
        var el = At(bodyEnd, 0);
        var er = At(bodyEnd, 1);
        var hip = role == RoofStructuralRole.Hip;
        var sbl = new RoofPoint3D(sl.X, sl.Y,
            (hip ? sl.Z : bodyStart.Z) - height);
        var sbr = new RoofPoint3D(sr.X, sr.Y,
            (hip ? sr.Z : bodyStart.Z) - height);
        var ebl = new RoofPoint3D(el.X, el.Y,
            (hip ? el.Z : bodyEnd.Z) - height);
        var ebr = new RoofPoint3D(er.X, er.Y,
            (hip ? er.Z : bodyEnd.Z) - height);
        var faces = hip
            ? new List<RoofStructuralPhysicalFace>
            {
                Face(RoofStructuralPhysicalFaceKind.Top, sl, el, er, sr),
                Face(RoofStructuralPhysicalFaceKind.SideLeft, sl, el, ebl, sbl),
                Face(RoofStructuralPhysicalFaceKind.SideRight, sbr, ebr, er, sr),
                Face(RoofStructuralPhysicalFaceKind.Bottom, sbl, ebl, ebr, sbr),
                Face(RoofStructuralPhysicalFaceKind.EaveClip, sl, sbl, sbr, sr),
                Face(RoofStructuralPhysicalFaceKind.RidgeEnd, er, ebr, ebl, el),
            }
            : new List<RoofStructuralPhysicalFace>
            {
                Face(RoofStructuralPhysicalFaceKind.TopLeft, bodyStart, bodyEnd, el, sl),
                Face(RoofStructuralPhysicalFaceKind.TopRight, sr, er, bodyEnd, bodyStart),
                Face(RoofStructuralPhysicalFaceKind.SideLeft, sl, el, ebl, sbl),
                Face(RoofStructuralPhysicalFaceKind.SideRight, sbr, ebr, er, sr),
                Face(RoofStructuralPhysicalFaceKind.Bottom, sbl, ebl, ebr, sbr),
                Face(RoofStructuralPhysicalFaceKind.EaveClip, bodyStart, sl, sbl, sbr, sr),
                Face(RoofStructuralPhysicalFaceKind.RidgeEnd, bodyEnd, er, ebr, ebl, el),
            };
        foreach (var plane in eavePlanes)
        {
            if (!TryClip(faces, plane.Point, plane.Normal, out var clipped))
            {
                failureReason = "EaveClipDegenerate";
                return false;
            }
            faces = clipped!;
        }
        if (!RoofStructuralLowerEndProfile.TryResolve(physicalTopAxis,
                resolvedEaveElevationMm, height, lowerEndCutMode, contactingOrdinaries,
                out var lowerPlanes))
        {
            failureReason = "LowerEndProfileUnresolved";
            return false;
        }
        for (var index = 0; index < lowerPlanes.Count; index++)
        {
            var plane = lowerPlanes[index];
            if (!TryClip(faces, plane.Point, plane.RetainedNormal,
                    index == 0 ? RoofStructuralPhysicalFaceKind.LowerEndProfile
                        : RoofStructuralPhysicalFaceKind.BottomTrim,
                    out var lowerClipped))
            {
                failureReason = "LowerEndProfileDegenerate";
                return false;
            }
            faces = lowerClipped!;
        }
        if (upperNodeMiterPlane is not null)
        {
            if (!TryClip(faces, upperNodeMiterPlane.Point,
                    upperNodeMiterPlane.RetainedNormal,
                    RoofStructuralPhysicalFaceKind.UpperNodeMiter,
                    out var miterClipped))
            {
                failureReason = "UpperNodeMiterDegenerate";
                return false;
            }
            faces = miterClipped!;
        }
        if (upperNodeMiterPlane is not null)
        {
            if (!RoofStructuralRafterRoofEnvelope.TryResolve(topology, edge,
                    resolvedEaveElevationMm, out var envelopePlanes))
            {
                failureReason = "UpperRoofEnvelopeUnresolved";
                return false;
            }
            foreach (var plane in envelopePlanes)
            {
                if (!TryClip(faces, plane.Point, plane.RetainedNormal,
                        RoofStructuralPhysicalFaceKind.RoofEnvelopeClip,
                        out var envelopeClipped))
                {
                    failureReason = "UpperRoofEnvelopeDegenerate";
                    return false;
                }
                faces = envelopeClipped!;
            }
        }
        if ((hip
                ? faces.Count(face => face.Kind == RoofStructuralPhysicalFaceKind.Top) != 1
                : faces.Count(face => face.Kind is RoofStructuralPhysicalFaceKind.TopLeft or
                    RoofStructuralPhysicalFaceKind.TopRight) != 2) ||
            (upperNodeMiterPlane is null &&
             faces.All(face => face.Kind != RoofStructuralPhysicalFaceKind.RidgeEnd)))
        {
            failureReason = "StructuralBodyDegenerate";
            return false;
        }
        var vertices = new List<RoofPoint3D>();
        foreach (var point in faces.SelectMany(face => face.Vertices))
            AddUnique(vertices, point);
        if (vertices.Count < 6 || vertices.Any(point => !Finite(point)))
        {
            failureReason = "StructuralBodyDegenerate";
            return false;
        }
        model = new RoofStructuralRafterPhysicalModel(role, topologyEdgeIndex,
            axis, widthMm, height, requiredHeight, explicitVerticalHeightMm.HasValue,
            warning, faces.AsReadOnly(), vertices.AsReadOnly(),
            hip ? new RoofSegment3D(At(axis.Start, 0), At(axis.End, 0)) : null,
            hip ? new RoofSegment3D(At(axis.Start, 1), At(axis.End, 1)) : null,
            hip ? physicalTopAxis : null);
        failureReason = string.Empty;
        return true;
    }

    private static RoofStructuralPhysicalFace Face(
        RoofStructuralPhysicalFaceKind kind, params RoofPoint3D[] vertices) =>
        new(kind, Array.AsReadOnly(vertices));

    internal static bool TryResolveEaveSourceStart(RoofSegment3D axis,
        double widthMm, IReadOnlyList<RoofStructuralRafterClipPlane> eavePlanes,
        out RoofPoint3D start)
    {
        start = default;
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var run = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(run) || run <= Tolerance || !Finite(widthMm) ||
            widthMm <= Tolerance || eavePlanes.Count != 2) return false;
        var alongX = dx / run;
        var alongY = dy / run;
        var sideX = -alongY;
        var sideY = alongX;
        var overrun = 1d;
        foreach (var plane in eavePlanes)
        {
            var inward = plane.RetainedNormal.X * alongX +
                plane.RetainedNormal.Y * alongY;
            var lateral = Math.Abs(plane.RetainedNormal.X * sideX +
                plane.RetainedNormal.Y * sideY) * widthMm / 2d;
            if (!Finite(inward) || inward <= Tolerance || !Finite(lateral))
                return false;
            // Start both strip sides outside each eave half-space. Their
            // eventual plan ends are then made by the boundaries, never by
            // the temporary source prism end face.
            overrun = Math.Max(overrun, (lateral + 1d) / inward);
        }
        start = new RoofPoint3D(axis.Start.X - alongX * overrun,
            axis.Start.Y - alongY * overrun,
            axis.Start.Z - (axis.End.Z - axis.Start.Z) * overrun / run);
        return Finite(start);
    }

    private static bool TryClip(List<RoofStructuralPhysicalFace> faces,
        RoofPoint3D planePoint, RoofPoint3D normal,
        out List<RoofStructuralPhysicalFace>? clipped) =>
        TryClip(faces, planePoint, normal,
            RoofStructuralPhysicalFaceKind.EaveClip, out clipped);

    private static bool TryClip(List<RoofStructuralPhysicalFace> faces,
        RoofPoint3D planePoint, RoofPoint3D normal,
        RoofStructuralPhysicalFaceKind capKind,
        out List<RoofStructuralPhysicalFace>? clipped)
    {
        clipped = null;
        var output = new List<RoofStructuralPhysicalFace>();
        var intersections = new List<RoofPoint3D>();
        var hasOutside = false;
        var hasInside = false;
        foreach (var face in faces)
        {
            var polygon = face.Vertices;
            var kept = new List<RoofPoint3D>();
            var previous = polygon[polygon.Count - 1];
            var prior = Dot(Subtract(previous, planePoint), normal);
            foreach (var current in polygon)
            {
                var distance = Dot(Subtract(current, planePoint), normal);
                hasOutside |= distance < -Tolerance;
                hasInside |= distance > Tolerance;
                if ((prior < -Tolerance && distance > Tolerance) ||
                    (prior > Tolerance && distance < -Tolerance))
                {
                    var fraction = prior / (prior - distance);
                    var crossing = new RoofPoint3D(
                        previous.X + fraction * (current.X - previous.X),
                        previous.Y + fraction * (current.Y - previous.Y),
                        previous.Z + fraction * (current.Z - previous.Z));
                    AddUnique(kept, crossing);
                    AddUnique(intersections, crossing);
                }
                if (distance >= -Tolerance)
                {
                    AddUnique(kept, current);
                    if (Math.Abs(distance) <= Tolerance)
                        AddUnique(intersections, current);
                }
                previous = current;
                prior = distance;
            }
            if (kept.Count >= 3 && PolygonArea(kept) > Tolerance)
                output.Add(new RoofStructuralPhysicalFace(face.Kind, kept.AsReadOnly()));
        }
        if (!hasInside || output.Count < 5)
            return false;
        if (hasOutside)
        {
            if (intersections.Count < 3)
                return false;
            var centroid = new RoofPoint3D(intersections.Average(point => point.X),
                intersections.Average(point => point.Y), intersections.Average(point => point.Z));
            // A WCS-horizontal cut has no XY tangent. Pick a nonparallel
            // reference axis so both vertical and horizontal caps order well.
            var tangent = Math.Abs(normal.Z) > 0.9d
                ? new RoofPoint3D(1d, 0d, 0d)
                : new RoofPoint3D(-normal.Y, normal.X, 0d);
            var bitangent = new RoofPoint3D(
                normal.Y * tangent.Z - normal.Z * tangent.Y,
                normal.Z * tangent.X - normal.X * tangent.Z,
                normal.X * tangent.Y - normal.Y * tangent.X);
            var ordered = intersections.OrderBy(point => Math.Atan2(
                Dot(Subtract(point, centroid), bitangent),
                Dot(Subtract(point, centroid), tangent))).ToArray();
            if (PolygonArea(ordered) <= Tolerance)
                return false;
            output.Add(Face(capKind, ordered));
        }
        clipped = output;
        return true;
    }

    private static double PolygonArea(IReadOnlyList<RoofPoint3D> points)
    {
        var total = new RoofPoint3D(0d, 0d, 0d);
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            total = new RoofPoint3D(total.X + a.Y * b.Z - a.Z * b.Y,
                total.Y + a.Z * b.X - a.X * b.Z,
                total.Z + a.X * b.Y - a.Y * b.X);
        }
        return Math.Sqrt(Dot(total, total)) / 2d;
    }

    private static void AddUnique(List<RoofPoint3D> points, RoofPoint3D candidate)
    {
        if (!points.Any(point => point.DistanceTo(candidate) <= Tolerance))
            points.Add(candidate);
    }

    private static RoofPoint3D Add(RoofPoint3D point, RoofPoint3D direction, double scale) =>
        new(point.X + direction.X * scale, point.Y + direction.Y * scale,
            point.Z + direction.Z * scale);
    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Cross2(double ax, double ay, double bx, double by) => ax * by - ay * bx;
    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);
    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
