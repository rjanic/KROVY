using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinMaterializationAutoCadSourceContractTests
{
    private static readonly string Service = Read(
        "Infrastructure",
        "RoofAutomaticPurlinMaterializationService.cs");
    private static readonly string Store = Read(
        "Infrastructure",
        "RoofAutomaticPurlinGeneratedStore.cs");
    private static readonly string Collector = Read(
        "Infrastructure",
        "RoofAssemblyGroupMemberCollector.cs");
    private static readonly string GroupDiagnostics = Read(
        "Infrastructure",
        "RoofAssemblyGroupSyncService.cs");
    private static readonly string Commands = Read(
        "Commands",
        "AutoCadAutomaticPurlinMaterializationCommands.cs");
    private static readonly string ProductionCommands = Read(
        "Commands",
        "AcKrovyCommands.cs");

    [Fact]
    public void Materializer_EnsuresBoundaryIdentityAndUsesExistingRoofGeometry()
    {
        Assert.Contains("RoofPurlinLayoutStore.Read(owner)", Service);
        Assert.Contains("RoofBoundaryIdentityService.EnsureBoundaryIdentity", Service);
        Assert.Contains("RoofDefinitionPersistence.Restore", Service);
        Assert.Contains("RoofAutomaticPurlinPlanner.Create", Service);
        Assert.DoesNotContain("RoofBoundaryIdentityStore.Read(owner)", Service);
        Assert.DoesNotContain("CalculateSlopeCorrectedLength", Service);
    }

    [Fact]
    public void FirstMaterialization_EnsuresIdentityBeforeLayoutAndCommitsAllWritesOnce()
    {
        var command = Member(
            Commands,
            "public void Materialize()",
            "private static bool TryPromptNewLayout");
        Assert.Equal(1, Count(command, "StartTransaction()"));
        Assert.Equal(1, Count(command, "transaction.Commit()"));
        AssertOrdered(
            command,
            "RoofAutomaticPurlinMaterializationService.PrepareInTransaction",
            "RoofPurlinLayoutStore.Read(owner)",
            "RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction",
            "RoofPurlinLayoutStore.Write(owner, transaction, layout)",
            "transaction.Commit()");
        Assert.Contains("RoofBoundaryIdentityService.EnsureBoundaryIdentity", Service);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Service);
    }

    [Fact]
    public void FailureAfterEnsure_LeavesIdentityLayoutChildrenAndGroupToTransactionRollback()
    {
        var command = Member(
            Commands,
            "public void Materialize()",
            "private static bool TryPromptNewLayout");
        var materialize = command.IndexOf(
            "RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction",
            StringComparison.Ordinal);
        var failed = command.IndexOf("if (!result.IsSuccess)", materialize, StringComparison.Ordinal);
        var layoutWrite = command.IndexOf(
            "RoofPurlinLayoutStore.Write(owner, transaction, layout)",
            failed,
            StringComparison.Ordinal);
        var commit = command.IndexOf("transaction.Commit()", layoutWrite, StringComparison.Ordinal);

        Assert.True(materialize >= 0 && failed > materialize);
        Assert.True(layoutWrite > failed && commit > layoutWrite);
        Assert.Contains("firstLayout ? \"rolledback\" : layoutResult", command);
        Assert.Contains("return;", command[failed..layoutWrite]);
        Assert.DoesNotContain("transaction.Commit()", command[failed..layoutWrite]);
    }

    [Fact]
    public void SecondIdenticalInvocation_DoesNotRewriteIdentityOrLayoutOrCreateNewMembers()
    {
        var prepare = Member(
            Service,
            "public static RoofAutomaticPurlinMaterializationPreparation? PrepareInTransaction",
            "public static RoofAutomaticPurlinMaterializationResult MaterializePreparedInTransaction");
        var command = Member(
            Commands,
            "public void Materialize()",
            "private static bool TryPromptNewLayout");
        var existingLayout = Member(
            command,
            "if (storedLayout.Exists)",
            "var result = RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction");
        var unchanged = Member(
            Service,
            "if (member.Existing is not null)",
            "var line = new Line(member.Start, member.End);");
        var normalizedUnchanged = unchanged.Replace("\r\n", "\n");
        var noWriteStart = normalizedUnchanged.IndexOf(
            "else\n                {\n                    existingCount++;",
            StringComparison.Ordinal);
        Assert.True(noWriteStart >= 0);
        var noWrite = normalizedUnchanged[noWriteStart..];

        Assert.Contains("RoofBoundaryIdentityService.EnsureBoundaryIdentity", prepare);
        Assert.DoesNotContain("RoofBoundaryIdentityStore.Write", prepare);
        Assert.Contains("layout = storedLayout.Data!", existingLayout);
        Assert.DoesNotContain("RoofPurlinLayoutStore.Write", existingLayout);
        Assert.Contains("existingCount++", noWrite);
        Assert.DoesNotContain("AppendEntity", noWrite);
        Assert.DoesNotContain("WriteAtomic", noWrite);
    }

    [Fact]
    public void MissingLayout_IsExplicitReadOnlyFailure()
    {
        var method = Member(
            Service,
            "public static RoofAutomaticPurlinMaterializationResult MaterializePersistedLayoutInTransaction",
            "public static RoofAutomaticPurlinMaterializationResult MaterializeInTransaction");
        Assert.Contains("if (!storedLayout.Exists)", method);
        Assert.Contains("\"NoLayout\"", method);
        Assert.DoesNotContain(".Write(", method);
        Assert.DoesNotContain("AppendEntity", method);
    }

    [Fact]
    public void Reconcile_DiscoversOnlyExactOwnerMetadataAndFailsClosedBeforeMutation()
    {
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.ScanForOwner", Service);
        Assert.Contains("scan.MalformedIds.Count > 0", Service);
        Assert.Contains("RoofAutomaticPurlinMaterializationRules.Reconcile", Service);
        Assert.Contains("DuplicateExistingKey", Service);
        AssertOrdered(
            Service,
            "scan.MalformedIds.Count > 0",
            "RoofAutomaticPurlinMaterializationRules.Reconcile",
            "stale.Erase()",
            "modelSpace.AppendEntity(line)");
        Assert.Contains("RoofOwnerReference", Store);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", Store);
    }

    [Fact]
    public void Materializer_ReusesMatchingEntityPreservesValidIdAndRemovesStale()
    {
        Assert.Contains("existingByKey.TryGetValue(item.GeneratedKey", Service);
        Assert.Contains("RoofAutomaticPurlinElementIdAllocationRules.Assign", Service);
        Assert.Contains("ReadElementIdOwners", Service);
        Assert.Contains("FindAllTimberElements", Service);
        Assert.Contains("member.Existing.Line.StartPoint = member.Start", Service);
        Assert.Contains("member.Existing.Line.EndPoint = member.End", Service);
        Assert.Contains("stale.Erase()", Service);
        Assert.DoesNotContain("count == 1", Service);
        Assert.DoesNotContain("ElementNumberingService.GetNextNumber", Service);
        Assert.DoesNotContain("SynchronizeElementIds", Service);
    }

    [Fact]
    public void ElementIdAllocation_UsesGlobalOwnersSelfExclusionAndBatchReservation()
    {
        Assert.Contains("ReadElementIdOwners", Service);
        Assert.Contains("RoofAutomaticPurlinElementIdRequest", Service);
        Assert.Contains("RoofAutomaticPurlinElementIdAllocationRules.Assign", Service);
        Assert.Contains(
            "Database-wide ownership map over every readable intelligent Timber entity",
            Service);
        Assert.DoesNotContain("ResolveElementId(", Service);
        Assert.DoesNotContain("ReadElementIdCounts(", Service);
    }

    [Fact]
    public void UnchangedMatch_PerformsNoGeometryMetadataOrLayerWrite()
    {
        var update = Member(
            Service,
            "if (member.Existing is not null)",
            "var line = new Line(member.Start, member.End);");
        Assert.Contains("if (geometryChanged || metadataChanged || layerChanged)", update);
        Assert.Contains("if (geometryChanged)", update);
        Assert.Contains("if (metadataChanged)", update);
        Assert.Contains("if (layerChanged)", update);
        Assert.Contains("else\n                {\n                    existingCount++;", update.Replace("\r\n", "\n"));
    }

    [Fact]
    public void NewEntity_UsesRequiredAppendAtomicMetadataRegisterOrder()
    {
        var create = Member(
            Service,
            "var line = new Line(member.Start, member.End);",
            "createdCount++;");
        AssertOrdered(
            create,
            "modelSpace.AppendEntity(line)",
            "RoofAutomaticPurlinGeneratedStore.WriteAtomic",
            "transaction.AddNewlyCreatedDBObject(line, true)");
        Assert.Equal(1, Count(create, "WriteAtomic"));
    }

    [Fact]
    public void EveryMember_UsesPurlinSchemaDefaultsPlanLengthAndNoAnnotations()
    {
        var rules = ReadCore(
            "Services",
            "Roofs",
            "RoofAutomaticPurlinMaterializationRules.cs");
        Assert.Contains("TimberElementDefaults.For(TimberElementType.Purlin", rules);
        Assert.DoesNotContain("WidthMm = 160d", rules);
        Assert.DoesNotContain("HeightMm = 220d", rules);
        Assert.DoesNotContain("Material = \"Smrek C24\"", rules);
        Assert.DoesNotContain("CuttingAllowanceMm = 200d", rules);
        Assert.Contains("AnnotationMode = TimberAnnotationMode.NoAnnotations", rules);
        Assert.Contains("LengthCalculationMode = LengthCalculationMode.PlanLength", rules);
        Assert.Contains("ManualLengthMm = null", rules);
        Assert.Contains("TimberElementDataSchema.CurrentVersion", Service);
        Assert.Contains("RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion", Service);
        Assert.Contains("VerifyNoOwnedAnnotations", Service);
    }

    [Fact]
    public void ManualAndOtherGeneratedTimber_AreNotAutomaticPurlinCandidates()
    {
        Assert.Contains("ScanForOwner", Service);
        Assert.Contains("RoofGeneratedTimberStore.Read(line).Exists", Service);
        Assert.Contains("RoofStructuralGeneratedStore.Read(line).Exists", Service);
        Assert.Contains("RoofAttachedManualTimberStore.Read(line).Exists", Service);
        Assert.DoesNotContain("FindAllTimberElements(database, transaction, metadataStore).Where", Service);
    }

    [Fact]
    public void CanonicalGroup_HasDistinctAutomaticPurlinCategoryExactlyOnce()
    {
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.FindByOwner", Collector);
        Assert.Contains("AutomaticPurlinCount", Collector);
        Assert.Contains("members = new HashSet<ObjectId>", Collector);
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.Read(entity).Data", GroupDiagnostics);
        Assert.Contains("return \"AutomaticPurlin\"", GroupDiagnostics);
        Assert.Contains("automaticPurlin=", GroupDiagnostics);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Service);
        Assert.Contains("RoofDisplayGroupService.Inspect", Service);
    }

    [Fact]
    public void EditAndAssign_BlockAutomaticPurlinsBeforeDialogs()
    {
        Assert.Contains("ContainsAutomaticPurlinGenerated(document.Database, ids)", ProductionCommands);
        Assert.Contains("RoofAutomaticPurlinGeneratedStore.Read(entity).Exists", ProductionCommands);
        var assign = Member(
            ProductionCommands,
            "private static void AssignSelectedElements(",
            "[CommandMethod(AcKrovyCommandNames.Renumber");
        AssertOrdered(assign, "ContainsAutomaticPurlinGenerated", "new ElementEditWindow");
        var edit = Member(
            ProductionCommands,
            "public void Edit()",
            "[CommandMethod(AcKrovyCommandNames.FlipSlope");
        AssertOrdered(edit, "ContainsAutomaticPurlinGenerated", "new ElementEditWindow");
    }

    [Fact]
    public void DebugCommand_IsCompileTimeIsolatedAndFirstRunIsSingleTransaction()
    {
        Assert.StartsWith("#if DEBUG", Commands);
        Assert.EndsWith("#endif\n", Commands.Replace("\r\n", "\n"));
        Assert.Contains("AK_DEBUG_PURLIN_MATERIALIZE", Commands);
        AssertOrdered(
            Commands,
            "RoofAutomaticPurlinMaterializationService.PrepareInTransaction",
            "RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction",
            "RoofPurlinLayoutStore.Write(owner, transaction, layout)",
            "transaction.Commit()");
        Assert.Contains("if (storedLayout.Exists)", Commands);
        Assert.Contains("layout = storedLayout.Data!", Commands);
        Assert.Contains("RoofPurlinLayoutPersistenceRules.ValidateForWrite", Commands);
        Assert.Contains("firstLayout ? \"rolledback\" : layoutResult", Commands);
    }

    [Fact]
    public void MaterializationService_HasOnlyDebugProofAndProductionApplyCallers()
    {
        var callers = Directory.GetFiles(
                Path.Combine(RepositoryRoot(), "src", "AcKrovy.AutoCAD"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .Where(file => file.Source.Contains(
                "RoofAutomaticPurlinMaterializationService.Materialize",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, callers.Length);
        var debug = Assert.Single(callers, caller => caller.Path.EndsWith(
            "AutoCadAutomaticPurlinMaterializationCommands.cs",
            StringComparison.OrdinalIgnoreCase));
        var production = Assert.Single(callers, caller => caller.Path.EndsWith(
            "RoofAutomaticPurlinProductionApplyService.cs",
            StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("#if DEBUG", debug.Source);
        Assert.DoesNotContain("#if DEBUG", production.Source);
    }

    [Fact]
    public void DebugCommand_EmitsDeterministicHostProofFields()
    {
        Assert.Contains("ROOF_PURLIN_LAYOUT", Commands);
        Assert.Contains("ROOF_PURLIN_MEMBER", Commands);
        Assert.Contains("ROOF_PURLIN_MATERIALIZE", Commands);
        foreach (var field in new[]
                 {
                     "owner=", "handle=", "elementId=", "role=", "layoutId=",
                     "key=", "start=", "end=", "length=", "result=",
                     "desired=", "actual=", "ridge=", "intermediate=", "created=",
                     "existing=", "updated=", "staleRemoved=", "duplicates=",
                     "missing=", "groupCanonical=",
                     "bottomRelative=", "centerRelative=", "topRelative=", "seatingDepth=",
                 })
        {
            Assert.Contains(field, Commands);
        }
    }

    [Fact]
    public void Materializer_HasNoLifecycleCopyOrAutomaticAnnotationIntegration()
    {
        var combined = Service + Commands;
        Assert.DoesNotContain("CommandEnded", combined);
        Assert.DoesNotContain("ObjectModified", combined);
        Assert.DoesNotContain("ObjectAppended", combined);
        Assert.DoesNotContain("ObjectErased", combined);
        Assert.DoesNotContain("DeepClone", combined);
        Assert.DoesNotContain("Wblock", combined);
        Assert.DoesNotContain("TimberAnnotationService.Ensure", combined);
        Assert.DoesNotContain("ElementLabelService", combined);
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, value);
            previous = current;
        }
    }

    private static string Member(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, endMarker);
        return source[start..end];
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Read(string folder, string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            folder,
            fileName));

    private static string ReadCore(params string[] path) =>
        File.ReadAllText(path.Prepend("AcKrovy.Core").Prepend("src")
            .Prepend(RepositoryRoot()).Aggregate(Path.Combine));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
