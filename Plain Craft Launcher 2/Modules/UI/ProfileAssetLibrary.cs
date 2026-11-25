using System.IO;
using System.Windows.Media.Imaging;

namespace PCL;

internal static class ProfileAssetLibrary
{
    internal static string LibraryFolder => Path.Combine(ModBase.pathAppdataConfig, "SkinLibrary");

    internal static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    internal static System.Drawing.Size ReadSkinSize(string file)
    {
        var image = new MyBitmap(file);
        return new System.Drawing.Size(image.pic.Width, image.pic.Height);
    }
}
