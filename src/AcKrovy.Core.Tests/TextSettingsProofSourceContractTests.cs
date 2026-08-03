using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TextSettingsProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CommandsPolicyAndService_AreEntirelyDebugOnly()
    {
        foreach (var source in new[] { Commands(), Policy(), Service() })
        {
            var trimmed = source.Trim();
            Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
            Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        }

        Assert.Contains("AK_DEV_TEXT_SETTINGS_CREATE", Commands());
        Assert.Contains("AK_DEV_TEXT_SETTINGS_VERIFY", Commands());
        Assert.Contains("AK_DEV_TEXT_SETTINGS_CLEAN", Commands());
    }

    [Fact]
    public void Create_UsesRealProductionAnnotationPathAndPresets()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("TimberAnnotationService.EnsureForElement(", create);
        Assert.Contains("AutoCadAnnotationPresentationBatchContext.Create(", create);
        Assert.Contains("AutoCadTextStylePresetService.EnsureBuiltIn(", create);
        Assert.Contains("AutoCadTextStylePresetService.EnsureUserPreset(", create);
        Assert.Contains("transaction.Commit();", create);
        Assert.Contains("StartOpenCloseTransaction()", create);
        Assert.Contains("VerifyCore(", create);
        Assert.DoesNotContain("new MText()", create);
        Assert.DoesNotContain("new MLeader()", create);
        Assert.DoesNotContain("new DBText()", create);
    }

    [Fact]
    public void Architecture2027Preflight_CountsOnlyRealExactModelSpaceEntities()
    {
        var create = Member(Service(), "public static void Create(");

        Assert.Contains("blockTable[BlockTableRecord.ModelSpace]", Service());
        Assert.Contains(".OfType<Entity>()", create);
        Assert.Contains("!entity.IsErased", create);
        Assert.Contains("entity.OwnerId == modelSpace.ObjectId", create);
        Assert.DoesNotContain("database.CurrentSpaceId", create);
        Assert.DoesNotContain("LayoutDictionary", create);
        Assert.DoesNotContain("PaperSpace", create);
    }

    [Fact]
    public void Verify_IsReadOnlyAndReportsPresentationFields()
    {
        var verify = Member(Service(), "public static void Verify(");
        var core = Member(Service(), "private static bool VerifyCore(");

        Assert.Contains("StartOpenCloseTransaction()", verify);
        Assert.DoesNotContain("OpenMode.ForWrite", verify + core);
        Assert.DoesNotContain("UpgradeOpen", verify + core);
        Assert.DoesNotContain("Commit();", verify + core);
        Assert.Contains("DBMOD", verify);
        Assert.Contains("NOT TESTED", Service() + Policy());
        Assert.Contains("TextStyleId", Service());
        Assert.Contains("modelHeight", Service());
    }

    [Fact]
    public void Clean_RemovesProofEntitiesAndRestoresProofOwnedUserPresetOnly()
    {
        var clean = Member(Service(), "public static void Clean(");
        var service = Service();

        Assert.Contains("SlopeAnnotationService.DeleteForSourceHandle(", clean);
        Assert.Contains("ElementLabelService.DeleteForSourceHandle(", clean);
        Assert.Contains("RestoreUserPresetLibrary(", clean);
        Assert.Contains("ProofOwnedUserPresetLibraryMutation", clean);
        Assert.Contains("TryCleanupProofOwnedUserTextStyle(", clean);
        Assert.Contains("proof-created USER TextStyle", service);
        Assert.Contains(
            "Existing user presets/styles were not deleted",
            service);
        Assert.DoesNotContain("Purge", clean);
    }

    [Fact]
    public void Policy_CoversItemDimensionSlopeBlocksUserPresetAndRoleIsolation()
    {
        var policy = Policy();

        Assert.Contains("\"IP\"", policy);
        Assert.Contains("\"IC\"", policy);
        Assert.Contains("\"IR\"", policy);
        Assert.Contains("\"IS\"", policy);
        Assert.Contains("\"CF\"", policy);
        Assert.Contains("\"FL\"", policy);
        Assert.Contains("\"DL\"", policy);
        Assert.Contains("\"SC\"", policy);
        Assert.Contains("\"SA\"", policy);
        Assert.Contains("\"IU\"", policy);
        Assert.Contains("UserFramedToken", policy);
        Assert.Contains("UserFramedTwinToken", policy);
        Assert.Contains("g3_host_user", policy);
        Assert.Contains("G3 Host User", policy);
        Assert.Contains("PreferredUserPresetFont", policy);
        Assert.Contains("\"HZ\"", policy);
        Assert.Contains("\"PP\"", policy);
        Assert.Contains("RoleIsolationToken", policy);
        Assert.Contains("RoleIsolationPatchedDimensionHeightMm", policy);
        Assert.Contains("RoleIsolationPatchedSlopeHeightMm", policy);
        Assert.DoesNotContain("RoleIsolationPatchedItemHeightMm", policy);
        Assert.Contains("ClassicStyleName", policy);
        Assert.Contains("ArchitecturalStyleName", policy);
        Assert.Contains("ItemUserPreset", policy);
        Assert.Contains("HorizontalMarker", policy);
        Assert.Contains("PostPerpendicular", policy);
        Assert.Contains("Denominator: 50", policy);
        Assert.Contains("Denominator: 100", policy);
        Assert.Contains("CreateUserPreset(", policy);
        Assert.Contains("ResolvePreferredUserFont(", policy);
        Assert.Contains("ExpectedFramedDefinitionHeightMm", policy);
        Assert.Contains(
            "ExpectedFramedAttributeHeightMm(",
            policy);
        Assert.Contains(
            "CalculateModelHeightMm(\n            paperHeightMm,\n            denominator)",
            policy.Replace("\r\n", "\n"));
        Assert.DoesNotContain(
            "ExpectedFramedAttributeHeightMm(double paperHeightMm)",
            policy.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Service_FramedInspection_UsesBaselineAttrHeightContract()
    {
        var service = Service().Replace("\r\n", "\n");
        var inspect = Member(
            service,
            "private static AutoCadTextSettingsProofRoleExpectation InspectFramedItem(");
        var verify = Member(
            service,
            "private static bool VerifyRoleExpectation(");

        Assert.Contains("ExpectedFramedDefinitionHeightMm", inspect);
        Assert.Contains(
            "ExpectedFramedAttributeHeightMm(\n                item.PaperHeightMm,\n                proofCase.Denominator)",
            inspect);
        Assert.Contains("CreateCanonicalName(", inspect);
        Assert.Contains("attribute.Height,", inspect);
        Assert.Contains("attribute.TextStyleId", inspect);
        Assert.Contains("ResolveStyleName(", inspect);
        Assert.Contains("framed AttributeReference.TextStyleId", inspect);
        Assert.Contains(
            "G3 AttributeDefinition.TextStyleId",
            inspect);
        Assert.Contains(
            "attributeDefinition.TextStyleId != attribute.TextStyleId",
            inspect);
        Assert.Contains("definitionStyle=", inspect);
        Assert.Contains("TextStyleIdentity=", inspect);
        Assert.Contains("geometry parity=PASS", inspect);
        Assert.Contains("ValidateExistingDefinitionDetailed(", inspect);
        Assert.Contains("AssertSharedUserFramedDefinition(", service);
        Assert.Contains("EnsureProofUserPresetInLibrary(", service);
        Assert.Contains("presentation.FramedItemCodeText", service);
        Assert.DoesNotContain(
            "attribute.Height * leader.BlockScale.X",
            inspect);
        Assert.DoesNotContain(
            "attribute.Height * leader.BlockScale.X",
            verify);
        Assert.Contains("modelHeight = attribute.Height;", verify);
    }

    [Fact]
    public void Service_RoleIsolation_PatchesDimensionSlopeLeavesItemAtDefault()
    {
        var service = Service().Replace("\r\n", "\n");
        var isolation = Member(
            service,
            "private static IReadOnlyList<AutoCadTextSettingsProofRoleExpectation>\n        RunRoleIsolation(");

        Assert.Contains(
            "CreateRoleIsolationPatchedSettings(",
            isolation);
        Assert.Contains(
            "DefaultItemCodePaperHeightMm",
            isolation);
        Assert.Contains(
            "RoleIsolationPatchedDimensionHeightMm",
            isolation);
        Assert.Contains(
            "RoleIsolationPatchedSlopeHeightMm",
            isolation);
        Assert.Contains(
            "ItemCode ObjectId/BlockContentId changed",
            isolation);
        Assert.DoesNotContain(
            "RoleIsolationPatchedItemHeightMm",
            isolation);
        Assert.DoesNotContain(
            "ItemCode did not adopt patched Arch",
            isolation);
    }

    private static string Commands() =>
        Source("src", "AcKrovy.AutoCAD", "Commands",
            "AutoCadTextSettingsProofCommands.cs");

    private static string Policy() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadTextSettingsProofPolicy.cs");

    private static string Service() =>
        Source("src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadTextSettingsProofService.cs");

    private static string Member(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing member {signature}");
        var brace = source.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
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
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces for {signature}");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
    }
}
