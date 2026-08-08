using System.Globalization;
using System.Text;

namespace AcKrovy.Infrastructure.Diagnostics;

public static class BuildInfoFormatter
{
    public static string Format(BuildInfoSnapshot info)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        var hasDllTime = TryParseLocal(info.DllLastWriteTimeLocal, out var dllTime);
        var hasProcessTime = TryParseLocal(
            info.ProcessStartTimeLocal,
            out var processStartTime);
        var status = hasDllTime && hasProcessTime
            ? processStartTime >= dllTime
                ? "OK"
                : "WARNING"
            : "UNKNOWN";
        var version = FirstAvailable(
            info.ProductVersion,
            info.InformationalVersion,
            info.AssemblyVersion);

        var text = new StringBuilder();
        text.AppendLine("=== ACAD KROVY BUILD INFO ===");
        text.AppendLine();
        text.Append("KROVY VERSION: ").AppendLine(version);
        text.AppendLine();
        text.Append("DLL BUILD TIME: ")
            .AppendLine(hasDllTime ? FormatFriendly(dllTime) : BuildInfoCollector.Unavailable);
        text.Append("AUTOCAD START TIME: ")
            .AppendLine(hasProcessTime
                ? FormatFriendly(processStartTime)
                : BuildInfoCollector.Unavailable);
        text.AppendLine();
        text.Append("STATUS=").AppendLine(status);
        text.AppendLine(status switch
        {
            "OK" => "AutoCAD bol spustený po tomto builde.",
            "WARNING" =>
                "DLL bola vytvorená po spustení AutoCADu.\nReštartuj AutoCAD.",
            _ => "Čas DLL alebo AutoCAD procesu nie je dostupný.",
        });
        text.AppendLine();
        text.AppendLine("LOADED DLL:");
        text.AppendLine(info.AssemblyLocation);
        text.AppendLine();
        text.AppendLine("--- TECHNICAL DETAILS ---");
        Append(text, "AssemblyLocation", info.AssemblyLocation);
        Append(text, "AssemblyName", info.AssemblyName);
        Append(text, "AssemblyVersion", info.AssemblyVersion);
        Append(text, "FileVersion", info.FileVersion);
        Append(text, "ProductVersion", info.ProductVersion);
        Append(text, "InformationalVersion", info.InformationalVersion);
        Append(text, "MVID", info.ModuleVersionId);
        Append(text, "ModuleVersionId", info.ModuleVersionId);
        Append(text, "DllLengthBytes", info.DllLengthBytes);
        Append(text, "DllLastWriteTimeLocal", info.DllLastWriteTimeLocal);
        Append(text, "DllLastWriteTimeUtc", info.DllLastWriteTimeUtc);
        Append(text, "DllSHA256", info.DllSha256);
        Append(text, "ProcessName", info.ProcessName);
        Append(text, "ProcessId", info.ProcessId);
        Append(text, "ProcessStartTimeLocal", info.ProcessStartTimeLocal);
        Append(text, "RuntimeVersion", info.RuntimeVersion);
        Append(text, "FrameworkDescription", info.FrameworkDescription);
        Append(text, "OSDescription", info.OsDescription);
        Append(text, "ProcessArchitecture", info.ProcessArchitecture);
        Append(text, "GitHead", info.GitHead);
        Append(text, "GitBranch", info.GitBranch);
        Append(text, "GitWorkingTreeDirty", info.GitWorkingTreeDirty);
        text.Append("=== END BUILD INFO ===");
        return text.ToString();
    }

    private static bool TryParseLocal(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);

    private static string FormatFriendly(DateTimeOffset value) =>
        value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FirstAvailable(params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(
                value,
                BuildInfoCollector.Unavailable,
                StringComparison.Ordinal)) ?? BuildInfoCollector.Unavailable;

    private static void Append(StringBuilder text, string key, string? value) =>
        text.Append(key)
            .Append('=')
            .AppendLine(string.IsNullOrWhiteSpace(value)
                ? BuildInfoCollector.Unavailable
                : value);
}
