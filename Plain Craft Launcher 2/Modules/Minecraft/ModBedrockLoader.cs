using System;
using System.Collections.Generic;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     基岩版 GDK 便携版的模组加载器选择（参考 BedrockBoot）。
///     GDK 版安装完成后在版本列表里为每个版本选择加载器：
///     原版不加载任何 DLL，LeviLamina 注入该版本 mods 目录内的 DLL，
///     BLoader 则按需下载并注入 BLoader.dll（自带 Mod 装载与文件重定向）。
/// </summary>
public static class ModBedrockLoader
{
    public const string Vanilla = "vanilla";
    public const string LeviLamina = "levilamina";
    public const string BLoader = "bloader";

    /// <summary>读取指定版本的加载器，未设置或无法识别时按原版处理。</summary>
    public static string GetLoader(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return Vanilla;
        var loaders = Config.Launch.BedrockLoaders;
        if (loaders is null || !loaders.TryGetValue(versionId, out var loader)) return Vanilla;
        return loader is LeviLamina or BLoader ? loader : Vanilla;
    }

    /// <summary>保存指定版本的加载器；选择原版时直接移除记录。</summary>
    public static void SetLoader(string versionId, string loader)
    {
        if (string.IsNullOrEmpty(versionId)) return;
        var loaders = new Dictionary<string, string>(Config.Launch.BedrockLoaders ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (loader is LeviLamina or BLoader)
            loaders[versionId] = loader;
        else
            loaders.Remove(versionId);
        Config.Launch.BedrockLoaders = loaders;
    }

    /// <summary>加载器的显示名称。</summary>
    public static string GetDisplayName(string loader)
    {
        return loader switch
        {
            LeviLamina => Lang.Text("Bedrock.Loader.LeviLamina"),
            BLoader => Lang.Text("Bedrock.Loader.BLoader"),
            _ => Lang.Text("Bedrock.Loader.Vanilla")
        };
    }
}
