using System;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Formats generated-plan-rebuild HardFailure detail so HOST diagnostics retain the
/// post-Create exception instead of collapsing to bare <c>Failed</c>.
/// </summary>
public static class RoofGeneratedPlanRebuildFailureRules
{
    public const string SubstageOrdinaryPhysicalReconcile = "ordinary-physical-reconcile";
    public const string SubstageAnnotations = "annotations";
    public const string SubstageVisibility = "visibility";
    public const string SubstageGroupSync = "group-sync";
    public const string SubstageReplayPlan = "override-replay";
    public const string SubstagePlanCreate = "plan-create";
    public const string SubstageUnknown = "unknown";

    public sealed class FailureDetail
    {
        public FailureDetail(
            string ownerHandle,
            string substage,
            string member,
            string handle,
            string result,
            string exceptionType,
            string exceptionMessage)
        {
            OwnerHandle = ownerHandle;
            Substage = substage;
            Member = member;
            Handle = handle;
            Result = result;
            ExceptionType = exceptionType;
            ExceptionMessage = exceptionMessage;
        }

        public string OwnerHandle { get; }
        public string Substage { get; }
        public string Member { get; }
        public string Handle { get; }
        public string Result { get; }
        public string ExceptionType { get; }
        public string ExceptionMessage { get; }

        public string ToMarkerLine() =>
            "owner=" + OwnerHandle +
            " substage=" + Substage +
            " member=" + Member +
            " handle=" + Handle +
            " result=" + Result +
            " exceptionType=" + ExceptionType +
            " exceptionMessage=" + ExceptionMessage;

        public string ToHardFailureExceptionToken() =>
            string.IsNullOrWhiteSpace(ExceptionType)
                ? ExceptionMessage
                : ExceptionType + ":" + ExceptionMessage;
    }

    public static FailureDetail FromException(
        string ownerHandle,
        Exception exception,
        string? servicePhase = null,
        string? member = null,
        string? handle = null)
    {
        if (exception is null)
            throw new ArgumentNullException(nameof(exception));

        var root = exception;
        while (root.InnerException is not null)
            root = root.InnerException;

        var substage = ResolveSubstage(servicePhase, exception.Message, root.Message);
        var resolvedMember = FirstNonEmpty(
            member,
            ExtractMember(root.Message),
            ExtractMember(exception.Message),
            "-");
        return new FailureDetail(
            string.IsNullOrWhiteSpace(ownerHandle) ? "-" : ownerHandle,
            substage,
            resolvedMember,
            string.IsNullOrWhiteSpace(handle) ? "-" : handle!,
            FirstNonEmpty(root.Message, exception.Message, "Failed"),
            root.GetType().Name,
            root.Message ?? string.Empty);
    }

    public static string ResolveSubstage(string? servicePhase, params string?[] messages)
    {
        if (!string.IsNullOrWhiteSpace(servicePhase))
        {
            if (ContainsIgnoreCase(servicePhase!, "Reconcile") ||
                ContainsIgnoreCase(servicePhase!, "ordinary-physical") ||
                ContainsIgnoreCase(servicePhase!, "CreateSolid"))
                return SubstageOrdinaryPhysicalReconcile;
            if (ContainsIgnoreCase(servicePhase!, "Annotation"))
                return SubstageAnnotations;
            if (ContainsIgnoreCase(servicePhase!, "Visibility"))
                return SubstageVisibility;
            if (ContainsIgnoreCase(servicePhase!, "Group"))
                return SubstageGroupSync;
            if (ContainsIgnoreCase(servicePhase!, "Replay") ||
                ContainsIgnoreCase(servicePhase!, "Override"))
                return SubstageReplayPlan;
            if (ContainsIgnoreCase(servicePhase!, "Create") ||
                ContainsIgnoreCase(servicePhase!, "TimberSourceLine"))
                return SubstagePlanCreate;
            return servicePhase!;
        }

        if (messages is not null)
        {
            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message))
                    continue;
                if (ContainsIgnoreCase(message!, "Ordinary physical") ||
                    ContainsIgnoreCase(message!, "CreateSolid") ||
                    ContainsIgnoreCase(message!, "rafter prism") ||
                    ContainsIgnoreCase(message!, "side profile") ||
                    ContainsIgnoreCase(message!, "GeneratedMemberKeyMismatch"))
                    return SubstageOrdinaryPhysicalReconcile;
                if (ContainsIgnoreCase(message!, "Annotation"))
                    return SubstageAnnotations;
            }
        }

        return SubstageUnknown;
    }

    public static string? ExtractMember(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        const string marker = "member=";
        var index = message!.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var start = index + marker.Length;
        var end = start;
        while (end < message.Length &&
               !char.IsWhiteSpace(message[end]) &&
               message[end] != ';' &&
               message[end] != ',')
            end++;
        var value = message.Substring(start, end - start).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ContainsIgnoreCase(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string FirstNonEmpty(params string?[] values)
    {
        if (values is null)
            return "-";
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }

        return "-";
    }
}
