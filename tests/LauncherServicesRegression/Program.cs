using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PCL;
using PCL.Core.App;
using PCL.Core.App.Configuration.Storage;
using PCL.Core.Utils;
using PCL.Network;

if (args.Length > 0 && args[0] == "--child")
{
    switch (args[1])
    {
        case "exit7": return 7;
        case "sleep": Console.WriteLine(Environment.ProcessId); Console.Out.Flush(); Thread.Sleep(10000); return 0;
        case "short-sleep": Console.WriteLine("child-completed"); Thread.Sleep(100); return 0;
        case "spawn-and-exit":
        case "spawn-short-and-exit":
            var executable = Environment.ProcessPath!;
            var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? "\"" + typeof(ModBase).Assembly.Location + "\" " : "";
            using (var child = Process.Start(new ProcessStartInfo(executable,
                       prefix + "--child " + (args[1] == "spawn-and-exit" ? "sleep" : "short-sleep"))
                   { UseShellExecute = false, CreateNoWindow = true })!)
            {
                Console.WriteLine("child:" + child.Id); Console.Out.Flush();
                if (args[1] == "spawn-short-and-exit")
                {
                    child.WaitForExit();
                    Console.WriteLine("child-exit:" + child.ExitCode);
                }
            }
            return 0;
        case "flood":
            var outTask = Task.Run(() => Console.Out.Write(new string('o', 300000)));
            var errTask = Task.Run(() => Console.Error.Write(new string('e', 300000)));
            Task.WaitAll(outTask, errTask); return 0;
        case "warning": Console.Error.Write("ordinary diagnostic"); return 0;
    }
}

var passed = 0;
var failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
TException Reject<TException>(Action action) where TException : Exception
{
    try { action(); } catch (TException ex) { return ex; }
    throw new Exception("Expected " + typeof(TException).Name);
}
var childFile = Environment.ProcessPath!;
var childPrefix = Path.GetFileNameWithoutExtension(childFile).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
    ? "\"" + typeof(ModBase).Assembly.Location + "\" " : "";
ModBase.ProcessExecutionResult RunChild(string mode, int timeout = 10000) =>
    ModBase.ShellAndGetResult(childFile, childPrefix + "--child " + mode, timeout);

Check("empty stdout and nonzero exit are failed", () =>
{
    var result = RunChild("exit7");
    Require(result.ExitCode == 7 && !result.TimedOut && !result.Succeeded && result.Output == "", "Exit 7 was reported as success.");
    var error = Reject<ModBase.ProcessExecutionException>(() =>
        ModBase.ShellAndGetOutputChecked(childFile, childPrefix + "--child exit7"));
    Require(error.Result.ExitCode == 7, "Strict exception lost exit code.");
});
Check("timeout kills created process and remains failed", () =>
{
    var watch = Stopwatch.StartNew();
    var result = RunChild("sleep", 500);
    Require(result.TimedOut && !result.Succeeded && watch.Elapsed < TimeSpan.FromSeconds(6), "Timeout was not bounded.");
    if (int.TryParse(result.StandardOutput.Trim(), out var pid))
    {
        try { using var process = Process.GetProcessById(pid); Require(process.HasExited, "Timed out child is still running."); }
        catch (ArgumentException) { }
    }
    var error = Reject<ModBase.ProcessExecutionException>(() =>
        ModBase.ShellAndGetOutputChecked(childFile, childPrefix + "--child sleep", 100));
    Require(error.Result.TimedOut, "Strict exception lost timeout state.");
});
Check("stdout and stderr drain concurrently", () =>
{
    var result = RunChild("flood");
    Require(result.Succeeded && result.StandardOutput.Length == 300000 && result.StandardError.Length == 300000,
        "Output was lost or a full pipe blocked the process.");
});
Check("exited parent cannot leave a pipe-holding child after timeout", () =>
{
    var result = RunChild("spawn-and-exit", 1000);
    var marker = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(line => line.StartsWith("child:", StringComparison.Ordinal));
    Require(marker is not null && int.TryParse(marker[6..], out _), "Fixture did not start its child.");
    var childId = int.Parse(marker![6..]);
    var childAlive = false;
    try
    {
        using var child = Process.GetProcessById(childId);
        childAlive = !child.HasExited;
        // A failed regression must still clean up only the child created by this fixture.
        if (childAlive) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); }
    }
    catch (ArgumentException) { }
    Require(result.TimedOut && result.ExitCode == 0 && !childAlive, "Exited root left its child alive after API returned.");
});
Check("parent and short-lived child complete normally", () =>
{
    var result = RunChild("spawn-short-and-exit");
    Require(result.Succeeded && result.StandardOutput.Contains("child-exit:0"),
        $"Normal child fixture failed: exit={result.ExitCode}, timeout={result.TimedOut}, stdout={result.StandardOutput}, stderr={result.StandardError}");
});
Check("stderr on exit zero is diagnostic", () =>
{
    Require(RunChild("warning").Succeeded, "Nonempty stderr was guessed to be failure.");
});
Check("start failure propagates", () =>
{
    Reject<System.ComponentModel.Win32Exception>(() => ModBase.ShellAndGetResult("PclRegressionMissingExecutable_581e.exe"));
});

foreach (var status in new uint[] { 0, 0x102, 0xffffffff })
{
    Check("injection argument lifetime wait=" + status, () =>
    {
        var closed = 0; var freed = 0; var exitReads = 0;
        Action run = () => ModBedrockMods.CompleteInjectionThread(new IntPtr(1), _ => status,
            _ => { exitReads++; return 42; }, _ => closed++, () => freed++);
        if (status == 0) run();
        else if (status == 0x102) Reject<TimeoutException>(run);
        else Reject<InvalidOperationException>(run);
        Require(closed == 1 && freed == (status == 0 ? 1 : 0) && exitReads == (status == 0 ? 1 : 0),
            "An active thread lost its argument, or a thread handle leaked.");
    });
}
Check("completed injection releases argument if exit query fails", () =>
{
    var freed = false; var closed = false;
    Reject<InvalidOperationException>(() => ModBedrockMods.CompleteInjectionThread(new IntPtr(1), _ => 0,
        _ => throw new InvalidOperationException(), _ => closed = true, () => freed = true));
    Require(freed && closed, "Completed thread resources were leaked.");
});
Check("injection abort confirms process termination", () =>
{
    var terminated = false; var waits = new List<uint>();
    ModBedrockMods.AbortCreatedProcess(new IntPtr(1), (_, timeout) =>
    {
        waits.Add(timeout); return timeout == 0 ? 0x102u : 0u;
    }, _ => terminated = true);
    Require(terminated && waits.SequenceEqual(new uint[] { 0, 5000 }), "Abort did not confirm created process exit.");
});
Check("injection abort does not terminate an already exited process", () =>
{
    ModBedrockMods.AbortCreatedProcess(new IntPtr(1), (_, _) => 0,
        _ => throw new Exception("An already exited process was terminated."));
});
Check("unconfirmed injection process termination aborts fallback", () =>
{
    Reject<InvalidOperationException>(() => ModBedrockMods.AbortCreatedProcess(new IntPtr(1), (_, _) => 0x102, _ => { }));
});

Check("idle config flush needs no new message", () =>
{
    var file = new MemoryFile();
    var storage = new FileConfigStorage(file, TimeSpan.FromMilliseconds(80));
    try
    {
        storage.SetValue("test", 1);
        Require(SpinWait.SpinUntil(() => file.Persisted("test", 1), 2000), "First sync never completed.");
        storage.SetValue("test", 2);
        Require(SpinWait.SpinUntil(() => file.Persisted("test", 2), 2000), "Second idle write was stranded.");
    }
    finally { storage.Stop(); }
});
Check("continuous config writes cannot starve flush", () =>
{
    var file = new MemoryFile();
    var storage = new FileConfigStorage(file, TimeSpan.FromMilliseconds(60));
    using var stop = new CancellationTokenSource();
    var writer = Task.Run(() => { var value = 0; while (!stop.IsCancellationRequested) { storage.SetValue("test", ++value); Thread.Sleep(1); } });
    try { Require(SpinWait.SpinUntil(() => file.SyncCount >= 3, 2000), "Continuous writes deferred every sync."); }
    finally { stop.Cancel(); writer.GetAwaiter().GetResult(); storage.Stop(); }
});
Check("Stop drains all queued keys and latest set/delete", () =>
{
    var file = new MemoryFile();
    var storage = new FileConfigStorage(file, TimeSpan.FromSeconds(10));
    for (var i = 0; i < 1000; i++) storage.SetValue("key" + i, i);
    storage.SetValue("same", 1); storage.SetValue("same", 2); storage.Delete("same"); storage.SetValue("last", 5);
    storage.Stop(); storage.Stop();
    Require(Enumerable.Range(0, 1000).All(i => file.Persisted("key" + i, i)) && !file.Exists("same") && file.Persisted("last", 5),
        "Stop lost accepted operations.");
});
Check("failed config sync and blocked notification cannot block Stop", () =>
{
    var file = new MemoryFile { ThrowOnSync = true };
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var requestedBlock = true;
    void Notify(bool block) { requestedBlock = block; entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
    PCL.Core.UI.MsgBoxWrapper.OnShow += Notify;
    var storage = new FileConfigStorage(file, TimeSpan.FromMilliseconds(60));
    try
    {
        storage.SetValue("test", 1);
        Require(entered.Wait(2000), "Controlled save error was not reported.");
        Require(!requestedBlock, "Save notification requested a blocking dialog.");
        var stop = Task.Run(storage.Stop);
        Require(stop.Wait(1000), "Stop waited for notification callback.");
        stop.GetAwaiter().GetResult();
    }
    finally { release.Set(); PCL.Core.UI.MsgBoxWrapper.OnShow -= Notify; storage.Stop(); }
});
Check("throwing config notification does not kill worker or Stop", () =>
{
    var file = new MemoryFile { ThrowOnSync = true };
    using var notified = new ManualResetEventSlim();
    void Notify(bool block) { notified.Set(); throw new InvalidOperationException("controlled notification failure"); }
    PCL.Core.UI.MsgBoxWrapper.OnShow += Notify;
    var storage = new FileConfigStorage(file, TimeSpan.FromMilliseconds(60));
    try
    {
        storage.SetValue("test", 1);
        Require(notified.Wait(2000), "Controlled notification was not invoked.");
        Require(SpinWait.SpinUntil(() => file.SyncCount >= 2, 2000), "Worker died instead of retrying save.");
        var stop = Task.Run(storage.Stop);
        Require(stop.Wait(1000), "Stop hung after notification failure.");
        stop.GetAwaiter().GetResult();
    }
    finally { PCL.Core.UI.MsgBoxWrapper.OnShow -= Notify; storage.Stop(); }
});

var hashA = new string('a', 64);
var hashB = new string('b', 64);
object Release(string tag, bool prerelease = false, bool draft = false, string arch = "x64", string? hash = null) => new
{
    tag_name = tag, prerelease, draft, body = tag + " notes",
    assets = new[] { new { name = "PCL_" + arch + ".exe", browser_download_url = "https://example.invalid/" + tag + "/" + arch + ".exe", digest = "sha256:" + (hash ?? hashA) } }
};
UpdatesGitHubModel Source(params object[] releases) => new("test", "fixture", "Mock", _ => JsonSerializer.Serialize(releases));
string DownloadUrl(UpdatesGitHubModel source, UpdateChannel channel, UpdateArch arch)
{
    var loader = (ModLoader.LoaderTask<int, List<DownloadFile>>)source.GetDownloadLoader(channel, arch, "temporary.exe")[0];
    loader.Run(); return loader.output[0].Urls[0];
}
Check("stable and beta select separate semantic releases", () =>
{
    var source = Source(Release("v2.0.0-beta.10", true, hash: hashB), Release("v1.5.0"),
        Release("v2.0.0-beta.2", true), Release("v8.0.0", draft: true));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.5.0", "Stable selected prerelease/draft.");
    var beta = source.GetLatestVersion(UpdateChannel.beta, UpdateArch.x64);
    Require(beta.VersionName == "2.0.0-beta.10" && beta.Sha256 == hashB && beta.Changelog == "v2.0.0-beta.10 notes", "Beta metadata mismatched.");
    Require(DownloadUrl(source, UpdateChannel.beta, UpdateArch.x64).Contains("v2.0.0-beta.10/"), "Beta URL mismatched.");
});
Check("beta upgrades to newer stable release", () =>
{
    var source = Source(Release("v2.0.0-beta.1", true), Release("v2.0.0"));
    Require(source.GetLatestVersion(UpdateChannel.beta, UpdateArch.x64).VersionName == "2.0.0", "Beta remained pinned to an older prerelease.");
});
Check("missing architecture never falls back", () =>
{
    foreach (var pair in new[] { ("x64", UpdateArch.arm64), ("arm64", UpdateArch.x64), ("untagged", UpdateArch.x64) })
    {
        var source = Source(Release("v2.0.0", arch: pair.Item1));
        Require(source.IsLatest(UpdateChannel.stable, pair.Item2, SemVer.Parse("1.0.0"), 10000), "Wrong architecture announced.");
        Require(source.GetLatestVersion(UpdateChannel.stable, pair.Item2).VersionName == ModBase.versionBaseName, "Wrong architecture exposed as update.");
        Reject<Exception>(() => source.GetDownloadLoader(UpdateChannel.stable, pair.Item2, "temporary.exe"));
    }
});
Check("failed refresh preserves each channel cache", () =>
{
    var fail = false;
    var json = JsonSerializer.Serialize(new[] { Release("v1.4.0"), Release("v2.0.0-beta.1", true) });
    var source = new UpdatesGitHubModel("test", "fixture", "Mock", _ => fail ? throw new IOException("fixture offline") : json);
    source.RefreshCache(); fail = true; source.RefreshCache();
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.4.0" &&
        source.GetLatestVersion(UpdateChannel.beta, UpdateArch.x64).VersionName == "2.0.0-beta.1", "Channel cache was mixed or erased.");
});
Check("download keeps version and hash snapshot across refresh", () =>
{
    var json = JsonSerializer.Serialize(new[] { Release("v2.0.0", hash: hashB) });
    var source = new UpdatesGitHubModel("test", "fixture", "Mock", _ => json);
    var versionA = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    json = JsonSerializer.Serialize(new[] { Release("v3.0.0") }); source.RefreshCache();
    var versionB = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    // A 读取 V2，B 在刷新后读取 V3，再分别创建/执行下载；不能依赖源上的最后选择字典。
    var loadersA = source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary-a.exe", versionA);
    var loadersB = source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary-b.exe", versionB);
    var loaderA = (ModLoader.LoaderTask<int, List<DownloadFile>>)loadersA[0]; loaderA.Run();
    var loaderB = (ModLoader.LoaderTask<int, List<DownloadFile>>)loadersB[0]; loaderB.Run();
    Require(versionA.Sha256 == hashB && loaderA.output[0].Urls[0].Contains("v2.0.0/") &&
        versionB.Sha256 == hashA && loaderB.output[0].Urls[0].Contains("v3.0.0/"), "Interleaved callers mixed a returned hash and another release URL.");
});
Check("wrapper forwards immutable version snapshot after another query", () =>
{
    var json = JsonSerializer.Serialize(new[] { Release("v2.0.0", hash: hashB) });
    var source = new UpdatesGitHubModel("test", "fixture", "Mock", _ => json);
    var wrapper = new UpdatesWrapperModel(new[] { source });
    var versionA = wrapper.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    json = JsonSerializer.Serialize(new[] { Release("v3.0.0") }); source.RefreshCache();
    wrapper.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    var loader = (ModLoader.LoaderTask<int, List<DownloadFile>>)wrapper.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary.exe", versionA)[0];
    loader.Run();
    Require(loader.output[0].Urls[0].Contains("v2.0.0/"), "Wrapper discarded the caller's snapshot.");
});
Check("snapshot rejects mutated metadata and wrong architecture", () =>
{
    var source = Source(Release("v2.0.0"));
    var version = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    Reject<InvalidOperationException>(() => source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.arm64, "temporary.exe", version));
    version.Sha256 = hashB;
    Reject<InvalidOperationException>(() => source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary.exe", version));
});
Check("legacy provider supports the new interface overload", () =>
{
    IUpdateSource source = new LegacyUpdateSource();
    var version = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    var loaders = source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary.exe", version);
    Require(loaders.Count == 0 && ((LegacyUpdateSource)source).DownloadCalls == 1, "Legacy provider compatibility was broken.");
});
Check("invalid digest does not announce downloadable update", () =>
{
    var source = Source(Release("v2.0.0", hash: "invalid"));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == ModBase.versionBaseName, "Invalid hash announced.");
    Reject<Exception>(() => source.GetDownloadLoader(UpdateChannel.stable, UpdateArch.x64, "temporary.exe"));
});
Check("prerelease tag is excluded from stable even if API flag is false", () =>
{
    var source = Source(Release("v2.0.0-beta.1"), Release("invalid"), Release("v1.3.0"));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.3.0", "Unstable tag entered stable cache.");
});
Check("release pagination includes older published stable", () =>
{
    var requests = new List<string>();
    var source = new UpdatesGitHubModel("test", "fixture", "Mock", url =>
    {
        requests.Add(url);
        return url.EndsWith("page=1", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(Enumerable.Range(0, 100).Select(i => Release("v2.0.0-beta." + i, true)))
            : JsonSerializer.Serialize(new[] { Release("v1.8.0") });
    });
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.8.0" && requests.Count == 2,
        "Stable release beyond the first page was missed.");
});
Check("successful empty refresh clears stale releases", () =>
{
    var json = JsonSerializer.Serialize(new[] { Release("v2.0.0") });
    var source = new UpdatesGitHubModel("test", "fixture", "Mock", _ => json);
    source.RefreshCache(); json = "[]"; source.RefreshCache();
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == ModBase.versionBaseName, "Deleted release remained latest.");
});

object CnbRelease(string tag, bool prerelease = false, bool draft = false, string[]? assets = null) => new
{
    tag_name = tag, prerelease, draft, body = tag + " cnb notes",
    assets = (assets ?? new[] { "PCL_Symbio_x64.exe" })
        .Select(n => (object)new
        {
            name = n,
            brower_download_url = "https://cnb.example.invalid/" + tag + "/" + n
        }).ToArray()
};
UpdatesCnbModel CnbSource(params object[] releases) =>
    new("test/cnb", "MockCNB", _ => JsonSerializer.Serialize(releases));
string AnyDownloadUrl(IUpdateSource source, UpdateChannel channel, UpdateArch arch)
{
    var loader = (ModLoader.LoaderTask<int, List<DownloadFile>>)source.GetDownloadLoader(channel, arch, "temporary.exe")[0];
    loader.Run(); return loader.output[0].Urls[0];
}
Check("CNB reads the exe attachment of a release", () =>
{
    var source = CnbSource(CnbRelease("v2.0.0"));
    var version = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    Require(version.VersionName == "2.0.0" && version.Source == "MockCNB" && version.Changelog == "v2.0.0 cnb notes",
        "CNB release metadata mismatched.");
    // CNB 附件没有 digest，此源在无校验文件时应留空而不是伪造哈希
    Require(version.Sha256 == "", "CNB source invented a checksum.");
    Require(AnyDownloadUrl(source, UpdateChannel.stable, UpdateArch.x64) ==
            "https://cnb.example.invalid/v2.0.0/PCL_Symbio_x64.exe", "CNB download URL mismatched.");
});
Check("CNB uses the published checksum file when present", () =>
{
    var sha = new string('c', 64);
    var json = JsonSerializer.Serialize(new[]
        { CnbRelease("v2.0.0", assets: new[] { "PCL_Symbio_x64.exe", "PCL_Symbio_x64.exe.sha256" }) });
    var source = new UpdatesCnbModel("test/cnb", "MockCNB", url =>
        url.EndsWith(".sha256", StringComparison.Ordinal) ? sha + "  PCL_Symbio_x64.exe" : json);
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).Sha256 == sha,
        "CNB checksum file was ignored.");
});
Check("CNB release without a usable exe falls over to GitHub", () =>
{
    // 只有源码包时不能谎报“已是最新”，必须抛出让 wrapper 回退
    var cnb = CnbSource(CnbRelease("v2.0.0", assets: new[] { "Source code.zip" }));
    Reject<InvalidDataException>(() => cnb.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64));
    Reject<InvalidDataException>(() => cnb.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("1.0.0"), 10000));
    var wrapper = new UpdatesWrapperModel(new IUpdateSource[] { cnb, Source(Release("v1.5.0")) });
    Require(wrapper.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.5.0",
        "Wrapper did not fail over from CNB to GitHub.");
    Require(wrapper.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("1.5.0"), 10500),
        "Wrapper lost GitHub's verdict while failing over.");
});
Check("CNB falls over when the attachment lacks the requested architecture", () =>
{
    var cnb = CnbSource(CnbRelease("v2.0.0", assets: new[] { "PCL_Symbio_arm64.exe" }));
    Reject<InvalidDataException>(() => cnb.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64));
    Require(cnb.GetLatestVersion(UpdateChannel.stable, UpdateArch.arm64).VersionName == "2.0.0",
        "CNB arm64 attachment was not recognised.");
});
Check("CNB keeps prereleases out of the stable channel", () =>
{
    var source = CnbSource(CnbRelease("v2.0.0-beta.3", true), CnbRelease("v1.9.0"),
        CnbRelease("v8.0.0", draft: true));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.9.0",
        "CNB prerelease or draft leaked into stable.");
    Require(source.GetLatestVersion(UpdateChannel.beta, UpdateArch.x64).VersionName == "2.0.0-beta.3",
        "CNB beta channel selection mismatched.");
});

bool IsManifestUrl(string url) => url.Contains("/-/git/raw/updates/", StringComparison.Ordinal);
UpdatesCnbModel CnbRouted(Func<string, string> fetch) => new("test/cnb", "MockCNB", fetch);
var manifestHash = new string('d', 64);
Check("CNB prefers the updates manifest over the releases API", () =>
{
    var manifest = JsonSerializer.Serialize(new
    {
        version = "2.0.0", sha256 = manifestHash, changelog = "manifest notes",
        downloads = new { x64 = "https://cnb.example.invalid/manifest/2.0.0-x64.exe" }
    });
    var api = JsonSerializer.Serialize(new[] { CnbRelease("v1.5.0") });
    var source = CnbRouted(url => IsManifestUrl(url) ? manifest : api);
    var version = source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64);
    Require(version.VersionName == "2.0.0" && version.Sha256 == manifestHash &&
            version.Changelog == "manifest notes", "Manifest metadata was ignored.");
    Require(AnyDownloadUrl(source, UpdateChannel.stable, UpdateArch.x64) ==
            "https://cnb.example.invalid/manifest/2.0.0-x64.exe", "Manifest download URL was ignored.");
    Require(source.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("2.0.0"), 20000),
        "Manifest version comparison is wrong.");
});
Check("CNB falls back to the releases API when the manifest is absent", () =>
{
    // 清单尚未发布时是 404 页面（HTML），必须安静退回 Releases API
    var source = CnbRouted(url => IsManifestUrl(url)
        ? "<!DOCTYPE html><html>404</html>"
        : JsonSerializer.Serialize(new[] { CnbRelease("v1.5.0") }));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.5.0",
        "Absent manifest did not fall back to the releases API.");
});
Check("CNB reads the beta manifest for the beta channel", () =>
{
    var stableManifest = JsonSerializer.Serialize(new
    {
        version = "1.9.0", downloads = new { x64 = "https://cnb.example.invalid/stable/1.9.0-x64.exe" }
    });
    var betaManifest = JsonSerializer.Serialize(new
    {
        version = "2.0.0-beta.1", downloads = new { x64 = "https://cnb.example.invalid/beta/2.0.0-beta.1-x64.exe" }
    });
    var source = CnbRouted(url => url.EndsWith("latest-beta.json", StringComparison.Ordinal)
        ? betaManifest
        : IsManifestUrl(url) ? stableManifest : "[]");
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.9.0",
        "Stable manifest was not used.");
    Require(source.GetLatestVersion(UpdateChannel.beta, UpdateArch.x64).VersionName == "2.0.0-beta.1",
        "Beta manifest was not used.");
});
Check("CNB falls back to the releases API when the manifest lacks the architecture", () =>
{
    var manifest = JsonSerializer.Serialize(new
    {
        version = "2.0.0", downloads = new { arm64 = "https://cnb.example.invalid/2.0.0-arm64.exe" }
    });
    var source = CnbRouted(url => IsManifestUrl(url)
        ? manifest
        : JsonSerializer.Serialize(new[] { CnbRelease("v1.5.0") }));
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.x64).VersionName == "1.5.0",
        "Manifest without x64 did not fall back to the releases API.");
    Require(source.GetLatestVersion(UpdateChannel.stable, UpdateArch.arm64).VersionName == "2.0.0",
        "Manifest arm64 entry was not used.");
});

Check("a failing source is queried only once per check", () =>
{
    var dead = new CountingSource("Dead", () => throw new IOException("offline"));
    var live = new CountingSource("Live", () => true);
    var wrapper = new UpdatesWrapperModel(new IUpdateSource[] { dead, live });
    Require(wrapper.IsLatest(UpdateChannel.stable, UpdateArch.x64, SemVer.Parse("1.0.0"), 10000),
        "Live source was not used after the dead one failed.");
    // 此前实现会在每轮循环里重试一次「缓存的版本源」，导致同一源被请求 2~3 次（日志中表现为反复 401）
    Require(dead.Calls == 1, "Dead source was queried " + dead.Calls + " times instead of once.");
    Require(live.Calls == 1, "Live source was queried " + live.Calls + " times instead of once.");
});
Check("a source without data is not adopted as the cached source", () =>
{
    var empty = new CountingSource("Empty", () => true) { RefreshResult = false };
    var live = new CountingSource("Live", () => true);
    var wrapper = new UpdatesWrapperModel(new IUpdateSource[] { empty, live });
    Require(wrapper.RefreshCache(), "RefreshCache reported failure although one source had data.");
    Require(wrapper.SourceName == "Live", "An empty source was cached as the version source: " + wrapper.SourceName);
});

Check("CNB request carries the token only when it is configured", () =>
{
    var previous = Secrets.CnbToken;
    try
    {
        Secrets.CnbToken = "";
        Require(UpdatesCnbModel.CreateRequestParam(true).Headers is null,
            "Authorization header was added without a token.");
        Require(UpdatesCnbModel.CreateRequestParam(true).Accept == "application/vnd.cnb.api+json",
            "API request lost its OpenAPI Accept header.");
        Require(UpdatesCnbModel.CreateRequestParam(false).Accept is null,
            "Manifest request should not force the OpenAPI Accept header.");

        Secrets.CnbToken = "test-token";
        Require(UpdatesCnbModel.CreateRequestParam(true).Headers?["Authorization"] == "Bearer test-token",
            "API request did not carry the token.");
        Require(UpdatesCnbModel.CreateRequestParam(false).Headers is null,
            "Manifest request must not carry the token, it is anonymously readable.");
    }
    finally
    {
        Secrets.CnbToken = previous;
    }
});

Console.WriteLine($"{passed}/{passed + failed} passed");
return failed == 0 ? 0 : 1;

sealed class MemoryFile : IKeyValueFileProvider
{
    private readonly ConcurrentDictionary<string, object?> _values = new();
    private ConcurrentDictionary<string, object?> _persisted = new();
    private int _syncCount;
    public int SyncCount => Volatile.Read(ref _syncCount);
    public bool ThrowOnSync { get; init; }
    public string FilePath => "test://config";
    public T Get<T>(string key) => (T)_values[key]!;
    public void Set<T>(string key, T value) => _values[key] = value;
    public bool Exists(string key) => _values.ContainsKey(key);
    public void Remove(string key) => _values.TryRemove(key, out _);
    public bool Persisted(string key, object value) => _persisted.TryGetValue(key, out var found) && Equals(value, found);
    public void Sync()
    {
        Interlocked.Increment(ref _syncCount);
        if (ThrowOnSync) throw new IOException("controlled save failure");
        _persisted = new ConcurrentDictionary<string, object?>(_values);
    }
}

sealed class LegacyUpdateSource : IUpdateSource
{
    public string SourceName { get; set; } = "Legacy";
    public int DownloadCalls { get; private set; }
    public bool IsAvailable() => true;
    public bool RefreshCache() => true;
    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch) => new() { VersionName = "1.0.0", Sha256 = "", Changelog = "", Source = SourceName };
    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode) => true;
    public VersionAnnouncementDataModel GetAnnouncementList() => new() { Content = [] };
    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output) { DownloadCalls++; return []; }
}

/// <summary>统计调用次数的替身源，用于验证回退时不会重复请求同一个源。</summary>
sealed class CountingSource(string name, Func<bool> isLatest) : IUpdateSource
{
    public int Calls { get; private set; }
    public bool RefreshResult { get; init; } = true;
    public string SourceName { get; set; } = name;
    public bool IsAvailable() => true;
    public bool RefreshCache() => RefreshResult;
    public VersionDataModel GetLatestVersion(UpdateChannel channel, UpdateArch arch) => throw new NotSupportedException();
    public bool IsLatest(UpdateChannel channel, UpdateArch arch, SemVer currentVersion, int currentVersionCode)
    {
        Calls++;
        return isLatest();
    }
    public VersionAnnouncementDataModel GetAnnouncementList() => new() { Content = [] };
    public List<ModLoader.LoaderBase> GetDownloadLoader(UpdateChannel channel, UpdateArch arch, string output) => throw new NotSupportedException();
}
