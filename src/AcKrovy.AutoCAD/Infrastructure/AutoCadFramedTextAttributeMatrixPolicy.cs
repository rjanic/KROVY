#if DEBUG
using System.Globalization;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadFramedTextAttributeMatrixVariantKind
{
    PreDatabaseCurrent,
    AppendBeforeSet,
    GetModifySetAfterAppend,
    SecondWriteTransaction,
    BlockScaleAfterSet,
}

internal enum AutoCadFramedTextAttributeMatrixBlockScaleOrder
{
    BeforeSetBlockAttribute,
    AfterSetBlockAttribute,
}

internal enum AutoCadFramedTextAttributeMatrixCheckStatus
{
    Pass,
    Fail,
    NotTested,
}

internal enum AutoCadFramedTextAttributeMatrixOutcome
{
    HostSupportedCandidate,
    MixedResults,
    PerInstanceHeightAndStyleNotSupported,
    Inconclusive,
}

internal enum AutoCadFramedTextAttributeMatrixCapabilityStatus
{
    Supported,
    NotSupportedByTestedPaths,
    NotTested,
    Inconclusive,
}

internal sealed record AutoCadFramedTextAttributeMatrixCase(
    string Name,
    string Token,
    AutoCadFramedTextAttributeMatrixVariantKind Kind,
    AutoCadFramedTextAttributeMatrixBlockScaleOrder BlockScaleOrder,
    double BlockPositionX)
{
    public double ExpectedBaseHeight =>
        AutoCadFramedTextAttributeProofPolicy.Cases[0].BaseAttributeHeight;

    public double ExpectedBlockScale =>
        Kind is AutoCadFramedTextAttributeMatrixVariantKind.AppendBeforeSet or
            AutoCadFramedTextAttributeMatrixVariantKind.BlockScaleAfterSet
                ? AutoCadFramedTextAttributeProofPolicy.Cases[2].BlockScale
                : AutoCadFramedTextAttributeProofPolicy.Cases[0].BlockScale;

    public double ExpectedEffectiveHeight =>
        ExpectedBaseHeight * ExpectedBlockScale;
}

internal readonly record struct AutoCadFramedTextAttributeHeightObservation(
    double RawAttributeHeight,
    double BlockScale)
{
    public bool HasValidBlockScale =>
        double.IsFinite(BlockScale) && BlockScale > 0d;

    public double? NormalizedBaseHeight =>
        HasValidBlockScale && double.IsFinite(RawAttributeHeight)
            ? RawAttributeHeight / BlockScale
            : null;

    public double? ActualEffectiveHeight =>
        HasValidBlockScale && double.IsFinite(RawAttributeHeight)
            ? RawAttributeHeight
            : null;
}

internal sealed record AutoCadFramedTextAttributeMatrixObservation(
    string Token,
    double RawAttributeHeight,
    double BlockScale,
    string TextStyleHandle,
    string TextStyleName)
{
    private AutoCadFramedTextAttributeHeightObservation HeightObservation =>
        new(RawAttributeHeight, BlockScale);

    public bool HasValidBlockScale =>
        HeightObservation.HasValidBlockScale;

    public double? NormalizedBaseHeight =>
        HeightObservation.NormalizedBaseHeight;

    public double? ActualEffectiveHeight =>
        HeightObservation.ActualEffectiveHeight;
}

internal sealed record AutoCadFramedTextAttributeMatrixPhaseResult(
    AutoCadFramedTextAttributeMatrixCheckStatus TokenStatus,
    AutoCadFramedTextAttributeMatrixCheckStatus BaseHeightStatus,
    AutoCadFramedTextAttributeMatrixCheckStatus EffectiveHeightStatus,
    AutoCadFramedTextAttributeMatrixCheckStatus StyleStatus,
    AutoCadFramedTextAttributeMatrixCheckStatus BlockScaleStatus)
{
    public AutoCadFramedTextAttributeMatrixCheckStatus OverallStatus =>
        TokenStatus == AutoCadFramedTextAttributeMatrixCheckStatus.Fail ||
        BaseHeightStatus == AutoCadFramedTextAttributeMatrixCheckStatus.Fail ||
        EffectiveHeightStatus == AutoCadFramedTextAttributeMatrixCheckStatus.Fail ||
        StyleStatus == AutoCadFramedTextAttributeMatrixCheckStatus.Fail ||
        BlockScaleStatus == AutoCadFramedTextAttributeMatrixCheckStatus.Fail
            ? AutoCadFramedTextAttributeMatrixCheckStatus.Fail
            : StyleStatus == AutoCadFramedTextAttributeMatrixCheckStatus.NotTested
                ? AutoCadFramedTextAttributeMatrixCheckStatus.NotTested
                : AutoCadFramedTextAttributeMatrixCheckStatus.Pass;
}

internal sealed record AutoCadFramedTextAttributeMatrixVariantResult(
    AutoCadFramedTextAttributeMatrixCase Variant,
    AutoCadFramedTextAttributeMatrixPhaseResult? PreCommit,
    AutoCadFramedTextAttributeMatrixPhaseResult? PostCommit)
{
    public bool IsHostSupportedCandidate =>
        PostCommit is
        {
            BaseHeightStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            EffectiveHeightStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            StyleStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
            BlockScaleStatus: AutoCadFramedTextAttributeMatrixCheckStatus.Pass,
        };
}

internal sealed record AutoCadFramedTextAttributeMatrixCapabilitySummary(
    AutoCadFramedTextAttributeMatrixCapabilityStatus Token,
    AutoCadFramedTextAttributeMatrixCapabilityStatus BaseHeight,
    AutoCadFramedTextAttributeMatrixCapabilityStatus TextStyle,
    AutoCadFramedTextAttributeMatrixCapabilityStatus BlockScale);

internal sealed record AutoCadFramedTextAttributeDefinitionAuditSnapshot(
    string DiagnosticObjectId,
    string Handle,
    string OwnerBlockHandle,
    string Tag,
    string Prompt,
    string TextString,
    string TextStyleHandle,
    string TextStyleName,
    double Height,
    double PositionX,
    double PositionY,
    double PositionZ,
    double AlignmentX,
    double AlignmentY,
    double AlignmentZ,
    double Rotation,
    double WidthFactor,
    double Oblique,
    int HorizontalMode,
    int VerticalMode,
    bool Invisible,
    bool Constant,
    bool Preset,
    bool Verifiable,
    bool LockPositionInBlock,
    bool IsErased,
    string Layer,
    int ColorMethod,
    string LinetypeHandle,
    int LineWeight);

internal sealed record AutoCadFramedTextAttributeDefinitionFieldComparison(
    string FieldName,
    string Before,
    string After,
    bool IsIntegrityRelevant,
    bool HasChanged);

internal sealed record AutoCadFramedTextAttributeDefinitionAuditResult(
    IReadOnlyList<AutoCadFramedTextAttributeDefinitionFieldComparison> Fields)
{
    public bool IntegrityPreserved =>
        Fields.All(entry => !entry.IsIntegrityRelevant || !entry.HasChanged);

    public IReadOnlyList<string> ChangedIntegrityFields =>
        Fields
            .Where(entry => entry.IsIntegrityRelevant && entry.HasChanged)
            .Select(entry => entry.FieldName)
            .ToArray();
}

internal static class AutoCadFramedTextAttributeMatrixPolicy
{
    public const int MarkerSchemaVersion = 1;

    public static IReadOnlyList<AutoCadFramedTextAttributeMatrixCase> Variants
        { get; } = Array.AsReadOnly<AutoCadFramedTextAttributeMatrixCase>(
        [
            new(
                "VARIANT 1 - PreDatabaseCurrent",
                "AK23_MATRIX_V1",
                AutoCadFramedTextAttributeMatrixVariantKind.PreDatabaseCurrent,
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .BeforeSetBlockAttribute,
                0d),
            new(
                "VARIANT 2 - AppendBeforeSet",
                "AK23_MATRIX_V2",
                AutoCadFramedTextAttributeMatrixVariantKind.AppendBeforeSet,
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .BeforeSetBlockAttribute,
                800d),
            new(
                "VARIANT 3 - GetModifySetAfterAppend",
                "AK23_MATRIX_V3",
                AutoCadFramedTextAttributeMatrixVariantKind.GetModifySetAfterAppend,
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .BeforeSetBlockAttribute,
                1600d),
            new(
                "VARIANT 4 - SecondWriteTransaction",
                "AK23_MATRIX_V4",
                AutoCadFramedTextAttributeMatrixVariantKind.SecondWriteTransaction,
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .BeforeSetBlockAttribute,
                2400d),
            new(
                "VARIANT 5 - BlockScaleAfterSet",
                "AK23_MATRIX_V5",
                AutoCadFramedTextAttributeMatrixVariantKind.BlockScaleAfterSet,
                AutoCadFramedTextAttributeMatrixBlockScaleOrder
                    .AfterSetBlockAttribute,
                3200d),
        ]);

    public static AutoCadFramedTextAttributeMatrixPhaseResult Evaluate(
        AutoCadFramedTextAttributeMatrixCase variant,
        AutoCadFramedTextAttributeMatrixObservation observation,
        string expectedStyleHandle,
        string definitionStyleHandle,
        bool distinctStyleOverrideExpected)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedStyleHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionStyleHandle);

        var tokenStatus = string.Equals(
                variant.Token,
                observation.Token,
                StringComparison.Ordinal)
            ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
            : AutoCadFramedTextAttributeMatrixCheckStatus.Fail;
        var blockScaleStatus = observation.HasValidBlockScale &&
            AutoCadFramedTextAttributeProofPolicy.AreClose(
                variant.ExpectedBlockScale,
                observation.BlockScale)
            ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
            : AutoCadFramedTextAttributeMatrixCheckStatus.Fail;
        var baseHeightStatus = observation.NormalizedBaseHeight is double baseHeight &&
            AutoCadFramedTextAttributeProofPolicy.AreClose(
                variant.ExpectedBaseHeight,
                baseHeight)
            ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
            : AutoCadFramedTextAttributeMatrixCheckStatus.Fail;
        var effectiveHeightStatus =
            observation.ActualEffectiveHeight is double effectiveHeight &&
            AutoCadFramedTextAttributeProofPolicy.AreClose(
                variant.ExpectedEffectiveHeight,
                effectiveHeight)
            ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
            : AutoCadFramedTextAttributeMatrixCheckStatus.Fail;
        var styleCanBeTested = distinctStyleOverrideExpected &&
            !string.Equals(
                expectedStyleHandle,
                definitionStyleHandle,
                StringComparison.OrdinalIgnoreCase);
        var styleStatus = !styleCanBeTested
            ? AutoCadFramedTextAttributeMatrixCheckStatus.NotTested
            : string.Equals(
                expectedStyleHandle,
                observation.TextStyleHandle,
                StringComparison.OrdinalIgnoreCase)
                ? AutoCadFramedTextAttributeMatrixCheckStatus.Pass
                : AutoCadFramedTextAttributeMatrixCheckStatus.Fail;

        return new AutoCadFramedTextAttributeMatrixPhaseResult(
            tokenStatus,
            baseHeightStatus,
            effectiveHeightStatus,
            styleStatus,
            blockScaleStatus);
    }

    public static AutoCadFramedTextAttributeMatrixOutcome DetermineOutcome(
        IReadOnlyCollection<AutoCadFramedTextAttributeMatrixVariantResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Any(result => result.IsHostSupportedCandidate))
        {
            return AutoCadFramedTextAttributeMatrixOutcome.HostSupportedCandidate;
        }
        if (results.Count == 0 || results.Any(result => result.PostCommit is null))
        {
            return AutoCadFramedTextAttributeMatrixOutcome.Inconclusive;
        }
        if (results.Any(result =>
                result.PostCommit!.StyleStatus ==
                    AutoCadFramedTextAttributeMatrixCheckStatus.NotTested))
        {
            return AutoCadFramedTextAttributeMatrixOutcome.Inconclusive;
        }
        if (results.All(result =>
                result.PostCommit!.TokenStatus ==
                    AutoCadFramedTextAttributeMatrixCheckStatus.Pass &&
                result.PostCommit.BaseHeightStatus ==
                    AutoCadFramedTextAttributeMatrixCheckStatus.Fail &&
                result.PostCommit.EffectiveHeightStatus ==
                    AutoCadFramedTextAttributeMatrixCheckStatus.Fail &&
                result.PostCommit.StyleStatus ==
                    AutoCadFramedTextAttributeMatrixCheckStatus.Fail))
        {
            return AutoCadFramedTextAttributeMatrixOutcome
                .PerInstanceHeightAndStyleNotSupported;
        }

        return AutoCadFramedTextAttributeMatrixOutcome.MixedResults;
    }

    public static AutoCadFramedTextAttributeMatrixCapabilitySummary
        SummarizeCapabilities(
            IReadOnlyCollection<AutoCadFramedTextAttributeMatrixVariantResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new AutoCadFramedTextAttributeMatrixCapabilitySummary(
            Summarize(
                results.Select(result => result.PostCommit?.TokenStatus)),
            Summarize(
                results.Select(result => result.PostCommit?.BaseHeightStatus)),
            Summarize(
                results.Select(result => result.PostCommit?.StyleStatus)),
            Summarize(
                results.Select(result => result.PostCommit?.BlockScaleStatus)));
    }

    public static AutoCadFramedTextAttributeDefinitionAuditResult
        CompareDefinitionSnapshots(
            AutoCadFramedTextAttributeDefinitionAuditSnapshot before,
            AutoCadFramedTextAttributeDefinitionAuditSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var fields = new List<
            AutoCadFramedTextAttributeDefinitionFieldComparison>();

        AddString(
            fields,
            "ObjectId",
            before.DiagnosticObjectId,
            after.DiagnosticObjectId,
            integrityRelevant: false,
            StringComparison.Ordinal);
        AddString(fields, "Handle", before.Handle, after.Handle, true,
            StringComparison.OrdinalIgnoreCase);
        AddString(
            fields,
            "OwnerBlockHandle",
            before.OwnerBlockHandle,
            after.OwnerBlockHandle,
            true,
            StringComparison.OrdinalIgnoreCase);
        AddString(fields, "Tag", before.Tag, after.Tag, true,
            StringComparison.Ordinal);
        AddString(fields, "Prompt", before.Prompt, after.Prompt, true,
            StringComparison.Ordinal);
        AddString(fields, "TextString", before.TextString, after.TextString, true,
            StringComparison.Ordinal);
        AddString(
            fields,
            "TextStyleId",
            before.TextStyleHandle,
            after.TextStyleHandle,
            true,
            StringComparison.OrdinalIgnoreCase);
        AddString(
            fields,
            "TextStyleName",
            before.TextStyleName,
            after.TextStyleName,
            true,
            StringComparison.Ordinal);
        AddDouble(fields, "Height", before.Height, after.Height);
        AddDouble(fields, "Position.X", before.PositionX, after.PositionX);
        AddDouble(fields, "Position.Y", before.PositionY, after.PositionY);
        AddDouble(fields, "Position.Z", before.PositionZ, after.PositionZ);
        AddDouble(fields, "AlignmentPoint.X", before.AlignmentX, after.AlignmentX);
        AddDouble(fields, "AlignmentPoint.Y", before.AlignmentY, after.AlignmentY);
        AddDouble(fields, "AlignmentPoint.Z", before.AlignmentZ, after.AlignmentZ);
        AddDouble(fields, "Rotation", before.Rotation, after.Rotation);
        AddDouble(fields, "WidthFactor", before.WidthFactor, after.WidthFactor);
        AddDouble(fields, "Oblique", before.Oblique, after.Oblique);
        AddValue(fields, "HorizontalMode", before.HorizontalMode, after.HorizontalMode);
        AddValue(fields, "VerticalMode", before.VerticalMode, after.VerticalMode);
        AddValue(fields, "Invisible", before.Invisible, after.Invisible);
        AddValue(fields, "Constant", before.Constant, after.Constant);
        AddValue(fields, "Preset", before.Preset, after.Preset);
        AddValue(fields, "Verifiable", before.Verifiable, after.Verifiable);
        AddValue(
            fields,
            "LockPositionInBlock",
            before.LockPositionInBlock,
            after.LockPositionInBlock);
        AddValue(fields, "IsErased", before.IsErased, after.IsErased);
        AddString(fields, "Layer", before.Layer, after.Layer, true,
            StringComparison.Ordinal);
        AddValue(fields, "ColorMethod", before.ColorMethod, after.ColorMethod);
        AddString(
            fields,
            "LinetypeId",
            before.LinetypeHandle,
            after.LinetypeHandle,
            true,
            StringComparison.OrdinalIgnoreCase);
        AddValue(fields, "LineWeight", before.LineWeight, after.LineWeight);

        return new AutoCadFramedTextAttributeDefinitionAuditResult(
            fields.AsReadOnly());
    }

    private static AutoCadFramedTextAttributeMatrixCapabilityStatus Summarize(
        IEnumerable<AutoCadFramedTextAttributeMatrixCheckStatus?> statuses)
    {
        var captured = statuses.ToArray();
        var tested = captured.Where(status => status.HasValue).Select(status =>
            status!.Value).ToArray();
        if (tested.Any(status =>
                status == AutoCadFramedTextAttributeMatrixCheckStatus.Pass))
        {
            return AutoCadFramedTextAttributeMatrixCapabilityStatus.Supported;
        }
        if (tested.Length == 0)
        {
            return AutoCadFramedTextAttributeMatrixCapabilityStatus.Inconclusive;
        }
        if (tested.All(status =>
                status == AutoCadFramedTextAttributeMatrixCheckStatus.NotTested))
        {
            return AutoCadFramedTextAttributeMatrixCapabilityStatus.NotTested;
        }
        if (captured.Any(status => !status.HasValue))
        {
            return AutoCadFramedTextAttributeMatrixCapabilityStatus.Inconclusive;
        }

        return AutoCadFramedTextAttributeMatrixCapabilityStatus
            .NotSupportedByTestedPaths;
    }

    private static void AddString(
        ICollection<AutoCadFramedTextAttributeDefinitionFieldComparison> fields,
        string name,
        string before,
        string after,
        bool integrityRelevant,
        StringComparison comparison) =>
        fields.Add(new AutoCadFramedTextAttributeDefinitionFieldComparison(
            name,
            before,
            after,
            integrityRelevant,
            !string.Equals(before, after, comparison)));

    private static void AddDouble(
        ICollection<AutoCadFramedTextAttributeDefinitionFieldComparison> fields,
        string name,
        double before,
        double after) =>
        fields.Add(new AutoCadFramedTextAttributeDefinitionFieldComparison(
            name,
            before.ToString("G17", CultureInfo.InvariantCulture),
            after.ToString("G17", CultureInfo.InvariantCulture),
            true,
            !AutoCadFramedTextAttributeProofPolicy.AreClose(before, after)));

    private static void AddValue<T>(
        ICollection<AutoCadFramedTextAttributeDefinitionFieldComparison> fields,
        string name,
        T before,
        T after)
        where T : struct =>
        fields.Add(new AutoCadFramedTextAttributeDefinitionFieldComparison(
            name,
            Convert.ToString(before, CultureInfo.InvariantCulture) ?? string.Empty,
            Convert.ToString(after, CultureInfo.InvariantCulture) ?? string.Empty,
            true,
            !EqualityComparer<T>.Default.Equals(before, after)));

    public static string CreateMarker(
        AutoCadFramedTextAttributeMatrixCase variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return $"{MarkerSchemaVersion}|{variant.Token}";
    }

    public static bool TryParseMarker(
        string? marker,
        out AutoCadFramedTextAttributeMatrixCase? variant)
    {
        variant = null;
        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        var separator = marker.IndexOf('|');
        if (separator <= 0 ||
            !int.TryParse(marker.AsSpan(0, separator), out var version) ||
            version != MarkerSchemaVersion)
        {
            return false;
        }

        var token = marker[(separator + 1)..];
        variant = Variants.FirstOrDefault(candidate => string.Equals(
            candidate.Token,
            token,
            StringComparison.Ordinal));
        return variant is not null;
    }
}
#endif
