namespace AcKrovy.Core.Services;

public sealed class UnsupportedTimberElementDataSchemaException : NotSupportedException
{
    public UnsupportedTimberElementDataSchemaException(int schemaVersion, int currentVersion)
        : base($"Verzia údajov ACAD KROVY {schemaVersion} nie je podporovaná touto verziou doplnku. Podporovaná verzia je {currentVersion}.")
    {
        SchemaVersion = schemaVersion;
        CurrentVersion = currentVersion;
    }

    public int SchemaVersion { get; }
    public int CurrentVersion { get; }
}
