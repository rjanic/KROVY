namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Deterministic collision-aware annotation-lane assignment for roof-section labels.
/// Pure doubles / structs — no WPF dependency.
/// </summary>
internal static class AutomaticPurlinSectionLabelLanes
{
    internal readonly record struct LabelRect(
        double X,
        double Y,
        double Width,
        double Height)
    {
        internal double Left => X;
        internal double Top => Y;
        internal double Right => X + Width;
        internal double Bottom => Y + Height;

        internal bool Intersects(LabelRect other, double padding)
        {
            return Left < other.Right + padding &&
                   Right + padding > other.Left &&
                   Top < other.Bottom + padding &&
                   Bottom + padding > other.Top;
        }
    }

    internal readonly record struct LabelRequest(
        string Id,
        double AnchorX,
        double PreferredTopY,
        double Width,
        double Height,
        bool PreferBelow = false);

    internal readonly record struct LabelPlacement(
        string Id,
        int LaneIndex,
        double X,
        double Y,
        double Width,
        double Height,
        bool PreferBelow = false)
    {
        internal LabelRect Bounds => new(X, Y, Width, Height);
    }

    /// <summary>
    /// Preferred top of a ridge label: two annotation-box heights below the ridge
    /// contact anchor (WPF Y grows downward).
    /// </summary>
    internal const double RidgeLabelBelowBoxMultiples = 2d;

    internal static LabelRequest CreateCompactRequest(
        string id,
        double anchorX,
        double anchorY,
        int side,
        double viewportWidth,
        double viewportHeight,
        double width,
        double height,
        double margin,
        double verticalOffset,
        bool preferBelow = false)
    {
        var preferredCenterX = anchorX + Math.Sign(side) * width * 0.38d;
        var safeCenterX = viewportWidth >= width + 2d * margin
            ? Math.Clamp(preferredCenterX, margin + width / 2d, viewportWidth - margin - width / 2d)
            : viewportWidth / 2d;
        var preferredTop = preferBelow
            ? Math.Clamp(
                anchorY + RidgeLabelBelowBoxMultiples * height,
                margin,
                Math.Max(margin, viewportHeight - margin - height))
            : Math.Clamp(
                anchorY - height - verticalOffset,
                margin,
                Math.Max(margin, viewportHeight - margin - height));
        return new LabelRequest(id, safeCenterX, preferredTop, width, height, preferBelow);
    }

    internal static double PreferredLeaderDistance(
        LabelRequest request,
        double anchorX,
        double anchorY)
    {
        var dx = request.AnchorX - anchorX;
        var dy = request.PreferredTopY + request.Height - anchorY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Assigns non-overlapping label rectangles. Lane 0 uses PreferredTopY; each later
    /// lane shifts upward by <paramref name="laneStep"/> (screen Y decreases when
    /// <paramref name="lanesGrowNegativeY"/> is true, matching WPF top-down Y).
    /// </summary>
    internal static IReadOnlyList<LabelPlacement> Assign(
        IReadOnlyList<LabelRequest> requests,
        double laneStep,
        double padding = 2d,
        bool lanesGrowNegativeY = true)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (!double.IsFinite(laneStep) || laneStep <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(laneStep));
        }

        if (!double.IsFinite(padding) || padding < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(padding));
        }

        var ordered = requests
            .Select((request, index) => (request, index))
            .OrderBy(entry => entry.request.PreferredTopY)
            .ThenBy(entry => entry.request.AnchorX)
            .ThenBy(entry => entry.request.Id, StringComparer.Ordinal)
            .ThenBy(entry => entry.index)
            .ToArray();

        var placed = new List<LabelPlacement>(ordered.Length);
        foreach (var (request, _) in ordered)
        {
            var width = Math.Max(1d, request.Width);
            var height = Math.Max(1d, request.Height);
            var x = request.AnchorX - width / 2d;
            var lane = 0;
            // Labels above their member grow upward (negative Y); prefer-below labels
            // grow farther downward so collision stacks stay under the ridge.
            var growDown = request.PreferBelow || !lanesGrowNegativeY;
            while (true)
            {
                var y = growDown
                    ? request.PreferredTopY + lane * laneStep
                    : request.PreferredTopY - lane * laneStep;
                var candidate = new LabelRect(x, y, width, height);
                if (!placed.Any(existing => existing.Bounds.Intersects(candidate, padding)))
                {
                    placed.Add(new LabelPlacement(
                        request.Id,
                        lane,
                        candidate.X,
                        candidate.Y,
                        candidate.Width,
                        candidate.Height,
                        request.PreferBelow));
                    break;
                }

                lane++;
                if (lane > 256)
                {
                    placed.Add(new LabelPlacement(
                        request.Id,
                        lane,
                        candidate.X,
                        candidate.Y,
                        candidate.Width,
                        candidate.Height,
                        request.PreferBelow));
                    break;
                }
            }
        }

        return placed
            .OrderBy(placement => placement.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
