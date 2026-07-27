using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ProductivityCommandSourceContractTests
{
    private static readonly string CommandsSource = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "Commands",
        "AcKrovyCommands.cs"));

    [Fact]
    public void NewCommands_AreRegisteredWithExpectedPickFirstFlags()
    {
        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.Diagnostics, CommandFlags.Modal)]",
            CommandsSource);
        Assert.Contains("AcKrovyCommandNames.SelectSimilar,", CommandsSource);
        Assert.Contains("AcKrovyCommandNames.ExportCsv,", CommandsSource);
        Assert.Contains("CommandFlags.Modal | CommandFlags.UsePickSet", CommandsSource);
    }

    [Fact]
    public void SelectSimilar_UsesReadOnlyScannerAndSetsImpliedSelection()
    {
        var method = Segment("private static void SelectSimilarCore()", "private static ObjectId PromptForSeedEntity");

        Assert.Contains("DrawingScanner", method);
        Assert.Contains("ReadAllTimberElements", method);
        Assert.Contains("OpenMode.ForRead", method);
        Assert.Contains("SetImpliedSelection", method);
        Assert.DoesNotContain("OpenMode.ForWrite", method);
        Assert.DoesNotContain("transaction.Commit()", method);
    }

    [Fact]
    public void CsvExport_ReadsModelSpaceAndNeverOpensEntitiesForWrite()
    {
        var export = Segment("private static void ExportCsvCore()", "private static IReadOnlyList<TimberElementMeasurement> ReadMeasurements");
        var reader = Segment("private static IReadOnlyList<TimberElementMeasurement> ReadMeasurements", "private static IReadOnlyList<ObjectId> PromptForManualEntities");

        Assert.Contains("DrawingScanner.FindAllTimberElements", export);
        Assert.Contains("TimberCsvFormatter.Format", export);
        Assert.Contains("SafeFileWriter.WriteAllBytes", export);
        Assert.Contains("OpenMode.ForRead", reader);
        Assert.DoesNotContain("OpenMode.ForWrite", export + reader);
        Assert.DoesNotContain("transaction.Commit()", export + reader);
    }

    [Fact]
    public void CommandBoundary_LogsUnexpectedFailuresWithoutWrappingExpectedCancelStatus()
    {
        var boundary = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Diagnostics",
            "CommandExecutionBoundary.cs"));

        Assert.Contains("CommandFailed", boundary);
        Assert.Contains("exception", boundary);
        Assert.DoesNotContain("PromptStatus.Cancel", boundary);
    }

    [Fact]
    public void NewCommandCancelPaths_ReturnNormally()
    {
        var select = Segment(
            "private static void SelectSimilarCore()",
            "private static ObjectId PromptForSeedEntity");
        var export = Segment(
            "private static void ExportCsvCore()",
            "private static IReadOnlyList<TimberElementMeasurement> ReadMeasurements");

        Assert.Contains("AcApp.ShowModalWindow(dialog) != true", select);
        Assert.Contains("if (seedId.IsNull)", select);
        Assert.Contains("AcApp.ShowModalWindow(optionsDialog) != true", export);
        Assert.Contains("saveDialog.ShowDialog() != true", export);
    }

    [Theory]
    [InlineData("AppLanguageSettingsStore.cs")]
    [InlineData("CustomElementDefinitionCatalogStore.cs")]
    [InlineData("ElementLayerProfileStore.cs")]
    [InlineData("SettingsUiPreferencesStore.cs")]
    [InlineData("TimberElementDefaultProfileStore.cs")]
    public void LocalJsonStores_UseCentralRecoverableSettingsManager(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Settings",
            fileName));

        Assert.Contains("AcKrovyDiagnostics.Settings", source);
        Assert.Contains(".Load(", source);
        Assert.Contains(".Save(", source);
    }

    private static string Segment(string start, string end)
    {
        var startIndex = CommandsSource.IndexOf(start, StringComparison.Ordinal);
        var endIndex = CommandsSource.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return CommandsSource.Substring(startIndex, endIndex - startIndex);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "AcKrovy.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
