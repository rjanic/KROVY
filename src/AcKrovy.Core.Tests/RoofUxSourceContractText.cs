namespace AcKrovy.Core.Tests;

internal static class RoofUxSourceContractText
{
    private static readonly string Repository = RepositoryRoot();

    public static string Read(params string[] path) =>
        Normalize(File.ReadAllText(Path.Combine([Repository, .. path])));

    public static string Member(string source, string start, string end)
    {
        source = Normalize(source);
        start = Normalize(start);
        end = Normalize(end);
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            throw new InvalidOperationException($"Source member markers not found: {start} -> {end}");
        }
        return source[startIndex..endIndex];
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
