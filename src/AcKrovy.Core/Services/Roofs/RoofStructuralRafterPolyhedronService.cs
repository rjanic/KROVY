using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Validates identity, physical ordinary contacts and the closed polyhedral body
/// produced by the shared Hip/Valley physical builder. No host objects are used.
/// </summary>
public static class RoofStructuralRafterPolyhedronService
{
    private const double Tolerance = 1e-5;

    public static bool TryBuild(
        RoofStructuralRafterPolyhedronRequest? request,
        out RoofStructuralRafterPolyhedron? polyhedron,
        out string failureReason)
    {
        polyhedron = null;
        failureReason = "InvalidStructuralRequest";
        if (request?.Topology is not { } topology ||
            request.StructuralEdge is not { } resolved ||
            request.OrdinaryMembers is null ||
            !Finite(request.PhysicalEaveElevationMm) ||
            !Finite(request.StructuralWidthMm) || request.StructuralWidthMm <= Tolerance ||
            resolved.TopologyEdgeIndex < 0 || resolved.TopologyEdgeIndex >= topology.Edges.Count ||
            resolved.StructuralIdentity is not { } key ||
            key.Role is not (RoofStructuralRole.Hip or RoofStructuralRole.Valley) ||
            key.BoundaryEdgeIdA <= 0 || key.BoundaryEdgeIdA >= key.BoundaryEdgeIdB ||
            resolved.StructuralRole != key.Role ||
            !Enum.IsDefined(typeof(RoofStructuralHeightMode), request.HeightMode) ||
            !Enum.IsDefined(typeof(LowerEndCutMode), request.LowerEndCutMode) ||
            (request.HeightMode == RoofStructuralHeightMode.Automatic &&
             request.ExplicitVerticalHeightMm.HasValue) ||
            (request.HeightMode == RoofStructuralHeightMode.Explicit &&
             (!request.ExplicitVerticalHeightMm.HasValue ||
              !Finite(request.ExplicitVerticalHeightMm.Value) ||
              request.ExplicitVerticalHeightMm.Value <= Tolerance)))
            return false;

        var edge = topology.Edges[resolved.TopologyEdgeIndex];
        var axis = topology.Segment(edge);
        if (!resolved.IsAutomaticStructuralTimberEligible ||
            !RoofPhysicalStructuralFold.IsTimberEligibleFold(topology, edge, key.Role) ||
            edge.FaceIndices.Count != 2 ||
            edge.FaceIndices[0] == edge.FaceIndices[1] ||
            !Same(axis.Start, resolved.Segment3D.Start) ||
            !Same(axis.End, resolved.Segment3D.End) ||
            edge.FaceIndices.Any(index => index < 0 || index >= topology.Faces.Count))
        {
            failureReason = "StructuralEdgeMismatch";
            return false;
        }

        var anchor = edge.StartNodeIndex < topology.BoundaryVertexCount
            ? edge.StartNodeIndex : edge.EndNodeIndex;
        if (anchor >= topology.BoundaryVertexCount ||
            (edge.StartNodeIndex < topology.BoundaryVertexCount) ==
            (edge.EndNodeIndex < topology.BoundaryVertexCount))
        {
            failureReason = "StructuralEaveAnchorUnresolved";
            return false;
        }
        if (request.UpperNodeMiterPlane is { } miter)
        {
            var upperNode = topology.Nodes[edge.StartNodeIndex == anchor
                ? edge.EndNodeIndex : edge.StartNodeIndex];
            var eaveNode = topology.Nodes[anchor];
            var normal = miter.RetainedNormal;
            var miterNormalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
            if (!Finite(miter.Point) || !Finite(normal) ||
                Math.Abs(miter.Point.X - upperNode.X) > Tolerance ||
                Math.Abs(miter.Point.Y - upperNode.Y) > Tolerance ||
                Math.Abs(normal.Z) > Tolerance ||
                Math.Abs(miterNormalLength - 1d) > Tolerance ||
                (eaveNode.X - upperNode.X) * normal.X +
                (eaveNode.Y - upperNode.Y) * normal.Y <= Tolerance)
            {
                failureReason = "UpperNodeMiterPlaneInvalid";
                return false;
            }
        }
        var boundaryIndices = new List<int>(2);
        foreach (var faceIndex in edge.FaceIndices)
        {
            var face = topology.Faces[faceIndex];
            var matches = topology.Edges.Select((candidate, index) => (candidate, index))
                .Where(item => item.candidate.Kind == RoofTopologyEdgeKind.Eave &&
                    item.candidate.FaceIndices.Contains(face.SourceEdgeIndex) &&
                    (item.candidate.StartNodeIndex == anchor ||
                     item.candidate.EndNodeIndex == anchor))
                .ToArray();
            if (matches.Length != 1)
            {
                failureReason = "AdjacentEaveBoundaryUnresolved";
                return false;
            }
            boundaryIndices.Add(matches[0].index);
        }
        if (boundaryIndices[0] == boundaryIndices[1])
        {
            failureReason = "AdjacentEaveBoundaryAmbiguous";
            return false;
        }

        var expectedRole = key.Role == RoofStructuralRole.Hip
            ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley;
        if (!RoofStructuralOrdinaryContactRules.TrySelectContacts(
                request.OrdinaryMembers,
                axis,
                resolved.TopologyEdgeIndex,
                expectedRole,
                request.StructuralWidthMm,
                out var contacts,
                out failureReason))
            return false;
        var maximumSpan = 0d;
        foreach (var member in contacts)
        {
            var vertices = member.StructuralCut!.CutFaceVertices;
            maximumSpan = Math.Max(maximumSpan,
                vertices.Max(point => point.Z) - vertices.Min(point => point.Z));
        }
        if (request.HeightMode == RoofStructuralHeightMode.Automatic &&
            contacts.Count == 0)
        {
            failureReason = "AutomaticHeightNeedsOrdinaryCuts";
            return false;
        }

        // The shared physical builder currently recognizes topology Hip/Valley.
        // An eligible inclined Ridge is a planner-resolved Hip fold. Reclassify
        // only the transient builder input after checking the real fold above.
        var constructionTopology = edge.Kind == RoofTopologyEdgeKind.Ridge
            ? new RoofTopology(topology.Nodes,
                topology.Edges.Select((item, index) =>
                    index == resolved.TopologyEdgeIndex
                        ? new RoofTopologyEdge(item.StartNodeIndex,
                            item.EndNodeIndex, RoofTopologyEdgeKind.Hip,
                            item.FaceIndices, item.OriginatingBoundaryVertexIndex)
                        : item), topology.Faces,
                topology.BoundaryVertexCount, topology.PitchDegrees)
            : topology;
        if (!RoofStructuralRafterPhysicalBuilder.TryBuild(
                constructionTopology, resolved.TopologyEdgeIndex,
                request.PhysicalEaveElevationMm, request.StructuralWidthMm,
                request.ExplicitVerticalHeightMm, request.OrdinaryMembers,
                request.LowerEndCutMode, request.UpperNodeMiterPlane,
                out var geometry, out failureReason))
            return false;

        if (!RoofStructuralLowerEndProfile.TryResolve(
                geometry!.PhysicalTopAxis ?? geometry.UpperAxis,
                request.PhysicalEaveElevationMm,
                geometry.PhysicalVerticalHeightMm, request.LowerEndCutMode,
                contacts, out var lowerPlanes))
        {
            failureReason = "LowerEndProfileUnresolved";
            return false;
        }
        IReadOnlyList<RoofStructuralRafterClipPlane> envelopePlanes =
            Array.Empty<RoofStructuralRafterClipPlane>();
        if (request.UpperNodeMiterPlane is not null &&
            !RoofStructuralRafterRoofEnvelope.TryResolve(constructionTopology,
                constructionTopology.Edges[resolved.TopologyEdgeIndex],
                request.PhysicalEaveElevationMm, out envelopePlanes))
        {
            failureReason = "UpperRoofEnvelopeUnresolved";
            return false;
        }

        if (geometry is null || geometry.Role != key.Role ||
            geometry.TopologyEdgeIndex != resolved.TopologyEdgeIndex ||
            geometry.UsesExplicitHeight !=
                (request.HeightMode == RoofStructuralHeightMode.Explicit) ||
            geometry.Faces.Count < (key.Role == RoofStructuralRole.Hip ? 6 : 7) ||
            geometry.BodyVertices.Count < 6 ||
            !Finite(geometry.PhysicalVerticalHeightMm) ||
            geometry.PhysicalVerticalHeightMm <= Tolerance ||
            !Same(geometry.UpperAxis.Start,
                new RoofPoint3D(topology.Nodes[anchor].X,
                    topology.Nodes[anchor].Y,
                    topology.Nodes[anchor].Z + request.PhysicalEaveElevationMm)))
        {
            failureReason = "StructuralPolyhedronInvalid";
            return false;
        }
        if (!ValidateFaces(topology, edge, geometry, boundaryIndices))
        {
            failureReason = "StructuralFacesInvalid";
            return false;
        }
        if (!TryCreateConstruction(topology, edge, geometry,
                boundaryIndices, lowerPlanes, envelopePlanes,
                request.UpperNodeMiterPlane,
                out var halves, out var eavePlanes, out var ridgePlane))
        {
            failureReason = "StructuralConvexConstructionInvalid";
            return false;
        }
        polyhedron = new RoofStructuralRafterPolyhedron(key, request.HeightMode,
            geometry, Array.AsReadOnly(boundaryIndices.ToArray()), maximumSpan,
            halves!, eavePlanes!, ridgePlane!, request.UpperNodeMiterPlane,
            request.LowerEndCutMode, lowerPlanes, envelopePlanes);
        failureReason = string.Empty;
        return true;
    }

    private static bool TryCreateConstruction(
        RoofTopology topology, RoofTopologyEdge edge,
        RoofStructuralRafterPhysicalModel geometry,
        IReadOnlyList<int> boundaryIndices,
        IReadOnlyList<RoofStructuralRafterClipPlane> lowerPlanes,
        IReadOnlyList<RoofStructuralRafterClipPlane> envelopePlanes,
        RoofStructuralRafterClipPlane? upperNodeMiterPlane,
        out IReadOnlyList<RoofStructuralRafterConvexHalf>? halves,
        out IReadOnlyList<RoofStructuralRafterClipPlane>? eavePlanes,
        out RoofStructuralRafterClipPlane? ridgePlane)
    {
        halves = null;
        eavePlanes = null;
        ridgePlane = null;
        var axis = geometry.UpperAxis;
        var localStart = topology.Nodes[edge.StartNodeIndex];
        if (Math.Abs(localStart.X - axis.Start.X) > Tolerance ||
            Math.Abs(localStart.Y - axis.Start.Y) > Tolerance)
            localStart = topology.Nodes[edge.EndNodeIndex];
        var physicalDatum = axis.Start.Z - localStart.Z;
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length <= Tolerance) return false;
        var alongX = dx / length;
        var alongY = dy / length;
        var wx = -alongY;
        var wy = alongX;
        var ridge = new RoofStructuralRafterClipPlane(axis.End,
            new RoofPoint3D(-alongX, -alongY, 0d));
        var planes = new List<RoofStructuralRafterClipPlane>(2);
        foreach (var index in boundaryIndices)
        {
            var boundary = topology.Edges[index];
            var start = topology.Nodes[boundary.StartNodeIndex];
            var end = topology.Nodes[boundary.EndNodeIndex];
            var ex = end.X - start.X;
            var ey = end.Y - start.Y;
            var eLength = Math.Sqrt(ex * ex + ey * ey);
            if (!Finite(eLength) || eLength <= Tolerance) return false;
            var nx = -ey / eLength;
            var ny = ex / eLength;
            var towardInterior = (axis.End.X - start.X) * nx +
                (axis.End.Y - start.Y) * ny;
            if (!Finite(towardInterior) || Math.Abs(towardInterior) <= Tolerance)
                return false;
            if (towardInterior < 0d) { nx = -nx; ny = -ny; }
            planes.Add(new RoofStructuralRafterClipPlane(
                new RoofPoint3D(start.X, start.Y, axis.Start.Z),
                new RoofPoint3D(nx, ny, 0d)));
        }
        var bodyStart = axis.Start;
        if (lowerPlanes.Count != 1 &&
            !RoofStructuralRafterPhysicalBuilder.TryResolveEaveSourceStart(
                axis, geometry.WidthMm, planes, out bodyStart))
            return false;

        if (geometry.Role == RoofStructuralRole.Hip)
        {
            var leftFace = edge.FaceIndices.Select(index => topology.Faces[index])
                .SingleOrDefault(face => face.BoundaryNodeIndices.Average(index =>
                    topology.Nodes[index].X) * wx +
                    face.BoundaryNodeIndices.Average(index =>
                    topology.Nodes[index].Y) * wy > axis.Start.X * wx + axis.Start.Y * wy);
            var rightFace = edge.FaceIndices.Select(index => topology.Faces[index])
                .SingleOrDefault(face => face != leftFace);
            if (leftFace is null || rightFace is null ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, leftFace, out var leftNormal) ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, rightFace, out var rightNormal)) return false;
            var leftOrigin = topology.Nodes[leftFace.BoundaryNodeIndices[0]];
            var rightOrigin = topology.Nodes[rightFace.BoundaryNodeIndices[0]];
            double LeftZ(double x, double y) => physicalDatum + leftOrigin.Z -
                (leftNormal.X * (x - leftOrigin.X) +
                 leftNormal.Y * (y - leftOrigin.Y)) / leftNormal.Z;
            double RightZ(double x, double y) => physicalDatum + rightOrigin.Z -
                (rightNormal.X * (x - rightOrigin.X) +
                 rightNormal.Y * (y - rightOrigin.Y)) / rightNormal.Z;
            var overrun = 1d;
            if (upperNodeMiterPlane is not null &&
                !RoofStructuralUpperNodeMiterResolver.TryRequiredOverrunMm(
                    axis, geometry.WidthMm, upperNodeMiterPlane, out overrun))
                return false;
            var endX = axis.End.X + alongX * overrun;
            var endY = axis.End.Y + alongY * overrun;
            var half = geometry.WidthMm / 2d;
            var sl = new RoofPoint3D(bodyStart.X + wx * half,
                bodyStart.Y + wy * half,
                LeftZ(bodyStart.X + wx * half, bodyStart.Y + wy * half));
            var sr = new RoofPoint3D(bodyStart.X - wx * half,
                bodyStart.Y - wy * half,
                RightZ(bodyStart.X - wx * half, bodyStart.Y - wy * half));
            var el = new RoofPoint3D(endX + wx * half, endY + wy * half,
                LeftZ(endX + wx * half, endY + wy * half));
            var er = new RoofPoint3D(endX - wx * half, endY - wy * half,
                RightZ(endX - wx * half, endY - wy * half));
            var height = geometry.PhysicalVerticalHeightMm;
            var source = new[]
            {
                sl, sr, el, er,
                new RoofPoint3D(sl.X, sl.Y, sl.Z - height),
                new RoofPoint3D(sr.X, sr.Y, sr.Z - height),
                new RoofPoint3D(el.X, el.Y, el.Z - height),
                new RoofPoint3D(er.X, er.Y, er.Z - height),
            };
            if (source.Any(point => !Finite(point))) return false;
            var active = planes.Concat(lowerPlanes)
                .Append(upperNodeMiterPlane)
                .Concat(envelopePlanes)
                .Where(plane => plane is not null)
                .Select(plane => new RoofConvexPrismPlaneClipper.Plane(
                    plane!.Point, plane.RetainedNormal)).ToList();
            if (upperNodeMiterPlane is null)
                active.Add(new RoofConvexPrismPlaneClipper.Plane(
                    ridge.Point, ridge.RetainedNormal));
            if (!RoofConvexPrismPlaneClipper.TryClip(source, active,
                    out var clipped, allowNoOpPlanes: true) ||
                clipped is null || clipped.TopFace.Count < 3 ||
                clipped.Body.Any(point => !Finite(point) ||
                    point.Z > LeftZ(point.X, point.Y) + Tolerance ||
                    point.Z > RightZ(point.X, point.Y) + Tolerance) ||
                geometry.BodyVertices.Any(point => !clipped.Body.Any(
                    candidate => Same(point, candidate)))) return false;
            halves = Array.AsReadOnly(new[]
            {
                new RoofStructuralRafterConvexHalf(leftFace.SourceEdgeIndex, 0,
                    Array.AsReadOnly(source), clipped.Body, clipped.TopFace),
            });
            eavePlanes = Array.AsReadOnly(planes.ToArray());
            ridgePlane = ridge;
            return true;
        }

        var parts = new List<RoofStructuralRafterConvexHalf>(2);
        foreach (var faceIndex in edge.FaceIndices)
        {
            var face = topology.Faces[faceIndex];
            if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, face, out var normal)) return false;
            var centroidX = face.BoundaryNodeIndices.Average(index => topology.Nodes[index].X);
            var centroidY = face.BoundaryNodeIndices.Average(index => topology.Nodes[index].Y);
            var sideValue = (centroidX - axis.Start.X) * wx +
                (centroidY - axis.Start.Y) * wy;
            if (!Finite(sideValue) || Math.Abs(sideValue) <= Tolerance) return false;
            var sign = Math.Sign(sideValue);
            var half = geometry.WidthMm / 2d;
            var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
            double RoofZ(double x, double y) =>
                physicalDatum + origin.Z -
                (normal.X * (x - origin.X) +
                 normal.Y * (y - origin.Y)) / normal.Z;
            // A V miter needs the far side of the strip to extend beyond its
            // centerline node. A perpendicular ridge clip at the node would
            // erase that reach and leave a triangular gap at the joint.
            var overrun = 1d;
            if (upperNodeMiterPlane is not null &&
                !RoofStructuralUpperNodeMiterResolver.TryRequiredOverrunMm(
                    axis, geometry.WidthMm, upperNodeMiterPlane, out overrun))
                return false;
            var endX = axis.End.X + alongX * overrun;
            var endY = axis.End.Y + alongY * overrun;
            var endAxisZ = axis.End.Z +
                (axis.End.Z - axis.Start.Z) * overrun / length;
            var startSideX = bodyStart.X + sign * wx * half;
            var startSideY = bodyStart.Y + sign * wy * half;
            var endSideX = endX + sign * wx * half;
            var endSideY = endY + sign * wy * half;
            var startSideZ = RoofZ(startSideX, startSideY);
            var endSideZ = RoofZ(endSideX, endSideY);
            var height = geometry.PhysicalVerticalHeightMm;
            if (!Finite(startSideZ) || !Finite(endSideZ) ||
                startSideZ <= bodyStart.Z - height + Tolerance ||
                endSideZ <= endAxisZ - height + Tolerance)
                return false;
            var source = new[]
            {
                bodyStart,
                new RoofPoint3D(startSideX, startSideY, startSideZ),
                new RoofPoint3D(endX, endY, endAxisZ),
                new RoofPoint3D(endSideX, endSideY, endSideZ),
                new RoofPoint3D(bodyStart.X, bodyStart.Y, bodyStart.Z - height),
                new RoofPoint3D(startSideX, startSideY, bodyStart.Z - height),
                new RoofPoint3D(endX, endY, endAxisZ - height),
                new RoofPoint3D(endSideX, endSideY, endAxisZ - height),
            };
            if (source.Any(point => !Finite(point)) ||
                parts.Any(part => part.SideSign == sign)) return false;
            var active = new List<RoofConvexPrismPlaneClipper.Plane>();
            foreach (var plane in planes)
            {
                var distances = source.Select(point =>
                    (point.X - plane.Point.X) * plane.RetainedNormal.X +
                    (point.Y - plane.Point.Y) * plane.RetainedNormal.Y).ToArray();
                if (distances.Min() < -Tolerance)
                {
                    if (distances.Max() <= Tolerance) return false;
                    active.Add(new RoofConvexPrismPlaneClipper.Plane(
                        plane.Point, plane.RetainedNormal));
                }
            }
            foreach (var plane in lowerPlanes)
                active.Add(new RoofConvexPrismPlaneClipper.Plane(
                    plane.Point, plane.RetainedNormal));
            if (upperNodeMiterPlane is not null)
                active.Add(new RoofConvexPrismPlaneClipper.Plane(
                    upperNodeMiterPlane.Point,
                    upperNodeMiterPlane.RetainedNormal));
            foreach (var plane in envelopePlanes)
                active.Add(new RoofConvexPrismPlaneClipper.Plane(
                    plane.Point, plane.RetainedNormal));
            if (upperNodeMiterPlane is null)
                active.Add(new RoofConvexPrismPlaneClipper.Plane(
                    ridge.Point, ridge.RetainedNormal));
            if (!RoofConvexPrismPlaneClipper.TryClip(source, active,
                    out var clipped, allowNoOpPlanes: true) ||
                clipped is null ||
                clipped.TopFace.Count < 3 ||
                clipped.Body.Any(point => !Finite(point))) return false;
            if (clipped.Body.Any(point =>
                    point.Z > RoofZ(point.X, point.Y) + Tolerance)) return false;
            parts.Add(new RoofStructuralRafterConvexHalf(
                face.SourceEdgeIndex, sign, Array.AsReadOnly(source),
                clipped.Body, clipped.TopFace));
        }
        if (parts.Count != 2) return false;
        if (geometry.BodyVertices.Any(point => !parts.SelectMany(part =>
                part.ClippedBodyVertices).Any(candidate => Same(point, candidate))))
            return false;
        halves = Array.AsReadOnly(parts.OrderByDescending(part => part.SideSign).ToArray());
        eavePlanes = Array.AsReadOnly(planes.ToArray());
        ridgePlane = ridge;
        return true;
    }

    private static bool ValidateFaces(
        RoofTopology topology, RoofTopologyEdge edge,
        RoofStructuralRafterPhysicalModel geometry,
        IReadOnlyList<int> eaveBoundaryIndices)
    {
        var axis = geometry.UpperAxis;
        var localStart = topology.Nodes[edge.StartNodeIndex];
        if (Math.Abs(localStart.X - axis.Start.X) > Tolerance ||
            Math.Abs(localStart.Y - axis.Start.Y) > Tolerance)
            localStart = topology.Nodes[edge.EndNodeIndex];
        var physicalDatum = axis.Start.Z - localStart.Z;
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var run = Math.Sqrt(dx * dx + dy * dy);
        if (run <= Tolerance || !Finite(run)) return false;
        var wx = -dy / run;
        var wy = dx / run;
        var halfWidth = geometry.WidthMm / 2d;
        foreach (var face in geometry.Faces)
        {
            if (face.Vertices.Count < 3 || face.Vertices.Any(point => !Finite(point)))
                return false;
            if (face.Kind is RoofStructuralPhysicalFaceKind.SideLeft or
                RoofStructuralPhysicalFaceKind.SideRight)
            {
                var expected = face.Kind == RoofStructuralPhysicalFaceKind.SideLeft
                    ? halfWidth : -halfWidth;
                if (face.Vertices.Any(point => Math.Abs(
                    (point.X - axis.Start.X) * wx +
                    (point.Y - axis.Start.Y) * wy - expected) > Tolerance))
                    return false;
            }
        }
        foreach (var point in geometry.BodyVertices)
        {
            var lateral = (point.X - axis.Start.X) * wx +
                (point.Y - axis.Start.Y) * wy;
            if (!Finite(point) || Math.Abs(lateral) > halfWidth + Tolerance)
                return false;
            foreach (var index in eaveBoundaryIndices)
            {
                var eave = topology.Edges[index];
                var a = topology.Nodes[eave.StartNodeIndex];
                var b = topology.Nodes[eave.EndNodeIndex];
                var center = topology.Nodes[edge.StartNodeIndex == eave.StartNodeIndex ||
                    edge.StartNodeIndex == eave.EndNodeIndex
                    ? edge.EndNodeIndex : edge.StartNodeIndex];
                var reference = Cross(b.X - a.X, b.Y - a.Y,
                    center.X - a.X, center.Y - a.Y);
                var value = Cross(b.X - a.X, b.Y - a.Y,
                    point.X - a.X, point.Y - a.Y);
                if (Math.Abs(reference) <= Tolerance ||
                    value * Math.Sign(reference) < -Tolerance *
                        Math.Sqrt((b.X - a.X) * (b.X - a.X) +
                            (b.Y - a.Y) * (b.Y - a.Y)))
                    return false;
            }
        }
        if (geometry.Role == RoofStructuralRole.Hip)
        {
            var top = geometry.Faces.Where(face =>
                face.Kind == RoofStructuralPhysicalFaceKind.Top).ToArray();
            if (top.Length != 1 || geometry.UpperLeftEdge is null ||
                geometry.UpperRightEdge is null ||
                geometry.PhysicalTopAxis is null ||
                geometry.Faces.Any(face => face.Kind is
                    RoofStructuralPhysicalFaceKind.TopLeft or
                    RoofStructuralPhysicalFaceKind.TopRight)) return false;
            var left = geometry.UpperLeftEdge;
            var right = geometry.UpperRightEdge;
            var longitudinal = new RoofPoint3D(left.End.X - left.Start.X,
                left.End.Y - left.Start.Y, left.End.Z - left.Start.Z);
            var transverse = new RoofPoint3D(right.Start.X - left.Start.X,
                right.Start.Y - left.Start.Y, right.Start.Z - left.Start.Z);
            var topNormal = new RoofPoint3D(
                longitudinal.Y * transverse.Z - longitudinal.Z * transverse.Y,
                longitudinal.Z * transverse.X - longitudinal.X * transverse.Z,
                longitudinal.X * transverse.Y - longitudinal.Y * transverse.X);
            var topNormalLength = Math.Sqrt(topNormal.X * topNormal.X +
                topNormal.Y * topNormal.Y + topNormal.Z * topNormal.Z);
            if (!Finite(topNormalLength) || topNormalLength <= Tolerance ||
                top[0].Vertices.Any(point => Math.Abs(
                    (point.X - left.Start.X) * topNormal.X +
                    (point.Y - left.Start.Y) * topNormal.Y +
                    (point.Z - left.Start.Z) * topNormal.Z) / topNormalLength >
                    Tolerance)) return false;
            foreach (var (upperEdge, side) in new[] { (left, 1d), (right, -1d) })
            {
                var matching = edge.FaceIndices.Where(index =>
                {
                    var face = topology.Faces[index];
                    var centroidX = face.BoundaryNodeIndices.Average(node =>
                        topology.Nodes[node].X);
                    var centroidY = face.BoundaryNodeIndices.Average(node =>
                        topology.Nodes[node].Y);
                    return Math.Sign((centroidX - axis.Start.X) * wx +
                        (centroidY - axis.Start.Y) * wy) == side;
                }).ToArray();
                if (matching.Length != 1 ||
                    !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                        topology, topology.Faces[matching[0]], out var normal))
                    return false;
                var origin = topology.Nodes[topology.Faces[matching[0]]
                    .BoundaryNodeIndices[0]];
                foreach (var point in new[] { upperEdge.Start, upperEdge.End })
                    if (Math.Abs(normal.X * (point.X - origin.X) +
                        normal.Y * (point.Y - origin.Y) +
                        normal.Z * (point.Z - origin.Z - physicalDatum)) >
                        Tolerance) return false;
            }
            foreach (var index in edge.FaceIndices)
            {
                var face = topology.Faces[index];
                if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                        topology, face, out var normal)) return false;
                var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
                if (geometry.BodyVertices.Any(point =>
                    normal.X * (point.X - origin.X) +
                    normal.Y * (point.Y - origin.Y) +
                    normal.Z * (point.Z - origin.Z - physicalDatum) >
                    Tolerance)) return false;
            }
            return true;
        }
        var topFaces = geometry.Faces.Where(face => face.Kind is
            RoofStructuralPhysicalFaceKind.TopLeft or
            RoofStructuralPhysicalFaceKind.TopRight).ToArray();
        if (topFaces.Length != 2) return false;
        foreach (var topFace in topFaces)
        {
            var side = topFace.Kind == RoofStructuralPhysicalFaceKind.TopLeft
                ? 1d : -1d;
            var matching = edge.FaceIndices.Where(index =>
            {
                var face = topology.Faces[index];
                var centroidX = face.BoundaryNodeIndices.Average(node => topology.Nodes[node].X);
                var centroidY = face.BoundaryNodeIndices.Average(node => topology.Nodes[node].Y);
                return Math.Sign((centroidX - axis.Start.X) * wx +
                    (centroidY - axis.Start.Y) * wy) == side;
            }).ToArray();
            if (matching.Length != 1) return false;
            var sourceFace = topology.Faces[matching[0]];
            if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, sourceFace, out var normal)) return false;
            var origin = topology.Nodes[sourceFace.BoundaryNodeIndices[0]];
            if (topFace.Vertices.Any(point => Math.Abs(
                normal.X * (point.X - origin.X) +
                normal.Y * (point.Y - origin.Y) +
                normal.Z * (point.Z - origin.Z -
                    physicalDatum)) > Tolerance))
                return false;
        }
        return true;
    }

    private static double Cross(double ax, double ay, double bx, double by) =>
        ax * by - ay * bx;
    private static bool Same(RoofPoint3D a, RoofPoint3D b) =>
        a.DistanceTo(b) <= Tolerance;
    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);
    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
