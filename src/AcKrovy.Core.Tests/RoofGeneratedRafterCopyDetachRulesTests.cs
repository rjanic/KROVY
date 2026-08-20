using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedRafterCopyDetachRulesTests
{
    [Fact]
    public void AppendedClone_DuplicatingPreCommandKey_Detaches()
    {
        var preKeys = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["291A"] = new[] { "Face0:s3", "Face1:s3" },
        };
        var preHandles = new[] { "29AD", "29AE" };
        var appended = new[]
        {
            new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine("29B0", "291A", RafterRoofFace.Face0, 3),
            new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine("29B1", "291A", RafterRoofFace.Face1, 3),
        };

        var detached = RoofGeneratedRafterCopyDetachRules.FindAppendedCloneDetachHandles(
            preKeys,
            preHandles,
            appended,
            wholeRoofRewriteMemberHandles: []);

        Assert.Equal(["29B0", "29B1"], detached.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void WholeRoofRewrite_ClaimedHandles_AreNotDetached()
    {
        var preKeys = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["291A"] = new[] { "Face0:s0" },
        };
        var appended = new[]
        {
            new RoofGeneratedRafterCopyDetachRules.AppendedGeneratedLine("NEW1", "291A", RafterRoofFace.Face0, 0),
        };

        var detached = RoofGeneratedRafterCopyDetachRules.FindAppendedCloneDetachHandles(
            preKeys,
            ["OLD1"],
            appended,
            ["NEW1"]);

        Assert.Empty(detached);
    }
}
