using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral schema-1 validation for owner purlin-layout persistence.</summary>
public static class RoofPurlinLayoutPersistenceRules
{
    public const string BottomEdgeHeightAboveReferenceToken = "BottomEdgeHeightAboveReference";
    public const string PlanDistanceFromEaveToken = "PlanDistanceFromEave";
    public const string PlanDistanceFromRidgeToken = "PlanDistanceFromRidge";
    public const string NoReferenceRidgeToken = "None";
    public const string RidgeReferenceToken = "Ridge";
    public const string NoSeatingDepthToken = "None";
    public const string PercentOfRafterHeightToken = "PercentOfRafterHeight";
    public const string AbsoluteMmToken = "AbsoluteMm";

    public static RoofAutomaticPurlinLayoutItem ResolveWallPlatePlacement(
        RoofAutomaticPurlinLayout layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (layout.WallPlatePlacement is not null)
        {
            return layout.WallPlatePlacement with
            {
                LayoutItemId = RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            };
        }

        return CreateLegacyWallPlatePlacement(layout.WallPlateLowerEdgeHeightMm);
    }

    public static RoofAutomaticPurlinLayoutItem CreateLegacyWallPlatePlacement(
        double lowerEdgeHeightMm) =>
        new(
            RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            true,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            lowerEdgeHeightMm);

    /// <summary>
    /// Default plan-distance from eave for a new-draft wall plate (mm).
    /// </summary>
    public const double DefaultNewDraftWallPlateEaveDistanceMm = 500d;

    /// <summary>
    /// Default plan-distance from eave for the seed intermediate row (mm).
    /// </summary>
    public const double DefaultNewDraftIntermediateEaveDistanceMm = 1800d;

    /// <summary>
    /// UI new-draft defaults for an unpersisted automatic-purlin layout. Must not be
    /// used as the meaning of a missing owner layout section (<see cref="RoofAutomaticPurlinLayout.Empty"/>).
    /// </summary>
    public static RoofAutomaticPurlinLayout CreateNewDraftDefaults(
        double eaveDistanceMm = DefaultNewDraftWallPlateEaveDistanceMm,
        double seatingPercent = RoofAutomaticPurlinPlanner.DefaultWallPlateSeatingPercent,
        double wallPlateWidthMm = 140d,
        double wallPlateHeightMm = 140d,
        double ridgeWidthMm = 160d,
        double ridgeHeightMm = 220d,
        double intermediateWidthMm = 160d,
        double intermediateHeightMm = 220d,
        double intermediateEaveDistanceMm = DefaultNewDraftIntermediateEaveDistanceMm) =>
        new(
            true,
            [
                new RoofAutomaticPurlinLayoutItem(
                    RoofAutomaticPurlinLayoutItemIdentity.Create(),
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    intermediateEaveDistanceMm,
                    SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        seatingPercent),
                    WidthMm: intermediateWidthMm,
                    HeightMm: intermediateHeightMm),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 0d,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                eaveDistanceMm,
                SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: wallPlateWidthMm,
                HeightMm: wallPlateHeightMm),
            RidgeWidthMm = ridgeWidthMm,
            RidgeHeightMm = ridgeHeightMm,
            RidgeSeatingDepth = new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                seatingPercent),
        };

    public static RoofPurlinLayoutValidationResult ValidateForWrite(RoofAutomaticPurlinLayout? layout)
    {
        if (layout?.IntermediateItems is null)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        var items = new List<RoofPurlinLayoutStoredItem>(layout.IntermediateItems.Count);
        foreach (var item in layout.IntermediateItems)
        {
            if (item is null || !TryFormatPlacement(item.PlacementMode, out var placementToken))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementToken);
            }

            var ridge = item.ReferenceRidgeKey;
            if (ridge is not null &&
                (ridge.Role != RoofStructuralRole.Ridge ||
                 ridge.BoundaryEdgeIdA <= 0 ||
                 ridge.BoundaryEdgeIdA >= ridge.BoundaryEdgeIdB))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidReferenceRidge);
            }

            var ridgeToken = ridge is null ? NoReferenceRidgeToken : RidgeReferenceToken;
            var seatingToken = NoSeatingDepthToken;
            var seatingValue = 0d;
            if (item.SeatingDepth is not null)
            {
                if (!TryFormatSeating(item.SeatingDepth.Mode, out seatingToken))
                {
                    return Invalid(RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken);
                }

                seatingValue = item.SeatingDepth.Value;
            }

            items.Add(new RoofPurlinLayoutStoredItem(
                item.LayoutItemId,
                item.Enabled ? 1 : 0,
                placementToken,
                item.PlacementValueMm,
                ridgeToken,
                ridge?.BoundaryEdgeIdA ?? 0,
                ridge?.BoundaryEdgeIdB ?? 0,
                seatingToken,
                seatingValue,
                ToStoredDimension(item.WidthMm),
                ToStoredDimension(item.HeightMm)));
        }

        if (!TryNormalizeWallPlatePlacement(
                layout.WallPlateLowerEdgeHeightMm,
                layout.WallPlatePlacement,
                out var wallPlatePlacement,
                out var wallPlateLowerEdgeHeightMm,
                out var wallPlateError,
                wallPlateLowerEdgeHeightExplicit: true))
        {
            return Invalid(wallPlateError);
        }

        if (!TryValidateOptionalDimension(layout.RidgeWidthMm, out _) ||
            !TryValidateOptionalDimension(layout.RidgeHeightMm, out _) ||
            !TryValidateOptionalDimension(wallPlatePlacement.WidthMm, out _) ||
            !TryValidateOptionalDimension(wallPlatePlacement.HeightMm, out _))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidSectionDimension);
        }

        if (!RoofAutomaticPurlinRafterSourceRules.TryValidateManualProfile(
                layout.ManualRafterWidthMm,
                layout.ManualRafterHeightMm,
                out var manualError))
        {
            return Invalid(manualError);
        }

        if (layout.RafterSourcePolicy is not (
                RoofAutomaticPurlinRafterSourcePolicy.Unset or
                RoofAutomaticPurlinRafterSourcePolicy.PreferManual or
                RoofAutomaticPurlinRafterSourcePolicy.PreferActual))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidRafterSourcePolicy);
        }

        if (layout.RafterSourcePolicy == RoofAutomaticPurlinRafterSourcePolicy.PreferManual &&
            !RoofAutomaticPurlinRafterSourceRules.HasCompleteManualProfile(layout))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidManualRafterProfile);
        }

        if (!RoofAutomaticPurlinRafterSourceRules.TryValidateAcknowledgedActual(
                layout.AcknowledgedActualKind,
                layout.AcknowledgedActualWidthMm,
                layout.AcknowledgedActualHeightMm,
                out var acknowledgedError))
        {
            return Invalid(acknowledgedError);
        }

        var sectionDimensions = HasAnySectionDimension(layout, wallPlatePlacement, items)
            ? new RoofPurlinLayoutStoredSectionDimensions(
                ToStoredDimension(wallPlatePlacement.WidthMm),
                ToStoredDimension(wallPlatePlacement.HeightMm),
                ToStoredDimension(layout.RidgeWidthMm),
                ToStoredDimension(layout.RidgeHeightMm))
            : null;

        if (layout.RidgeSeatingDepth is not null &&
            (layout.RidgeSeatingDepth.Mode is not (
                 RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight or
                 RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm) ||
             !IsFinite(layout.RidgeSeatingDepth.Value) ||
             layout.RidgeSeatingDepth.Value < 0d ||
             (layout.RidgeSeatingDepth.Mode ==
                  RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
              layout.RidgeSeatingDepth.Value > 100d)))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidSeatingDepth);
        }

        var rafterProfile = HasPersistedRafterProfile(layout)
            ? new RoofPurlinLayoutStoredRafterProfile(
                (int)layout.RafterSourcePolicy,
                ToStoredDimension(layout.ManualRafterWidthMm),
                ToStoredDimension(layout.ManualRafterHeightMm),
                (int)layout.AcknowledgedActualKind,
                ToStoredDimension(layout.AcknowledgedActualWidthMm),
                ToStoredDimension(layout.AcknowledgedActualHeightMm))
            : null;

        var validated = ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            layout.RidgeEnabled ? 1 : 0,
            items,
            layout.WallPlateEnabled ? 1 : 0,
            wallPlateLowerEdgeHeightMm,
            ToStoredWallPlatePlacement(wallPlatePlacement),
            sectionDimensions,
            rafterProfile,
            wallPlateLowerEdgeHeightExplicit: true);
        if (validated.Layout is null)
        {
            return validated;
        }

        return new RoofPurlinLayoutValidationResult(
            true,
            validated.Layout with { RidgeSeatingDepth = layout.RidgeSeatingDepth },
            RoofPurlinLayoutPersistenceError.None);
    }

    /// <summary>
    /// True when optional section-dimensions trailer must be written.
    /// </summary>
    public static bool HasPersistedSectionDimensions(RoofAutomaticPurlinLayout layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var wallPlatePlacement = ResolveWallPlatePlacement(layout);
        return layout.RidgeWidthMm is not null ||
               layout.RidgeHeightMm is not null ||
               wallPlatePlacement.WidthMm is not null ||
               wallPlatePlacement.HeightMm is not null ||
               layout.IntermediateItems.Any(item =>
                   item.WidthMm is not null || item.HeightMm is not null);
    }

    /// <summary>
    /// True when schema-2/3 optional rafter-profile trailer must be written.
    /// </summary>
    public static bool HasPersistedRafterProfile(RoofAutomaticPurlinLayout layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        return layout.RafterSourcePolicy != RoofAutomaticPurlinRafterSourcePolicy.Unset ||
               layout.ManualRafterWidthMm is not null ||
               layout.ManualRafterHeightMm is not null ||
               layout.AcknowledgedActualKind !=
                   RoofAutomaticPurlinAcknowledgedActualKind.Unspecified;
    }

    /// <summary>
    /// Owner-write equivalence for Apply skip/postcondition checks. Includes every
    /// field that Encode/Decode persist for schema-1/2/3 except non-encoded
    /// <see cref="RoofAutomaticPurlinLayout.RidgeSeatingDepth"/>.
    /// </summary>
    public static bool AreEquivalentForOwnerWrite(
        RoofAutomaticPurlinLayout first,
        RoofAutomaticPurlinLayout second)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        return first.WallPlateEnabled == second.WallPlateEnabled &&
               first.RidgeEnabled == second.RidgeEnabled &&
               Math.Abs(first.WallPlateLowerEdgeHeightMm - second.WallPlateLowerEdgeHeightMm) <=
                   1e-9 &&
               NullableDimensionEquals(first.RidgeWidthMm, second.RidgeWidthMm) &&
               NullableDimensionEquals(first.RidgeHeightMm, second.RidgeHeightMm) &&
               Equals(ResolveWallPlatePlacement(first), ResolveWallPlatePlacement(second)) &&
               first.IntermediateItems.SequenceEqual(second.IntermediateItems) &&
               first.RafterSourcePolicy == second.RafterSourcePolicy &&
               NullableDimensionEquals(first.ManualRafterWidthMm, second.ManualRafterWidthMm) &&
               NullableDimensionEquals(first.ManualRafterHeightMm, second.ManualRafterHeightMm) &&
               first.AcknowledgedActualKind == second.AcknowledgedActualKind &&
               NullableDimensionEquals(
                   first.AcknowledgedActualWidthMm,
                   second.AcknowledgedActualWidthMm) &&
               NullableDimensionEquals(
                   first.AcknowledgedActualHeightMm,
                   second.AcknowledgedActualHeightMm);
    }

    private static bool NullableDimensionEquals(double? first, double? second) =>
        (first is null && second is null) ||
        (first is { } left && second is { } right && Math.Abs(left - right) <= 1e-9);

    public static double ToStoredSectionDimension(double? value) => ToStoredDimension(value);

    public static RoofPurlinLayoutValidationResult ValidateStored(
        int schemaVersion,
        int ridgeEnabledValue,
        IReadOnlyList<RoofPurlinLayoutStoredItem>? items,
        int wallPlateEnabledValue = 0,
        double wallPlateLowerEdgeHeightMm = 0d,
        RoofPurlinLayoutStoredItem? wallPlatePlacementItem = null,
        RoofPurlinLayoutStoredSectionDimensions? sectionDimensions = null,
        RoofPurlinLayoutStoredRafterProfile? rafterProfile = null,
        bool wallPlateLowerEdgeHeightExplicit = false)
    {
        if (!RoofPurlinLayoutSchema.IsSupported(schemaVersion))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.UnsupportedSchemaVersion);
        }

        if (schemaVersion == RoofPurlinLayoutSchema.Version1 && rafterProfile is not null)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.UnexpectedTrailingValue);
        }

        if (schemaVersion == RoofPurlinLayoutSchema.Version2 &&
            rafterProfile is not null &&
            rafterProfile.AcknowledgedActualKindValue !=
                (int)RoofAutomaticPurlinAcknowledgedActualKind.Unspecified)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.UnexpectedTrailingValue);
        }

        if (!TryParseBoolean(ridgeEnabledValue, out var ridgeEnabled))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidRidgeEnabled);
        }

        if (!TryParseBoolean(wallPlateEnabledValue, out var wallPlateEnabled))
        {
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidWallPlateEnabled);
        }

        if (!IsFinite(wallPlateLowerEdgeHeightMm) ||
            (wallPlatePlacementItem is null && wallPlateLowerEdgeHeightMm < 0d))
        {
            // Legacy-only lower-edge field stays non-negative. When a wall-plate
            // placement item is present, BottomEdge may mirror a signed offset here.
            return Invalid(RoofPurlinLayoutPersistenceError.InvalidWallPlateLowerEdgeHeight);
        }

        if (items is null)
        {
            return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
        }

        var layoutItems = new List<RoofAutomaticPurlinLayoutItem>(items.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
            {
                return Invalid(RoofPurlinLayoutPersistenceError.IncompletePayload);
            }

            if (string.IsNullOrWhiteSpace(item.LayoutItemId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.EmptyLayoutItemId);
            }

            if (!RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(item.LayoutItemId, out var normalizedId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.MalformedLayoutItemId);
            }

            if (!string.Equals(item.LayoutItemId, normalizedId, StringComparison.Ordinal))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.NonCanonicalLayoutItemId);
            }

            if (!ids.Add(normalizedId))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.DuplicateLayoutItemId);
            }

            if (!TryParseBoolean(item.EnabledValue, out var enabled))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidEnabled);
            }

            if (!TryParsePlacement(item.PlacementToken, out var placementMode))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementToken);
            }

            if (!IsValidPersistedPlacementValueMm(placementMode, item.PlacementValueMm))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidPlacementValue);
            }

            if (!TryParseRidgeReference(item, placementMode, out var referenceRidge))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidReferenceRidge);
            }

            if (!TryParseSeating(item, placementMode, out var seating, out var seatingError))
            {
                return Invalid(seatingError);
            }

            if (!TryParseStoredDimension(item.WidthMm, out var widthMm) ||
                !TryParseStoredDimension(item.HeightMm, out var heightMm))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidSectionDimension);
            }

            layoutItems.Add(new RoofAutomaticPurlinLayoutItem(
                normalizedId,
                enabled,
                placementMode,
                item.PlacementValueMm,
                referenceRidge,
                seating,
                widthMm,
                heightMm));
        }

        RoofAutomaticPurlinLayoutItem? parsedWallPlatePlacement = null;
        if (wallPlatePlacementItem is not null)
        {
            if (!TryParseWallPlatePlacementItem(
                    wallPlatePlacementItem,
                    out parsedWallPlatePlacement,
                    out var wallPlateParseError))
            {
                return Invalid(wallPlateParseError);
            }
        }

        if (!TryNormalizeWallPlatePlacement(
                wallPlateLowerEdgeHeightMm,
                parsedWallPlatePlacement,
                out var wallPlatePlacement,
                out var normalizedLowerEdge,
                out var wallPlateError,
                wallPlateLowerEdgeHeightExplicit))
        {
            return Invalid(wallPlateError);
        }

        double? ridgeWidthMm = null;
        double? ridgeHeightMm = null;
        if (sectionDimensions is not null)
        {
            if (!TryParseStoredDimension(sectionDimensions.WallPlateWidthMm, out var wallPlateWidthMm) ||
                !TryParseStoredDimension(sectionDimensions.WallPlateHeightMm, out var wallPlateHeightMm) ||
                !TryParseStoredDimension(sectionDimensions.RidgeWidthMm, out ridgeWidthMm) ||
                !TryParseStoredDimension(sectionDimensions.RidgeHeightMm, out ridgeHeightMm))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidSectionDimension);
            }

            wallPlatePlacement = wallPlatePlacement with
            {
                WidthMm = wallPlateWidthMm,
                HeightMm = wallPlateHeightMm,
            };
        }

        var rafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.Unset;
        double? manualRafterWidthMm = null;
        double? manualRafterHeightMm = null;
        var acknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.Unspecified;
        double? acknowledgedActualWidthMm = null;
        double? acknowledgedActualHeightMm = null;
        if (rafterProfile is not null)
        {
            if (rafterProfile.SourcePolicyValue is not (
                    (int)RoofAutomaticPurlinRafterSourcePolicy.Unset or
                    (int)RoofAutomaticPurlinRafterSourcePolicy.PreferManual or
                    (int)RoofAutomaticPurlinRafterSourcePolicy.PreferActual))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidRafterSourcePolicy);
            }

            rafterSourcePolicy = (RoofAutomaticPurlinRafterSourcePolicy)rafterProfile.SourcePolicyValue;
            if (!TryParseStoredDimension(rafterProfile.ManualWidthMm, out manualRafterWidthMm) ||
                !TryParseStoredDimension(rafterProfile.ManualHeightMm, out manualRafterHeightMm))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidManualRafterProfile);
            }

            if (!RoofAutomaticPurlinRafterSourceRules.TryValidateManualProfile(
                    manualRafterWidthMm,
                    manualRafterHeightMm,
                    out var manualError))
            {
                return Invalid(manualError);
            }

            if (rafterSourcePolicy == RoofAutomaticPurlinRafterSourcePolicy.PreferManual &&
                (manualRafterWidthMm is null || manualRafterHeightMm is null))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidManualRafterProfile);
            }

            if (rafterProfile.AcknowledgedActualKindValue is not (
                    (int)RoofAutomaticPurlinAcknowledgedActualKind.Unspecified or
                    (int)RoofAutomaticPurlinAcknowledgedActualKind.NonePresent or
                    (int)RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidAcknowledgedActualProfile);
            }

            acknowledgedActualKind =
                (RoofAutomaticPurlinAcknowledgedActualKind)rafterProfile.AcknowledgedActualKindValue;
            if (!TryParseStoredDimension(
                    rafterProfile.AcknowledgedActualWidthMm,
                    out acknowledgedActualWidthMm) ||
                !TryParseStoredDimension(
                    rafterProfile.AcknowledgedActualHeightMm,
                    out acknowledgedActualHeightMm))
            {
                return Invalid(RoofPurlinLayoutPersistenceError.InvalidAcknowledgedActualProfile);
            }

            if (!RoofAutomaticPurlinRafterSourceRules.TryValidateAcknowledgedActual(
                    acknowledgedActualKind,
                    acknowledgedActualWidthMm,
                    acknowledgedActualHeightMm,
                    out var acknowledgedError))
            {
                return Invalid(acknowledgedError);
            }
        }

        return new RoofPurlinLayoutValidationResult(
            true,
            new RoofAutomaticPurlinLayout(ridgeEnabled, Array.AsReadOnly(layoutItems.ToArray()))
            {
                WallPlateEnabled = wallPlateEnabled,
                WallPlateLowerEdgeHeightMm = normalizedLowerEdge,
                WallPlatePlacement = wallPlatePlacement,
                RidgeWidthMm = ridgeWidthMm,
                RidgeHeightMm = ridgeHeightMm,
                ManualRafterWidthMm = manualRafterWidthMm,
                ManualRafterHeightMm = manualRafterHeightMm,
                RafterSourcePolicy = rafterSourcePolicy,
                AcknowledgedActualKind = acknowledgedActualKind,
                AcknowledgedActualWidthMm = acknowledgedActualWidthMm,
                AcknowledgedActualHeightMm = acknowledgedActualHeightMm,
            },
            RoofPurlinLayoutPersistenceError.None);
    }

    private static bool TryNormalizeWallPlatePlacement(
        double wallPlateLowerEdgeHeightMm,
        RoofAutomaticPurlinLayoutItem? wallPlatePlacement,
        out RoofAutomaticPurlinLayoutItem normalized,
        out double normalizedLowerEdgeHeightMm,
        out RoofPurlinLayoutPersistenceError error,
        bool wallPlateLowerEdgeHeightExplicit = false)
    {
        error = RoofPurlinLayoutPersistenceError.None;
        normalizedLowerEdgeHeightMm = wallPlateLowerEdgeHeightMm;
        if (wallPlatePlacement is null)
        {
            if (!IsFinite(wallPlateLowerEdgeHeightMm) || wallPlateLowerEdgeHeightMm < 0d)
            {
                error = RoofPurlinLayoutPersistenceError.InvalidWallPlateLowerEdgeHeight;
                normalized = CreateLegacyWallPlatePlacement(0d);
                return false;
            }

            normalized = CreateLegacyWallPlatePlacement(wallPlateLowerEdgeHeightMm);
            return true;
        }

        if (!TryValidateWallPlatePlacementItem(wallPlatePlacement, out error))
        {
            normalized = wallPlatePlacement;
            return false;
        }

        normalized = wallPlatePlacement with
        {
            LayoutItemId = RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            Enabled = true,
        };
        if (normalized.PlacementMode ==
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            if (wallPlateLowerEdgeHeightExplicit)
            {
                // Explicit payload / in-memory LowerEdge is authoritative — including
                // Place=0 with absolute bootstrap stash, and Place=0 with LowerEdge=0.
                if (!IsFinite(wallPlateLowerEdgeHeightMm))
                {
                    error = RoofPurlinLayoutPersistenceError.InvalidWallPlateLowerEdgeHeight;
                    return false;
                }

                normalizedLowerEdgeHeightMm = wallPlateLowerEdgeHeightMm;
            }
            else if (Math.Abs(normalized.PlacementValueMm) <= 1e-9 &&
                     IsFinite(wallPlateLowerEdgeHeightMm) &&
                     Math.Abs(wallPlateLowerEdgeHeightMm) > 1e-9)
            {
                // Defensive in-memory path before encode: keep stash when Place=0.
                normalizedLowerEdgeHeightMm = wallPlateLowerEdgeHeightMm;
            }
            else
            {
                // Legacy full-placement trailer without LowerEdge Real: reconstruct
                // from Place (historical behavior).
                normalizedLowerEdgeHeightMm = normalized.PlacementValueMm;
            }
        }
        else
        {
            normalizedLowerEdgeHeightMm = 0d;
        }

        return true;
    }

    private static bool TryParseWallPlatePlacementItem(
        RoofPurlinLayoutStoredItem item,
        out RoofAutomaticPurlinLayoutItem placement,
        out RoofPurlinLayoutPersistenceError error)
    {
        placement = null!;
        error = RoofPurlinLayoutPersistenceError.None;
        if (!TryParsePlacement(item.PlacementToken, out var placementMode))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidPlacementToken;
            return false;
        }

        if (!IsValidPersistedPlacementValueMm(placementMode, item.PlacementValueMm))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidPlacementValue;
            return false;
        }

        if (!TryParseRidgeReference(item, placementMode, out var referenceRidge))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidReferenceRidge;
            return false;
        }

        if (!TryParseSeating(item, placementMode, out var seating, out error))
        {
            return false;
        }

        placement = new RoofAutomaticPurlinLayoutItem(
            RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            true,
            placementMode,
            item.PlacementValueMm,
            referenceRidge,
            seating,
            null,
            null);
        return true;
    }

    private static bool TryValidateWallPlatePlacementItem(
        RoofAutomaticPurlinLayoutItem item,
        out RoofPurlinLayoutPersistenceError error)
    {
        error = RoofPurlinLayoutPersistenceError.None;
        if (!TryFormatPlacement(item.PlacementMode, out _))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidPlacementToken;
            return false;
        }

        if (!IsValidPersistedPlacementValueMm(item.PlacementMode, item.PlacementValueMm))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidPlacementValue;
            return false;
        }

        var ridge = item.ReferenceRidgeKey;
        if (item.PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge)
        {
            if (ridge is not null &&
                (ridge.Role != RoofStructuralRole.Ridge ||
                 ridge.BoundaryEdgeIdA <= 0 ||
                 ridge.BoundaryEdgeIdA >= ridge.BoundaryEdgeIdB))
            {
                error = RoofPurlinLayoutPersistenceError.InvalidReferenceRidge;
                return false;
            }
        }
        else if (ridge is not null)
        {
            error = RoofPurlinLayoutPersistenceError.InvalidReferenceRidge;
            return false;
        }

        if (item.PlacementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
            item.SeatingDepth is null)
        {
            return TryValidateOptionalDimension(item.WidthMm, out error) &&
                   TryValidateOptionalDimension(item.HeightMm, out error);
        }

        if (item.SeatingDepth is null ||
            !TryFormatSeating(item.SeatingDepth.Mode, out _) ||
            !IsFinite(item.SeatingDepth.Value) ||
            item.SeatingDepth.Value < 0d ||
            (item.SeatingDepth.Mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
             item.SeatingDepth.Value > 100d))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepth;
            return false;
        }

        if (!TryValidateOptionalDimension(item.WidthMm, out error) ||
            !TryValidateOptionalDimension(item.HeightMm, out error))
        {
            return false;
        }

        return true;
    }

    public static RoofPurlinLayoutStoredItem ToStoredWallPlatePlacement(
        RoofAutomaticPurlinLayoutItem item)
    {
        TryFormatPlacement(item.PlacementMode, out var placementToken);
        var ridge = item.ReferenceRidgeKey;
        var seatingToken = NoSeatingDepthToken;
        var seatingValue = 0d;
        if (item.SeatingDepth is not null)
        {
            TryFormatSeating(item.SeatingDepth.Mode, out seatingToken);
            seatingValue = item.SeatingDepth.Value;
        }

        return new RoofPurlinLayoutStoredItem(
            RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            1,
            placementToken,
            item.PlacementValueMm,
            ridge is null ? NoReferenceRidgeToken : RidgeReferenceToken,
            ridge?.BoundaryEdgeIdA ?? 0,
            ridge?.BoundaryEdgeIdB ?? 0,
            seatingToken,
            seatingValue,
            ToStoredDimension(item.WidthMm),
            ToStoredDimension(item.HeightMm));
    }

    private static bool TryParseRidgeReference(
        RoofPurlinLayoutStoredItem item,
        RoofAutomaticPurlinPlacementMode placementMode,
        out RoofStructuralLogicalKey? reference)
    {
        reference = null;
        if (string.Equals(item.ReferenceRidgeRoleToken, NoReferenceRidgeToken, StringComparison.Ordinal))
        {
            return item.ReferenceRidgeBoundaryEdgeIdA == 0 && item.ReferenceRidgeBoundaryEdgeIdB == 0;
        }

        if (placementMode != RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge ||
            !string.Equals(item.ReferenceRidgeRoleToken, RidgeReferenceToken, StringComparison.Ordinal) ||
            item.ReferenceRidgeBoundaryEdgeIdA <= 0 ||
            item.ReferenceRidgeBoundaryEdgeIdA >= item.ReferenceRidgeBoundaryEdgeIdB)
        {
            return false;
        }

        reference = new RoofStructuralLogicalKey(
            RoofStructuralRole.Ridge,
            item.ReferenceRidgeBoundaryEdgeIdA,
            item.ReferenceRidgeBoundaryEdgeIdB);
        return true;
    }

    private static bool TryParseSeating(
        RoofPurlinLayoutStoredItem item,
        RoofAutomaticPurlinPlacementMode placementMode,
        out RoofAutomaticPurlinSeatingDepth? seating,
        out RoofPurlinLayoutPersistenceError error)
    {
        seating = null;
        error = RoofPurlinLayoutPersistenceError.None;
        if (string.Equals(item.SeatingDepthToken, NoSeatingDepthToken, StringComparison.Ordinal) &&
            item.SeatingDepthValue == 0d)
        {
            // Legacy BottomEdge rows may omit seating; distance modes still require it below.
            if (placementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
            {
                return true;
            }

            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken;
            return false;
        }

        if (!TryParseSeatingToken(item.SeatingDepthToken, out var mode))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken;
            return false;
        }

        if (!IsFinite(item.SeatingDepthValue) || item.SeatingDepthValue < 0d ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
             item.SeatingDepthValue > 100d))
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSeatingDepth;
            return false;
        }

        seating = new RoofAutomaticPurlinSeatingDepth(mode, item.SeatingDepthValue);
        return true;
    }

    private static bool TryParsePlacement(string token, out RoofAutomaticPurlinPlacementMode mode)
    {
        mode = token switch
        {
            BottomEdgeHeightAboveReferenceToken => RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            PlanDistanceFromEaveToken => RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            PlanDistanceFromRidgeToken => RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            _ => 0,
        };
        return mode != 0;
    }

    private static bool TryFormatPlacement(RoofAutomaticPurlinPlacementMode mode, out string token)
    {
        token = mode switch
        {
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference => BottomEdgeHeightAboveReferenceToken,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave => PlanDistanceFromEaveToken,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge => PlanDistanceFromRidgeToken,
            _ => string.Empty,
        };
        return token.Length > 0;
    }

    /// <summary>
    /// BottomEdge is a signed offset relative to the selected datum (negative = below).
    /// Physical roof bounds are enforced by the planner. Plan-distance modes allow
    /// zero (along the reference edge) and require non-negative distances.
    /// </summary>
    public static bool IsValidPlacementValueMm(
        RoofAutomaticPurlinPlacementMode mode,
        double placementValueMm)
    {
        if (!IsFinite(placementValueMm))
        {
            return false;
        }

        return mode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
               placementValueMm >= 0d;
    }

    /// <summary>
    /// Persistence of plan-distance rows historically required a strictly positive
    /// distance. BottomEdge may persist any finite signed offset.
    /// </summary>
    public static bool IsValidPersistedPlacementValueMm(
        RoofAutomaticPurlinPlacementMode mode,
        double placementValueMm)
    {
        if (!IsFinite(placementValueMm))
        {
            return false;
        }

        return mode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
               placementValueMm > 0d;
    }

    private static bool TryParseSeatingToken(string token, out RoofAutomaticPurlinSeatingDepthMode mode)
    {
        mode = token switch
        {
            PercentOfRafterHeightToken => RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            AbsoluteMmToken => RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
            _ => 0,
        };
        return mode != 0;
    }

    private static bool TryFormatSeating(RoofAutomaticPurlinSeatingDepthMode mode, out string token)
    {
        token = mode switch
        {
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight => PercentOfRafterHeightToken,
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm => AbsoluteMmToken,
            _ => string.Empty,
        };
        return token.Length > 0;
    }

    private static bool TryParseBoolean(int value, out bool parsed)
    {
        parsed = value == 1;
        return value is 0 or 1;
    }

    private static bool HasAnySectionDimension(
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinLayoutItem wallPlatePlacement,
        IReadOnlyList<RoofPurlinLayoutStoredItem> items) =>
        layout.RidgeWidthMm is not null ||
        layout.RidgeHeightMm is not null ||
        wallPlatePlacement.WidthMm is not null ||
        wallPlatePlacement.HeightMm is not null ||
        items.Any(item => item.WidthMm > 0d || item.HeightMm > 0d);

    private static double ToStoredDimension(double? value) =>
        value is { } dimension && dimension > 0d ? dimension : 0d;

    private static bool TryValidateOptionalDimension(
        double? value,
        out RoofPurlinLayoutPersistenceError error)
    {
        error = RoofPurlinLayoutPersistenceError.None;
        if (value is null)
        {
            return true;
        }

        if (!IsFinite(value.Value) || value.Value <= 0d)
        {
            error = RoofPurlinLayoutPersistenceError.InvalidSectionDimension;
            return false;
        }

        return true;
    }

    private static bool TryParseStoredDimension(double stored, out double? value)
    {
        value = null;
        if (!IsFinite(stored) || stored < 0d)
        {
            return false;
        }

        if (stored == 0d)
        {
            return true;
        }

        value = stored;
        return true;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofPurlinLayoutValidationResult Invalid(RoofPurlinLayoutPersistenceError error) =>
        new(false, null, error);
}
