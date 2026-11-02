using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Configuration.Storage;
using PCL.Core.UI;

namespace PCL.Core.Test.App.Configuration.Storage;

[TestClass]
[DoNotParallelize]
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
    public async Task Stop_WhenFinalSaveFails_ShouldNotWaitForNotification()
    {
        using var releaseNotification = new ManualResetEventSlim();
        var notificationCount = 0;
        MsgBoxHandler handler = (string message, string caption, ICollection<MsgBoxButtonInfo> buttons,
            MsgBoxTheme theme, bool block, ref int result) =>
        {
            Interlocked.Increment(ref notificationCount);
            releaseNotification.Wait();
        };
        MsgBoxWrapper.OnShow += handler;
        var storage = new FileConfigStorage(new FailingFileProvider());
        var stopping = Task.Run(storage.Stop);
        try
        {
            Assert.AreSame(stopping, await Task.WhenAny(stopping, Task.Delay(2000)),
                "Final save errors must not wait for a dialog on the closing window.");
            await stopping;
            Assert.AreEqual(0, Volatile.Read(ref notificationCount), "Stop should only log save failures.");
        }
        finally
        {
            releaseNotification.Set();
            MsgBoxWrapper.OnShow -= handler;
            await Task.WhenAny(stopping, Task.Delay(2000));
        }
    }

    [TestMethod]
    public async Task Stop_ShouldFinishWhileAnEarlierSaveNotificationIsBlocked()
    {
        using var notificationEntered = new ManualResetEventSlim();
        using var releaseNotification = new ManualResetEventSlim();
        using var notificationFinished = new ManualResetEventSlim();
        MsgBoxHandler handler = (string message, string caption, ICollection<MsgBoxButtonInfo> buttons,
            MsgBoxTheme theme, bool block, ref int result) =>
        {
            notificationEntered.Set();
            releaseNotification.Wait();
            notificationFinished.Set();
        };
        MsgBoxWrapper.OnShow += handler;
        var storage = new FileConfigStorage(new FailingFileProvider());
        Task? stopping = null;
        try
        {
            storage.SetValue("JvmArgs", "-Xmx2G");
            Assert.IsTrue(notificationEntered.Wait(3000), "The running-save notification was not exercised.");
            stopping = Task.Run(storage.Stop);
            Assert.AreSame(stopping, await Task.WhenAny(stopping, Task.Delay(2000)),
                "A pending notification must not own the write worker or block Stop.");
            await stopping;
        }
        finally
        {
            releaseNotification.Set();
            MsgBoxWrapper.OnShow -= handler;
            if (notificationEntered.IsSet) notificationFinished.Wait(2000);
            stopping ??= Task.Run(storage.Stop);
            await Task.WhenAny(stopping, Task.Delay(2000));
        }
    }

    [TestMethod]
    public async Task Stop_ShouldFinishWhenAnEarlierNotificationHandlerThrows()
    {
        using var notificationEntered = new ManualResetEventSlim();
        MsgBoxHandler handler = (string message, string caption, ICollection<MsgBoxButtonInfo> buttons,
            MsgBoxTheme theme, bool block, ref int result) =>
        {
            notificationEntered.Set();
            throw new InvalidOperationException("Simulated notification failure.");
        };
        MsgBoxWrapper.OnShow += handler;
        var storage = new FileConfigStorage(new FailingFileProvider());
        Task? stopping = null;
        try
        {
            storage.SetValue("JvmArgs", "-Xmx2G");
            Assert.IsTrue(notificationEntered.Wait(3000), "The throwing notification was not exercised.");
            stopping = Task.Run(storage.Stop);
            Assert.AreSame(stopping, await Task.WhenAny(stopping, Task.Delay(2000)),
                "A notification exception must not lose the write worker's completion state.");
            await stopping;
        }
        finally
        {
            MsgBoxWrapper.OnShow -= handler;
            stopping ??= Task.Run(storage.Stop);
            await Task.WhenAny(stopping, Task.Delay(2000));
        }
    }

    [TestMethod]
    public async Task Stop_ShouldAllowConcurrentAndRepeatedCalls()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider);
        storage.SetValue("JvmArgs", "-Xmx2G");

        await Task.WhenAll(Task.Run(storage.Stop), Task.Run(storage.Stop))
            .WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Run(storage.Stop).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual("-Xmx2G", provider.Get<string>("JvmArgs"));
    }

    [TestMethod]
    public void Stop_ShouldDrainPendingWritesAndKeepLatestValues()
    {
        var provider = new TestFileProvider();
        var storage = new FileConfigStorage(provider);
        for (var i = 0; i < 200; i++)
        {
            storage.SetValue($"setting-{i}", i);
            storage.SetValue("latest", i);
        }

        storage.Stop();

        for (var i = 0; i < 200; i++) Assert.AreEqual(i, provider.Get<int>($"setting-{i}"));
        Assert.AreEqual(199, provider.Get<int>("latest"));
    }

    private sealed class FailingFileProvider : IKeyValueFileProvider
    {
        public string FilePath => "test://unwritable-config";
        public T Get<T>(string key) => default!;
        public void Set<T>(string key, T value) { }
        public bool Exists(string key) => false;
        public void Remove(string key) { }
        public void Sync() => throw new IOException("Simulated configuration save failure.");
    }

    private sealed class TestFileProvider : IKeyValueFileProvider
    {
        private readonly Dictionary<string, object?> _values = [];

        public string FilePath => "test://config";

        public T Get<T>(string key) => (T)_values[key]!;

        public void Set<T>(string key, T value) => _values[key] = value;

        public bool Exists(string key) => _values.ContainsKey(key);

        public void Remove(string key) => _values.Remove(key);

        public void Sync() { }
    }
}
