using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedLayoutFingerprintTests
{
    // HOST R2-S2-H3a L fixture (spacing 500): 131 ordinary / 12 Ridge↔Valley.
    private static readonly RoofPoint2D[] HostL131 =
    [
        new(0, 0), new(14600, 0), new(14600, 6500),
        new(6450, 6500), new(6450, 14600), new(0, 14600),
    ];

    [Fact]
    public void FingerprintIsDeterministicFixedSizeAndUtf8Sha256()
    {
        var signature = CreateRectangleLayout(10000, 6000, 30d, 500d).Signature;
        var first = RoofGeneratedLayoutFingerprint.Compute(signature);
        var second = RoofGeneratedLayoutFingerprint.Compute(signature);

        Assert.Equal(first, second);
        Assert.Equal(RoofGeneratedLayoutFingerprint.PersistedIdentityLength, first.Length);
        Assert.StartsWith(RoofGeneratedLayoutFingerprint.Prefix, first, StringComparison.Ordinal);
        Assert.True(RoofGeneratedLayoutFingerprint.IsFingerprint(first));
        Assert.DoesNotContain("GetHashCode", File.ReadAllText(Path.Combine(
            FindRoot(),
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofGeneratedLayoutFingerprint.cs")));
    }

    [Fact]
    public void GeometrySlopeAndSpacingChangeFingerprint()
    {
        var baseLayout = CreateRectangleLayout(10000, 6000, 30d, 500d).Signature;
        var longer = CreateRectangleLayout(12000, 6000, 30d, 500d).Signature;
        var steeper = CreateRectangleLayout(10000, 6000, 45d, 500d).Signature;
        var widerSpacing = CreateRectangleLayout(10000, 6000, 30d, 900d).Signature;

        var baseFp = RoofGeneratedLayoutFingerprint.Compute(baseLayout);
        Assert.NotEqual(baseFp, RoofGeneratedLayoutFingerprint.Compute(longer));
        Assert.NotEqual(baseFp, RoofGeneratedLayoutFingerprint.Compute(steeper));
        Assert.NotEqual(baseFp, RoofGeneratedLayoutFingerprint.Compute(widerSpacing));
    }

    [Fact]
    public void CompactIdentityDoesNotGrowWithCanonicalSignatureLength()
    {
        var small = "ROOF_FACE_RAFTER_LAYOUT_V2;short";
        var large = "ROOF_FACE_RAFTER_LAYOUT_V2;" + new string('X', 50_000);
        var smallFp = RoofGeneratedLayoutFingerprint.Compute(small);
        var largeFp = RoofGeneratedLayoutFingerprint.Compute(large);

        Assert.Equal(smallFp.Length, largeFp.Length);
        Assert.Equal(RoofGeneratedLayoutFingerprint.PersistedIdentityLength, largeFp.Length);
        Assert.NotEqual(smallFp, largeFp);

        var largePayload = RoofGeneratedTimberDataCodec.Encode(new RoofGeneratedTimberData(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            "ABCD",
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            0,
            2,
            500d,
            largeFp));

        var hugeVerboseData = new RoofGeneratedTimberData(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            "ABCD",
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            0,
            2,
            500d,
            large);
        Assert.True(RoofGeneratedTimberDataCodec.TryValidate(hugeVerboseData, out _));
        var hugeVerbosePayload = RoofGeneratedTimberDataCodec.Encode(hugeVerboseData);
        Assert.True(hugeVerbosePayload.Length > 40_000);
        Assert.True(largePayload.Length < 200);
        Assert.True(largePayload.Length < hugeVerbosePayload.Length / 100);
    }

    [Fact]
    public void CodecRoundTripCompactAndLegacyVerbose()
    {
        var full = CreateRectangleLayout(10000, 6000, 30d, 500d).Signature;
        var compact = RoofGeneratedLayoutFingerprint.ToPersistedIdentity(full);

        var compactData = new RoofGeneratedTimberData(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            "2912",
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            3,
            64,
            500d,
            compact);
        var legacyData = compactData with { LayoutSignature = full };

        Assert.True(RoofGeneratedTimberDataCodec.TryDecode(
            RoofGeneratedTimberDataCodec.Encode(compactData),
            out var decodedCompact,
            out _));
        Assert.Equal(compact, decodedCompact!.LayoutSignature);

        Assert.True(RoofGeneratedTimberDataCodec.TryDecode(
            RoofGeneratedTimberDataCodec.Encode(legacyData),
            out var decodedLegacy,
            out _));
        Assert.Equal(full, decodedLegacy!.LayoutSignature);
    }

    [Fact]
    public void FreshnessMatchesLegacyVerboseAndNewFingerprint()
    {
        var geometry = SolveHip(Rectangle(10000, 6000), 30d);
        var layout = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(500d, 80d, 1d)).Layout!;
        var full = layout.Signature;
        var fingerprint = RoofGeneratedLayoutFingerprint.ToPersistedIdentity(full);
        var after = CreateRectangleLayout(12000, 6000, 30d, 500d).Signature;

        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(full, full));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(fingerprint, full));
        Assert.False(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(fingerprint, after));
        Assert.False(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(full, after));
        Assert.True(RoofGeneratedTimberFreshness.IsLayoutCurrent(full, geometry.Signature));
        Assert.False(RoofGeneratedTimberFreshness.IsLayoutCurrent(fingerprint, geometry.Signature));
    }

    [Fact]
    public void RecipeUnificationIgnoresLayoutIdentityAndRejectsDivergentSpacing()
    {
        var full = CreateRectangleLayout(10000, 6000, 30d, 500d).Signature;
        var fp = RoofGeneratedLayoutFingerprint.ToPersistedIdentity(full);
        Assert.True(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24"),
                new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24"),
            ],
            out var recipe));
        Assert.Equal(500d, recipe.MaximumSpacingMm);
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [
                new RoofRafterGenerationRecipe(80d, 160d, 500d, "Smrek C24"),
                new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
            ],
            out _));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(fp, full));
        Assert.False(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.Compute(full + ";mutated"),
            full));
    }

    [Fact]
    public void HostL131_AllCandidatesEncodeUnderConservativeBudget_WithTwelveRidgeValley()
    {
        var geometry = SolveHip(HostL131, 30d);
        var face = CreateFaceLayout(geometry, 500d);
        var layout = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(500d, 80d, 1d)).Layout!;

        Assert.Equal(131, face.Segments.Count);
        Assert.Equal(131, layout.Rafters.Count);
        Assert.Equal(12, face.Segments.Count(IsRidgeValley));
        Assert.True(face.Signature.Length > 5_000);

        var fingerprint = RoofGeneratedLayoutFingerprint.ToPersistedIdentity(layout.Signature);
        Assert.Equal(RoofGeneratedLayoutFingerprint.PersistedIdentityLength, fingerprint.Length);

        var legacyEncode = RoofGeneratedTimberDataCodec.Encode(new RoofGeneratedTimberData(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            "2912",
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            0,
            131,
            500d,
            layout.Signature));
        Assert.True(legacyEncode.Length > 5_000);

        for (var index = 0; index < 131; index++)
        {
            var data = new RoofGeneratedTimberData(
                RoofGeneratedTimberDataSchema.CurrentVersion,
                "2912",
                RoofGeneratedTimberKind.Rafter,
                RafterRoofFace.Face0,
                index,
                131,
                500d,
                fingerprint);
            Assert.True(RoofGeneratedTimberDataCodec.TryValidate(data, out _));
            var payload = RoofGeneratedTimberDataCodec.Encode(data);
            Assert.True(payload.Length < 256);
            Assert.True(RoofGeneratedTimberDataCodec.TryDecode(payload, out var decoded, out _));
            Assert.Equal(fingerprint, decoded!.LayoutSignature);
        }
    }

    [Fact]
    public void RectangleAndRegenerationFingerprintsRemainStable()
    {
        var before = CreateRectangleLayout(10000, 6000, 30d, 500d);
        var after = CreateRectangleLayout(12000, 6000, 30d, 500d);
        Assert.Equal(64, before.Rafters.Count);
        Assert.Equal(72, after.Rafters.Count);
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(before.Signature),
            before.Signature));
        Assert.True(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(after.Signature),
            after.Signature));
        Assert.False(RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(before.Signature),
            after.Signature));
    }

    private static bool IsRidgeValley(RoofFaceRafterSegment segment) =>
        (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge &&
         segment.EndBoundaryRole == RoofRafterBoundaryRole.Valley) ||
        (segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
         segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);

    private static RoofRafterLayout CreateRectangleLayout(
        double length,
        double width,
        double slope,
        double spacing)
    {
        var geometry = SolveHip(Rectangle(length, width), slope);
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(spacing, 80d, 1d));
        Assert.True(result.IsValid);
        return result.Layout!;
    }

    private static RoofFaceRafterLayout CreateFaceLayout(HipRoofGeometry geometry, double spacing)
    {
        var result = RoofFaceRafterLayoutService.Create(geometry.Topology, spacing);
        Assert.True(result.IsValid);
        return result.Layout!;
    }

    private static HipRoofGeometry SolveHip(IReadOnlyList<RoofPoint2D> polygon, double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(polygon, true));
        Assert.True(validation.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(solved.IsValid);
        return Assert.IsType<HipRoofGeometry>(solved.Geometry);
    }

    private static RoofPoint2D[] Rectangle(double length, double width) =>
        [new(0, 0), new(length, 0), new(length, width), new(0, width)];

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
