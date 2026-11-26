using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Multiplayer;

/// <summary>用户自己维护的基岩版服务器条目。</summary>
public sealed record BedrockServer(string Name, string Host, int Port)
{
    public string Address => $"{Host}:{Port}";
}

/// <summary>
///     基岩版自定义服务器列表（启动器侧书签，不写基岩版自己的 servers 文件）。
///     落盘：<c>%AppData%\PCLSE\bedrock-servers.json</c>。
/// </summary>
public static class BedrockServerList
{
    private static readonly object Sync = new();
    private static List<BedrockServer>? _cache;

    private static string? _filePath;

    /// <summary>存储路径。默认 %AppData%\PCLSE\bedrock-servers.json，可覆盖（便携版 / 测试）。</summary>
    public static string FilePath
    {
        get => _filePath ?? Path.Combine(Paths.SharedData, "bedrock-servers.json");
        set => _filePath = value;
    }

    public static IReadOnlyList<BedrockServer> All
    {
        get
        {
            lock (Sync) return Load().ToArray();
        }
    }

    public static void Add(BedrockServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(server.Host)) throw new ArgumentException("服务器地址不能为空", nameof(server));
        lock (Sync)
        {
            var list = Load();
            if (list.Any(item => string.Equals(item.Address, server.Address, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("该地址已经在列表里了");
            list.Add(server with { Name = string.IsNullOrWhiteSpace(server.Name) ? server.Address : server.Name.Trim() });
            Save(list);
        }
    }

    public static void Remove(string address)
    {
        lock (Sync)
        {
            var list = Load();
            if (list.RemoveAll(item => string.Equals(item.Address, address, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(list);
        }
    }

    public static void Update(string address, BedrockServer updated)
    {
        lock (Sync)
        {
            var list = Load();
            var index = list.FindIndex(item => string.Equals(item.Address, address, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException("要修改的条目不存在");
            list[index] = updated;
            Save(list);
        }
    }

    /// <summary>解析 "名称 1.2.3.4:19132" / "1.2.3.4:19132" / "域名" 形式的输入。</summary>
    public static BedrockServer Parse(string input, int defaultPort = 19132)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("输入不能为空", nameof(input));
        var text = input.Trim();
        string name = "", address = text;
        var space = text.LastIndexOf(' ');
        if (space > 0)
        {
            name = text[..space].Trim();
            address = text[(space + 1)..].Trim();
        }

        var host = address;
        var port = defaultPort;
        var colon = address.LastIndexOf(':');
        if (colon > 0 && colon < address.Length - 1 && !address.Contains(']'))
        {
            host = address[..colon];
            if (!int.TryParse(address[(colon + 1)..], out port) || port is < 1 or > 65535)
                throw new ArgumentException("端口不合法");
        }

        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("服务器地址不能为空");
        return new BedrockServer(string.IsNullOrWhiteSpace(name) ? $"{host}:{port}" : name, host, port);
    }

    private static List<BedrockServer> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<BedrockServer>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    private static void Save(List<BedrockServer> list)
    {
        _cache = list;
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
