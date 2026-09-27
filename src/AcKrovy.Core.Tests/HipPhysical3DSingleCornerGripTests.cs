using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Single-corner GRIP_STRETCH of a rectangular Physical3D hip: compact Edge01/Edge12
/// stays RigidEquivalent while the footprint becomes non-rectangular. Host must still
/// refresh 2D and suspend (not disable) physical 3D until the rectangle returns.
/// </summary>
public sealed class HipPhysical3DSingleCornerGripTests
{
    [Fact]
    public void MoveFarCorner_KeepsCompactDescriptor_ButLeavesRectangularEligibility()
    {
        var originalInput = Input(Rectangle(10000d, 6000d));
        var original = Validate(originalInput);
        var originalGeometry = Solve(original, 30d);
        var stored = RoofDefinitionPersistence.Create(originalInput, original, originalGeometry);

        // Move only V3 — Edge01/Edge12 lengths unchanged (schema-5 compact collision).
        var changedInput = Input([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(1500, 4500),
        ]);
        var changed = Validate(changedInput);
        var classification = RoofDefinitionPersistence.Classify(changedInput, changed, stored);
        var changedGeometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);

        Assert.Equal(RoofSourceChangeKind.RigidEquivalent, classification.Kind);
        Assert.Equal(10000d, stored.RigidFootprint!.Edge01LengthMm, 9);
        Assert.Equal(6000d, stored.RigidFootprint.Edge12LengthMm, 9);
        Assert.False(RectangularRoofFootprintRules.IsRectangular(changed));
        Assert.False(
            RectangularSymmetricHipEligibility.Evaluate(changed, changedGeometry).IsEligible);

        var oldSignature = RoofWireframe.BuildGenerationSignature(
            HipRoofWireframe.Create(originalGeometry, 0d));
        var newSignature = RoofWireframe.BuildGenerationSignature(
            HipRoofWireframe.Create(changedGeometry, 0d));
        Assert.NotEqual(oldSignature, newSignature);

        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            halfRoofWidthMm: 3000d,
            pitchDegrees: 30d,
            physical3DEnabled: true);
        Assert.True(
            RoofPhysical3DSuspensionRules.ShouldSuspendMaterialization(
                elevation.Physical3DEnabled,
                footprintEligible: false));
        Assert.True(
            RoofPhysical3DSuspensionRules.PreservePreferenceWhileSuspended(elevation)
                .Physical3DEnabled);
        Assert.True(RoofPhysical3DSuspensionRules.UseFlattenedDrawingPlane(true));
    }

    [Fact]
    public void ValidInvalidValid_RebuildsPhysicalEavesAndKeepsPlanPerimeterExcluded()
    {
        var originalInput = Input(Rectangle(10000d, 6000d));
        var irregularInput = Input([
            new RoofPoint2D(0, 0), new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000), new RoofPoint2D(1500, 4500),
        ]);
        var preference = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave, 3000d, 3000d, 45d, true);
        var stored = RoofDefinitionPersistence.Create(
            originalInput, Validate(originalInput), Solve(Validate(originalInput), 45d));

        foreach (var input in new[] { originalInput, irregularInput, originalInput })
        {
            var footprint = Validate(input);
            var geometry = Assert.IsType<HipRoofGeometry>(
                RoofDefinitionPersistence.Classify(input, footprint, stored).Geometry);
            stored = RoofDefinitionPersistence.UpdateGeometry(stored, input, geometry);
            var plan = RoofWireframe.CreateOwnedHipOrLegacy(geometry, 500d, true);
            Assert.DoesNotContain(plan, edge =>
                RoofPhysical3DPlanDisplayRules.IsSourcePerimeterDisplayRole(edge.Role));
            Assert.All(plan, edge =>
            {
                Assert.Equal(500d, edge.Segment.Start.Z);
                Assert.Equal(500d, edge.Segment.End.Z);
            });

            var eligible = RectangularSymmetricHipEligibility.Evaluate(footprint, geometry).IsEligible;
            if (!eligible)
            {
                preference = RoofPhysical3DSuspensionRules.PreservePreferenceWhileSuspended(preference);
                Assert.True(preference.Physical3DEnabled);
                Assert.False(RectangularHipRoofPhysical3DBuilder.TryBuild(
                    "AB", footprint, geometry, preference).IsValid);
                continue;
            }

            var model = RectangularHipRoofPhysical3DBuilder.TryBuild(
                "AB", footprint, geometry, preference).Model!;
            Assert.Equal(4, model.Faces.Count);
            Assert.Equal(4, model.Hips.Count);
            Assert.Equal(4, model.Eaves.Count);
            Assert.Equal(4, model.Eaves.Select(eave => eave.StructuralId).Distinct().Count());
            Assert.All(model.Eaves, eave =>
            {
                Assert.Equal(3000d, eave.Segment.Start.Z, 8);
                Assert.Equal(3000d, eave.Segment.End.Z, 8);
                var metadata = RoofPhysical3DGeneratedDataRules.Create(
                    "AB", RoofPhysical3DGeneratedRole.EaveEdge, eave.StructuralId, model.GenerationSignature);
                var decoded = RoofPhysical3DGeneratedDataRules.ValidateStored(
                    metadata.SchemaVersion, metadata.RoofOwnerReference, metadata.Role.ToString(),
                    metadata.StructuralId, metadata.GenerationSignature);
                Assert.Equal(metadata, decoded.Data);
            });
            var ridge = Assert.Single(model.Ridges).Segment;
            Assert.Equal(6000d, ridge.Start.Z, 8);
            Assert.Equal(6000d, ridge.End.Z, 8);
            Assert.Equal(4000d, ridge.LengthMm, 8);
        }
    }

    [Fact]
    public void StretchTwoCorners_RemainsEligibleRectangle_SupportedResize()
    {
        var originalInput = Input(Rectangle(10000d, 6000d));
        var original = Validate(originalInput);
        var stored = RoofDefinitionPersistence.Create(
            originalInput,
            original,
            Solve(original, 30d));
        var changedInput = Input(Rectangle(12000d, 6000d));
        var changed = Validate(changedInput);
        var classification = RoofDefinitionPersistence.Classify(changedInput, changed, stored);
        var geometry = Assert.IsType<HipRoofGeometry>(classification.Geometry);

        Assert.Equal(RoofSourceChangeKind.SupportedResize, classification.Kind);
        Assert.True(RectangularRoofFootprintRules.IsRectangular(changed));
        Assert.True(RectangularSymmetricHipEligibility.Evaluate(changed, geometry).IsEligible);

        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            30d,
            true);
        Assert.False(
            RoofPhysical3DSuspensionRules.ShouldSuspendMaterialization(
                elevation.Physical3DEnabled,
                footprintEligible: true));
    }

    [Fact]
    public void RestoreRectangle_AfterIrregular_IsEligibleAgainWithPreferencePreserved()
    {
        var irregular = Validate(Input([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(1500, 4500),
        ]));
        var restored = Validate(Input(Rectangle(10000d, 6000d)));
        var irregularGeometry = Solve(irregular, 30d);
        var restoredGeometry = Solve(restored, 30d);
        Assert.False(
            RectangularSymmetricHipEligibility.Evaluate(irregular, irregularGeometry).IsEligible);
        Assert.True(
            RectangularSymmetricHipEligibility.Evaluate(restored, restoredGeometry).IsEligible);

        var preference = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            30d,
            true,
            RoofPhysicalDisplayVisibility.Both);
        var suspended = RoofPhysical3DSuspensionRules.PreservePreferenceWhileSuspended(preference);
        Assert.True(suspended.Physical3DEnabled);
        Assert.Equal(RoofPhysicalDisplayVisibility.Both, suspended.DisplayVisibility);
        Assert.Equal(3000d, suspended.ResolvedEaveRelativeElevationMm);
    }

    [Fact]
    public void FlattenedDisplay_UsedWhilePhysical3DPreferenceOn_EvenWhenIneligible()
    {
        var irregular = Validate(Input([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(1500, 4500),
        ]));
        var geometry = Solve(irregular, 30d);
        Assert.False(RectangularSymmetricHipEligibility.Evaluate(irregular, geometry).IsEligible);

        var flat = RoofWireframe.CreateOwnedHipOrLegacy(
            geometry,
            sourceElevation: 0d,
            physical3DEnabled: true);
        Assert.All(flat, edge =>
        {
            Assert.Equal(0d, edge.Segment.Start.Z, 12);
            Assert.Equal(0d, edge.Segment.End.Z, 12);
        });
    }

    private static RoofPoint2D[] Rectangle(double width, double height) =>
        [new(0, 0), new(width, 0), new(width, height), new(0, height)];

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> points) =>
        new(points, true, false, true);

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint, double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }
}
