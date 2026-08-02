#if DEBUG
using System.Text;
using System.Text.Json;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadItemLeaderBlockVariantProofPolicyTests
{
    [Fact]
    public void Cases_AreExactlyAThroughEWithRequiredRelationships()
    {
        var cases = AutoCadItemLeaderBlockVariantProofPolicy.Cases;

        Assert.Equal(["A", "B", "C", "D", "E"],
            cases.Select(value => value.Token));
        Assert.Equal(
            [
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockFrameKind.Circle,
                AutoCadItemLeaderBlockFrameKind.Slot,
                AutoCadItemLeaderBlockFrameKind.Circle,
            ],
            cases.Select(value => value.FrameKind));
        Assert.True(cases.Single(value => value.Token == "E").CreatesLeader);
        Assert.All(
            cases.Where(value => value.Token is "A" or "C" or "D" or "E"),
            value => Assert.Equal(
                AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA,
                value.StyleSlot));
    }

    [Theory]
    [InlineData("A", 2d, 50, 100d, 1d, 100d)]
    [InlineData("B", 3.2d, 50, 160d, 1d, 160d)]
    [InlineData("C", 2d, 100, 100d, 2d, 200d)]
    [InlineData("D", 2d, 50, 100d, 1d, 100d)]
    [InlineData("E", 2d, 50, 100d, 1d, 100d)]
    public void Cases_UseDefinitionAtBase50AndPerLeaderBlockScale(
        string token,
        double paperHeight,
        int denominator,
        double definitionHeight,
        double blockScale,
        double effectiveHeight)
    {
        var proofCase = AutoCadItemLeaderBlockVariantProofPolicy.Cases
            .Single(value => value.Token == token);

        Assert.Equal(paperHeight, proofCase.ItemNumberPaperHeightMm);
        Assert.Equal(denominator, proofCase.AnnotationScaleDenominator);
        Assert.Equal(definitionHeight, proofCase.DefinitionBaseHeight);
        Assert.Equal(blockScale, proofCase.BlockScale);
        Assert.Equal(effectiveHeight, proofCase.EffectiveHeight);
        Assert.Equal(paperHeight * denominator, proofCase.EffectiveHeight);
        Assert.Equal(
            paperHeight * TimberAnnotationScaleRules.DefaultDenominator,
            proofCase.DefinitionBaseHeight);
    }

    [Fact]
    public void Markers_AThroughE_RoundTripVersionedInvariantPayload()
    {
        foreach (var proofCase in AutoCadItemLeaderBlockVariantProofPolicy.Cases)
        {
            var marker = Marker(
                proofCase.Token,
                proofCase.Token == "B" ? "Štýl 日本語" : "Standard");
            var chunks =
                AutoCadItemLeaderBlockVariantProofPolicy.SerializeMarker(marker);

            Assert.True(
                AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeMarker(
                    chunks,
                    out var restored));
            Assert.Equal(marker, restored);
            Assert.All(
                chunks,
                chunk => Assert.InRange(
                    chunk.Length,
                    1,
                    AutoCadItemLeaderBlockVariantProofPolicy
                        .PayloadAsciiChunkLength));
        }
    }

    [Fact]
    public void Marker_RejectsInvalidSchemaAndCorruptPayload()
    {
        Assert.False(
            AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeMarker(
                ["not-base64"],
                out _));
        Assert.False(
            AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeMarker(
                Encode(Marker("A", "Standard") with { SchemaVersion = 99 }),
                out _));
    }

    [Fact]
    public void PersistedMarkerContainsNoObjectIdOrHandleIdentity()
    {
        var properties = typeof(AutoCadItemLeaderBlockVariantProofMarker)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, name =>
            name.Contains("ObjectId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name =>
            name.Contains("Handle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("VariantKeyPayload", properties);
        Assert.Contains("CanonicalBlockName", properties);
    }

    [Fact]
    public void Manifest_StyleBTested_RoundTripsAsTested()
    {
        var manifest = TestedManifest();

        var chunks =
            AutoCadItemLeaderBlockVariantProofPolicy.SerializeManifest(manifest);

        Assert.True(
            AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeManifest(
                chunks,
                out var restored));
        Assert.Equal(
            AutoCadItemLeaderBlockVariantProofStyleBState.Tested,
            restored!.StyleBState);
        Assert.Equal("AK_PROOF_TIMES", restored.CanonicalStyleNameB);
        Assert.Equal(["A", "B", "C", "D", "E"],
            restored.ExpectedCases.Select(marker => marker.CaseToken));
    }

    [Fact]
    public void Manifest_StyleBNotTested_RoundTripsWithoutBExpectation()
    {
        var markers = AutoCadItemLeaderBlockVariantProofPolicy.Cases
            .Where(proofCase => proofCase.Token != "B")
            .Select(proofCase => Marker(proofCase.Token, "Standard"));
        var manifest = AutoCadItemLeaderBlockVariantProofPolicy.CreateManifest(
            AutoCadItemLeaderBlockVariantProofStyleBState
                .NotTestedNoSecondCompatibleStyle,
            "Standard",
            null,
            markers);

        var chunks =
            AutoCadItemLeaderBlockVariantProofPolicy.SerializeManifest(manifest);

        Assert.True(
            AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeManifest(
                chunks,
                out var restored));
        Assert.Equal(
            AutoCadItemLeaderBlockVariantProofStyleBState
                .NotTestedNoSecondCompatibleStyle,
            restored!.StyleBState);
        Assert.Null(restored.CanonicalStyleNameB);
        Assert.DoesNotContain(restored.ExpectedCases, marker =>
            marker.CaseToken == "B");
    }

    [Fact]
    public void Manifest_InvalidSchemaIsRejected()
    {
        var invalid = TestedManifest() with { SchemaVersion = 99 };

        Assert.False(
            AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeManifest(
                Encode(invalid),
                out _));
    }

    [Fact]
    public void Recovery_MissingManifestFailsInsteadOfReportingNotTested()
    {
        var result =
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                null,
                []);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Contains("manifest is missing or invalid", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, error =>
            error.Contains("NOT TESTED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recovery_RequiresEveryExpectedPostCommitMarker()
    {
        var manifest = TestedManifest();
        var complete = Observe(manifest.ExpectedCases);
        var missingE = Observe(manifest.ExpectedCases.Where(marker =>
            marker.CaseToken != "E"));

        Assert.True(
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                complete).Succeeded);
        var incomplete =
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                missingE);
        Assert.False(incomplete.Succeeded);
        Assert.Contains(incomplete.Errors, error =>
            error.Contains("case E marker is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Recovery_DuplicateCaseMarkerFails()
    {
        var manifest = TestedManifest();
        var observations = Observe(manifest.ExpectedCases).ToList();
        observations.Add(Observed(Marker("A", "AK_PROOF_ARIAL"), "duplicate-A"));

        var result =
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                observations);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Contains("case A has 2 markers", StringComparison.Ordinal));
    }

    [Fact]
    public void Recovery_InvalidModelSpaceProofPayloadFailsExplicitly()
    {
        var manifest = TestedManifest();
        var observations = Observe(manifest.ExpectedCases).ToList();
        observations.Add(new AutoCadItemLeaderBlockVariantObservedMarker(
            AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
            true,
            null,
            "invalid-xdata-candidate"));

        var result =
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                observations);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Contains(
                "Invalid or unreadable proof payload",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Recovery_IgnoresWrongSuiteAndPaperSpaceMarkers()
    {
        var manifest = TestedManifest();
        var observations = Observe(manifest.ExpectedCases).ToList();
        observations.Add(Observed(
            Marker("A", "AK_PROOF_ARIAL") with
            {
                SuiteIdentifier = "ANOTHER_SUITE",
            },
            "wrong-suite"));
        observations.Add(new AutoCadItemLeaderBlockVariantObservedMarker(
            AutoCadItemLeaderBlockVariantProofObjectSpace.PaperSpace,
            true,
            Marker("A", "AK_PROOF_ARIAL"),
            "paper-space"));

        var result =
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                observations);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.AcceptedCandidateByCase.Count);
    }

    [Fact]
    public void Recovery_DoesNotDependOnPersistedHandleOrSessionState()
    {
        var manifest = TestedManifest();
        var first = Observe(manifest.ExpectedCases, "session-one-");
        var reopened = Observe(manifest.ExpectedCases, "reopened-");

        Assert.True(
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                first).Succeeded);
        Assert.True(
            AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
                manifest,
                reopened).Succeeded);
    }

    [Fact]
    public void E_PersistsOwnMarkerAndSharesCanonicalVariantWithAAndC()
    {
        var a = Marker("A", "AK_PROOF_ARIAL");
        var c = Marker("C", "AK_PROOF_ARIAL");
        var e = Marker("E", "AK_PROOF_ARIAL");

        Assert.Equal(a.VariantKeyPayload, c.VariantKeyPayload);
        Assert.Equal(a.VariantKeyPayload, e.VariantKeyPayload);
        Assert.Equal(a.CanonicalBlockName, c.CanonicalBlockName);
        Assert.Equal(a.CanonicalBlockName, e.CanonicalBlockName);
    }

    [Fact]
    public void Evaluation_DistinguishesPassFailAndNotTested()
    {
        var pass = AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
            "height",
            100d,
            100d);
        var fail = AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
            "height",
            100d,
            160d);
        var notTested = AutoCadItemLeaderBlockVariantProofPolicy.NotTested(
            "style variation",
            "Only one compatible style exists.");

        Assert.Equal(AutoCadItemLeaderBlockVariantProofStatus.Pass, pass.Status);
        Assert.Equal(AutoCadItemLeaderBlockVariantProofStatus.Fail, fail.Status);
        Assert.Equal(
            AutoCadItemLeaderBlockVariantProofStatus.NotTested,
            notTested.Status);
    }

    [Fact]
    public void ArchitectureTemplate_PaperSpaceEntitiesDoNotBlockEmptyModelSpace()
    {
        var result = Evaluate(
            currentSpaceIsModelSpace: false,
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.PaperSpace,
                dxfName: "VIEWPORT"),
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.PaperSpace,
                dxfName: "INSERT"),
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.DatabaseObject,
                isEntity: false,
                dxfName: "AEC_DICTIONARY"));

        Assert.True(result.Passed);
        Assert.Empty(result.BlockingModelSpaceEntities);
    }

    [Fact]
    public void LiveEntityOwnedDirectlyByModelSpaceBlocksPreflight()
    {
        var entity = Snapshot(
            AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
            handle: "2A",
            objectId: "(42)",
            dxfName: "LINE",
            rxClassName: "AcDbLine",
            layer: "KROVY",
            ownerHandle: "1F",
            ownerName: "*Model_Space");

        var result = Evaluate(currentSpaceIsModelSpace: true, entity);

        var blocking = Assert.Single(result.BlockingModelSpaceEntities);
        Assert.False(result.Passed);
        Assert.Equal("2A", blocking.Handle);
        Assert.Equal("(42)", blocking.ObjectId);
        Assert.Equal("LINE", blocking.DxfName);
        Assert.Equal("AcDbLine", blocking.RxClassName);
        Assert.Equal("KROVY", blocking.Layer);
        Assert.Equal("1F", blocking.OwnerHandle);
        Assert.Equal("*Model_Space", blocking.OwnerName);
        Assert.True(blocking.OwnerIsModelSpace);
    }

    [Fact]
    public void ErasedOrInvalidModelSpaceObjectsDoNotBlock()
    {
        var result = Evaluate(
            currentSpaceIsModelSpace: true,
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                isErased: true),
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                isValid: false,
                isEntity: false,
                ownerIsModelSpace: false));

        Assert.True(result.Passed);
        Assert.Empty(result.BlockingModelSpaceEntities);
        Assert.Equal(1, result.InvalidModelSpaceObjectCount);
    }

    [Fact]
    public void NestedBlockDefinitionEntitiesDoNotBlock()
    {
        var result = Evaluate(
            currentSpaceIsModelSpace: true,
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.BlockDefinition,
                dxfName: "LINE",
                ownerIsModelSpace: false));

        Assert.True(result.Passed);
    }

    [Fact]
    public void ModelSpaceSourceWithDifferentOwnerDoesNotBlock()
    {
        var result = Evaluate(
            currentSpaceIsModelSpace: true,
            Snapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                ownerIsModelSpace: false));

        Assert.True(result.Passed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurrentSpaceSelectionNeverChangesEmptyModelSpaceResult(
        bool currentSpaceIsModelSpace)
    {
        var result = Evaluate(currentSpaceIsModelSpace);

        Assert.True(result.Passed);
        Assert.Equal(currentSpaceIsModelSpace, result.CurrentSpaceIsModelSpace);
    }

    private static AutoCadItemLeaderBlockVariantProofManifest TestedManifest() =>
        AutoCadItemLeaderBlockVariantProofPolicy.CreateManifest(
            AutoCadItemLeaderBlockVariantProofStyleBState.Tested,
            "AK_PROOF_ARIAL",
            "AK_PROOF_TIMES",
            AutoCadItemLeaderBlockVariantProofPolicy.Cases.Select(proofCase =>
                Marker(
                    proofCase.Token,
                    proofCase.Token == "B"
                        ? "AK_PROOF_TIMES"
                        : "AK_PROOF_ARIAL")));

    private static AutoCadItemLeaderBlockVariantProofMarker Marker(
        string token,
        string styleName)
    {
        var proofCase = AutoCadItemLeaderBlockVariantProofPolicy.Cases
            .Single(candidate => candidate.Token == token);
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            proofCase.ToItemNumberLeaderStyle(),
            proofCase.Token);
        var key = AutoCadItemLeaderBlockVariantKey.FromDefinition(
            definition,
            styleName,
            proofCase.ItemNumberPaperHeightMm);
        return AutoCadItemLeaderBlockVariantProofPolicy.CreateMarker(
            proofCase,
            key,
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key));
    }

    private static IReadOnlyList<AutoCadItemLeaderBlockVariantObservedMarker>
        Observe(
        IEnumerable<AutoCadItemLeaderBlockVariantProofMarker> markers,
        string prefix = "model-") =>
        markers
            .Select(marker => Observed(marker, prefix + marker.CaseToken))
            .ToArray();

    private static AutoCadItemLeaderBlockVariantObservedMarker Observed(
        AutoCadItemLeaderBlockVariantProofMarker marker,
        string diagnosticId) =>
        new(
            AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
            true,
            marker,
            diagnosticId);

    private static IReadOnlyList<string> Encode<T>(T payload)
    {
        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        return [Convert.ToBase64String(Encoding.UTF8.GetBytes(json))];
    }

    private static AutoCadItemLeaderBlockVariantProofPreflightResult Evaluate(
        bool currentSpaceIsModelSpace,
        params AutoCadItemLeaderBlockVariantProofObjectSnapshot[] objects) =>
        AutoCadItemLeaderBlockVariantProofPreflightPolicy.Evaluate(
            objects,
            currentSpaceIsModelSpace);

    private static AutoCadItemLeaderBlockVariantProofObjectSnapshot Snapshot(
        AutoCadItemLeaderBlockVariantProofObjectSpace space,
        bool isValid = true,
        bool isEntity = true,
        bool isErased = false,
        bool ownerIsModelSpace = true,
        string handle = "10",
        string objectId = "(16)",
        string dxfName = "LINE",
        string rxClassName = "AcDbLine",
        string layer = "0",
        string ownerHandle = "1F",
        string ownerName = "*Model_Space") =>
        new(
            space,
            isValid,
            isEntity,
            isErased,
            ownerIsModelSpace,
            handle,
            objectId,
            dxfName,
            rxClassName,
            layer,
            ownerHandle,
            ownerName);
}
#endif
