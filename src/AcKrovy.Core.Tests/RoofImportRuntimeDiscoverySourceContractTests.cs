using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofImportRuntimeDiscoverySourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Diagnostics = ReadAutoCad(
        "Infrastructure",
        "RoofImportRuntimeDiscoveryDiagnostics.cs");
    private static readonly string Plugin = ReadAutoCad("PluginEntry.cs");

    [Fact]
    public void Diagnostics_AreDebugOnlyAndReadOnly()
    {
        var trimmed = Diagnostics.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        Assert.Contains("OpenMode.ForRead", Diagnostics);
        Assert.DoesNotContain("OpenMode.ForWrite", Diagnostics);
        Assert.DoesNotContain("transaction.Commit", Diagnostics);
        Assert.DoesNotContain("UpgradeOpen", Diagnostics);
        Assert.DoesNotContain(".Erase(", Diagnostics);
        Assert.DoesNotContain("Idle", Diagnostics);
        Assert.DoesNotContain("Timer", Diagnostics);
    }

    [Fact]
    public void AutoCad2027EventSurface_IsSubscribedAndUnsubscribedSymmetrically()
    {
        var events = new[]
        {
            "CommandWillStart",
            "CommandEnded",
            "CommandCancelled",
            "CommandFailed",
            "ObjectAppended",
            "BeginInsert",
            "InsertMappingAvailable",
            "InsertEnded",
            "InsertAborted",
            "BeginDeepClone",
            "BeginDeepCloneTranslation",
            "DeepCloneEnded",
            "DeepCloneAborted",
            "WblockNotice",
            "BeginWblockBlock",
            "BeginWblockEntireDatabase",
            "BeginWblockObjects",
            "BeginWblockSelectedObjects",
            "WblockMappingAvailable",
            "WblockEnded",
            "WblockAborted",
        };

        foreach (var eventName in events)
        {
            Assert.Contains($" += {eventName};", Diagnostics);
            Assert.Contains($" -= {eventName};", Diagnostics);
        }
    }

    [Fact]
    public void WblockDiscovery_CapturesConstructedTransientDatabaseWithoutObsoleteTo()
    {
        Assert.Contains(
            "Database.DatabaseConstructed += DatabaseConstructed;",
            Diagnostics);
        Assert.Contains(
            "Database.DatabaseConstructed -= DatabaseConstructed;",
            Diagnostics);
        Assert.Contains("AttachTransientDatabase(candidate)", Diagnostics);
        Assert.Contains("scope=transient-wblock subscription=attached", Diagnostics);
        Assert.Contains("sender as Database ?? _database", Diagnostics);
        Assert.DoesNotContain("WblockNoticeEventArgs.To", Diagnostics);
        Assert.DoesNotContain("e.To;", Diagnostics);
    }

    [Fact]
    public void WblockTransientSubscriptions_AreDirectSymmetricAndLifecycleBound()
    {
        foreach (var eventName in new[]
                 {
                     "ObjectAppended",
                     "BeginDeepClone",
                     "BeginDeepCloneTranslation",
                     "DeepCloneEnded",
                     "DeepCloneAborted",
                     "BeginWblockBlock",
                     "BeginWblockEntireDatabase",
                     "BeginWblockObjects",
                     "BeginWblockSelectedObjects",
                     "WblockMappingAvailable",
                     "WblockEnded",
                     "WblockAborted",
                     "DatabaseToBeDestroyed",
                 })
        {
            Assert.Contains($"database.{eventName} +=", Diagnostics);
            Assert.Contains($"database.{eventName} -=", Diagnostics);
        }

        Assert.Contains("HashSet<Database> _transientDatabases", Diagnostics);
        Assert.Contains("MdiActiveDocument, _document", Diagnostics);
        Assert.Contains("IsDocumentDatabase(candidate)", Diagnostics);
        Assert.Contains("DetachTransientDatabase(eventDatabase);", Diagnostics);
        Assert.Contains("DetachAllTransientDatabases();", Diagnostics);
        Assert.Contains("ReleaseTransientReferences(database);", Diagnostics);
    }

    [Fact]
    public void Output_ContainsAllDeterministicDiscoveryRecordFamilies()
    {
        foreach (var prefix in new[]
                 {
                     "ROOF_IMPORT_EVENT",
                     "ROOF_IMPORT_MAP",
                     "ROOF_IMPORT_OBJECT",
                     "ROOF_IMPORT_BTR",
                     "ROOF_IMPORT_XDATA",
                     "ROOF_IMPORT_GROUP",
                     "ROOF_IMPORT_SUMMARY",
                 })
        {
            Assert.Contains(prefix, Diagnostics);
        }

        Assert.Contains("++_sequence", Diagnostics);
        Assert.Contains("rawCommand=", Diagnostics);
        Assert.Contains("normalizedCommand=", Diagnostics);
        Assert.Contains("documentDb=", Diagnostics);
        Assert.Contains("sourceDb=", Diagnostics);
        Assert.Contains("destinationDb=", Diagnostics);
        Assert.Contains("cloneContext=", Diagnostics);
        Assert.Contains("dbmod=", Diagnostics);
    }

    [Fact]
    public void MappingAndRecursiveGraph_UseExactIdsWithoutNameAuthority()
    {
        Assert.Contains("foreach (IdPair pair in mapping)", Diagnostics);
        Assert.Contains("pair.IsCloned", Diagnostics);
        Assert.Contains("pair.IsPrimary", Diagnostics);
        Assert.Contains("mapping.OriginalDatabase", Diagnostics);
        Assert.Contains("mapping.DestinationDatabase", Diagnostics);
        Assert.Contains("DumpBtrGraph(", Diagnostics);
        Assert.Contains("HashSet<ObjectId> visitedBtrs", Diagnostics);
        Assert.Contains("graph-cycle", Diagnostics);
        Assert.DoesNotContain("GroupNamePrefix", Diagnostics);
        Assert.DoesNotContain("AK_ROOF_", Diagnostics);
    }

    [Fact]
    public void MetadataDump_CoversRoofTimberLegacyAndAnnotationFamilies()
    {
        foreach (var token in new[]
                 {
                     "RoofDefinitionStore.Read",
                     "RoofDisplayStore.Read",
                     "RoofGeneratedTimberStore.Read",
                     "RoofAttachedManualTimberStore.Read",
                     "ElementDataStore.TryRead",
                     "ElementLabelStore.TryRead",
                     "SlopeArrowStore.TryRead",
                     "SlopeAngleTextStore.TryRead",
                     "PostFootprintPerpendicularAnnotationStore.TryRead",
                     "final1005=",
                     "childIdentity=",
                     "relativeSegment=",
                     "sourceHandle=",
                 })
        {
            Assert.Contains(token, Diagnostics);
        }
    }

    [Fact]
    public void Tracking_IsPerDocumentAndNeverFeedsProductionLifecycle()
    {
        Assert.Contains("Dictionary<Document, DocumentTracker>", Diagnostics);
        Assert.Contains("DocumentToBeDestroyed", Diagnostics);
        Assert.DoesNotContain("static readonly HashSet<ObjectId>", Diagnostics);
        Assert.DoesNotContain("LiveGeometrySynchronizationService.", Diagnostics);
        Assert.DoesNotContain("RoofForeignClipboardDegradationService", Diagnostics);
        Assert.DoesNotContain("RoofWholeRoofCopyRebindService", Diagnostics);
        Assert.Contains("IsDiscoveryCloneContext(e.IdMapping.DeepCloneContext)", Diagnostics);
        Assert.DoesNotContain("DeepCloneType.Copy or", Diagnostics);
    }

    [Fact]
    public void Plugin_WiresDiscoveryBeforeProductionLifecycleOnlyInDebug()
    {
        var start = Plugin.IndexOf(
            "RoofImportRuntimeDiscoveryDiagnostics.Start();",
            StringComparison.Ordinal);
        var production = Plugin.IndexOf(
            "LiveGeometrySynchronizationService.Start();",
            StringComparison.Ordinal);

        Assert.True(start >= 0 && start < production);
        Assert.Contains("RoofImportRuntimeDiscoveryDiagnostics.Stop();", Plugin);
        Assert.Contains("#if DEBUG", Plugin);
    }

    private static string ReadAutoCad(params string[] path) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot, "src", "AcKrovy.AutoCAD" }
                .Concat(path)
                .ToArray()));

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
