using System;
using System.Collections.Concurrent;
using LiteDB;
using PCL.Core.App.IoC;

namespace PCL.Core.App.Database;

[LifecycleService(LifecycleState.Loading)]
public class DatabaseService() : GeneralService("database", "数据库管理")
{
    // 注意这里存的是 IDisposable 而不是 LiteDatabase：
    // 程序集是单文件发布、按需加载的，若 Stop() 里直接引用 LiteDatabase 类型，
    // 从未打开过数据库的进程在关闭时会去加载 LiteDB 并抛出 FileNotFoundException。
    private static readonly ConcurrentDictionary<string, IDisposable> _Instances = new();

    /// <inheritdoc />
    public override void Stop()
    {
        foreach (var instance in _Instances.Values)
        {
            instance.Dispose();
        }

        _Instances.Clear();
    }

    /// <summary>
    /// Get the database connenction from specified connection path.<br/>
    /// If not exists, a new connection will be created and cached.
    /// </summary>
    /// <returns>Getted connenction instance.</returns>
    /// <exception cref="ArgumentException">Throw if connection path is invalid.</exception>
    public static LiteDatabase GetConnection(string connectionPath)
    {
        if (string.IsNullOrWhiteSpace(connectionPath))
        {
            throw new ArgumentException("Connection string canot be null or whitespace.", nameof(connectionPath));
        }

        return (LiteDatabase)_Instances.GetOrAdd(connectionPath, cp => new LiteDatabase(cp));
    }
}