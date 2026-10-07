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
            !Finite(eaveElevationMm) ||
            !Finite(widthMm) || widthMm <= 0d ||
            !Finite(heightMm) || heightMm <= 0d ||
            !Enum.IsDefined(typeof(LowerEndCutMode), settings.LowerEndCutMode) ||
            !Enum.IsDefined(typeof(RidgeJoinMode), settings.RidgeJoinMode))
        {
            return false;
        }

        if (generatedLayout.Signature != faceLayout.Signature)
        {
            failureReason = "FaceLayoutSignatureMismatch";
            return false;
        }

        var faces = topology.Faces.ToDictionary(face => face.SourceEdgeIndex);
        var replayByKey = replayPlan?.Items.ToDictionary(item => item.Rafter.LogicalKey);
        if (replayPlan is { IsValid: false })
        {
            failureReason = replayPlan.FailureReason ?? "ReplayPlanInvalid";
            return false;
        }
        if (replayByKey is not null && replayByKey.Count != generatedLayout.Rafters.Count)
        {
            failureReason = "ReplayKeyCountMismatch";
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
                failureReason = $"FaceSegmentUnresolved:{rafter.LogicalKey}";
                return false;
            }

            if (rafter.PlanStart.DistanceTo(segment.PlanStart) > Tolerance ||
                rafter.PlanEnd.DistanceTo(segment.PlanEnd) > Tolerance)
            {
                failureReason = $"FaceSegmentPlanMismatch:{rafter.LogicalKey}";
                return false;
            }
            if (!faces.TryGetValue(segment.SourceFaceIndex, out var face) ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, face, out var normal) ||
                face.BoundaryNodeIndices.Count < 3)
            {
                failureReason = $"FaceNormalUnresolved:{rafter.LogicalKey}";
                return false;
            }

            var planStart = segment.PlanStart;
            var planEnd = segment.PlanEnd;
            RoofGeneratedMemberReplayItem? replayItem = null;
            if (replayByKey is not null)
            {
                if (!replayByKey.TryGetValue(rafter.LogicalKey, out var replay) ||
                    replay.Rafter.LogicalKey != rafter.LogicalKey)
                {
                    failureReason = $"ReplayKeyMissing:{rafter.LogicalKey}";
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
                    failureReason = $"ReplayGeometryInvalid:{rafter.LogicalKey}";
                    return false;
                }
                planStart = new RoofPoint2D(applied.Start.X, applied.Start.Y);
                planEnd = new RoofPoint2D(applied.End.X, applied.End.Y);
                replayItem = replay;
            }

            var semanticAxis = new RoofSegment3D(
                new RoofPoint3D(planStart.X, planStart.Y, 0d),
                new RoofPoint3D(planEnd.X, planEnd.Y, 0d));
            if (replayItem?.Disposition == RoofGeneratedMemberReplayDisposition.GeometryReplayed &&
                replayItem.Override?.PhysicalReferenceSegment is { } gripReference)
            {
                var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d);
                if (!RoofAttachedManualRelativeGeometryRules.TryReplay(canonical.Start, canonical.End,
                        gripReference, out var referenceStart, out var referenceEnd) ||
                    !RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(new(referenceStart, referenceEnd),
                        new(semanticAxis.Start, semanticAxis.End), RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
                        out var placement, out _, out _))
                { failureReason = "InvalidGeneratedGripPhysicalReference"; return false; }
                var referenceAxis = new RoofSegment3D(referenceStart, referenceEnd);
                var referenceSegment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(topology, segment, referenceAxis);
                if (!TryBuildSemanticMember(topology, referenceSegment, rafter.LogicalKey, referenceAxis,
                        eaveElevationMm, widthMm, heightMm, settings, structuralSources,
                        out var gripMember, out failureReason)) return false;
                members.Add(TranslateMember(gripMember!, semanticAxis, placement));
                continue;
            }
            if (replayItem is { Disposition: RoofGeneratedMemberReplayDisposition.GeometryReplayed,
                    Override: { Suppressed: false, RotationRadians: 0d, StartOffsetMm: 0d, EndOffsetMm: 0d } } &&
                RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
                    RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d),
                    new(semanticAxis.Start, semanticAxis.End),
                    RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
                    out var translation, out _, out _) &&
                (Math.Abs(translation.X) > Tolerance || Math.Abs(translation.Y) > Tolerance))
            {
                // Accepted MOVE carries the complete body and its resolved cuts;
                // do not lift its displaced axis onto the original roof plane.
                var canonicalAxis = new RoofSegment3D(
                    new(segment.PlanStart.X, segment.PlanStart.Y, 0d),
                    new(segment.PlanEnd.X, segment.PlanEnd.Y, 0d));
                if (!TryBuildSemanticMember(topology, segment, rafter.LogicalKey, canonicalAxis,
                        eaveElevationMm, widthMm, heightMm, settings, structuralSources,
                        out var canonicalMember, out failureReason)) return false;
                members.Add(TranslateMember(canonicalMember!, semanticAxis, translation));
                continue;
            }
            if (replayItem is { CarriesAcceptedTranslation: true, Disposition: RoofGeneratedMemberReplayDisposition.GeometryReplayed,
                    Override: { Suppressed: false } completeEdit } &&
                RoofGeneratedMemberOverrideMath.TryApply(
                    RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d),
                    RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
                    completeEdit with { AlongMm = 0d, LateralMm = 0d }, out var localShape) &&
                RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
                    localShape, new(semanticAxis.Start, semanticAxis.End),
                    RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
                    out var carriedTranslation, out _, out _) &&
                (Math.Abs(carriedTranslation.X) > Tolerance || Math.Abs(carriedTranslation.Y) > Tolerance))
            {
                // Endpoint/rotation shape stays in the source roof frame. Carry the
                // earlier accepted translation on the resulting body and every cut.
                var localAxis = new RoofSegment3D(localShape.Start, localShape.End);
                var localSegment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(
                    topology, segment, localAxis);
                if (!TryBuildSemanticMember(topology, localSegment, rafter.LogicalKey, localAxis,
                        eaveElevationMm, widthMm, heightMm, settings, structuralSources,
                        out var localMember, out failureReason)) return false;
                members.Add(TranslateMember(localMember!, semanticAxis, carriedTranslation));
                continue;
            }
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

    /// <summary>Translate Core physical geometry and every host construction/clip input together.</summary>
    private static RoofAutomaticRafterPhysicalMember TranslateMember(
        RoofAutomaticRafterPhysicalMember member, RoofSegment3D plan, RoofPoint3D translation)
    {
        RoofPoint3D Shift(RoofPoint3D point) => Add(point, translation, 1d);
        IReadOnlyList<RoofPoint3D> ShiftAll(IReadOnlyList<RoofPoint3D> points) =>
            Array.AsReadOnly(points.Select(Shift).ToArray());
        return member with
        {
            PlanAxis = plan,
            SolidVertices = ShiftAll(member.SolidVertices),
            HorizontalCut = member.HorizontalCut is { } horizontal ? horizontal with
            {
                SourcePrismVertices = ShiftAll(horizontal.SourcePrismVertices),
                BodyVertices = ShiftAll(horizontal.BodyVertices),
                CutFaceVertices = ShiftAll(horizontal.CutFaceVertices),
                TopFaceVertices = ShiftAll(horizontal.TopFaceVertices),
            } : null,
            StructuralCut = member.StructuralCut is { } structural ? structural with
            {
                PlanePoint = Shift(structural.PlanePoint),
                SourcePrismVertices = ShiftAll(structural.SourcePrismVertices),
                CutFaceVertices = ShiftAll(structural.CutFaceVertices),
                TopFaceVertices = ShiftAll(structural.TopFaceVertices),
                BottomFaceVertices = structural.BottomFaceVertices is { } bottom ? ShiftAll(bottom) : null,
                LowerContactEdge = structural.LowerContactEdge is { } contact
                    ? new RoofSegment3D(Shift(contact.Start), Shift(contact.End)) : null,
            } : null,
            RidgeOverlapCut = member.RidgeOverlapCut is { } ridge ? ridge with
            {
                PlanePoint = Shift(ridge.PlanePoint),
                SourcePrismVertices = ShiftAll(ridge.SourcePrismVertices),
                BodyVertices = ShiftAll(ridge.BodyVertices),
                TopFaceVertices = ShiftAll(ridge.TopFaceVertices),
                BottomFaceVertices = ShiftAll(ridge.BottomFaceVertices),
                CutFaceVertices = ShiftAll(ridge.CutFaceVertices),
            } : null,
            RidgeMeetCut = member.RidgeMeetCut is { } meet ? meet with
            {
                PlanePoint = Shift(meet.PlanePoint),
                SourcePrismVertices = ShiftAll(meet.SourcePrismVertices),
                BodyVertices = ShiftAll(meet.BodyVertices),
                TopFaceVertices = ShiftAll(meet.TopFaceVertices),
                BottomFaceVertices = ShiftAll(meet.BottomFaceVertices),
                CutFaceVertices = ShiftAll(meet.CutFaceVertices),
            } : null,
        };
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
        bool validateCanonicalLength = false, RoofOrdinarySectionFrame? independentSectionFrame = null)
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
        if (!RoofOrdinaryPhysicalFrameRules.TryCreate(semanticAxis,
                new RoofPoint3D(origin.X, origin.Y, origin.Z + eaveElevationMm), normal, out var frame) ||
            frame is null) return false;
        var a = frame.UpperAxis.Start;
        var b = frame.UpperAxis.End;
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

        var run = frame.LongitudinalAxis;
        var planRun = new RoofPoint3D(dx / planLength, dy / planLength, 0d);
        var widthDirection = frame.SectionWidthAxis;
        var heightDirection = frame.SectionHeightAxis;
        if (independentSectionFrame is not null)
        {
            if (!RoofOrdinarySectionTransportRules.IsValid(independentSectionFrame))
            { failureReason = "InvalidIndependentSectionFrame"; return false; }

            var roofRun = frame.LongitudinalAxis;
            var samePitchAsRoof =
                Dot(roofRun, independentSectionFrame.LongitudinalAxis) >= 1 - 1e-9;
            if (samePitchAsRoof)
            {
                // Historical Independent path: roof upper axis + independent W/H.
                run = roofRun;
                widthDirection = independentSectionFrame.WidthAxis;
                heightDirection = independentSectionFrame.HeightAxis;
            }
            else
            {
                // Elevation (or other member-owned pitch) differs from persisted roof nodes.
                // Place the prism on the Elevation spatial pitch, not the stale roof plane.
                var pitchZ = Math.Abs(independentSectionFrame.LongitudinalAxis.Z);
                if (pitchZ > 1d) pitchZ = 1d;
                var pitchRad = Math.Asin(pitchZ);
                var tanPitch = Math.Tan(pitchRad);
                if (!Finite(tanPitch))
                { failureReason = "InvalidIndependentSectionFrame"; return false; }

                var planDx = semanticAxis.End.X - semanticAxis.Start.X;
                var planDy = semanticAxis.End.Y - semanticAxis.Start.Y;
                var planLen = Math.Sqrt(planDx * planDx + planDy * planDy);
                if (planLen <= Tolerance)
                { failureReason = "InvalidIndependentSectionFrame"; return false; }

                var eaveAtStart = segment.StartBoundaryRole == RoofRafterBoundaryRole.Eave
                    || (segment.EndBoundaryRole != RoofRafterBoundaryRole.Eave &&
                        ResolveIndependentEaveAtStart(
                            independentSectionFrame, planDx / planLen, planDy / planLen));
                double upperStartZ;
                double upperEndZ;
                if (eaveAtStart)
                {
                    upperStartZ = eaveElevationMm;
                    upperEndZ = eaveElevationMm + planLen * tanPitch;
                }
                else
                {
                    upperEndZ = eaveElevationMm;
                    upperStartZ = eaveElevationMm + planLen * tanPitch;
                }

                a = new RoofPoint3D(semanticAxis.Start.X, semanticAxis.Start.Y, upperStartZ);
                b = new RoofPoint3D(semanticAxis.End.X, semanticAxis.End.Y, upperEndZ);
                dx = b.X - a.X;
                dy = b.Y - a.Y;
                dz = b.Z - a.Z;
                planLength = Math.Sqrt(dx * dx + dy * dy);
                trueLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (planLength <= Tolerance || trueLength <= Tolerance || !Finite(trueLength))
                { failureReason = "InvalidIndependentSectionFrame"; return false; }

                run = new RoofPoint3D(dx / trueLength, dy / trueLength, dz / trueLength);
                planRun = new RoofPoint3D(dx / planLength, dy / planLength, 0d);
                if (!RoofOrdinarySectionTransportRules.TryTransport(
                        independentSectionFrame, run, out var transported) ||
                    transported is null)
                { failureReason = "InvalidIndependentSectionFrame"; return false; }
                widthDirection = transported.NewFrame.WidthAxis;
                heightDirection = transported.NewFrame.HeightAxis;
            }
        }
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
                lowerStart = Add(prismStart, heightDirection, -heightMm);
                if (!TryLower(b, heightDirection, run, endCut, heightMm, out lowerEnd))
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
                lowerEnd = Add(prismEnd, heightDirection, -heightMm);
                if (!TryLower(a, heightDirection, run, startCut, heightMm, out lowerStart))
                {
                    failureReason = $"HorizontalCut:InnerEndDegenerate:{key}";
                    return false;
                }
            }
        }
        else if (!TryLower(a, heightDirection, run, startCut, heightMm, out lowerStart) ||
                 !TryLower(b, heightDirection, run, endCut, heightMm, out lowerEnd))
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

        RoofConvexPrismPlaneClipper.Plane? ridgeMeetPlane = null;
        if (settings.RidgeJoinMode == RidgeJoinMode.Meet &&
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge))
        {
            if (!TryResolveRidgeMeetPlane(topology, face, segment, out var resolvedRidgePlane))
            {
                failureReason = $"RidgeMeet:TopologyPlaneUnresolved:{key}";
                return false;
            }
            var atStart = segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge;
            var endTop = atStart ? prismStart : prismEnd;
            var endBottom = atStart ? lowerStart : lowerEnd;
            var corners = new[]
            {
                Add(endTop, widthDirection, -halfWidth), Add(endTop, widthDirection, halfWidth),
                Add(endBottom, widthDirection, -halfWidth), Add(endBottom, widthDirection, halfWidth),
            };
            var offsets = corners.Select(corner =>
                Dot(Add(corner, resolvedRidgePlane.Point, -1d), resolvedRidgePlane.Normal)).ToArray();
            if (offsets.Any(offset => Math.Abs(offset) >
                    RoofFaceRafterLayoutService.CoordinateToleranceMm))
            {
                var advance = atStart ? Add(new RoofPoint3D(0, 0, 0), run, -1d) : run;
                var rate = Dot(advance, resolvedRidgePlane.Normal);
                if (rate >= -Tolerance)
                {
                    failureReason = $"RidgeMeet:InvalidAdvanceDirection:{key}";
                    return false;
                }
                var extension = (Math.Max(0d, offsets.Max()) + 1d) / -rate;
                if (!Finite(extension) || extension > trueLength * 10d)
                {
                    failureReason = $"RidgeMeet:ExtensionDegenerate:{key}";
                    return false;
                }
                if (atStart)
                {
                    prismStart = Add(prismStart, advance, extension);
                    lowerStart = Add(lowerStart, advance, extension);
                }
                else
                {
                    prismEnd = Add(prismEnd, advance, extension);
                    lowerEnd = Add(lowerEnd, advance, extension);
                }
                ridgeMeetPlane = resolvedRidgePlane;
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

        RoofRidgeMeetCut? ridgeMeetCut = null;
        if (ridgeMeetPlane is { } meetPlane)
        {
            var planes = new List<RoofConvexPrismPlaneClipper.Plane>();
            if (horizontalEave)
                planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                    new RoofPoint3D(0d, 0d, eaveElevationMm), new RoofPoint3D(0d, 0d, 1d)));
            if (structuralCut is not null)
                planes.Add(new RoofConvexPrismPlaneClipper.Plane(
                    structuralCut.PlanePoint, structuralCut.PlaneNormal));
            planes.Add(meetPlane);
            if (!RoofConvexPrismPlaneClipper.TryClip(sourceVertices, planes,
                    out var clipped, allowNoOpPlanes: true) ||
                clipped is null || clipped.CutFaces.Count == 0)
            {
                failureReason = $"RidgeMeet:ClippedBodyDegenerate:{key}";
                return false;
            }
            physicalVertices = clipped.Body;
            ridgeMeetCut = new RoofRidgeMeetCut(
                meetPlane.Point, meetPlane.Normal, Array.AsReadOnly(sourceVertices),
                clipped.Body, clipped.TopFace, clipped.BottomFace,
                clipped.CutFaces[clipped.CutFaces.Count - 1]);
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
            ridgeOverlapCut) { RidgeMeetCut = ridgeMeetCut };
        failureReason = string.Empty;
        return true;
    }

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

    private static bool TryResolveRidgeMeetPlane(
        RoofTopology topology, RoofTopologyFace sourceFace,
        RoofFaceRafterSegment segment,
        out RoofConvexPrismPlaneClipper.Plane plane)
    {
        plane = null!;
        if ((segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge) ==
            (segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge)) return false;
        var ridgePoint = segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge
            ? segment.PlanStart : segment.PlanEnd;
        var edges = topology.Edges.Where(edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge &&
            edge.FaceIndices.Contains(sourceFace.SourceEdgeIndex) &&
            PointOnTopologyEdge(topology, edge, ridgePoint)).ToArray();
        if (edges.Length != 1) return false;

        var edgeStart = topology.Nodes[edges[0].StartNodeIndex];
        var edgeEnd = topology.Nodes[edges[0].EndNodeIndex];
        var dx = edgeEnd.X - edgeStart.X;
        var dy = edgeEnd.Y - edgeStart.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length <= Tolerance) return false;
        var normal = new RoofPoint3D(-dy / length, dx / length, 0d);
        var interior = new RoofPoint3D(
            sourceFace.BoundaryNodeIndices.Average(index => topology.Nodes[index].X),
            sourceFace.BoundaryNodeIndices.Average(index => topology.Nodes[index].Y), 0d);
        var point = new RoofPoint3D(edgeStart.X, edgeStart.Y, 0d);
        var side = Dot(Add(interior, point, -1d), normal);
        if (!Finite(side) || Math.Abs(side) <= Tolerance) return false;
        if (side < 0d) normal = Add(new RoofPoint3D(0d, 0d, 0d), normal, -1d);
        plane = new RoofConvexPrismPlaneClipper.Plane(point, normal);
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
        RoofPoint3D heightDirection,
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
            (heightDirection.X * cutNormal.X +
             heightDirection.Y * cutNormal.Y +
             heightDirection.Z * cutNormal.Z) / denominator;
        lower = new RoofPoint3D(
            upper.X - heightMm * heightDirection.X + shift * run.X,
            upper.Y - heightMm * heightDirection.Y + shift * run.Y,
            upper.Z - heightMm * heightDirection.Z + shift * run.Z);
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

    private static bool ResolveIndependentEaveAtStart(
        RoofOrdinarySectionFrame frame,
        double planDirX,
        double planDirY)
    {
        var prevXyLen = Math.Sqrt(
            frame.LongitudinalAxis.X * frame.LongitudinalAxis.X +
            frame.LongitudinalAxis.Y * frame.LongitudinalAxis.Y);
        if (prevXyLen <= 1e-9) return frame.LongitudinalAxis.Z >= 0;
        var sameSense =
            (frame.LongitudinalAxis.X / prevXyLen) * planDirX +
            (frame.LongitudinalAxis.Y / prevXyLen) * planDirY >= 0;
        return sameSense ? frame.LongitudinalAxis.Z >= -1e-12 : frame.LongitudinalAxis.Z < 0;
    }

    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
