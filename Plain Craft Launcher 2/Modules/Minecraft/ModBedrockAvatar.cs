using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace PCL;

/// <summary>
///     基岩版账号头像：只走 Windows 本地接口取图，不联网、不使用任何令牌。
///     <para>
///         解析顺序：① 当前用户设置的账户头像（注册表 <c>AccountPicture\Users\&lt;SID&gt;</c> 指向的 png/jpg）
///         → ② 系统账户头像缓存（<c>ProgramData\Microsoft\User Account Pictures\&lt;用户名&gt;.dat</c>，
///         accountpicture-ms 容器，内嵌 PNG/JPEG，需要解包）→ ③ 系统默认头像 <c>user.png</c>。
///         取到的图会复制/解包到 <c>.minecraft\bedrock\cache\avatar</c> 并复用，避免每次刷新都重新读系统文件。
///     </para>
/// </summary>
internal static class ModBedrockAvatar
{
    private static readonly string[] ImageValueNames = ["Image448", "Image192", "Image96", "Image64", "Image32"];

    /// <summary>头像缓存目录。</summary>
    private static string CacheDirectory => Path.Combine(ModBedrockInstance.DataRoot, "cache", "avatar");

    /// <summary>
    ///     取当前登录用户的系统头像，返回缓存文件路径；系统里没有可用图片时返回 <see langword="null" />。
    /// </summary>
    /// <param name="seed">用于在缓存目录里区分不同账号（通常是 gamertag 或邮箱）</param>
    public static string? Resolve(string? seed)
    {
        try
        {
            var source = FindSystemPicture();
            if (source is null) return null;

            var key = MakeCacheKey(seed);
            var target = Path.Combine(CacheDirectory, key + ".png");
            if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= source.Value.Stamp)
                return target;

            Directory.CreateDirectory(CacheDirectory);
            if (!TryMaterialize(source.Value.Path, target)) return null;
            ModBase.Log($"[Bedrock] 已缓存系统头像：{target}", ModBase.LogLevel.Debug);
            return target;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 读取系统头像失败", ModBase.LogLevel.Debug);
            return null;
        }
    }

    /// <summary>按用户设置的账户头像 → accountpicture-ms → 默认头像的顺序找一张可用的系统图片。</summary>
    private static (string Path, DateTime Stamp)? FindSystemPicture()
    {
        // ① 注册表里记录的账户头像文件（用户自己换过头像时才有）
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (!string.IsNullOrWhiteSpace(sid))
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
                if (key is not null)
                    foreach (var name in ImageValueNames)
                    {
                        if (key.GetValue(name) is not string path || !File.Exists(path)) continue;
                        var info = new FileInfo(path);
                        if (info.Length > 0) return (path, info.LastWriteTimeUtc);
                    }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 读取账户头像注册表失败", ModBase.LogLevel.Debug);
        }

        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var accountPictures = Path.Combine(commonData, "Microsoft", "User Account Pictures");

        // ② accountpicture-ms 容器（.dat），里面通常内嵌一张 PNG 或 JPEG
        try
        {
            var dat = Path.Combine(accountPictures, Environment.UserName + ".dat");
            if (File.Exists(dat) && new FileInfo(dat).Length > 1024)
                return (dat, File.GetLastWriteTimeUtc(dat));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 读取 accountpicture-ms 失败", ModBase.LogLevel.Debug);
        }

        // ③ 系统默认头像，保证界面上始终有一张图
        try
        {
            var fallback = Path.Combine(accountPictures, "user.png");
            if (File.Exists(fallback)) return (fallback, File.GetLastWriteTimeUtc(fallback));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 读取系统默认头像失败", ModBase.LogLevel.Debug);
        }

        return null;
    }

    /// <summary>把源文件写成缓存图片：普通图片直接复制，accountpicture-ms 则抽出内嵌的 PNG/JPEG。</summary>
    private static bool TryMaterialize(string sourcePath, string targetPath)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        if (bytes.Length == 0) return false;

        // 本身就是 png / jpg：直接落盘
        if (IsPng(bytes) || IsJpeg(bytes))
        {
            File.WriteAllBytes(targetPath, bytes);
            return true;
        }

        // accountpicture-ms：扫描内嵌图片的起始签名，截到文件末尾
        var start = FindEmbeddedImageStart(bytes);
        if (start < 0) return false;
        var slice = bytes[start..];
        File.WriteAllBytes(targetPath, slice);
        return true;
    }

    private static int FindEmbeddedImageStart(byte[] bytes)
    {
        for (var i = 0; i + 8 < bytes.Length; i++)
            if (IsPng(bytes.AsSpan(i)) || IsJpeg(bytes.AsSpan(i)))
                return i;
        return -1;
    }

    private static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    private static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    /// <summary>缓存文件名：只保留字母数字，避免账号名里的特殊字符落成非法路径。</summary>
    private static string MakeCacheKey(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed)) return "current";
        var cleaned = new string(seed.Where(char.IsLetterOrDigit).Take(32).ToArray());
        return cleaned.Length == 0 ? "current" : cleaned.ToLowerInvariant();
    }
}
