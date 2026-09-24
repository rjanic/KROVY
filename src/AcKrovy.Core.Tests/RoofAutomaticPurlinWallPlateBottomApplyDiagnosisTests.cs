using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;
using Xunit.Abstractions;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST Apply failure diagnosis after WallPlateBottom Place=0 / LowerEdge stash split.
/// Read-only probes — no production fix.
/// </summary>
public sealed class RoofAutomaticPurlinWallPlateBottomApplyDiagnosisTests
{
    private readonly ITestOutputHelper _output;

    public RoofAutomaticPurlinWallPlateBottomApplyDiagnosisTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void HostCase_45deg_Place0_Stash_DiagnoseApplyParityAndDatumResolution()
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        // Product layout as dialog TryCreateDraft persists after BottomEdge=0 under WPB.
        var productLayout = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    1010d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 357.417d,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                0d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };

        var solved = Solve(45d);
        var requested = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var planning = new RoofAutomaticPurlinPlanningInput(requested, 220d, 125d)
        {
            PurlinWidthMm = 160d,
            WallPlatesEnabled = true,
            WallPlateWidthMm = 140d,
            WallPlateHeightMm = 140d,
        };

        // Stage A: dialog TryCreateDatum path — ResolveEffectiveDatum WITHOUT Prepare.
        var datumWithoutPrepare = RoofAutomaticPurlinPlanner.ResolveEffectiveDatum(
            solved.Geometry,
            solved.Provenance,
            productLayout,
            planning);
        _output.WriteLine(
            $"A ResolveEffectiveDatum(no Prepare): valid={datumWithoutPrepare.IsValid} " +
            $"err={datumWithoutPrepare.Error} " +
            $"LocalZ={datumWithoutPrepare.Datum?.ReferenceLocalZMm} " +
            $"WpBottom={datumWithoutPrepare.WallPlateBottomLocalZMm}");

        // Stage B: Planner.Create expands via Prepare (preview + materialize).
        var previewPlan = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            productLayout,
            planning);
        _output.WriteLine(
            $"B Planner.Create(product): valid={previewPlan.IsValid} err={previewPlan.Error} " +
            $"items={previewPlan.Plan?.Items.Count}");
        Assert.True(previewPlan.IsValid, previewPlan.Error.ToString());

        // Stage C: ValidateForWrite (Apply gate).
        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(productLayout);
        _output.WriteLine(
            $"C ValidateForWrite: valid={written.IsValid} err={written.Error} " +
            $"Place={written.Layout?.WallPlatePlacement?.PlacementValueMm} " +
            $"LowerEdge={written.Layout?.WallPlateLowerEdgeHeightMm}");
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(0d, written.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(357.417d, written.Layout.WallPlateLowerEdgeHeightMm, 3);

        // Stage D: rematerialize with ValidateForWrite layout + draft datum LocalZ from A
        // (mirrors ProductionApplyService passing datumValidation.Datum).
        Assert.True(datumWithoutPrepare.IsValid && datumWithoutPrepare.Datum is not null);
        var rematerializeInput = new RoofAutomaticPurlinPlanningInput(
            datumWithoutPrepare.Datum,
            220d,
            125d)
        {
            PurlinWidthMm = 160d,
            WallPlatesEnabled = true,
            WallPlateWidthMm = 140d,
            WallPlateHeightMm = 140d,
        };
        var rematerialized = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            written.Layout,
            rematerializeInput);
        _output.WriteLine(
            $"D Rematerialize: valid={rematerialized.IsValid} err={rematerialized.Error}");
        Assert.True(rematerialized.IsValid, rematerialized.Error.ToString());

        // Stage E: PlansEquivalent-style check (Apply preview-production-plan-mismatch).
        var mismatches = ComparePlans(previewPlan.Plan!, rematerialized.Plan!);
        foreach (var line in mismatches)
        {
            _output.WriteLine(line);
        }

        _output.WriteLine($"E mismatchCount={mismatches.Count}");

        // Stage F: persistence round-trip equivalence (owner-metadata-postcondition).
        var storedWallPlate = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
            written.Layout.WallPlatePlacement);
        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items:
            [
                new(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    1,
                    RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
                    1010d,
                    RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
                    0,
                    0,
                    RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
                    25d,
                    160d,
                    220d),
            ],
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: written.Layout.WallPlateLowerEdgeHeightMm,
            wallPlatePlacementItem: storedWallPlate);
        _output.WriteLine(
            $"F ValidateStored: valid={reread.IsValid} err={reread.Error} " +
            $"Place={reread.Layout?.WallPlatePlacement?.PlacementValueMm} " +
            $"LowerEdge={reread.Layout?.WallPlateLowerEdgeHeightMm}");
        Assert.True(reread.IsValid, reread.Error.ToString());
        var equivalent = RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
            reread.Layout!,
            written.Layout);
        _output.WriteLine($"F AreEquivalentForOwnerWrite={equivalent}");
        if (!equivalent)
        {
            DumpLayoutDiff(written.Layout, reread.Layout!, _output);
        }

        // Stage F2: ValidateForWrite → ValidateStored using Encode path fields from
        // ValidateForWrite's own round-trip via ValidateStored of its reconstructed form.
        var roundTrip = RoundTripViaValidateForWriteFields(written.Layout);
        _output.WriteLine(
            $"F2 roundTrip valid={roundTrip.IsValid} err={roundTrip.Error} " +
            $"equiv={roundTrip.IsValid && RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(roundTrip.Layout!, written.Layout)}");
        if (roundTrip.IsValid &&
            !RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                roundTrip.Layout!,
                written.Layout))
        {
            DumpLayoutDiff(written.Layout, roundTrip.Layout!, _output);
        }

        // Stage F3: simulate Decode AFTER extended full-placement XData write.
        var afterDecode = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items: written.Layout!.IntermediateItems.Select(ToStoredIntermediate).ToArray(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: written.Layout.WallPlateLowerEdgeHeightMm,
            wallPlatePlacementItem: RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
                written.Layout.WallPlatePlacement!),
            sectionDimensions: new RoofPurlinLayoutStoredSectionDimensions(140d, 140d, 0d, 0d),
            wallPlateLowerEdgeHeightExplicit: true);
        Assert.True(afterDecode.IsValid, afterDecode.Error.ToString());
        _output.WriteLine(
            $"F3 afterDecode Place={afterDecode.Layout!.WallPlatePlacement!.PlacementValueMm} " +
            $"LowerEdge={afterDecode.Layout.WallPlateLowerEdgeHeightMm}");
        Assert.Equal(357.417d, afterDecode.Layout.WallPlateLowerEdgeHeightMm, 3);
        Assert.True(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                written.Layout,
                afterDecode.Layout),
            "Apply postcondition must pass after LowerEdge XData fix.");

        // Stage F3b: legacy full-placement without explicit LowerEdge still falls back.
        var legacyDecode = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items: written.Layout.IntermediateItems.Select(ToStoredIntermediate).ToArray(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 0d,
            wallPlatePlacementItem: RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
                written.Layout.WallPlatePlacement!),
            sectionDimensions: new RoofPurlinLayoutStoredSectionDimensions(140d, 140d, 0d, 0d),
            wallPlateLowerEdgeHeightExplicit: false);
        Assert.True(legacyDecode.IsValid, legacyDecode.Error.ToString());
        Assert.Equal(0d, legacyDecode.Layout!.WallPlateLowerEdgeHeightMm, 9);

        // Stage G: ResolveEffectiveDatum after Prepare (correct bootstrap).
        var prepared = RoofAutomaticPurlinPitchAdaptationRules
            .PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(productLayout);
        var datumWithPrepare = RoofAutomaticPurlinPlanner.ResolveEffectiveDatum(
            solved.Geometry,
            solved.Provenance,
            prepared,
            planning);
        _output.WriteLine(
            $"G ResolveEffectiveDatum(after Prepare): LocalZ={datumWithPrepare.Datum?.ReferenceLocalZMm} " +
            $"WpBottom={datumWithPrepare.WallPlateBottomLocalZMm} " +
            $"PlaceAfterPrepare={prepared.WallPlatePlacement!.PlacementValueMm}");

        Assert.Equal(0d, datumWithoutPrepare.Datum!.ReferenceLocalZMm, 3);
        Assert.True(
            datumWithPrepare.Datum!.ReferenceLocalZMm > 300d,
            "Prepared bootstrap must resolve WP bottom near ~357 mm, not eave 0.");
    }

    private static List<string> ComparePlans(
        RoofAutomaticPurlinPlan expected,
        RoofAutomaticPurlinPlan actual)
    {
        var lines = new List<string>();
        if (expected.Items.Count != actual.Items.Count)
        {
            lines.Add($"count {expected.Items.Count} vs {actual.Items.Count}");
            return lines;
        }

        for (var i = 0; i < expected.Items.Count; i++)
        {
            var left = expected.Items[i];
            var right = actual.Items[i];
            if (left.GeneratedKey != right.GeneratedKey)
            {
                lines.Add($"[{i}] GeneratedKey {left.GeneratedKey} vs {right.GeneratedKey}");
            }

            if (left.ElevationProfile is null || right.ElevationProfile is null)
            {
                lines.Add($"[{i}] missing elevation profile");
                continue;
            }

            if (Math.Abs(
                    left.ElevationProfile.BottomLocalZMm -
                    right.ElevationProfile.BottomLocalZMm) > 1e-6)
            {
                lines.Add(
                    $"[{i}] BottomLocalZ {left.ElevationProfile.BottomLocalZMm} vs " +
                    $"{right.ElevationProfile.BottomLocalZMm}");
            }

            if (Math.Abs(
                    left.ElevationProfile.BottomRelativeElevationMm -
                    right.ElevationProfile.BottomRelativeElevationMm) > 1e-6)
            {
                lines.Add(
                    $"[{i}] BottomRelative {left.ElevationProfile.BottomRelativeElevationMm} vs " +
                    $"{right.ElevationProfile.BottomRelativeElevationMm}");
            }
        }

        return lines;
    }

    private static RoofPurlinLayoutValidationResult RoundTripViaValidateForWriteFields(
        RoofAutomaticPurlinLayout written)
    {
        var storedWp = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
            written.WallPlatePlacement!);
        return RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            written.RidgeEnabled ? 1 : 0,
            written.IntermediateItems.Select(ToStoredIntermediate).ToArray(),
            written.WallPlateEnabled ? 1 : 0,
            written.WallPlateLowerEdgeHeightMm,
            storedWp,
            sectionDimensions: RoofPurlinLayoutPersistenceRules.HasPersistedSectionDimensions(written)
                ? new RoofPurlinLayoutStoredSectionDimensions(
                    written.WallPlatePlacement?.WidthMm ?? 0d,
                    written.WallPlatePlacement?.HeightMm ?? 0d,
                    written.RidgeWidthMm ?? 0d,
                    written.RidgeHeightMm ?? 0d)
                : null);
    }

    private static RoofPurlinLayoutStoredItem ToStoredIntermediate(
        RoofAutomaticPurlinLayoutItem item)
    {
        var seatingToken = RoofPurlinLayoutPersistenceRules.NoSeatingDepthToken;
        var seatingValue = 0d;
        if (item.SeatingDepth is not null)
        {
            seatingToken = item.SeatingDepth.Mode ==
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight
                    ? RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken
                    : "AbsoluteMm";
            seatingValue = item.SeatingDepth.Value;
        }

        return new RoofPurlinLayoutStoredItem(
            item.LayoutItemId,
            item.Enabled ? 1 : 0,
            RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
            item.PlacementValueMm,
            RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
            0,
            0,
            seatingToken,
            seatingValue,
            item.WidthMm ?? 0d,
            item.HeightMm ?? 0d);
    }

    private static void DumpLayoutDiff(
        RoofAutomaticPurlinLayout expected,
        RoofAutomaticPurlinLayout actual,
        ITestOutputHelper output)
    {
        output.WriteLine(
            $"diff WallPlateEnabled {expected.WallPlateEnabled} vs {actual.WallPlateEnabled}");
        output.WriteLine(
            $"diff RidgeEnabled {expected.RidgeEnabled} vs {actual.RidgeEnabled}");
        output.WriteLine(
            $"diff LowerEdge {expected.WallPlateLowerEdgeHeightMm} vs {actual.WallPlateLowerEdgeHeightMm}");
        output.WriteLine(
            $"diff WP Place {expected.WallPlatePlacement?.PlacementValueMm} vs {actual.WallPlatePlacement?.PlacementValueMm}");
        output.WriteLine(
            $"diff WP Mode {expected.WallPlatePlacement?.PlacementMode} vs {actual.WallPlatePlacement?.PlacementMode}");
        output.WriteLine(
            $"diff WP W/H {expected.WallPlatePlacement?.WidthMm}x{expected.WallPlatePlacement?.HeightMm} vs " +
            $"{actual.WallPlatePlacement?.WidthMm}x{actual.WallPlatePlacement?.HeightMm}");
        output.WriteLine(
            $"diff WP Seating {FormatSeating(expected.WallPlatePlacement?.SeatingDepth)} vs " +
            $"{FormatSeating(actual.WallPlatePlacement?.SeatingDepth)}");
        output.WriteLine(
            $"diff WP Equals={Equals(expected.WallPlatePlacement, actual.WallPlatePlacement)}");
        output.WriteLine(
            $"diff ResolveWP Equals={Equals(RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(expected), RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(actual))}");
        output.WriteLine(
            $"diff Intermediate count {expected.IntermediateItems.Count} vs {actual.IntermediateItems.Count}");
        for (var i = 0;
             i < Math.Min(expected.IntermediateItems.Count, actual.IntermediateItems.Count);
             i++)
        {
            var a = expected.IntermediateItems[i];
            var b = actual.IntermediateItems[i];
            output.WriteLine(
                $"diff Int[{i}] Equals={Equals(a, b)} Place={a.PlacementValueMm}/{b.PlacementValueMm} " +
                $"W/H={a.WidthMm}x{a.HeightMm}/{b.WidthMm}x{b.HeightMm} " +
                $"Seat={FormatSeating(a.SeatingDepth)}/{FormatSeating(b.SeatingDepth)}");
        }

        output.WriteLine(
            $"diff RafterPolicy {expected.RafterSourcePolicy} vs {actual.RafterSourcePolicy}");
        output.WriteLine(
            $"diff ManualRafter {expected.ManualRafterWidthMm}x{expected.ManualRafterHeightMm} vs " +
            $"{actual.ManualRafterWidthMm}x{actual.ManualRafterHeightMm}");
        output.WriteLine(
            $"diff Ack {expected.AcknowledgedActualKind} " +
            $"{expected.AcknowledgedActualWidthMm}x{expected.AcknowledgedActualHeightMm} vs " +
            $"{actual.AcknowledgedActualKind} " +
            $"{actual.AcknowledgedActualWidthMm}x{actual.AcknowledgedActualHeightMm}");
    }

    private static string FormatSeating(RoofAutomaticPurlinSeatingDepth? seating) =>
        seating is null ? "null" : $"{seating.Mode}:{seating.Value}";

    private static SolvedFixture Solve(double pitchDegrees)
    {
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000),
        };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var geometry = Assert.IsType<HipRoofGeometry>(
            HipRoofGeometrySolver.Solve(new RoofDefinition(
                normalized.Validation.Footprint!,
                new RoofParameters(pitchDegrees),
                RoofKind.Hip)).Geometry);
        return new SolvedFixture(geometry, provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
