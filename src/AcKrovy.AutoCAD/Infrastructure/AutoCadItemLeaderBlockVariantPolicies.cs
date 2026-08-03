using System.Globalization;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadItemLeaderBlockVariantValidationReasonCode
{
    Valid,
    BlockUnavailable,
    BlockFlagsMismatch,
    ItemNoMissing,
    ItemNoDuplicate,
    UnexpectedAttributeDefinition,
    FrameEntityCountMismatch,
    ItemNoWrongOwner,
    ItemNoWrongTag,
    ItemNoInvalidTextStyleId,
    ItemNoTextStyleDatabaseMismatch,
    ItemNoWrongCanonicalTextStyle,
    ItemNoWrongDefinitionHeight,
    ItemNoWrongImmutableProperty,
    FrameGeometryMismatch,
}

internal sealed record AutoCadItemLeaderBlockVariantValidationField(
    string PropertyName,
    string Expected,
    string Actual,
    bool Passed,
    string Tolerance);

internal sealed record AutoCadItemLeaderBlockVariantAttributeSnapshot(
    bool OwnerMatchesBlock,
    string Tag,
    string Prompt,
    string DefaultText,
    double Height,
    string TextStyleObjectId,
    bool TextStyleIdIsValid,
    bool TextStyleBelongsToDatabase,
    bool TextStyleMatchesResolvedRuntimeId,
    string CanonicalTextStyleName,
    double TextStyleFixedHeight,
    string TextStyleAnnotativeState,
    double PositionX,
    double PositionY,
    double PositionZ,
    double AlignmentX,
    double AlignmentY,
    double AlignmentZ,
    double Rotation,
    string HorizontalMode,
    string VerticalMode,
    bool LockPositionInBlock,
    bool Constant,
    bool Invisible,
    bool Preset,
    bool Verifiable,
    bool IsMTextAttributeDefinition,
    bool IsErased,
    bool HasByBlockAppearance);

internal sealed record AutoCadItemLeaderBlockVariantAttributeValidation(
    bool IsValid,
    AutoCadItemLeaderBlockVariantValidationReasonCode ReasonCode,
    IReadOnlyList<AutoCadItemLeaderBlockVariantValidationField> Fields,
    string Reason);

internal sealed record AutoCadItemLeaderBlockVariantInventoryValidation(
    bool IsValid,
    AutoCadItemLeaderBlockVariantValidationReasonCode ReasonCode,
    string Reason);

internal sealed record AutoCadItemLeaderBlockVariantDefinitionDiagnostic(
    string BlockName,
    string BlockHandle,
    string BlockObjectId,
    string DatabaseIdentity,
    int EntityCount,
    int AttributeDefinitionCount,
    string AttributeHandle,
    string AttributeObjectId,
    string AttributeOwnerHandle,
    string AttributeOwnerName,
    AutoCadItemLeaderBlockVariantAttributeSnapshot? Attribute,
    string FrameSignature);

internal sealed record AutoCadItemLeaderBlockVariantDefinitionValidationResult(
    bool IsValid,
    AutoCadItemLeaderBlockVariantValidationReasonCode ReasonCode,
    IReadOnlyList<AutoCadItemLeaderBlockVariantValidationField> FieldFailures,
    IReadOnlyList<AutoCadItemLeaderBlockVariantValidationField> FieldChecks,
    string? ActualCanonicalTextStyleName,
    double? ActualDefinitionHeight,
    string ActualFrameSignature,
    string Reason,
    AutoCadItemLeaderBlockVariantDefinitionDiagnostic Diagnostic);

internal static class AutoCadItemLeaderBlockVariantAttributeValidationPolicy
{
    public const double DatabaseDoubleTolerance = 1e-9;

    public static AutoCadItemLeaderBlockVariantAttributeValidation Evaluate(
        AutoCadItemLeaderBlockVariantAttributeSnapshot actual,
        double expectedDefinitionHeight)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (!double.IsFinite(expectedDefinitionHeight) ||
            expectedDefinitionHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDefinitionHeight));
        }

        var checks = new List<AutoCadItemLeaderBlockVariantValidationField>
        {
            Boolean("owner is variant BlockTableRecord", true, actual.OwnerMatchesBlock),
            Boolean("IsErased", false, actual.IsErased),
            Text(
                "Tag",
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                actual.Tag,
                StringComparison.OrdinalIgnoreCase),
            Text(
                "Prompt",
                TimberItemLeaderBlockDefinitionRules.AttributeTag,
                actual.Prompt,
                StringComparison.Ordinal),
            Text("TextString/default text", string.Empty, actual.DefaultText),
            Boolean("TextStyleId is valid", true, actual.TextStyleIdIsValid),
            Boolean(
                "TextStyleId belongs to current Database",
                true,
                actual.TextStyleBelongsToDatabase),
            Number("Height", expectedDefinitionHeight, actual.Height),
            Text("HorizontalMode", "TextCenter", actual.HorizontalMode),
            Text("VerticalMode", "TextVerticalMid", actual.VerticalMode),
            Number("Rotation", 0d, actual.Rotation),
            Number("AlignmentPoint.X", 0d, actual.AlignmentX),
            Number("AlignmentPoint.Y", 0d, actual.AlignmentY),
            Number("AlignmentPoint.Z", 0d, actual.AlignmentZ),
            Boolean("LockPositionInBlock", true, actual.LockPositionInBlock),
            Boolean("Constant", false, actual.Constant),
            Boolean("Invisible", false, actual.Invisible),
            Boolean("Preset", false, actual.Preset),
            Boolean("Verifiable", false, actual.Verifiable),
            Boolean(
                "IsMTextAttributeDefinition",
                false,
                actual.IsMTextAttributeDefinition),
            Boolean("ByBlock appearance", true, actual.HasByBlockAppearance),
        };
        var failures = checks.Where(check => !check.Passed).ToArray();
        if (failures.Length == 0)
        {
            return new AutoCadItemLeaderBlockVariantAttributeValidation(
                true,
                AutoCadItemLeaderBlockVariantValidationReasonCode.Valid,
                checks.AsReadOnly(),
                "ITEM_NO matches the immutable variant contract.");
        }

        var reasonCode = Classify(failures[0].PropertyName);
        return new AutoCadItemLeaderBlockVariantAttributeValidation(
            false,
            reasonCode,
            checks.AsReadOnly(),
            $"{failures[0].PropertyName} mismatch: expected " +
            $"'{failures[0].Expected}', actual '{failures[0].Actual}'.");
    }

    public static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static AutoCadItemLeaderBlockVariantValidationField Boolean(
        string property,
        bool expected,
        bool actual) =>
        new(property, expected.ToString(), actual.ToString(), expected == actual, "exact");

    private static AutoCadItemLeaderBlockVariantValidationField Text(
        string property,
        string expected,
        string actual,
        StringComparison comparison = StringComparison.Ordinal) =>
        new(
            property,
            expected,
            actual,
            string.Equals(expected, actual, comparison),
            comparison.ToString());

    private static AutoCadItemLeaderBlockVariantValidationField Number(
        string property,
        double expected,
        double actual) =>
        new(
            property,
            Format(expected),
            Format(actual),
            double.IsFinite(actual) &&
                Math.Abs(expected - actual) <= DatabaseDoubleTolerance,
            Format(DatabaseDoubleTolerance));

    private static AutoCadItemLeaderBlockVariantValidationReasonCode Classify(
        string property) => property switch
        {
            "owner is variant BlockTableRecord" =>
                AutoCadItemLeaderBlockVariantValidationReasonCode.ItemNoWrongOwner,
            "Tag" => AutoCadItemLeaderBlockVariantValidationReasonCode.ItemNoWrongTag,
            "TextStyleId is valid" =>
                AutoCadItemLeaderBlockVariantValidationReasonCode
                    .ItemNoInvalidTextStyleId,
            "TextStyleId belongs to current Database" =>
                AutoCadItemLeaderBlockVariantValidationReasonCode
                    .ItemNoTextStyleDatabaseMismatch,
            "Height" => AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoWrongDefinitionHeight,
            _ => AutoCadItemLeaderBlockVariantValidationReasonCode
                .ItemNoWrongImmutableProperty,
        };
}

internal static class AutoCadItemLeaderBlockVariantInventoryValidationPolicy
{
    public static AutoCadItemLeaderBlockVariantInventoryValidation Evaluate(
        int attributeDefinitionCount,
        int itemNumberAttributeCount,
        int frameEntityCount,
        string? soleAttributeTag)
    {
        if (attributeDefinitionCount < 0 || itemNumberAttributeCount < 0 ||
            frameEntityCount < 0 ||
            itemNumberAttributeCount > attributeDefinitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(attributeDefinitionCount));
        }
        if (itemNumberAttributeCount == 0)
        {
            return attributeDefinitionCount == 1
                ? Invalid(
                    AutoCadItemLeaderBlockVariantValidationReasonCode.ItemNoWrongTag,
                    $"The only AttributeDefinition has wrong tag '{soleAttributeTag ?? "<unavailable>"}'.")
                : Invalid(
                    AutoCadItemLeaderBlockVariantValidationReasonCode.ItemNoMissing,
                    "ITEM_NO AttributeDefinition is missing.");
        }
        if (itemNumberAttributeCount > 1)
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode.ItemNoDuplicate,
                $"Definition contains {itemNumberAttributeCount} live ITEM_NO attributes.");
        }
        if (attributeDefinitionCount != 1)
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode
                    .UnexpectedAttributeDefinition,
                $"Definition contains {attributeDefinitionCount} live AttributeDefinitions; expected one.");
        }
        if (frameEntityCount != 1)
        {
            return Invalid(
                AutoCadItemLeaderBlockVariantValidationReasonCode
                    .FrameEntityCountMismatch,
                $"Definition contains {frameEntityCount} live frame entities; expected one.");
        }

        return new AutoCadItemLeaderBlockVariantInventoryValidation(
            true,
            AutoCadItemLeaderBlockVariantValidationReasonCode.Valid,
            "Definition contains exactly one ITEM_NO and one frame entity.");
    }

    private static AutoCadItemLeaderBlockVariantInventoryValidation Invalid(
        AutoCadItemLeaderBlockVariantValidationReasonCode reasonCode,
        string reason) =>
        new(false, reasonCode, reason);
}

internal enum AutoCadItemLeaderBlockVariantCandidateState
{
    Missing,
    Matching,
    Invalid,
}

internal enum AutoCadItemLeaderBlockVariantCollisionDecisionKind
{
    Create,
    Reuse,
    Exhausted,
}

internal sealed record AutoCadItemLeaderBlockVariantCollisionDecision(
    AutoCadItemLeaderBlockVariantCollisionDecisionKind Kind,
    string CandidateName,
    bool IsCollision,
    int CollisionAttempt);

internal static class AutoCadItemLeaderBlockVariantCollisionPolicy
{
    public const int MaximumCollisionAttempts = 64;

    public static AutoCadItemLeaderBlockVariantCollisionDecision Select(
        AutoCadItemLeaderBlockVariantKey key,
        Func<string, AutoCadItemLeaderBlockVariantCandidateState> inspect)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(inspect);

        var canonicalName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
        var canonicalState = inspect(canonicalName);
        if (canonicalState == AutoCadItemLeaderBlockVariantCandidateState.Missing)
        {
            return new AutoCadItemLeaderBlockVariantCollisionDecision(
                AutoCadItemLeaderBlockVariantCollisionDecisionKind.Create,
                canonicalName,
                false,
                0);
        }
        if (canonicalState == AutoCadItemLeaderBlockVariantCandidateState.Matching)
        {
            return new AutoCadItemLeaderBlockVariantCollisionDecision(
                AutoCadItemLeaderBlockVariantCollisionDecisionKind.Reuse,
                canonicalName,
                false,
                0);
        }

        for (var attempt = 1;
             attempt <= MaximumCollisionAttempts;
             attempt++)
        {
            var collisionName =
                AutoCadItemLeaderBlockVariantNamePolicy.CreateCollisionName(
                    key,
                    attempt);
            var state = inspect(collisionName);
            if (state == AutoCadItemLeaderBlockVariantCandidateState.Missing)
            {
                return new AutoCadItemLeaderBlockVariantCollisionDecision(
                    AutoCadItemLeaderBlockVariantCollisionDecisionKind.Create,
                    collisionName,
                    true,
                    attempt);
            }
            if (state == AutoCadItemLeaderBlockVariantCandidateState.Matching)
            {
                return new AutoCadItemLeaderBlockVariantCollisionDecision(
                    AutoCadItemLeaderBlockVariantCollisionDecisionKind.Reuse,
                    collisionName,
                    true,
                    attempt);
            }
        }

        return new AutoCadItemLeaderBlockVariantCollisionDecision(
            AutoCadItemLeaderBlockVariantCollisionDecisionKind.Exhausted,
            canonicalName,
            true,
            MaximumCollisionAttempts);
    }
}

internal sealed record AutoCadItemLeaderBlockVariantBatchEntry<TDefinitionId>(
    TDefinitionId DefinitionId,
    string ResolvedBlockName,
    bool IsCollision);

internal sealed class AutoCadItemLeaderBlockVariantBatchIndex<TDefinitionId>
    where TDefinitionId : notnull
{
    private readonly Dictionary<
        AutoCadItemLeaderBlockVariantKey,
        AutoCadItemLeaderBlockVariantBatchEntry<TDefinitionId>> _entries = [];

    public AutoCadDatabaseIdentityToken DatabaseIdentity { get; }
    public int Count => _entries.Count;

    public AutoCadItemLeaderBlockVariantBatchIndex(
        AutoCadDatabaseIdentityToken databaseIdentity)
    {
        DatabaseIdentity = databaseIdentity.IsValid
            ? databaseIdentity
            : throw new ArgumentException(
                "A valid database identity is required.",
                nameof(databaseIdentity));
    }

    public bool TryGet(
        AutoCadDatabaseIdentityToken databaseIdentity,
        AutoCadItemLeaderBlockVariantKey key,
        out AutoCadItemLeaderBlockVariantBatchEntry<TDefinitionId>? entry)
    {
        EnsureDatabase(databaseIdentity);
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out entry);
    }

    public void Add(
        AutoCadDatabaseIdentityToken databaseIdentity,
        AutoCadItemLeaderBlockVariantKey key,
        TDefinitionId definitionId,
        string resolvedBlockName,
        bool isCollision)
    {
        EnsureDatabase(databaseIdentity);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(definitionId);
        if (string.IsNullOrWhiteSpace(resolvedBlockName))
        {
            throw new ArgumentException(
                "Resolved block name is required.",
                nameof(resolvedBlockName));
        }

        var entry = new AutoCadItemLeaderBlockVariantBatchEntry<TDefinitionId>(
            definitionId,
            resolvedBlockName,
            isCollision);
        if (_entries.TryGetValue(key, out var existing))
        {
            if (!EqualityComparer<TDefinitionId>.Default.Equals(
                    existing.DefinitionId,
                    definitionId) ||
                existing.ResolvedBlockName != resolvedBlockName ||
                existing.IsCollision != isCollision)
            {
                throw new InvalidOperationException(
                    "The batch already contains a different definition for this key.");
            }
            return;
        }

        _entries.Add(key, entry);
    }

    private void EnsureDatabase(AutoCadDatabaseIdentityToken databaseIdentity)
    {
        var comparison = AutoCadDatabaseIdentityPolicy.Compare(
            DatabaseIdentity,
            databaseIdentity,
            managedReferenceEquals: false);
        if (!comparison.IsSameDatabase)
        {
            throw new ArgumentException(
                "Batch operation used a different database identity.",
                nameof(databaseIdentity));
        }
    }
}
