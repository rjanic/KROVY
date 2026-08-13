using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Maps AutoCAD PromptKeyword / GetKeywords results to AK_LABEL intentions.
/// Accepts global names, local matching tokens, display labels, and single-letter
/// initials of the local tokens (AutoCAD unique-prefix shortcuts).
/// </summary>
public static class AkLabelIntentionPromptRules
{
    public const string GlobalMissing = "Missing";
    public const string GlobalSelect = "Select";
    public const string GlobalAll = "All";

    /// <summary>
    /// Alternate registered global for All when <see cref="GlobalAll"/>'s initial
    /// ('A') would collide with another keyword's local initial (DE Auswählen).
    /// AutoCAD uniqueness spans global and local names together.
    /// </summary>
    public const string GlobalAllDisambiguated = "ResetAll";

    public static AkLabelIntention Parse(
        string? rawResult,
        bool isNoneOrEmpty,
        string missingLocal,
        string selectLocal,
        string allLocal,
        string? missingDisplay = null,
        string? selectDisplay = null,
        string? allDisplay = null)
    {
        if (isNoneOrEmpty || string.IsNullOrWhiteSpace(rawResult))
        {
            return AkLabelIntention.MissingOnly;
        }

        var value = rawResult!.Trim();

        // Prefer All before Select so Restore-all tokens cannot fall through to Select.
        if (MatchesOption(value, GlobalAll, allLocal, allDisplay) ||
            MatchesOption(value, GlobalAllDisambiguated, allLocal, allDisplay))
        {
            return AkLabelIntention.ResetAll;
        }

        if (MatchesOption(value, GlobalSelect, selectLocal, selectDisplay))
        {
            return AkLabelIntention.ResetSelected;
        }

        if (MatchesOption(value, GlobalMissing, missingLocal, missingDisplay))
        {
            return AkLabelIntention.MissingOnly;
        }

        // Unknown token — keep safe default (do not invent Select).
        return AkLabelIntention.MissingOnly;
    }

    /// <summary>
    /// Chooses the AutoCAD-registered global name for All.
    /// When a select local begins with 'A' (e.g. German Auswählen), registering
    /// global <c>All</c> creates an A/A prefix collision and AutoCAD keeps only
    /// Select working. Use <see cref="GlobalAllDisambiguated"/> in that case.
    /// </summary>
    public static string ResolveRegisteredAllGlobal(string selectLocal) =>
        HasAllGlobalSelectLocalCollision(selectLocal)
            ? GlobalAllDisambiguated
            : GlobalAll;

    /// <summary>
    /// True when Select's local initial equals global <see cref="GlobalAll"/>'s
    /// initial ('A'), which AutoCAD treats as a keyword-prefix collision.
    /// </summary>
    public static bool HasAllGlobalSelectLocalCollision(string selectLocal) =>
        !string.IsNullOrWhiteSpace(selectLocal) &&
        GetLocalInitial(selectLocal) == GetLocalInitial(GlobalAll);

    /// <summary>
    /// AutoCAD keyword matching requires unique first letters across local tokens.
    /// </summary>
    public static bool HaveUniqueLocalInitials(
        string missingLocal,
        string selectLocal,
        string allLocal)
    {
        var missing = GetLocalInitial(missingLocal);
        var select = GetLocalInitial(selectLocal);
        var all = GetLocalInitial(allLocal);
        return missing != select && missing != all && select != all;
    }

    /// <summary>
    /// After resolving the registered All global, Select's local initial must not
    /// collide with that global initial (AutoCAD global+local uniqueness).
    /// </summary>
    public static bool HaveUniqueRegisteredAllInitial(string selectLocal)
    {
        var registeredAllGlobal = ResolveRegisteredAllGlobal(selectLocal);
        return GetLocalInitial(selectLocal) != GetLocalInitial(registeredAllGlobal);
    }

    public static char GetLocalInitial(string localKeyword)
    {
        if (string.IsNullOrWhiteSpace(localKeyword))
        {
            throw new ArgumentException(
                "Local keyword must be non-empty.",
                nameof(localKeyword));
        }

        return char.ToUpperInvariant(localKeyword.Trim()[0]);
    }

    private static bool MatchesOption(
        string value,
        string globalName,
        string localName,
        string? displayName)
    {
        if (string.Equals(value, globalName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(localName))
        {
            var local = localName.Trim();
            if (string.Equals(value, local, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Single-letter AutoCAD unique-prefix shortcut (C / V / O, …).
            if (value.Length == 1 &&
                char.ToUpperInvariant(value[0]) == GetLocalInitial(local))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var display = displayName!;
        return string.Equals(value, display.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
