using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Multiplayer;

/// <summary>本地好友（玩家名）。不依赖任何账号体系，纯启动器侧名单。</summary>
public sealed record BedrockFriend(string Name);

/// <summary>
///     基岩版好友名单 + 在线判定。
///     PaperConnect 是去中心化的（没有中心服务器保存上线状态），所以"在线"只可能来自
///     **当前房间里确实有这个人**（协议 c:player 返回的 players）。不接 Xbox / presence。
///     落盘：<c>%AppData%\PCLSE\bedrock-friends.json</c>。
/// </summary>
public static class BedrockFriendList
{
    private static readonly object Sync = new();
    private static List<BedrockFriend>? _cache;
    private static string? _filePath;

    public static string FilePath
    {
        get => _filePath ?? Path.Combine(Paths.SharedData, "bedrock-friends.json");
        set => _filePath = value;
    }

    public static IReadOnlyList<BedrockFriend> All
    {
        get
        {
            lock (Sync) return Load().ToArray();
        }
    }

    public static bool Add(string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("玩家名不能为空", nameof(name));
        lock (Sync)
        {
            var list = Load();
            if (list.Any(item => string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase)))
                return false;
            list.Add(new BedrockFriend(normalized));
            Save(list);
            return true;
        }
    }

    public static void Remove(string name)
    {
        lock (Sync)
        {
            var list = Load();
            if (list.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
                Save(list);
        }
    }

    /// <summary>
    ///     判定好友状态：只在"当前房间成员"里有这个人才算在线。
    ///     返回 (是否在线, 展示文案)，文案直接给 UI 用，避免各处硬编码。
    /// </summary>
    public static (bool Online, string Text) Describe(BedrockFriend friend,
        IEnumerable<RoomMember> roomMembers, bool inRoom)
    {
        var match = roomMembers.FirstOrDefault(member =>
            string.Equals(member.Name, friend.Name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return (true, match.IsHost ? "在线 · 房主" : "在线 · 同房间");
        return (false, inRoom ? "不在当前房间" : "离线（未进入房间）");
    }

    private static List<BedrockFriend> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<BedrockFriend>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    private static void Save(List<BedrockFriend> list)
    {
        _cache = list;
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
