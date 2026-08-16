using Xunit;



namespace AcKrovy.Core.Tests;



/// <summary>

/// Guards against reintroducing automatic AutoCAD CLI spam on roof grip/resize hot paths.

/// Manual on-demand DEBUG commands remain allowed outside these files.

/// </summary>

public sealed class RoofAutomaticDebugCliSpamSourceContractTests

{

    private static readonly string[] HotPathFiles =

    [

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs"),

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs"),

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripGeometrySnapshotService.cs"),

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripPreCommandBaselineService.cs"),

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs"),

        RoofUxSourceContractText.Read(

            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGroupGripResizeAdoptionService.cs"),

    ];



    private static readonly string[] ForbiddenBanners =

    [

        "AK_DEV_ROOF_GRIP_RAW",

        "AK_DEV_ROOF_GRIP_SNAP",

        "AK_DEV_ROOF_GRIP_BASELINE",

        "AK_DEV_ROOF_GROUP_GRIP_DELTA",

        "AK_DEV_ROOF_RESIZE_CLASSIFY",

        "AK_DEV_ROOF_RESIZE_APPLY",

        "AK_DEV_ROOF_LIVE_RESIZE_DIAG",

        "AK_DEV_ROOF_DISPLAY_REBUILD_DIAG",

    ];



    [Fact]

    public void RoofHotPaths_DoNotContainAutomaticDebugCliSpamBanners()

    {

        var combined = string.Concat(HotPathFiles);

        foreach (var banner in ForbiddenBanners)

        {

            Assert.DoesNotContain(banner, combined);

        }



        Assert.DoesNotContain("RoofGroupGripRawDiag", combined);

        Assert.DoesNotContain("WriteTimingSummary", combined);

        Assert.DoesNotContain("ReportRebuildEraseSelection", combined);

        Assert.DoesNotContain("ReportRebuildCreate", combined);

        Assert.DoesNotContain("ReportRebuildGroup", combined);

        Assert.DoesNotContain("ReportGroupGripDelta", combined);

        Assert.DoesNotContain("WriteLiveTopology", combined);

        Assert.DoesNotContain("WriteClassifyDiag", combined);

        Assert.DoesNotContain("WriteGripSemanticsDiag", combined);

    }

}


