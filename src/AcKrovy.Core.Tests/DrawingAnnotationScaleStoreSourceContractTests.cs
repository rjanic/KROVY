using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class DrawingAnnotationScaleStoreSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CadNeutralContract_DoesNotExposeAutodeskTypes()
    {
        var contract = Source(
            "src",
            "AcKrovy.Cad.Abstractions",
            "Drawing",
            "IDrawingAnnotationScaleStore.cs");

        Assert.Contains("bool TryRead(out int denominator);", contract);
        Assert.Contains("void Write(int denominator);", contract);
        Assert.Contains("void Remove();", contract);
        Assert.DoesNotContain("Autodesk", contract);
        Assert.DoesNotContain("Database", contract);
        Assert.DoesNotContain("Transaction", contract);
        Assert.DoesNotContain("ObjectId", contract);
    }

    [Fact]
    public void AutoCadStore_UsesStableDrawingLevelKeysAndTwoIntegers()
    {
        var store = StoreSource();

        Assert.Contains(
            "ApplicationDictionaryName = \"ACAD_KROVY\"",
            store);
        Assert.Contains(
            "DrawingSettingsRecordName = \"DRAWING_SETTINGS\"",
            store);
        Assert.Contains("DxfCode.Int32", store);
        Assert.Contains(
            "new TypedValue(DxfInt32Code, settings.SchemaVersion)",
            store);
        Assert.Contains("settings.AnnotationScaleDenominator", store);
        Assert.DoesNotContain("CANNOSCALE", store);
        Assert.DoesNotContain("AnnotationScaleDenominator", Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelStore.cs"));
    }

    [Fact]
    public void TryRead_IsStrictlyReadOnlyAndDoesNotRepairTheDrawing()
    {
        var read = Segment(
            StoreSource(),
            "public bool TryRead(out int denominator)",
            "public void Write(int denominator)");

        Assert.Contains("_database.NamedObjectsDictionaryId", read);
        Assert.Contains("OpenMode.ForRead", read);
        Assert.Contains("TryParsePayload", read);
        Assert.DoesNotContain("OpenMode.ForWrite", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain("SetAt(", read);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", read);
        Assert.DoesNotContain("Commit(", read);
        Assert.DoesNotContain("DocumentLock", read);
    }

    [Fact]
    public void Write_NormalizesAndOwnsNeitherTransactionNorDocumentLock()
    {
        var store = StoreSource();
        var write = Segment(
            store,
            "public void Write(int denominator)",
            "private DBDictionary GetOrCreateApplicationDictionary");

        Assert.Contains("TimberDrawingSettings.Create(denominator)", write);
        Assert.Contains("TryRead(out var existingDenominator)", write);
        Assert.Contains("record.Data = data", write);
        Assert.DoesNotContain("Commit(", store);
        Assert.DoesNotContain("DocumentLock", store);
    }

    [Fact]
    public void Remove_IsIdempotentAndUsesTheExistingDrawingRecord()
    {
        var remove = Segment(
            StoreSource(),
            "public void Remove()",
            "private DBDictionary GetOrCreateApplicationDictionary");

        Assert.Contains("DrawingSettingsRecordName", remove);
        Assert.Contains("if (root is null || !root.Contains", remove);
        Assert.Contains("applicationDictionary.Remove(DrawingSettingsRecordName)", remove);
        Assert.DoesNotContain("SetAt(", remove);
    }

    [Fact]
    public void ProductionAnnotationPath_UsesNoNativeAnnotativeContexts()
    {
        var labels = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");

        Assert.DoesNotContain("EnableAnnotationScale = true", labels);
        Assert.DoesNotContain("ApplyCurrentAnnotationScale", labels);
        Assert.DoesNotContain("ACDB_ANNOTATIONSCALES", labels);
        Assert.DoesNotContain("AddContext(", labels);
        Assert.DoesNotContain("CANNOSCALE", labels);
    }

    [Fact]
    public void ElementAnnotationServices_DoNotResolveScalePerElement()
    {
        var labels = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs");
        var annotations = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "TimberAnnotationService.cs");
        var upsert = Segment(
            labels,
            "public static bool UpsertForElement(",
            "public static bool UpsertForPostFootprint(");

        Assert.DoesNotContain("AutoCadAnnotationScaleService.Create", upsert);
        Assert.DoesNotContain("TimberElementDefaultProfileStore.Load", upsert);
        Assert.DoesNotContain("AutoCadAnnotationScaleService.Create", annotations);
        Assert.DoesNotContain("TimberElementDefaultProfileStore.Load", annotations);
    }

    [Fact]
    public void SettingsPath_ExposesOnlyCurrentDrawingScaleSelector()
    {
        var commands = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");
        var window = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");
        var xaml = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml");

        Assert.DoesNotContain("WriteAnnotationScaleToDrawing", commands);
        Assert.Contains("TimberDrawingAnnotationScaleChange", window);
        Assert.Contains("AnnotationScaleDenominator = _legacyUserDefaultScaleDenominator", window);
        Assert.Contains("DrawingAnnotationScaleSelector", xaml);
        Assert.DoesNotContain("UserDefaultAnnotationScaleSelector", xaml);
        Assert.DoesNotContain("UseDefaultAnnotationScale_Click", xaml);
        Assert.Contains("ReadAndMigrateAnnotationScaleSettingsState", commands);
    }

    private static string StoreSource() => Source(
        "src",
        "AcKrovy.AutoCAD",
        "Infrastructure",
        "AutoCadDrawingAnnotationScaleStore.cs");

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
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
