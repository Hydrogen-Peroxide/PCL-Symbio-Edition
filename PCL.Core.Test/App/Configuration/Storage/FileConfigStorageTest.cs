using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Configuration.Storage;
using PCL.Core.UI;

namespace PCL.Core.Test.App.Configuration.Storage;

[TestClass]
public class FileConfigStorageTest
{
    [TestMethod]
    public void Stop_ShouldPersistQueuedWrite()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider);

        storage.SetValue("JvmArgs", "-Xmx2G");
        storage.Stop();

        Assert.AreEqual("-Xmx2G", provider.Get<string>("JvmArgs"));
    }

    [TestMethod]
    public void IdleWrite_ShouldPersistWithoutAnotherWriteOrStop()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider, System.TimeSpan.FromMilliseconds(80));
        try
        {
            storage.SetValue("value", 1);
            Assert.IsTrue(SpinWait.SpinUntil(() => provider.Persisted("value", 1), 2000));
            storage.SetValue("value", 2);
            Assert.IsTrue(SpinWait.SpinUntil(() => provider.Persisted("value", 2), 2000));
        }
        finally { storage.Stop(); }
    }

    [TestMethod]
    public void ContinuousWrites_ShouldStillSyncPeriodically()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider, System.TimeSpan.FromMilliseconds(60));
        using var cancel = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            var value = 0;
            while (!cancel.IsCancellationRequested)
            {
                storage.SetValue("value", ++value);
                Thread.Sleep(1);
            }
        });
        try { Assert.IsTrue(SpinWait.SpinUntil(() => provider.SyncCount >= 3, 2000)); }
        finally { cancel.Cancel(); writer.GetAwaiter().GetResult(); storage.Stop(); }
    }

    [TestMethod]
    public void Stop_ShouldDrainAllKeysAndLatestOperation_AndBeIdempotent()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider);
        for (var i = 0; i < 1000; i++) storage.SetValue("key" + i, i);
        storage.SetValue("same", 1);
        storage.SetValue("same", 2);
        storage.Delete("same");
        storage.SetValue("last", 5);
        storage.Stop();
        storage.Stop();
        Assert.IsTrue(Enumerable.Range(0, 1000).All(i => provider.Persisted("key" + i, i)));
        Assert.IsFalse(provider.Exists("same"));
        Assert.IsTrue(provider.Persisted("last", 5));
    }

    [TestMethod]
    public void SaveFailure_BlockedNotification_ShouldNotBlockStop()
    {
        var provider = new TestFileProvider { ThrowOnSync = true };
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var requestedBlock = true;
        MsgBoxHandler handler = (string message, string caption, System.Collections.Generic.ICollection<MsgBoxButtonInfo> buttons,
            MsgBoxTheme theme, bool block, ref int result) =>
        {
            requestedBlock = block;
            entered.Set();
            release.Wait(System.TimeSpan.FromSeconds(5));
        };
        MsgBoxWrapper.OnShow += handler;
        var storage = new FileConfigStorage(provider, System.TimeSpan.FromMilliseconds(60));
        try
        {
            storage.SetValue("value", 1);
            Assert.IsTrue(entered.Wait(2000));
            Assert.IsFalse(requestedBlock);
            var stop = Task.Run(storage.Stop);
            Assert.IsTrue(stop.Wait(1000), "Stop must not await UI response.");
            stop.GetAwaiter().GetResult();
        }
        finally { release.Set(); MsgBoxWrapper.OnShow -= handler; storage.Stop(); }
    }

    [TestMethod]
    public void SaveFailure_ThrowingNotification_ShouldNotKillWorkerOrStop()
    {
        var provider = new TestFileProvider { ThrowOnSync = true };
        using var notified = new ManualResetEventSlim();
        MsgBoxHandler handler = (string message, string caption, System.Collections.Generic.ICollection<MsgBoxButtonInfo> buttons,
            MsgBoxTheme theme, bool block, ref int result) =>
        {
            notified.Set();
            throw new System.InvalidOperationException("Controlled notification failure.");
        };
        MsgBoxWrapper.OnShow += handler;
        var storage = new FileConfigStorage(provider, System.TimeSpan.FromMilliseconds(60));
        try
        {
            storage.SetValue("value", 1);
            Assert.IsTrue(notified.Wait(2000));
            Assert.IsTrue(SpinWait.SpinUntil(() => provider.SyncCount >= 2, 2000), "Worker must retry after notification failure.");
            var stop = Task.Run(storage.Stop);
            Assert.IsTrue(stop.Wait(1000));
            stop.GetAwaiter().GetResult();
        }
        finally { MsgBoxWrapper.OnShow -= handler; storage.Stop(); }
    }

    private sealed class TestFileProvider : IKeyValueFileProvider
    {
        private readonly ConcurrentDictionary<string, object?> _values = new();
        private ConcurrentDictionary<string, object?> _persisted = new();
        private int _syncCount;
        public int SyncCount => Volatile.Read(ref _syncCount);
        public bool ThrowOnSync { get; init; }
        public bool Persisted(string key, object value) => _persisted.TryGetValue(key, out var found) && Equals(found, value);

        public string FilePath => "test://config";

        public T Get<T>(string key) => (T)_values[key]!;

        public void Set<T>(string key, T value) => _values[key] = value;

        public bool Exists(string key) => _values.ContainsKey(key);

        public void Remove(string key) => _values.TryRemove(key, out _);

        public void Sync()
        {
            Interlocked.Increment(ref _syncCount);
            if (ThrowOnSync) throw new System.IO.IOException("Controlled save failure.");
            _persisted = new ConcurrentDictionary<string, object?>(_values);
        }
    }
}
