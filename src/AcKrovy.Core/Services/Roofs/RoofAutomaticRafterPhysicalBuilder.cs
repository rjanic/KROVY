using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Derives independent Z=0 plan axes and simple physical prisms from one
/// ordinary-rafter layout. Roof topology supplies the physical upper face.
/// </summary>
public static class RoofAutomaticRafterPhysicalBuilder
{
    private const double Tolerance = 1e-9;

    public static bool TryBuild(
        string ownerReference,
        RoofTopology topology,
        RoofFaceRafterLayout faceLayout,
        RoofRafterLayout generatedLayout,
        double eaveElevationMm,
        double widthMm,
        double heightMm,
        RoofAutomaticRafterPhysicalSettings settings,
        out RoofAutomaticRafterPhysicalModel? model) =>
        TryBuild(ownerReference, topology, faceLayout, generatedLayout,
            eaveElevationMm, widthMm, heightMm, settings, null, out model);

    public static bool TryBuild(
        string ownerReference,
        RoofTopology topology,
        RoofFaceRafterLayout faceLayout,
        RoofRafterLayout generatedLayout,
        double eaveElevationMm,
        double widthMm,
        double heightMm,
        RoofAutomaticRafterPhysicalSettings settings,
        RoofGeneratedMemberReplayPlan? replayPlan,
        out RoofAutomaticRafterPhysicalModel? model) =>
        TryBuild(ownerReference, topology, faceLayout, generatedLayout,
            eaveElevationMm, widthMm, heightMm, settings, replayPlan,
            out model, out _);

    public static bool TryBuild(
        string ownerReference,
        RoofTopology topology,
        RoofFaceRafterLayout faceLayout,
        RoofRafterLayout generatedLayout,
        double eaveElevationMm,
        double widthMm,
        double heightMm,
        RoofAutomaticRafterPhysicalSettings settings,
        RoofGeneratedMemberReplayPlan? replayPlan,
        out RoofAutomaticRafterPhysicalModel? model,
        out string failureReason) =>
        TryBuild(ownerReference, topology, faceLayout, generatedLayout,
            eaveElevationMm, widthMm, heightMm, settings, replayPlan,
            structuralSources: null, out model, out failureReason);

    public static bool TryBuild(
        string ownerReference,
        RoofTopology topology,
        RoofFaceRafterLayout faceLayout,
        RoofRafterLayout generatedLayout,
        double eaveElevationMm,
        double widthMm,
        double heightMm,
        RoofAutomaticRafterPhysicalSettings settings,
        RoofGeneratedMemberReplayPlan? replayPlan,
        IReadOnlyList<RoofStructuralRafterTrimSource>? structuralSources,
        out RoofAutomaticRafterPhysicalModel? model,
        out string failureReason)
    {
        model = null;
        failureReason = "InvalidPhysicalRafterInput";
        if (string.IsNullOrWhiteSpace(ownerReference) || topology is null ||
            faceLayout is null || generatedLayout is null || settings is null ||
            generatedLayout.Signature != faceLayout.Signature ||
            !Finite(eaveElevationMm) ||
            !Finite(widthMm) || widthMm <= 0d ||
            !Finite(heightMm) || heightMm <= 0d ||
            !Enum.IsDefined(typeof(LowerEndCutMode), settings.LowerEndCutMode) ||
            !Enum.IsDefined(typeof(RidgeJoinMode), settings.RidgeJoinMode))
        {
            return false;
        }

        var faces = topology.Faces.ToDictionary(face => face.SourceEdgeIndex);
        var replayByKey = replayPlan?.Items.ToDictionary(item => item.Rafter.LogicalKey);
        if (replayPlan is { IsValid: false } ||
            (replayByKey is not null && replayByKey.Count != generatedLayout.Rafters.Count))
        {
            return false;
        }
        var members = new List<RoofAutomaticRafterPhysicalMember>(generatedLayout.Rafters.Count);
        var seen = new HashSet<(RafterRoofFace Face, int Station)>();
        foreach (var rafter in generatedLayout.Rafters)
        {
            if (rafter.Face is not (RafterRoofFace.Face0 or RafterRoofFace.Face1) ||
                rafter.StationIndex < 0 ||
                !seen.Add((rafter.Face, rafter.StationIndex)) ||
                !TryResolveFaceSegment(faceLayout, rafter, out var segment))
            {
                return false;
            }

            if (rafter.PlanStart.DistanceTo(segment.PlanStart) > Tolerance ||
                rafter.PlanEnd.DistanceTo(segment.PlanEnd) > Tolerance)
            {
                return false;
            }
            if (!faces.TryGetValue(segment.SourceFaceIndex, out var face) ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, face, out var normal) ||
                face.BoundaryNodeIndices.Count < 3)
            {
                return false;
            }

            var planStart = segment.PlanStart;
            var planEnd = segment.PlanEnd;
            if (replayByKey is not null)
            {
                if (!replayByKey.TryGetValue(rafter.LogicalKey, out var replay) ||
                    replay.Rafter != rafter)
                {
                    return false;
                }
                if (replay.Geometry is not { } applied)
                {
                    continue;
                }
                if (!Finite(applied.Start) || !Finite(applied.End) ||
                    Math.Abs(applied.Start.Z) > Tolerance ||
                    Math.Abs(applied.End.Z) > Tolerance)
                {
                    return false;
                }
                planStart = new RoofPoint2D(applied.Start.X, applied.Start.Y);
                planEnd = new RoofPoint2D(applied.End.X, applied.End.Y);
            }

            var semanticAxis = new RoofSegment3D(
                new RoofPoint3D(planStart.X, planStart.Y, 0d),
                new RoofPoint3D(planEnd.X, planEnd.Y, 0d));
            if (replayByKey is not null &&
                (planStart.DistanceTo(segment.PlanStart) > Tolerance ||
                 planEnd.DistanceTo(segment.PlanEnd) > Tolerance))
                segment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(
                    topology, segment, semanticAxis);
            if (!TryBuildSemanticMember(topology, segment, rafter.LogicalKey,
                    semanticAxis, eaveElevationMm, widthMm, heightMm, settings,
                    structuralSources, out var member, out failureReason,
                    validateCanonicalLength: replayByKey is null)) return false;
            members.Add(member!);
        }

        model = new RoofAutomaticRafterPhysicalModel(
            ownerReference, Array.AsReadOnly(members.ToArray()));
        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Hip Face0-flattened layouts index <see cref="RoofFaceRafterLayout.Segments"/> by
    /// StationIndex. SimpleGable Face0/Face1 layouts resolve by SourceFaceIndex + StationIndex.
    /// </summary>
    private static bool TryResolveFaceSegment(
        RoofFaceRafterLayout faceLayout,
        RoofRafterGeometry rafter,
        out RoofFaceRafterSegment segment)
    {
        segment = null!;
        if (rafter.Face == RafterRoofFace.Face0 &&
            rafter.StationIndex < faceLayout.Segments.Count)
        {
            var candidate = faceLayout.Segments[rafter.StationIndex];
            if (rafter.PlanStart.DistanceTo(candidate.PlanStart) <= Tolerance &&
                rafter.PlanEnd.DistanceTo(candidate.PlanEnd) <= Tolerance)
            {
                segment = candidate;
                return true;
            }
        }

        var sourceFaceIndex = rafter.Face == RafterRoofFace.Face0 ? 0 : 1;
        var matches = faceLayout.Segments.Where(item =>
                item.SourceFaceIndex == sourceFaceIndex &&
                item.StationIndex == rafter.StationIndex &&
                rafter.PlanStart.DistanceTo(item.PlanStart) <= Tolerance &&
                rafter.PlanEnd.DistanceTo(item.PlanEnd) <= Tolerance)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        segment = matches[0];
        return true;
    }

    /// <summary>Shared prism and end-cut solver for any accepted ordinary semantic axis.
    /// No CAD entities or solids participate in geometry authority.</summary>
    public static bool TryBuildSemanticMember(
        RoofTopology topology, RoofFaceRafterSegment segment, RoofGeneratedMemberKey key,
        RoofSegment3D semanticAxis, double eaveElevationMm, double widthMm, double heightMm,
        RoofAutomaticRafterPhysicalSettings settings,
        IReadOnlyList<RoofStructuralRafterTrimSource>? structuralSources,
        out RoofAutomaticRafterPhysicalMember? member, out string failureReason,
        bool validateCanonicalLength = false)
    {
        member = null;
        failureReason = "InvalidSemanticPhysicalInput";
        if (!Finite(semanticAxis.Start) || !Finite(semanticAxis.End) ||
            Math.Abs(semanticAxis.Start.Z) > Tolerance || Math.Abs(semanticAxis.End.Z) > Tolerance ||
            !Finite(eaveElevationMm) || !Finite(widthMm) || widthMm <= 0d ||
            !Finite(heightMm) || heightMm <= 0d ||
            !Enum.IsDefined(typeof(LowerEndCutMode), settings.LowerEndCutMode) ||
            !Enum.IsDefined(typeof(RidgeJoinMode), settings.RidgeJoinMode)) return false;
        var face = topology.Faces.SingleOrDefault(item => item.SourceEdgeIndex == segment.SourceFaceIndex);
        if (face is null || face.BoundaryNodeIndices.Count < 3 ||
            !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(topology, face, out var normal)) return false;
        var planStart = new RoofPoint2D(semanticAxis.Start.X, semanticAxis.Start.Y);
        var planEnd = new RoofPoint2D(semanticAxis.End.X, semanticAxis.End.Y);
        var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
        var a = Upper(planStart, origin, normal, eaveElevationMm);
        var b = Upper(planEnd, origin, normal, eaveElevationMm);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        var trueLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!Finite(a) || !Finite(b) || planLength <= Tolerance ||
            !Finite(trueLength) || trueLength <= Tolerance ||
            (validateCanonicalLength && Math.Abs(segment.PlanLengthMm - planLength) >
                1e-7 * Math.Max(1d, planLength)))
        {
            return false;
        }

        var run = new RoofPoint3D(dx / trueLength, dy / trueLength, dz / trueLength);
        var planRun = new RoofPoint3D(dx / planLength, dy / planLength, 0d);
        // For an edited in-plane axis, transverse upper corners must still
        // lie on the same roof plane (a horizontal offset alone would not).
        var widthDirection = new RoofPoint3D(-planRun.Y, planRun.X,
            (normal.X * planRun.Y - normal.Y * planRun.X) / normal.Z);
        var widthLength = Math.Sqrt(Dot(widthDirection, widthDirection));
        if (!Finite(widthLength) || widthLength <= Tolerance)
        {
            return false;
        }
        widthDirection = new RoofPoint3D(
            widthDirection.X / widthLength,
            widthDirection.Y / widthLength,
            widthDirection.Z / widthLength);
        var startCut = CutNormal(segment.StartBoundaryRole, settings, run, planRun);
        var endCut = CutNormal(segment.EndBoundaryRole, settings, run, planRun);
        var halfWidth = widthMm / 2d;
        var horizontalEave = settings.LowerEndCutMode == LowerEndCutMode.Horizontal &&
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Eave ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Eave);
        var prismStart = a;
        var prismEnd = b;
        RoofPoint3D lowerStart;
        RoofPoint3D lowerEnd;
        if (horizontalEave)
        {
            // Extend only the transient source prism past the eave. Its
            // entire top front edge must be below the WCS eave plane so
            // the final roof-plane/eave intersection is computed by the
            // clip, even when a replayed axis has a tilted width vector.
            var eaveAtStart = segment.StartBoundaryRole == RoofRafterBoundaryRole.Eave;
            var upslopeZ = eaveAtStart ? run.Z : -run.Z;
            if (upslopeZ <= Tolerance)
            {
                failureReason = $"HorizontalCut:InvalidUpslopeDirection:{key}";
                return false;
            }
            var extension = (halfWidth * Math.Abs(widthDirection.Z) + 1d) / upslopeZ;
            if (!Finite(extension) || extension > trueLength * 10d)
            {
                failureReason = $"HorizontalCut:ExtensionDegenerate:{key}";
                return false;
            }
            if (eaveAtStart)
            {
                prismStart = new RoofPoint3D(
                    a.X - extension * run.X,
                    a.Y - extension * run.Y,
                    a.Z - extension * run.Z);
                lowerStart = Add(prismStart,
                    new RoofPoint3D(normal.X, normal.Y, normal.Z), -heightMm);
                if (!TryLower(b, normal, run, endCut, heightMm, out lowerEnd))
                {
                    failureReason = $"HorizontalCut:InnerEndDegenerate:{key}";
                    return false;
                }
            }
            else
            {
                prismEnd = new RoofPoint3D(
                    b.X + extension * run.X,
                    b.Y + extension * run.Y,
                    b.Z + extension * run.Z);
                lowerEnd = Add(prismEnd,
                    new RoofPoint3D(normal.X, normal.Y, normal.Z), -heightMm);
                if (!TryLower(a, normal, run, startCut, heightMm, out lowerStart))
                {
                    failureReason = $"HorizontalCut:InnerEndDegenerate:{key}";
                    return false;
                }
            }
        }
        else if (!TryLower(a, normal, run, startCut, heightMm, out lowerStart) ||
                 !TryLower(b, normal, run, endCut, heightMm, out lowerEnd))
        {
            return false;
        }

        RoofConvexPrismPlaneClipper.Plane? ridgeOverlapPlane = null;
        var ridgeExtensionMm = 0d;
        if (settings.RidgeJoinMode == RidgeJoinMode.Overlap &&
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge))
        {
            if (!TryResolveOpposingRidgeRoofPlane(topology, face, segment,
                    eaveElevationMm, out var opposingPlane))
            {
                failureReason = $"RidgeOverlap:OpposingPlaneUnresolved:{key}";
                return false;
            }
            ridgeOverlapPlane = opposingPlane;
            var ridgeAtStart = segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge;
            var towardRidge = ridgeAtStart
                ? new RoofPoint3D(-run.X, -run.Y, -run.Z) : run;
            var advanceRate = Dot(towardRidge, opposingPlane.Normal);
            if (!Finite(advanceRate) || advanceRate >= -Tolerance)
            {
                failureReason = $"RidgeOverlap:InvalidAdvanceDirection:{key}";
                return false;
            }
            var ridgeLower = ridgeAtStart ? lowerStart : lowerEnd;
            foreach (var side in new[] { -halfWidth, halfWidth })
            {
                var corner = Add(ridgeLower, widthDirection, side);
                var signed = Dot(Add(corner, opposingPlane.Point, -1d), opposingPlane.Normal);
                if (!Finite(signed))
                {
                    failureReason = $"RidgeOverlap:InvalidLowerCorner:{key}";
                    return false;
                }
                if (signed > RoofFaceRafterLayoutService.CoordinateToleranceMm)
                    ridgeExtensionMm = Math.Max(ridgeExtensionMm,
                        signed / -advanceRate);
            }
            if (!Finite(ridgeExtensionMm))
            {
                failureReason = $"RidgeOverlap:ExtensionDegenerate:{key}";
                return false;
            }
            // Extend the transient prism before slicing: at shallow pitches
            // the unextended lower ridge end face lies below the opposing
            // plane and would otherwise survive as a triangular spike.
            if (ridgeAtStart)
            {
                prismStart = Add(prismStart, towardRidge, ridgeExtensionMm);
                lowerStart = Add(lowerStart, towardRidge, ridgeExtensionMm);
            }
            else
            {
                prismEnd = Add(prismEnd, towardRidge, ridgeExtensionMm);
                lowerEnd = Add(lowerEnd, towardRidge, ridgeExtensionMm);
            }
        }

        var sourceVertices = new[]
        {
            Add(prismStart, widthDirection, -halfWidth),
            Add(prismStart, widthDirection, halfWidth),
            Add(prismEnd, widthDirection, -halfWidth),
            Add(prismEnd, widthDirection, halfWidth),
            Add(lowerStart, widthDirection, -halfWidth),
            Add(lowerStart, widthDirection, halfWidth),
            Add(lowerEnd, widthDirection, -halfWidth),
            Add(lowerEnd, widthDirection, halfWidth),
        };
        if (sourceVertices.Any(point => !Finite(point)))
        {
            return false;
        }
        RoofHorizontalRafterCut? horizontalCut = null;
        RoofStructuralRafterSideCut? structuralCut = null;
        IReadOnlyList<RoofPoint3D> physicalVertices =
            Array.AsReadOnly(sourceVertices);
        var structuralAtStart = segment.StartBoundaryRole is
            RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley;
        var structuralAtEnd = segment.EndBoundaryRole is
            RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley;
        if (structuralSources is not null && (structuralAtStart || structuralAtEnd))
        {
            if (structuralAtStart && structuralAtEnd)
            {
                failureReason = $"StructuralCut:TwoStructuralEnds:{key}";
                return false;
            }
            var role = structuralAtStart
                ? segment.StartBoundaryRole : segment.EndBoundaryRole;
            if (!RoofStructuralSidePlaneResolver.TryResolve(
                    topology, segment.SourceFaceIndex, role,
                    structuralAtStart ? segment.PlanStart : segment.PlanEnd,
                    structuralAtStart ? planEnd : planStart,
                    structuralSources, out structuralCut, out var sideReason))
            {
                failureReason = $"StructuralCut:{sideReason}:{key}";
                return false;
            }
        }
        if (structuralCut is not null)
        {
            var planes = new List<RoofConvexPrismPlaneClipper.Plane>();
            if (horizontalEave)
            {
                planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                    new RoofPoint3D(0d, 0d, eaveElevationMm),
                    new RoofPoint3D(0d, 0d, 1d)));
            }
            planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                structuralCut.PlanePoint, structuralCut.PlaneNormal));
            if (!RoofConvexPrismPlaneClipper.TryClip(
                    sourceVertices, planes, out var clipped))
            {
                failureReason = $"StructuralCut:ClippedBodyDegenerate:{key}";
                return false;
            }
            physicalVertices = clipped!.Body;
            var structuralFaceIndex = horizontalEave ? 1 : 0;
            var hasLowerContact = TryFindBottomContactEdge(clipped.BottomFace,
                clipped.CutFaces[structuralFaceIndex], out var lowerContactEdge);
            if (!horizontalEave && !hasLowerContact)
            {
                failureReason = $"StructuralCut:BottomContactUnresolved:{key}";
                return false;
            }
            structuralCut = structuralCut with
            {
                SourcePrismVertices = Array.AsReadOnly(sourceVertices),
                CutFaceVertices = clipped.CutFaces[structuralFaceIndex],
                TopFaceVertices = clipped.TopFace,
                BottomFaceVertices = clipped.BottomFace,
                LowerContactEdge = hasLowerContact ? lowerContactEdge : null,
            };
            if (horizontalEave)
            {
                horizontalCut = new RoofHorizontalRafterCut(
                    eaveElevationMm,
                    Array.AsReadOnly(sourceVertices),
                    clipped.Body,
                    clipped.CutFaces[0],
                    clipped.TopFace);
            }
        }
        else if (horizontalEave)
        {
            if (!TryClipHorizontal(sourceVertices, eaveElevationMm,
                    out horizontalCut))
            {
                failureReason = $"HorizontalCut:ClippedBodyDegenerate:{key}";
                return false;
            }
            physicalVertices = horizontalCut!.BodyVertices;
        }

        RoofRidgeOverlapCut? ridgeOverlapCut = null;
        if (ridgeOverlapPlane is { } opposingPlaneForCut)
        {
            var minimum = physicalVertices.Min(point =>
                Dot(Add(point, opposingPlaneForCut.Point, -1d), opposingPlaneForCut.Normal));
            if (minimum < -RoofFaceRafterLayoutService.CoordinateToleranceMm)
            {
                var planes = new List<RoofConvexPrismPlaneClipper.Plane>();
                if (horizontalEave)
                    planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                        new RoofPoint3D(0d, 0d, eaveElevationMm),
                        new RoofPoint3D(0d, 0d, 1d)));
                if (structuralCut is not null)
                    planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                        structuralCut.PlanePoint, structuralCut.PlaneNormal));
                planes.Add(opposingPlaneForCut);
                if (!RoofConvexPrismPlaneClipper.TryClip(sourceVertices,
                        planes, out var clipped, allowNoOpPlanes: true) ||
                    clipped is null || clipped.CutFaces.Count == 0)
                {
                    failureReason = $"RidgeOverlap:ClippedBodyDegenerate:{key}";
                    return false;
                }
                physicalVertices = clipped.Body;
                ridgeOverlapCut = new RoofRidgeOverlapCut(
                    opposingPlaneForCut.Point, opposingPlaneForCut.Normal,
                    Array.AsReadOnly(sourceVertices), ridgeExtensionMm,
                    clipped.Body, clipped.TopFace, clipped.BottomFace,
                    clipped.CutFaces[clipped.CutFaces.Count - 1]);
            }
        }

        member = new RoofAutomaticRafterPhysicalMember(
            key,
            segment.SourceFaceIndex,
            segment.StartBoundaryRole,
            segment.EndBoundaryRole,
            new RoofSegment3D(
                new RoofPoint3D(planStart.X, planStart.Y, 0d),
                new RoofPoint3D(planEnd.X, planEnd.Y, 0d)),
            physicalVertices,
            widthMm,
            heightMm,
            topology.PitchDegrees,
            trueLength,
            horizontalCut,
            structuralCut,
            ridgeOverlapCut);
        failureReason = string.Empty;
        return true;
    }

    private static RoofPoint3D Upper(
        RoofPoint2D point,
        RoofPoint3D origin,
        RoofFaceUnitNormal normal,
        double eaveElevationMm) =>
        new(point.X, point.Y,
            eaveElevationMm + origin.Z -
            (normal.X * (point.X - origin.X) +
             normal.Y * (point.Y - origin.Y)) / normal.Z);

    private static bool TryResolveOpposingRidgeRoofPlane(
        RoofTopology topology, RoofTopologyFace sourceFace,
        RoofFaceRafterSegment segment, double eaveElevationMm,
        out RoofConvexPrismPlaneClipper.Plane plane)
    {
        plane = null!;
        var ridgePoint = segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge
            ? segment.PlanStart : segment.PlanEnd;
        var oppositeFaces = topology.Edges.Where(edge =>
                edge.Kind == RoofTopologyEdgeKind.Ridge &&
                edge.FaceIndices.Count == 2 &&
                edge.FaceIndices.Contains(sourceFace.SourceEdgeIndex) &&
                PointOnTopologyEdge(topology, edge, ridgePoint))
            .Select(edge => edge.FaceIndices.Single(index =>
                index != sourceFace.SourceEdgeIndex))
            .Distinct().ToArray();
        if (oppositeFaces.Length != 1) return false;
        var opposite = topology.Faces.SingleOrDefault(face =>
            face.SourceEdgeIndex == oppositeFaces[0]);
        if (opposite is null ||
            !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                topology, opposite, out var normal)) return false;
        var origin = topology.Nodes[opposite.BoundaryNodeIndices[0]];
        plane = new RoofConvexPrismPlaneClipper.Plane(
            new RoofPoint3D(origin.X, origin.Y, origin.Z + eaveElevationMm),
            new RoofPoint3D(-normal.X, -normal.Y, -normal.Z));
        return true;
    }

    private static bool PointOnTopologyEdge(RoofTopology topology,
        RoofTopologyEdge edge, RoofPoint2D point)
    {
        var a = topology.Nodes[edge.StartNodeIndex];
        var b = topology.Nodes[edge.EndNodeIndex];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= Tolerance) return false;
        var px = point.X - a.X;
        var py = point.Y - a.Y;
        var tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;
        return Math.Abs(px * dy - py * dx) <= tolerance * Math.Sqrt(lengthSquared) &&
            px * dx + py * dy >= -tolerance * Math.Sqrt(lengthSquared) &&
            px * dx + py * dy <= lengthSquared + tolerance * Math.Sqrt(lengthSquared);
    }

    private static RoofPoint3D CutNormal(
        RoofRafterBoundaryRole role,
        RoofAutomaticRafterPhysicalSettings settings,
        RoofPoint3D run,
        RoofPoint3D planRun) => role switch
        {
            RoofRafterBoundaryRole.Eave => settings.LowerEndCutMode switch
            {
                LowerEndCutMode.Vertical => planRun,
                LowerEndCutMode.Horizontal => new RoofPoint3D(0d, 0d, 1d),
                _ => run,
            },
            RoofRafterBoundaryRole.Ridge when settings.RidgeJoinMode == RidgeJoinMode.Meet =>
                planRun,
            _ => run,
        };

    private static bool TryLower(
        RoofPoint3D upper,
        RoofFaceUnitNormal faceNormal,
        RoofPoint3D run,
        RoofPoint3D cutNormal,
        double heightMm,
        out RoofPoint3D lower)
    {
        lower = default;
        var denominator = Dot(run, cutNormal);
        if (Math.Abs(denominator) <= Tolerance)
        {
            return false;
        }

        var shift = heightMm *
            (faceNormal.X * cutNormal.X +
             faceNormal.Y * cutNormal.Y +
             faceNormal.Z * cutNormal.Z) / denominator;
        lower = new RoofPoint3D(
            upper.X - heightMm * faceNormal.X + shift * run.X,
            upper.Y - heightMm * faceNormal.Y + shift * run.Y,
            upper.Z - heightMm * faceNormal.Z + shift * run.Z);
        return Finite(lower);
    }

    private static RoofPoint3D Add(RoofPoint3D point, RoofPoint3D direction, double scale) =>
        new(point.X + direction.X * scale,
            point.Y + direction.Y * scale,
            point.Z + direction.Z * scale);

    public static bool TryFindBottomContactEdge(
        IReadOnlyList<RoofPoint3D> bottomFace,
        IReadOnlyList<RoofPoint3D> cutFace,
        out RoofSegment3D edge)
    {
        edge = null!;
        var common = bottomFace.Where(bottom => cutFace.Any(cut =>
            bottom.DistanceTo(cut) <= Tolerance)).ToArray();
        if (common.Length < 2) return false;
        var pair = (from first in common
                from second in common
                select (first, second, length: first.DistanceTo(second)))
            .OrderByDescending(item => item.length).First();
        if (pair.length <= Tolerance) return false;
        edge = new RoofSegment3D(pair.first, pair.second);
        return true;
    }

    private static bool TryClipHorizontal(
        IReadOnlyList<RoofPoint3D> source,
        double eaveElevationMm,
        out RoofHorizontalRafterCut? cut)
    {
        cut = null;
        // The six faces of the transient convex prism. This is a narrow
        // half-space clip, not a general CAD kernel or host-dependent solver.
        int[][] faces =
        [
            [0, 1, 3, 2], // upper roof face
            [4, 6, 7, 5], // lower face
            [0, 2, 6, 4],
            [1, 5, 7, 3],
            [0, 4, 5, 1],
            [2, 3, 7, 6],
        ];
        var body = new List<RoofPoint3D>();
        var onPlane = new List<RoofPoint3D>();
        IReadOnlyList<RoofPoint3D>? topFace = null;
        for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            var polygon = ClipPolygon(
                faces[faceIndex].Select(index => source[index]).ToArray(),
                eaveElevationMm);
            if (faceIndex == 0)
            {
                topFace = polygon;
            }
            foreach (var point in polygon)
            {
                AddUnique(body, point);
                if (Math.Abs(point.Z - eaveElevationMm) <= Tolerance)
                {
                    AddUnique(onPlane, point);
                }
            }
        }

        if (body.Count < 4 || onPlane.Count < 3 || topFace is null ||
            topFace.Count < 3 ||
            topFace.Count(point => Math.Abs(point.Z - eaveElevationMm) <= Tolerance) < 2 ||
            body.Max(point => point.Z) <= eaveElevationMm + Tolerance)
        {
            return false;
        }
        var centerX = onPlane.Average(point => point.X);
        var centerY = onPlane.Average(point => point.Y);
        var cutFace = onPlane.OrderBy(point =>
            Math.Atan2(point.Y - centerY, point.X - centerX)).ToArray();
        var doubledArea = 0d;
        for (var i = 0; i < cutFace.Length; i++)
        {
            var next = cutFace[(i + 1) % cutFace.Length];
            doubledArea += cutFace[i].X * next.Y - next.X * cutFace[i].Y;
        }
        if (!Finite(doubledArea) || Math.Abs(doubledArea) <= Tolerance)
        {
            return false;
        }

        cut = new RoofHorizontalRafterCut(
            eaveElevationMm,
            Array.AsReadOnly(source.ToArray()),
            Array.AsReadOnly(body.ToArray()),
            Array.AsReadOnly(cutFace),
            Array.AsReadOnly(topFace.ToArray()));
        return true;
    }

    private static IReadOnlyList<RoofPoint3D> ClipPolygon(
        IReadOnlyList<RoofPoint3D> polygon,
        double planeZ)
    {
        var result = new List<RoofPoint3D>();
        var previous = polygon[polygon.Count - 1];
        var previousInside = previous.Z >= planeZ - Tolerance;
        foreach (var current in polygon)
        {
            var currentInside = current.Z >= planeZ - Tolerance;
            if (previousInside != currentInside)
            {
                var fraction = (planeZ - previous.Z) / (current.Z - previous.Z);
                result.Add(new RoofPoint3D(
                    previous.X + fraction * (current.X - previous.X),
                    previous.Y + fraction * (current.Y - previous.Y),
                    planeZ));
            }
            if (currentInside)
            {
                result.Add(Math.Abs(current.Z - planeZ) <= Tolerance
                    ? new RoofPoint3D(current.X, current.Y, planeZ)
                    : current);
            }
            previous = current;
            previousInside = currentInside;
        }
        return result;
    }

    private static void AddUnique(List<RoofPoint3D> points, RoofPoint3D candidate)
    {
        if (!points.Any(point =>
                Math.Abs(point.X - candidate.X) <= Tolerance &&
                Math.Abs(point.Y - candidate.Y) <= Tolerance &&
                Math.Abs(point.Z - candidate.Z) <= Tolerance))
        {
            points.Add(candidate);
        }
    }

    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
