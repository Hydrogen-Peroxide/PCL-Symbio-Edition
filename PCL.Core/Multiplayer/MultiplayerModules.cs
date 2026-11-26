using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Core.Multiplayer;

/// <summary>
///     联机模块注册表：内置实现与插件实现都注册到这里，UI 只按 id 取。
///     注册表本身无状态副作用，重复注册同 id 以先注册者为准（内置实现优先于插件）。
/// </summary>
public static class MultiplayerModules
{
    private static readonly ConcurrentDictionary<string, IMultiplayerModule> Registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已注册的全部模块。</summary>
    public static IReadOnlyCollection<IMultiplayerModule> All => Registered.Values.ToArray();

    /// <summary>按版本筛选。</summary>
    public static IReadOnlyList<IMultiplayerModule> ForEdition(MultiplayerEdition edition) =>
        Registered.Values.Where(module => module.Edition == edition).ToArray();

    /// <summary>注册模块；同 id 已存在时返回 false（内置实现不会被插件覆盖）。</summary>
    public static bool Register(IMultiplayerModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Id)) throw new ArgumentException("模块 Id 不能为空", nameof(module));
        return Registered.TryAdd(module.Id, module);
    }

    /// <summary>按 id 取模块，不存在返回 null。</summary>
    public static IMultiplayerModule? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Registered.GetValueOrDefault(id);

    /// <summary>取某版本下第一个可用模块，没有则取第一个（用于 UI 默认项）。</summary>
    public static IMultiplayerModule? Default(MultiplayerEdition edition)
    {
        var modules = ForEdition(edition);
        return modules.FirstOrDefault(module => module.IsAvailable) ?? modules.FirstOrDefault();
    }
}
