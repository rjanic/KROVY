using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementDataVersioning
{
    public static bool IsSupported(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return ResolveVersion(data) <= TimberElementDataSchema.CurrentVersion;
    }

    public static TimberElementData Normalize(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var version = ResolveVersion(data);
        if (version > TimberElementDataSchema.CurrentVersion)
        {
            throw new UnsupportedTimberElementDataSchemaException(version, TimberElementDataSchema.CurrentVersion);
        }

        return data.SchemaVersion == version
            ? data
            : data with { SchemaVersion = version };
    }

    private static int ResolveVersion(TimberElementData data) =>
        data.SchemaVersion <= 0
            ? TimberElementDataSchema.LegacyImplicitVersion
            : data.SchemaVersion;
}
