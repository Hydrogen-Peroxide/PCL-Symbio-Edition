using PCL.Core.Utils.Hash;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.IO.Storage.Cache;

public class FileCacheStorage : IDisposable
{
    private readonly HashStorage _hashStorage;
    private readonly ConcurrentDictionary<string, int> _refCounts = [];

    public FileCacheStorage(string cacheRoot, bool enableCompression = true)
    {
        Directory.CreateDirectory(cacheRoot);

        _hashStorage = new HashStorage(
            cacheRoot,
            SHA256Provider.Instance,
            compressObjects: enableCompression,
            correctMisplacedFile: false,
            prefixLength: 2);
    }

    public async Task<string> StoreAsync(Stream source, string? knownHash = null) =>
        (await StoreWithLengthAsync(source, knownHash).ConfigureAwait(false)).Hash;

    internal async Task<(string Hash, long Length)> StoreWithLengthAsync(Stream source, string? knownHash = null)
    {
        var stored = await _hashStorage.PutWithLengthAsync(source, knownHash).ConfigureAwait(false);
        _refCounts.AddOrUpdate(stored.Hash, 1, (_, count) => count + 1);
        return stored;
    }

    public Stream? Retrieve(string hash) => _hashStorage.Get(hash);

    public string? GetFilePath(string hash) => _hashStorage.GetExistingPath(hash);

    public bool Exists(string hash) => _hashStorage.Exists(hash);

    public async Task<bool> ReleaseAsync(string hash)
    {
        var spin = new SpinWait();
        while (true)
        {
            if (!_refCounts.TryGetValue(hash, out var count))
            {
                return false;
            }

            switch (TryRelease(hash, count))
            {
                case ReleaseResult.Removed:
                    return await _hashStorage.DeleteAsync(hash).ConfigureAwait(false);
                case ReleaseResult.Decremented:
                    return true;
            }

            if (spin.NextSpinWillYield)
                await Task.Yield();
            else
                spin.SpinOnce();
        }
    }

    private enum ReleaseResult { Retry, Decremented, Removed }

    private ReleaseResult TryRelease(string hash, int expected) =>
        expected > 1
            ? (_refCounts.TryUpdate(hash, expected - 1, expected) ? ReleaseResult.Decremented : ReleaseResult.Retry)
            : (_refCounts.TryRemove(new KeyValuePair<string, int>(hash, expected)) ? ReleaseResult.Removed : ReleaseResult.Retry);

    public Task<bool> ForceDeleteAsync(string hash)
    {
        _refCounts.TryRemove(hash, out _);
        return _hashStorage.DeleteAsync(hash);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _refCounts.Clear();
    }
}
