#if DEBUG
using System.Globalization;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcBr = Autodesk.AutoCAD.BoundaryRepresentation;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Reads the database Solid3d BRep after every materialization cut. No DWG writes.</summary>
internal static class RoofFinalSolidFrameAudit
{
    private const double PlaneToleranceMm = 0.01;
    private static readonly Dictionary<(Database Database, string Member), Snapshot> Baselines = new();
    private sealed record Snapshot(Vector3d L, Vector3d W, Vector3d H, double Roll,
        double TopError, double? RidgeError, string DimensionVerdict, string MaterializationVerdict);
    private sealed record MeasuredFace(int Index, Vector3d Normal, Point3d Centroid,
        double AreaMm2, IReadOnlyList<Point3d> Vertices, double PlanarityMm);

    /// <summary>Measures the final database BRep after GRIP rebuild. Diagnostic failure
    /// never changes the already approved lifecycle or transaction outcome.</summary>
    internal static void TraceOrdinaryRebuild(Document document, Line line, Solid3d solid,
        RoofOrdinaryPhysicalBuildState state, RoofAutomaticRafterPhysicalMember member)
    {
        var prefix = $"line={line.Handle} solid={solid.Handle} planAxis={V(line.EndPoint - line.StartPoint)} ";
        void Emit(string message)
        {
            try { document.Editor.WriteMessage("\nROOF_ORDINARY_PHYSICAL_FRAME " + prefix + message); } catch { }
            AcKrovyDiagnostics.Info("ROOF_ORDINARY_PHYSICAL_FRAME", prefix + message);
        }
        const string unavailable = "physicalAxis=unavailable roofNormal=unavailable " +
            "sectionWidthAxis=unavailable sectionHeightAxis=unavailable planProjectionErrorMm=unavailable " +
            "topFacePlaneErrorMm=unavailable rollErrorDegrees=unavailable oldPhysicalAxis=unavailable " +
            "oldWidthAxis=unavailable oldHeightAxis=unavailable newPhysicalAxis=unavailable newWidthAxis=unavailable " +
            "newHeightAxis=unavailable minimumTransportAngleDegrees=unavailable " +
            "extraTwistAroundNewAxisDegrees=unavailable result=inconclusive";
        try
        {
            if (!RoofOrdinaryPhysicalFrameRules.TryCreate(state, member, out var frame) || frame is null)
            { Emit(unavailable + " reason=builder-frame-unavailable"); return; }
            Vector3d V3(RoofPoint3D p) => new(p.X, p.Y, p.Z);
            Point3d P3(RoofPoint3D p) => new(p.X, p.Y, p.Z);
            var n = V3(frame.RoofNormal);
            var transport = member.SectionOrientation;
            if (transport is null)
            { Emit(unavailable + " reason=pre-edit-transport-unavailable"); return; }
            var origin = P3(frame.RoofPlaneOrigin);
            if (!RoofOrdinaryPhysicalSectionFrameReader.TryRead(solid, transport.NewFrame,
                    member.WidthMm, member.HeightMm, out var actual) || actual is null)
            { Emit(unavailable + $" reason=brep-section-faces-unresolved expectedRoofNormal={V(n)}"); return; }
            var w = V3(actual.Frame.WidthAxis);
            var h = V3(actual.Frame.HeightAxis);
            var l = V3(actual.Frame.LongitudinalAxis);
            var centered = actual.UpperAxisPoint;
            var planDirection = new Vector3d(l.X, l.Y, 0);
            var projectionError = planDirection.Length <= 1e-9 ? double.PositiveInfinity :
                new[] { line.StartPoint, line.EndPoint }.Max(p => Math.Abs(
                    planDirection.X * (p.Y - centered.Y) - planDirection.Y * (p.X - centered.X)) / planDirection.Length);
            var planeError = actual.TopVertices.Max(p => Math.Abs((p - origin).DotProduct(n)));
            var roll = RoofOrdinarySectionTransportRules.ExtraTwistDegrees(transport.NewFrame, actual.Frame);
            var extraTwist = RoofOrdinarySectionTransportRules.ExtraTwistDegrees(transport.MinimumTransportFrame, actual.Frame);
            var horizontalErrorDegrees = Math.Asin(Math.Clamp(Math.Abs(w.Z), 0, 1)) * 180 / Math.PI;
            var width = actual.WidthMm;
            var height = actual.HeightMm;
            var axisDot = l.DotProduct(V3(frame.LongitudinalAxis));
            var topDot = h.DotProduct(n);
            var handedness = l.CrossProduct(w).DotProduct(h);
            // Roof provenance resolves the axis/elevation, not Independent roll.
            // Top-face distance from that roof plane is reported, never a PASS oracle.
            var pass = projectionError <= 0.001 && Math.Abs(roll) <= 0.001 &&
                axisDot >= 1 - 1e-9 && handedness >= 1 - 1e-9 &&
                h.DotProduct(V3(transport.NewFrame.HeightAxis)) >= 1 - 1e-9 &&
                (!transport.DirectionChanged || (horizontalErrorDegrees <= 0.001 && h.Z > 0)) &&
                Math.Abs(width - member.WidthMm) <= 0.001 && Math.Abs(height - member.HeightMm) <= 0.001;
            Emit($"physicalAxis={V(l)} roofNormal={V(n)} sectionWidthAxis={V(w)} sectionHeightAxis={V(h)} " +
                $"planProjectionErrorMm={F(projectionError)} topFacePlaneErrorMm={F(planeError)} " +
                $"rollErrorDegrees={F(roll)} result={(pass ? "pass" : "fail")} " +
                $"oldPhysicalAxis={V(V3(transport.OldFrame.LongitudinalAxis))} " +
                $"oldWidthAxis={V(V3(transport.OldFrame.WidthAxis))} oldHeightAxis={V(V3(transport.OldFrame.HeightAxis))} " +
                $"newPhysicalAxis={V(l)} newWidthAxis={V(w)} newHeightAxis={V(h)} " +
                $"minimumTransportAngleDegrees={F(transport.MinimumTransportAngleDegrees)} " +
                $"extraTwistAroundNewAxisDegrees={F(extraTwist)} horizontalWidthErrorDegrees={F(horizontalErrorDegrees)} " +
                $"rollOracle={(transport.DirectionChanged ? "worldUpHorizontalWidth" : "preserveUnchangedDirection")} " +
                $"roofFace={member.SourceFaceIndex} roofPlaneOrigin={P(origin)} " +
                $"expectedUpperAxisStart={P(P3(frame.UpperAxis.Start))} expectedUpperAxisEnd={P(P3(frame.UpperAxis.End))} " +
                $"actualWidthMm={F(width)} actualHeightMm={F(height)} expectedWidthMm={F(member.WidthMm)} " +
                $"expectedHeightMm={F(member.HeightMm)} axisDot={F(axisDot)} topNormalDot={F(topDot)} " +
                $"handedness={F(handedness)} startBoundary={member.StartBoundaryRole} endBoundary={member.EndBoundaryRole} " +
                $"measurement=finalBRep");
        }
        catch (Exception ex) { Emit(unavailable + " reason=" + ex.GetType().Name); }
    }

    public static void Run(Document document)
    {
        var editor = document.Editor;
        var choice = editor.GetEntity(new PromptEntityOptions(
            "\nSelect Generated Ordinary Plan2D rafter or its Physical3D solid: "));
        if (choice.Status != PromptStatus.OK) return;
        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(choice.ObjectId, OpenMode.ForRead) is not Entity selected ||
                !TryResolve(document.Database, transaction, selected, out var owner, out var plan, out var solid,
                    out var key))
            {
                Write(document, "result=unresolved reason=selection-must-be-unique-generated-ordinary-pair");
                return;
            }

            var stored = RoofDefinitionStore.Read(owner).Data;
            var footprintInput = RoofPolylineExtractor.Extract(owner);
            var footprint = RoofFootprintValidator.Validate(footprintInput);
            if (stored is null || !footprint.IsValid || footprint.Footprint is null ||
                RoofDefinitionPersistence.Classify(footprintInput, footprint.Footprint, stored).Geometry
                    is not HipRoofGeometry hip ||
                RoofPhysicalElevationStore.Read(owner).Data is not { Physical3DEnabled: true } elevation ||
                !RoofGeneratedRafterSetService.TryRecoverRecipe(document.Database, transaction,
                    RoofGeneratedTimberStore.FindByOwner(document.Database, transaction, owner.Handle.ToString()),
                    out var recipe))
            {
                Write(document, "result=unresolved reason=current-hip-roof-or-recipe-unavailable");
                return;
            }

            var face = hip.Topology.Faces.SingleOrDefault(item => (int)item.SourceEdgeIndex == (int)key.RoofFace);
            if (face is null || !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    hip.Topology, face, out var roofNormal))
            {
                Write(document, "result=unresolved reason=roof-face-unavailable");
                return;
            }
            var node = hip.Topology.Nodes[face.BoundaryNodeIndices[0]];
            var origin = new Point3d(node.X, node.Y,
                node.Z + elevation.ResolvedEaveRelativeElevationMm);
            var normal = new Vector3d(roofNormal.X, roofNormal.Y, roofNormal.Z);
            var p0 = plan.StartPoint;
            var p1 = plan.EndPoint;
            var lifted0 = Lift(p0, origin, normal);
            var lifted1 = Lift(p1, origin, normal);
            var lengthXY = p0.DistanceTo(new Point3d(p1.X, p1.Y, p0.Z));
            if (lengthXY <= 1e-9) { Write(document, "result=invalid-plan-axis"); return; }
            var planAxis = new Vector3d((p1.X - p0.X) / lengthXY, (p1.Y - p0.Y) / lengthXY, 0);
            var expectedL = (lifted1 - lifted0).GetNormal();
            var expectedW = normal.CrossProduct(expectedL).GetNormal();
            var faceLayout = RoofFaceRafterLayoutService.Create(hip.Topology, recipe.MaximumSpacingMm);
            var canonicalSegment = faceLayout.Layout?.Segments.SingleOrDefault(item =>
                item.SourceFaceIndex == (int)key.RoofFace && item.StationIndex == key.StationIndex);
            var segment = canonicalSegment is null ? null : RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(
                hip.Topology, canonicalSegment, new RoofSegment3D(
                    new RoofPoint3D(p0.X, p0.Y, 0), new RoofPoint3D(p1.X, p1.Y, 0)));

            var faces = ReadBrepFaces(solid);
            Write(document, $"identity owner={owner.Handle} memberKey={key} planHandle={plan.Handle} " +
                $"solidHandle={solid.Handle} planStart={P(p0)} planEnd={P(p1)} " +
                $"planAxisXY={V(planAxis)} planLength={F(lengthXY)} roofFace={key.RoofFace} " +
                $"roofPlaneOrigin={P(origin)} roofNormal={V(normal)} " +
                $"expectedWidthMm={F(recipe.WidthMm)} expectedHeightMm={F(recipe.HeightMm)} " +
                $"brepFaceCount={faces.Count}");
            foreach (var f in faces)
                Write(document, $"faceIndex={f.Index} normal={V(f.Normal)} centroid={P(f.Centroid)} " +
                    $"areaMm2={F(f.AreaMm2)} vertexCount={f.Vertices.Count} " +
                    $"planarityMm={F(f.PlanarityMm)} vertices=[{string.Join(",", f.Vertices.Select(P))}]");

            var top = faces.Where(f => f.Normal.DotProduct(normal) > 0.9)
                .OrderByDescending(f => f.Centroid.GetAsVector().DotProduct(normal)).FirstOrDefault();
            var bottom = faces.Where(f => f.Normal.DotProduct(normal) < -0.9)
                .OrderBy(f => f.Centroid.GetAsVector().DotProduct(normal)).FirstOrDefault();
            var sidePlus = faces.Where(f => f.Normal.DotProduct(expectedW) > 0.9)
                .OrderByDescending(f => f.AreaMm2).FirstOrDefault();
            var sideMinus = faces.Where(f => f.Normal.DotProduct(expectedW) < -0.9)
                .OrderByDescending(f => f.AreaMm2).FirstOrDefault();
            if (top is null || bottom is null || sidePlus is null || sideMinus is null)
            {
                Write(document, "result=incomplete reason=top-bottom-or-side-face-unresolved");
                return;
            }

            // These axes and dimensions come only from the final BRep face planes.
            var actualH = -top.Normal;
            var actualW = sidePlus.Normal;
            var actualL = actualW.CrossProduct(top.Normal).GetNormal();
            var actualWidth = Math.Abs((sidePlus.Centroid - sideMinus.Centroid).DotProduct(actualW));
            var actualHeight = Math.Abs((top.Centroid - bottom.Centroid).DotProduct(top.Normal));
            var topErrors = top.Vertices.Select(v => (v - origin).DotProduct(normal)).ToArray();
            var topError = topErrors.Select(Math.Abs).Max();
            var roll = Math.Atan2(actualW.DotProduct(normal), actualW.DotProduct(expectedW)) * 180 / Math.PI;
            var actualLXY = new Vector3d(actualL.X, actualL.Y, 0);
            var yaw = actualLXY.Length > 1e-9
                ? Math.Atan2(planAxis.X * actualLXY.Y - planAxis.Y * actualLXY.X,
                    planAxis.X * actualLXY.X + planAxis.Y * actualLXY.Y) * 180 / Math.PI : double.NaN;
            Write(document, $"section topFace={top.Index} bottomFace={bottom.Index} " +
                $"sideFaces={sideMinus.Index},{sidePlus.Index} Lactual={V(actualL)} Wactual={V(actualW)} " +
                $"Hactual={V(actualH)} Lexpected={V(expectedL)} Wexpected={V(expectedW)} " +
                $"HdownExpected={V(-normal)} dot_Lactual_Lexpected={F(actualL.DotProduct(expectedL))} " +
                $"dot_Wactual_Wexpected={F(actualW.DotProduct(expectedW))} " +
                $"dot_Hactual_Hexpected={F(actualH.DotProduct(-normal))} " +
                $"actualWidthMm={F(actualWidth)} actualHeightMm={F(actualHeight)} " +
                $"rollErrorDegrees={F(roll)} yawErrorDegrees={F(yaw)} " +
                $"handedness={F(actualL.CrossProduct(actualW).DotProduct(top.Normal))}");
            Write(document, $"topNormal={V(top.Normal)} dotTopRoofNormal={F(top.Normal.DotProduct(normal))} " +
                $"topPlaneDistancesToExpectedRoofPlane=[{string.Join(",", topErrors.Select(F))}] " +
                $"maxTopPlaneErrorMm={F(topError)} bottomNormal={V(bottom.Normal)}");

            var endCandidates = faces.Where(f => f.Index != top.Index && f.Index != bottom.Index &&
                    f.Index != sidePlus.Index && f.Index != sideMinus.Index &&
                    Math.Abs(f.Normal.DotProduct(expectedL)) > 0.3).ToArray();
            var startFace = endCandidates.OrderBy(f => (f.Centroid - lifted0).DotProduct(expectedL))
                .FirstOrDefault();
            var endFace = endCandidates.OrderByDescending(f => (f.Centroid - lifted0).DotProduct(expectedL))
                .FirstOrDefault();
            ReportEnd(document, "Start", startFace, lifted0, expectedL, planAxis,
                segment?.StartBoundaryRole, elevation.LowerEndCutMode);
            ReportEnd(document, "End", endFace, lifted1, expectedL, planAxis,
                segment?.EndBoundaryRole, elevation.LowerEndCutMode);

            var ridge = hip.Topology.Edges.Where(edge => edge.Kind == RoofTopologyEdgeKind.Ridge &&
                    edge.FaceIndices.Contains(face.SourceEdgeIndex))
                .Select(edge => (A: hip.Topology.Nodes[edge.StartNodeIndex],
                    B: hip.Topology.Nodes[edge.EndNodeIndex]))
                .OrderBy(edge => DistanceToLineXY(p1, edge.A.X, edge.A.Y, edge.B.X, edge.B.Y))
                .FirstOrDefault();
            double? ridgeError = null;
            if (elevation.RidgeJoinMode == RidgeJoinMode.Meet && endFace is not null && ridge != default &&
                DistanceToLineXY(p1, ridge.A.X, ridge.A.Y, ridge.B.X, ridge.B.Y) < 1)
            {
                var ridgeNormal = new Vector3d(-(ridge.B.Y - ridge.A.Y),
                    ridge.B.X - ridge.A.X, 0).GetNormal();
                if (ridgeNormal.DotProduct(planAxis) < 0) ridgeNormal = -ridgeNormal;
                var ridgePoint = new Point3d(ridge.A.X, ridge.A.Y, 0);
                var errors = endFace.Vertices.Select(v => (v - ridgePoint).DotProduct(ridgeNormal)).ToArray();
                ridgeError = errors.Select(Math.Abs).Max();
                Write(document, $"expectedRidgeCutPlane point={P(ridgePoint)} normal={V(ridgeNormal)} " +
                    $"ridgeFaceIndex={endFace.Index} vertexPlaneErrorsMm=[{string.Join(",", errors.Select(F))}] " +
                    $"maxRidgeCutErrorMm={F(ridgeError.Value)}");
            }
            else Write(document, "expectedRidgeCutPlane=unavailable reason=end-not-on-current-ridge");

            string materialization = "UNDETERMINED";
            if (segment is not null &&
                segment.StartBoundaryRole is not (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley) &&
                segment.EndBoundaryRole is not (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley) &&
                RoofAutomaticRafterPhysicalBuilder.TryBuildSemanticMember(hip.Topology, segment, key,
                    new RoofSegment3D(new RoofPoint3D(p0.X, p0.Y, 0),
                        new RoofPoint3D(p1.X, p1.Y, 0)),
                    elevation.ResolvedEaveRelativeElevationMm, recipe.WidthMm, recipe.HeightMm,
                    new RoofAutomaticRafterPhysicalSettings(elevation.LowerEndCutMode,
                        elevation.RidgeJoinMode), null, out var coreMember, out _) && coreMember is not null &&
                coreMember.HorizontalCut is null && coreMember.StructuralCut is null &&
                coreMember.RidgeOverlapCut is null)
            {
                var brepVertices = faces.SelectMany(f => f.Vertices).ToArray();
                var coreVertices = coreMember.SolidVertices.Select(v => new Point3d(v.X, v.Y, v.Z)).ToArray();
                var same = coreVertices.All(v => brepVertices.Any(b => b.DistanceTo(v) <= PlaneToleranceMm)) &&
                    brepVertices.All(v => coreVertices.Any(c => c.DistanceTo(v) <= PlaneToleranceMm));
                materialization = same ? "NO" : "YES";
                Write(document, $"coreBrepVertexComparison coreVertexCount={coreVertices.Length} " +
                    $"brepVertexCount={brepVertices.Length} maxMatchingToleranceMm={F(PlaneToleranceMm)} " +
                    $"MATERIALIZATION_DIFFERS_FROM_CORE={materialization}");
            }
            else Write(document, "MATERIALIZATION_DIFFERS_FROM_CORE=UNDETERMINED reason=core-model-unavailable-or-other-cut");

            var dimensionVerdict = Math.Abs(actualWidth - recipe.WidthMm) <= PlaneToleranceMm &&
                Math.Abs(actualHeight - recipe.HeightMm) <= PlaneToleranceMm ? "PASS" : "FAIL";
            Write(document, $"verdict FINAL_SOLID_SECTION_ROLL={(Math.Abs(roll) <= 0.1 ? "NO" : "YES")} " +
                $"FINAL_SOLID_TOP_PLANE={(topError <= PlaneToleranceMm ? "PASS" : "FAIL")} " +
                $"FINAL_SOLID_SECTION_DIMENSIONS={dimensionVerdict} " +
                $"RIDGE_MEET_FINAL_SOLID={(ridgeError is null ? "UNDETERMINED" : ridgeError <= PlaneToleranceMm ? "PASS" : "FAIL")} " +
                $"MATERIALIZATION_DIFFERS_FROM_CORE={materialization}");
            var snapshot = new Snapshot(actualL, actualW, actualH, roll, topError, ridgeError,
                dimensionVerdict, materialization);
            var pairKey = (document.Database, owner.Handle + ":" + PhysicalId(key));
            if (!Baselines.TryGetValue(pairKey, out var baseline))
            {
                Baselines[pairKey] = snapshot;
                Write(document, "comparison phase=CREATE baseline=captured-in-memory");
            }
            else
            {
                Write(document, $"comparison CREATE Lactual={V(baseline.L)} Wactual={V(baseline.W)} " +
                    $"Hactual={V(baseline.H)} rollError={F(baseline.Roll)} topError={F(baseline.TopError)} " +
                    $"ridgeCutError={NullableF(baseline.RidgeError)} dimensions={baseline.DimensionVerdict} " +
                    $"materializationDiffers={baseline.MaterializationVerdict}");
                Write(document, $"comparison FIRST_GRIP Lactual={V(snapshot.L)} Wactual={V(snapshot.W)} " +
                    $"Hactual={V(snapshot.H)} rollError={F(snapshot.Roll)} topError={F(snapshot.TopError)} " +
                    $"ridgeCutError={NullableF(snapshot.RidgeError)} dimensions={snapshot.DimensionVerdict} " +
                    $"materializationDiffers={snapshot.MaterializationVerdict}");
            }
            // The read-only transaction is intentionally disposed without Commit.
        }
        catch (System.Exception ex)
        {
            Write(document, $"result=failed error={ex.GetType().Name} detail={ex.Message}");
        }
    }

    private static bool TryResolve(Database database, Transaction transaction, Entity selected,
        out Polyline owner, out Line plan, out Solid3d solid, out RoofGeneratedMemberKey key)
    {
        owner = null!; plan = null!; solid = null!; key = default;
        string ownerHandle;
        string physicalId;
        if (selected is Line selectedLine && RoofGeneratedTimberStore.Read(selectedLine).Data is { } generated &&
            generated.MemberKind == RoofGeneratedTimberKind.Rafter)
        {
            ownerHandle = generated.RoofOwnerReference;
            key = RoofGeneratedMemberKey.From(generated);
            physicalId = PhysicalId(key);
        }
        else if (selected is Solid3d selectedSolid &&
            RoofPhysical3DGeneratedStore.Read(selectedSolid).Data is
                { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } physical)
        {
            ownerHandle = physical.RoofOwnerReference;
            physicalId = physical.StructuralId;
        }
        else return false;

        var plans = RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerHandle)
            .Select(id => transaction.GetObject(id, OpenMode.ForRead) as Line)
            .Where(line => line is not null && RoofGeneratedTimberStore.Read(line).Data is { } data &&
                data.MemberKind == RoofGeneratedTimberKind.Rafter &&
                PhysicalId(RoofGeneratedMemberKey.From(data)) == physicalId).ToArray();
        var solids = RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, ownerHandle)
            .Select(id => transaction.GetObject(id, OpenMode.ForRead) as Solid3d)
            .Where(body => body is not null && RoofPhysical3DGeneratedStore.Read(body).Data is { } data &&
                data.Role == RoofPhysical3DGeneratedRole.OrdinaryRafterSolid &&
                data.StructuralId == physicalId).ToArray();
        if (plans.Length != 1 || solids.Length != 1) return false;
        plan = plans[0]!; solid = solids[0]!;
        key = RoofGeneratedMemberKey.From(RoofGeneratedTimberStore.Read(plan).Data!);
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var space = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var owners = space.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead) as Polyline)
            .Where(poly => poly is not null && poly.Handle.ToString().Equals(ownerHandle,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (owners.Length != 1) return false;
        owner = owners[0]!;
        return true;
    }

    private static List<MeasuredFace> ReadBrepFaces(Solid3d solid)
    {
        var path = new FullSubentityPath(new[] { solid.ObjectId },
            new SubentityId(SubentityType.Null, IntPtr.Zero));
        using var brep = new AcBr.Brep(path);
        var raw = brep.Faces.Select((face, index) => (index, vertices: face.Loops
            .SelectMany(loop => loop.Vertices.Select(vertex => vertex.Point)).ToArray()))
            .Where(item => item.vertices.Length >= 3).ToArray();
        var all = raw.SelectMany(item => item.vertices).ToArray();
        if (all.Length == 0) return new List<MeasuredFace>();
        var center = Average(all);
        var result = new List<MeasuredFace>();
        foreach (var (index, vertices) in raw)
        {
            var vertexMean = Average(vertices);
            var vector = new Vector3d(0, 0, 0);
            for (var i = 1; i + 1 < vertices.Length; i++)
            {
                vector = (vertices[i] - vertices[0]).CrossProduct(vertices[i + 1] - vertices[0]);
                if (vector.Length > 1e-8) break;
            }
            if (vector.Length <= 1e-8) continue;
            var normal = vector.GetNormal();
            if (normal.DotProduct(vertexMean - center) < 0) normal = -normal;
            var planarity = vertices.Select(p => Math.Abs((p - vertices[0]).DotProduct(normal))).Max();
            if (planarity > PlaneToleranceMm) continue;
            var signedArea = 0d;
            var moment = new Vector3d(0, 0, 0);
            for (var i = 1; i + 1 < vertices.Length; i++)
            {
                var triangleArea = (vertices[i] - vertices[0])
                    .CrossProduct(vertices[i + 1] - vertices[0]).DotProduct(normal) / 2;
                signedArea += triangleArea;
                moment += (vertices[0].GetAsVector() + vertices[i].GetAsVector() +
                    vertices[i + 1].GetAsVector()) * (triangleArea / 3);
            }
            var centroid = Math.Abs(signedArea) > 1e-9
                ? new Point3d(moment.X / signedArea, moment.Y / signedArea, moment.Z / signedArea)
                : vertexMean;
            result.Add(new(index, normal, centroid, Math.Abs(signedArea),
                vertices, planarity));
        }
        return result;
    }

    private static void ReportEnd(Document document, string label, MeasuredFace? face,
        Point3d expectedPoint, Vector3d expectedL, Vector3d planAxis,
        RoofRafterBoundaryRole? role, LowerEndCutMode lowerCutMode)
    {
        if (face is null) { Write(document, $"end={label} result=unresolved"); return; }
        var cutNormal = role switch
        {
            RoofRafterBoundaryRole.Ridge => planAxis,
            RoofRafterBoundaryRole.Eave when lowerCutMode == LowerEndCutMode.Vertical => planAxis,
            RoofRafterBoundaryRole.Eave when lowerCutMode == LowerEndCutMode.Horizontal => Vector3d.ZAxis,
            _ => expectedL,
        };
        var errors = face.Vertices.Select(p => (p - expectedPoint).DotProduct(cutNormal)).ToArray();
        Write(document, $"end={label} role={role?.ToString() ?? "Unknown"} faceIndex={face.Index} faceNormal={V(face.Normal)} " +
            $"expectedSemanticCutPlane point={P(expectedPoint)} normal={V(cutNormal)} " +
            $"vertices=[{string.Join(",", face.Vertices.Select(P))}] " +
            $"vertexPlaneErrorsMm=[{string.Join(",", errors.Select(F))}] " +
            $"maxCutErrorMm={F(errors.Select(Math.Abs).Max())}");
    }

    private static Point3d Lift(Point3d point, Point3d origin, Vector3d normal) =>
        new(point.X, point.Y, origin.Z -
            (normal.X * (point.X - origin.X) + normal.Y * (point.Y - origin.Y)) / normal.Z);
    private static Point3d Average(IReadOnlyList<Point3d> points) => new(
        points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
    private static double DistanceToLineXY(Point3d point, double ax, double ay, double bx, double by) =>
        Math.Abs((bx - ax) * (point.Y - ay) - (by - ay) * (point.X - ax)) /
        Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
    private static string PhysicalId(RoofGeneratedMemberKey key) =>
        $"{key.MemberKind}:{key.RoofFace}:{key.StationIndex.ToString(CultureInfo.InvariantCulture)}";
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string NullableF(double? value) => value.HasValue ? F(value.Value) : "UNDETERMINED";
    private static string P(Point3d value) => $"({F(value.X)},{F(value.Y)},{F(value.Z)})";
    private static string V(Vector3d value) => $"({F(value.X)},{F(value.Y)},{F(value.Z)})";
    private static void Write(Document document, string message)
    {
        try { document.Editor.WriteMessage("\nROOF_3D_FRAME_AUDIT " + message); } catch { }
        AcKrovyDiagnostics.Info("ROOF_3D_FRAME_AUDIT", message);
    }
}
#endif
