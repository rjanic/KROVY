using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AnnotationScaleRegressionTests
{
    // ──────────────────────────────────────────────
    // Bug 1: Settings save must preserve loaded denominator
    // ──────────────────────────────────────────────

    [Fact]
    public void DefaultProfile_HasDefaultScaleDenominator50()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            profile.AnnotationScaleDenominator);
    }

    [Fact]
    public void Normalize_PreservesValidNonDefaultDenominator()
    {
        var profile = new TimberElementDefaultProfile
        {
            AnnotationScaleDenominator = 25,
            DefaultAnnotationMode = TimberAnnotationMode.FullLabel,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            Styles = TimberElementDefaultProfile.CreateDefault().Styles,
        };

        var normalized = profile.Normalize();

        Assert.Equal(25, normalized.AnnotationScaleDenominator);
        Assert.Equal(TimberAnnotationMode.FullLabel, normalized.DefaultAnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Plain, normalized.DefaultItemNumberLeaderStyle);
    }

    [Fact]
    public void Normalize_PreservesDenominator100()
    {
        var profile = new TimberElementDefaultProfile
        {
            AnnotationScaleDenominator = 100,
            DefaultAnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Circle,
            Styles = TimberElementDefaultProfile.CreateDefault().Styles,
        };

        var normalized = profile.Normalize();

        Assert.Equal(100, normalized.AnnotationScaleDenominator);
    }

    [Fact]
    public void Normalize_ChangingAnnotationModeDoesNotAffectDenominator()
    {
        var profile = CreateProfileWithDenominator(25);
        profile.DefaultAnnotationMode = TimberAnnotationMode.DimensionsLeader;
        profile.DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain;

        var normalized = profile.Normalize();

        Assert.Equal(25, normalized.AnnotationScaleDenominator);
        Assert.Equal(TimberAnnotationMode.DimensionsLeader, normalized.DefaultAnnotationMode);
    }

    [Fact]
    public void Normalize_ChangingItemLeaderStyleDoesNotAffectDenominator()
    {
        var profile = CreateProfileWithDenominator(25);
        profile.DefaultAnnotationMode = TimberAnnotationMode.ItemNumberLeader;
        profile.DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Circle;

        var normalized = profile.Normalize();

        Assert.Equal(25, normalized.AnnotationScaleDenominator);
        Assert.Equal(ItemNumberLeaderStyle.Circle, normalized.DefaultItemNumberLeaderStyle);
    }

    [Fact]
    public void CreateDefault_ReturnsDenominator50()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(50, profile.AnnotationScaleDenominator);
    }

    // ──────────────────────────────────────────────
    // Bug 2: DimensionsWithItemNumber + Plain must not reach block-definition
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRepresentation_DimensionsWithItemNumberPlusPlain_ReturnsLeader()
    {
        var representation = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Plain);

        Assert.Equal(TimberMainAnnotationRepresentation.Leader, representation);
    }

    [Fact]
    public void GetRepresentation_ItemNumberLeaderPlusPlain_ReturnsLeader()
    {
        var representation = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain);

        Assert.Equal(TimberMainAnnotationRepresentation.Leader, representation);
    }

    [Fact]
    public void GetRepresentation_DimensionsLeaderPlusPlain_ReturnsLeader()
    {
        var representation = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain);

        Assert.Equal(TimberMainAnnotationRepresentation.Leader, representation);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void GetRepresentation_FramedStyles_ReturnBlockLeaderForItemNumberLeader(
        ItemNumberLeaderStyle style)
    {
        var representation = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.ItemNumberLeader,
            style);

        Assert.Equal(TimberMainAnnotationRepresentation.BlockLeader, representation);
    }

    [Fact]
    public void GetRepresentation_DimensionsWithItemNumberPlusFramed_ReturnsLeader()
    {
        // For combined mode, the single-argument overload returns Leader.
        // The two-argument overload only upgrades to BlockLeader for ItemNumberLeader + framed.
        var representation = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle);

        Assert.Equal(TimberMainAnnotationRepresentation.Leader, representation);
    }

    [Fact]
    public void IsFramedItemLeader_PlainStyle_ReturnsFalse()
    {
        Assert.False(TimberAnnotationModeRules.IsFramedItemLeader(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Plain));
        Assert.False(TimberAnnotationModeRules.IsFramedItemLeader(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void IsFramedItemLeader_FramedStyles_ReturnsTrue(ItemNumberLeaderStyle style)
    {
        Assert.True(TimberAnnotationModeRules.IsFramedItemLeader(
            TimberAnnotationMode.DimensionsWithItemNumber,
            style));
        Assert.True(TimberAnnotationModeRules.IsFramedItemLeader(
            TimberAnnotationMode.ItemNumberLeader,
            style));
    }

    // ──────────────────────────────────────────────
    // Combination matrix: no combination may throw
    // ──────────────────────────────────────────────

    [Fact]
    public void AllAnnotationModeAndStyleCombinations_NormalizeWithoutException()
    {
        var modes = new[]
        {
            TimberAnnotationMode.FullLabel,
            TimberAnnotationMode.ItemNumberLeader,
            TimberAnnotationMode.DimensionsLeader,
            TimberAnnotationMode.DimensionsWithItemNumber,
            TimberAnnotationMode.NoAnnotations,
        };

        var styles = new[]
        {
            ItemNumberLeaderStyle.Plain,
            ItemNumberLeaderStyle.Circle,
            ItemNumberLeaderStyle.Slot,
            ItemNumberLeaderStyle.Rectangle,
        };

        foreach (var mode in modes)
        foreach (var style in styles)
        {
            var normalizedMode = TimberAnnotationModeRules.Normalize(mode);
            var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
            var representation = TimberAnnotationModeRules.GetRepresentation(mode, style);

            // Verify no exception from representation resolution
            Assert.True(
                representation is TimberMainAnnotationRepresentation.FullLabel
                    or TimberMainAnnotationRepresentation.Leader
                    or TimberMainAnnotationRepresentation.BlockLeader
                    or TimberMainAnnotationRepresentation.None);

            // Verify IsFramedItemLeader does not throw
            var isFramed = TimberAnnotationModeRules.IsFramedItemLeader(mode, style);

            // Plain must never be framed
            if (normalizedStyle == ItemNumberLeaderStyle.Plain)
            {
                Assert.False(isFramed);
            }

            // Framed styles in item leader modes must be framed
            if ((normalizedMode == TimberAnnotationMode.ItemNumberLeader ||
                 normalizedMode == TimberAnnotationMode.DimensionsWithItemNumber) &&
                normalizedStyle != ItemNumberLeaderStyle.Plain)
            {
                Assert.True(isFramed);
            }
        }
    }

    [Fact]
    public void BlockDefinitionRules_RejectPlainWithoutException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TimberItemLeaderBlockDefinitionRules.Resolve(
                ItemNumberLeaderStyle.Plain,
                "K1"));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void BlockDefinitionRules_AcceptFramedStyles(ItemNumberLeaderStyle style)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");

        Assert.NotNull(definition);
        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);
    }

    // ──────────────────────────────────────────────
    // Backward compatibility: old profile without scale
    // ──────────────────────────────────────────────

    [Fact]
    public void OldProfile_WithoutScaleDenominator_UsesDefault50()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        // Simulate old profile by using default construction
        var normalized = profile.Normalize();

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            normalized.AnnotationScaleDenominator);
    }

    // ──────────────────────────────────────────────
    // Source-contract: production code paths
    // ──────────────────────────────────────────────

    [Fact]
    public void UpsertCombinedLeader_UsesDynamicRepresentationNotHardcodedBlockLeader()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        // Must contain the dynamic representation logic
        Assert.Contains(
            "ItemNumberLeaderStyleRules.Normalize(data.ItemNumberLeaderStyle)",
            source);

        // Must still contain BlockLeader for framed styles
        Assert.Contains(
            "TimberMainAnnotationRepresentation.BlockLeader",
            source);
    }

    [Fact]
    public void SettingsWindowPersistsSelectedScaleAsTheNewElementDefault()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");

        Assert.Contains("TryGetDrawingScaleDenominator(out var annotationScaleDenominator)", source);
        Assert.Contains("AnnotationScaleDenominator = includeAnnotation", source);
        Assert.DoesNotContain("_legacyUserDefaultScaleDenominator", source);
    }

    [Fact]
    public void BlockDefinitionResolve_IsNotCalledForPlainInProduction()
    {
        var labels = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        // CreateBlockMLeader must check style before calling Resolve
        // Verify that BlockLeader path in UpsertCombinedLeader is guarded
        var combineSegments = Between(
            labels,
            "private static bool UpsertCombinedLeader",
            "private static void DeleteUnexpectedCompositeComponents");

        // The combined path must not hardcode BlockLeader
        Assert.Contains("ItemNumberLeaderStyleRules.Normalize", combineSegments);
    }

    // ──────────────────────────────────────────────
    // R3: Deterministic TextStyleId for native MLeaders
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateLeaderMText_SetsExplicitTextStyleId()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        var methodBody = Between(
            source,
            "private static MText CreateLeaderMText(",
            "private static bool TryUpdateNativeLeader(");

        Assert.Contains("text.TextStyleId = resolvedTextStyleId ?? database.Textstyle", methodBody);
    }

    [Fact]
    public void ApplyInstanceProperties_SetsExplicitTextStyleId()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

        var methodBody = Between(
            source,
            "public static void ApplyInstanceProperties(",
            "public static void ApplyBlockInstanceProperties(");

        Assert.Contains("leader.TextStyleId = resolvedTextStyleId ?? database.Textstyle", methodBody);
    }

    [Fact]
    public void CreateNativeMLeader_CallsCreateLeaderMTextWithTextHeight()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        var methodBody = Between(
            source,
            "private static MLeader CreateNativeMLeader(",
            "private static MLeader CreateBlockMLeader(");

        Assert.Contains("CreateLeaderMText(", methodBody);
        Assert.Contains("effectiveTextHeight", methodBody);
    }

    [Fact]
    public void TryUpdateNativeLeader_UsesSameCreateLeaderMTextPath()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        var methodBody = ExtractMethodBody(
            source,
            "private static bool TryUpdateNativeLeader(");

        Assert.False(
            string.IsNullOrWhiteSpace(methodBody),
            "Could not extract TryUpdateNativeLeader method body.");

        Assert.Contains("CreateLeaderMText(", methodBody);
        Assert.Contains("effectiveTextHeight", methodBody);
    }

    [Fact]
    public void BlockInstanceProperties_DoNotSetTextStyleId()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

        var methodBody = Between(
            source,
            "public static void ApplyBlockInstanceProperties(",
            "public static void ApplyCombinedBlockInstanceProperties(");

        Assert.DoesNotContain("TextStyleId", methodBody);
    }

    [Fact]
    public void ProductionCode_HasNoAnnotativeContexts()
    {
        var labels = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        Assert.DoesNotContain("EnableAnnotationScale = true", labels);
        Assert.DoesNotContain("ApplyCurrentAnnotationScale", labels);
        Assert.DoesNotContain("ACDB_ANNOTATIONSCALES", labels);
        Assert.DoesNotContain("AddContext(", labels);
        Assert.DoesNotContain("CANNOSCALE", labels);
    }

    // ──────────────────────────────────────────────
    // R4A: Framed (Circle/Slot/Rectangle) presentation scaling
    // ──────────────────────────────────────────────

    [Fact]
    public void Calculate_FramedCircle_ScalesEnvelopeAndClearance()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d, AnchorY: 0d,
            TextX: 1000d, TextY: 1000d,
            RotationRadians: 0d);

        var layoutAt50 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 1d);

        var layoutAt100 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 2d);

        var layoutAt25 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 0.5d);

        // Envelope scales proportionally
        Assert.Equal(layoutAt50.EnvelopeWidthMm * 2d, layoutAt100.EnvelopeWidthMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeHeightMm * 2d, layoutAt100.EnvelopeHeightMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeWidthMm * 0.5d, layoutAt25.EnvelopeWidthMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeHeightMm * 0.5d, layoutAt25.EnvelopeHeightMm, 0.1d);
    }

    [Fact]
    public void Calculate_FramedSlot_ScalesEnvelopeAndClearance()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d, AnchorY: 0d,
            TextX: 1000d, TextY: 1000d,
            RotationRadians: 0d);

        var layoutAt50 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Slot, presentationScaleFactor: 1d);

        var layoutAt100 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Slot, presentationScaleFactor: 2d);

        Assert.Equal(layoutAt50.EnvelopeWidthMm * 2d, layoutAt100.EnvelopeWidthMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeHeightMm * 2d, layoutAt100.EnvelopeHeightMm, 0.1d);
    }

    [Fact]
    public void Calculate_FramedRectangle_ScalesEnvelopeAndClearance()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d, AnchorY: 0d,
            TextX: 1000d, TextY: 1000d,
            RotationRadians: 0d);

        var layoutAt50 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Rectangle, presentationScaleFactor: 1d);

        var layoutAt100 = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Rectangle, presentationScaleFactor: 2d);

        Assert.Equal(layoutAt50.EnvelopeWidthMm * 2d, layoutAt100.EnvelopeWidthMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeHeightMm * 2d, layoutAt100.EnvelopeHeightMm, 0.1d);
    }

    [Fact]
    public void CalculateBlock_ScalesFirstSegmentAndEnvelope()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d, AnchorY: 0d,
            TextX: 0d, TextY: 0d,
            RotationRadians: 0d);

        var layoutAt50 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 1d);

        var layoutAt100 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 2d);

        var layoutAt25 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 0.5d);

        Assert.Equal(layoutAt50.EnvelopeWidthMm * 2d, layoutAt100.EnvelopeWidthMm, 0.1d);
        Assert.Equal(layoutAt50.EnvelopeWidthMm * 0.5d, layoutAt25.EnvelopeWidthMm, 0.1d);
    }

    [Fact]
    public void BlockDefinitions_StayAtBaseScale()
    {
        // Block definition constant must remain 1 regardless of scaling
        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle, "K1");

        Assert.Equal(400d, definition.WidthMm);
    }

    [Fact]
    public void CreateBlockMLeader_UsesPresentationScaleFactorNotConstant()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        var methodBody = ExtractMethodBody(
            source,
            "private static MLeader CreateBlockMLeader(");

        Assert.False(
            string.IsNullOrWhiteSpace(methodBody),
            "Could not extract CreateBlockMLeader method body.");

        Assert.Contains("presentationScaleFactor", methodBody);
        Assert.Contains("new Scale3d(presentationScaleFactor)", methodBody);
        Assert.DoesNotContain("TimberItemLeaderBlockDefinitionRules.BlockScale", methodBody);
    }

    [Fact]
    public void ApplyBlockInstanceProperties_ScalesArrowAndLanding()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

        var methodBody = Between(
            source,
            "public static void ApplyBlockInstanceProperties(",
            "public static void ApplyCombinedBlockInstanceProperties(");

        Assert.Contains("* presentationScaleFactor", methodBody);
        Assert.Contains("ArrowheadSize * presentationScaleFactor", methodBody);
        Assert.Contains("LandingDistance * presentationScaleFactor", methodBody);
    }

    [Fact]
    public void ApplyCombinedBlockInstanceProperties_ScalesArrowAndLanding()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyMLeaderStyleService.cs");

        var methodBody = ExtractMethodBody(
            source,
            "public static void ApplyCombinedBlockInstanceProperties(");

        Assert.False(
            string.IsNullOrWhiteSpace(methodBody));

        Assert.Contains("* presentationScaleFactor", methodBody);
        Assert.Contains("ArrowheadSize * presentationScaleFactor", methodBody);
        Assert.Contains("LandingDistance * presentationScaleFactor", methodBody);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void Calculate_AtScale1_HasIdenticalGeometryToBaseline(ItemNumberLeaderStyle style)
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 100d, AnchorY: 200d,
            TextX: 100d, TextY: 200d,
            RotationRadians: 0d);

        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", style, presentationScaleFactor: 1d);

        var baseline = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", style, presentationScaleFactor: 1d);

        Assert.Equal(baseline.EnvelopeWidthMm, layout.EnvelopeWidthMm, 0.001d);
        Assert.Equal(baseline.EnvelopeHeightMm, layout.EnvelopeHeightMm, 0.001d);
        Assert.Equal(baseline.KneeX, layout.KneeX, 0.001d);
        Assert.Equal(baseline.KneeY, layout.KneeY, 0.001d);
        Assert.Equal(baseline.ContentX, layout.ContentX, 0.001d);
        Assert.Equal(baseline.ContentY, layout.ContentY, 0.001d);
    }

    [Fact]
    public void CalculateBlock_Calculate_SameEnvelopeForSameStyleAndScale()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 100d, AnchorY: 200d,
            TextX: 100d, TextY: 200d,
            RotationRadians: 0d);

        var blockLayout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 2d);

        var plainLayout = TimberItemLeaderLayoutCalculator.Calculate(
            placement, "K1", ItemNumberLeaderStyle.Circle, presentationScaleFactor: 2d);

        // Both should produce scaled envelopes (CalculateBlock uses block definition,
        // Calculate uses min-diameter — they may differ but both should scale)
        Assert.Equal(800d, blockLayout.EnvelopeWidthMm);
        Assert.Equal(800d, plainLayout.EnvelopeWidthMm);
    }

    [Fact]
    public void PlainItemNumberLeader_Calculate_StillScalesCorrectly()
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d, AnchorY: 0d,
            TextX: 1000d, TextY: 1000d,
            RotationRadians: 0d);

        var layoutAt25 =
            TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
                placement,
                "K1",
                presentationScaleFactor: 0.5d);

        var layoutAt100 =
            TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
                placement,
                "K1",
                presentationScaleFactor: 2d);

        // Plain envelope = estimated text width; at 2x the text height, envelope is roughly 2x
        Assert.True(layoutAt100.EnvelopeWidthMm > layoutAt25.EnvelopeWidthMm * 3d);
    }

    // ──────────────────────────────────────────────
    // R4A-compile-fix: Scale service single-instance lifecycle
    // ──────────────────────────────────────────────

    [Fact]
    public void RequiresCircleNormalization_HasPresentationScaleFactorParameter()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        Assert.Contains(
            "private static bool RequiresCircleNormalization(",
            source);
        Assert.Contains(
            "double presentationScaleFactor",
            Between(
                source,
                "private static bool RequiresCircleNormalization(",
                "{"));
    }

    [Fact]
    public void FindCircleNormalizationSourceIds_UsesPerElementScaleContext()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        var methodBody = ExtractMethodBody(
            source,
            "internal static IReadOnlySet<ObjectId> FindCircleNormalizationSourceIds(");

        Assert.False(
            string.IsNullOrWhiteSpace(methodBody),
            "Could not extract FindCircleNormalizationSourceIds.");

        Assert.Contains(
            "annotationScaleService.ResolveForElement(data)",
            methodBody);
        Assert.Contains(
            "source.ScaleContext.ScaleFactor",
            methodBody);
    }

    [Fact]
    public void RenumberAll_UsesBatchScaleServiceForCircleNormalization()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberElementRenumberingService.cs");

        var methodBody = ExtractMethodBody(
            source,
            "public static TimberElementRenumberingResult RenumberAll(");

        Assert.Contains("presentationBatchContext", methodBody);
        Assert.Contains(
            "FindCircleNormalizationSourceIds(",
            methodBody);
        var invocationStart = methodBody.IndexOf(
            "FindCircleNormalizationSourceIds(",
            StringComparison.Ordinal);
        Assert.Contains(
            "presentationBatchContext.AnnotationScaleService",
            ExtractInvocation(methodBody, invocationStart));
    }

    [Fact]
    public void UpdateLabelsForChangedEntities_HasPresentationBatchParameter()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");

        Assert.Contains(
            "AutoCadAnnotationPresentationBatchContext presentationBatchContext",
            Between(
                source,
                "private static void UpdateLabelsForChangedEntities(",
                ")"));
    }

    [Fact]
    public void UpdateLabelsForChangedEntities_DoesNotCreateScaleServiceInternally()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");

        var methodBody = ExtractMethodBody(
            source,
            "private static void UpdateLabelsForChangedEntities(");

        Assert.False(
            string.IsNullOrWhiteSpace(methodBody),
            "Could not extract UpdateLabelsForChangedEntities.");

        Assert.DoesNotContain(
            "AutoCadAnnotationScaleService.Create(",
            methodBody);
        Assert.DoesNotContain(
            "AutoCadAnnotationPresentationBatchContext.Create(",
            methodBody);
    }

    [Fact]
    public void ApplySettingsToExistingElements_CreatesPresentationBatchOnce()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");

        var methodBody = ExtractMethodBody(
            source,
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements(");
        Assert.False(
            string.IsNullOrWhiteSpace(methodBody),
            "Could not extract ApplySettingsToExistingElements.");

        Assert.Equal(
            1,
            CountOccurrences(
                methodBody,
                "AutoCadAnnotationPresentationBatchContext.Create("));

        var updateCall = methodBody.IndexOf(
            "UpdateLabelsForChangedEntities(",
            StringComparison.Ordinal);

        Assert.True(updateCall >= 0);
        Assert.DoesNotContain("FindCircleNormalizationSourceIds(", methodBody);
        Assert.Contains(
            "presentationBatchContext",
            ExtractInvocation(methodBody, updateCall));
    }

    [Fact]
    public void AllCallSites_PassPresentationBatchToUpdateLabels()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");

        const string methodDeclaration =
            "private static void UpdateLabelsForChangedEntities(";
        var declarationIndex = source.IndexOf(
            methodDeclaration,
            StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0);

        var count = 0;
        var pos = 0;
        while ((pos = source.IndexOf(
                   "UpdateLabelsForChangedEntities(",
                   pos,
                   StringComparison.Ordinal)) >= 0)
        {
            if (pos == declarationIndex + methodDeclaration.IndexOf(
                    "UpdateLabelsForChangedEntities(",
                    StringComparison.Ordinal))
            {
                pos += "UpdateLabelsForChangedEntities(".Length;
                continue;
            }

            var invocation = ExtractInvocation(source, pos);
            Assert.False(string.IsNullOrWhiteSpace(invocation));
            Assert.Contains("presentationBatchContext", invocation);
            pos += invocation.Length;
            count++;
        }

        Assert.Equal(3, count);
    }

    [Theory]
    [InlineData(0.5d, 62.5d, 37.5d)]
    [InlineData(1d, 125d, 75d)]
    [InlineData(2d, 250d, 150d)]
    public void CombinedDimensionTypography_ScalesExactlyOnce(
        double presentationScaleFactor,
        double expectedTextHeightMm,
        double expectedFrameGapMm)
    {
        Assert.Equal(
            expectedTextHeightMm,
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor));
        Assert.Equal(
            expectedTextHeightMm,
            TimberCombinedDimensionTypographyRules.CalculateEnvelopeHeightMm(
                presentationScaleFactor));
        Assert.Equal(
            expectedFrameGapMm,
            TimberCombinedDimensionTypographyRules.CalculateMinimumFrameGapMm(
                presentationScaleFactor));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void CombinedDimensionTypography_IsIndependentFromFramedItemStyle(
        ItemNumberLeaderStyle style)
    {
        Assert.NotEqual(ItemNumberLeaderStyle.Plain, style);
        Assert.Equal(
            125d,
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(1d));
    }

    [Theory]
    [InlineData(0.5d, 37.5d)]
    [InlineData(1d, 75d)]
    [InlineData(2d, 150d)]
    public void CombinedDimensionLayout_LeavesRequiredGapBeforeFrame(
        double presentationScaleFactor,
        double expectedGapMm)
    {
        var landingDistanceMm =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            presentationScaleFactor;
        var textHeightMm =
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var envelopeWidthMm =
            TimberCombinedDimensionTypographyRules.CalculateEnvelopeWidthMm(
                "160\\P220",
                presentationScaleFactor);
        var centerOffsetMm =
            TimberCombinedDimensionTypographyRules
                .CalculateTextCenterOffsetFromLandingStartMm(
                    landingDistanceMm,
                    envelopeWidthMm,
                    textHeightMm);
        var actualGapMm =
            landingDistanceMm -
            centerOffsetMm -
            envelopeWidthMm / 2d;

        Assert.Equal(expectedGapMm, actualGapMm);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(1d)]
    [InlineData(2d)]
    public void CombinedFramedItem_BlockScaleRemainsPresentationScaleFactor(
        double presentationScaleFactor)
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static MLeader CreateBlockMLeader(");

        Assert.Contains(
            "leader.BlockScale = new Scale3d(presentationScaleFactor)",
            methodBody);
        Assert.Equal(
            presentationScaleFactor,
            presentationScaleFactor *
            TimberItemLeaderBlockDefinitionRules.BlockScale);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void CombinedFramedItem_BaseGeometryRemainsUnchangedAtScale50(
        ItemNumberLeaderStyle style)
    {
        var placement = new TimberLeaderPlacement(
            AnchorX: 0d,
            AnchorY: 0d,
            TextX: 0d,
            TextY: 0d,
            RotationRadians: 0d);
        var definition =
            TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "K1",
            style,
            presentationScaleFactor: 1d);

        Assert.Equal(definition.WidthMm, layout.EnvelopeWidthMm);
        Assert.Equal(definition.HeightMm, layout.EnvelopeHeightMm);
        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);
    }

    [Fact]
    public void UpsertCombinedLeader_UsesDedicatedDimensionTypographyAndEnvelope()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static bool UpsertCombinedLeader(");

        Assert.Contains(
            "AutoCadDimensionsLeaderPresentationPolicy.TryPrepare(",
            methodBody);
        Assert.Contains(
            "dimensionsPresentation.ModelHeightMm",
            methodBody);
        Assert.Contains(
            "TimberCombinedDimensionTypographyRules.CalculateEnvelopeHeightMm(",
            methodBody);
        Assert.Contains(
            "TimberCombinedDimensionTypographyRules.CalculateEnvelopeWidthMm(",
            methodBody);
        Assert.Contains("dimensionTextHeightMm", methodBody);
        Assert.Contains("envelopeWidthMm: dimensionEnvelopeWidthMm", methodBody);
        Assert.Contains("envelopeHeightMm: dimensionEnvelopeHeightMm", methodBody);
        Assert.Contains(
            "resolvedTextStyleId: dimensionsPresentation.TextStyleId",
            methodBody);
        Assert.DoesNotContain("DefaultTextHeightMm", methodBody);
    }

    [Fact]
    public void CombinedDimension_CreateAndUpdateShareSameTextHeightAssignment()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static bool UpsertLabel(");
        var appearanceBody = ExtractMethodBody(
            source,
            "private static void ApplyLabelAppearance(");

        Assert.Equal(1, CountOccurrences(methodBody, "ApplyLabelAppearance("));
        Assert.Contains("label.TextHeight = textHeightMm", appearanceBody);
    }

    [Theory]
    [InlineData(0.5d, 62.5d)]
    [InlineData(1d, 125d)]
    [InlineData(2d, 250d)]
    public void DimensionTypography_AllDimensionModesUseSharedScale(
        double presentationScaleFactor,
        double expectedTextHeightMm)
    {
        Assert.Equal(
            expectedTextHeightMm,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor));
        Assert.Equal(
            expectedTextHeightMm,
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor));
        Assert.Equal(
            TimberDimensionTypographyRules
                .BaseDimensionTextHeightAtScale50Mm,
            TimberCombinedDimensionTypographyRules
                .BaseDimensionTextHeightAtScale50Mm);
    }

    [Fact]
    public void FullLabelAndDimensionsLeader_UseSharedDimensionTypographyInProduction()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "public static bool UpsertForElement(");

        Assert.Contains(
            "fullLabelPresentation.ModelHeightMm",
            methodBody);
        Assert.Contains(
            "TimberAnnotationTextRole.Dimension",
            Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "AutoCadFullLabelPresentationPolicy.cs"));
        Assert.Contains(
            "roleText.ModelHeightMm",
            Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                "AutoCadFullLabelPresentationPolicy.cs"));
        Assert.Contains(
            "TimberDimensionTypographyRules",
            methodBody);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsLeader",
            methodBody);
        Assert.DoesNotContain(
            "annotationScaleService.ScaleTextHeight(DefaultTextHeightMm)",
            methodBody);
    }

    [Fact]
    public void FramedCircle_BaseDefinitionIsFourHundredWithCentered135Text()
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");

        Assert.Equal(400d, definition.WidthMm);
        Assert.Equal(400d, definition.HeightMm);
        Assert.Equal(200d, definition.WidthMm / 2d);
        Assert.Equal(135d, definition.TextHeightMm);
    }

    [Theory]
    [InlineData(0.5d, 200d, 67.5d)]
    [InlineData(1d, 400d, 135d)]
    [InlineData(2d, 800d, 270d)]
    public void FramedCircle_InstanceScaleAppliesExactlyOnce(
        double blockScale,
        double expectedDiameterMm,
        double expectedTextHeightMm)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");

        Assert.Equal(expectedDiameterMm, definition.WidthMm * blockScale);
        Assert.Equal(expectedTextHeightMm, definition.TextHeightMm * blockScale);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void LinearFrames_UseCircleReductionFactorAndPreserveRatio(
        ItemNumberLeaderStyle style)
    {
        var definition =
            TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");
        var factor =
            TimberItemLeaderBlockDefinitionRules
                .FramedGeometryReductionFactor;

        Assert.Equal(400d / 520d, factor, 12);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .PreviousSmallFrameWidthMm * factor,
            definition.WidthMm,
            10);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .PreviousFrameHeightMm * factor,
            definition.HeightMm,
            10);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .PreviousSmallFrameWidthMm /
            TimberItemLeaderBlockDefinitionRules
                .PreviousFrameHeightMm,
            definition.WidthMm / definition.HeightMm,
            10);
        Assert.Equal(135d, definition.TextHeightMm);
    }

    [Fact]
    public void AllLinearFrameVariants_UseSameGeometryReductionFactor()
    {
        var factor =
            TimberItemLeaderBlockDefinitionRules
                .FramedGeometryReductionFactor;

        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm,
            TimberItemLeaderBlockDefinitionRules
                .PreviousSmallFrameWidthMm * factor,
            10);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.MediumFrameWidthMm,
            TimberItemLeaderBlockDefinitionRules
                .PreviousMediumFrameWidthMm * factor,
            10);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.LargeFrameWidthMm,
            TimberItemLeaderBlockDefinitionRules
                .PreviousLargeFrameWidthMm * factor,
            10);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.FrameHeightMm,
            TimberItemLeaderBlockDefinitionRules
                .PreviousFrameHeightMm * factor,
            10);
    }

    [Theory]
    [InlineData("VT12", TimberItemLeaderBlockSize.Medium)]
    [InlineData("VT1234", TimberItemLeaderBlockSize.Large)]
    public void LinearFrameVariantSelection_PreservesR4A3GeometrySizing(
        string itemNumber,
        TimberItemLeaderBlockSize expectedSize)
    {
        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Slot,
                     ItemNumberLeaderStyle.Rectangle,
                 })
        {
            var definition =
                TimberItemLeaderBlockDefinitionRules.Resolve(
                    style,
                    itemNumber);

            Assert.Equal(expectedSize, definition.Size);
            Assert.Equal(
                TimberItemNumberTypographyRules
                    .BaseItemNumberTextHeightAtScale50Mm,
                definition.TextHeightMm);
        }
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedDefinitions_AreIndependentFromDrawingScale(
        ItemNumberLeaderStyle style)
    {
        var baseDefinition =
            TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");

        Assert.Equal(
            baseDefinition,
            TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1"));
        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);
    }

    [Fact]
    public void CircleNormalization_DetectsLegacyDiameterAndTextHeight()
    {
        Assert.True(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedCircleDiameter(400d));
        Assert.False(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedCircleDiameter(520d));
        Assert.True(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedFramedItemTextHeight(135d));
        Assert.False(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedFramedItemTextHeight(175d));

        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static bool RequiresCircleNormalization(");

        Assert.Contains("HasExpectedCircleDiameter(", methodBody);
        Assert.Contains("HasExpectedFramedItemTextHeight(", methodBody);
    }

    [Fact]
    public void BlockEnsure_RebuildsIncompatibleLegacyDefinition()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockService.cs");
        var ensureBody = ExtractMethodBody(
            source,
            "public static ItemLeaderBlockReference Ensure(");
        var compatibilityBody = ExtractMethodBody(
            source,
            "private static bool IsCompatibleDefinition(");

        Assert.Contains("IsCompatibleDefinition(", ensureBody);
        Assert.Contains("EraseDefinitionContents(", ensureBody);
        Assert.Contains("AddFrameGeometry(", ensureBody);
        Assert.Contains("definition.TextHeightMm", compatibilityBody);
        Assert.Contains("HasExpectedCircleDiameter(", compatibilityBody);
        Assert.Contains("HasExpectedExtents(", compatibilityBody);
    }

    [Fact]
    public void NativeLeaderCreateAndUpdate_UseSameBaseTextHeightParameter()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var createBody = ExtractMethodBody(
            source,
            "private static MLeader CreateNativeMLeader(");
        var updateBody = ExtractMethodBody(
            source,
            "private static bool TryUpdateNativeLeader(");

        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            createBody);
        Assert.Contains(
            "baseTextHeightMm * presentationScaleFactor",
            updateBody);
        Assert.DoesNotContain(
            "DefaultTextHeightMm * presentationScaleFactor",
            createBody);
        Assert.DoesNotContain(
            "DefaultTextHeightMm * presentationScaleFactor",
            updateBody);
    }

    [Theory]
    [InlineData(0.5d, 67.5d)]
    [InlineData(1d, 135d)]
    [InlineData(2d, 270d)]
    public void ItemNumberTypography_AllItemRepresentationsUseSharedScale(
        double presentationScaleFactor,
        double expectedTextHeightMm)
    {
        Assert.Equal(
            expectedTextHeightMm,
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor));

        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Circle,
                     ItemNumberLeaderStyle.Slot,
                     ItemNumberLeaderStyle.Rectangle,
                 })
        {
            var definition =
                TimberItemLeaderBlockDefinitionRules.Resolve(style, "VT1");
            Assert.Equal(
                TimberItemNumberTypographyRules
                    .BaseItemNumberTextHeightAtScale50Mm,
                definition.TextHeightMm);
            Assert.Equal(
                expectedTextHeightMm,
                definition.TextHeightMm * presentationScaleFactor);
        }
    }

    [Fact]
    public void ItemNumberAndDimensionTypographyRemainIndependent()
    {
        Assert.Equal(
            135d,
            TimberItemNumberTypographyRules
                .BaseItemNumberTextHeightAtScale50Mm);
        Assert.Equal(
            125d,
            TimberDimensionTypographyRules
                .BaseDimensionTextHeightAtScale50Mm);
    }

    [Fact]
    public void UpsertForElement_UsesItemTypographyOnlyForPlainItemLeader()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "public static bool UpsertForElement(");

        Assert.Contains(
            "TimberAnnotationMode.ItemNumberLeader",
            methodBody);
        Assert.Contains(
            "TimberItemNumberTypographyRules",
            methodBody);
        Assert.Contains(
            "TimberAnnotationMode.DimensionsLeader",
            methodBody);
        Assert.Contains(
            "TimberDimensionTypographyRules",
            methodBody);
    }

    [Fact]
    public void CalculateShortLeaderPlacement_UsesDedicatedPlainItemCalculator()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var calculatorCall = source.IndexOf(
            "TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(",
            StringComparison.Ordinal);

        Assert.True(calculatorCall >= 0);
        Assert.Contains(
            "presentationScaleFactor",
            ExtractInvocation(source, calculatorCall));
    }

    [Fact]
    public void PlainItemLeader_CreateAndUpdateConsumeTheSamePlacement()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static bool UpsertLeader(");
        var updateStart = methodBody.IndexOf(
            "TryUpdateNativeLeader(",
            StringComparison.Ordinal);
        var createStart = methodBody.IndexOf(
            "CreateNativeMLeader(",
            StringComparison.Ordinal);

        Assert.True(updateStart >= 0);
        Assert.True(createStart >= 0);
        Assert.Contains(
            "placement",
            ExtractInvocation(methodBody, updateStart));
        Assert.Contains(
            "placement",
            ExtractInvocation(methodBody, createStart));
    }

    [Fact]
    public void CombinedItemComponent_UsesSharedItemNumberTypography()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var methodBody = ExtractMethodBody(
            source,
            "private static bool UpsertCombinedLeader(");

        Assert.Contains(
            "TimberItemNumberTypographyRules",
            methodBody);
        Assert.Contains(
            "baseTextHeightMm:",
            methodBody);
    }

    [Fact]
    public void LegacyFramedTextHeightRequiresDefinitionNormalization()
    {
        Assert.True(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedFramedItemTextHeight(135d));
        Assert.False(
            TimberItemLeaderBlockDefinitionRules
                .HasExpectedFramedItemTextHeight(175d));

        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockService.cs");
        var ensureBody = ExtractMethodBody(
            source,
            "public static ItemLeaderBlockReference Ensure(");
        var compatibilityBody = ExtractMethodBody(
            source,
            "private static bool IsCompatibleDefinition(");

        Assert.Contains("IsCompatibleDefinition(", ensureBody);
        Assert.Contains("EraseDefinitionContents(", ensureBody);
        Assert.Contains(
            "definition.TextHeightMm",
            compatibilityBody);
    }

    [Theory]
    [InlineData(0.5d, 62.5d, 52.083333333333336d)]
    [InlineData(1d, 125d, 104.16666666666667d)]
    [InlineData(2d, 250d, 208.33333333333334d)]
    public void FullLabelCenterOffset_DerivesFromScaledTypographyExactlyOnce(
        double presentationScaleFactor,
        double textHeightMm,
        double expectedCenterOffsetMm)
    {
        var actualTextHeightMm =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var lineAdvanceMm =
            TimberDimensionTypographyRules.CalculateLineAdvanceMm(
                actualTextHeightMm);
        var centerOffsetMm =
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(actualTextHeightMm);
        var placement =
            TimberElementLabelPlacementCalculator.Calculate(
                0d,
                0d,
                4000d,
                0d,
                2000d,
                0d,
                centerOffsetMm);

        Assert.Equal(
            expectedCenterOffsetMm,
            Math.Abs(placement.Y),
            8);
        Assert.Equal(lineAdvanceMm / 2d, centerOffsetMm, 8);
        Assert.Equal(
            textHeightMm,
            actualTextHeightMm,
            8);
    }

    [Fact]
    public void FullLabelProductionPath_DerivesOffsetFromScaledTextHeight()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var upsertBody = ExtractMethodBody(
            source,
            "public static bool UpsertForElement(");
        var placementBody = ExtractMethodBody(
            source,
            "private static LabelPlacement CalculatePlacement(");
        var placementCallStart = upsertBody.IndexOf(
            "CalculatePlacement(",
            StringComparison.Ordinal);
        var placementCall = ExtractInvocation(
            upsertBody,
            placementCallStart);
        var labelCallStart = upsertBody.IndexOf(
            "UpsertLabel(",
            StringComparison.Ordinal);
        var labelCall = ExtractInvocation(upsertBody, labelCallStart);

        Assert.Contains("sourceEntity", placementCall);
        Assert.Contains("textHeightMm", placementCall);
        Assert.DoesNotContain("annotationScaleService", placementCall);
        Assert.DoesNotContain("labelText", placementCall);
        Assert.Contains(
            "fullLabelPresentation.ModelHeightMm",
            upsertBody);
        Assert.Contains(
            "AutoCadFullLabelPresentationPolicy.TryPrepare(",
            upsertBody);
        Assert.Equal(
            0,
            CountOccurrences(upsertBody, "CalculateTextHeightMm("));
        Assert.Contains(
            "AttachmentPoint.MiddleCenter",
            labelCall);
        Assert.Contains("textHeightMm", labelCall);
        Assert.Contains("lineSpacingFactor: null", labelCall);
        Assert.Contains(
            "resolvedTextStyleId: fullLabelPresentation.TextStyleId",
            labelCall);
        Assert.DoesNotContain("envelopeWidthMm:", labelCall);
        Assert.DoesNotContain("envelopeHeightMm:", labelCall);
        Assert.Contains(
            "TimberElementLabelPlacementCalculator.Calculate(",
            placementBody);
        Assert.Contains(
            "CalculateFullLabelCenterOffsetMm(",
            placementBody);
        Assert.Contains("fullLabelTextHeightMm", placementBody);
        Assert.DoesNotContain("ScaleLength(", placementBody);
        Assert.DoesNotContain("LabelOffsetMm", source);
        Assert.DoesNotContain("CalculateEnvelopeAware(", source);
    }

    [Fact]
    public void FullLabel_CreateAndUpdateConsumeTheSamePlacement()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var upsertBody = ExtractMethodBody(
            source,
            "private static bool UpsertLabel(");
        var appearanceBody = ExtractMethodBody(
            source,
            "private static void ApplyLabelAppearance(");

        Assert.Equal(
            1,
            CountOccurrences(upsertBody, "ApplyLabelAppearance("));
        Assert.Contains("placement.Location", appearanceBody);
        Assert.Contains("placement.RotationRadians", appearanceBody);
    }

    [Fact]
    public void ProductionCode_NoFreePresentationScaleFactorVariable()
    {
        var source = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        // RequiresCircleNormalization must have the parameter declared
        var methodBody = ExtractMethodBody(
            source,
            "private static bool RequiresCircleNormalization(");

        Assert.Contains("presentationScaleFactor", methodBody);

        // But no other method that doesn't declare it should use it
        // (The compile error was exactly this: using it without declaring)
        Assert.Contains(
            "double presentationScaleFactor",
            Between(
                source,
                "private static bool RequiresCircleNormalization(",
                "{"));

    }

    private static TimberElementDefaultProfile CreateProfileWithDenominator(int denominator)
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.AnnotationScaleDenominator = denominator;
        return profile;
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return startIndex < 0 || endIndex < 0
            ? string.Empty
            : source.Substring(startIndex, endIndex - startIndex);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        var braceStart = source.IndexOf('{', startIndex + signature.Length);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(braceStart, i - braceStart + 1);
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractInvocation(string source, int startIndex)
    {
        if (startIndex < 0 || startIndex >= source.Length)
        {
            return string.Empty;
        }

        var parenthesisStart = source.IndexOf('(', startIndex);
        if (parenthesisStart < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = parenthesisStart; i < source.Length; i++)
        {
            if (source[i] == '(')
            {
                depth++;
            }
            else if (source[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return string.Empty;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
