using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FramedTextAttributeHostProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Commands_AreEntirelyDebugOnlyAndNotRegisteredInProductUi()
    {
        var command = CommandSource().Trim();

        Assert.StartsWith("#if DEBUG", command, StringComparison.Ordinal);
        Assert.EndsWith("#endif", command, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_TEXTATTR_CREATE", command);
        Assert.Contains("AK_DEV_TEXTATTR_VERIFY", command);
        Assert.Contains("AK_DEV_TEXTSTYLE_DIAG", command);
        Assert.Contains("AK_DEV_TEXTATTR_MATRIX", command);
        Assert.Contains("AK_DEV_TEXTATTR_MATRIX_CLEAN", command);

        string[] productSurfaceDirectories =
        [
            Path.Combine(RepositoryRoot, "src", "AcKrovy.AutoCAD", "UI"),
            Path.Combine(RepositoryRoot, "src", "AcKrovy.Localization"),
        ];
        foreach (var directory in productSurfaceDirectories)
        {
            Assert.All(
                Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(file => new[] { ".cs", ".xaml", ".resx", ".json", ".md" }
                        .Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)),
                file =>
                {
                    var source = File.ReadAllText(file);
                    Assert.DoesNotContain("AK_DEV_TEXTATTR_CREATE", source);
                    Assert.DoesNotContain("AK_DEV_TEXTATTR_VERIFY", source);
                    Assert.DoesNotContain("AK_DEV_TEXTSTYLE_DIAG", source);
                    Assert.DoesNotContain("AK_DEV_TEXTATTR_MATRIX", source);
                    Assert.DoesNotContain("AK_DEV_TEXTATTR_MATRIX_CLEAN", source);
                });
        }
    }

    [Fact]
    public void HostProof_AppliesPerInstanceValuesBeforeSetBlockAttribute()
    {
        var source = ServiceSource();
        var createLeader = Member(
            source,
            "private static void CreateLeader(");
        var textIndex = createLeader.IndexOf(
            "attribute.TextString =",
            StringComparison.Ordinal);
        var styleIndex = createLeader.IndexOf(
            "attribute.TextStyleId =",
            StringComparison.Ordinal);
        var heightIndex = createLeader.IndexOf(
            "attribute.Height =",
            StringComparison.Ordinal);
        var applyIndex = createLeader.IndexOf(
            "leader.SetBlockAttribute(",
            StringComparison.Ordinal);

        Assert.True(textIndex >= 0 && textIndex < applyIndex);
        Assert.True(styleIndex >= 0 && styleIndex < applyIndex);
        Assert.True(heightIndex >= 0 && heightIndex < applyIndex);
        Assert.Contains("leader.GetBlockAttribute(", createLeader);
        Assert.Contains("leader.BlockScale = new Scale3d(", createLeader);
        Assert.Contains("Matrix3d.Identity", createLeader);
        Assert.Contains("actualStyleName: ReadCanonicalStyleName(", createLeader);
        Assert.Contains("definitionBase=", source);
        Assert.Contains("definitionStyle=", source);
    }

    [Fact]
    public void SharedDefinitionAndBlock_AreReadOnlyInProofCode()
    {
        var source = ServiceSource();

        Assert.Contains("block.BlockId", source);
        Assert.Contains("block.AttributeDefinitionId", source);
        Assert.Contains("OpenMode.ForRead", source);
        Assert.Equal(1, CountOccurrences(source, "OpenMode.ForWrite"));
        Assert.Contains("preserveExistingDefinition: true", source);
        Assert.DoesNotContain("sharedDefinition.UpgradeOpen", source);
        Assert.DoesNotContain("sharedBlock.UpgradeOpen", source);
        Assert.DoesNotContain("sharedDefinition.Height =", source);
        Assert.DoesNotContain("sharedDefinition.TextStyleId =", source);
        Assert.DoesNotContain("sharedDefinition.TextString =", source);
        Assert.DoesNotContain("sharedBlock.AppendEntity", source);
        Assert.DoesNotContain("OpenMode.ForWrite) is AttributeDefinition", source);
        Assert.DoesNotContain("OpenMode.ForWrite) is BlockTableRecord", source);
    }

    [Fact]
    public void HostProof_DoesNotMutateStylesMetadataProfilesOrDrawingSettings()
    {
        var source = ServiceSource() + CommandSource();

        Assert.DoesNotContain("AcKrovyMLeaderStyleService", source);
        Assert.DoesNotContain("MLeaderStyleTableRecord", source);
        Assert.DoesNotContain("database.Textstyle =", source);
        Assert.DoesNotContain("new TextStyleTableRecord", source);
        Assert.DoesNotContain("ElementDataStore", source);
        Assert.DoesNotContain("PrepareForWrite", source);
        Assert.DoesNotContain("TimberElementDefaultProfile", source);
        Assert.DoesNotContain("DrawingAnnotation", source);
        Assert.DoesNotContain("ApplicationSettings", source);
        Assert.DoesNotContain("DECORAIR_ACADKROVY", source);
        Assert.Contains("AK23_TEXTATTR_PROOF", source);
    }

    [Fact]
    public void HostProof_UsesStageTwoCatalogAndCentralHeightScaleAuthorities()
    {
        var service = ServiceSource();
        var policy = PolicySource();

        Assert.Contains("AutoCadTextStyleResolver.ReadCatalog(", service);
        Assert.Contains(
            "TimberAnnotationTextSettingsRules.CalculateModelHeightMm(",
            policy);
        Assert.Contains("TimberAnnotationScaleRules.DefaultDenominator", policy);
        Assert.Contains("TimberAnnotationScaleRules.GetScaleFactor(", policy);
        Assert.Contains(
            "TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm",
            policy);
        Assert.DoesNotContain("2.7d", policy);
        Assert.DoesNotContain("const double", policy);
    }

    [Fact]
    public void TextStyleDiagnostics_AreDebugOnlyReadOnlyAndExplainEveryDecision()
    {
        var command = CommandSource().Trim();
        var service = ServiceSource();
        var diagnostic = Member(
            service,
            "private static void WriteTextStyleDiagnostics(");

        Assert.StartsWith("#if DEBUG", command, StringComparison.Ordinal);
        Assert.Contains("AK_DEV_TEXTSTYLE_DIAG", command);
        Assert.Contains("ReadCatalogWithDiagnostics(", service);
        Assert.Contains("ObjectId.IsValid=", diagnostic);
        Assert.Contains("IsErased=", diagnostic);
        Assert.Contains("ToString(\"G17\"", diagnostic);
        Assert.Contains("Annotative=", diagnostic);
        Assert.Contains("ExpectedDbNative=", diagnostic);
        Assert.Contains("ActualDbNative=", diagnostic);
        Assert.Contains("ManagedReferenceEquals=", diagnostic);
        Assert.Contains("HostDatabaseIdentity=", diagnostic);
        Assert.Contains("ACCEPTED", diagnostic);
        Assert.Contains("REJECTED", diagnostic);
        Assert.DoesNotContain("OpenMode.ForWrite", diagnostic);
        Assert.DoesNotContain("Commit(", diagnostic);
    }

    [Fact]
    public void HostProof_UsesOneExplicitActiveDocumentDatabaseFlow()
    {
        var command = CommandSource();
        var service = ServiceSource();
        var resolver = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "AutoCadTextStyleResolver.cs");
        var combined = command + service + resolver;

        Assert.Contains("DocumentManager.MdiActiveDocument", command);
        Assert.Contains("var database = document.Database;", service);
        Assert.Contains(
            "database.TransactionManager.StartTransaction()",
            service);
        Assert.Contains("database.TextStyleTableId", resolver);
        Assert.DoesNotContain("HostApplicationServices.WorkingDatabase", combined);
    }

    [Fact]
    public void HostProof_PersistsOnlyDedicatedEntityXDataAndChecksDatabaseIdentity()
    {
        var source = ServiceSource();

        Assert.Contains("leader.XData = buffer", source);
        Assert.Contains("leader.GetXDataForApplication(ProofRegAppName)", source);
        Assert.Contains("AutoCadDatabaseIdentity.IsSame(", source);
        Assert.DoesNotContain(
            "ReferenceEquals(style.TextStyleId.Database, database)",
            source);
        Assert.DoesNotContain(
            "ReferenceEquals(textStyleId.Database, database)",
            source);
        Assert.DoesNotContain("static AutoCadTextStyleCatalog", source);
        Assert.DoesNotContain("private static Database", source);
        Assert.DoesNotContain("private static Transaction", source);
        Assert.DoesNotContain("private static DBObject", source);
    }

    [Fact]
    public void ProductionRenderers_AreNotConnectedToHostProof()
    {
        string[] rendererFiles =
        [
            "ElementLabelService.cs",
            "TimberAnnotationService.cs",
            "SlopeAnnotationService.cs",
            "SlopeAngleTextService.cs",
            "AcKrovyMLeaderStyleService.cs",
            "AcKrovyItemLeaderBlockService.cs",
        ];

        foreach (var file in rendererFiles)
        {
            var source = Source(
                "src",
                "AcKrovy.AutoCAD",
                "Infrastructure",
                file);
            Assert.DoesNotContain("AutoCadFramedTextAttributeProof", source);
            Assert.DoesNotContain("AK_DEV_TEXTATTR", source);
            Assert.DoesNotContain("AK23_TEXTATTR_PROOF", source);
            Assert.DoesNotContain("AutoCadFramedTextAttributeMatrix", source);
            Assert.DoesNotContain("AK23_TEXTATTR_MATRIX", source);
        }
    }

    [Fact]
    public void Matrix_IsDebugOnlyAndDefinesAllRequiredOperationOrders()
    {
        var service = MatrixServiceSource().Trim();
        var policy = MatrixPolicySource().Trim();

        Assert.StartsWith("#if DEBUG", service, StringComparison.Ordinal);
        Assert.EndsWith("#endif", service, StringComparison.Ordinal);
        Assert.StartsWith("#if DEBUG", policy, StringComparison.Ordinal);
        Assert.EndsWith("#endif", policy, StringComparison.Ordinal);
        Assert.Contains("PreDatabaseCurrent", policy);
        Assert.Contains("AppendBeforeSet", policy);
        Assert.Contains("GetModifySetAfterAppend", policy);
        Assert.Contains("SecondWriteTransaction", policy);
        Assert.Contains("BlockScaleAfterSet", policy);
        Assert.Contains("BeforeSetBlockAttribute", policy);
        Assert.Contains("AfterSetBlockAttribute", policy);
        Assert.Contains("RunSingleWriteTransaction(", service);
        Assert.Contains("CreateDefaultLeaderInFirstTransaction(", service);
        Assert.Contains("ModifyInSecondWriteTransaction(", service);
        Assert.Contains("ReadObservation(", service);
    }

    [Fact]
    public void Matrix_CommitsBeforeFreshPostCommitReadAndContinuesPerVariant()
    {
        var source = MatrixServiceSource();
        var runVariant = Member(
            source,
            "private static AutoCadFramedTextAttributeMatrixVariantResult RunVariant(");
        var singleWrite = Member(
            source,
            "RunSingleWriteTransaction(");

        Assert.Contains("foreach (var variant", source);
        Assert.Contains("catch (System.Exception exception)", runVariant);
        Assert.Contains("postCommit = ReadObservation(", runVariant);
        Assert.Contains("transaction.Commit();", singleWrite);
        Assert.Contains(
            "using var transaction = database.TransactionManager.StartTransaction();",
            Member(
                source,
                "private static AutoCadFramedTextAttributeMatrixObservation ReadObservation("));
        Assert.Contains("PRE-COMMIT", source);
        Assert.Contains("POST-COMMIT", source);
        Assert.Contains("HEIGHT=", source);
        Assert.Contains("STYLE=", source);
        Assert.Contains("HOST-SUPPORTED CANDIDATE", source);
    }

    [Fact]
    public void Matrix_HostOperationsFollowDeclaredVariantOrder()
    {
        var source = MatrixServiceSource();
        var preDatabase = SwitchCase(source, "PreDatabaseCurrent");
        var appendBefore = SwitchCase(source, "AppendBeforeSet");
        var getModify = SwitchCase(source, "GetModifySetAfterAppend");
        var scaleAfter = SwitchCase(source, "BlockScaleAfterSet");

        AssertAppearsBefore(preDatabase, "SetFromDefinition(", "AppendLeader(");
        AssertAppearsBefore(appendBefore, "AppendLeader(", "SetFromDefinition(");
        AssertAppearsBefore(getModify, "AppendLeader(", "ModifyExistingAttribute(");
        AssertAppearsBefore(
            scaleAfter,
            "SetFromDefinition(",
            "leader.BlockScale =");
        AssertAppearsBefore(
            source,
            "CreateDefaultLeaderInFirstTransaction(",
            "ModifyInSecondWriteTransaction(");
    }

    [Fact]
    public void Matrix_SharedDefinitionAndBlockRemainReadOnly()
    {
        var source = MatrixServiceSource();

        Assert.Contains("OpenMode.ForRead", source);
        Assert.DoesNotContain("definition.UpgradeOpen", source);
        Assert.DoesNotContain("definition.Height =", source);
        Assert.DoesNotContain("definition.TextStyleId =", source);
        Assert.DoesNotContain("definition.TextString =", source);
        Assert.DoesNotContain("sharedBlock.UpgradeOpen", source);
        Assert.DoesNotContain("sharedBlock.AppendEntity", source);
        Assert.DoesNotContain("MLeaderStyle", source);
        Assert.DoesNotContain("database.Textstyle =", source);
        Assert.DoesNotContain("new TextStyleTableRecord", source);
    }

    [Fact]
    public void Matrix_DiagnosticsExposeDefinitionExpectedAndActualValues()
    {
        var source = MatrixServiceSource();

        Assert.Contains("shared AttributeDefinition snapshot", source);
        Assert.Contains("ObjectId=", source);
        Assert.Contains("TextStyleId=", source);
        Assert.Contains("Height=", source);
        Assert.Contains("TextString=", source);
        Assert.Contains("Position=", source);
        Assert.Contains("Rotation=", source);
        Assert.Contains("definitionBaseHeight=", source);
        Assert.Contains("expectedBaseHeight=", source);
        Assert.Contains("expectedBlockScale=", source);
        Assert.Contains("expectedEffectiveHeight=", source);
        Assert.Contains("rawAttributeHeight=", source);
        Assert.Contains("normalizedBaseHeight=", source);
        Assert.Contains("actualEffectiveHeight=", source);
        Assert.Contains("definitionStyle=", source);
        Assert.Contains("expectedStyle=", source);
        Assert.Contains("actualStyle=", source);
        Assert.Contains("BlockScale=", source);
        Assert.Contains("AlignmentPoint=", source);
        Assert.Contains("WidthFactor=", source);
        Assert.Contains("Oblique=", source);
        Assert.Contains("LockPositionInBlock=", source);
        Assert.Contains("IsErased=", source);
    }

    [Fact]
    public void Matrix_NormalizesHostScaledHeightWithoutDoubleScaling()
    {
        var service = MatrixServiceSource();
        var policy = MatrixPolicySource();

        Assert.Contains("RawAttributeHeight / BlockScale", policy);
        Assert.Contains("? RawAttributeHeight", policy);
        Assert.DoesNotContain("RawAttributeHeight * BlockScale", policy);
        Assert.Contains("double.IsFinite(BlockScale)", policy);
        Assert.Contains("BlockScale > 0d", policy);
        Assert.Contains("BaseHeightStatus", policy);
        Assert.Contains("EffectiveHeightStatus", policy);
        Assert.Contains("BlockScaleStatus", policy);
        Assert.Contains("PER-INSTANCE BASE HEIGHT SUPPORT", service);
        Assert.Contains("PER-INSTANCE TEXT STYLE SUPPORT", service);
        Assert.Contains("BLOCK SCALE SUPPORT", service);
        Assert.Contains("SHARED ATTRIBUTE DEFINITION INTEGRITY", service);
    }

    [Fact]
    public void HostProof_UsesTheSameNormalizedHeightInterpretation()
    {
        var service = ServiceSource();

        Assert.Contains(
            "new AutoCadFramedTextAttributeHeightObservation(",
            service);
        Assert.Contains("normalizedBaseHeight=", service);
        Assert.Contains("actualEffectiveHeight=", service);
        Assert.DoesNotContain("attribute.Height * blockScale", service);
        Assert.DoesNotContain(
            "attribute.Height * leader.BlockScale.X",
            service);
    }

    [Fact]
    public void Matrix_DefinitionIntegrityIsFieldBasedAndExcludesRuntimeObjectId()
    {
        var service = MatrixServiceSource();
        var policy = MatrixPolicySource();

        Assert.Contains("CompareDefinitionSnapshots(", policy);
        Assert.Contains("integrityRelevant: false", policy);
        Assert.Contains("\"ObjectId\"", policy);
        Assert.Contains("ChangedIntegrityFields", policy);
        Assert.Contains("field-by-field integrity audit", service);
        Assert.Contains("CaptureDefinitionCheckpoint(", service);
        Assert.Contains("Definition checkpoint after", service);
        Assert.Contains("UNCHANGED:", service);
        Assert.Contains("CHANGED:", service);
        Assert.Contains("[DIAGNOSTIC ONLY]", service);
        Assert.DoesNotContain("current == setup.Definition", service);
    }

    [Fact]
    public void Matrix_CleanupErasesOnlyRecognizedDedicatedXDataEntities()
    {
        var service = MatrixServiceSource();
        var policy = MatrixPolicySource();
        var cleanup = Member(
            service,
            "public static void Cleanup(Document document)");

        Assert.Contains("AK23_TEXTATTR_MATRIX", service);
        Assert.Contains("TryReadMarker(leader, out _)", cleanup);
        Assert.Contains("leader.UpgradeOpen();", cleanup);
        Assert.Contains("leader.Erase();", cleanup);
        Assert.Contains("TryParseMarker(", policy);
        Assert.DoesNotContain("BlockTableRecord.Erase", cleanup);
        Assert.DoesNotContain("TextStyleTableRecord", cleanup);
        Assert.DoesNotContain("ProofRegAppName", service);
        Assert.DoesNotContain("DECORAIR_ACADKROVY", service);
    }

    [Fact]
    public void Matrix_UsesExistingProofHeightAuthorityAndNoNewPaperDefaults()
    {
        var policy = MatrixPolicySource();

        Assert.Contains(
            "AutoCadFramedTextAttributeProofPolicy.Cases[0].BaseAttributeHeight",
            policy);
        Assert.Contains(
            "AutoCadFramedTextAttributeProofPolicy.Cases[0].BlockScale",
            policy);
        Assert.Contains(
            "AutoCadFramedTextAttributeProofPolicy.Cases[2].BlockScale",
            policy);
        Assert.DoesNotContain("PaperHeightMm", policy);
        Assert.DoesNotContain("DefaultItemNumberPaperHeightMm", policy);
        Assert.DoesNotContain("const double", policy);
    }

    [Fact]
    public void Matrix_DoesNotWriteTimberMetadataProfilesOrDrawingSettings()
    {
        var source = MatrixServiceSource() + MatrixPolicySource();

        Assert.DoesNotContain("ElementDataStore", source);
        Assert.DoesNotContain("PrepareForWrite", source);
        Assert.DoesNotContain("TimberElementDefaultProfile", source);
        Assert.DoesNotContain("DrawingAnnotation", source);
        Assert.DoesNotContain("ApplicationSettings", source);
        Assert.DoesNotContain("Metadata", source);
        Assert.DoesNotContain("MLeaderStyle", source);
    }

    private static string CommandSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Commands",
        "AutoCadFramedTextAttributeProofCommands.cs");

    private static string ServiceSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadFramedTextAttributeProofService.cs");

    private static string PolicySource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadFramedTextAttributeProofPolicy.cs");

    private static string MatrixServiceSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadFramedTextAttributeMatrixService.cs");

    private static string MatrixPolicySource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadFramedTextAttributeMatrixPolicy.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Member(string source, string declarationPrefix)
    {
        source = NormalizeLineEndings(source);
        declarationPrefix = NormalizeLineEndings(declarationPrefix);
        var lines = source.Split('\n');
        var startLine = Array.FindIndex(
            lines,
            line => line.TrimStart().StartsWith(
                declarationPrefix,
                StringComparison.Ordinal));
        Assert.True(
            startLine >= 0,
            $"Member declaration not found: {declarationPrefix}");

        var startIndent = LeadingWhitespace(lines[startLine]);
        var endLine = lines.Length;
        for (var index = startLine + 1; index < lines.Length; index++)
        {
            if (LeadingWhitespace(lines[index]) <= startIndent &&
                IsMemberDeclaration(lines[index]))
            {
                endLine = index;
                break;
            }
        }

        return string.Join('\n', lines[startLine..endLine]);
    }

    private static string SwitchCase(string source, string variantName)
    {
        var lines = NormalizeLineEndings(source).Split('\n');
        for (var start = 0; start < lines.Length; start++)
        {
            if (!lines[start].TrimStart().StartsWith("case ", StringComparison.Ordinal))
            {
                continue;
            }

            var end = start + 1;
            while (end < lines.Length &&
                   !lines[end].TrimStart().StartsWith("case ", StringComparison.Ordinal) &&
                   !lines[end].TrimStart().StartsWith("default:", StringComparison.Ordinal))
            {
                end++;
            }

            var candidate = string.Join('\n', lines[start..end]);
            if (candidate.Contains(variantName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        Assert.Fail($"Switch case not found: {variantName}");
        return string.Empty;
    }

    private static bool IsMemberDeclaration(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("public ", StringComparison.Ordinal) ||
            trimmed.StartsWith("private ", StringComparison.Ordinal) ||
            trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
            trimmed.StartsWith("protected ", StringComparison.Ordinal);
    }

    private static int LeadingWhitespace(string line) =>
        line.Length - line.TrimStart().Length;

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static void AssertAppearsBefore(
        string source,
        string first,
        string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"First marker not found: {first}");
        Assert.True(secondIndex > firstIndex, $"Order mismatch: {first} / {second}");
    }

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
