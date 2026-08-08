using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AcKrovy.Infrastructure.Diagnostics;

public static class BuildInfoCollector
{
    public const string Unavailable = "<unavailable>";

    public static BuildInfoSnapshot Collect(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var location = Safe(() => assembly.Location);
        var fileInfo = TryFileInfo(location);
        var versionInfo = TryVersionInfo(location);
        using var process = Process.GetCurrentProcess();

        return new BuildInfoSnapshot(
            AssemblyLocation: location,
            AssemblyName: Safe(() => assembly.GetName().Name),
            AssemblyVersion: Safe(() => assembly.GetName().Version?.ToString()),
            FileVersion: versionInfo?.FileVersion ?? Unavailable,
            ProductVersion: versionInfo?.ProductVersion ?? Unavailable,
            InformationalVersion: Safe(() => assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion),
            ModuleVersionId: Safe(() =>
                assembly.ManifestModule.ModuleVersionId.ToString("D")),
            DllLengthBytes: fileInfo is null
                ? Unavailable
                : fileInfo.Length.ToString(CultureInfo.InvariantCulture),
            DllLastWriteTimeLocal: fileInfo is null
                ? Unavailable
                : FormatLocal(fileInfo.LastWriteTime),
            DllLastWriteTimeUtc: fileInfo is null
                ? Unavailable
                : FormatUtc(fileInfo.LastWriteTimeUtc),
            DllSha256: ComputeSha256(location),
            ProcessName: Safe(() => process.ProcessName),
            ProcessId: Safe(() => process.Id.ToString(CultureInfo.InvariantCulture)),
            ProcessStartTimeLocal: Safe(() => FormatLocal(process.StartTime)),
            RuntimeVersion: Safe(() => Environment.Version.ToString()),
            FrameworkDescription: Safe(() => RuntimeInformation.FrameworkDescription),
            OsDescription: Safe(() => RuntimeInformation.OSDescription),
            ProcessArchitecture: Safe(() => RuntimeInformation.ProcessArchitecture.ToString()),
            GitHead: ReadAssemblyMetadata(assembly, "GitHead"),
            GitBranch: ReadAssemblyMetadata(assembly, "GitBranch"),
            GitWorkingTreeDirty:
                ReadAssemblyMetadata(assembly, "GitWorkingTreeDirty"));
    }

    public static string FormatLocal(DateTime value) =>
        value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture);

    public static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString(
            "yyyy-MM-dd HH:mm:ss.fff 'UTC'",
            CultureInfo.InvariantCulture);

    private static string ReadAssemblyMetadata(Assembly assembly, string key) =>
        Safe(() => assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                key,
                StringComparison.OrdinalIgnoreCase))?
            .Value);

    private static FileInfo? TryFileInfo(string location)
    {
        try
        {
            return location == Unavailable || !File.Exists(location)
                ? null
                : new FileInfo(location);
        }
        catch
        {
            return null;
        }
    }

    private static FileVersionInfo? TryVersionInfo(string location)
    {
        try
        {
            return location == Unavailable || !File.Exists(location)
                ? null
                : FileVersionInfo.GetVersionInfo(location);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string location)
    {
        try
        {
            if (location == Unavailable || !File.Exists(location))
            {
                return Unavailable;
            }

            using var stream = File.OpenRead(location);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var text = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
        catch (Exception exception)
        {
            var reason = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message.Replace('\r', ' ').Replace('\n', ' ');
            return $"<unavailable: {reason}>";
        }
    }

    private static string Safe(Func<string?> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? Unavailable : value!.Trim();
        }
        catch
        {
            return Unavailable;
        }
    }
}
