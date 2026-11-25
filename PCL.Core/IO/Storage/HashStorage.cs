using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using PCL.Core.Logging;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.Hash;

namespace PCL.Core.IO.Storage;

public class HashStorage(string folder, IHashProvider hashProvider, bool compressObjects = false, bool correctMisplacedFile = true, int prefixLength = 2)
{
    /// <summary>
    /// 保存文件到哈希存储库中
    /// </summary>
    /// <param name="fromPath">欲存储的文件位置</param>
    /// <param name="hash">欲存储的文件的哈希，请确保与哈希存储库指定的哈希计算方法所用算法一致</param>
    /// <returns>成功返回文件的哈希，失败返回 null</returns>
    /// <exception cref="ArgumentNullException">提供的参数不正确</exception>
    public async Task<string?> PutAsync(string fromPath, string? hash = null)
    {
        //参数检查
        ArgumentNullException.ThrowIfNull(fromPath);
        var filePath = Path.GetFullPath(fromPath);
        if (!File.Exists(filePath)) return null;
        //必要数据准备
        await using var originalFs = File.Open(fromPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await PutAsync(originalFs, hash).ConfigureAwait(false);
    }

    public async Task<string?> PutAsync(Stream input, string? hash = null) =>
        (await PutWithLengthAsync(input, hash).ConfigureAwait(false)).Hash;

    internal async Task<(string Hash, long Length)> PutWithLengthAsync(Stream input, string? hash = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (hash is not null && hash.Length != hashProvider.Length)
            throw new ArgumentException("Provide hash is not correct", nameof(hash));

        if (!input.CanSeek)
        {
            var bufferPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await using var buffer = new FileStream(bufferPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await input.CopyToAsync(buffer).ConfigureAwait(false);
            return await PutWithLengthAsync(buffer, hash).ConfigureAwait(false);
        }

        input.Position = 0;
        var length = input.Length;
        var fileHash = hash ?? (await hashProvider.ComputeHashAsync(input).ConfigureAwait(false)).ToHexString();
        var destPath = _GetDestPath(fileHash);
        if (correctMisplacedFile) _CorrectMisplacedFile(fileHash, destPath);
        if (_IsAvailable(destPath, fileHash)) return (fileHash, length);

        input.Position = 0;
        var directory = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(directory);
        var stagedPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = _GetSaveStream(stagedPath))
            {
                await input.CopyToAsync(destination).ConfigureAwait(false);
            }

            // Publish only a complete file. Another writer may already have stored the same hash.
            if (!_IsAvailable(destPath, fileHash))
            {
                try { File.Move(stagedPath, destPath, overwrite: File.Exists(destPath)); }
                catch (IOException) when (_IsAvailable(destPath, fileHash)) { }
            }
        }
        finally
        {
            File.Delete(stagedPath);
        }

        return (fileHash, length);
    }

    public Task<bool> DeleteAsync(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        var filePath = _GetDestPath(hash);
        if (!File.Exists(filePath) && correctMisplacedFile)
            filePath = _GetMisplacedFilePath(hash);

        if (!File.Exists(filePath)) return Task.FromResult(false);

        try
        {
            File.Delete(filePath);
        }
        catch (FileNotFoundException) { /* 忽略此错误 */ }
        catch (DirectoryNotFoundException ex)
        {
            LogWrapper.Error(ex, "HashStorage", $"Unexpected directory not found {filePath}");
            return Task.FromResult(false);
        }
        catch (IOException ex)
        {
            LogWrapper.Error(ex, "HashStorage", $"Failed to delete file {filePath}");
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogWrapper.Error(ex, "HashStorage", $"Access denied when deleting file {filePath}");
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public Stream? Get(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var destPath = _GetDestPath(hash);
        if (correctMisplacedFile) _CorrectMisplacedFile(hash, destPath);

        return _IsAvailable(destPath, hash) ? _GetReadStream(destPath) : null;
    }

    public bool Exists(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return GetExistingPath(hash) is not null ||
               (correctMisplacedFile && _IsAvailable(_GetMisplacedFilePath(hash), hash));
    }

    internal string? GetExistingPath(string hash)
    {
        var path = _GetDestPath(hash);
        return _IsAvailable(path, hash) ? path : null;
    }

    private bool _IsAvailable(string path, string hash)
    {
        var file = new FileInfo(path);
        // Earlier writes could leave a zero-byte file after hashing a nonempty stream.
        return file.Exists && (file.Length != 0 ||
            string.Equals(hash, hashProvider.ComputeHash(ReadOnlySpan<byte>.Empty).ToHexString(),
                StringComparison.OrdinalIgnoreCase));
    }

    private Stream _GetSaveStream(string destPath)
    {
        var fs = File.Open(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (compressObjects) return new DeflateStream(fs, CompressionMode.Compress);
        return fs;
    }

    private Stream _GetReadStream(string destPath)
    {
        var fs = File.Open(destPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (compressObjects) return new DeflateStream(fs, CompressionMode.Decompress);
        return fs;
    }

    private string _GetDestPath(string hash)
    {
        if (hash.Length < prefixLength)
            throw new ArgumentException($"Hash length({hash.Length}) is shorter than required prefix length({prefixLength})", nameof(hash));
        return Path.Combine(folder, hash[..prefixLength], hash);
    }

    private void _CorrectMisplacedFile(string hash, string correctPath)
    {
        var misplacedPath = _GetMisplacedFilePath(hash);
        if (File.Exists(correctPath) || !File.Exists(misplacedPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(correctPath)!);
        try
        {
            File.Move(misplacedPath, correctPath);
            LogWrapper.Info("HashStorage", $"Move misplaced file into correct folder: {hash}");
        }
        catch (IOException) when (File.Exists(correctPath)) { }
    }

    private string _GetMisplacedFilePath(string hash)
    {
        return Path.Combine(folder, hash);
    }

}
