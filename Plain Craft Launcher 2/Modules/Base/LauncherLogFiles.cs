using System.Globalization;
using System.IO;
using System.IO.Compression;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;

namespace PCL;

internal static class LauncherLogFiles
{
    internal sealed record Entry(string Title, string Path, bool Current);
    internal static string DirectoryPath => LogService.Logger.Configuration.StoreFolder;
    private static HashSet<string> ActiveFiles() =>
        LogService.Logger.CurrentLogFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static Entry[] List()
    {
        var active = ActiveFiles();
        return Directory.EnumerateFiles(DirectoryPath).OrderByDescending(File.GetLastWriteTime)
            .Select(path =>
            {
                var current = active.Contains(Path.GetFullPath(path));
                var name = Path.GetFileName(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                if (stem.StartsWith("Launch-") && DateTime.TryParseExact(stem[7..], "yyyy-M-d-HHmmssfff",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    name = Lang.Date(time, "G");
                if (current) name = Lang.Text("Setup.Log.CurrentSuffix", name);
                else if (stem.StartsWith("LastPending"))
                    name = stem.Length > 12 ? Lang.Text("Setup.Log.TempStored", stem[12..]) : Lang.Text("Setup.Log.TempUnoutput");
                return new Entry(name, Path.GetFullPath(path), current);
            }).ToArray();
    }

    internal static void CleanHistory()
    {
        var active = ActiveFiles();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath))
            if (!active.Contains(Path.GetFullPath(file))) File.Delete(file);
    }

    internal static void Export(string destination, bool all)
    {
        var inputs = List().Where(entry => all || entry.Current || entry.Path.IsMatch(RegexPatterns.LastPendingLogPath)).ToArray();
        var target = Path.GetFullPath(destination);
        if (inputs.Any(file => StringComparer.OrdinalIgnoreCase.Equals(file.Path, target)))
            throw new IOException("导出位置与源日志相同。");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                foreach (var file in inputs)
                {
                    var entry = archive.CreateEntry(Path.GetFileName(file.Path), CompressionLevel.Fastest);
                    using var from = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var to = entry.Open();
                    from.CopyTo(to);
                }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
