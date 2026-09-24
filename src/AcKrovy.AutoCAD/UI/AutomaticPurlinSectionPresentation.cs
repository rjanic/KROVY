using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Pure presentation model for the Automatic-Purlin roof section. It intersects
/// already-solved topology and planned members with a vertical section plane; it
/// never re-solves purlin placement.
/// </summary>
internal sealed record AutomaticPurlinSectionPresentation(
    IReadOnlyList<AutomaticPurlinSectionLineMm> SlopeLines,
    IReadOnlyList<AutomaticPurlinSectionRafterMm> Rafters,
    IReadOnlyList<AutomaticPurlinSectionWallMm> Walls,
    IReadOnlyList<AutomaticPurlinSectionMemberMm> Members,
    double MinXMm,
    double MaxXMm,
    double MinZMm,
    double MaxZMm,
    double RafterHeightMm,
    double RafterWidthMm = 80d,
    string RafterHeightDimensionText = "",
    string RafterCaptionText = "",
    AutomaticPurlinSectionReferencePlane? ReferencePlane = null)
{
    private const double IntersectionToleranceMm = 0.01d;

    internal static AutomaticPurlinSectionPresentation Empty { get; } = new(
        Array.Empty<AutomaticPurlinSectionLineMm>(), Array.Empty<AutomaticPurlinSectionRafterMm>(),
        Array.Empty<AutomaticPurlinSectionWallMm>(),
        Array.Empty<AutomaticPurlinSectionMemberMm>(), 0d, 1d, 0d, 1d, 160d, 80d);

    internal string RafterDimensionText => string.IsNullOrWhiteSpace(RafterCaptionText)
        ? string.Format(
            CultureInfo.CurrentUICulture,
            "{0:0.###} × {1:0.###}",
            RafterWidthMm,
            RafterHeightMm)
        : string.Format(
            CultureInfo.CurrentUICulture,
            "{0} {1:0.###} × {2:0.###}",
            RafterCaptionText,
            RafterWidthMm,
            RafterHeightMm);

    internal string RafterSectionDimensionText => string.Format(
        CultureInfo.CurrentUICulture,
        "{0:0.###} × {1:0.###}",
        RafterWidthMm,
        RafterHeightMm);

    internal static AutomaticPurlinSectionPresentation Create(
        HipRoofGeometry geometry,
        RoofAutomaticPurlinPlan? plan,
        Func<RoofAutomaticPurlinPlanItem, string> titleResolver,
        Func<string, string> text,
        CultureInfo culture,
        double rafterHeightMm = 160d,
        double rafterWidthMm = 80d,
        RoofRelativeElevationDatum? referenceDatum = null,
        RoofAutomaticPurlinLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(titleResolver);
        ArgumentNullException.ThrowIfNull(text);
        culture ??= CultureInfo.InvariantCulture;
        if (!TryBuildSectionFrame(geometry, out var frame)) return Empty;

        var rafterHeight = double.IsFinite(rafterHeightMm) && rafterHeightMm > 0d ? rafterHeightMm : 160d;
        var rafterWidth = double.IsFinite(rafterWidthMm) && rafterWidthMm > 0d ? rafterWidthMm : 80d;
        var slopes = BuildSlopeLines(geometry.Topology, frame);
        var rafters = BuildRafters(slopes, rafterHeight);
        // Architectural CenterLocalZMm shares the SourceEave face frame (eave upper face ≈ 0).
        // BuildRafters now keeps that same upper-face line as the drawn rafter top, so members
        // and the reference guide stay in one physical-to-schematic elevation frame.
        // Plan-distance / ridge seating still aligns rectangle tops to the (now physical)
        // rafter edge at the seating fraction when needed.
        var schematicOriginLocalZMm = ResolveSchematicElevationOriginLocalZMm(
            slopes,
            rafters,
            rafterHeight,
            rafterWidth);
        var members = ApplySchematicSeating(
            MapMembersToSchematicElevationSpace(
                BuildMembers(plan, layout, frame, titleResolver, text, culture),
                schematicOriginLocalZMm),
            rafters,
            rafterHeight);
        var walls = BuildWalls(slopes, members, rafterHeight);
        // Provisional presentation for reference-plane / eave-tip resolution.
        var provisional = new AutomaticPurlinSectionPresentation(
            slopes, rafters, walls, members,
            0d, 1d, 0d, 1d,
            rafterHeight,
            rafterWidth);
        var referencePlane = TryCreateReferencePlane(
            referenceDatum,
            culture,
            rafters,
            members,
            provisional);
        var xs = rafters.SelectMany(rafter => rafter.Corners.Select(point => point.XMm))
            .Concat(walls.SelectMany(wall => new[] { wall.CenterXMm - wall.WidthMm / 2d, wall.CenterXMm + wall.WidthMm / 2d }))
            .Concat(members.SelectMany(member => new[] { member.CenterXMm - member.WidthMm / 2d, member.CenterXMm + member.WidthMm / 2d }))
            .Where(double.IsFinite).ToArray();
        var zs = rafters.SelectMany(rafter => rafter.Corners.Select(point => point.ZMm))
            .Concat(walls.SelectMany(wall => new[] { wall.TopZMm, wall.BottomZMm }))
            .Concat(members.SelectMany(member => new[] { member.CenterZMm - member.HeightMm / 2d, member.CenterZMm + member.HeightMm / 2d }))
            .Concat(referencePlane is null
                ? Array.Empty<double>()
                : new[] { referencePlane.VisualLocalZMm })
            .Where(double.IsFinite).ToArray();
        if (xs.Length == 0 || zs.Length == 0) return Empty;

        var heightDimension = string.Format(
            culture,
            "{0:0.###} {1}",
            rafterHeight,
            text("AutomaticPurlin_UnitMillimetres"));
        var caption = text("AutomaticPurlin_SchematicRafter");

        return new AutomaticPurlinSectionPresentation(
            slopes, rafters, walls, members,
            xs.Min(), xs.Max(), zs.Min(), zs.Max(),
            rafterHeight,
            rafterWidth,
            heightDimension,
            caption,
            referencePlane);
    }

    private static List<AutomaticPurlinSectionRafterMm> BuildRafters(
        IEnumerable<AutomaticPurlinSectionLineMm> slopes,
        double rafterHeightMm)
    {
        var result = new List<AutomaticPurlinSectionRafterMm>();
        foreach (var line in slopes)
        {
            var dx = line.X2Mm - line.X1Mm;
            var dz = line.Z2Mm - line.Z1Mm;
            var length = Math.Sqrt(dx * dx + dz * dz);
            if (length <= IntersectionToleranceMm)
            {
                continue;
            }

            // Slope line = physical UPPER rafter face (roof surface), matching Core.
            // Offset once by full perpendicular height into the timber for the lower face.
            // (±H/2 about the face as a "centerline" put the drawn upper above the physical
            // upper face and broke BottomEdge tip-mapped contact.)
            var nx = -dz / length;
            var nz = dx / length;
            if (nz > 0d)
            {
                nx = -nx;
                nz = -nz;
            }

            var ox = nx * rafterHeightMm;
            var oz = nz * rafterHeightMm;
            result.Add(new AutomaticPurlinSectionRafterMm(
                SideOf((line.X1Mm + line.X2Mm) / 2d),
                line,
                [
                    new(line.X1Mm, line.Z1Mm),
                    new(line.X2Mm, line.Z2Mm),
                    new(line.X2Mm + ox, line.Z2Mm + oz),
                    new(line.X1Mm + ox, line.Z1Mm + oz),
                ]));
        }

        return result
            .OrderBy(rafter => rafter.Side)
            .ToList();
    }

    private static List<AutomaticPurlinSectionLineMm> BuildSlopeLines(RoofTopology topology, SectionFrame frame)
    {
        var lines = new List<AutomaticPurlinSectionLineMm>();
        foreach (var face in topology.Faces)
        {
            var points = IntersectFace(face, topology, frame);
            if (points.Count < 2) continue;
            var first = points.OrderBy(frame.ProjectX).First();
            var last = points.OrderBy(frame.ProjectX).Last();
            if (Math.Abs(frame.ProjectX(last) - frame.ProjectX(first)) <= IntersectionToleranceMm &&
                Math.Abs(last.Z - first.Z) <= IntersectionToleranceMm) continue;
            lines.Add(new AutomaticPurlinSectionLineMm(frame.ProjectX(first), first.Z, frame.ProjectX(last), last.Z));
        }

        return DeduplicateLines(lines);
    }

    private static List<RoofPoint3D> IntersectFace(RoofTopologyFace face, RoofTopology topology, SectionFrame frame)
    {
        var points = new List<RoofPoint3D>();
        var indices = face.BoundaryNodeIndices;
        for (var index = 0; index < indices.Count; index++)
        {
            AddIntersections(topology.Nodes[indices[index]], topology.Nodes[indices[(index + 1) % indices.Count]], frame, points);
        }

        return DeduplicatePoints(points, frame);
    }

    private static List<AutomaticPurlinSectionMemberMm> BuildMembers(
        RoofAutomaticPurlinPlan? plan,
        RoofAutomaticPurlinLayout? layout,
        SectionFrame frame,
        Func<RoofAutomaticPurlinPlanItem, string> titleResolver,
        Func<string, string> text,
        CultureInfo culture)
    {
        if (plan is null) return [];
        var candidates = new List<MemberCandidate>();
        foreach (var item in plan.Items)
        {
            if (item.ElevationProfile is null || !double.IsFinite(item.WidthMm) || item.WidthMm <= 0d ||
                !double.IsFinite(item.HeightMm) || item.HeightMm <= 0d ||
                !TryIntersectSegmentWithSectionPlane(item.Segment3D, frame, out var intersection)) continue;
            candidates.Add(new MemberCandidate(
                item,
                frame.ProjectX(intersection),
                ResolvePlacementMode(item, layout)));
        }

        // Split plan intervals can meet one cut. Show one physical body per role, row and side;
        // never all front/back intervals merely because they share an elevation.
        return candidates
            .GroupBy(candidate => new
            {
                candidate.Item.GeneratorRole,
                LayoutItemId = candidate.Item.LayoutItemId ?? candidate.Item.GeneratedKey.ToString(),
                Side = SideOf(candidate.CenterXMm),
            })
            .Select(group => group.OrderBy(candidate => Math.Abs(candidate.CenterXMm))
                .ThenBy(candidate => candidate.Item.GeneratedKey.ToString(), StringComparer.Ordinal).First())
            .OrderBy(candidate => candidate.Item.GeneratorRole)
            .ThenBy(candidate => candidate.Item.LayoutItemId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CenterXMm)
            .Select(candidate => CreateMember(candidate, titleResolver, text, culture))
            .ToList();
    }

    private static RoofAutomaticPurlinPlacementMode? ResolvePlacementMode(
        RoofAutomaticPurlinPlanItem item,
        RoofAutomaticPurlinLayout? layout)
    {
        if (layout is null)
        {
            return null;
        }

        if (item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
        {
            return RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout).PlacementMode;
        }

        if (item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
            !string.IsNullOrWhiteSpace(item.LayoutItemId))
        {
            return layout.IntermediateItems
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.LayoutItemId, item.LayoutItemId, StringComparison.Ordinal))
                ?.PlacementMode;
        }

        return null;
    }

    private static AutomaticPurlinSectionMemberMm CreateMember(
        MemberCandidate candidate,
        Func<RoofAutomaticPurlinPlanItem, string> titleResolver,
        Func<string, string> text,
        CultureInfo culture)
    {
        var item = candidate.Item;
        var profile = item.ElevationProfile!;
        var seatingDepthMm = item.PhysicalPlacement?.SeatingDepthMm ?? profile.SeatingDepthMm;
        return new AutomaticPurlinSectionMemberMm(
            item.GeneratedKey.ToString(), item.GeneratorRole, titleResolver(item),
            string.Format(culture, "{0:0.###} × {1:0.###}", item.WidthMm, item.HeightMm),
            FormatEdgeLabel(text("AutomaticPurlin_SectionBottomAbbrev"), profile.BottomRelativeElevationMm, culture),
            string.Empty,
            FormatEdgeLabel(text("AutomaticPurlin_SectionTopAbbrev"), profile.TopRelativeElevationMm, culture),
            SideOf(candidate.CenterXMm), candidate.CenterXMm, profile.CenterLocalZMm,
            item.WidthMm, item.HeightMm,
            item.PhysicalPlacement?.RafterLowerSurfaceLocalZMm,
            seatingDepthMm,
            item.LayoutItemId,
            candidate.PlacementMode);
    }

    private static string FormatEdgeLabel(string abbrev, double relativeElevationMm, CultureInfo culture) =>
        string.Format(
            culture,
            "{0} {1}",
            abbrev,
            RoofRelativeElevationDatumRules.FormatMetres(relativeElevationMm, culture));

    /// <summary>
    /// Plan-distance / ridge: align the rectangle top to the SVG visual rafter edge at the
    /// seating fraction (0% = lower drawn edge, 100% = upper). BottomEdgeHeightAboveReference
    /// keeps physical CenterZ (tip-mapped) so bottoms stay on the ±0.000 reference.
    /// ElevationProfile label texts are never rewritten.
    /// </summary>
    private static List<AutomaticPurlinSectionMemberMm> ApplySchematicSeating(
        IReadOnlyList<AutomaticPurlinSectionMemberMm> members,
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        double rafterHeightMm)
    {
        if (members.Count == 0)
        {
            return members as List<AutomaticPurlinSectionMemberMm> ?? members.ToList();
        }

        var defaultSeatingMm =
            rafterHeightMm * RoofAutomaticPurlinPlanner.DefaultWallPlateSeatingPercent / 100d;
        var result = new List<AutomaticPurlinSectionMemberMm>(members.Count);
        foreach (var member in members)
        {
            if (member.Role is not (RoofAutomaticPurlinGeneratorRole.WallPlate
                    or RoofAutomaticPurlinGeneratorRole.Intermediate
                    or RoofAutomaticPurlinGeneratorRole.Ridge))
            {
                result.Add(member);
                continue;
            }

            var depth = member.SeatingDepthMm is { } seating &&
                        seating >= 0d &&
                        seating <= rafterHeightMm
                ? seating
                : defaultSeatingMm;
            if (depth < 0d || !(member.HeightMm > 0d) || !(member.WidthMm > 0d) ||
                !(rafterHeightMm > 0d))
            {
                result.Add(member with { SeatingDepthMm = depth });
                continue;
            }

            var p = depth / rafterHeightMm;
            if (!TryResolveCornerContactTopZMm(rafters, member, p, out var topZ, out var lowerAtContact))
            {
                result.Add(member with { SeatingDepthMm = depth });
                continue;
            }

            if (ShouldAlignRectangleTopToVisualSeating(member))
            {
                result.Add(member with
                {
                    CenterZMm = topZ - member.HeightMm / 2d,
                    RafterLowerSurfaceZMm = lowerAtContact,
                    SeatingDepthMm = depth,
                });
                continue;
            }

            result.Add(member with
            {
                RafterLowerSurfaceZMm = lowerAtContact,
                SeatingDepthMm = depth,
            });
        }

        return result;
    }

    /// <summary>
    /// Plan-distance seating and ridge purlins follow SVG visual thickness.
    /// Bottom-edge elevation authority keeps the physical tip-mapped profile.
    /// Layout is required to distinguish plan-distance from bottom-edge; when omitted,
    /// only ridge is visually re-aligned.
    /// </summary>
    private static bool ShouldAlignRectangleTopToVisualSeating(AutomaticPurlinSectionMemberMm member) =>
        member.Role == RoofAutomaticPurlinGeneratorRole.Ridge ||
        member.PlacementMode is RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave
            or RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

    /// <summary>
    /// Drawn eave-tip Z used as the schematic origin for SourceEave / ExplicitLocalPlane
    /// reference guides. Architectural local Z = 0 maps to this origin so member bottoms
    /// at relative ±0.000 coincide with the reference line.
    /// </summary>
    private static double ResolveSchematicElevationOriginLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionLineMm> slopes,
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        double rafterHeightMm,
        double rafterWidthMm)
    {
        var tipProbe = new AutomaticPurlinSectionPresentation(
            slopes,
            rafters,
            Array.Empty<AutomaticPurlinSectionWallMm>(),
            Array.Empty<AutomaticPurlinSectionMemberMm>(),
            0d,
            1d,
            0d,
            1d,
            rafterHeightMm,
            rafterWidthMm);
        if (AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                tipProbe,
                out var visualTopZ) &&
            double.IsFinite(visualTopZ))
        {
            return visualTopZ;
        }

        return TryResolveSourceEaveUpperTipLocalZMm(rafters, out var tipZ) && double.IsFinite(tipZ)
            ? tipZ
            : 0d;
    }

    /// <summary>
    /// Maps architectural local Z (SourceEave face frame) into the schematic elevation
    /// frame whose origin is the drawn eave tip — the same origin used by
    /// <see cref="TryResolveSchematicReferenceLocalZMm"/> for SourceEave / ExplicitLocalPlane.
    /// </summary>
    private static List<AutomaticPurlinSectionMemberMm> MapMembersToSchematicElevationSpace(
        IReadOnlyList<AutomaticPurlinSectionMemberMm> members,
        double schematicOriginLocalZMm)
    {
        if (members.Count == 0 ||
            !double.IsFinite(schematicOriginLocalZMm) ||
            Math.Abs(schematicOriginLocalZMm) <= IntersectionToleranceMm)
        {
            return members as List<AutomaticPurlinSectionMemberMm> ?? members.ToList();
        }

        var result = new List<AutomaticPurlinSectionMemberMm>(members.Count);
        foreach (var member in members)
        {
            result.Add(member with { CenterZMm = member.CenterZMm + schematicOriginLocalZMm });
        }

        return result;
    }

    /// <summary>
    /// Outer top-corner X for side members; ridge uses both corners separately.
    /// Left = top-left, Right = top-right.
    /// </summary>
    internal static double SideContactCornerOffsetXMm(
        AutomaticPurlinSectionSide side,
        double widthMm) =>
        side switch
        {
            AutomaticPurlinSectionSide.Left => -widthMm / 2d,
            AutomaticPurlinSectionSide.Right => widthMm / 2d,
            _ => 0d,
        };

    private static bool TryResolveCornerContactTopZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionMemberMm member,
        double seatingFraction,
        out double topZMm,
        out double lowerAtContactZMm)
    {
        topZMm = 0d;
        lowerAtContactZMm = 0d;
        if (member.Side == AutomaticPurlinSectionSide.Center ||
            member.Role == RoofAutomaticPurlinGeneratorRole.Ridge)
        {
            var leftX = member.CenterXMm - member.WidthMm / 2d;
            var rightX = member.CenterXMm + member.WidthMm / 2d;
            if (!TryInterpolateRafterEdgeZMm(
                    rafters,
                    AutomaticPurlinSectionSide.Left,
                    leftX,
                    seatingFraction,
                    out var leftTop,
                    out var leftLower) ||
                !TryInterpolateRafterEdgeZMm(
                    rafters,
                    AutomaticPurlinSectionSide.Right,
                    rightX,
                    seatingFraction,
                    out var rightTop,
                    out var rightLower))
            {
                return false;
            }

            // Both top corners share one horizontal top edge.
            topZMm = (leftTop + rightTop) / 2d;
            lowerAtContactZMm = (leftLower + rightLower) / 2d;
            return true;
        }

        var contactX = member.CenterXMm +
            SideContactCornerOffsetXMm(member.Side, member.WidthMm);
        if (!TryInterpolateRafterEdgeZMm(
                rafters,
                member.Side,
                contactX,
                seatingFraction,
                out topZMm,
                out lowerAtContactZMm))
        {
            return false;
        }

        return true;
    }

    private static bool TryInterpolateRafterEdgeZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionSide side,
        double xMm,
        double seatingFraction,
        out double edgeZMm,
        out double lowerZMm)
    {
        // Authoritative seating contact: physical BuildRafters upper/lower edges
        // (same frame as tip-mapped / physical member elevations). SVG master edges are
        // only a fallback when corner edges cannot be resolved.
        edgeZMm = 0d;
        lowerZMm = 0d;
        var rafter = rafters.FirstOrDefault(candidate => candidate.Side == side);
        if (rafter is not null)
        {
            var lower = LowerEdgeZAtX(rafter, xMm);
            var upper = UpperEdgeZAtX(rafter, xMm);
            if (lower is not null && upper is not null)
            {
                lowerZMm = lower.Value;
                edgeZMm = lower.Value + seatingFraction * (upper.Value - lower.Value);
                return double.IsFinite(edgeZMm);
            }
        }

        return AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
            rafters,
            side,
            xMm,
            seatingFraction,
            out edgeZMm,
            out lowerZMm);
    }

    private static double? TryResolveRafterLowerZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionMemberMm member)
    {
        if (member.Side == AutomaticPurlinSectionSide.Center)
        {
            var left = rafters.FirstOrDefault(rafter => rafter.Side == AutomaticPurlinSectionSide.Left);
            var right = rafters.FirstOrDefault(rafter => rafter.Side == AutomaticPurlinSectionSide.Right);
            if (left is null || right is null)
            {
                return null;
            }

            var leftZ = LowerEdgeZAtX(left, member.CenterXMm);
            var rightZ = LowerEdgeZAtX(right, member.CenterXMm);
            return leftZ is null || rightZ is null ? null : (leftZ.Value + rightZ.Value) / 2d;
        }

        var sideRafter = rafters.FirstOrDefault(rafter => rafter.Side == member.Side);
        return sideRafter is null ? null : LowerEdgeZAtX(sideRafter, member.CenterXMm);
    }

    private static double? TryResolveRafterUpperZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm> rafters,
        AutomaticPurlinSectionMemberMm member)
    {
        if (member.Side == AutomaticPurlinSectionSide.Center)
        {
            var left = rafters.FirstOrDefault(rafter => rafter.Side == AutomaticPurlinSectionSide.Left);
            var right = rafters.FirstOrDefault(rafter => rafter.Side == AutomaticPurlinSectionSide.Right);
            if (left is null || right is null)
            {
                return null;
            }

            var leftZ = UpperEdgeZAtX(left, member.CenterXMm);
            var rightZ = UpperEdgeZAtX(right, member.CenterXMm);
            return leftZ is null || rightZ is null ? null : (leftZ.Value + rightZ.Value) / 2d;
        }

        var sideRafter = rafters.FirstOrDefault(rafter => rafter.Side == member.Side);
        return sideRafter is null ? null : UpperEdgeZAtX(sideRafter, member.CenterXMm);
    }

    private static double? LowerEdgeZAtX(AutomaticPurlinSectionRafterMm rafter, double xMm)
    {
        if (rafter.Corners.Count < 4)
        {
            return null;
        }

        // BuildRafters: [0]/[1] upper edge, [3]→[2] lower edge along the slope.
        return EdgeZAtX(rafter.Corners[3], rafter.Corners[2], xMm);
    }

    private static double? UpperEdgeZAtX(AutomaticPurlinSectionRafterMm rafter, double xMm)
    {
        if (rafter.Corners.Count < 4)
        {
            return null;
        }

        return EdgeZAtX(rafter.Corners[0], rafter.Corners[1], xMm);
    }

    private static double? EdgeZAtX(
        AutomaticPurlinSectionPointMm start,
        AutomaticPurlinSectionPointMm end,
        double xMm)
    {
        var dx = end.XMm - start.XMm;
        if (Math.Abs(dx) <= IntersectionToleranceMm)
        {
            return (start.ZMm + end.ZMm) / 2d;
        }

        var t = (xMm - start.XMm) / dx;
        return start.ZMm + t * (end.ZMm - start.ZMm);
    }

    private static List<AutomaticPurlinSectionWallMm> BuildWalls(
        IReadOnlyList<AutomaticPurlinSectionLineMm> slopes,
        IReadOnlyList<AutomaticPurlinSectionMemberMm> members,
        double rafterHeightMm)
    {
        var eaves = slopes.SelectMany(line => new[] { new SectionPoint(line.X1Mm, line.Z1Mm), new SectionPoint(line.X2Mm, line.Z2Mm) })
            .OrderBy(point => point.ZMm)
            .GroupBy(point => SideOf(point.XMm))
            .Select(group => group.OrderBy(point => point.ZMm).First())
            .Where(point => SideOf(point.XMm) != AutomaticPurlinSectionSide.Center)
            .OrderBy(point => point.XMm).ToArray();
        var result = new List<AutomaticPurlinSectionWallMm>(eaves.Length);
        foreach (var eave in eaves)
        {
            var side = SideOf(eave.XMm);
            var plate = members.Where(member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate && SideOf(member.CenterXMm) == side)
                .OrderBy(member => Math.Abs(member.CenterXMm - eave.XMm)).FirstOrDefault();
            var wallCenterX = plate?.CenterXMm ?? eave.XMm;
            var wallTopZ = plate is null
                ? eave.ZMm
                : plate.CenterZMm - plate.HeightMm / 2d;
            result.Add(new AutomaticPurlinSectionWallMm(
                side,
                wallCenterX,
                wallTopZ,
                wallTopZ - Math.Max(650d, rafterHeightMm * 4d),
                Math.Max(260d, (plate?.WidthMm ?? rafterHeightMm) * 1.75d)));
        }

        return result;
    }

    private static bool TryBuildSectionFrame(HipRoofGeometry geometry, out SectionFrame frame)
    {
        frame = default;
        var ridge = geometry.Ridges
            .Where(segment => Math.Abs(segment.Start.Z - segment.End.Z) <= RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
            .OrderByDescending(segment => segment.LengthMm)
            .ThenBy(segment => Math.Min(segment.Start.X, segment.End.X))
            .ThenBy(segment => Math.Min(segment.Start.Y, segment.End.Y)).FirstOrDefault()
            ?? geometry.Topology.Edges.Where(edge => edge.Kind == RoofTopologyEdgeKind.Eave)
                .Select(geometry.Topology.Segment).OrderByDescending(segment => segment.LengthMm).FirstOrDefault();
        if (ridge is null) return false;
        var dx = ridge.End.X - ridge.Start.X;
        var dy = ridge.End.Y - ridge.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= RoofAutomaticPurlinPlanner.CoordinateToleranceMm) return false;
        dx /= length;
        dy /= length;
        frame = new SectionFrame((ridge.Start.X + ridge.End.X) / 2d, (ridge.Start.Y + ridge.End.Y) / 2d, -dy, dx, dx, dy);
        return true;
    }

    private static bool TryIntersectSegmentWithSectionPlane(RoofSegment3D segment, SectionFrame frame, out RoofPoint3D intersection)
    {
        var points = new List<RoofPoint3D>(2);
        AddIntersections(segment.Start, segment.End, frame, points);
        if (points.Count == 0) { intersection = default; return false; }
        intersection = points.Count == 1 ? points[0] : new RoofPoint3D(
            (points[0].X + points[1].X) / 2d, (points[0].Y + points[1].Y) / 2d, (points[0].Z + points[1].Z) / 2d);
        return true;
    }

    private static void AddIntersections(RoofPoint3D start, RoofPoint3D end, SectionFrame frame, ICollection<RoofPoint3D> points)
    {
        var first = frame.SignedDistance(start);
        var second = frame.SignedDistance(end);
        var firstOnPlane = Math.Abs(first) <= IntersectionToleranceMm;
        var secondOnPlane = Math.Abs(second) <= IntersectionToleranceMm;
        if (firstOnPlane) points.Add(start);
        if (secondOnPlane) points.Add(end);
        if (firstOnPlane || secondOnPlane || Math.Sign(first) == Math.Sign(second)) return;
        var fraction = first / (first - second);
        if (fraction is < 0d or > 1d) return;
        points.Add(new RoofPoint3D(start.X + (end.X - start.X) * fraction, start.Y + (end.Y - start.Y) * fraction, start.Z + (end.Z - start.Z) * fraction));
    }

    private static List<RoofPoint3D> DeduplicatePoints(IEnumerable<RoofPoint3D> points, SectionFrame frame) => points
        .OrderBy(frame.ProjectX).ThenBy(point => point.Z).Aggregate(new List<RoofPoint3D>(), (result, point) =>
        {
            if (!result.Any(existing => Math.Abs(frame.ProjectX(existing) - frame.ProjectX(point)) <= IntersectionToleranceMm && Math.Abs(existing.Z - point.Z) <= IntersectionToleranceMm)) result.Add(point);
            return result;
        });

    private static List<AutomaticPurlinSectionLineMm> DeduplicateLines(IEnumerable<AutomaticPurlinSectionLineMm> lines) => lines
        .Aggregate(new List<AutomaticPurlinSectionLineMm>(), (result, line) =>
        {
            if (!result.Any(existing => LinesNearlyEqual(existing, line))) result.Add(line);
            return result;
        });

    private static bool LinesNearlyEqual(AutomaticPurlinSectionLineMm a, AutomaticPurlinSectionLineMm b) =>
        (Near(a.X1Mm, b.X1Mm) && Near(a.Z1Mm, b.Z1Mm) && Near(a.X2Mm, b.X2Mm) && Near(a.Z2Mm, b.Z2Mm)) ||
        (Near(a.X1Mm, b.X2Mm) && Near(a.Z1Mm, b.Z2Mm) && Near(a.X2Mm, b.X1Mm) && Near(a.Z2Mm, b.Z1Mm));

    private static AutomaticPurlinSectionSide SideOf(double xMm) =>
        xMm < -IntersectionToleranceMm
            ? AutomaticPurlinSectionSide.Left
            : xMm > IntersectionToleranceMm
                ? AutomaticPurlinSectionSide.Right
                : AutomaticPurlinSectionSide.Center;
    private static bool Near(double left, double right) => Math.Abs(left - right) <= 1d;

    private readonly record struct SectionFrame(double OriginX, double OriginY, double TransverseX, double TransverseY, double LongitudinalX, double LongitudinalY)
    {
        internal double ProjectX(RoofPoint3D point) => (point.X - OriginX) * TransverseX + (point.Y - OriginY) * TransverseY;
        internal double SignedDistance(RoofPoint3D point) => (point.X - OriginX) * LongitudinalX + (point.Y - OriginY) * LongitudinalY;
    }


    /// <summary>
    /// Builds the single active reference-plane annotation from an already-resolved
    /// datum, aligned to schematic geometry (not a second placement solver).
    /// SourceEave / ExplicitLocal → SVG plumb-cut top-outer rafter tip;
    /// WallPlateBottom → schematic wall-plate bottom edge.
    /// </summary>
    /// <summary>
    /// Strešná rovina local Z — schematic/SVG helper retained for drawing only.
    /// Technical inspector values must use
    /// <see cref="RoofAutomaticPurlinRoofPlaneRules"/> (physical), not this path.
    /// </summary>
    internal static bool TryResolveRoofPlaneLocalZMm(
        AutomaticPurlinSectionPresentation presentation,
        AutomaticPurlinSectionMemberMm member,
        out double roofPlaneLocalZMm)
    {
        roofPlaneLocalZMm = 0d;
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(member);
        if (member.Role == RoofAutomaticPurlinGeneratorRole.Ridge ||
            member.Side == AutomaticPurlinSectionSide.Center)
        {
            return AutomaticPurlinSectionSvgTemplate.TryResolveVisualRidgeRoofPlaneLocalZMm(
                presentation.Rafters,
                out roofPlaneLocalZMm);
        }

        if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
        {
            return false;
        }

        return AutomaticPurlinSectionSvgTemplate.TryResolveVisualRoofPlaneLocalZMm(
            presentation.Rafters,
            member.Side,
            member.CenterXMm,
            out roofPlaneLocalZMm);
    }

    internal static AutomaticPurlinSectionReferencePlane? TryCreateReferencePlane(
        RoofRelativeElevationDatum? datum,
        CultureInfo culture,
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters = null,
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members = null,
        AutomaticPurlinSectionPresentation? schematic = null)
    {
        if (datum is null ||
            !double.IsFinite(datum.ReferenceLocalZMm) ||
            !double.IsFinite(datum.ReferenceRelativeElevationMm))
        {
            return null;
        }

        if (!TryResolveSchematicReferenceLocalZMm(
                datum,
                rafters,
                members,
                schematic,
                out var localZMm))
        {
            return null;
        }

        culture ??= CultureInfo.InvariantCulture;
        var visualLocalZMm = ClampReferencePlaneVisualLocalZMm(
            localZMm,
            members,
            rafters,
            schematic);
        return new AutomaticPurlinSectionReferencePlane(
            datum.ReferenceKind,
            localZMm,
            FormatReferenceElevationLabel(datum.ReferenceRelativeElevationMm, culture),
            visualLocalZMm);
    }

    internal static string FormatReferenceElevationLabel(
        double relativeElevationMm,
        CultureInfo culture) =>
        RoofRelativeElevationDatumRules.FormatMetres(relativeElevationMm, culture);

    /// <summary>
    /// Visual zoom/draw clamp: keep the guide within
    /// [eave − 1200 mm, ridge-top + 1200 mm] so extreme LocalZ values do not
    /// shrink the schematic. True LocalZMm remains unclamped for labels/calc.
    /// </summary>
    internal const double ReferencePlaneVisualMaxAboveRidgeMm = 1200d;
    internal const double ReferencePlaneVisualMinBelowEaveMm = 1200d;

    internal static double ClampReferencePlaneVisualLocalZMm(
        double localZMm,
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members,
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        AutomaticPurlinSectionPresentation? schematic)
    {
        if (!double.IsFinite(localZMm))
        {
            return localZMm;
        }

        if (!TryResolveReferencePlaneVisualClampRange(
                members,
                rafters,
                schematic,
                out var minVisualZMm,
                out var maxVisualZMm))
        {
            return localZMm;
        }

        return Math.Clamp(localZMm, minVisualZMm, maxVisualZMm);
    }

    internal static bool TryResolveReferencePlaneVisualClampRange(
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members,
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        AutomaticPurlinSectionPresentation? schematic,
        out double minVisualZMm,
        out double maxVisualZMm)
    {
        minVisualZMm = 0d;
        maxVisualZMm = 0d;
        if (!TryResolveSchematicEaveLevelLocalZMm(rafters, schematic, out var eaveZMm) ||
            !TryResolveSchematicRidgeTopLocalZMm(members, rafters, out var ridgeTopZMm))
        {
            return false;
        }

        minVisualZMm = eaveZMm - ReferencePlaneVisualMinBelowEaveMm;
        maxVisualZMm = ridgeTopZMm + ReferencePlaneVisualMaxAboveRidgeMm;
        return minVisualZMm <= maxVisualZMm;
    }

    /// <summary>
    /// Maps the architectural datum onto the already-built section schematic.
    /// </summary>
    internal static bool TryResolveSchematicReferenceLocalZMm(
        RoofRelativeElevationDatum datum,
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members,
        AutomaticPurlinSectionPresentation? schematic,
        out double localZMm)
    {
        ArgumentNullException.ThrowIfNull(datum);
        localZMm = datum.ReferenceLocalZMm;
        switch (datum.ReferenceKind)
        {
            case RoofRelativeElevationReferenceKind.SourceEavePlane:
            case RoofRelativeElevationReferenceKind.ExplicitLocalPlane:
                // Architectural LocalZ=0 is the source eave. Map onto the drawn
                // plumb-cut top-outer rafter tip so Explicit@0 matches SourceEave@0.
                if (schematic is not null &&
                    AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                        schematic,
                        out var visualTopZ))
                {
                    localZMm = visualTopZ + datum.ReferenceLocalZMm;
                    return double.IsFinite(localZMm);
                }

                if (!TryResolveSourceEaveUpperTipLocalZMm(rafters, out var tipZ))
                {
                    return double.IsFinite(localZMm);
                }

                localZMm = tipZ + datum.ReferenceLocalZMm;
                return double.IsFinite(localZMm);

            case RoofRelativeElevationReferenceKind.WallPlateBottom:
                return TryResolveSchematicWallPlateBottomLocalZMm(members, out localZMm);

            default:
                return double.IsFinite(localZMm);
        }
    }

    /// <summary>
    /// Lower-Z endpoint of each rafter upper edge (Corners[0]→[1]) — eave tip fallback.
    /// </summary>
    internal static bool TryResolveSourceEaveUpperTipLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        out double localZMm)
    {
        localZMm = 0d;
        if (rafters is null || rafters.Count == 0)
        {
            return false;
        }

        var tips = new List<double>(rafters.Count);
        foreach (var rafter in rafters)
        {
            if (rafter.Corners.Count < 4)
            {
                continue;
            }

            var a = rafter.Corners[0];
            var b = rafter.Corners[1];
            tips.Add(a.ZMm <= b.ZMm ? a.ZMm : b.ZMm);
        }

        if (tips.Count == 0)
        {
            return false;
        }

        localZMm = tips.Average();
        return double.IsFinite(localZMm);
    }

    /// <summary>
    /// Bottom edge of schematic wall-plate rectangles (CenterZ − Height/2).
    /// </summary>
    internal static bool TryResolveSchematicWallPlateBottomLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members,
        out double localZMm)
    {
        localZMm = 0d;
        if (members is null || members.Count == 0)
        {
            return false;
        }

        var bottoms = members
            .Where(member =>
                member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                member.HeightMm > 0d &&
                double.IsFinite(member.CenterZMm) &&
                double.IsFinite(member.HeightMm))
            .Select(member => member.CenterZMm - member.HeightMm / 2d)
            .Where(double.IsFinite)
            .ToArray();
        if (bottoms.Length == 0)
        {
            return false;
        }

        localZMm = bottoms.Average();
        return true;
    }

    private static bool TryResolveSchematicRidgeTopLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionMemberMm>? members,
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        out double ridgeTopZMm)
    {
        ridgeTopZMm = 0d;
        if (members is not null)
        {
            var ridgeTops = members
                .Where(member =>
                    member.Role == RoofAutomaticPurlinGeneratorRole.Ridge &&
                    member.HeightMm > 0d &&
                    double.IsFinite(member.CenterZMm))
                .Select(member => member.MemberTopZMm)
                .Where(double.IsFinite)
                .ToArray();
            if (ridgeTops.Length > 0)
            {
                ridgeTopZMm = ridgeTops.Max();
                return true;
            }
        }

        if (rafters is null || rafters.Count == 0)
        {
            return false;
        }

        var tips = rafters
            .SelectMany(rafter => rafter.Corners)
            .Select(point => point.ZMm)
            .Where(double.IsFinite)
            .ToArray();
        if (tips.Length == 0)
        {
            return false;
        }

        ridgeTopZMm = tips.Max();
        return true;
    }

    private static bool TryResolveSchematicEaveLevelLocalZMm(
        IReadOnlyList<AutomaticPurlinSectionRafterMm>? rafters,
        AutomaticPurlinSectionPresentation? schematic,
        out double eaveZMm)
    {
        if (schematic is not null &&
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                schematic,
                out eaveZMm))
        {
            return true;
        }

        return TryResolveSourceEaveUpperTipLocalZMm(rafters, out eaveZMm);
    }

    private readonly record struct MemberCandidate(
        RoofAutomaticPurlinPlanItem Item,
        double CenterXMm,
        RoofAutomaticPurlinPlacementMode? PlacementMode);
    private readonly record struct SectionPoint(double XMm, double ZMm);
}


/// <summary>
/// Single active architectural reference plane for the roof-section schematic.
/// <see cref="LocalZMm"/> is the true resolved elevation; <see cref="VisualLocalZMm"/>
/// is clamped for schematic zoom/draw only.
/// </summary>
internal sealed record AutomaticPurlinSectionReferencePlane(
    RoofRelativeElevationReferenceKind Kind,
    double LocalZMm,
    string LabelText,
    double VisualLocalZMm)
{
    public AutomaticPurlinSectionReferencePlane(
        RoofRelativeElevationReferenceKind kind,
        double localZMm,
        string labelText)
        : this(kind, localZMm, labelText, localZMm)
    {
    }
}

internal sealed record AutomaticPurlinSectionLineMm(double X1Mm, double Z1Mm, double X2Mm, double Z2Mm);
internal enum AutomaticPurlinSectionSide { Left = -1, Center = 0, Right = 1 }
internal readonly record struct AutomaticPurlinSectionPointMm(double XMm, double ZMm);
internal sealed record AutomaticPurlinSectionRafterMm(
    AutomaticPurlinSectionSide Side,
    AutomaticPurlinSectionLineMm CenterLine,
    IReadOnlyList<AutomaticPurlinSectionPointMm> Corners);
internal sealed record AutomaticPurlinSectionWallMm(
    AutomaticPurlinSectionSide Side,
    double CenterXMm,
    double TopZMm,
    double BottomZMm,
    double WidthMm);
internal sealed record AutomaticPurlinSectionMemberMm(
    string StableKey,
    RoofAutomaticPurlinGeneratorRole Role,
    string Title,
    string DimensionText,
    string BottomText,
    string AxisText,
    string TopText,
    AutomaticPurlinSectionSide Side,
    double CenterXMm,
    double CenterZMm,
    double WidthMm,
    double HeightMm,
    double? RafterLowerSurfaceZMm,
    double? SeatingDepthMm,
    string? LayoutItemId = null,
    RoofAutomaticPurlinPlacementMode? PlacementMode = null)
{
    internal double MemberTopZMm => CenterZMm + HeightMm / 2d;

    /// <summary>
    /// Vertical overlap of this member into the rafter in the WPF section view.
    /// When seating is present this equals the authoritative Core <see cref="SeatingDepthMm"/>
    /// (schematic section seating), not the face-normal Z projection.
    /// </summary>
    internal double? VisibleRafterOverlapZMm =>
        SeatingDepthMm is { } depth
            ? depth
            : RafterLowerSurfaceZMm is { } lower
                ? MemberTopZMm - lower
                : null;
}
