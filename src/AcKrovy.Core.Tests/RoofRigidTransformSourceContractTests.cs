using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRigidTransformSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Store = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDefinitionStore.cs");
    private static readonly string Extractor = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPolylineExtractor.cs");
    private static readonly string Persistence = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs");
    private static readonly string Codec = Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionDataCodec.cs");

    [Fact]
    public void Workflow_PreservesNativeInputBeforeCanonicalValidation()
    {
        var readPath = Segment(Workflow, "while (true)", "private static void ShowPreview");
        Assert.Contains("sourceInput = RoofPolylineExtractor.Extract(polyline);", readPath);
        Assert.Contains("RoofFootprintValidator.Validate(sourceInput)", readPath);
        Assert.Contains("RoofDefinitionPersistence.Restore(\n                    sourceInput,", readPath);
        Assert.Contains("Enumerable.Range(0, polyline.NumberOfVertices)", Extractor);
        Assert.Contains("polyline.GetPoint3dAt(index)", Extractor);
        Assert.DoesNotContain("OrderBy", Extractor);
    }

    [Fact]
    public void V2Restore_DerivesDirectionFromCurrentSourceWithoutMutation()
    {
        var restoreV2 = Segment(Persistence, "private static RoofDefinitionRestoreResult RestoreV2", "private static RoofDefinitionRestoreResult Solve");
        Assert.Contains("TryReadSourceTopology(source", restoreV2);
        Assert.Contains("data.RidgeEdgeFamily", restoreV2);
        Assert.Contains("RoofDirection2D.TryCreate(edge.X, edge.Y", restoreV2);
        Assert.DoesNotContain("Encode", restoreV2);
        Assert.DoesNotContain("Write", restoreV2);
        Assert.DoesNotContain("XData", restoreV2);
    }

    [Fact]
    public void V1Read_RemainsSeparateAndDoesNotMigrate()
    {
        var restoreV1 = Segment(Persistence, "private static RoofDefinitionRestoreResult RestoreV1", "private static RoofDefinitionRestoreResult RestoreV2");
        Assert.Contains("footprint.Signature", restoreV1);
        Assert.Contains("data.FootprintSignature", restoreV1);
        Assert.Contains("data.RidgeDirectionX", restoreV1);
        Assert.DoesNotContain("CurrentVersion", restoreV1);
        Assert.DoesNotContain("Encode", restoreV1);
        Assert.DoesNotContain("RoofDefinitionPersistence.Create", restoreV1);
        Assert.DoesNotContain("new RoofDefinitionData", restoreV1);
    }

    [Fact]
    public void NewWriter_UsesV2TopologyPayloadWithoutAbsoluteWcsFields()
    {
        var create = Segment(Persistence, "public static RoofDefinitionData Create", "public static RoofDefinitionRestoreResult Restore");
        Assert.Contains("RoofDefinitionDataSchema.CurrentVersion", create);
        Assert.Contains("RidgeEdgeFamily: edgeFamily", create);
        Assert.Contains("RigidFootprint: topology.Descriptor", create);
        Assert.DoesNotContain("geometry.RidgeDirection.X,", create);
        Assert.DoesNotContain("footprint.Signature", create);

        var encodeV2 = Segment(Codec, "private static string EncodeV2", "private static bool TryDecodeV1");
        Assert.Contains("descriptor.Edge01LengthMm", encodeV2);
        Assert.Contains("descriptor.Edge12LengthMm", encodeV2);
        Assert.DoesNotContain("RidgeDirectionX", encodeV2);
        Assert.DoesNotContain("FootprintSignature", encodeV2);
    }

    [Fact]
    public void LifecycleReadPath_HasNoWriteOrMetadataRewrite()
    {
        var selection = Segment(Workflow, "while (true)", "private static void ShowPreview");
        Assert.Contains("OpenMode.ForRead", selection);
        Assert.Contains("RoofDefinitionStore.Read(polyline)", selection);
        Assert.DoesNotContain("OpenMode.ForWrite", selection);
        Assert.DoesNotContain("RoofDefinitionStore.Write", selection);
        Assert.DoesNotContain("transaction.Commit", selection);
        Assert.DoesNotContain("UpgradeOpen", selection);
        Assert.DoesNotContain("XData =", selection);
    }

    [Fact]
    public void NoRoofReactorOrDeferredWriteInfrastructureWasAdded()
    {
        var source = Workflow + Store + Extractor + Persistence;
        Assert.DoesNotContain("ObjectModified", source);
        Assert.DoesNotContain("CommandEnded", source);
        Assert.DoesNotContain("DatabaseReactor", source);
        Assert.DoesNotContain("ObjectOverrule", source);
        Assert.DoesNotContain("Deferred", source);
        Assert.DoesNotContain("WriteQueue", source);
    }

    [Fact]
    public void NoPermanentRoofEntityPathWasAdded()
    {
        var source = Workflow + Store + Extractor;
        Assert.DoesNotContain("AppendEntity", source);
        Assert.DoesNotContain("BlockTableRecord", source);
        Assert.DoesNotContain("new Line(", source);
        Assert.DoesNotContain("new Polyline(", source);
        Assert.DoesNotContain("3DFace", source);
        Assert.DoesNotContain("Mesh", source);
        Assert.DoesNotContain("Solid3d", source);
        Assert.DoesNotContain("BlockReference", source);
    }

    [Fact]
    public void WritePath_RemainsOnlyAfterExplicitConfirmation()
    {
        var confirmation = Workflow.IndexOf("ConfirmPersistence(editor)", StringComparison.Ordinal);
        var create = Workflow.IndexOf("RoofDefinitionPersistence.Create(", confirmation, StringComparison.Ordinal);
        var persist = Workflow.IndexOf("TryPersist(", create, StringComparison.Ordinal);
        var write = Workflow.IndexOf("RoofDefinitionStore.Write", persist, StringComparison.Ordinal);
        Assert.True(confirmation >= 0 && create > confirmation && persist > create && write > persist);
        Assert.Equal(1, CountOccurrences(Workflow, "transaction.Commit();"));
        Assert.Equal(1, CountOccurrences(Workflow, "OpenMode.ForWrite"));
        Assert.Equal(1, CountOccurrences(Store, "entity.XData = buffer;"));
    }

    [Fact]
    public void RoofSchema_RemainsIndependentFromTimberAndDrawingSchemas()
    {
        var stage4 = Persistence + Codec + Workflow + Store;
        Assert.DoesNotContain("TimberElementDataSchema", stage4);
        Assert.DoesNotContain("TimberDrawingSettings", stage4);
        Assert.Contains("DECORAIR_ACADKROVY_ROOF", Store);
    }

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Repository, .. path]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
