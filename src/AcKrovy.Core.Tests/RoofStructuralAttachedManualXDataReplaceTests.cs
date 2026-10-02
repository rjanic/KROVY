using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// M1 COPY Generated→Manual XData replacement contracts.
/// AutoCAD HOST is authoritative for merge semantics; these prove the intended
/// erase-sentinel wiring and a CAD-neutral model of why filter-only rewrite fails.
/// </summary>
public sealed class RoofStructuralAttachedManualXDataReplaceTests
{
    private const string GeneratedRegApp = "DECORAIR_ACADKROVY_ROOF_STRUCTURAL_GENERATED";
    private const string ManualRegApp = "DECORAIR_ACADKROVY_ROOF_STRUCTURAL_ATTACHED_MANUAL";
    private const string ForeignRegApp = "DECORAIR_ACADKROVY";

    [Fact]
    public void WriteReplacingGenerated_UsesRegAppOnlyEraseSentinel_BeforeManualWrite()
    {
        var store = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofStructuralAttachedManualStore.cs");
        var method = Segment(store, "private static void WriteWithoutGenerated",
            "private static bool HasApplicationSection");
        Assert.Contains("HasApplicationSection(entity, RoofStructuralGeneratedStore.RegAppName)", method);
        Assert.Contains("eraseGenerated", method);
        Assert.Contains("eraseManual", method);
        Assert.Contains("new TypedValue(DxfRegAppNameCode, RoofStructuralGeneratedStore.RegAppName)", method);
        Assert.Contains("new TypedValue(DxfRegAppNameCode, RegAppName)", method);
        Assert.Contains("entity.XData = eraseGenerated", method);
        Assert.Contains("entity.XData = eraseManual", method);
        Assert.Contains("manualData is not null && HasApplicationSection(entity, RegAppName)", method);
        // Must not rely on filter-only full rewrite to clear Generated (AutoCAD merges).
        Assert.DoesNotContain("skip =", method);
        Assert.Contains("BuildSection(entity, transaction, manualData)", method);
    }

    [Fact]
    public void TryConvertClone_VerifiesLiveSameTransactionRead_NotSnapshot()
    {
        var router = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofStructuralNativeEditService.cs");
        var convert = Segment(router, "private static bool TryConvertCloneToAttachedManual",
            "private static bool VerifyCommitted");
        Assert.Contains("WriteReplacingGenerated(line, transaction, created.Data)", convert);
        Assert.Contains("var generatedAfter = RoofStructuralGeneratedStore.Read(line)", convert);
        Assert.Contains("var manualAfter = RoofStructuralAttachedManualStore.Read(line)", convert);
        Assert.Contains("GeneratedIdentityNotCleared", convert);
        Assert.Contains("ManualIdentityNotPersisted", convert);
        Assert.Contains("Same-transaction live reread", convert);
        Assert.DoesNotContain("MEMBER_CHECKPOINT", convert);
    }

    [Fact]
    public void AutoCadXDataMergeModel_FilterOnlyRewriteLeavesGeneratedPresent()
    {
        // CAD-neutral model of Entity.XData merge: assigning a buffer updates only
        // RegApps present in that buffer. Omitting Generated does NOT erase it.
        var before = new List<(string RegApp, string Payload)>
        {
            (ForeignRegApp, "timber"),
            (GeneratedRegApp, "Hip|1|4"),
        };
        var filtered = FilterOut(before, GeneratedRegApp, ManualRegApp);
        filtered.Add((ManualRegApp, "ManualStructural:guid"));
        var afterMerge = MergeByRegApp(before, filtered);

        Assert.Contains(afterMerge, s => s.RegApp == GeneratedRegApp);
        Assert.Contains(afterMerge, s => s.RegApp == ManualRegApp);
        Assert.Contains(afterMerge, s => s.RegApp == ForeignRegApp);
    }

    [Fact]
    public void AutoCadXDataMergeModel_RegAppOnlyEraseThenManualWrite_ClearsGenerated()
    {
        var before = new List<(string RegApp, string Payload)>
        {
            (ForeignRegApp, "timber"),
            (GeneratedRegApp, "Hip|1|4"),
        };
        var afterErase = EraseRegApp(before, GeneratedRegApp);
        Assert.DoesNotContain(afterErase, s => s.RegApp == GeneratedRegApp);
        Assert.Contains(afterErase, s => s.RegApp == ForeignRegApp);

        var afterManual = MergeByRegApp(
            afterErase,
            [(ManualRegApp, "ManualStructural:guid")]);
        Assert.DoesNotContain(afterManual, s => s.RegApp == GeneratedRegApp);
        Assert.Contains(afterManual, s => s.RegApp == ManualRegApp);
        Assert.Contains(afterManual, s => s.RegApp == ForeignRegApp);
    }

    [Fact]
    public void ConversionInvariant_GeneratedAbsent_AndManualPresent()
    {
        // Documents the exact postcondition TryConvertCloneToAttachedManual enforces.
        var after = MergeByRegApp(
            EraseRegApp(
                [
                    (ForeignRegApp, "timber"),
                    (GeneratedRegApp, "Hip|1|4"),
                ],
                GeneratedRegApp),
            [(ManualRegApp, "ManualStructural:guid")]);

        Assert.Null(Find(after, GeneratedRegApp));
        Assert.NotNull(Find(after, ManualRegApp));
        Assert.NotNull(Find(after, ForeignRegApp));
    }

    [Fact]
    public void ManualDataRules_StillCreateValidCopyChildFromGeneratedLogicalKey()
    {
        var key = new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 1, 4);
        var created = RoofStructuralAttachedManualDataRules.Create(
            "2912",
            RoofStructuralAttachedManualIdentityRules.Create(),
            key,
            RoofStructuralAttachedManualCreationKind.Copy,
            120d,
            RoofStructuralHeightMode.Automatic,
            null);
        Assert.True(created.IsValid, created.Error.ToString());
        Assert.Equal(key, created.Data!.SourceLogicalKey);
        Assert.StartsWith("ManualStructural:",
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(created.Data.ManualIdentity));
    }

    private static List<(string RegApp, string Payload)> FilterOut(
        IEnumerable<(string RegApp, string Payload)> source,
        params string[] drop)
    {
        var banned = new HashSet<string>(drop, StringComparer.OrdinalIgnoreCase);
        return source.Where(s => !banned.Contains(s.RegApp)).ToList();
    }

    private static List<(string RegApp, string Payload)> EraseRegApp(
        IEnumerable<(string RegApp, string Payload)> source,
        string regApp) =>
        source.Where(s => !string.Equals(s.RegApp, regApp, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static List<(string RegApp, string Payload)> MergeByRegApp(
        IEnumerable<(string RegApp, string Payload)> existing,
        IEnumerable<(string RegApp, string Payload)> assigned)
    {
        var map = existing.ToDictionary(
            s => s.RegApp, s => s.Payload, StringComparer.OrdinalIgnoreCase);
        foreach (var section in assigned)
            map[section.RegApp] = section.Payload;
        return map.Select(pair => (pair.Key, pair.Value)).ToList();
    }

    private static (string RegApp, string Payload)? Find(
        IEnumerable<(string RegApp, string Payload)> sections,
        string regApp)
    {
        foreach (var section in sections)
        {
            if (string.Equals(section.RegApp, regApp, StringComparison.OrdinalIgnoreCase))
                return section;
        }

        return null;
    }

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine([root, .. path]));
    }

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
