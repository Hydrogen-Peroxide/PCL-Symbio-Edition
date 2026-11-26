using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Provision;

/// <summary>
///     通用分片下载器：HTTP Range 断点续传 + SHA256 校验 + 进度回调。
///     并发由静态 <see cref="SemaphoreSlim" /> 统一限制（默认 4），避免各业务各 new 一把 Task 堆死线程池。
/// </summary>
public sealed class DownloadService : IDisposable
{
    /// <summary>全局并发闸门：所有下载共享，改这里等于改全局策略。</summary>
    private static readonly SemaphoreSlim Concurrency = new(4, 4);

    private readonly HttpClient _http;

    public DownloadService(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    /// <summary>
    ///     下载到 <paramref name="destination" />。
    ///     <paramref name="expectedSha256" /> 为空时跳过校验（仅用于上游不提供摘要的场景，调用方需自行保证来源可信）。
    /// </summary>
    public async Task DownloadAsync(Uri uri, string destination, string? expectedSha256 = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("目标路径不能为空", nameof(destination));

        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var partPath = destination + ".part";
            await DownloadRangeAsync(uri, partPath, progress, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual = await ComputeSha256Async(partPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partPath);
                    throw new InvalidDataException($"SHA256 校验失败（期望 {expectedSha256}，实际 {actual}）");
                }
            }

            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partPath, destination);
            progress?.Report(1d);
        }
        finally
        {
            Concurrency.Release();
        }
    }

    /// <summary>
    ///     单流 + Range 续传。分片并发留给调用方按需扩展；当前场景（工具 jar / zip）单流已够快，
    ///     且更容易保证断点语义正确。
    /// </summary>
    private async Task DownloadRangeAsync(Uri uri, string partPath, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        long existing = 0;
        if (File.Exists(partPath)) existing = new FileInfo(partPath).Length;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // 服务端不支持 Range 时只能从头再来
        if (existing > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            existing = 0;
            TryDelete(partPath);
        }

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength is { } length ? length + existing : (long?)null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partPath, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = existing;
        var lastReport = 0L;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            // 每 256 KB 报一次，避免进度回调本身成为 UI 线程负担
            if (total is > 0 && received - lastReport >= 256 * 1024)
            {
                lastReport = received;
                progress?.Report(Math.Clamp((double)received / total.Value, 0, 1));
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }

    public void Dispose() => _http.Dispose();
}
