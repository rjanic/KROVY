using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral resolution of durable manual vs actual rafter cross-section sources.
/// Uses the acknowledged-actual snapshot to respect confirmed PreferManual / PreferActual
/// decisions and to raise a NEW conflict only when actual W×H changes.
/// Does not invent profile defaults as ExplicitManual.
/// </summary>
public static class RoofAutomaticPurlinRafterSourceRules
{
    public enum Outcome
    {
        /// <summary>No authoritative profile; UI must request manual entry.</summary>
        UnresolvedNoSource = 0,

        /// <summary>Use durable manual W×H.</summary>
        UseManual = 1,

        /// <summary>Use recovered/selected actual roof rafter W×H.</summary>
        UseActual = 2,

        /// <summary>Manual and actual differ; user must choose explicitly.</summary>
        Conflict = 3,

        /// <summary>Actual rafter recipe is ambiguous/heterogeneous.</summary>
        AmbiguousActual = 4,

        /// <summary>Persisted manual profile is incomplete or non-positive.</summary>
        InvalidManual = 5,

        /// <summary>
        /// PreferActual but no actual rafters remain; recoverable manual exists.
        /// Transition is unresolved until the user explicitly confirms manual use.
        /// Width/Height may carry provisional recoverable dimensions for preview only.
        /// </summary>
        MissingActualWithRecoverableManual = 6,
    }

    public readonly record struct Resolution(
        Outcome Outcome,
        double? WidthMm,
        double? HeightMm,
        RoofAutomaticPurlinRafterSourcePolicy EffectivePolicy,
        bool HasRecoverableManualProfile);

    public static bool AreCrossSectionsEqual(
        double leftWidthMm,
        double leftHeightMm,
        double rightWidthMm,
        double rightHeightMm)
    {
        var tolerance = RoofAutomaticPurlinPlanner.CoordinateToleranceMm;
        return Math.Abs(leftWidthMm - rightWidthMm) <= tolerance &&
               Math.Abs(leftHeightMm - rightHeightMm) <= tolerance;
    }

    public static bool TryValidateManualProfile(
        double? widthMm,
        double? heightMm,
        out RoofPurlinLayoutPersistenceError error)
    {
        error = RoofPurlinLayoutPersistenceError.None;
        if (widthMm is null && heightMm is null)
        {
            return true;
        }

        if (widthMm is null || heightMm is null)
        {
            error = RoofPurlinLayoutPersistenceError.InvalidManualRafterProfile;
            return false;
        }

        if (!IsPositiveFinite(widthMm.Value) || !IsPositiveFinite(heightMm.Value))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidManualRafterProfile;
            return false;
        }

        return true;
    }

    public static bool TryValidateAcknowledgedActual(
        RoofAutomaticPurlinAcknowledgedActualKind kind,
        double? widthMm,
        double? heightMm,
        out RoofPurlinLayoutPersistenceError error)
    {
        error = RoofPurlinLayoutPersistenceError.None;
        switch (kind)
        {
            case RoofAutomaticPurlinAcknowledgedActualKind.Unspecified:
            case RoofAutomaticPurlinAcknowledgedActualKind.NonePresent:
                if (widthMm is not null || heightMm is not null)
                {
                    error = RoofPurlinLayoutPersistenceError.InvalidAcknowledgedActualProfile;
                    return false;
                }

                return true;

            case RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile:
                if (widthMm is null || heightMm is null ||
                    !IsPositiveFinite(widthMm.Value) ||
                    !IsPositiveFinite(heightMm.Value))
                {
                    error = RoofPurlinLayoutPersistenceError.InvalidAcknowledgedActualProfile;
                    return false;
                }

                return true;

            default:
                error = RoofPurlinLayoutPersistenceError.InvalidAcknowledgedActualProfile;
                return false;
        }
    }

    public static bool HasCompleteManualProfile(RoofAutomaticPurlinLayout layout) =>
        layout.ManualRafterWidthMm is { } width &&
        layout.ManualRafterHeightMm is { } height &&
        IsPositiveFinite(width) &&
        IsPositiveFinite(height);

    public static bool IsAcknowledgedActualMatchingCurrent(
        RoofAutomaticPurlinLayout layout,
        double actualWidthMm,
        double actualHeightMm) =>
        layout.AcknowledgedActualKind ==
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile &&
        layout.AcknowledgedActualWidthMm is { } width &&
        layout.AcknowledgedActualHeightMm is { } height &&
        AreCrossSectionsEqual(width, height, actualWidthMm, actualHeightMm);

    /// <summary>
    /// Builds the acknowledgement snapshot for an explicit Apply-time source decision.
    /// </summary>
    public static (
        RoofAutomaticPurlinAcknowledgedActualKind Kind,
        double? WidthMm,
        double? HeightMm) CreateAcknowledgement(
        bool actualAvailable,
        double actualWidthMm,
        double actualHeightMm)
    {
        if (actualAvailable &&
            IsPositiveFinite(actualWidthMm) &&
            IsPositiveFinite(actualHeightMm))
        {
            return (
                RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                actualWidthMm,
                actualHeightMm);
        }

        return (RoofAutomaticPurlinAcknowledgedActualKind.NonePresent, null, null);
    }

    public static Resolution Resolve(
        RoofAutomaticPurlinLayout layout,
        bool actualAvailable,
        double actualWidthMm,
        double actualHeightMm,
        bool actualAmbiguous)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (!TryValidateManualProfile(
                layout.ManualRafterWidthMm,
                layout.ManualRafterHeightMm,
                out _))
        {
            return new Resolution(
                Outcome.InvalidManual,
                null,
                null,
                layout.RafterSourcePolicy,
                HasRecoverableManualProfile: false);
        }

        if (!TryValidateAcknowledgedActual(
                layout.AcknowledgedActualKind,
                layout.AcknowledgedActualWidthMm,
                layout.AcknowledgedActualHeightMm,
                out _))
        {
            return new Resolution(
                Outcome.InvalidManual,
                null,
                null,
                layout.RafterSourcePolicy,
                HasCompleteManualProfile(layout));
        }

        var hasManual = HasCompleteManualProfile(layout);
        var manualWidth = layout.ManualRafterWidthMm;
        var manualHeight = layout.ManualRafterHeightMm;

        if (actualAmbiguous)
        {
            return new Resolution(
                Outcome.AmbiguousActual,
                hasManual ? manualWidth : null,
                hasManual ? manualHeight : null,
                layout.RafterSourcePolicy,
                hasManual);
        }

        var hasActual = actualAvailable &&
                        IsPositiveFinite(actualWidthMm) &&
                        IsPositiveFinite(actualHeightMm);

        return layout.RafterSourcePolicy switch
        {
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual when hasManual =>
                ResolvePreferManual(
                    layout,
                    hasActual,
                    actualWidthMm,
                    actualHeightMm,
                    manualWidth!.Value,
                    manualHeight!.Value),

            RoofAutomaticPurlinRafterSourcePolicy.PreferManual =>
                hasActual
                    ? new Resolution(
                        Outcome.UseActual,
                        actualWidthMm,
                        actualHeightMm,
                        RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                        false)
                    : new Resolution(
                        Outcome.UnresolvedNoSource,
                        null,
                        null,
                        RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                        false),

            RoofAutomaticPurlinRafterSourcePolicy.PreferActual when hasActual =>
                ResolvePreferActualWithActual(
                    layout,
                    hasManual,
                    actualWidthMm,
                    actualHeightMm,
                    manualWidth,
                    manualHeight),

            // PreferActual with no actual + recoverable manual: unresolved transition.
            // Do not silently accept PreferManual; provisional W×H may be used for preview only.
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual when hasManual =>
                new Resolution(
                    Outcome.MissingActualWithRecoverableManual,
                    manualWidth,
                    manualHeight,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                    true),

            RoofAutomaticPurlinRafterSourcePolicy.PreferActual =>
                new Resolution(
                    Outcome.UnresolvedNoSource,
                    null,
                    null,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                    false),

            // Unset / legacy: never invent ExplicitManual from defaults.
            _ when hasManual && hasActual =>
                AreCrossSectionsEqual(
                    manualWidth!.Value,
                    manualHeight!.Value,
                    actualWidthMm,
                    actualHeightMm)
                    ? new Resolution(
                        Outcome.UseActual,
                        actualWidthMm,
                        actualHeightMm,
                        RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                        true)
                    : ResolveDifferingManualAndActual(
                        layout,
                        RoofAutomaticPurlinRafterSourcePolicy.Unset,
                        manualWidth.Value,
                        manualHeight.Value,
                        actualWidthMm,
                        actualHeightMm),

            _ when hasActual =>
                new Resolution(
                    Outcome.UseActual,
                    actualWidthMm,
                    actualHeightMm,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                    false),

            _ when hasManual =>
                new Resolution(
                    Outcome.UseManual,
                    manualWidth,
                    manualHeight,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                    true),

            _ =>
                new Resolution(
                    Outcome.UnresolvedNoSource,
                    null,
                    null,
                    RoofAutomaticPurlinRafterSourcePolicy.Unset,
                    false),
        };
    }

    private static Resolution ResolvePreferManual(
        RoofAutomaticPurlinLayout layout,
        bool hasActual,
        double actualWidthMm,
        double actualHeightMm,
        double manualWidthMm,
        double manualHeightMm)
    {
        if (!hasActual)
        {
            return new Resolution(
                Outcome.UseManual,
                manualWidthMm,
                manualHeightMm,
                RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                true);
        }

        // Any current actual must be reconciled against the durable acknowledgement.
        // Do NOT short-circuit on manual==actual: NonePresent / Unspecified still mean
        // "first appearance requires confirmation", and a stale ExplicitProfile must
        // raise a NEW conflict even when the new actual happens to match manual W×H.
        return ResolveDifferingManualAndActual(
            layout,
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            manualWidthMm,
            manualHeightMm,
            actualWidthMm,
            actualHeightMm);
    }

    private static Resolution ResolvePreferActualWithActual(
        RoofAutomaticPurlinLayout layout,
        bool hasManual,
        double actualWidthMm,
        double actualHeightMm,
        double? manualWidthMm,
        double? manualHeightMm)
    {
        if (hasManual &&
            !AreCrossSectionsEqual(
                manualWidthMm!.Value,
                manualHeightMm!.Value,
                actualWidthMm,
                actualHeightMm))
        {
            var differing = ResolveDifferingManualAndActual(
                layout,
                RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                manualWidthMm.Value,
                manualHeightMm.Value,
                actualWidthMm,
                actualHeightMm);
            if (differing.Outcome == Outcome.Conflict)
            {
                return differing;
            }
        }

        return new Resolution(
            Outcome.UseActual,
            actualWidthMm,
            actualHeightMm,
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            hasManual);
    }

    /// <summary>
    /// Manual and actual differ. Respect the persisted decision only when the current
    /// actual matches the acknowledged snapshot; otherwise raise a NEW conflict.
    /// Legacy Unspecified acknowledgement always requires one explicit confirmation.
    /// </summary>
    private static Resolution ResolveDifferingManualAndActual(
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinRafterSourcePolicy preferredPolicy,
        double manualWidthMm,
        double manualHeightMm,
        double actualWidthMm,
        double actualHeightMm)
    {
        if (IsAcknowledgedActualMatchingCurrent(layout, actualWidthMm, actualHeightMm))
        {
            return preferredPolicy == RoofAutomaticPurlinRafterSourcePolicy.PreferActual
                ? new Resolution(
                    Outcome.UseActual,
                    actualWidthMm,
                    actualHeightMm,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
                    true)
                : new Resolution(
                    Outcome.UseManual,
                    manualWidthMm,
                    manualHeightMm,
                    RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                    true);
        }

        // NonePresent: first appearance of actual after manual-only.
        // Unspecified: legacy schema-2 — cannot classify; require one confirmation.
        // ExplicitProfile that does not match: genuinely NEW actual section.
        return new Resolution(
            Outcome.Conflict,
            manualWidthMm,
            manualHeightMm,
            preferredPolicy,
            true);
    }

    private static bool IsPositiveFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
}
