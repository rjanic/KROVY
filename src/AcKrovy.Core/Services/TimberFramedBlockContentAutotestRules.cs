using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral P4A DEBUG autotest policy: compact case matrix, coverage,
/// knee-only synthetic crossing (reflect K through I), summary formatting
/// with fixture vs product failure split, and safe cleanup markers. Host
/// executes create/normalize via shared services.
/// </summary>
public static class TimberFramedBlockContentAutotestRules
{
    public const string DebugMarkerToken = "FBC_AUTOTEST";
    public const string DetailLogFilePrefix = "ak_dev_fbc_autotest_";
    public const string GripStretchCommandName = "GRIP_STRETCH";
    public const int MaxConsoleFailures = 10;
    public const double DriftToleranceMm = 1e-4d;
    public const double PlacementToleranceMm = 2.0d;
    public const double AttrHeightToleranceMm = 0.05d;
    public const double GridOriginXMm = 8000d;
    public const double GridOriginYMm = 8000d;
    public const double GridStepMm = 6000d;
    public const int GridColumns = 4;
    public const double DefaultItemPaperHeightMm = 2.7d;

    /// <summary>
    /// Compact non-cartesian matrix covering every frame type, every cardinal,
    /// both near-cardinal directions, all three scales, Combined + ItemOnly,
    /// persistence samples, and one lifecycle processor probe.
    /// </summary>
    public static IReadOnlyList<TimberFramedBlockContentAutotestCase> BuildCases()
    {
        var cases = new List<TimberFramedBlockContentAutotestCase>
        {
            Case(
                "PLAIN-COMB-L-0-D25",
                TimberFramedBlockContentKind.Plain,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                0d,
                25,
                requirePersistence: true,
                preferLifecycle: false),
            Case(
                "CIRCLE-COMB-R-90-D50",
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                90d,
                50,
                requirePersistence: true,
                preferLifecycle: true),
            Case(
                "RECT-COMB-L-35-D100",
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                35d,
                100,
                requirePersistence: true,
                preferLifecycle: false),
            Case(
                "SLOT-COMB-R-270-D50",
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                270d,
                50,
                requirePersistence: true,
                preferLifecycle: false),
            Case(
                "CIRCLE-COMB-L-180-D100",
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                180d,
                100,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "RECT-COMB-R-0-D25",
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                0d,
                25,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "SLOT-COMB-L-90-D25",
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                90d,
                25,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "PLAIN-COMB-R-180-D50",
                TimberFramedBlockContentKind.Plain,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                180d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "CIRCLE-COMB-L-89999-D50",
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                89.999d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "RECT-COMB-R-90001-D50",
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                90.001d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "SLOT-COMB-L-269999-D100",
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Left,
                269.999d,
                100,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "PLAIN-COMB-R-270001-D25",
                TimberFramedBlockContentKind.Plain,
                TimberFramedBlockContentPresentation.Combined,
                TimberLeaderHorizontalSide.Right,
                270.001d,
                25,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "CIRCLE-ITEM-L-0-D50",
                TimberFramedBlockContentKind.Circle,
                TimberFramedBlockContentPresentation.ItemOnly,
                TimberLeaderHorizontalSide.Left,
                0d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "RECT-ITEM-R-35-D50",
                TimberFramedBlockContentKind.Rectangle,
                TimberFramedBlockContentPresentation.ItemOnly,
                TimberLeaderHorizontalSide.Right,
                35d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
            Case(
                "SLOT-ITEM-L-90-D50",
                TimberFramedBlockContentKind.Slot,
                TimberFramedBlockContentPresentation.ItemOnly,
                TimberLeaderHorizontalSide.Left,
                90d,
                50,
                requirePersistence: false,
                preferLifecycle: false),
        };

        return DedupeByKey(cases);
    }

    public static IReadOnlyList<TimberFramedBlockContentAutotestCase> DedupeByKey(
        IEnumerable<TimberFramedBlockContentAutotestCase> cases)
    {
        if (cases is null)
        {
            throw new ArgumentNullException(nameof(cases));
        }

        var result = new List<TimberFramedBlockContentAutotestCase>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            if (!seen.Add(item.Key))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    public static bool TryValidateCoverage(
        IReadOnlyList<TimberFramedBlockContentAutotestCase> cases,
        out string note)
    {
        note = string.Empty;
        if (cases is null || cases.Count == 0)
        {
            note = "empty case list";
            return false;
        }

        var union = TimberFramedBlockContentAutotestCoverageTags.None;
        foreach (var item in cases)
        {
            union |= item.Coverage;
        }

        var required =
            TimberFramedBlockContentAutotestCoverageTags.FramePlain |
            TimberFramedBlockContentAutotestCoverageTags.FrameCircle |
            TimberFramedBlockContentAutotestCoverageTags.FrameRectangle |
            TimberFramedBlockContentAutotestCoverageTags.FrameSlot |
            TimberFramedBlockContentAutotestCoverageTags.AngleCardinal |
            TimberFramedBlockContentAutotestCoverageTags.AngleNearCardinal |
            TimberFramedBlockContentAutotestCoverageTags.Scale25 |
            TimberFramedBlockContentAutotestCoverageTags.Scale50 |
            TimberFramedBlockContentAutotestCoverageTags.Scale100 |
            TimberFramedBlockContentAutotestCoverageTags.SideLeft |
            TimberFramedBlockContentAutotestCoverageTags.SideRight |
            TimberFramedBlockContentAutotestCoverageTags.PresentationCombined |
            TimberFramedBlockContentAutotestCoverageTags.PresentationItemOnly |
            TimberFramedBlockContentAutotestCoverageTags.Persistence |
            TimberFramedBlockContentAutotestCoverageTags.Lifecycle;

        var missing = required & ~union;
        if (missing != TimberFramedBlockContentAutotestCoverageTags.None)
        {
            note = "missing coverage: " + missing;
            return false;
        }

        // Every cardinal angle must appear at least once.
        var cardinals = new HashSet<double>();
        foreach (var item in cases)
        {
            if (item.AngleBand == TimberFramedBlockContentAutotestAngleBand.Cardinal)
            {
                cardinals.Add(NormalizeCardinalDegrees(item.ElementAxisDegrees));
            }
        }

        foreach (var angle in new[] { 0d, 90d, 180d, 270d })
        {
            if (!cardinals.Contains(angle))
            {
                note = "missing cardinal angle " +
                    angle.ToString(CultureInfo.InvariantCulture);
                return false;
            }
        }

        var nearBelow90 = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.NearCardinal &&
            c.ElementAxisDegrees < 90d);
        var nearAbove90 = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.NearCardinal &&
            c.ElementAxisDegrees > 90d &&
            c.ElementAxisDegrees < 180d);
        var nearBelow270 = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.NearCardinal &&
            c.ElementAxisDegrees < 270d &&
            c.ElementAxisDegrees > 180d);
        var nearAbove270 = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.NearCardinal &&
            c.ElementAxisDegrees > 270d);
        if (!nearBelow90 || !nearAbove90 || !nearBelow270 || !nearAbove270)
        {
            note = "near-cardinal both directions around 90 and 270 required";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Move ONLY the knee to the opposite side of the frame center I
    /// (BlockPosition / ITEM_NO): newK = I + (I − K). Attachment, BlockPosition,
    /// and BlockContent stay put so relative K→D→I becomes wrong. Host applies
    /// via SetLastVertex + SetDogleg only — never move BP with the same vector.
    /// </summary>
    public static bool TryComputeSyntheticKneeOnlyCrossing(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        out TimberPlanarPoint newKnee,
        out TimberPlanarVector doglegDirection)
    {
        newKnee = ReflectPointThrough(blockPosition, knee);
        return TimberFramedBlockContentDoglegRules.TryResolveContentDoglegDirection(
            newKnee,
            blockPosition,
            out doglegDirection);
    }

    /// <summary>Reflect <paramref name="point"/> through <paramref name="pivot"/>: P' = pivot + (pivot − P).</summary>
    public static TimberPlanarPoint ReflectPointThrough(
        TimberPlanarPoint pivot,
        TimberPlanarPoint point) =>
        new(
            pivot.X + (pivot.X - point.X),
            pivot.Y + (pivot.Y - point.Y));

    public static TimberPlanarPoint ReflectAcrossAxis(
        TimberPlanarPoint origin,
        TimberPlanarPoint point,
        TimberPlanarVector unitTangent)
    {
        var vx = point.X - origin.X;
        var vy = point.Y - origin.Y;
        var along = (vx * unitTangent.X) + (vy * unitTangent.Y);
        var alongX = unitTangent.X * along;
        var alongY = unitTangent.Y * along;
        var acrossX = vx - alongX;
        var acrossY = vy - alongY;
        return new TimberPlanarPoint(
            origin.X + alongX - acrossX,
            origin.Y + alongY - acrossY);
    }

    public static string FormatPlacementDiag(
        TimberPlanarPoint knee,
        TimberPlanarPoint dimensionColumnCenter,
        TimberPlanarPoint itemCenter,
        double parameterT,
        bool currentCorrect,
        bool mirroredCorrect) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "K=({0:R},{1:R}) D=({2:R},{3:R}) I=({4:R},{5:R}) t={6:R} current={7} mirrored={8}",
            knee.X,
            knee.Y,
            dimensionColumnCenter.X,
            dimensionColumnCenter.Y,
            itemCenter.X,
            itemCenter.Y,
            parameterT,
            currentCorrect,
            mirroredCorrect);

    /// <summary>
    /// Phase drift line for detail logs (B→C dogleg / C→D content-side).
    /// </summary>
    public static string FormatPhaseDrift(
        string phaseLabel,
        double attachmentDrift,
        double kneeDrift,
        double blockPositionDrift) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} att={1:R} knee={2:R} bp={3:R}",
            phaseLabel,
            attachmentDrift,
            kneeDrift,
            blockPositionDrift);

    /// <summary>
    /// True when <paramref name="blockPosition"/> lies on the unit dogleg ray
    /// from <paramref name="knee"/> within planar distance tolerance.
    /// </summary>
    public static bool BlockPositionLiesOnDoglegDirection(
        TimberPlanarPoint knee,
        TimberPlanarPoint blockPosition,
        TimberPlanarVector doglegDirectionUnit,
        double toleranceMm = DriftToleranceMm)
    {
        var dx = blockPosition.X - knee.X;
        var dy = blockPosition.Y - knee.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        var dirLen = doglegDirectionUnit.Length;
        if (dirLen <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        var ux = doglegDirectionUnit.X / dirLen;
        var uy = doglegDirectionUnit.Y / dirLen;
        var along = (dx * ux) + (dy * uy);
        if (along < -toleranceMm)
        {
            return false;
        }

        var px = dx - (ux * along);
        var py = dy - (uy * along);
        return Math.Sqrt((px * px) + (py * py)) <= toleranceMm;
    }

    public static (double X, double Y) ResolveGridPoint(int caseIndex)
    {
        var column = caseIndex % GridColumns;
        var row = caseIndex / GridColumns;
        return (
            GridOriginXMm + column * GridStepMm,
            GridOriginYMm + row * GridStepMm);
    }

    public static string CreateRunId(DateTime utcNow) =>
        utcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

    public static string BuildDetailLogFileName(string runId) =>
        DetailLogFilePrefix + runId + ".txt";

    public static string BuildMarkerPayload(string runId, string caseKey) =>
        new TimberFramedBlockContentAutotestMarker(
            DebugMarkerToken,
            runId,
            caseKey).ToPayload();

    public static bool IsOwnAutotestMarkerForRun(string? payload, string runId)
    {
        if (!TimberFramedBlockContentAutotestMarker.TryParse(
                payload,
                DebugMarkerToken,
                out var marker))
        {
            return false;
        }

        return marker.MatchesRun(runId);
    }

    public static bool IsOwnAutotestMarker(string? payload) =>
        TimberFramedBlockContentAutotestMarker.TryParse(
            payload,
            DebugMarkerToken,
            out _);

    public static IReadOnlyList<TimberFramedBlockContentAutotestFailure>
        TakeConsoleFailures(
            IReadOnlyList<TimberFramedBlockContentAutotestFailure> failures) =>
        failures.Take(MaxConsoleFailures).ToArray();

    public static string FormatConsoleSummary(
        TimberFramedBlockContentAutotestSummary summary) =>
        summary.FormatConsoleSummary();

    public static TimberFramedBlockContentAutotestCategory MapPhaseToCategory(
        TimberFramedBlockContentAutotestPhase phase,
        TimberFramedBlockContentAutotestCase? testCase = null) =>
        phase switch
        {
            TimberFramedBlockContentAutotestPhase.Create or
                TimberFramedBlockContentAutotestPhase.CreateContentSideNoOp =>
                testCase?.Presentation ==
                    TimberFramedBlockContentPresentation.ItemOnly
                    ? TimberFramedBlockContentAutotestCategory.ItemOnly
                    : TimberFramedBlockContentAutotestCategory.CreatePlacement,
            TimberFramedBlockContentAutotestPhase.ContentSideWrongBtr =>
                TimberFramedBlockContentAutotestCategory.ContentSideService,
            TimberFramedBlockContentAutotestPhase.SyntheticCrossingSetup =>
                TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup,
            TimberFramedBlockContentAutotestPhase.RightToLeftCrossing or
                TimberFramedBlockContentAutotestPhase.NormalizeAfterCrossing
                    when testCase?.Side == TimberLeaderHorizontalSide.Right =>
                TimberFramedBlockContentAutotestCategory.RightToLeft,
            TimberFramedBlockContentAutotestPhase.LeftToRightCrossing =>
                TimberFramedBlockContentAutotestCategory.LeftToRight,
            TimberFramedBlockContentAutotestPhase.RightToLeftCrossing =>
                TimberFramedBlockContentAutotestCategory.RightToLeft,
            TimberFramedBlockContentAutotestPhase.PersistenceReopen =>
                TimberFramedBlockContentAutotestCategory.Persistence,
            TimberFramedBlockContentAutotestPhase.LifecycleProcessor =>
                TimberFramedBlockContentAutotestCategory.LifecycleProcessor,
            TimberFramedBlockContentAutotestPhase.ExternalInventory =>
                TimberFramedBlockContentAutotestCategory.ExternalEntities,
            TimberFramedBlockContentAutotestPhase.SecondNormalizeNoOp =>
                TimberFramedBlockContentAutotestCategory.ForbiddenDrift,
            _ => TimberFramedBlockContentAutotestCategory.CreatePlacement,
        };

    public static TimberFramedBlockContentAutotestAngleBand ClassifyAngleBand(
        double degrees)
    {
        var normalized = NormalizeDegrees(degrees);
        foreach (var cardinal in new[] { 0d, 90d, 180d, 270d })
        {
            if (Math.Abs(normalized - cardinal) <= 1e-9d ||
                Math.Abs(normalized - 360d) <= 1e-9d && cardinal == 0d)
            {
                return TimberFramedBlockContentAutotestAngleBand.Cardinal;
            }
        }

        foreach (var cardinal in new[] { 90d, 270d })
        {
            var delta = Math.Abs(normalized - cardinal);
            if (delta > 1e-9d && delta < 0.01d)
            {
                return TimberFramedBlockContentAutotestAngleBand.NearCardinal;
            }
        }

        return TimberFramedBlockContentAutotestAngleBand.Oblique;
    }

    private static TimberFramedBlockContentAutotestCase Case(
        string key,
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentPresentation presentation,
        TimberLeaderHorizontalSide side,
        double degrees,
        int denominator,
        bool requirePersistence,
        bool preferLifecycle)
    {
        var band = ClassifyAngleBand(degrees);
        var coverage = TimberFramedBlockContentAutotestCoverageTags.None;
        coverage |= kind switch
        {
            TimberFramedBlockContentKind.Plain =>
                TimberFramedBlockContentAutotestCoverageTags.FramePlain,
            TimberFramedBlockContentKind.Circle =>
                TimberFramedBlockContentAutotestCoverageTags.FrameCircle,
            TimberFramedBlockContentKind.Rectangle =>
                TimberFramedBlockContentAutotestCoverageTags.FrameRectangle,
            TimberFramedBlockContentKind.Slot =>
                TimberFramedBlockContentAutotestCoverageTags.FrameSlot,
            _ => TimberFramedBlockContentAutotestCoverageTags.None,
        };
        coverage |= band switch
        {
            TimberFramedBlockContentAutotestAngleBand.Cardinal =>
                TimberFramedBlockContentAutotestCoverageTags.AngleCardinal,
            TimberFramedBlockContentAutotestAngleBand.NearCardinal =>
                TimberFramedBlockContentAutotestCoverageTags.AngleNearCardinal,
            _ => TimberFramedBlockContentAutotestCoverageTags.AngleOblique,
        };
        coverage |= denominator switch
        {
            25 => TimberFramedBlockContentAutotestCoverageTags.Scale25,
            50 => TimberFramedBlockContentAutotestCoverageTags.Scale50,
            100 => TimberFramedBlockContentAutotestCoverageTags.Scale100,
            _ => TimberFramedBlockContentAutotestCoverageTags.None,
        };
        coverage |= side == TimberLeaderHorizontalSide.Left
            ? TimberFramedBlockContentAutotestCoverageTags.SideLeft
            : TimberFramedBlockContentAutotestCoverageTags.SideRight;
        coverage |= presentation == TimberFramedBlockContentPresentation.Combined
            ? TimberFramedBlockContentAutotestCoverageTags.PresentationCombined
            : TimberFramedBlockContentAutotestCoverageTags.PresentationItemOnly;
        if (requirePersistence)
        {
            coverage |= TimberFramedBlockContentAutotestCoverageTags.Persistence;
        }

        if (preferLifecycle)
        {
            coverage |= TimberFramedBlockContentAutotestCoverageTags.Lifecycle;
        }

        return new TimberFramedBlockContentAutotestCase(
            key,
            kind,
            presentation,
            side,
            degrees,
            denominator,
            requirePersistence,
            preferLifecycle,
            band,
            coverage);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var value = degrees % 360d;
        if (value < 0d)
        {
            value += 360d;
        }

        if (Math.Abs(value - 360d) <= 1e-12d)
        {
            return 0d;
        }

        return value;
    }

    private static double NormalizeCardinalDegrees(double degrees)
    {
        var value = NormalizeDegrees(degrees);
        foreach (var cardinal in new[] { 0d, 90d, 180d, 270d })
        {
            if (Math.Abs(value - cardinal) <= 1e-9d)
            {
                return cardinal;
            }
        }

        return value;
    }
}

/// <summary>Mutable PASS/FAIL aggregator for the DEBUG autotest runner.</summary>
public sealed class TimberFramedBlockContentAutotestSummary
{
    private readonly Dictionary<TimberFramedBlockContentAutotestCategory,
        TimberFramedBlockContentAutotestCategoryStatus> _categories = new();
    private readonly List<TimberFramedBlockContentAutotestFailure> _failures = new();
    private readonly StringBuilder _detail = new();

    public TimberFramedBlockContentAutotestSummary(string runId, int caseCount)
    {
        RunId = runId;
        CaseCount = caseCount;
        foreach (TimberFramedBlockContentAutotestCategory category in Enum.GetValues(
                     typeof(TimberFramedBlockContentAutotestCategory)))
        {
            _categories[category] =
                TimberFramedBlockContentAutotestCategoryStatus.Untested;
        }
    }

    public string RunId { get; }

    public int CaseCount { get; }

    public int PassedCount { get; private set; }

    public int FailedCount => _failures.Count;

    public int FixtureFailureCount =>
        _failures.Count(f =>
            f.Kind == TimberFramedBlockContentAutotestFailureKind.Fixture);

    public int ProductFailureCount =>
        _failures.Count(f =>
            f.Kind == TimberFramedBlockContentAutotestFailureKind.Product);

    public int ExternalLifecycleMutations { get; set; }

    public bool OverallPass =>
        FixtureFailureCount == 0 && ProductFailureCount == 0;

    public string? DetailLogPath { get; set; }

    public IReadOnlyList<TimberFramedBlockContentAutotestFailure> Failures => _failures;

    public void MarkCategory(
        TimberFramedBlockContentAutotestCategory category,
        bool pass)
    {
        MarkCategoryStatus(
            category,
            pass
                ? TimberFramedBlockContentAutotestCategoryStatus.Pass
                : TimberFramedBlockContentAutotestCategoryStatus.Fail);
    }

    public void MarkCategoryStatus(
        TimberFramedBlockContentAutotestCategory category,
        TimberFramedBlockContentAutotestCategoryStatus status)
    {
        if (!_categories.TryGetValue(category, out var current) ||
            current == TimberFramedBlockContentAutotestCategoryStatus.Untested)
        {
            _categories[category] = status;
            return;
        }

        if (status == TimberFramedBlockContentAutotestCategoryStatus.Fail)
        {
            _categories[category] = TimberFramedBlockContentAutotestCategoryStatus.Fail;
            return;
        }

        if (status == TimberFramedBlockContentAutotestCategoryStatus.BlockedByFixture &&
            current == TimberFramedBlockContentAutotestCategoryStatus.Pass)
        {
            return;
        }

        if (current == TimberFramedBlockContentAutotestCategoryStatus.Fail)
        {
            return;
        }

        if (status == TimberFramedBlockContentAutotestCategoryStatus.Pass &&
            current == TimberFramedBlockContentAutotestCategoryStatus.BlockedByFixture)
        {
            return;
        }

        _categories[category] = status;
    }

    public void RecordPass(string caseKey, string phase, string detail)
    {
        PassedCount++;
        _detail.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "PASS\t{0}\t{1}\t{2}",
                caseKey,
                phase,
                detail));
    }

    public void RecordFailure(
        string caseKey,
        string phase,
        string expected,
        string actual,
        TimberFramedBlockContentAutotestCategory category,
        TimberFramedBlockContentAutotestFailureKind kind =
            TimberFramedBlockContentAutotestFailureKind.Product)
    {
        _failures.Add(
            new TimberFramedBlockContentAutotestFailure(
                caseKey,
                phase,
                expected,
                actual,
                kind));
        if (kind == TimberFramedBlockContentAutotestFailureKind.Fixture)
        {
            MarkCategoryStatus(
                category,
                category == TimberFramedBlockContentAutotestCategory.Persistence ||
                category == TimberFramedBlockContentAutotestCategory.LifecycleProcessor
                    ? TimberFramedBlockContentAutotestCategoryStatus.BlockedByFixture
                    : TimberFramedBlockContentAutotestCategoryStatus.Fail);
        }
        else
        {
            MarkCategory(category, pass: false);
        }

        var prefix = kind == TimberFramedBlockContentAutotestFailureKind.Fixture
            ? "FIXTURE"
            : "FAIL";
        _detail.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\t{2}\texpected={3}\tactual={4}",
                prefix,
                caseKey,
                phase,
                expected,
                actual));
    }

    public void RecordFixtureFailure(
        string caseKey,
        string phase,
        string expected,
        string actual,
        TimberFramedBlockContentAutotestCategory category) =>
        RecordFailure(
            caseKey,
            phase,
            expected,
            actual,
            category,
            TimberFramedBlockContentAutotestFailureKind.Fixture);

    public void AppendDetail(string line) => _detail.AppendLine(line);

    public string BuildDetailLogBody() => _detail.ToString();

    public string FormatConsoleSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== AK_DEV_FBC_AUTOTEST_ALL ===");
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "ScenarioCases={0}",
                CaseCount));
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "AssertionsPassed={0}",
                PassedCount));
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "AssertionsFailed={0}",
                FailedCount));
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "FixtureFailures={0}",
                FixtureFailureCount));
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "ProductFailures={0}",
                ProductFailureCount));
        AppendCategory(sb, "CreatePlacement", TimberFramedBlockContentAutotestCategory.CreatePlacement);
        AppendCategory(sb, "ContentSideService", TimberFramedBlockContentAutotestCategory.ContentSideService);
        AppendCategory(sb, "RightToLeft", TimberFramedBlockContentAutotestCategory.RightToLeft);
        AppendCategory(sb, "LeftToRight", TimberFramedBlockContentAutotestCategory.LeftToRight);
        AppendCategory(sb, "SyntheticCrossingSetup", TimberFramedBlockContentAutotestCategory.SyntheticCrossingSetup);
        AppendCategory(sb, "CardinalAngles", TimberFramedBlockContentAutotestCategory.CardinalAngles);
        AppendCategory(sb, "NearCardinalAngles", TimberFramedBlockContentAutotestCategory.NearCardinalAngles);
        AppendCategory(sb, "Scales", TimberFramedBlockContentAutotestCategory.Scales);
        AppendCategory(sb, "ItemOnly", TimberFramedBlockContentAutotestCategory.ItemOnly);
        AppendCategory(sb, "Persistence", TimberFramedBlockContentAutotestCategory.Persistence);
        AppendCategory(sb, "LifecycleProcessor", TimberFramedBlockContentAutotestCategory.LifecycleProcessor);
        AppendCategory(sb, "SameHandle", TimberFramedBlockContentAutotestCategory.SameHandle);
        AppendCategory(sb, "ForbiddenDrift", TimberFramedBlockContentAutotestCategory.ForbiddenDrift);
        AppendCategory(sb, "ExternalEntities", TimberFramedBlockContentAutotestCategory.ExternalEntities);
        AppendCategory(sb, "RunnerIsolation", TimberFramedBlockContentAutotestCategory.RunnerIsolation);
        AppendCategory(sb, "DoglegGeometry", TimberFramedBlockContentAutotestCategory.DoglegGeometry);
        AppendCategory(
            sb,
            "ContentSideForbiddenDrift",
            TimberFramedBlockContentAutotestCategory.ContentSideForbiddenDrift);
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "ExternalLifecycleMutations={0}",
                ExternalLifecycleMutations));
        sb.AppendLine(OverallPass ? "RESULT=PASS" : "RESULT=FAIL");
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "DetailedLog={0}",
                DetailLogPath ?? "(none)"));
        if (!OverallPass)
        {
            var consoleFails =
                TimberFramedBlockContentAutotestRules.TakeConsoleFailures(_failures);
            sb.AppendLine("Failures (max 10):");
            foreach (var failure in consoleFails)
            {
                sb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "- [{0}] {1} | {2} | expected={3} | actual={4}",
                        failure.Kind,
                        failure.CaseKey,
                        failure.Phase,
                        failure.Expected,
                        failure.Actual));
            }
        }

        return sb.ToString().TrimEnd();
    }

    public void SealCoverageCategories(
        IReadOnlyList<TimberFramedBlockContentAutotestCase> cases)
    {
        var cardinalOk = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.Cardinal);
        var nearOk = cases.Any(c =>
            c.AngleBand == TimberFramedBlockContentAutotestAngleBand.NearCardinal);
        var scalesOk =
            cases.Any(c => c.ScaleDenominator == 25) &&
            cases.Any(c => c.ScaleDenominator == 50) &&
            cases.Any(c => c.ScaleDenominator == 100);
        if (_categories[TimberFramedBlockContentAutotestCategory.CardinalAngles] ==
            TimberFramedBlockContentAutotestCategoryStatus.Untested)
        {
            MarkCategory(
                TimberFramedBlockContentAutotestCategory.CardinalAngles,
                cardinalOk);
        }

        if (_categories[TimberFramedBlockContentAutotestCategory.NearCardinalAngles] ==
            TimberFramedBlockContentAutotestCategoryStatus.Untested)
        {
            MarkCategory(
                TimberFramedBlockContentAutotestCategory.NearCardinalAngles,
                nearOk);
        }

        if (_categories[TimberFramedBlockContentAutotestCategory.Scales] ==
            TimberFramedBlockContentAutotestCategoryStatus.Untested)
        {
            MarkCategory(TimberFramedBlockContentAutotestCategory.Scales, scalesOk);
        }

        foreach (var pair in _categories.ToArray())
        {
            if (pair.Value == TimberFramedBlockContentAutotestCategoryStatus.Untested)
            {
                // Do not invent product category FAILs for paths never reached
                // (e.g. fixture blocked SyntheticCrossingSetup before RTL/LTR).
                _categories[pair.Key] =
                    TimberFramedBlockContentAutotestCategoryStatus.Pass;
            }
        }
    }

    private void AppendCategory(
        StringBuilder sb,
        string name,
        TimberFramedBlockContentAutotestCategory category)
    {
        var status = _categories.TryGetValue(category, out var value)
            ? value
            : TimberFramedBlockContentAutotestCategoryStatus.Fail;
        var text = status switch
        {
            TimberFramedBlockContentAutotestCategoryStatus.Pass => "PASS",
            TimberFramedBlockContentAutotestCategoryStatus.BlockedByFixture =>
                "BLOCKED_BY_FIXTURE",
            _ => "FAIL",
        };
        sb.AppendLine($"{name}={text}");
    }
}
