using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class TimberSourceLineCreationPhaseException : Exception
{
    public TimberSourceLineCreationPhaseException(
        string servicePhase,
        int candidateOrdinal,
        Exception innerException)
        : base(innerException.Message, innerException)
    {
        ServicePhase = servicePhase;
        CandidateOrdinal = candidateOrdinal;
    }

    public string ServicePhase { get; }
    public int CandidateOrdinal { get; }
}

internal sealed class TimberCreatedElementAnnotationPhaseException : Exception
{
    public TimberCreatedElementAnnotationPhaseException(
        int candidateOrdinal,
        ObjectId sourceId,
        Exception innerException)
        : base(innerException.Message, innerException)
    {
        CandidateOrdinal = candidateOrdinal;
        SourceId = sourceId;
    }

    public int CandidateOrdinal { get; }
    public ObjectId SourceId { get; }
}

internal sealed class RoofRafterMaterializationPhaseException : Exception
{
    public RoofRafterMaterializationPhaseException(
        string servicePhase,
        int candidateOrdinal,
        Exception innerException)
        : base(innerException.Message, innerException)
    {
        ServicePhase = servicePhase;
        CandidateOrdinal = candidateOrdinal;
    }

    public string ServicePhase { get; }
    public int CandidateOrdinal { get; }
}

#if DEBUG
internal static class RoofRafterPermanentCreateDiag
{
    public static void WriteRequest(
        Editor editor,
        string ownerReference,
        RoofKind roofKind,
        RoofRafterCreationRequest request,
        RoofRafterLayout layout)
    {
        Write(editor,
            "ROOF_RAFTER_CREATE_REQUEST" +
            $" owner={ownerReference}" +
            $" kind={roofKind}" +
            $" spacing={F(request.MaximumSpacingMm)}" +
            $" width={F(request.WidthMm)}" +
            $" height={F(request.HeightMm)}" +
            $" material={Safe(request.Material)}" +
            $" slope={F(request.RoofSlopeDegrees)}" +
            $" signatureFingerprint={RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature)}" +
            $" fullSignatureChars={layout.Signature.Length.ToString(CultureInfo.InvariantCulture)}" +
            $" count={layout.Rafters.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    public static void WriteLayoutSummary(
        Editor editor,
        IRoofGeometry geometry,
        RoofRafterLayout layout)
    {
        try
        {
            var pairs = ResolveBoundaryPairs(geometry, layout);
            var lengths = layout.Rafters.Select(item => item.PlanLengthMm).ToArray();
            var duplicateKeys = layout.Rafters.Count - layout.Rafters
                .Select(item => item.LogicalKey)
                .Distinct()
                .Count();
            var duplicateGeometry = layout.Rafters.Count - layout.Rafters
                .Select(GeometryKey)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var nearZero = layout.Rafters.Count(item =>
                !Finite(item.PlanLengthMm) ||
                item.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
            var invalid = layout.Rafters.Count(item =>
                !Finite(item.PlanStart.X) || !Finite(item.PlanStart.Y) ||
                !Finite(item.PlanEnd.X) || !Finite(item.PlanEnd.Y) ||
                !Finite(item.PlanLengthMm) || !Finite(item.TrueLengthMm) ||
                !Finite(item.SlopeDegrees));

            Write(editor,
                "ROOF_RAFTER_LAYOUT_SUMMARY" +
                $" total={layout.Rafters.Count.ToString(CultureInfo.InvariantCulture)}" +
                $" eaveRidge={CountPair(pairs, RoofRafterBoundaryRole.Eave, RoofRafterBoundaryRole.Ridge)}" +
                $" eaveHip={CountPair(pairs, RoofRafterBoundaryRole.Eave, RoofRafterBoundaryRole.Hip)}" +
                $" eaveValley={CountPair(pairs, RoofRafterBoundaryRole.Eave, RoofRafterBoundaryRole.Valley)}" +
                $" ridgeValley={CountPair(pairs, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley)}" +
                $" minPlanLength={F(lengths.Length == 0 ? double.NaN : lengths.Min())}" +
                $" maxPlanLength={F(lengths.Length == 0 ? double.NaN : lengths.Max())}" +
                $" duplicateLogicalKeys={duplicateKeys.ToString(CultureInfo.InvariantCulture)}" +
                $" duplicateGeometry={duplicateGeometry.ToString(CultureInfo.InvariantCulture)}" +
                $" zeroOrNearZero={nearZero.ToString(CultureInfo.InvariantCulture)}" +
                $" invalidCoordinates={invalid.ToString(CultureInfo.InvariantCulture)}");

            WriteRidgePairSummary(editor, geometry, layout, ownerReference: "-");
            WritePhasePlanSummary(editor, geometry, layout);
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_RAFTER_LAYOUT_SUMMARY" +
                $" total={layout.Rafters.Count.ToString(CultureInfo.InvariantCulture)}" +
                " eaveRidge=- eaveHip=- eaveValley=- ridgeValley=-" +
                " minPlanLength=- maxPlanLength=- duplicateLogicalKeys=-" +
                " duplicateGeometry=- zeroOrNearZero=- invalidCoordinates=-" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    public static void WritePhasePlanSummary(
        Editor editor,
        IRoofGeometry geometry,
        RoofRafterLayout layout)
    {
        try
        {
            if (geometry is not HipRoofGeometry hip)
            {
                return;
            }

            var plan = RoofFaceRafterLayoutService.DescribePhasePlan(
                hip.Topology,
                layout.RequestedMaximumSpacingMm);
            foreach (var component in plan.Components)
            {
                Write(editor,
                    "ROOF_RAFTER_PHASE_COMPONENT" +
                    $" id={component.ComponentId.ToString(CultureInfo.InvariantCulture)}" +
                    $" ridgeEdges={string.Join(",", component.RidgeEdgeIds)}" +
                    $" stationAxis=({F(component.StationAxisX)},{F(component.StationAxisY)})" +
                    $" canonicalPhase={F(component.CanonicalPhaseT)}" +
                    $" incidentFaces={string.Join(",", component.IncidentFaceIds)}" +
                    $" coupling={string.Join("|", component.CouplingReasons)}" +
                    $" collinear={(component.MembersCollinear ? "1" : "0")}" +
                    $" offsetMm={F(component.OffsetDistanceMm)}" +
                    $" consumingFaces={string.Join(",", component.FacesConsumingPhase)}");
            }

            foreach (var face in plan.FaceDecisions)
            {
                Write(editor,
                    "ROOF_RAFTER_FACE_PHASE" +
                    $" face={face.FaceId.ToString(CultureInfo.InvariantCulture)}" +
                    $" stationAxis=({F(face.StationAxisX)},{F(face.StationAxisY)})" +
                    $" ridgeBoundaries={string.Join(",", face.RidgeBoundaryIds)}" +
                    $" candidatePhases={string.Join(",", face.CandidatePhases.Select(F))}" +
                    $" selectedPhase={(face.SelectedPhaseT is null ? "-" : F(face.SelectedPhaseT.Value))}" +
                    $" fallbackUsed={(face.FallbackUsed ? "1" : "0")}" +
                    $" fallbackReason={Safe(face.FallbackReason)}");
            }
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_RAFTER_PHASE_COMPONENT" +
                " id=- result=ERROR" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    public static void WriteRidgePairSummary(
        Editor editor,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        string ownerReference)
    {
        try
        {
            if (geometry is not HipRoofGeometry hip)
            {
                return;
            }

            var solved = RoofFaceRafterLayoutService.Create(
                hip.Topology,
                layout.RequestedMaximumSpacingMm);
            if (!solved.IsValid || solved.Layout is null)
            {
                return;
            }

            var report = RoofFaceRafterLayoutService.EvaluateSharedRidgePairing(
                hip.Topology,
                solved.Layout);
            Write(editor,
                "ROOF_RAFTER_RIDGE_PAIR_SUMMARY" +
                $" owner={Safe(ownerReference)}" +
                $" ridgeComponentCount={report.RidgeComponentCount.ToString(CultureInfo.InvariantCulture)}" +
                $" pairedComponentCount={report.PairedComponentCount.ToString(CultureInfo.InvariantCulture)}" +
                $" leftEndpointCount={report.LeftEndpointCount.ToString(CultureInfo.InvariantCulture)}" +
                $" rightEndpointCount={report.RightEndpointCount.ToString(CultureInfo.InvariantCulture)}" +
                $" matchedStationCount={report.MatchedStationCount.ToString(CultureInfo.InvariantCulture)}" +
                $" unmatchedLeft={report.UnmatchedLeft.ToString(CultureInfo.InvariantCulture)}" +
                $" unmatchedRight={report.UnmatchedRight.ToString(CultureInfo.InvariantCulture)}" +
                $" maxPairGapMm={F(report.MaxPairGapMm)}" +
                $" result={(report.IsSatisfied ? "PASS" : "FAIL")}");

            foreach (var failure in report.Failures.Take(5))
            {
                Write(editor,
                    "ROOF_RAFTER_RIDGE_PAIR_FAIL" +
                    $" component={failure.ComponentEdgeIndex.ToString(CultureInfo.InvariantCulture)}" +
                    $" ridgeStart={FormatPoint(failure.RidgeStart)}" +
                    $" ridgeEnd={FormatPoint(failure.RidgeEnd)}" +
                    $" faceA={failure.FaceA.ToString(CultureInfo.InvariantCulture)}" +
                    $" faceB={failure.FaceB.ToString(CultureInfo.InvariantCulture)}" +
                    $" stationA={F(failure.StationAMm)}" +
                    $" stationB={F(failure.StationBMm)}" +
                    $" pointA={FormatPoint(failure.PointA)}" +
                    $" pointB={FormatPoint(failure.PointB)}" +
                    $" gapMm={F(failure.GapMm)}" +
                    $" boundaryA={failure.FaceAStartRole}->{failure.FaceAEndRole}" +
                    $" boundaryB={failure.FaceBStartRole}->{failure.FaceBEndRole}");
            }
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_RAFTER_RIDGE_PAIR_SUMMARY" +
                $" owner={Safe(ownerReference)}" +
                " ridgeComponentCount=- pairedComponentCount=- leftEndpointCount=-" +
                " rightEndpointCount=- matchedStationCount=- unmatchedLeft=-" +
                " unmatchedRight=- maxPairGapMm=- result=ERROR" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    public static void WriteMaterializeFailure(
        Editor editor,
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        int candidateOrdinal,
        string servicePhase,
        Exception exception)
    {
        try
        {
            var candidate = candidateOrdinal >= 0 && candidateOrdinal < layout.Rafters.Count
                ? layout.Rafters[candidateOrdinal]
                : null;
            var pair = candidate is null
                ? default(BoundaryPair?)
                : ResolveBoundaryPair(geometry, layout, candidateOrdinal);
            var error = Innermost(exception);
            Write(editor,
                "ROOF_RAFTER_MATERIALIZE_FAIL" +
                $" candidate={FormatOrdinal(candidateOrdinal)}" +
                $" logicalKey={FormatKey(candidate?.LogicalKey)}" +
                $" face={candidate?.Face.ToString() ?? "-"}" +
                $" stationIndex={candidate?.StationIndex.ToString(CultureInfo.InvariantCulture) ?? "-"}" +
                $" startBoundaryRole={pair?.Start.ToString() ?? "-"}" +
                $" endBoundaryRole={pair?.End.ToString() ?? "-"}" +
                $" startXY={FormatPoint(candidate?.PlanStart)}" +
                $" endXY={FormatPoint(candidate?.PlanEnd)}" +
                $" planLength={F(candidate?.PlanLengthMm ?? double.NaN)}" +
                $" slope={F(candidate?.SlopeDegrees ?? double.NaN)}" +
                $" width={F(recipe.WidthMm)}" +
                $" height={F(recipe.HeightMm)}" +
                $" service={Safe(servicePhase)}" +
                $" exception={Safe(error.GetType().FullName ?? error.GetType().Name)}:{Safe(error.Message)}");
        }
        catch (Exception diagnosticException)
        {
            var error = Innermost(exception);
            Write(editor,
                "ROOF_RAFTER_MATERIALIZE_FAIL" +
                $" candidate={FormatOrdinal(candidateOrdinal)}" +
                " logicalKey=- face=- stationIndex=- startBoundaryRole=- endBoundaryRole=-" +
                " startXY=- endXY=- planLength=- slope=-" +
                $" width={F(recipe.WidthMm)} height={F(recipe.HeightMm)}" +
                $" service={Safe(servicePhase)}" +
                $" exception={Safe(error.GetType().FullName ?? error.GetType().Name)}:{Safe(error.Message)}" +
                $" diagnosticError={Safe(diagnosticException.GetType().Name)}:{Safe(diagnosticException.Message)}");
        }
    }

    public static void WriteCreateFailure(
        Editor editor,
        string ownerReference,
        string phase,
        int candidateOrdinal,
        Exception exception)
    {
        var error = Innermost(exception);
        Write(editor,
            "ROOF_RAFTER_CREATE_FAIL" +
            $" owner={ownerReference}" +
            $" phase={Safe(phase)}" +
            $" candidate={FormatOrdinal(candidateOrdinal)}" +
            $" exception={Safe(error.GetType().FullName ?? error.GetType().Name)}:{Safe(error.Message)}" +
            " transaction=rollback");
    }

    public static void WriteCreateResultFailure(
        Editor editor,
        string ownerReference,
        string phase,
        string resultCode) =>
        Write(editor,
            "ROOF_RAFTER_CREATE_FAIL" +
            $" owner={ownerReference}" +
            $" phase={Safe(phase)}" +
            " candidate=-" +
            $" exception=result:{Safe(resultCode)}" +
            " transaction=rollback");

    public static void WriteSuccess(
        Editor editor,
        string ownerReference,
        int generatedCount,
        int annotationCount) =>
        Write(editor,
            "ROOF_RAFTER_CREATE_SUCCESS" +
            $" owner={ownerReference}" +
            $" generated={generatedCount.ToString(CultureInfo.InvariantCulture)}" +
            $" annotations={annotationCount.ToString(CultureInfo.InvariantCulture)}" +
            " transaction=commit");

    public static int CountAnnotations(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> sourceIds)
    {
        try
        {
            var sourceHandles = sourceIds
                .Where(id => !id.IsNull && !id.IsErased)
                .Select(id => id.Handle.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var count = 0;
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
                        database) ||
                    entity is null ||
                    !RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var sourceHandle) ||
                    !sourceHandles.Contains(sourceHandle))
                {
                    continue;
                }

                count++;
            }

            return count;
        }
        catch
        {
            return -1;
        }
    }

    public static void WriteHipSourcePolygon(
        Editor editor,
        string ownerReference,
        RoofFootprintInput source)
    {
        try
        {
            var vertices = source.Vertices ?? Array.Empty<RoofPoint2D>();
            Write(editor,
                "ROOF_HIP_SOURCE_POLYGON" +
                $" owner={Safe(ownerReference)}" +
                $" vertexCount={vertices.Count.ToString(CultureInfo.InvariantCulture)}" +
                $" fingerprint={PolygonFingerprint(vertices)}" +
                $" coords={FormatPolygonCoords(vertices)}");
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_HIP_SOURCE_POLYGON" +
                $" owner={Safe(ownerReference)}" +
                " vertexCount=- fingerprint=- coords=-" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    public static void WriteSolveInput(
        Editor editor,
        string ownerReference,
        RoofFootprintInput source,
        HipRoofGeometry geometry,
        double spacingMm,
        RoofRafterLayout layout)
    {
        try
        {
            var vertices = source.Vertices ?? Array.Empty<RoofPoint2D>();
            var topology = geometry.Topology;
            Write(editor,
                "ROOF_RAFTER_SOLVE_INPUT" +
                $" owner={Safe(ownerReference)}" +
                $" vertexCount={vertices.Count.ToString(CultureInfo.InvariantCulture)}" +
                $" fingerprint={PolygonFingerprint(vertices)}" +
                $" topologyVertices={topology.BoundaryVertexCount.ToString(CultureInfo.InvariantCulture)}" +
                $" ridge={topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge).ToString(CultureInfo.InvariantCulture)}" +
                $" hip={topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip).ToString(CultureInfo.InvariantCulture)}" +
                $" valley={topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley).ToString(CultureInfo.InvariantCulture)}" +
                $" spacing={F(spacingMm)}" +
                $" slope={F(geometry.PrimarySlopeDegrees)}" +
                $" signatureFingerprint={RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature)}" +
                $" rafterCount={layout.Rafters.Count.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_RAFTER_SOLVE_INPUT" +
                $" owner={Safe(ownerReference)}" +
                " result=ERROR" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    public static void WriteFaceCoverageSummary(
        Editor editor,
        HipRoofGeometry geometry,
        RoofRafterLayout layout)
    {
        try
        {
            var faceResult = RoofFaceRafterLayoutService.Create(
                geometry.Topology,
                layout.RequestedMaximumSpacingMm);
            if (!faceResult.IsValid || faceResult.Layout is null)
            {
                Write(editor,
                    "ROOF_RAFTER_FACE_COVERAGE_SUMMARY" +
                    " face=- result=ERROR reason=face-layout-invalid");
                return;
            }

            var reports = RoofFaceRafterLayoutService.EvaluateFaceCoverage(
                geometry.Topology,
                faceResult.Layout);
            foreach (var report in reports)
            {
                Write(editor,
                    "ROOF_RAFTER_FACE_COVERAGE_SUMMARY" +
                    $" face={report.FaceId.ToString(CultureInfo.InvariantCulture)}" +
                    $" stationAxis=({F(report.StationAxisX)},{F(report.StationAxisY)})" +
                    $" fullMinT={F(report.FullFaceMinT)}" +
                    $" fullMaxT={F(report.FullFaceMaxT)}" +
                    $" ridgeMinT={(report.RidgeFamilyMinT is null ? "-" : F(report.RidgeFamilyMinT.Value))}" +
                    $" ridgeMaxT={(report.RidgeFamilyMaxT is null ? "-" : F(report.RidgeFamilyMaxT.Value))}" +
                    $" enumMinT={F(report.EnumerationMinT)}" +
                    $" enumMaxT={F(report.EnumerationMaxT)}" +
                    $" phase={F(report.PhaseT)}" +
                    $" firstStationT={(report.FirstGeneratedStationT is null ? "-" : F(report.FirstGeneratedStationT.Value))}" +
                    $" lastStationT={(report.LastGeneratedStationT is null ? "-" : F(report.LastGeneratedStationT.Value))}" +
                    $" stationCount={report.StationCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" emittedSegments={report.EmittedSegmentCount.ToString(CultureInfo.InvariantCulture)}" +
                    $" minPlanLength={F(report.MinPlanLengthMm)}" +
                    $" maxPlanLength={F(report.MaxPlanLengthMm)}" +
                    $" firstRoles={Safe(report.FirstTerminationRoles)}" +
                    $" lastRoles={Safe(report.LastTerminationRoles)}" +
                    $" uncoveredStartMm={F(report.UncoveredStartMm)}" +
                    $" uncoveredEndMm={F(report.UncoveredEndMm)}" +
                    $" result={report.Result}");
            }
        }
        catch (Exception ex)
        {
            Write(editor,
                "ROOF_RAFTER_FACE_COVERAGE_SUMMARY" +
                " face=- result=ERROR" +
                $" diagnosticError={Safe(ex.GetType().Name)}:{Safe(ex.Message)}");
        }
    }

    private static string PolygonFingerprint(IReadOnlyList<RoofPoint2D> vertices)
    {
        if (vertices.Count == 0)
        {
            return "-";
        }

        var payload = string.Join(";", vertices.Select(point =>
            Math.Round(point.X, 3, MidpointRounding.AwayFromZero)
                .ToString("R", CultureInfo.InvariantCulture) + "," +
            Math.Round(point.Y, 3, MidpointRounding.AwayFromZero)
                .ToString("R", CultureInfo.InvariantCulture)));
        return RoofGeneratedLayoutFingerprint.ToPersistedIdentity("POLY:" + payload);
    }

    private static string FormatPolygonCoords(IReadOnlyList<RoofPoint2D> vertices)
    {
        if (vertices.Count == 0)
        {
            return "-";
        }

        if (vertices.Count > 24)
        {
            return PolygonFingerprint(vertices);
        }

        return string.Join(";", vertices.Select(PointKey));
    }

    private static IReadOnlyList<BoundaryPair> ResolveBoundaryPairs(
        IRoofGeometry geometry,
        RoofRafterLayout layout)
    {
        if (geometry is not HipRoofGeometry hip)
        {
            return Array.Empty<BoundaryPair>();
        }

        var solved = RoofFaceRafterLayoutService.Create(
            hip.Topology,
            layout.RequestedMaximumSpacingMm);
        if (!solved.IsValid || solved.Layout is null ||
            !string.Equals(solved.Layout.Signature, layout.Signature, StringComparison.Ordinal) ||
            solved.Layout.Segments.Count != layout.Rafters.Count)
        {
            return Array.Empty<BoundaryPair>();
        }

        return solved.Layout.Segments
            .Select(item => new BoundaryPair(item.StartBoundaryRole, item.EndBoundaryRole))
            .ToArray();
    }

    private static BoundaryPair? ResolveBoundaryPair(
        IRoofGeometry geometry,
        RoofRafterLayout layout,
        int candidateOrdinal)
    {
        var pairs = ResolveBoundaryPairs(geometry, layout);
        return candidateOrdinal >= 0 && candidateOrdinal < pairs.Count
            ? pairs[candidateOrdinal]
            : null;
    }

    private static int CountPair(
        IReadOnlyList<BoundaryPair> pairs,
        RoofRafterBoundaryRole first,
        RoofRafterBoundaryRole second) =>
        pairs.Count(pair =>
            (pair.Start == first && pair.End == second) ||
            (pair.Start == second && pair.End == first));

    private static string GeometryKey(RoofRafterGeometry candidate)
    {
        var first = PointKey(candidate.PlanStart);
        var second = PointKey(candidate.PlanEnd);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static string PointKey(RoofPoint2D point) =>
        F(Math.Round(point.X, 6, MidpointRounding.AwayFromZero)) + "," +
        F(Math.Round(point.Y, 6, MidpointRounding.AwayFromZero));

    private static string FormatPoint(RoofPoint2D? point) =>
        point is null ? "-" : PointKey(point.Value);

    private static string FormatKey(RoofGeneratedMemberKey? key) =>
        key is null
            ? "-"
            : $"{key.Value.MemberKind}/{key.Value.RoofFace}/{key.Value.StationIndex.ToString(CultureInfo.InvariantCulture)}";

    private static string FormatOrdinal(int ordinal) =>
        ordinal < 0 ? "-" : ordinal.ToString(CultureInfo.InvariantCulture);

    private static string F(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static Exception Innermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    private static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static void Write(Editor editor, string message)
    {
        try
        {
            editor.WriteMessage("\n" + message);
        }
        catch
        {
        }
    }

    private readonly record struct BoundaryPair(
        RoofRafterBoundaryRole Start,
        RoofRafterBoundaryRole End);
}
#endif
