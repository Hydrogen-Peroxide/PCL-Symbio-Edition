using System.IO;

namespace PCL;

internal sealed record BedrockInstanceStatistics(long Bytes, int Files, int Worlds, DateTime Created, DateTime Modified)
{
    internal static BedrockInstanceStatistics? Read(string directory, CancellationToken token)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return null;
        long bytes = 0;
        var count = 0;
        foreach (var file in root.EnumerateFiles("*", new EnumerationOptions
                 { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            token.ThrowIfCancellationRequested();
            bytes += file.Length;
            count++;
        }
        var worlds = Path.Combine(directory, "minecraftWorlds");
        return new(bytes, count, Directory.Exists(worlds) ? Directory.GetDirectories(worlds).Length : 0,
            root.CreationTime, root.LastWriteTime);
    }
}
