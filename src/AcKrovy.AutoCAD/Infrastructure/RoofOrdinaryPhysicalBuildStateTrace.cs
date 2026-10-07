using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only command snapshot evidence; never persisted or used to select a recovery path.</summary>
internal sealed class RoofOrdinaryPhysicalBuildStateTrace
{
    private readonly Dictionary<string, string> _fields = new();
    internal string Failure { get; private set; } = "none";

    internal void Add(string key, object? value) => _fields[key] = value switch
    {
        null => "none",
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? "none",
        _ => value.ToString() ?? "none",
    };

    internal bool Fail(string reason)
    {
        Failure = reason;
        return false;
    }

    internal void State(RoofOrdinaryPhysicalBuildState? state)
    {
        if (state is null) return;
        Add("storedVersion", state.Version);
        Add("storedSection", $"{state.WidthMm.ToString("R", CultureInfo.InvariantCulture)}x{state.HeightMm.ToString("R", CultureInfo.InvariantCulture)}");
        Add("storedEaveElevationMm", state.EaveElevationMm);
        Add("storedPitchDegrees", state.PitchDegrees);
        Add("storedTopology", $"nodes:{state.Nodes?.Length ?? 0},faces:{state.Faces?.Length ?? 0},edges:{state.Edges?.Length ?? 0}");
        Add("storedFace", state.Anchor?.SourceFaceIndex);
        var face = state.Faces?.FirstOrDefault(f => f?.SourceEdge == state.Anchor?.SourceFaceIndex);
        Add("storedFaceNodes", face?.Nodes is null || state.Nodes is null ? "none" :
            string.Join(",", face.Nodes.Where(i => i >= 0 && i < state.Nodes.Length).Select(i => Point(state.Nodes[i]))));
        Add("storedEaveEdge", state.Anchor?.SourceEaveEdgeIndex);
        Add("storedStartBoundary", state.Anchor?.StartBoundaryRole);
        Add("storedEndBoundary", state.Anchor?.EndBoundaryRole);
        Add("storedStructuralSources", state.StructuralSources?.Length);
        Add("storedCuts", state.Settings);
        Add("storedAcceptedPlan", Axis(state.AcceptedPlanAxis));
        Add("storedFrame", state.SectionFrame is null ? "none" :
            $"L:{Point(state.SectionFrame.LongitudinalAxis)},W:{Point(state.SectionFrame.WidthAxis)},H:{Point(state.SectionFrame.HeightAxis)}");
    }

    internal void InvalidState(RoofOrdinaryPhysicalBuildState? state)
    {
        string component;
        if (state is null) component = "state_null";
        else if (state.Version is not (1 or RoofOrdinaryPhysicalBuildState.CurrentVersion)) component = "version";
        else if (state.SectionFrame is not null && !RoofOrdinarySectionTransportRules.IsValid(state.SectionFrame)) component = "section_frame";
        else if (state.Nodes is not { Length: >= 3 } || state.Nodes.Any(p => !Finite(p))) component = "topology_nodes";
        else if (state.Faces is not { Length: > 0 } || state.Faces.Any(f => f is null ||
            f.Nodes is not { Length: >= 3 } || f.Nodes.Any(i => i < 0 || i >= state.Nodes.Length))) component = "topology_faces";
        else if (state.Edges is null || state.Edges.Any(e => e is null || e.Start < 0 || e.Start >= state.Nodes.Length ||
            e.End < 0 || e.End >= state.Nodes.Length || !Enum.IsDefined(typeof(RoofTopologyEdgeKind), e.Kind) ||
            e.Faces is null || e.Faces.Any(i => !state.Faces.Any(f => f.SourceEdge == i)))) component = "topology_edges";
        else if (state.BoundaryVertexCount < 3 || state.BoundaryVertexCount > state.Nodes.Length) component = "boundary_vertex_count";
        else if (state.Anchor is null) component = "anchor";
        else if (state.Settings is null) component = "cut_settings";
        else if (state.StructuralSources is null) component = "structural_sources";
        else if (!Finite(state.EaveElevationMm)) component = "eave_elevation";
        else if (!Finite(state.PitchDegrees)) component = "pitch";
        else if (!Finite(state.WidthMm) || state.WidthMm <= 0 || !Finite(state.HeightMm) || state.HeightMm <= 0) component = "section_dimensions";
        else if (!Finite(state.AcceptedPlanAxis.Start) || !Finite(state.AcceptedPlanAxis.End) ||
            state.AcceptedPlanAxis.Start.DistanceTo(state.AcceptedPlanAxis.End) <= 0.0001d) component = "accepted_plan_axis";
        else component = "unclassified";
        Add("invalidStoredComponent", component);
    }

    internal void Rebase(string phase, RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan, bool resolved)
    {
        var start = Delta(state.AcceptedPlanAxis.Start, plan.Start);
        var end = Delta(state.AcceptedPlanAxis.End, plan.End);
        Add(phase + "Resolved", resolved);
        Add(phase + "StartDelta", Point(start));
        Add(phase + "EndDelta", Point(end));
        Add(phase + "DeltaMismatchMm", start.DistanceTo(end));
        if (!resolved) Fail("RoofOrdinaryPhysicalBuildStateRules.TryRebase:" + phase +
            (!RoofOrdinaryPhysicalBuildStateRules.IsValid(state) ? ":invalid_state" :
                Math.Abs(start.Z) > 0.0001d ? ":non_plan_translation" : ":endpoint_deltas_differ"));
    }

    internal string Fields => string.Join(" ", _fields.Select(pair => pair.Key + "=" +
        pair.Value.Replace('\r', '_').Replace('\n', '_').Replace(' ', '_')));
    internal static string Point(RoofPoint3D point) => FormattableString.Invariant($"({point.X:R},{point.Y:R},{point.Z:R})");
    internal static string Axis(RoofSegment3D axis) => Point(axis.Start) + "->" + Point(axis.End);
    private static RoofPoint3D Delta(RoofPoint3D before, RoofPoint3D after) =>
        new(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Finite(RoofPoint3D point) => Finite(point.X) && Finite(point.Y) && Finite(point.Z);
}
