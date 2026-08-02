using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ItemLeaderBlockVariantSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void KeyAndName_ContainNoHostObjectsAndUseExactStableFingerprint()
    {
        var source = KeySource();

        Assert.Contains("sealed record AutoCadItemLeaderBlockVariantKey", source);
        Assert.Contains("ItemNumberPaperHeightMm.ToString(\"R\"", source);
        Assert.Contains("CultureInfo.InvariantCulture", source);
        Assert.Contains("SHA256.HashData", source);
        Assert.Contains("Encoding.UTF8.GetBytes", source);
        Assert.Contains("styleLength=", source);
        Assert.Contains("baseDenominator=", source);
        Assert.Contains("TimberAnnotationScaleRules.DefaultDenominator", source);
        Assert.Contains("TimberAnnotationTextSettingsRules", source);
        Assert.DoesNotContain("GetHashCode", source);
        Assert.DoesNotContain("ObjectId", source);
        Assert.DoesNotContain("Database", source);
        Assert.DoesNotContain("Transaction", source);
        Assert.DoesNotContain("DBObject", source);
        Assert.DoesNotContain("Guid.NewGuid", source);
    }

    [Fact]
    public void VariantService_ConsumesResolvedStyleAndNeverImplementsFallback()
    {
        var source = ServiceSource();

        Assert.Contains("resolvedCanonicalTextStyleName", source);
        Assert.Contains("resolvedTextStyleId", source);
        Assert.Contains("presentationContext.ResolvedTextStyleName", source);
        Assert.Contains("presentationContext.ResolvedTextStyleId", source);
        Assert.Contains("NoCompatibleTextStyle", source);
        Assert.DoesNotContain("ResolveExplicit", source);
        Assert.DoesNotContain("ResolveLegacy", source);
        Assert.DoesNotContain("ReadCatalog", source);
        Assert.DoesNotContain("TextStyleResolutionKind", source);
        Assert.DoesNotContain("database.Textstyle =", source);
        Assert.DoesNotContain("MLeaderStyle", source);
    }

    [Fact]
    public void ExistingDefinitions_AreValidatedReadOnlyAndNeverRepaired()
    {
        var source = Normalize(ServiceSource());
        var validate = Member(
            source,
            "        ValidateExistingDefinitionDetailed(");

        Assert.Contains("OpenMode.ForRead", validate);
        Assert.DoesNotContain("OpenMode.ForWrite", validate);
        Assert.DoesNotContain("UpgradeOpen", validate);
        Assert.DoesNotContain(".Height =", validate);
        Assert.DoesNotMatch(@"\.TextStyleId\s*=(?!=)", validate);
        Assert.DoesNotContain("AppendEntity", validate);
        Assert.DoesNotContain("Erase(", validate);
        Assert.DoesNotContain("EraseDefinitionContents", source);
        Assert.Contains("CreateCollisionName", PoliciesSource());
        Assert.Contains("CreatedCollisionVariant", ResultSource());
        Assert.Contains("ReusedCollisionVariant", ResultSource());
    }

    [Fact]
    public void Hotfix3_DefinitionValidationUsesSharedAttributeDefinitionAuthority()
    {
        var service = Normalize(ServiceSource());
        var proof = Normalize(ProofServiceSource());
        var policies = Normalize(PoliciesSource());
        var validate = Member(
            service,
            "        ValidateExistingDefinitionDetailed(");

        Assert.Contains("foreach (ObjectId id in block)", validate);
        Assert.Contains("Entity entity", validate);
        Assert.Contains("OfType<AttributeDefinition>()", validate);
        Assert.Contains("attribute.TextStyleId", validate);
        Assert.Contains("TextStyleTableRecord", validate);
        Assert.Contains("textStyle?.Name", validate);
        Assert.Contains("key.ResolvedCanonicalTextStyleName", validate);
        Assert.Contains("key.BaseDenominator", validate);
        Assert.Contains("CalculateModelHeightMm(", validate);
        Assert.Contains("OpenMode.ForRead", validate);
        Assert.DoesNotContain("MLeader", validate);
        Assert.DoesNotContain("GetBlockAttribute", validate);
        Assert.DoesNotContain("BlockScale", validate);
        Assert.DoesNotContain("database.Textstyle", validate);
        Assert.DoesNotContain("OpenMode.ForWrite", validate);
        Assert.DoesNotContain("UpgradeOpen", validate);
        Assert.DoesNotContain("attribute.Position.DistanceTo", validate);
        Assert.Contains(
            "ValidateExistingDefinitionDetailed(",
            proof);
        Assert.Contains("FieldFailures", policies);
        Assert.Contains("Expected", policies);
        Assert.Contains("Actual", policies);
        Assert.Contains("Tolerance", policies);
        Assert.Contains("ItemNoMissing", policies);
        Assert.Contains("ItemNoDuplicate", policies);
    }

    [Fact]
    public void Hotfix3_ProofDiagnosticsAreFieldLevelReadOnlyAndDebugOnly()
    {
        var proof = Normalize(ProofServiceSource());
        var diagnostics = Member(
            proof,
            "private static void WriteDefinitionValidationDiagnostics(");
        var verify = Member(proof, "public static void Verify(");

        Assert.Contains("reasonCode=", diagnostics);
        Assert.Contains("AttributeDefinitionCount", diagnostics);
        Assert.Contains("TextStyleId=", diagnostics);
        Assert.Contains("styleFixedHeight", diagnostics);
        Assert.Contains("styleAnnotative", diagnostics);
        Assert.Contains("Position=", diagnostics);
        Assert.Contains("AlignmentPoint=", diagnostics);
        Assert.Contains("expected=", diagnostics);
        Assert.Contains("actual=", diagnostics);
        Assert.Contains("tolerance=", diagnostics);
        Assert.DoesNotContain("OpenMode.ForWrite", verify + diagnostics);
        Assert.DoesNotContain("UpgradeOpen", verify + diagnostics);
        Assert.DoesNotContain("Commit();", verify + diagnostics);
        Assert.StartsWith("#if DEBUG", ProofServiceSource().Trim());
    }

    [Fact]
    public void CreatePath_UsesSharedGeometryAndCentralHeightAuthority()
    {
        var service = ServiceSource();
        var legacyFactory = LegacyBlockSource();

        Assert.Contains(
            "AcKrovyItemLeaderBlockService.AddFrameGeometry(",
            service);
        Assert.Contains(
            "AcKrovyItemLeaderBlockService.AddItemNumberAttribute(",
            service);
        Assert.Contains(
            "TimberAnnotationTextSettingsRules.CalculateModelHeightMm(",
            service);
        Assert.Contains("key.BaseDenominator", service);
        Assert.Contains("internal static void AddFrameGeometry(", legacyFactory);
        Assert.Contains("internal static ObjectId AddItemNumberAttribute(", legacyFactory);
        Assert.DoesNotContain("const int BaseDenominator", service);
        Assert.DoesNotContain("const double Base", service);
    }

    [Fact]
    public void BatchCatalog_IsInstanceScopedAndRejectsForeignDatabaseObjects()
    {
        var source = ResultSource() + PoliciesSource();

        Assert.Contains("AutoCadItemLeaderBlockVariantBatchIndex<ObjectId>", source);
        Assert.Contains("Dictionary<", source);
        Assert.Contains("AutoCadItemLeaderBlockVariantKey", source);
        Assert.Contains("public Database Database { get; }", source);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(Database, blockId)", source);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(Database, database)", source);
        Assert.DoesNotContain("static readonly Dictionary", source);
        Assert.DoesNotContain("ConcurrentDictionary", source);
        Assert.DoesNotContain("JsonSerializer", source);
    }

    [Fact]
    public void CollisionStateMachine_IsTheProductionSelectionAuthority()
    {
        var policy = PoliciesSource();
        var service = ServiceSource();

        Assert.Contains("AutoCadItemLeaderBlockVariantCollisionPolicy.Select(", service);
        Assert.Contains("CandidateState.Missing", policy);
        Assert.Contains("CandidateState.Matching", policy);
        Assert.Contains("Invalid,", policy);
        Assert.Contains("CollisionDecisionKind.Create", policy);
        Assert.Contains("CollisionDecisionKind.Reuse", policy);
        Assert.Contains("CollisionDecisionKind.Exhausted", policy);
        Assert.DoesNotContain("Guid.NewGuid", policy);
    }

    [Fact]
    public void ResultModel_DerivesFlagsFromKindAndValidatesFactories()
    {
        var source = ResultSource();

        Assert.Contains("private AutoCadItemLeaderBlockVariantResult(", source);
        Assert.Contains("public bool Succeeded => Kind is", source);
        Assert.Contains("public bool WroteToDatabase => Kind is", source);
        Assert.Contains("public bool IsCollision => Kind is", source);
        Assert.Contains("A successful result requires a valid key", source);
        Assert.Contains("A failed result cannot expose a block ObjectId", source);
        Assert.DoesNotContain("bool succeeded", source);
        Assert.DoesNotContain("bool wroteToDatabase", source);
    }

    [Fact]
    public void Stage4B_ConnectsOnlyTheProductionFramedRenderer()
    {
        var elementLabels = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "ElementLabelService.cs");
        var orchestration = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "TimberAnnotationService.cs");
        Assert.Contains(
            "AcKrovyItemLeaderBlockVariantService.Ensure(",
            elementLabels);
        Assert.Contains(
            "ItemLeaderVariantCatalog",
            orchestration);

        string[] excludedFiles =
        [
            "SlopeAnnotationService.cs",
            "SlopeAngleTextService.cs",
            "AcKrovyMLeaderStyleService.cs",
        ];
        foreach (var file in excludedFiles)
        {
            var source = Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                file);
            Assert.DoesNotContain("AcKrovyItemLeaderBlockVariantService", source);
            Assert.DoesNotContain("AutoCadItemLeaderBlockVariantKey", source);
            Assert.DoesNotContain("AutoCadItemLeaderBlockVariantBatchCatalog", source);
        }
    }

    [Fact]
    public void HostProof_IsEntirelyDebugOnlyAndOffProductSurfaces()
    {
        var command = CommandSource().Trim();
        var service = ProofServiceSource().Trim();
        var policy = ProofPolicySource().Trim();

        Assert.StartsWith("#if DEBUG", command, StringComparison.Ordinal);
        Assert.EndsWith("#endif", command, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", service, StringComparison.Ordinal);
        Assert.EndsWith("#endif", service, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", policy, StringComparison.Ordinal);
        Assert.EndsWith("#endif", policy, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_BLOCKVARIANT_CREATE", command);
        Assert.Contains("AK_DEV_BLOCKVARIANT_VERIFY", command);
        Assert.Contains("AK_DEV_BLOCKVARIANT_CLEAN", command);
        Assert.Contains("AK_DEV_BLOCKVARIANT_PROOF", service);

        var excludedRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "AcKrovy.AutoCAD", "UI"),
            Path.Combine(RepositoryRoot, "src", "AcKrovy.Localization"),
            Path.Combine(RepositoryRoot, "deploy"),
        };
        foreach (var root in excludedRoots)
        {
            Assert.All(
                Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => new[] { ".cs", ".xaml", ".resx", ".xml" }
                        .Contains(Path.GetExtension(path),
                            StringComparer.OrdinalIgnoreCase)),
                path => Assert.DoesNotContain(
                    "AK_DEV_BLOCKVARIANT",
                    File.ReadAllText(path)));
        }
    }

    [Fact]
    public void Proof_CreateVerifyCleanupRespectStage4AHostBoundaries()
    {
        var service = ProofServiceSource();

        Assert.Contains("AutoCadTextStyleResolver.ReadCatalog(", service);
        Assert.Contains("AcKrovyItemLeaderBlockVariantService.EnsureResolved(", service);
        Assert.Contains("leader.BlockScale = new Scale3d(", service);
        Assert.Contains("attribute.TextString = proofCase.Token", service);
        Assert.DoesNotContain("attribute.Height =", service);
        Assert.DoesNotContain("attribute.TextStyleId =", service);
        Assert.Contains("OpenMode.ForRead", service);
        Assert.Contains("OpenMode.ForWrite", service);
        Assert.Contains("leader.Erase()", service);
        Assert.DoesNotContain("Purge(", service);
        Assert.DoesNotContain("TextStyleTableRecord {", service);
        Assert.DoesNotContain("database.Textstyle =", service);
        Assert.DoesNotContain("MLeaderStyle", service);
        Assert.DoesNotContain("TimberElementStore", service);
        Assert.DoesNotContain("PrepareForWrite", service);
    }

    [Fact]
    public void Architecture2027Preflight_EnumeratesOnlyExplicitModelSpace()
    {
        var service = Normalize(ProofServiceSource());
        var preflight = Member(
            service,
            "private static AutoCadItemLeaderBlockVariantProofPreflightResult\n" +
            "        ReadModelSpacePreflight(");
        var snapshot = Member(
            service,
            "private static AutoCadItemLeaderBlockVariantProofObjectSnapshot\n" +
            "        ReadModelSpaceObjectSnapshot(");

        Assert.Contains("database.BlockTableId", preflight);
        Assert.Contains("BlockTableRecord.ModelSpace", preflight);
        Assert.Contains("blockTable[BlockTableRecord.ModelSpace]", preflight);
        Assert.Contains("foreach (ObjectId id in modelSpace)", preflight);
        Assert.Contains("OpenMode.ForRead", preflight);
        Assert.Contains("entity.OwnerId == modelSpace.ObjectId", snapshot);
        Assert.Contains("OpenMode.ForRead", snapshot);
        Assert.DoesNotContain("OpenMode.ForWrite", preflight + snapshot);
        Assert.DoesNotContain("UpgradeOpen", preflight + snapshot);
        Assert.DoesNotContain("GetBlockModelSpaceId", preflight);
        Assert.DoesNotContain("foreach (ObjectId id in blockTable)", preflight);
        Assert.DoesNotContain("LayoutDictionary", preflight);
        Assert.DoesNotContain("PaperSpace", preflight);
        Assert.DoesNotContain("ProofRegAppName", preflight);
    }

    [Fact]
    public void Architecture2027Preflight_CurrentSpaceIsDiagnosticOnly()
    {
        var service = Normalize(ProofServiceSource());
        var preflight = Member(
            service,
            "private static AutoCadItemLeaderBlockVariantProofPreflightResult\n" +
            "        ReadModelSpacePreflight(");
        var policy = ProofPolicySource();

        Assert.Contains(
            "currentSpaceIsModelSpace: database.CurrentSpaceId == modelSpaceId",
            preflight);
        Assert.Contains("bool CurrentSpaceIsModelSpace", policy);
        Assert.Contains("candidate.Space ==", policy);
        Assert.Contains("candidate.OwnerIsModelSpace", policy);
        Assert.DoesNotContain("CurrentSpaceIsModelSpace &&", policy);
        Assert.DoesNotContain("if (database.CurrentSpaceId", preflight);
        Assert.DoesNotContain("database.CurrentSpaceId]", preflight);
    }

    [Fact]
    public void Architecture2027Preflight_DiagnosticsIdentifyBlockingEntity()
    {
        var service = Normalize(ProofServiceSource());
        var diagnostic = Member(
            service,
            "private static void WriteModelSpacePreflight(");

        Assert.Contains("Model space preflight: PASS, entity count = 0.", diagnostic);
        Assert.Contains("entity count =", diagnostic);
        Assert.Contains("entity.Handle", diagnostic);
        Assert.Contains("entity.ObjectId", diagnostic);
        Assert.Contains("entity.DxfName", diagnostic);
        Assert.Contains("entity.RxClassName", diagnostic);
        Assert.Contains("entity.Layer", diagnostic);
        Assert.Contains("entity.OwnerHandle", diagnostic);
        Assert.Contains("entity.OwnerName", diagnostic);
        Assert.Contains("entity.IsErased", diagnostic);
        Assert.Contains("entity.OwnerIsModelSpace", diagnostic);
        Assert.Contains(".Take(20)", diagnostic);
        Assert.DoesNotContain("OpenMode.ForWrite", diagnostic);
        Assert.DoesNotContain("UpgradeOpen", diagnostic);
    }

    [Fact]
    public void SaveReopenProof_AppendsResidentMarkedMLeadersAndCommits()
    {
        var service = Normalize(ProofServiceSource());
        var createLeader = Member(
            service,
            "private static ObjectId CreateLeader(");
        var appendIndex = createLeader.IndexOf(
            "modelSpace.AppendEntity(leader)",
            StringComparison.Ordinal);
        var addIndex = createLeader.IndexOf(
            "transaction.AddNewlyCreatedDBObject(leader, true)",
            StringComparison.Ordinal);
        var markerIndex = createLeader.IndexOf(
            "SetMarker(leader, marker)",
            StringComparison.Ordinal);

        Assert.True(appendIndex >= 0 && appendIndex < addIndex);
        Assert.True(addIndex >= 0 && addIndex < markerIndex);
        Assert.Contains("leader.BlockContentId = blockId", createLeader);
        Assert.Contains("leader.BlockScale = new Scale3d(", createLeader);
        Assert.Contains("attribute.TextString = proofCase.Token", createLeader);
        Assert.Contains("transaction.Commit();", service);
        Assert.DoesNotContain("TransientManager", service);
        Assert.DoesNotContain("database.CurrentSpaceId", createLeader);
    }

    [Fact]
    public void SaveReopenProof_PersistsVersionedManifestWithoutObjectIds()
    {
        var service = Normalize(ProofServiceSource());
        var policy = Normalize(ProofPolicySource());
        var markerContract = Between(
            policy,
            "internal sealed record AutoCadItemLeaderBlockVariantProofMarker(",
            "internal sealed record AutoCadItemLeaderBlockVariantProofManifest(");
        var manifestContract = Between(
            policy,
            "internal sealed record AutoCadItemLeaderBlockVariantProofManifest(",
            "internal sealed record AutoCadItemLeaderBlockVariantObservedMarker(");

        Assert.Contains("database.NamedObjectsDictionaryId", service);
        Assert.Contains("DBDictionary", service);
        Assert.Contains("Xrecord", service);
        Assert.Contains("ProofManifestDictionaryKey", service);
        Assert.Contains("WriteManifest(database, transaction, manifest)", service);
        Assert.Contains("ReadManifest(database, transaction)", service);
        Assert.Contains("SchemaVersion", markerContract);
        Assert.Contains("SuiteIdentifier", markerContract);
        Assert.Contains("VariantKeyPayload", markerContract);
        Assert.Contains("CanonicalBlockName", markerContract);
        Assert.Contains("ExpectedCases", manifestContract);
        Assert.Contains("StyleBState", manifestContract);
        Assert.DoesNotContain("ObjectId", markerContract + manifestContract);
        Assert.DoesNotContain("Handle", markerContract + manifestContract);
    }

    [Fact]
    public void SaveReopenProof_UsesManifestAndIndependentPostCommitReadback()
    {
        var service = Normalize(ProofServiceSource());
        var create = Member(service, "public static void Create(");
        var verify = Member(service, "public static void Verify(");

        var commitIndex = create.IndexOf(
            "transaction.Commit();",
            StringComparison.Ordinal);
        var readTransactionIndex = create.IndexOf(
            "readTransaction",
            StringComparison.Ordinal);
        var scanIndex = create.IndexOf(
            "ScanProofState(database, readTransaction)",
            StringComparison.Ordinal);
        Assert.True(commitIndex >= 0 && commitIndex < readTransactionIndex);
        Assert.True(readTransactionIndex >= 0 && readTransactionIndex < scanIndex);
        Assert.Contains("EvaluateScanRecovery(readback)", create);
        Assert.Contains("post-commit readback: PASS", create);
        Assert.Contains("ScanProofState(database, transaction)", verify);
        Assert.Contains("EvaluateScanRecovery(scan)", verify);
        Assert.Contains("scan.Manifest.StyleBState", verify);
        Assert.DoesNotContain("DefaultIfEmpty(false)", verify);
        Assert.DoesNotContain("OpenMode.ForWrite", verify);
        Assert.DoesNotContain("UpgradeOpen", verify);
        Assert.DoesNotContain("Commit();", verify);
    }

    [Fact]
    public void SaveReopenProof_ScansExactModelSpaceAndReportsRecoveryCandidates()
    {
        var service = Normalize(ProofServiceSource());
        var scan = Member(
            service,
            "private static ProofScanResult ScanProofState(");
        var diagnostics = Member(
            service,
            "private static void WriteScanDiagnostics(");

        Assert.Contains("OpenModelSpace(", scan);
        Assert.Contains("OpenMode.ForRead", scan);
        Assert.Contains("MLeader", scan);
        Assert.Contains("ProofRegAppName", scan);
        Assert.Contains("TryReadMarker", scan);
        Assert.Contains("TotalModelSpaceMLeaderCount", diagnostics);
        Assert.Contains("ProofXDataMLeaderCount", diagnostics);
        Assert.Contains("InvalidProofPayloadCount", diagnostics);
        Assert.Contains("candidate.Handle", diagnostics);
        Assert.Contains("candidate.OwnerName", diagnostics);
        Assert.Contains("candidate.BlockContentName", diagnostics);
        Assert.Contains("candidate.XDataRegAppNames", diagnostics);
        Assert.Contains("candidate.MarkerSchema", diagnostics);
        Assert.Contains("candidate.CaseToken", diagnostics);
        Assert.Contains("Situation A", diagnostics);
        Assert.Contains("Situation B", diagnostics);
        Assert.DoesNotContain("CurrentSpaceId", scan);
        Assert.DoesNotContain("OpenMode.ForWrite", scan + diagnostics);
    }

    [Fact]
    public void SaveReopenProof_EHasOwnMarkerAndNoStaticCrossCommandState()
    {
        var service = ProofServiceSource();
        var policy = ProofPolicySource();

        Assert.DoesNotContain("createsLeader: false", policy);
        Assert.Contains("markers.Add(marker)", service);
        Assert.Contains("A/E same BlockTableRecord", service);
        Assert.Contains("cases.TryGetValue(\"E\"", service);
        Assert.DoesNotContain("static readonly Dictionary", service);
        Assert.DoesNotContain("static Dictionary", service);
        Assert.DoesNotContain("E repeated Ensure key", service);
    }

    [Fact]
    public void Stage4A_DoesNotChangeProtectedVersionsOrStartStage4B()
    {
        Assert.Contains(
            "<AcKrovyVersion>0.22.0</AcKrovyVersion>",
            Source("Directory.Build.props"));
        Assert.Contains(
            "public const int CurrentVersion = 6;",
            Source("src", "AcKrovy.Core", "Models", "TimberElementDataSchema.cs"));
        Assert.Contains(
            "public const int CurrentVersion = 2;",
            Source("src", "AcKrovy.Core", "Models", "TimberElementDefaultProfile.cs"));

        var combined = KeySource() + ServiceSource() + ResultSource() +
            PoliciesSource() + ProofPolicySource() + ProofServiceSource() +
            CommandSource();
        Assert.DoesNotContain("Stage 4B", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Etapa 4B", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreProduction_RemainsFreeOfAutodeskAndObjectId()
    {
        var files = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", "AcKrovy.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.All(files, file =>
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Autodesk", source);
            Assert.DoesNotContain("ObjectId", source);
        });
    }

    [Fact]
    public void MemberExtraction_IsLineEndingAgnostic()
    {
        const string lf = "internal static bool Example()\n{\n    return true;\n}\n";
        var expected = Member(lf, "internal static bool Example(");

        Assert.Equal(expected, Member(lf.Replace("\n", "\r\n"),
            "internal static bool Example("));
        Assert.Equal(expected, Member(lf.Replace("\n", "\r"),
            "internal static bool Example("));
    }

    private static string KeySource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadItemLeaderBlockVariantKey.cs");

    private static string ServiceSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AcKrovyItemLeaderBlockVariantService.cs");

    private static string ResultSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadItemLeaderBlockVariantResult.cs");

    private static string PoliciesSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadItemLeaderBlockVariantPolicies.cs");

    private static string LegacyBlockSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AcKrovyItemLeaderBlockService.cs");

    private static string ProofPolicySource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadItemLeaderBlockVariantProofPolicy.cs");

    private static string ProofServiceSource() => Source(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadItemLeaderBlockVariantProofService.cs");

    private static string CommandSource() => Source(
        "src", "AcKrovy.AutoCAD", "Commands",
        "AutoCadItemLeaderBlockVariantProofCommands.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Member(string source, string declarationPrefix)
    {
        source = Normalize(source);
        declarationPrefix = Normalize(declarationPrefix);
        var start = source.IndexOf(declarationPrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Member not found: {declarationPrefix}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0, $"Opening brace not found: {declarationPrefix}");
        var depth = 0;
        for (var index = brace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }
        throw new InvalidOperationException($"Closing brace not found: {declarationPrefix}");
    }

    private static string Between(string source, string start, string end)
    {
        source = Normalize(source);
        start = Normalize(start);
        end = Normalize(end);
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

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
