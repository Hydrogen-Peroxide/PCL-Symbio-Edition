using System.Collections.Concurrent;
using System.IO;
using PCL.Core.IO.Net.Http;

namespace PCL;

internal static class ImageCache
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Pending = new(StringComparer.Ordinal);

    internal static string GetPath(string address) =>
        Path.Combine(ModBase.pathTemp, "Cache", "Images", ModBase.GetStringMD5(address) + ".png");

    internal static bool IsRemote(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    internal static Task<string?> FetchAsync(string address) =>
        Pending.GetOrAdd(address, key => new Lazy<Task<string?>>(() => Download(key))).Value;

    private static async Task<string?> Download(string address)
    {
        var destination = GetPath(address);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var response = await HttpRequest.Create(address).SendAsync(addMetedata: false).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.AsStreamAsync().ConfigureAwait(false))
                await input.CopyToAsync(output).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"加载远程图片失败（{address}）", ModBase.LogLevel.Developer);
            return null;
        }
        finally
        {
            Pending.TryRemove(address, out _);
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ModBase.Log(ex, "清理未完成的图片缓存失败", ModBase.LogLevel.Developer);
            }
        }
    }
}
