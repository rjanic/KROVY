#if DEBUG
using System.Text;
using System.Text.Json;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadFramedTextAttributeProofStyleSlot
{
    StyleA,
    StyleB,
}

internal enum AutoCadFramedTextAttributeProofStatus
{
    Pass,
    Fail,
    NotTested,
    InvalidEnvironment,
}

internal sealed record AutoCadFramedTextAttributeProofCase
{
    public string Token { get; }
    public AutoCadFramedTextAttributeProofStyleSlot StyleSlot { get; }
    public double ItemNumberPaperHeightMm { get; }
    public int AnnotationScaleDenominator { get; }
    public double BaseAttributeHeight { get; }
    public double BlockScale { get; }
    public double EffectiveModelHeight { get; }
    public double BlockPositionX { get; }

    private AutoCadFramedTextAttributeProofCase(
        string token,
        AutoCadFramedTextAttributeProofStyleSlot styleSlot,
        double itemNumberPaperHeightMm,
        int annotationScaleDenominator,
        double blockPositionX)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Proof token is required.", nameof(token));
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
        StyleSlot = styleSlot;
        ItemNumberPaperHeightMm = itemNumberPaperHeightMm;
        AnnotationScaleDenominator = annotationScaleDenominator;
        BaseAttributeHeight = CalculateBaseAttributeHeight(
            itemNumberPaperHeightMm);
        BlockScale = CalculateBlockScale(annotationScaleDenominator);
        EffectiveModelHeight = CalculateEffectiveModelHeight(
            BaseAttributeHeight,
            BlockScale);
        BlockPositionX = blockPositionX;
    }

    public static AutoCadFramedTextAttributeProofCase Create(
        string token,
        AutoCadFramedTextAttributeProofStyleSlot styleSlot,
        double itemNumberPaperHeightMm,
        int annotationScaleDenominator,
        double blockPositionX) =>
        new(
            token,
            styleSlot,
            itemNumberPaperHeightMm,
            annotationScaleDenominator,
            blockPositionX);

    private static double CalculateBaseAttributeHeight(double paperHeightMm) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            paperHeightMm,
            TimberAnnotationScaleRules.DefaultDenominator);

    private static double CalculateBlockScale(int denominator) =>
        TimberAnnotationScaleRules.GetScaleFactor(denominator);

    private static double CalculateEffectiveModelHeight(
        double baseAttributeHeight,
        double blockScale) =>
        baseAttributeHeight * blockScale;
}

internal sealed record AutoCadFramedTextAttributeDefinitionSnapshot(
    string AttributeDefinitionHandle,
    string TextStyleHandle,
    string Tag,
    string Prompt,
    double Height,
    string TextString,
    double PositionX,
    double PositionY,
    double PositionZ,
    double AlignmentX,
    double AlignmentY,
    double AlignmentZ,
    double Rotation,
    int HorizontalMode,
    int VerticalMode,
    bool Invisible,
    bool Constant,
    bool LockPositionInBlock);

internal sealed record AutoCadFramedTextAttributeProofPayload(
    int SchemaVersion,
    string CaseToken,
    string ExpectedStyleName,
    string ExpectedStyleHandle,
    double ItemNumberPaperHeightMm,
    int AnnotationScaleDenominator,
    double ExpectedBaseAttributeHeight,
    double ExpectedBlockScale,
    double ExpectedEffectiveModelHeight,
    string BlockDefinitionHandle,
    bool DistinctStyleComparisonExpected,
    AutoCadFramedTextAttributeDefinitionSnapshot AttributeDefinitionSnapshot);

internal sealed record AutoCadFramedTextAttributeProofCheckResult
{
    public string CheckName { get; }
    public AutoCadFramedTextAttributeProofStatus Status { get; }
    public string? Expected { get; }
    public string? Actual { get; }
    public string Message { get; }
    public bool IsFailure => Status == AutoCadFramedTextAttributeProofStatus.Fail;
    public bool IsInvalidEnvironment =>
        Status == AutoCadFramedTextAttributeProofStatus.InvalidEnvironment;

    private AutoCadFramedTextAttributeProofCheckResult(
        string checkName,
        AutoCadFramedTextAttributeProofStatus status,
        string? expected,
        string? actual,
        string message)
    {
        CheckName = string.IsNullOrWhiteSpace(checkName)
            ? throw new ArgumentException("Check name is required.", nameof(checkName))
            : checkName;
        Status = status;
        Expected = expected;
        Actual = actual;
        Message = message;
    }

    public static AutoCadFramedTextAttributeProofCheckResult Evaluated(
        string checkName,
        bool passed,
        string expected,
        string actual) =>
        new(
            checkName,
            passed
                ? AutoCadFramedTextAttributeProofStatus.Pass
                : AutoCadFramedTextAttributeProofStatus.Fail,
            expected ?? throw new ArgumentNullException(nameof(expected)),
            actual ?? throw new ArgumentNullException(nameof(actual)),
            passed ? "Values match." : "Values differ.");

    public static AutoCadFramedTextAttributeProofCheckResult NotTested(
        string checkName,
        string reason) =>
        new(
            checkName,
            AutoCadFramedTextAttributeProofStatus.NotTested,
            null,
            null,
            string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("Reason is required.", nameof(reason))
                : reason);

    public static AutoCadFramedTextAttributeProofCheckResult InvalidEnvironment(
        string checkName,
        string reason) =>
        new(
            checkName,
            AutoCadFramedTextAttributeProofStatus.InvalidEnvironment,
            null,
            null,
            string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("Reason is required.", nameof(reason))
                : reason);
}

internal static class AutoCadFramedTextAttributeProofPolicy
{
    public const int PayloadSchemaVersion = 1;
    public const int XDataAsciiChunkLength = 240;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<AutoCadFramedTextAttributeProofCase> Cases { get; } =
        Array.AsReadOnly(
        [
            AutoCadFramedTextAttributeProofCase.Create(
                "AK23_PROOF_A",
                AutoCadFramedTextAttributeProofStyleSlot.StyleA,
                2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                0d),
            AutoCadFramedTextAttributeProofCase.Create(
                "AK23_PROOF_B",
                AutoCadFramedTextAttributeProofStyleSlot.StyleB,
                3.2d,
                TimberAnnotationScaleRules.DefaultDenominator,
                800d),
            AutoCadFramedTextAttributeProofCase.Create(
                "AK23_PROOF_C",
                AutoCadFramedTextAttributeProofStyleSlot.StyleA,
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                100,
                1800d),
        ]);

    public static AutoCadFramedTextAttributeProofPayload CreatePayload(
        AutoCadFramedTextAttributeProofCase proofCase,
        string expectedStyleName,
        string expectedStyleHandle,
        string blockDefinitionHandle,
        bool distinctStyleComparisonExpected,
        AutoCadFramedTextAttributeDefinitionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(proofCase);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AutoCadFramedTextAttributeProofPayload(
            PayloadSchemaVersion,
            proofCase.Token,
            RequireValue(expectedStyleName, nameof(expectedStyleName)),
            RequireValue(expectedStyleHandle, nameof(expectedStyleHandle)),
            proofCase.ItemNumberPaperHeightMm,
            proofCase.AnnotationScaleDenominator,
            proofCase.BaseAttributeHeight,
            proofCase.BlockScale,
            proofCase.EffectiveModelHeight,
            RequireValue(blockDefinitionHandle, nameof(blockDefinitionHandle)),
            distinctStyleComparisonExpected,
            snapshot);
    }

    public static IReadOnlyList<string> SerializePayload(
        AutoCadFramedTextAttributeProofPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsValidPayload(payload))
        {
            throw new ArgumentException("Proof payload is invalid.", nameof(payload));
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var chunks = new List<string>();
        for (var index = 0; index < encoded.Length; index += XDataAsciiChunkLength)
        {
            chunks.Add(encoded.Substring(
                index,
                Math.Min(XDataAsciiChunkLength, encoded.Length - index)));
        }

        return chunks.AsReadOnly();
    }

    public static bool TryDeserializePayload(
        IEnumerable<string> chunks,
        out AutoCadFramedTextAttributeProofPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        payload = null;
        try
        {
            var encoded = string.Concat(chunks);
            if (encoded.Length == 0)
            {
                return false;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            payload = JsonSerializer.Deserialize<
                AutoCadFramedTextAttributeProofPayload>(json, JsonOptions);
            return payload is not null && IsValidPayload(payload);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or NotSupportedException)
        {
            payload = null;
            return false;
        }
    }

    public static bool AreClose(double expected, double actual) =>
        !double.IsNaN(expected) &&
        !double.IsInfinity(expected) &&
        !double.IsNaN(actual) &&
        !double.IsInfinity(actual) &&
        Math.Abs(expected - actual) <=
            CadLayerScaleHydrationRules.ComparisonTolerance;

    public static bool SnapshotsMatch(
        AutoCadFramedTextAttributeDefinitionSnapshot expected,
        AutoCadFramedTextAttributeDefinitionSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return string.Equals(
                expected.AttributeDefinitionHandle,
                actual.AttributeDefinitionHandle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                expected.TextStyleHandle,
                actual.TextStyleHandle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.Tag, actual.Tag, StringComparison.Ordinal) &&
            string.Equals(expected.Prompt, actual.Prompt, StringComparison.Ordinal) &&
            AreClose(expected.Height, actual.Height) &&
            string.Equals(expected.TextString, actual.TextString, StringComparison.Ordinal) &&
            AreClose(expected.PositionX, actual.PositionX) &&
            AreClose(expected.PositionY, actual.PositionY) &&
            AreClose(expected.PositionZ, actual.PositionZ) &&
            AreClose(expected.AlignmentX, actual.AlignmentX) &&
            AreClose(expected.AlignmentY, actual.AlignmentY) &&
            AreClose(expected.AlignmentZ, actual.AlignmentZ) &&
            AreClose(expected.Rotation, actual.Rotation) &&
            expected.HorizontalMode == actual.HorizontalMode &&
            expected.VerticalMode == actual.VerticalMode &&
            expected.Invisible == actual.Invisible &&
            expected.Constant == actual.Constant &&
            expected.LockPositionInBlock == actual.LockPositionInBlock;
    }

    private static bool IsValidPayload(
        AutoCadFramedTextAttributeProofPayload payload)
    {
        var proofCase = Cases.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Token,
                payload.CaseToken,
                StringComparison.Ordinal));
        return payload.SchemaVersion == PayloadSchemaVersion &&
            proofCase is not null &&
            !string.IsNullOrWhiteSpace(payload.ExpectedStyleName) &&
            !string.IsNullOrWhiteSpace(payload.ExpectedStyleHandle) &&
            !string.IsNullOrWhiteSpace(payload.BlockDefinitionHandle) &&
            payload.AttributeDefinitionSnapshot is not null &&
            IsValidSnapshot(payload.AttributeDefinitionSnapshot) &&
            AreClose(
                proofCase.ItemNumberPaperHeightMm,
                payload.ItemNumberPaperHeightMm) &&
            proofCase.AnnotationScaleDenominator ==
                payload.AnnotationScaleDenominator &&
            AreClose(
                proofCase.BaseAttributeHeight,
                payload.ExpectedBaseAttributeHeight) &&
            AreClose(proofCase.BlockScale, payload.ExpectedBlockScale) &&
            AreClose(
                proofCase.EffectiveModelHeight,
                payload.ExpectedEffectiveModelHeight);
    }

    private static bool IsValidSnapshot(
        AutoCadFramedTextAttributeDefinitionSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.AttributeDefinitionHandle) &&
        !string.IsNullOrWhiteSpace(snapshot.TextStyleHandle) &&
        !string.IsNullOrWhiteSpace(snapshot.Tag) &&
        snapshot.Prompt is not null &&
        snapshot.TextString is not null &&
        AreClose(snapshot.Height, snapshot.Height) &&
        AreClose(snapshot.PositionX, snapshot.PositionX) &&
        AreClose(snapshot.PositionY, snapshot.PositionY) &&
        AreClose(snapshot.PositionZ, snapshot.PositionZ) &&
        AreClose(snapshot.AlignmentX, snapshot.AlignmentX) &&
        AreClose(snapshot.AlignmentY, snapshot.AlignmentY) &&
        AreClose(snapshot.AlignmentZ, snapshot.AlignmentZ) &&
        AreClose(snapshot.Rotation, snapshot.Rotation);

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;
}
#endif
