using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadItemLeaderBlockVariantResultKind
{
    ReusedExisting,
    ReusedCollisionVariant,
    CreatedNew,
    CreatedCollisionVariant,
    NoCompatibleTextStyle,
    InvalidRequest,
    DatabaseMismatch,
    ExistingDefinitionInvalid,
    TextMeasurementFailed,
    TextOverflow,
}

internal sealed record AutoCadItemLeaderBlockVariantResult
{
    public AutoCadItemLeaderBlockVariantResultKind Kind { get; }
    public AutoCadItemLeaderBlockVariantKey? VariantKey { get; }
    public string? CanonicalBlockName { get; }
    public string? ResolvedBlockName { get; }
    public ObjectId? BlockTableRecordId { get; }
    public AutoCadDatabaseIdentityToken? DatabaseIdentity { get; }
    public bool Succeeded => Kind is
        AutoCadItemLeaderBlockVariantResultKind.ReusedExisting or
        AutoCadItemLeaderBlockVariantResultKind.ReusedCollisionVariant or
        AutoCadItemLeaderBlockVariantResultKind.CreatedNew or
        AutoCadItemLeaderBlockVariantResultKind.CreatedCollisionVariant;
    public bool WroteToDatabase => Kind is
        AutoCadItemLeaderBlockVariantResultKind.CreatedNew or
        AutoCadItemLeaderBlockVariantResultKind.CreatedCollisionVariant;
    public bool IsCollision => Kind is
        AutoCadItemLeaderBlockVariantResultKind.ReusedCollisionVariant or
        AutoCadItemLeaderBlockVariantResultKind.CreatedCollisionVariant;
    public string DiagnosticReason { get; }
    public TimberItemLeaderTextFitResult? TextFit { get; }

    private AutoCadItemLeaderBlockVariantResult(
        AutoCadItemLeaderBlockVariantResultKind kind,
        AutoCadItemLeaderBlockVariantKey? variantKey,
        string? canonicalBlockName,
        string? resolvedBlockName,
        ObjectId? blockTableRecordId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string diagnosticReason,
        TimberItemLeaderTextFitResult? textFit)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (string.IsNullOrWhiteSpace(diagnosticReason))
        {
            throw new ArgumentException(
                "A diagnostic reason is required.",
                nameof(diagnosticReason));
        }

        var success = kind is
            AutoCadItemLeaderBlockVariantResultKind.ReusedExisting or
            AutoCadItemLeaderBlockVariantResultKind.ReusedCollisionVariant or
            AutoCadItemLeaderBlockVariantResultKind.CreatedNew or
            AutoCadItemLeaderBlockVariantResultKind.CreatedCollisionVariant;
        if (success &&
            (variantKey is null ||
             string.IsNullOrWhiteSpace(canonicalBlockName) ||
             string.IsNullOrWhiteSpace(resolvedBlockName) ||
             blockTableRecordId is not ObjectId id ||
             !IsUsableObjectId(id) ||
             databaseIdentity is not { IsValid: true } identity ||
             !IsBoundToDatabaseIdentity(id, identity)))
        {
            throw new ArgumentException(
                "A successful result requires a valid key, names, ObjectId, and database identity.");
        }
        if (!success && blockTableRecordId.HasValue)
        {
            throw new ArgumentException(
                "A failed result cannot expose a block ObjectId.");
        }
        if (kind == AutoCadItemLeaderBlockVariantResultKind.NoCompatibleTextStyle &&
            (variantKey is not null || canonicalBlockName is not null ||
             resolvedBlockName is not null))
        {
            throw new ArgumentException(
                "No-compatible-style results cannot claim a variant.");
        }

        Kind = kind;
        VariantKey = variantKey;
        CanonicalBlockName = canonicalBlockName;
        ResolvedBlockName = resolvedBlockName;
        BlockTableRecordId = blockTableRecordId;
        DatabaseIdentity = databaseIdentity;
        DiagnosticReason = diagnosticReason;
        TextFit = textFit;
    }

    public static AutoCadItemLeaderBlockVariantResult Reused(
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalBlockName,
        string resolvedBlockName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken databaseIdentity,
        bool collision,
        string reason) =>
        Success(
            collision
                ? AutoCadItemLeaderBlockVariantResultKind.ReusedCollisionVariant
                : AutoCadItemLeaderBlockVariantResultKind.ReusedExisting,
            key,
            canonicalBlockName,
            resolvedBlockName,
            blockId,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult Created(
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalBlockName,
        string resolvedBlockName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken databaseIdentity,
        bool collision,
        string reason) =>
        Success(
            collision
                ? AutoCadItemLeaderBlockVariantResultKind.CreatedCollisionVariant
                : AutoCadItemLeaderBlockVariantResultKind.CreatedNew,
            key,
            canonicalBlockName,
            resolvedBlockName,
            blockId,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult NoCompatibleTextStyle(
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.NoCompatibleTextStyle,
            null,
            null,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult InvalidRequest(
        AutoCadItemLeaderBlockVariantKey? key,
        string? canonicalBlockName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.InvalidRequest,
            key,
            canonicalBlockName,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult DatabaseMismatch(
        AutoCadItemLeaderBlockVariantKey? key,
        string? canonicalBlockName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.DatabaseMismatch,
            key,
            canonicalBlockName,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult ExistingDefinitionInvalid(
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalBlockName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.ExistingDefinitionInvalid,
            key,
            canonicalBlockName,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult TextMeasurementFailed(
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.TextMeasurementFailed,
            null,
            null,
            databaseIdentity,
            reason);

    public static AutoCadItemLeaderBlockVariantResult TextOverflow(
        AutoCadDatabaseIdentityToken? databaseIdentity,
        TimberItemLeaderTextFitResult textFit) =>
        Failure(
            AutoCadItemLeaderBlockVariantResultKind.TextOverflow,
            null,
            null,
            databaseIdentity,
            textFit.DiagnosticReason).WithTextFit(textFit);

    public AutoCadItemLeaderBlockVariantResult WithTextFit(
        TimberItemLeaderTextFitResult textFit) =>
        new(
            Kind,
            VariantKey,
            CanonicalBlockName,
            ResolvedBlockName,
            BlockTableRecordId,
            DatabaseIdentity,
            DiagnosticReason,
            textFit ?? throw new ArgumentNullException(nameof(textFit)));

    private static AutoCadItemLeaderBlockVariantResult Success(
        AutoCadItemLeaderBlockVariantResultKind kind,
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalBlockName,
        string resolvedBlockName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken databaseIdentity,
        string reason) =>
        new(
            kind,
            key,
            canonicalBlockName,
            resolvedBlockName,
            blockId,
            databaseIdentity,
            reason,
            null);

    private static AutoCadItemLeaderBlockVariantResult Failure(
        AutoCadItemLeaderBlockVariantResultKind kind,
        AutoCadItemLeaderBlockVariantKey? key,
        string? canonicalBlockName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        new(
            kind,
            key,
            canonicalBlockName,
            null,
            null,
            databaseIdentity,
            reason,
            null);

    private static bool IsUsableObjectId(ObjectId id)
    {
        try
        {
            return !id.IsNull && id.IsValid && !id.IsErased;
        }
        catch (AcadException)
        {
            return false;
        }
    }

    private static bool IsBoundToDatabaseIdentity(
        ObjectId id,
        AutoCadDatabaseIdentityToken expectedIdentity)
    {
        try
        {
            var actualIdentity = AutoCadDatabaseIdentity.TryGetIdentity(id.Database);
            return AutoCadDatabaseIdentityPolicy.Compare(
                    expectedIdentity,
                    actualIdentity,
                    managedReferenceEquals: false)
                .IsSameDatabase;
        }
        catch (AcadException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}

internal sealed class AutoCadItemLeaderBlockVariantBatchCatalog
{
    private readonly AutoCadItemLeaderBlockVariantBatchIndex<ObjectId> _index;

    public Database Database { get; }
    public AutoCadDatabaseIdentityToken DatabaseIdentity { get; }
    public int Count => _index.Count;

    public AutoCadItemLeaderBlockVariantBatchCatalog(Database database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        DatabaseIdentity = AutoCadDatabaseIdentity.TryGetIdentity(database) ??
            throw new ArgumentException(
                "Database is disposed or has no valid native identity.",
                nameof(database));
        _index = new AutoCadItemLeaderBlockVariantBatchIndex<ObjectId>(
            DatabaseIdentity);
    }

    public bool IsBoundTo(Database database) =>
        AutoCadDatabaseIdentity.IsSame(Database, database);

    public bool TryGet(
        AutoCadItemLeaderBlockVariantKey key,
        out AutoCadItemLeaderBlockVariantResult? result)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_index.TryGet(DatabaseIdentity, key, out var entry))
        {
            var cachedEntry = entry!;
            result = AutoCadItemLeaderBlockVariantResult.Reused(
                key,
                AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key),
                cachedEntry.ResolvedBlockName,
                cachedEntry.DefinitionId,
                DatabaseIdentity,
                cachedEntry.IsCollision,
                "Reused the database-bound batch catalog entry.");
            return true;
        }

        result = null;
        return false;
    }

    public void Add(
        AutoCadItemLeaderBlockVariantKey key,
        ObjectId blockId,
        string resolvedBlockName,
        bool isCollision)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(resolvedBlockName))
        {
            throw new ArgumentException(
                "Resolved block name is required.",
                nameof(resolvedBlockName));
        }
        if (!AutoCadDatabaseIdentity.IsSame(Database, blockId))
        {
            throw new ArgumentException(
                "Block ObjectId belongs to a different database.",
                nameof(blockId));
        }

        _index.Add(
            DatabaseIdentity,
            key,
            blockId,
            resolvedBlockName,
            isCollision);
    }
}
