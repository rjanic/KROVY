namespace AcKrovy.Infrastructure.IO;

public static class SafeFileWriter
{
    public static void WriteAllBytes(string path, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A destination path is required.", nameof(path));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Destination path must have a directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A failed cleanup must not hide the original write result.
            }
        }
    }
}
