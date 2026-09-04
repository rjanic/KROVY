using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofImportDocumentLockVetoSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Probe = ReadAutoCad(
        "Infrastructure",
        "RoofImportDocumentLockVetoProbe.cs");
    private static readonly string Commands = ReadAutoCad(
        "Commands",
        "AutoCadRoofImportDocumentLockVetoCommands.cs");
    private static readonly string Plugin = ReadAutoCad("PluginEntry.cs");

    [Fact]
    public void ProbeAndCommands_AreDebugOnlyWithDebugOnlyPluginWiring()
    {
        AssertDebugOnly(Probe);
        AssertDebugOnly(Commands);
        AssertInsideDebugBlock(Plugin, "RoofImportDocumentLockVetoProbe.Start();");
        AssertInsideDebugBlock(Plugin, "RoofImportDocumentLockVetoProbe.Stop();");
        Assert.DoesNotContain("DocumentLockModeWillChange", Probe);
    }

    [Fact]
    public void Probe_DefaultsOffAndArmsOnlyTheCurrentDocument()
    {
        Assert.Contains("private static bool _isArmed;", Probe);
        Assert.Contains("private static Document? _armedDocument;", Probe);
        Assert.DoesNotContain("private static bool _isArmed = true;", Probe);
        Assert.DoesNotContain("_isArmed = true;", ExtractMethod(Probe, "public static void Start()"));
        Assert.Contains("_armedDocument = document;", Probe);
        Assert.Contains("_isArmed = true;", Probe);
        Assert.Contains("ReferenceEquals(e.Document, _armedDocument)", Probe);
        Assert.Contains("mode=one-shot state=on", Probe);
    }

    [Fact]
    public void ArmedPredicate_UsesSeparateExactInsertGlobalCommandNames()
    {
        Assert.Contains(
            "internal const string DashInsertGlobalCommandName = \"-INSERT\";",
            Probe);
        Assert.Contains(
            "internal const string PaletteInsertGlobalCommandName = \"INSERT\";",
            Probe);
        Assert.Contains(
            "internal const string ClassicInsertGlobalCommandName = \"CLASSICINSERT\";",
            Probe);
        Assert.Contains("Arm(document, DashInsertGlobalCommandName);", Probe);
        Assert.Contains("Arm(document, PaletteInsertGlobalCommandName);", Probe);
        Assert.Contains("Arm(document, ClassicInsertGlobalCommandName);", Probe);
        Assert.Contains("_armedGlobalCommandName = globalCommandName;", Probe);
        var normalizedProbe = Probe.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "command,\n                    _armedGlobalCommandName,\n                    StringComparison.Ordinal",
            normalizedProbe);
        Assert.Contains("StringComparison.Ordinal", Probe);
        Assert.DoesNotContain("OrdinalIgnoreCase", Probe);
        Assert.DoesNotContain("NormalizeCommandName", Probe);
        Assert.DoesNotContain("TrimStart", Probe);
        Assert.DoesNotContain("StartsWith", Probe);
        Assert.DoesNotContain("EndsWith", Probe);
        Assert.DoesNotContain("ToUpper", Probe);
        Assert.DoesNotContain("ToLower", Probe);
    }

    [Fact]
    public void VetoCallback_UsesActual2027PropertiesAndVetoIsTheOnlyHostAction()
    {
        Assert.Contains("ROOF_IMPORT_LOCK_VETO command=", Probe);
        Assert.Contains("currentMode={e.CurrentMode}", Probe);
        Assert.Contains("myPreviousMode={e.MyPreviousMode}", Probe);
        Assert.Contains("myCurrentMode={e.MyCurrentMode}", Probe);
        Assert.DoesNotContain("MyNewMode", Probe);
        Assert.Equal(1, CountOccurrences(Probe, "e.Veto();"));

        var callbackStart = Probe.IndexOf(
            "private static void DocumentLockModeChanged(",
            StringComparison.Ordinal);
        var disarmIndex = Probe.IndexOf("_isArmed = false;", callbackStart, StringComparison.Ordinal);
        var targetClearIndex = Probe.IndexOf("_armedGlobalCommandName = null;", callbackStart, StringComparison.Ordinal);
        var vetoIndex = Probe.IndexOf("e.Veto();", callbackStart, StringComparison.Ordinal);
        Assert.True(callbackStart >= 0);
        Assert.True(disarmIndex > callbackStart && disarmIndex < vetoIndex);
        Assert.True(targetClearIndex > disarmIndex && targetClearIndex < vetoIndex);

        foreach (var forbidden in new[]
                 {
                     "StartTransaction",
                     "StartOpenCloseTransaction",
                     "OpenMode.ForWrite",
                     "UpgradeOpen",
                     ".Erase(",
                     "SetXData",
                     "TransactionManager",
                     ".LockDocument(",
                     "Idle",
                     "Timer",
                     "UserBreak",
                     "throw ",
                 })
        {
            Assert.DoesNotContain(forbidden, Probe);
        }
    }

    [Fact]
    public void VetoNotificationAndCleanup_AreSubscribedSymmetrically()
    {
        foreach (var eventName in new[]
                 {
                     "DocumentLockModeChanged",
                     "DocumentLockModeChangeVetoed",
                 })
        {
            Assert.Contains($"documents.{eventName} +=", Probe);
            Assert.Contains($"documents.{eventName} -=", Probe);
        }

        Assert.Contains("ROOF_IMPORT_LOCK_VETOED command=", Probe);
        Assert.Contains("ClearState();", ExtractMethod(Probe, "public static void Stop()"));
        Assert.Contains("_isArmed = false;", Probe);
        Assert.Contains("_armedDocument = null;", Probe);
        Assert.Contains("_armedGlobalCommandName = null;", Probe);
        Assert.Contains("_pendingVetoDocument = null;", Probe);
        Assert.Contains("_pendingVetoCommand = null;", Probe);
    }

    [Fact]
    public void ExplicitCommands_ArmAndDisarmWithoutUndoMarkers()
    {
        Assert.Contains("AK_DEBUG_INSERT_VETO_ON", Commands);
        Assert.Contains("AK_DEBUG_INSERT_PALETTE_VETO_ON", Commands);
        Assert.Contains("AK_DEBUG_CLASSICINSERT_VETO_ON", Commands);
        Assert.Contains("AK_DEBUG_INSERT_VETO_OFF", Commands);
        Assert.Contains("CommandFlags.NoUndoMarker", Commands);
        Assert.Contains("RoofImportDocumentLockVetoProbe.ArmDashInsert(document);", Commands);
        Assert.Contains("RoofImportDocumentLockVetoProbe.ArmPaletteInsert(document);", Commands);
        Assert.Contains("RoofImportDocumentLockVetoProbe.ArmClassicInsert(document);", Commands);
        Assert.Contains("RoofImportDocumentLockVetoProbe.Disarm(", Commands);
    }

    private static void AssertDebugOnly(string source)
    {
        var trimmed = source.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
    }

    private static void AssertInsideDebugBlock(string source, string token)
    {
        var tokenIndex = source.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenIndex >= 0, $"Token not found: {token}");

        var debugIndex = source.LastIndexOf("#if DEBUG", tokenIndex, StringComparison.Ordinal);
        var endIndex = source.LastIndexOf("#endif", tokenIndex, StringComparison.Ordinal);
        Assert.True(debugIndex >= 0 && debugIndex > endIndex, $"Token is not DEBUG-guarded: {token}");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");

        var nextMethod = source.IndexOf("\n    public static void ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
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
