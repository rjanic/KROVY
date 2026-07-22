using System.Reflection;

namespace AcKrovy.Localization;

/// <summary>Provides a presentation-safe application version from central build metadata.</summary>
public static class ApplicationVersionProvider
{
    private const string UnknownVersion = "0.0.0";

    public static string DisplayVersion => GetDisplayVersion(typeof(ApplicationVersionProvider).Assembly);

    public static string GetDisplayVersion(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return NormalizeDisplayVersion(informationalVersion, assembly.GetName().Version);
    }

    public static string NormalizeDisplayVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var cleanVersion = informationalVersion!.Trim();
            var metadataSeparator = cleanVersion.IndexOf('+');
            cleanVersion = metadataSeparator >= 0
                ? cleanVersion.Substring(0, metadataSeparator)
                : cleanVersion;

            if (cleanVersion.Length > 0)
            {
                return cleanVersion;
            }
        }

        if (assemblyVersion is null)
        {
            return UnknownVersion;
        }

        return assemblyVersion.Build >= 0
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}";
    }
}
