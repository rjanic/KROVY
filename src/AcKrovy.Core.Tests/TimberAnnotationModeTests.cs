using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationModeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void LegacyJsonWithoutAnnotationMode_DefaultsToFullLabel()
    {
        const string json =
            """{"SchemaVersion":3,"ElementId":"K1","ElementType":"Rafter","WidthMm":80,"HeightMm":160}""";

        var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.NotNull(data);
        Assert.Equal(TimberAnnotationMode.FullLabel, data!.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Plain, data.ItemNumberLeaderStyle);
    }

    [Fact]
    public void LegacyItemLeaderWithoutStyle_DefaultsToPlain()
    {
        const string json =
            """{"SchemaVersion":4,"ElementId":"K1","ElementType":"Rafter","AnnotationMode":"ItemNumberLeader","WidthMm":80,"HeightMm":160}""";

        var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.NotNull(data);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, data!.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Plain, data.ItemNumberLeaderStyle);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel, "K1\\P80x160\\P4300 mm")]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, "K1")]
    [InlineData(TimberAnnotationMode.DimensionsLeader, "80x160")]
    public void Formatter_UsesExactlyRequestedContent(TimberAnnotationMode mode, string expected)
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            WidthMm = 80,
            HeightMm = 160,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 4200,
            AnnotationMode = mode,
        };

        var text = TimberMainAnnotationFormatter.Format(data, TimberCalculator.Measure(data, null));

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, "PR2")]
    [InlineData(TimberAnnotationMode.DimensionsLeader, "100x200")]
    public void CustomShortModes_DoNotExposeCustomTypeName(
        TimberAnnotationMode mode,
        string expected)
    {
        var data = TimberElementDefaults.For(TimberElementType.Custom) with
        {
            ElementId = "PR2",
            CustomElementTypeId = "custom-1",
            CustomElementTypeName = "Hlavný prievlak",
            CustomElementTypePrefix = "PR",
            WidthMm = 100,
            HeightMm = 200,
            AnnotationMode = mode,
        };

        var text = TimberMainAnnotationFormatter.Format(data, TimberCalculator.Measure(data, 7900));

        Assert.Equal(expected, text);
        Assert.DoesNotContain("prievlak", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepresentationRules_ReplaceOnlyWhenEntityKindChanges()
    {
        Assert.False(TimberAnnotationModeRules.RequiresReplacement(
            TimberMainAnnotationRepresentation.FullLabel,
            TimberAnnotationMode.FullLabel));
        Assert.True(TimberAnnotationModeRules.RequiresReplacement(
            TimberMainAnnotationRepresentation.FullLabel,
            TimberAnnotationMode.ItemNumberLeader));
        Assert.False(TimberAnnotationModeRules.RequiresReplacement(
            TimberMainAnnotationRepresentation.Leader,
            TimberAnnotationMode.DimensionsLeader));
        Assert.True(TimberAnnotationModeRules.RequiresReplacement(
            TimberMainAnnotationRepresentation.Leader,
            TimberAnnotationMode.FullLabel));
        Assert.True(TimberAnnotationModeRules.RequiresItemLeaderReplacement(
            ItemNumberLeaderStyle.Plain,
            ItemNumberLeaderStyle.Circle));
        Assert.True(TimberAnnotationModeRules.RequiresItemLeaderReplacement(
            ItemNumberLeaderStyle.Circle,
            ItemNumberLeaderStyle.Slot));
        Assert.False(TimberAnnotationModeRules.RequiresItemLeaderReplacement(
            ItemNumberLeaderStyle.Slot,
            ItemNumberLeaderStyle.Slot));
    }

    [Theory]
    [InlineData(
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Plain,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Circle)]
    [InlineData(
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Plain,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Slot)]
    [InlineData(
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Circle,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Slot)]
    [InlineData(
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Slot,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Circle)]
    [InlineData(
        TimberAnnotationMode.DimensionsLeader,
        ItemNumberLeaderStyle.Plain,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Circle)]
    [InlineData(
        TimberAnnotationMode.DimensionsLeader,
        ItemNumberLeaderStyle.Plain,
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Slot)]
    public void FramedItemLeaderTransitions_AlwaysRecreateTheMLeader(
        TimberAnnotationMode existingMode,
        ItemNumberLeaderStyle existingStyle,
        TimberAnnotationMode desiredMode,
        ItemNumberLeaderStyle desiredStyle)
    {
        Assert.True(TimberAnnotationModeRules.RequiresLeaderRecreation(
            existingMode,
            existingStyle,
            desiredMode,
            desiredStyle));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedItemLeaderToPlain_RequiresFreshMTextContentLeader(
        ItemNumberLeaderStyle existingStyle)
    {
        Assert.Equal(
            TimberMainAnnotationRepresentation.BlockLeader,
            TimberAnnotationModeRules.GetRepresentation(
                TimberAnnotationMode.ItemNumberLeader,
                existingStyle));
        Assert.Equal(
            TimberMainAnnotationRepresentation.Leader,
            TimberAnnotationModeRules.GetRepresentation(
                TimberAnnotationMode.ItemNumberLeader,
                ItemNumberLeaderStyle.Plain));
        Assert.True(TimberAnnotationModeRules.RequiresLeaderRecreation(
            TimberAnnotationMode.ItemNumberLeader,
            existingStyle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedItemLeaderToFullLabel_ChangesMainRepresentation(
        ItemNumberLeaderStyle style)
    {
        Assert.True(TimberAnnotationModeRules.RequiresReplacement(
            TimberMainAnnotationRepresentation.Leader,
            TimberAnnotationMode.FullLabel));
        Assert.False(TimberAnnotationModeRules.RequiresLeaderRecreation(
            TimberAnnotationMode.ItemNumberLeader,
            style,
            TimberAnnotationMode.ItemNumberLeader,
            style));
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain,
        TimberMainAnnotationRepresentation.FullLabel)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain,
        TimberMainAnnotationRepresentation.Leader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain,
        TimberMainAnnotationRepresentation.Leader)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Circle,
        TimberMainAnnotationRepresentation.BlockLeader)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Slot,
        TimberMainAnnotationRepresentation.BlockLeader)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Rectangle,
        TimberMainAnnotationRepresentation.BlockLeader)]
    public void RepresentationRules_UseDedicatedCircleComposition(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationRepresentation expected)
    {
        Assert.Equal(expected, TimberAnnotationModeRules.GetRepresentation(mode, style));
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain)]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain)]
    public void ExistingMainRepresentations_CanTransitionToDedicatedCircle(
        TimberAnnotationMode existingMode,
        ItemNumberLeaderStyle existingStyle)
    {
        var existing = TimberAnnotationModeRules.GetRepresentation(
            existingMode,
            existingStyle);
        var circle = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle);

        Assert.NotEqual(circle, existing);
        Assert.Equal(TimberMainAnnotationRepresentation.BlockLeader, circle);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain)]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain)]
    public void DedicatedCircle_CanTransitionBackWithoutSharingEntityKind(
        TimberAnnotationMode desiredMode,
        ItemNumberLeaderStyle desiredStyle)
    {
        var circle = TimberAnnotationModeRules.GetRepresentation(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle);
        var desired = TimberAnnotationModeRules.GetRepresentation(
            desiredMode,
            desiredStyle);

        Assert.NotEqual(circle, desired);
    }

    [Fact]
    public void InvalidPersistedMode_IsSafelyNormalizedToFullLabel()
    {
        Assert.Equal(
            TimberAnnotationMode.FullLabel,
            TimberAnnotationModeRules.Normalize((TimberAnnotationMode)999));
    }

    [Fact]
    public void DefaultProfile_ControlsNewElementsWithoutMutatingExistingOnes()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Rafter);
        var profile = new TimberElementDefaultProfile
        {
            DefaultAnnotationMode = TimberAnnotationMode.DimensionsLeader,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Slot,
        };

        var created = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        Assert.Equal(TimberAnnotationMode.FullLabel, existing.AnnotationMode);
        Assert.Equal(TimberAnnotationMode.DimensionsLeader, created.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Slot, created.ItemNumberLeaderStyle);
    }

    [Fact]
    public void ApplyAnnotationMode_PreservesManufacturingIdentity()
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K7",
            LengthCalculationMode = LengthCalculationMode.PlanLength,
        };
        var before = TimberElementSignature.FromMeasurement(TimberCalculator.Measure(data, 4200));
        var profile = new TimberElementDefaultProfile
        {
            DefaultAnnotationMode = TimberAnnotationMode.ItemNumberLeader,
            DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Circle,
        };

        var updated = TimberElementDefaultApplicator.ApplyAnnotationMode(data, profile);
        var after = TimberElementSignature.FromMeasurement(TimberCalculator.Measure(updated, 4200));

        Assert.Equal(before, after);
        Assert.Equal("K7", updated.ElementId);
        Assert.Equal(ItemNumberLeaderStyle.Circle, updated.ItemNumberLeaderStyle);
    }

    [Fact]
    public void PersistedMode_RoundTripsAsLanguageNeutralSchemaValue()
    {
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = TimberAnnotationMode.DimensionsLeader,
        };

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var loaded = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.Contains("\"AnnotationMode\":\"DimensionsLeader\"", json);
        Assert.NotNull(loaded);
        Assert.Equal(TimberAnnotationMode.DimensionsLeader, loaded!.AnnotationMode);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Plain)]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void PersistedItemStyle_RoundTripsAsLanguageNeutralSchemaValue(
        ItemNumberLeaderStyle style)
    {
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle = style,
        };

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var loaded = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.Contains($"\"ItemNumberLeaderStyle\":\"{style}\"", json);
        Assert.NotNull(loaded);
        Assert.Equal(style, loaded!.ItemNumberLeaderStyle);
    }

    [Fact]
    public void Renumbering_ChangesItemLeaderButNotDimensionsLeaderText()
    {
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K9",
            WidthMm = 80,
            HeightMm = 160,
        };
        var renumbered = source with { ElementId = "K2" };
        var sourceMeasurement = TimberCalculator.Measure(source, 4200);
        var renumberedMeasurement = TimberCalculator.Measure(renumbered, 4200);

        Assert.NotEqual(
            TimberMainAnnotationFormatter.Format(
                source with { AnnotationMode = TimberAnnotationMode.ItemNumberLeader },
                sourceMeasurement),
            TimberMainAnnotationFormatter.Format(
                renumbered with { AnnotationMode = TimberAnnotationMode.ItemNumberLeader },
                renumberedMeasurement));
        Assert.Equal(
            TimberMainAnnotationFormatter.Format(
                source with { AnnotationMode = TimberAnnotationMode.DimensionsLeader },
                sourceMeasurement),
            TimberMainAnnotationFormatter.Format(
                renumbered with { AnnotationMode = TimberAnnotationMode.DimensionsLeader },
                renumberedMeasurement));
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    public void SlopeRefreshPlan_IsIndependentOfMainAnnotationMode(TimberAnnotationMode mode)
    {
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SlopeDegrees = 35,
            AnnotationMode = mode,
        };

        var plan = TimberAnnotationRefreshPlanner.Create(data);

        Assert.True(plan.EnsureLabel);
        Assert.True(plan.ReconcileSlopeArrow);
        Assert.True(plan.ReconcileSlopeAngleText);
    }

    [Fact]
    public void LinearLeaderPlacement_UsesMidpointAnchorAndStableRotation()
    {
        var horizontal = TimberLeaderPlacementCalculator.CalculateLinear(0, 0, 4000, 0, 2000, 0);
        var vertical = TimberLeaderPlacementCalculator.CalculateLinear(0, 0, 0, 4000, 0, 2000);
        var reversed = TimberLeaderPlacementCalculator.CalculateLinear(4000, 0, 0, 0, 2000, 0);

        Assert.Equal((2000d, 0d), (horizontal.AnchorX, horizontal.AnchorY));
        Assert.Equal((0d, 2000d), (vertical.AnchorX, vertical.AnchorY));
        Assert.Equal(horizontal.AnchorX, reversed.AnchorX);
        Assert.Equal(horizontal.AnchorY, reversed.AnchorY);
        Assert.Equal(horizontal.RotationRadians, reversed.RotationRadians, 10);
    }

    [Fact]
    public void PostLeaderPlacement_UsesTopCenterOfFootprint()
    {
        var bounds = new TimberRectangularFootprintBounds(100, 200, 240, 400);

        var placement = TimberLeaderPlacementCalculator.CalculatePost(bounds);

        Assert.Equal(170, placement.AnchorX);
        Assert.Equal(400, placement.AnchorY);
        Assert.Equal(170, placement.TextX);
        Assert.True(placement.TextY > placement.AnchorY);
        Assert.Equal(0, placement.RotationRadians);
    }

    [Fact]
    public void ItemLeaderLayout_SupportsReadableLeftAndRightLandings()
    {
        var right = TimberItemLeaderLayoutCalculator.Calculate(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "K1",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);
        var left = TimberItemLeaderLayoutCalculator.Calculate(
            new TimberLeaderPlacement(0, 0, -800, 360, 0),
            "K1",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Left);

        Assert.Equal(TimberLeaderHorizontalSide.Right, right.Side);
        Assert.True(right.ContentX > right.AnchorX);
        Assert.Equal(TimberLeaderHorizontalSide.Left, left.Side);
        Assert.True(left.ContentX < left.AnchorX);
        Assert.Equal(right.ContentY, left.ContentY);
    }

    [Fact]
    public void NativeLeaderFirstSegment_UsesSixtyDegreesOnRight()
    {
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            100,
            200,
            TimberLeaderHorizontalSide.Right,
            180);

        var angleDegrees = MeasureAngleDegrees(100, 200, knee.X, knee.Y);

        Assert.Equal(60d, angleDegrees, 10);
        Assert.Equal(60d, MeasureAcuteAngleDegrees(100, 200, knee.X, knee.Y), 10);
    }

    [Fact]
    public void NativeLeaderFirstSegment_UsesOneHundredTwentyDegreesOnLeft()
    {
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            100,
            200,
            TimberLeaderHorizontalSide.Left,
            180);

        var angleDegrees = MeasureAngleDegrees(100, 200, knee.X, knee.Y);

        Assert.Equal(120d, angleDegrees, 10);
        Assert.Equal(60d, MeasureAcuteAngleDegrees(100, 200, knee.X, knee.Y), 10);
    }

    [Theory]
    [InlineData(TimberLeaderHorizontalSide.Right, 300d)]
    [InlineData(TimberLeaderHorizontalSide.Left, 240d)]
    public void NativeLeaderFirstSegment_DownwardDirectionsKeepAcuteSixtyDegrees(
        TimberLeaderHorizontalSide side,
        double expectedOrientedAngleDegrees)
    {
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            100,
            200,
            side,
            360,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Down);

        Assert.Equal(
            expectedOrientedAngleDegrees,
            MeasureAngleDegrees(100, 200, knee.X, knee.Y),
            10);
        Assert.Equal(60d, MeasureAcuteAngleDegrees(100, 200, knee.X, knee.Y), 10);
    }

    [Theory]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(360d)]
    public void NativeLeaderFirstSegment_DifferentRunsPreserveSixtyDegrees(
        double horizontalRunMm)
    {
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            -500,
            750,
            TimberLeaderHorizontalSide.Right,
            horizontalRunMm);

        Assert.Equal(
            60d,
            MeasureAngleDegrees(-500, 750, knee.X, knee.Y),
            10);
    }

    [Fact]
    public void NativeLeaderFirstSegment_OppositeVertexOrderKeepsAcuteAngle()
    {
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            100,
            200,
            TimberLeaderHorizontalSide.Right,
            360);

        Assert.Equal(
            60d,
            MeasureAcuteAngleDegrees(knee.X, knee.Y, 100, 200),
            10);
    }

    [Fact]
    public void NativeLeaderFirstSegment_RotatedAnnotationPlaneKeepsLocalSixtyDegrees()
    {
        var rotation = 37d * Math.PI / 180d;
        var horizontalX = Math.Cos(rotation);
        var horizontalY = Math.Sin(rotation);
        var verticalX = -Math.Sin(rotation);
        var verticalY = Math.Cos(rotation);
        var knee = TimberItemLeaderLayoutCalculator.CalculateKnee(
            250,
            400,
            TimberLeaderHorizontalSide.Right,
            360,
            new TimberLeaderPlaneBasis(
                horizontalX,
                horizontalY,
                verticalX,
                verticalY));
        var segmentX = knee.X - 250d;
        var segmentY = knee.Y - 400d;

        Assert.Equal(
            60d,
            TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
                segmentX,
                segmentY,
                horizontalX,
                horizontalY) * 180d / Math.PI,
            10);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    public void NativeLeaderModes_UseSameSixtyDegreeGeometry(
        TimberAnnotationMode mode)
    {
        Assert.True(TimberNativeLeaderStyleRules.UsesDedicatedStyle(mode));
        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            mode == TimberAnnotationMode.ItemNumberLeader ? "K1" : "160x200",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(
            60d,
            MeasureAngleDegrees(
                layout.AnchorX,
                layout.AnchorY,
                layout.KneeX,
                layout.KneeY),
            10);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Plain)]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void PostAndCustomSupportEveryItemLeaderStyle(ItemNumberLeaderStyle style)
    {
        var post = TimberElementDefaults.For(TimberElementType.Post) with
        {
            ElementId = "S1",
            AnnotationMode = TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle = style,
        };
        var custom = TimberElementDefaults.For(TimberElementType.Custom) with
        {
            ElementId = "PR2",
            CustomElementTypeId = "custom-1",
            CustomElementTypeName = "Prievlak",
            CustomElementTypePrefix = "PR",
            AnnotationMode = TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle = style,
        };

        Assert.Equal("S1", TimberMainAnnotationFormatter.Format(post, TimberCalculator.Measure(post, null)));
        Assert.Equal("PR2", TimberMainAnnotationFormatter.Format(custom, TimberCalculator.Measure(custom, 7900)));
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle, "ACAD_KROVY_ITEM_CIRCLE")]
    [InlineData(ItemNumberLeaderStyle.Slot, "ACAD_KROVY_ITEM_SLOT")]
    [InlineData(ItemNumberLeaderStyle.Rectangle, "ACAD_KROVY_ITEM_RECTANGLE")]
    public void BlockDefinitions_UseStableLanguageNeutralNames(
        ItemNumberLeaderStyle style,
        string expectedName)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");

        Assert.Equal(expectedName, definition.BlockName);
        Assert.Equal(TimberItemLeaderBlockSize.Small, definition.Size);
        Assert.Equal(
            TimberMainAnnotationTextRules.TextHeightMm,
            definition.TextHeightMm);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void StandaloneFramedItemLeadersUseSplineAndInsertionPointAttachment(
        ItemNumberLeaderStyle style)
    {
        Assert.True(TimberNativeLeaderStyleRules.UsesSplineLeader(style));
        Assert.True(
            TimberNativeLeaderStyleRules.UsesInsertionPointBlockAttachment(style));
        Assert.False(
            TimberNativeLeaderStyleRules.UsesCenterExtentsBlockAttachment(style));
    }

    [Fact]
    public void PlainItemLeaderRemainsStraightAndDoesNotUseBlockAttachment()
    {
        Assert.False(
            TimberNativeLeaderStyleRules.UsesSplineLeader(
                ItemNumberLeaderStyle.Plain));
        Assert.False(
            TimberNativeLeaderStyleRules.UsesInsertionPointBlockAttachment(
                ItemNumberLeaderStyle.Plain));
        Assert.False(
            TimberNativeLeaderStyleRules.UsesCenterExtentsBlockAttachment(
                ItemNumberLeaderStyle.Plain));
    }

    [Fact]
    public void SlotDefinition_IsWiderThanHighAndIsNotEllipseStyle()
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Slot,
            "PR2");

        Assert.Equal(ItemNumberLeaderStyle.Slot, definition.Style);
        Assert.True(definition.WidthMm > definition.HeightMm);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules.FrameHeightMm,
            definition.HeightMm);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void LinearBlockDefinitions_SelectPortableSizeVariantForLongItemNumbers(
        ItemNumberLeaderStyle style)
    {
        var shortDefinition = TimberItemLeaderBlockDefinitionRules.Resolve(style, "K1");
        var longDefinition = TimberItemLeaderBlockDefinitionRules.Resolve(
            style,
            "PREFIX123456");

        Assert.NotEqual(shortDefinition.Size, longDefinition.Size);
        Assert.EndsWith("_L", longDefinition.BlockName, StringComparison.Ordinal);
        Assert.True(longDefinition.WidthMm > shortDefinition.WidthMm);
    }

    [Theory]
    [InlineData("K1")]
    [InlineData("KL1")]
    [InlineData("V1")]
    [InlineData("VT1")]
    [InlineData("S1")]
    public void CircleDefinition_UsesOneReferenceDiameterAndBlockScale(string itemNumber)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            itemNumber);

        Assert.Equal(TimberItemLeaderBlockSize.Small, definition.Size);
        Assert.Equal("ACAD_KROVY_ITEM_CIRCLE", definition.BlockName);
        Assert.Equal(TimberItemLeaderBlockDefinitionRules.CircleDiameterMm, definition.WidthMm);
        Assert.Equal(definition.WidthMm, definition.HeightMm);
        Assert.Equal(520d, definition.WidthMm);
        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);
    }

    [Theory]
    [InlineData("K")]
    [InlineData("K1")]
    [InlineData("KL1")]
    public void CircleDefinition_DoesNotDependOnNormalItemNumberLength(string itemNumber)
    {
        var reference = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");
        var actual = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            itemNumber);

        Assert.Equal(reference, actual);
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, "K1")]
    [InlineData(TimberElementType.WallPlate, "P1")]
    [InlineData(TimberElementType.Post, "S1")]
    [InlineData(TimberElementType.CollarTie, "KL1")]
    [InlineData(TimberElementType.Brace, "V1")]
    [InlineData(TimberElementType.Custom, "VT1")]
    public void CircleDefinition_DoesNotDependOnElementType(
        TimberElementType elementType,
        string itemNumber)
    {
        _ = elementType;
        var reference = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");

        Assert.Equal(reference, TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            itemNumber));
    }

    [Fact]
    public void CircleCompatibility_RejectsLegacyExpandedDiameter()
    {
        Assert.True(TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(520d));
        Assert.False(TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(760d));
        Assert.False(TimberItemLeaderBlockDefinitionRules.HasExpectedCircleDiameter(1800d));
    }

    [Fact]
    public void RenumberAndCopyTexts_KeepTheSameCircleDefinition()
    {
        var reference = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K1");

        Assert.Equal(reference, TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "K99"));
        Assert.Equal(reference, TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "KL1"));
        Assert.Equal(reference, TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "VT1"));
    }

    [Fact]
    public void CircleFix_DoesNotChangeSlotOrRectangleSizing()
    {
        var shortSlot = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Slot,
            "K1");
        var shortRectangle = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "K1");
        var longSlot = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Slot,
            "PREFIX123456");
        var longRectangle = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "PREFIX123456");

        Assert.Equal((600d, 360d, TimberItemLeaderBlockSize.Small),
            (shortSlot.WidthMm, shortSlot.HeightMm, shortSlot.Size));
        Assert.Equal((600d, 360d, TimberItemLeaderBlockSize.Small),
            (shortRectangle.WidthMm, shortRectangle.HeightMm, shortRectangle.Size));
        Assert.Equal((1600d, 360d, TimberItemLeaderBlockSize.Large),
            (longSlot.WidthMm, longSlot.HeightMm, longSlot.Size));
        Assert.Equal((1600d, 360d, TimberItemLeaderBlockSize.Large),
            (longRectangle.WidthMm, longRectangle.HeightMm, longRectangle.Size));
    }

    [Fact]
    public void CircleItemAttribute_RemainsCenteredAtTheExistingTextHeight()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AcKrovyItemLeaderBlockService.cs"));

        Assert.Contains("attribute.Height = textHeight;", source);
        Assert.Contains("TextHorizontalMode.TextCenter", source);
        Assert.Contains("TextVerticalMode.TextVerticalMid", source);
        Assert.Contains("attribute.AlignmentPoint = Point3d.Origin;", source);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void BlockLayout_FirstSegmentUsesSixtyDegrees(
        ItemNumberLeaderStyle style)
    {
        var right = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(100, 200, 100, 560, 0),
            "PR2",
            style,
            TimberLeaderHorizontalSide.Right);
        var left = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(100, 200, 100, 560, 0),
            "PR2",
            style,
            TimberLeaderHorizontalSide.Left);

        Assert.Equal(60d, MeasureAngleDegrees(
            right.AnchorX, right.AnchorY, right.KneeX, right.KneeY), 10);
        Assert.Equal(120d, MeasureAngleDegrees(
            left.AnchorX, left.AnchorY, left.KneeX, left.KneeY), 10);
        Assert.Equal(right.KneeY, right.ContentY, 10);
        Assert.Equal(left.KneeY, left.ContentY, 10);
        Assert.Equal(right.KneeX, right.ContentX, 10);
        Assert.Equal(left.KneeX, left.ContentX, 10);
    }

    [Fact]
    public void LegacyEllipseSchemaValue_IsInterpretedAndRewrittenAsSlot()
    {
        const string json =
            """{"SchemaVersion":4,"ElementId":"K1","ElementType":"Rafter","AnnotationMode":"ItemNumberLeader","ItemNumberLeaderStyle":"Ellipse"}""";

        var loaded = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);
        var rewritten = JsonSerializer.Serialize(loaded, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(ItemNumberLeaderStyle.Slot, loaded!.ItemNumberLeaderStyle);
        Assert.Contains("\"ItemNumberLeaderStyle\":\"Slot\"", rewritten);
    }

    [Fact]
    public void PlainAndDimensionsGeometry_RemainsIndependentOfCircleLayout()
    {
        var placement = new TimberLeaderPlacement(0, 0, 0, 360, 0);
        var plain = TimberItemLeaderLayoutCalculator.Calculate(
            placement,
            "K1",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);
        _ = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "K1",
            ItemNumberLeaderStyle.Circle,
            TimberLeaderHorizontalSide.Right);
        var dimensions = TimberItemLeaderLayoutCalculator.Calculate(
            placement,
            "160x200",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(60d, MeasureAcuteAngleDegrees(
            plain.AnchorX, plain.AnchorY, plain.KneeX, plain.KneeY), 10);
        Assert.Equal(60d, MeasureAcuteAngleDegrees(
            dimensions.AnchorX,
            dimensions.AnchorY,
            dimensions.KneeX,
            dimensions.KneeY), 10);
        Assert.Equal(
            TimberMainAnnotationTextRules.TextHeightMm,
            TimberItemLeaderLayoutCalculator.TextHeightMm);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    public void PlainAndDimensionsLeaderUseDedicatedNativeStyle(
        TimberAnnotationMode mode)
    {
        Assert.True(TimberNativeLeaderStyleRules.UsesDedicatedStyle(mode));
        Assert.Equal(
            "ACAD_KROVY_LEADER",
            TimberNativeLeaderStyleRules.Settings.StyleName);
    }

    [Fact]
    public void NativeLeaderStyleMatchesRequiredAutoCadProperties()
    {
        var settings = TimberNativeLeaderStyleRules.Settings;

        Assert.True(settings.UsesStraightLeader);
        Assert.True(settings.LeaderColorIsByBlock);
        Assert.True(settings.LeaderLinetypeIsByBlock);
        Assert.True(settings.LeaderLineweightIsByBlock);
        Assert.Equal(TimberMainAnnotationTextRules.TextHeightMm, settings.TextHeightMm);
        Assert.Equal(180d, settings.TextHeightMm);
        Assert.Equal(1d, settings.Scale);
        Assert.False(settings.UsesAnnotationScale);
        Assert.Equal(60, settings.FirstSegmentAngleDegrees);
        Assert.False(settings.HasArrowhead);
        Assert.Equal("_None", settings.NoneArrowBlockName);
        Assert.Equal(0.08d, settings.ArrowheadSize);
        Assert.True(settings.HasHorizontalLanding);
        Assert.Equal(0d, settings.LandingDistance);
        Assert.False(TimberNativeLeaderStyleRules.RequiresExplicitDoglegDirection);
        Assert.False(settings.ExtendsLeaderToText);
        Assert.Equal(
            TimberNativeLeaderTextAttachment.UnderlineBottomLine,
            settings.LeftTextAttachment);
        Assert.Equal(
            TimberNativeLeaderTextAttachment.UnderlineBottomLine,
            settings.RightTextAttachment);
    }

    [Fact]
    public void StandaloneFramedNativeLeaderStyleUsesOriginalSplineContract()
    {
        var settings = TimberNativeLeaderStyleRules.FramedSettings;

        Assert.False(settings.UsesStraightLeader);
        Assert.False(settings.HasArrowhead);
        Assert.Equal(0.08d, settings.ArrowheadSize);
        Assert.True(settings.HasHorizontalLanding);
        Assert.Equal(0d, settings.LandingDistance);
        Assert.False(settings.ExtendsLeaderToText);
    }

    [Fact]
    public void CombinedFramedNativeLeaderStyleUsesStraightClosedFilledLeader()
    {
        var settings = TimberNativeLeaderStyleRules.CombinedFramedSettings;

        Assert.True(settings.UsesStraightLeader);
        Assert.True(settings.HasArrowhead);
        Assert.Equal(350d, settings.LandingDistance);
        Assert.Equal(60, settings.FirstSegmentAngleDegrees);
        Assert.True(settings.ExtendsLeaderToText);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    public void NativeLeaderModes_UseFullLabelReferenceTextHeight(
        TimberAnnotationMode mode)
    {
        Assert.True(TimberNativeLeaderStyleRules.UsesDedicatedStyle(mode));
        Assert.Equal(
            TimberMainAnnotationTextRules.TextHeightMm,
            TimberNativeLeaderStyleRules.Settings.TextHeightMm);
        Assert.Equal(
            TimberMainAnnotationTextRules.TextHeightMm,
            TimberItemLeaderLayoutCalculator.TextHeightMm);
    }

    [Theory]
    [InlineData(TimberLeaderHorizontalSide.Right)]
    [InlineData(TimberLeaderHorizontalSide.Left)]
    public void NativeLeaderUnderlinesBottomLineOnBothSides(
        TimberLeaderHorizontalSide contentSide)
    {
        Assert.Equal(
            TimberNativeLeaderTextAttachment.UnderlineBottomLine,
            TimberNativeLeaderStyleRules.GetTextAttachment(contentSide));
    }

    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    public void RepeatedNativeLeaderReconcileDoesNotRequireReplacement(
        TimberAnnotationMode mode)
    {
        Assert.False(TimberAnnotationModeRules.RequiresLeaderRecreation(
            mode,
            ItemNumberLeaderStyle.Plain,
            mode,
            ItemNumberLeaderStyle.Plain));
    }

    [Theory]
    [InlineData("sk", "Kompletný popis", "Iba číslo položky", "Iba rozmery")]
    [InlineData("cs", "Úplný popis", "Pouze číslo položky", "Pouze rozměry")]
    [InlineData("en", "Full label", "Item number only", "Dimensions only")]
    [InlineData("de", "Vollständige Beschriftung", "Nur Positionsnummer", "Nur Abmessungen")]
    [InlineData("pl", "Pełny opis", "Tylko numer pozycji", "Tylko wymiary")]
    [InlineData("fr", "Étiquette complète", "Repère uniquement", "Dimensions uniquement")]
    public void DisplayNames_AreLocalizedWithoutChangingMode(
        string language,
        string full,
        string item,
        string dimensions)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        Assert.Equal(full, TimberAnnotationModeDisplayNameProvider.GetDisplayName(
            TimberAnnotationMode.FullLabel, culture));
        Assert.Equal(item, TimberAnnotationModeDisplayNameProvider.GetDisplayName(
            TimberAnnotationMode.ItemNumberLeader, culture));
        Assert.Equal(dimensions, TimberAnnotationModeDisplayNameProvider.GetDisplayName(
            TimberAnnotationMode.DimensionsLeader, culture));
    }

    private static double MeasureAngleDegrees(
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var angle = Math.Atan2(endY - startY, endX - startX) * 180d / Math.PI;
        return angle < 0d ? angle + 360d : angle;
    }

    private static double MeasureAcuteAngleDegrees(
        double startX,
        double startY,
        double endX,
        double endY) =>
        TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
            endX - startX,
            endY - startY,
            localHorizontalX: 1d,
            localHorizontalY: 0d) * 180d / Math.PI;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate AcKrovy.sln.");
    }

    [Theory]
    [InlineData("sk", "Bez rámčeka", "Kruh", "Slot", "Obdĺžnik")]
    [InlineData("cs", "Bez rámečku", "Kruh", "Slot", "Obdélník")]
    [InlineData("en", "No frame", "Circle", "Slot", "Rectangle")]
    [InlineData("de", "Ohne Rahmen", "Kreis", "Langloch", "Rechteck")]
    [InlineData("pl", "Bez ramki", "Okrąg", "Kapsuła", "Prostokąt")]
    [InlineData("fr", "Sans cadre", "Cercle", "Capsule", "Rectangle")]
    public void ItemStyleDisplayNames_AreLocalizedWithoutChangingStyle(
        string language,
        string plain,
        string circle,
        string slot,
        string rectangle)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        Assert.Equal(plain, ItemNumberLeaderStyleDisplayNameProvider.GetDisplayName(
            ItemNumberLeaderStyle.Plain, culture));
        Assert.Equal(circle, ItemNumberLeaderStyleDisplayNameProvider.GetDisplayName(
            ItemNumberLeaderStyle.Circle, culture));
        Assert.Equal(slot, ItemNumberLeaderStyleDisplayNameProvider.GetDisplayName(
            ItemNumberLeaderStyle.Slot, culture));
        Assert.Equal(rectangle, ItemNumberLeaderStyleDisplayNameProvider.GetDisplayName(
            ItemNumberLeaderStyle.Rectangle, culture));
    }
}
