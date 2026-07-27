namespace AcKrovy.Infrastructure.Settings;

public interface ISettingsFileSystem
{
    bool FileExists(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string content);

    void CreateDirectory(string path);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    long GetFileLength(string path);

    DateTime GetLastWriteTimeUtc(string path);
}

public sealed class PhysicalSettingsFileSystem : ISettingsFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (overwrite && File.Exists(destinationPath))
        {
            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);
            return;
        }

        File.Move(sourcePath, destinationPath);
    }

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}
