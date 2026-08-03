#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadItemLeaderBlockVariantProofStyleSlot
{
    StyleA,
    StyleB,
}

internal enum AutoCadItemLeaderBlockVariantProofStatus
{
    Pass,
    Fail,
    NotTested,
}

internal sealed record AutoCadItemLeaderBlockVariantProofCase
{
    public string Token { get; }
    public AutoCadItemLeaderBlockFrameKind FrameKind { get; }
    public AutoCadItemLeaderBlockVariantProofStyleSlot StyleSlot { get; }
    public double ItemNumberPaperHeightMm { get; }
    public int AnnotationScaleDenominator { get; }
    public double DefinitionBaseHeight { get; }
    public double BlockScale { get; }
    public double EffectiveHeight { get; }
    public double PositionX { get; }
    public bool CreatesLeader { get; }

    private AutoCadItemLeaderBlockVariantProofCase(
        string token,
        AutoCadItemLeaderBlockFrameKind frameKind,
        AutoCadItemLeaderBlockVariantProofStyleSlot styleSlot,
        double itemNumberPaperHeightMm,
        int annotationScaleDenominator,
        double positionX,
        bool createsLeader)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Proof token is required.", nameof(token));
        }
        if (!Enum.IsDefined(frameKind) || !Enum.IsDefined(styleSlot))
        {
            throw new ArgumentOutOfRangeException(nameof(frameKind));
        }
        if (!TimberAnnotationTextSettingsRules
                .IsValidItemNumberPaperHeightMm(itemNumberPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemNumberPaperHeightMm));
        }
        if (!TimberAnnotationScaleRules.IsValidDenominator(
                annotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(annotationScaleDenominator));
        }

        Token = token;
        FrameKind = frameKind;
        StyleSlot = styleSlot;
        ItemNumberPaperHeightMm = itemNumberPaperHeightMm;
        AnnotationScaleDenominator = annotationScaleDenominator;
        DefinitionBaseHeight =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                itemNumberPaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator);
        BlockScale = TimberAnnotationScaleRules.GetScaleFactor(
            annotationScaleDenominator);
        EffectiveHeight = DefinitionBaseHeight * BlockScale;
        PositionX = positionX;
        CreatesLeader = createsLeader;
    }

    public static AutoCadItemLeaderBlockVariantProofCase Create(
        string token,
        AutoCadItemLeaderBlockFrameKind frameKind,
        AutoCadItemLeaderBlockVariantProofStyleSlot styleSlot,
        double itemNumberPaperHeightMm,
        int annotationScaleDenominator,
        double positionX,
        bool createsLeader = true) =>
        new(
            token,
            frameKind,
            styleSlot,
            itemNumberPaperHeightMm,
            annotationScaleDenominator,
            positionX,
            createsLeader);

    public ItemNumberLeaderStyle ToItemNumberLeaderStyle() => FrameKind switch
    {
        AutoCadItemLeaderBlockFrameKind.Circle => ItemNumberLeaderStyle.Circle,
        AutoCadItemLeaderBlockFrameKind.Slot => ItemNumberLeaderStyle.Slot,
        AutoCadItemLeaderBlockFrameKind.Rectangle =>
            ItemNumberLeaderStyle.Rectangle,
        _ => throw new InvalidOperationException("Unsupported proof frame kind."),
    };
}

internal enum AutoCadItemLeaderBlockVariantProofStyleBState
{
    Tested,
    NotTestedNoSecondCompatibleStyle,
}

internal sealed record AutoCadItemLeaderBlockVariantProofMarker(
    int SchemaVersion,
    string SuiteIdentifier,
    string CaseToken,
    string VariantKeyPayload,
    string CanonicalBlockName,
    AutoCadItemLeaderBlockFrameKind ExpectedFrameKind,
    string ExpectedCanonicalStyleName,
    double ExpectedPaperHeight,
    double ExpectedDefinitionHeight,
    double ExpectedBlockScale,
    double ExpectedEffectiveHeight);

internal sealed record AutoCadItemLeaderBlockVariantProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    bool CreateCompleted,
    AutoCadItemLeaderBlockVariantProofStyleBState StyleBState,
    string CanonicalStyleNameA,
    string? CanonicalStyleNameB,
    AutoCadItemLeaderBlockVariantProofMarker[] ExpectedCases);

internal sealed record AutoCadItemLeaderBlockVariantObservedMarker(
    AutoCadItemLeaderBlockVariantProofObjectSpace Space,
    bool HasProofRegApp,
    AutoCadItemLeaderBlockVariantProofMarker? Marker,
    string CandidateDiagnosticId);

internal sealed record AutoCadItemLeaderBlockVariantProofRecoveryResult(
    bool Succeeded,
    IReadOnlyDictionary<string, string> AcceptedCandidateByCase,
    IReadOnlyList<string> Errors);

internal sealed record AutoCadItemLeaderBlockVariantProofCheck(
    string Name,
    AutoCadItemLeaderBlockVariantProofStatus Status,
    string Expected,
    string Actual);

internal enum AutoCadItemLeaderBlockVariantProofObjectSpace
{
    ModelSpace,
    PaperSpace,
    BlockDefinition,
    DatabaseObject,
}

internal sealed record AutoCadItemLeaderBlockVariantProofObjectSnapshot(
    AutoCadItemLeaderBlockVariantProofObjectSpace Space,
    bool IsValid,
    bool IsEntity,
    bool IsErased,
    bool OwnerIsModelSpace,
    string Handle,
    string ObjectId,
    string DxfName,
    string RxClassName,
    string Layer,
    string OwnerHandle,
    string OwnerName);

internal sealed record AutoCadItemLeaderBlockVariantProofPreflightResult(
    IReadOnlyList<AutoCadItemLeaderBlockVariantProofObjectSnapshot>
        BlockingModelSpaceEntities,
    int InvalidModelSpaceObjectCount,
    bool CurrentSpaceIsModelSpace)
{
    public bool Passed => BlockingModelSpaceEntities.Count == 0;
}

internal static class AutoCadItemLeaderBlockVariantProofPreflightPolicy
{
    public static AutoCadItemLeaderBlockVariantProofPreflightResult Evaluate(
        IEnumerable<AutoCadItemLeaderBlockVariantProofObjectSnapshot> objects,
        bool currentSpaceIsModelSpace)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var captured = objects.ToArray();
        var blocking = captured
            .Where(candidate =>
                candidate.Space ==
                    AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace &&
                candidate.IsValid &&
                candidate.IsEntity &&
                !candidate.IsErased &&
                candidate.OwnerIsModelSpace)
            .ToArray();
        var invalidCount = captured.Count(candidate =>
            candidate.Space ==
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace &&
            !candidate.IsValid);
        return new AutoCadItemLeaderBlockVariantProofPreflightResult(
            Array.AsReadOnly(blocking),
            invalidCount,
            currentSpaceIsModelSpace);
    }
}

internal static class AutoCadItemLeaderBlockVariantProofPolicy
{
    public const int MarkerSchemaVersion = 1;
    public const int ManifestSchemaVersion = 1;
    public const int PayloadAsciiChunkLength = 240;
    public const string SuiteIdentifier =
        "ACAD_KROVY_STAGE4A_BLOCK_VARIANT_PROOF";
    private const double Tolerance = 1e-9;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<AutoCadItemLeaderBlockVariantProofCase> Cases { get; } =
        Array.AsReadOnly(
        [
            AutoCadItemLeaderBlockVariantProofCase.Create(
                "A",
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA,
                2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                0d),
            AutoCadItemLeaderBlockVariantProofCase.Create(
                "B",
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleB,
                3.2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                800d),
            AutoCadItemLeaderBlockVariantProofCase.Create(
                "C",
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA,
                2d,
                100,
                1600d),
            AutoCadItemLeaderBlockVariantProofCase.Create(
                "D",
                AutoCadItemLeaderBlockFrameKind.Slot,
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA,
                2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                2400d),
            AutoCadItemLeaderBlockVariantProofCase.Create(
                "E",
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA,
                2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                3200d),
        ]);

    public static AutoCadItemLeaderBlockVariantProofMarker CreateMarker(
        AutoCadItemLeaderBlockVariantProofCase proofCase,
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalBlockName,
        string canonicalTextStyleName)
    {
        ArgumentNullException.ThrowIfNull(proofCase);
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(canonicalBlockName))
        {
            throw new ArgumentException(
                "Canonical block name is required.",
                nameof(canonicalBlockName));
        }
        if (string.IsNullOrWhiteSpace(canonicalTextStyleName))
        {
            throw new ArgumentException(
                "Canonical text-style name is required.",
                nameof(canonicalTextStyleName));
        }
        if (key.FrameKind != proofCase.FrameKind)
        {
            throw new ArgumentException(
                "Proof case and variant key do not match.",
                nameof(key));
        }

        return new AutoCadItemLeaderBlockVariantProofMarker(
            MarkerSchemaVersion,
            SuiteIdentifier,
            proofCase.Token,
            AutoCadItemLeaderBlockVariantNamePolicy.CreateFingerprintPayload(key),
            canonicalBlockName,
            proofCase.FrameKind,
            canonicalTextStyleName.Trim(),
            proofCase.ItemNumberPaperHeightMm,
            proofCase.DefinitionBaseHeight,
            proofCase.BlockScale,
            proofCase.EffectiveHeight);
    }

    public static AutoCadItemLeaderBlockVariantProofManifest CreateManifest(
        AutoCadItemLeaderBlockVariantProofStyleBState styleBState,
        string canonicalStyleNameA,
        string? canonicalStyleNameB,
        IEnumerable<AutoCadItemLeaderBlockVariantProofMarker> expectedCases)
    {
        if (string.IsNullOrWhiteSpace(canonicalStyleNameA))
        {
            throw new ArgumentException(
                "Canonical Style A name is required.",
                nameof(canonicalStyleNameA));
        }
        ArgumentNullException.ThrowIfNull(expectedCases);
        var manifest = new AutoCadItemLeaderBlockVariantProofManifest(
            ManifestSchemaVersion,
            SuiteIdentifier,
            true,
            styleBState,
            canonicalStyleNameA,
            canonicalStyleNameB,
            expectedCases.ToArray());
        if (!IsValidManifest(manifest))
        {
            throw new ArgumentException("Proof manifest is inconsistent.");
        }
        return manifest;
    }

    public static IReadOnlyList<string> SerializeMarker(
        AutoCadItemLeaderBlockVariantProofMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (!IsValidMarker(marker))
        {
            throw new ArgumentException("Proof marker is invalid.", nameof(marker));
        }
        return Serialize(marker);
    }

    public static bool TryDeserializeMarker(
        IEnumerable<string> chunks,
        out AutoCadItemLeaderBlockVariantProofMarker? marker)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return TryDeserialize(chunks, IsStructurallyValidMarker, out marker);
    }

    public static IReadOnlyList<string> SerializeManifest(
        AutoCadItemLeaderBlockVariantProofManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!IsValidManifest(manifest))
        {
            throw new ArgumentException(
                "Proof manifest is invalid.",
                nameof(manifest));
        }
        return Serialize(manifest);
    }

    public static bool TryDeserializeManifest(
        IEnumerable<string> chunks,
        out AutoCadItemLeaderBlockVariantProofManifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        return TryDeserialize(chunks, IsValidManifest, out manifest);
    }

    public static AutoCadItemLeaderBlockVariantProofRecoveryResult
        EvaluateRecovery(
        AutoCadItemLeaderBlockVariantProofManifest? manifest,
        IEnumerable<AutoCadItemLeaderBlockVariantObservedMarker> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (manifest is null || !IsValidManifest(manifest))
        {
            return new AutoCadItemLeaderBlockVariantProofRecoveryResult(
                false,
                new Dictionary<string, string>(),
                ["Persisted proof manifest is missing or invalid."]);
        }

        var errors = new List<string>();
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        var expected = manifest.ExpectedCases.ToDictionary(
            marker => marker.CaseToken,
            StringComparer.Ordinal);
        var modelSpace = observations
            .Where(observation => observation.Space ==
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace)
            .ToArray();
        foreach (var invalid in modelSpace.Where(observation =>
                     observation.HasProofRegApp && observation.Marker is null))
        {
            errors.Add(
                $"Invalid or unreadable proof payload: {invalid.CandidateDiagnosticId}.");
        }

        var suiteMarkers = modelSpace
            .Where(observation => observation.Marker is
                { SuiteIdentifier: SuiteIdentifier })
            .ToArray();
        foreach (var group in suiteMarkers.GroupBy(
                     observation => observation.Marker!.CaseToken,
                     StringComparer.Ordinal))
        {
            if (!expected.ContainsKey(group.Key))
            {
                errors.Add($"Unexpected proof case marker: {group.Key}.");
                continue;
            }
            var candidates = group.ToArray();
            if (candidates.Length != 1)
            {
                errors.Add(
                    $"Proof case {group.Key} has {candidates.Length} markers; expected exactly one.");
                continue;
            }
            if (candidates[0].Marker != expected[group.Key])
            {
                errors.Add($"Proof case {group.Key} payload differs from manifest.");
                continue;
            }
            accepted.Add(group.Key, candidates[0].CandidateDiagnosticId);
        }
        foreach (var token in expected.Keys.Where(token =>
                     !accepted.ContainsKey(token) &&
                     !errors.Any(error => error.Contains(
                         $"case {token} ",
                         StringComparison.Ordinal))))
        {
            errors.Add($"Proof case {token} marker is missing.");
        }

        return new AutoCadItemLeaderBlockVariantProofRecoveryResult(
            errors.Count == 0,
            accepted,
            errors.AsReadOnly());
    }

    public static AutoCadItemLeaderBlockVariantProofCheck Evaluate(
        string name,
        double expected,
        double actual) =>
        new(
            name,
            AreClose(expected, actual)
                ? AutoCadItemLeaderBlockVariantProofStatus.Pass
                : AutoCadItemLeaderBlockVariantProofStatus.Fail,
            Format(expected),
            Format(actual));

    public static AutoCadItemLeaderBlockVariantProofCheck Evaluate(
        string name,
        string expected,
        string actual,
        StringComparison comparison = StringComparison.Ordinal) =>
        new(
            name,
            string.Equals(expected, actual, comparison)
                ? AutoCadItemLeaderBlockVariantProofStatus.Pass
                : AutoCadItemLeaderBlockVariantProofStatus.Fail,
            expected,
            actual);

    public static AutoCadItemLeaderBlockVariantProofCheck NotTested(
        string name,
        string reason) =>
        new(
            name,
            AutoCadItemLeaderBlockVariantProofStatus.NotTested,
            reason,
            reason);

    public static bool AreClose(double expected, double actual) =>
        double.IsFinite(expected) && double.IsFinite(actual) &&
        Math.Abs(expected - actual) <= Tolerance;

    public static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsValidMarker(
        AutoCadItemLeaderBlockVariantProofMarker marker)
        => marker.SuiteIdentifier == SuiteIdentifier &&
            IsStructurallyValidMarker(marker);

    private static bool IsStructurallyValidMarker(
        AutoCadItemLeaderBlockVariantProofMarker marker)
    {
        var proofCase = Cases.FirstOrDefault(candidate =>
            candidate.Token == marker.CaseToken);
        return marker.SchemaVersion == MarkerSchemaVersion &&
            !string.IsNullOrWhiteSpace(marker.SuiteIdentifier) &&
            proofCase is not null &&
            !string.IsNullOrWhiteSpace(marker.VariantKeyPayload) &&
            AutoCadItemLeaderBlockVariantNamePolicy.IsSafeSymbolName(
                marker.CanonicalBlockName) &&
            marker.ExpectedFrameKind == proofCase.FrameKind &&
            !string.IsNullOrWhiteSpace(marker.ExpectedCanonicalStyleName) &&
            AreClose(
                marker.ExpectedPaperHeight,
                proofCase.ItemNumberPaperHeightMm) &&
            AreClose(
                marker.ExpectedDefinitionHeight,
                proofCase.DefinitionBaseHeight) &&
            AreClose(marker.ExpectedBlockScale, proofCase.BlockScale) &&
            AreClose(marker.ExpectedEffectiveHeight, proofCase.EffectiveHeight);
    }

    private static bool IsValidManifest(
        AutoCadItemLeaderBlockVariantProofManifest manifest)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion ||
            manifest.SuiteIdentifier != SuiteIdentifier ||
            !manifest.CreateCompleted ||
            !Enum.IsDefined(manifest.StyleBState) ||
            string.IsNullOrWhiteSpace(manifest.CanonicalStyleNameA) ||
            manifest.ExpectedCases is null ||
            manifest.ExpectedCases.Length == 0 ||
            manifest.ExpectedCases.Any(marker => !IsValidMarker(marker)) ||
            manifest.ExpectedCases.Select(marker => marker.CaseToken)
                .Distinct(StringComparer.Ordinal).Count() !=
                manifest.ExpectedCases.Length)
        {
            return false;
        }

        var hasB = manifest.ExpectedCases.Any(marker => marker.CaseToken == "B");
        var expectedTokens = Cases
            .Where(proofCase => proofCase.Token != "B" || hasB)
            .Select(proofCase => proofCase.Token)
            .ToHashSet(StringComparer.Ordinal);
        var actualTokens = manifest.ExpectedCases
            .Select(marker => marker.CaseToken)
            .ToHashSet(StringComparer.Ordinal);
        return expectedTokens.SetEquals(actualTokens) &&
            (manifest.StyleBState ==
                AutoCadItemLeaderBlockVariantProofStyleBState.Tested
                ? hasB && !string.IsNullOrWhiteSpace(manifest.CanonicalStyleNameB)
                : !hasB && manifest.CanonicalStyleNameB is null);
    }

    private static IReadOnlyList<string> Serialize<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var chunks = new List<string>();
        for (var index = 0;
             index < encoded.Length;
             index += PayloadAsciiChunkLength)
        {
            chunks.Add(encoded.Substring(
                index,
                Math.Min(PayloadAsciiChunkLength, encoded.Length - index)));
        }
        return chunks.AsReadOnly();
    }

    private static bool TryDeserialize<T>(
        IEnumerable<string> chunks,
        Func<T, bool> validate,
        out T? payload)
        where T : class
    {
        payload = null;
        try
        {
            var encoded = string.Concat(chunks);
            if (encoded.Length == 0)
            {
                return false;
            }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            payload = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return payload is not null && validate(payload);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or NotSupportedException)
        {
            payload = null;
            return false;
        }
    }
}
#endif
