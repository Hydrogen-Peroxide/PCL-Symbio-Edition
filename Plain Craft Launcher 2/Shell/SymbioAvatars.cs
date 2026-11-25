using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCL;

/// <summary>Loads the avatar images the shell and the account page show for Java profiles and Bedrock accounts.</summary>
internal static class SymbioAvatars
{
    private static readonly Dictionary<string, ImageSource?> BedrockCache = new();

    /// <summary>The cached skin head of a Java profile, if the launcher has already rendered it.</summary>
    public static ImageSource? Java(ModProfile.McProfile? profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.SkinHeadId)) return null;
        var file = Path.Combine(ModBase.pathTemp, "Cache", "Skin", "Head", profile.SkinHeadId + ".png");
        return File.Exists(file) ? Load(file) : null;
    }

    /// <summary>The Windows account picture cached for a Bedrock gamertag.</summary>
    public static ImageSource? Bedrock(string? gamertag)
    {
        if (string.IsNullOrWhiteSpace(gamertag)) return null;
        if (BedrockCache.TryGetValue(gamertag, out var cached)) return cached;
        var path = ModBedrockAvatar.Resolve(gamertag);
        var image = path is null ? null : Load(path);
        BedrockCache[gamertag] = image;
        return image;
    }

    public static ImageSource? Load(string source)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(source, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
