using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadTextStyleResolutionKind
{
    Requested,
    StandardFallback,
    CurrentFallback,
    FirstCompatibleFallback,
    NoCompatibleStyle,
}

internal enum AutoCadTextStyleRequestStatus
{
    NotRequested,
    Compatible,
    Missing,
    Incompatible,
}

internal sealed record AutoCadTextStylePolicyDescriptor(
    string CanonicalName,
    bool IsValid,
    bool IsErased,
    double TextSize,
    bool IsAnnotative,
    bool IsCurrent);

internal sealed record AutoCadTextStylePolicyEntry(
    string CanonicalName,
    bool IsCurrent);

internal sealed class AutoCadTextStylePolicyCatalog
{
    private static readonly StringComparer NameComparer =
        StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, AutoCadTextStylePolicyEntry>
        _compatibleByName;
    private readonly HashSet<string> _knownNames;

    public IReadOnlyList<AutoCadTextStylePolicyEntry> CompatibleStyles { get; }

    public AutoCadTextStylePolicyCatalog(
        IEnumerable<AutoCadTextStylePolicyDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        _compatibleByName = new Dictionary<string, AutoCadTextStylePolicyEntry>(
            NameComparer);
        _knownNames = new HashSet<string>(NameComparer);

        var compatible = new List<AutoCadTextStylePolicyEntry>();
        var orderedDescriptors = descriptors
            .Where(IsUsableNamedRecord)
            .OrderBy(descriptor => descriptor.CanonicalName, NameComparer)
            .ThenByDescending(descriptor => descriptor.IsCurrent)
            .ThenBy(descriptor => descriptor.CanonicalName, StringComparer.Ordinal);

        foreach (var descriptor in orderedDescriptors)
        {
            _knownNames.Add(descriptor.CanonicalName);
            if (!IsCompatible(descriptor) ||
                _compatibleByName.ContainsKey(descriptor.CanonicalName))
            {
                continue;
            }

            var entry = new AutoCadTextStylePolicyEntry(
                descriptor.CanonicalName,
                descriptor.IsCurrent);
            _compatibleByName.Add(entry.CanonicalName, entry);
            compatible.Add(entry);
        }

        CompatibleStyles = compatible.AsReadOnly();
    }

    public bool TryFindCompatible(
        string name,
        out AutoCadTextStylePolicyEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _compatibleByName.TryGetValue(name, out entry);
    }

    public bool ContainsKnownName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _knownNames.Contains(name);
    }

    public AutoCadTextStylePolicyEntry? CurrentCompatibleStyle =>
        CompatibleStyles.FirstOrDefault(entry => entry.IsCurrent);

    public AutoCadTextStylePolicyEntry? FirstCompatibleStyle =>
        CompatibleStyles.FirstOrDefault();

    private static bool IsUsableNamedRecord(
        AutoCadTextStylePolicyDescriptor descriptor) =>
        descriptor.IsValid &&
        !descriptor.IsErased &&
        !string.IsNullOrWhiteSpace(descriptor.CanonicalName);

    private static bool IsCompatible(
        AutoCadTextStylePolicyDescriptor descriptor) =>
        descriptor.TextSize == 0d && !descriptor.IsAnnotative;
}

internal sealed record AutoCadTextStyleSelection
{
    public string? RequestedTextStyleName { get; }
    public string? ResolvedTextStyleName { get; }
    public AutoCadTextStyleResolutionKind ResolutionKind { get; }
    public AutoCadTextStyleRequestStatus RequestStatus { get; }
    public bool IsFallback => ResolutionKind is
        AutoCadTextStyleResolutionKind.StandardFallback or
        AutoCadTextStyleResolutionKind.CurrentFallback or
        AutoCadTextStyleResolutionKind.FirstCompatibleFallback;

    public bool HasCompatibleStyle => ResolvedTextStyleName is not null;

    private AutoCadTextStyleSelection(
        string? requestedTextStyleName,
        string? resolvedTextStyleName,
        AutoCadTextStyleResolutionKind resolutionKind,
        AutoCadTextStyleRequestStatus requestStatus)
    {
        RequestedTextStyleName = requestedTextStyleName;
        ResolvedTextStyleName = resolvedTextStyleName;
        ResolutionKind = resolutionKind;
        RequestStatus = requestStatus;
    }

    public static AutoCadTextStyleSelection Resolved(
        string? requestedName,
        AutoCadTextStylePolicyEntry resolved,
        AutoCadTextStyleResolutionKind resolutionKind,
        AutoCadTextStyleRequestStatus requestStatus)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (!Enum.IsDefined(resolutionKind) ||
            !Enum.IsDefined(requestStatus) ||
            string.IsNullOrWhiteSpace(resolved.CanonicalName) ||
            requestedName is not null && string.IsNullOrWhiteSpace(requestedName) ||
            resolutionKind == AutoCadTextStyleResolutionKind.NoCompatibleStyle ||
            requestStatus == AutoCadTextStyleRequestStatus.Compatible !=
                (resolutionKind == AutoCadTextStyleResolutionKind.Requested) ||
            requestStatus == AutoCadTextStyleRequestStatus.NotRequested !=
                (requestedName is null))
        {
            throw new ArgumentException(
                "Resolved text-style selection has an inconsistent state.");
        }

        if (resolutionKind == AutoCadTextStyleResolutionKind.Requested &&
            !string.Equals(
                requestedName,
                resolved.CanonicalName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Requested resolution must resolve the requested style.");
        }

        var isStandard = string.Equals(
            resolved.CanonicalName,
            TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            StringComparison.OrdinalIgnoreCase);
        if (resolutionKind == AutoCadTextStyleResolutionKind.StandardFallback &&
                !isStandard ||
            resolutionKind == AutoCadTextStyleResolutionKind.CurrentFallback &&
                (!resolved.IsCurrent ||
                    isStandard &&
                    requestStatus != AutoCadTextStyleRequestStatus.NotRequested) ||
            resolutionKind == AutoCadTextStyleResolutionKind.FirstCompatibleFallback &&
                (resolved.IsCurrent || isStandard))
        {
            throw new ArgumentException(
                "Fallback resolution kind does not match the resolved style.");
        }

        return new AutoCadTextStyleSelection(
            requestedName,
            resolved.CanonicalName,
            resolutionKind,
            requestStatus);
    }

    public static AutoCadTextStyleSelection NoCompatibleStyle(
        string? requestedName,
        AutoCadTextStyleRequestStatus requestStatus)
    {
        if (!Enum.IsDefined(requestStatus) ||
            requestStatus == AutoCadTextStyleRequestStatus.Compatible ||
            requestedName is not null && string.IsNullOrWhiteSpace(requestedName) ||
            requestStatus == AutoCadTextStyleRequestStatus.NotRequested !=
                (requestedName is null))
        {
            throw new ArgumentException(
                "Unresolved text-style selection has an inconsistent state.");
        }

        return new AutoCadTextStyleSelection(
            requestedName,
            null,
            AutoCadTextStyleResolutionKind.NoCompatibleStyle,
            requestStatus);
    }
}

internal sealed class AutoCadTextStyleSelectionPolicy
{
    private readonly AutoCadTextStylePolicyCatalog _catalog;

    public AutoCadTextStyleSelectionPolicy(AutoCadTextStylePolicyCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public AutoCadTextStyleSelection ResolveExplicit(string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);

        if (_catalog.TryFindCompatible(requestedName, out var requested))
        {
            return AutoCadTextStyleSelection.Resolved(
                requestedName,
                requested!,
                AutoCadTextStyleResolutionKind.Requested,
                AutoCadTextStyleRequestStatus.Compatible);
        }

        var requestStatus = _catalog.ContainsKnownName(requestedName)
            ? AutoCadTextStyleRequestStatus.Incompatible
            : AutoCadTextStyleRequestStatus.Missing;

        if (_catalog.TryFindCompatible(
                TimberAnnotationTextSettingsRules.DefaultTextStyleName,
                out var standard))
        {
            return AutoCadTextStyleSelection.Resolved(
                requestedName,
                standard!,
                AutoCadTextStyleResolutionKind.StandardFallback,
                requestStatus);
        }

        var current = _catalog.CurrentCompatibleStyle;
        if (current is not null)
        {
            return AutoCadTextStyleSelection.Resolved(
                requestedName,
                current,
                AutoCadTextStyleResolutionKind.CurrentFallback,
                requestStatus);
        }

        return ResolveFirstOrNone(requestedName, requestStatus);
    }

    public AutoCadTextStyleSelection ResolveLegacy()
    {
        var current = _catalog.CurrentCompatibleStyle;
        if (current is not null)
        {
            return AutoCadTextStyleSelection.Resolved(
                null,
                current,
                AutoCadTextStyleResolutionKind.CurrentFallback,
                AutoCadTextStyleRequestStatus.NotRequested);
        }

        if (_catalog.TryFindCompatible(
                TimberAnnotationTextSettingsRules.DefaultTextStyleName,
                out var standard))
        {
            return AutoCadTextStyleSelection.Resolved(
                null,
                standard!,
                AutoCadTextStyleResolutionKind.StandardFallback,
                AutoCadTextStyleRequestStatus.NotRequested);
        }

        return ResolveFirstOrNone(
            null,
            AutoCadTextStyleRequestStatus.NotRequested);
    }

    private AutoCadTextStyleSelection ResolveFirstOrNone(
        string? requestedName,
        AutoCadTextStyleRequestStatus requestStatus)
    {
        var first = _catalog.FirstCompatibleStyle;
        return first is not null
            ? AutoCadTextStyleSelection.Resolved(
                requestedName,
                first,
                AutoCadTextStyleResolutionKind.FirstCompatibleFallback,
                requestStatus)
            : AutoCadTextStyleSelection.NoCompatibleStyle(
                requestedName,
                requestStatus);
    }
}

internal sealed record AutoCadTextStyleCatalogEntry(
    string CanonicalName,
    ObjectId TextStyleId,
    bool IsCurrent);

internal sealed record AutoCadTextStyleDescriptor(
    string CanonicalName,
    ObjectId TextStyleId,
    bool IsValid,
    bool IsErased,
    double TextSize,
    bool IsAnnotative,
    bool IsCurrent)
{
    public AutoCadTextStylePolicyDescriptor ToPolicyDescriptor() =>
        new(
            CanonicalName,
            IsValid,
            IsErased,
            TextSize,
            IsAnnotative,
            IsCurrent);
}

internal sealed class AutoCadTextStyleCatalog
{
    private readonly Dictionary<string, AutoCadTextStyleCatalogEntry>
        _compatibleByName;

    public Database Database { get; }
    public IReadOnlyList<AutoCadTextStyleCatalogEntry> CompatibleStyles { get; }
    public AutoCadTextStylePolicyCatalog PolicyCatalog { get; }

    private AutoCadTextStyleCatalog(
        Database database,
        IEnumerable<AutoCadTextStyleDescriptor> descriptors)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(descriptors);
        var captured = descriptors
            .Where(descriptor => IsBoundToDatabase(descriptor, database))
            .ToArray();
        PolicyCatalog = new AutoCadTextStylePolicyCatalog(
            captured.Select(descriptor => descriptor.ToPolicyDescriptor()));
        _compatibleByName = new Dictionary<string, AutoCadTextStyleCatalogEntry>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var policyEntry in PolicyCatalog.CompatibleStyles)
        {
            var descriptor = captured.First(candidate =>
                string.Equals(
                    candidate.CanonicalName,
                    policyEntry.CanonicalName,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.IsValid &&
                !candidate.IsErased &&
                candidate.TextSize == 0d &&
                !candidate.IsAnnotative &&
                candidate.IsCurrent == policyEntry.IsCurrent);
            _compatibleByName.Add(
                policyEntry.CanonicalName,
                new AutoCadTextStyleCatalogEntry(
                    policyEntry.CanonicalName,
                    descriptor.TextStyleId,
                    policyEntry.IsCurrent));
        }

        CompatibleStyles = Array.AsReadOnly(
            PolicyCatalog.CompatibleStyles
                .Select(entry => _compatibleByName[entry.CanonicalName])
                .ToArray());
    }

    public static AutoCadTextStyleCatalog Create(
        Database database,
        IEnumerable<AutoCadTextStyleDescriptor> descriptors) =>
        new(database, descriptors);

    public AutoCadTextStyleCatalogEntry? FindCompatible(string canonicalName) =>
        _compatibleByName.TryGetValue(canonicalName, out var entry)
            ? entry
            : null;

    private static bool IsBoundToDatabase(
        AutoCadTextStyleDescriptor descriptor,
        Database database)
    {
        try
        {
            return descriptor.IsValid &&
                !descriptor.IsErased &&
                !descriptor.TextStyleId.IsNull &&
                descriptor.TextStyleId.IsValid &&
                !descriptor.TextStyleId.IsErased &&
                ReferenceEquals(descriptor.TextStyleId.Database, database);
        }
        catch (AcadException)
        {
            return false;
        }
    }
}

internal sealed record AutoCadTextStyleResolution
{
    public string? RequestedTextStyleName { get; }
    public string? ResolvedTextStyleName { get; }
    public ObjectId? ResolvedTextStyleId { get; }
    public AutoCadTextStyleResolutionKind ResolutionKind { get; }
    public AutoCadTextStyleRequestStatus RequestStatus { get; }
    public bool IsFallback => ResolutionKind is
        AutoCadTextStyleResolutionKind.StandardFallback or
        AutoCadTextStyleResolutionKind.CurrentFallback or
        AutoCadTextStyleResolutionKind.FirstCompatibleFallback;
    public bool HasCompatibleStyle => ResolvedTextStyleId.HasValue;

    private AutoCadTextStyleResolution(
        AutoCadTextStyleSelection selection,
        AutoCadTextStyleCatalogEntry? resolved,
        Database database)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(database);
        if (selection.HasCompatibleStyle != (resolved is not null) ||
            resolved is not null &&
            !string.Equals(
                selection.ResolvedTextStyleName,
                resolved.CanonicalName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Text-style selection and host resolution do not match.",
                nameof(resolved));
        }

        if (resolved is not null && !IsUsableInDatabase(resolved, database))
        {
            throw new ArgumentException(
                "Resolved text style is invalid or belongs to another database.",
                nameof(resolved));
        }

        RequestedTextStyleName = selection.RequestedTextStyleName;
        ResolvedTextStyleName = resolved?.CanonicalName;
        ResolvedTextStyleId = resolved?.TextStyleId;
        ResolutionKind = selection.ResolutionKind;
        RequestStatus = selection.RequestStatus;
    }

    public static AutoCadTextStyleResolution Create(
        AutoCadTextStyleSelection selection,
        AutoCadTextStyleCatalogEntry? resolved,
        Database database) =>
        new(selection, resolved, database);

    private static bool IsUsableInDatabase(
        AutoCadTextStyleCatalogEntry resolved,
        Database database)
    {
        try
        {
            return !resolved.TextStyleId.IsNull &&
                resolved.TextStyleId.IsValid &&
                !resolved.TextStyleId.IsErased &&
                ReferenceEquals(resolved.TextStyleId.Database, database);
        }
        catch (AcadException)
        {
            return false;
        }
    }
}

/// <summary>
/// Resolves text styles only from a catalog captured for the current database
/// transaction. It never creates styles or mutates the drawing.
/// </summary>
internal sealed class AutoCadTextStyleResolver
{
    private readonly AutoCadTextStyleCatalog _catalog;
    private readonly AutoCadTextStyleSelectionPolicy _selectionPolicy;

    public AutoCadTextStyleResolver(AutoCadTextStyleCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _selectionPolicy = new AutoCadTextStyleSelectionPolicy(
            catalog.PolicyCatalog);
    }

    public Database Database => _catalog.Database;

    public AutoCadTextStyleResolution ResolveExplicit(string requestedName) =>
        Resolve(_selectionPolicy.ResolveExplicit(requestedName));

    public AutoCadTextStyleResolution ResolveLegacy() =>
        Resolve(_selectionPolicy.ResolveLegacy());

    public static AutoCadTextStyleCatalog ReadCatalog(
        Database database,
        Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var descriptors = new List<AutoCadTextStyleDescriptor>();
        try
        {
            if (transaction.GetObject(
                    database.TextStyleTableId,
                    OpenMode.ForRead,
                    false) is not TextStyleTable table)
            {
                return AutoCadTextStyleCatalog.Create(database, descriptors);
            }

            var currentStyleId = database.Textstyle;
            foreach (ObjectId id in table)
            {
                TryAddDescriptor(
                    descriptors,
                    database,
                    transaction,
                    id,
                    currentStyleId);
            }
        }
        catch (AcadException)
        {
            return AutoCadTextStyleCatalog.Create(database, descriptors);
        }

        return AutoCadTextStyleCatalog.Create(database, descriptors);
    }

    private AutoCadTextStyleResolution Resolve(
        AutoCadTextStyleSelection selection)
    {
        var resolved = selection.ResolvedTextStyleName is null
            ? null
            : _catalog.FindCompatible(selection.ResolvedTextStyleName);
        return AutoCadTextStyleResolution.Create(
            selection,
            resolved,
            _catalog.Database);
    }

    private static void TryAddDescriptor(
        ICollection<AutoCadTextStyleDescriptor> descriptors,
        Database database,
        Transaction transaction,
        ObjectId id,
        ObjectId currentStyleId)
    {
        try
        {
            if (!AutoCadObjectIdAccess.TryGetObject<TextStyleTableRecord>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var record,
                    database))
            {
                return;
            }

            descriptors.Add(new AutoCadTextStyleDescriptor(
                record!.Name,
                id,
                id.IsValid,
                record.IsErased,
                record.TextSize,
                record.Annotative == AnnotativeStates.True,
                id == currentStyleId));
        }
        catch (AcadException)
        {
            // A disappearing or malformed table record is simply unavailable.
        }
    }
}
