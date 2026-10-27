using System.Diagnostics.CodeAnalysis;

namespace PCL
{
    public static partial class ModBase
    {
        public const string versionBaseName = "1.0.0";
        public const int versionCode = 10000;
        public enum LogLevel { Normal, Hint, Developer }
        public static void Log(string message, LogLevel level = LogLevel.Normal, string? userSummary = null) { }
        public static void Log(Exception error, string message, LogLevel level = LogLevel.Normal, string? userSummary = null) { }
    }
    public static class ModLoader
    {
        public class LoaderBase;
        public sealed class LoaderTask<TInput, TOutput>(string name, Action<LoaderTask<TInput, TOutput>> action) : LoaderBase
        {
            public TOutput output = default!;
            public void Run() => action(this);
        }
    }
}

namespace PCL.Network
{
    public sealed class RequestParam { public int Retries; public int Timeout; public bool UseBrowserUserAgent; }
    public static class Requester
    {
        public static string FetchString(string url, RequestParam options) => throw new InvalidOperationException("Real network is forbidden in this regression host.");
    }
    public sealed class DownloadFile(string[] urls, string output) { public string[] Urls { get; } = urls; }
}
namespace PCL.Network.Loaders
{
    public sealed class LoaderDownload(string name, List<PCL.Network.DownloadFile> files) : PCL.ModLoader.LoaderBase;
}
namespace PCL.Core.App { }
namespace PCL.Core.App.Localization
{
    public static class Lang { public static string Text(string key, params object[] args) => key; }
}
namespace PCL.Core.Logging
{
    public static class LogWrapper
    {
        public static void Trace(string category, string message) { }
        public static void Error(Exception error, string category, string message) { }
        public static void Warn(Exception error, string category, string message) { }
        public static void Warn(string category, string message) { }
    }
}
namespace PCL.Core.UI
{
    public enum MsgBoxTheme { Error }
    public static class MsgBoxWrapper
    {
        public static event Action<bool>? OnShow;
        public static void Show(string message, string title, MsgBoxTheme theme, bool block = true) => OnShow?.Invoke(block);
    }
    public static class ExceptionDetails { public static string Compose(string summary, Exception error) => summary; }
}
namespace PCL.Core.App.Configuration.Storage
{
    public enum StorageAction { Get, Exists, Set, Delete }
    // Only lifecycle/access plumbing is stubbed; the channel, worker and timer are actual launcher source.
    public abstract class ConfigStorage
    {
        protected abstract bool OnAccess<TKey, TValue>(StorageAction action, ref TKey key,
            [NotNullWhen(true)] ref TValue value, object? argument);
        protected virtual void OnStop() { }
        public void Stop() => OnStop();
        public void SetValue<T>(string key, T value) => OnAccess(StorageAction.Set, ref key, ref value, null);
        public void Delete(string key) { object? value = null; OnAccess(StorageAction.Delete, ref key, ref value, null); }
    }
}
