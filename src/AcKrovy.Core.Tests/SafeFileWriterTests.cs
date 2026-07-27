using AcKrovy.Infrastructure.IO;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SafeFileWriterTests
{
    [Fact]
    public void WriteAllBytes_CreatesAndAtomicallyReplacesDestinationWithoutTemporaryFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AcKrovyTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "export.csv");

            SafeFileWriter.WriteAllBytes(path, [1, 2, 3]);
            SafeFileWriter.WriteAllBytes(path, [4, 5]);

            Assert.Equal([4, 5], File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
