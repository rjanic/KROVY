using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRoofSectionVisualSourceContractTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string Gable = Read("GableRoofSectionControl.cs");
    private static readonly string Monopitch = Read("MonopitchRoofSectionControl.cs");
    private static readonly string Layout = Read("MonopitchRoofSectionLayout.cs");
    private static readonly string Style = Read("RoofSectionDiagramStyle.cs");

    [Fact]
    public void GableAndMonopitchUseOneRoofSectionVisualStyle()
    {
        Assert.Contains("RoofSectionDiagramStyle.GreenRoofFaceBrush", Gable);
        Assert.Contains("RoofSectionDiagramStyle.GreenRoofFaceBrush", Monopitch);
        Assert.Contains("RoofSectionDiagramStyle.CreateRoofFacePen", Gable);
        Assert.Contains("RoofSectionDiagramStyle.CreateRoofFacePen", Monopitch);
        Assert.Contains("Color.FromRgb(184, 117, 53)", Style);
        Assert.Contains("Color.FromRgb(112, 64, 31)", Style);
        Assert.Contains("Color.FromRgb(122, 79, 42)", Style);
        Assert.Contains("Color.FromRgb(166, 139, 114)", Style);
        Assert.Contains("Color.FromRgb(211, 154, 44)", Style);
        Assert.Contains("RoofFaceStrokeThickness = 12d", Style);
        Assert.Contains("DatumStrokeThickness = 1.5d", Style);
        Assert.Contains("ReferenceStrokeThickness = 1.25d", Style);
        Assert.Equal(1, Count(Style, "Color.FromRgb(112, 64, 31)"));
    }

    [Fact]
    public void MonopitchDrawsDashedHorizontalAndVerticalHighProjectionReferences()
    {
        Assert.Contains("CreateReferencePen(guide)", Monopitch);
        Assert.Contains("DrawLine(referencePen, low, highProjection)", Monopitch);
        Assert.Contains("DrawLine(referencePen, high, highProjection)", Monopitch);
        Assert.Contains("DashStyle = DashStyles.Dash", Style);
        Assert.Contains("HighProjection", Layout);
        Assert.Contains("new MonopitchRoofSectionPoint(high.X, low.Y)", Layout);
    }

    [Fact]
    public void MonopitchReusesGableAngleReferenceArcAndAboveRoofPlacement()
    {
        Assert.Contains("CreateAngleAnnotation(low, high)", Monopitch);
        Assert.Contains("RoofSectionDiagramStyle.MonopitchAngleBaseOutwardOffset", Monopitch);
        Assert.Contains("GableRoofSectionControl.DrawHorizontalAngleReference(", Monopitch);
        Assert.Contains("GableRoofSectionControl.DrawInteriorAngleArcTowardLowEave(", Monopitch);
        Assert.Contains("GableRoofSectionControl.CreateAngleArcVertex(", Monopitch);
        Assert.Contains("RoofSectionDiagramStyle.AngleArcRadius", Monopitch);
        Assert.Contains("AngleArcRadius = 80d", Style);
        Assert.DoesNotContain("DrawDirectionArrow", Monopitch);
        Assert.Contains("angleAnnotation.LabelOrigin", Monopitch);
        Assert.DoesNotContain("private static void DrawAngle(", Monopitch);
        Assert.Contains("const double positionAlongSlope = 0.32d", Gable);
        Assert.Contains("outsideAnchor.Y - 34d", Gable);
        Assert.Contains("AngleMaximumRoofwardReach = 34d", Style);
        Assert.Contains("AngleSafeRoofClearance = 8d", Style);
    }

    [Fact]
    public void LayoutIsUniformResponsiveUiGeometryOnly()
    {
        Assert.Contains("var uniformScale = Math.Min(", Layout);
        Assert.Contains("availableWidth / state.SpanMm", Layout);
        Assert.Contains("availableHeight / state.HeightDifferenceMm", Layout);
        Assert.Contains("state.IsMirrored", Layout);
        Assert.DoesNotContain("AcKrovy.Core", Layout);
        Assert.DoesNotContain("MonopitchRoofMath", Layout);
        Assert.DoesNotContain("RoofDefinition", Layout);
        Assert.DoesNotContain("Rafter", Layout);
        Assert.DoesNotContain("Autodesk", Layout + Monopitch + Style);
    }

    private static int Count(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(
        Root,
        "src",
        "AcKrovy.AutoCAD",
        "UI",
        fileName));

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
