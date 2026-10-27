using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PCL;

/// <summary>Filesystem boundaries shared by Bedrock package extraction and installation.</summary>
internal static class BedrockFileSystem
{
    internal static string GetChildPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("安装包包含无效的相对路径。");

        var parts = relativePath.Split(['\\', '/']);
        foreach (var part in parts)
        {
            foreach (var character in part)
                if (character < 32)
                    throw new InvalidDataException("安装包包含无效的文件名字符。");
            if (part.Length == 0 || part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*', '\0']) >= 0)
                throw new InvalidDataException("安装包包含不安全的文件路径。");

            var stem = part.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && stem[3] is >= '0' and <= '9' &&
                 (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("安装包包含 Windows 保留的文件名。");
        }

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(parts)));
        if (!IsChildPath(rootPath, path))
            throw new InvalidDataException("安装包文件超出了目标目录。");
        EnsureNoReparsePoints(rootPath, path);
        return path;
    }

    internal static bool IsChildPath(string root, string path)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath : rootPath + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static bool IsReparsePoint(string path) =>
        Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal static void EnsureNoReparsePoints(string root, string path)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (IsReparsePoint(current))
            throw new IOException("安装目录不能是目录联接或符号链接。");
        foreach (var component in Path.GetRelativePath(current, path)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (IsReparsePoint(current))
                throw new IOException("安装路径经过了目录联接或符号链接。");
        }
    }

    // Use the native junction API rather than cmd.exe so paths containing '%' and '&' stay literal.
    internal static void CreateJunction(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (Exists(linkPath)) throw new IOException("目录联接位置已存在。");
        var target = Path.GetFullPath(targetPath);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var display = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + 2 + display.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes(checked((ushort)(buffer.Length - 8))).CopyTo(buffer, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(buffer, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(buffer, 12);
        BitConverter.GetBytes(checked((ushort)display.Length)).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16);
        display.CopyTo(buffer, 18 + substitute.Length);

        Directory.CreateDirectory(linkPath);
        try
        {
            using var handle = CreateFile(linkPath, 0x40000000,
                FileShare.Read | FileShare.Write | FileShare.Delete, IntPtr.Zero,
                FileMode.Open, 0x02000000 | 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new IOException("无法打开目录联接位置。", new Win32Exception(Marshal.GetLastWin32Error()));
            if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException("创建目录联接失败。", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        catch
        {
            if (Directory.Exists(linkPath) && !IsReparsePoint(linkPath)) Directory.Delete(linkPath);
            throw;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
        FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
        byte[] input, int inputSize, IntPtr output, int outputSize,
        out uint bytesReturned, IntPtr overlapped);
}
