using System.Globalization;
using AcKrovy.Infrastructure.Diagnostics;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class BuildInfoFormatterTests
{
    [Fact]
    public void Collect_ReportsActualModuleAndUppercaseSha256()
    {
        var info = BuildInfoCollector.Collect(typeof(BuildInfoFormatterTests).Assembly);

        Assert.True(Guid.TryParse(info.ModuleVersionId, out _));
        Assert.Matches("^[0-9A-F]{64}$", info.DllSha256);
        Assert.Equal(
            typeof(BuildInfoFormatterTests).Assembly.Location,
            info.AssemblyLocation);
    }

    [Fact]
    public void TimestampFormatting_IsInvariantAndDeterministic()
    {
        var utc = new DateTime(2026, 8, 8, 11, 0, 53, 125, DateTimeKind.Utc);

        Assert.Equal(
            "2026-08-08 11:00:53.125 UTC",
            BuildInfoCollector.FormatUtc(utc));
        Assert.DoesNotContain(
            CultureInfo.CurrentCulture.DateTimeFormat.DateSeparator +
            CultureInfo.CurrentCulture.DateTimeFormat.DateSeparator,
            BuildInfoCollector.FormatLocal(utc));
        Assert.Matches(
            "^2026-08-08 [0-9]{2}:[0-9]{2}:53\\.125 [+-][0-9]{2}:[0-9]{2}$",
            BuildInfoCollector.FormatLocal(utc));
    }

    [Fact]
    public void Format_KeepsUnavailableFieldsAndAllRequiredKeys()
    {
        var unavailable = BuildInfoCollector.Unavailable;
        var info = new BuildInfoSnapshot(
            unavailable,
            "AcKrovy.AutoCAD",
            "0.23.0.0",
            unavailable,
            unavailable,
            "0.23.0",
            "12345678-1234-1234-1234-1234567890ab",
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            "acad",
            "42",
            unavailable,
            "10.0.0",
            ".NET 10",
            "Windows",
            "X64",
            unavailable,
            unavailable,
            unavailable);

        var output = BuildInfoFormatter.Format(info);

        Assert.StartsWith("=== ACAD KROVY BUILD INFO ===", output);
        Assert.Contains("AssemblyLocation=<unavailable>", output);
        Assert.Contains("ModuleVersionId=12345678-1234-1234-1234-1234567890ab", output);
        Assert.Contains("DllSHA256=<unavailable>", output);
        Assert.Contains("GitHead=<unavailable>", output);
        Assert.EndsWith("=== END BUILD INFO ===", output);
    }

    [Fact]
    public void Format_UserFriendlyHeader_ReportsOkWhenAutoCadStartedAfterDll()
    {
        var output = BuildInfoFormatter.Format(CreateSnapshot(
            dllTime: "2026-08-08 17:45:12.000 +02:00",
            processTime: "2026-08-08 17:47:03.000 +02:00"));

        Assert.Contains("KROVY VERSION: 0.23.0", output);
        Assert.Contains("DLL BUILD TIME: 08.08.2026 17:45:12", output);
        Assert.Contains("AUTOCAD START TIME: 08.08.2026 17:47:03", output);
        Assert.Contains("STATUS=OK", output);
        Assert.Contains("AutoCAD bol spustený po tomto builde.", output);
        Assert.Contains(
            "LOADED DLL:\nC:\\Krovy\\AcKrovy.AutoCAD.dll",
            output.Replace("\r\n", "\n"));
        Assert.Contains("--- TECHNICAL DETAILS ---", output);
        Assert.Contains("MVID=12345678-1234-1234-1234-1234567890ab", output);
    }

    [Fact]
    public void Format_UserFriendlyHeader_WarnsWhenDllIsNewerThanAutoCadProcess()
    {
        var output = BuildInfoFormatter.Format(CreateSnapshot(
            dllTime: "2026-08-08 17:48:12.000 +02:00",
            processTime: "2026-08-08 17:47:03.000 +02:00"));

        Assert.Contains("STATUS=WARNING", output);
        Assert.Contains("DLL bola vytvorená po spustení AutoCADu.", output);
        Assert.Contains("Reštartuj AutoCAD.", output);
    }

    [Fact]
    public void AutoCadCommand_IsPermanentReadOnlyAndUsesLoadedAssembly()
    {
        var source = File.ReadAllText(
            FindRepoFile(
                "src",
                "AcKrovy.AutoCAD",
                "Commands",
                "AutoCadBuildInfoCommands.cs"));

        Assert.Contains("[CommandMethod(\"AK_BUILDINFO\"", source);
        Assert.Contains("typeof(AutoCadBuildInfoCommands).Assembly", source);
        Assert.DoesNotContain("StartTransaction", source);
        Assert.DoesNotContain("OpenMode.ForWrite", source);
        Assert.DoesNotContain("AppendEntity", source);
        Assert.DoesNotContain("git.exe", source, StringComparison.OrdinalIgnoreCase);
    }

    private static BuildInfoSnapshot CreateSnapshot(
        string dllTime,
        string processTime) =>
        new(
            "C:\\Krovy\\AcKrovy.AutoCAD.dll",
            "AcKrovy.AutoCAD",
            "0.23.0.0",
            "0.23.0.0",
            "0.23.0",
            "0.23.0",
            "12345678-1234-1234-1234-1234567890ab",
            "123456",
            dllTime,
            "2026-08-08 15:45:12.000 UTC",
            new string('A', 64),
            "acad",
            "42",
            processTime,
            "10.0.0",
            ".NET 10",
            "Windows",
            "X64",
            "abc123",
            "main",
            "false");

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
