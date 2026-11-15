using System.IO;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Yggdrasil;

namespace PCL;

/// <summary>
///     离线档案的本地皮肤与披风支持。<br/>
///     皮肤库里的 PNG 由 <see cref="LocalSkinServer" /> 以 Yggdrasil 协议对外提供，
///     启动游戏时再通过 authlib-injector 把客户端劫持到本地服务。
/// </summary>
public static class ModLocalSkin
{
    private static readonly object Lock = new();
    private static LocalSkinServer? _server;

    /// <summary>当前运行中的本地服务，未启动时为 null。</summary>
    public static LocalSkinServer? Server => _server;

    /// <summary>判断某个档案是否启用了本地皮肤或披风。</summary>
    public static bool IsEnabledFor(ModProfile.McProfile? profile)
    {
        return profile is { Type: ModLaunch.McLoginType.Legacy } &&
               (HasFile(profile.LocalSkin) || HasFile(profile.LocalCape));
    }

    private static bool HasFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <summary>
    ///     确保本地服务已启动，并把所有启用了本地皮肤的离线档案注册进去。
    /// </summary>
    public static LocalSkinServer EnsureServer()
    {
        lock (Lock)
        {
            if (_server is not null) return _server;

            var server = new LocalSkinServer(Path.Combine(ModBase.pathAppdataConfig, "SkinService"));
            foreach (var profile in ModProfile.profileList) RegisterCore(server, profile);
            _server = server;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => server.Dispose();
            ModBase.Log("[LocalSkin] 本地皮肤服务已启动：" + server.ApiRoot, ModBase.LogLevel.Developer);
            return server;
        }
    }

    /// <summary>把档案当前的皮肤设置同步到本地服务（服务未启动时忽略）。</summary>
    public static void Register(ModProfile.McProfile? profile)
    {
        if (profile is null) return;
        lock (Lock)
        {
            if (_server is null) return;
            RegisterCore(_server, profile);
        }
    }

    /// <summary>关闭某个档案的本地皮肤。</summary>
    public static void Unregister(ModProfile.McProfile? profile)
    {
        if (profile is null) return;
        lock (Lock) _server?.Unregister(profile.Uuid);
    }

    private static void RegisterCore(LocalSkinServer server, ModProfile.McProfile profile)
    {
        if (!IsEnabledFor(profile))
        {
            // 皮肤和披风都被取消时，同步撤销服务端残留的档案，避免继续返回旧材质
            server.Unregister(profile.Uuid);
            return;
        }

        try
        {
            server.Register(profile.Uuid, profile.Username, profile.LocalSkin, profile.LocalSkinSlim, profile.LocalCape);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[LocalSkin] 注册皮肤失败", ModBase.LogLevel.Developer);
        }
    }

    /// <summary>
    ///     皮肤设置变化后刷新启动页的预览。预览会顺带重新生成档案列表头像。
    /// </summary>
    public static void RefreshPreview()
    {
        try
        {
            ModBase.RunInUi(() => ModMain.frmLoginProfileSkin?.Reload());
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[LocalSkin] 刷新皮肤预览失败", ModBase.LogLevel.Developer);
        }
    }

    /// <summary>
    ///     确保 authlib-injector.jar 已就绪。<br/>
    ///     优先使用运行目录中已被联网更新过的版本；缺失时从随包资源释放，保证首次离线启动也能用。
    /// </summary>
    /// <returns>authlib-injector.jar 的完整路径</returns>
    public static string EnsureAuthlibInjector()
    {
        var target = Path.Combine(ModBase.pathPure, "authlib-injector.jar");
        if (File.Exists(target)) return target;

        try
        {
            using var resource = ModBase.GetResourceStream("Resources/authlib-injector.jar") ??
                                 throw new FileNotFoundException("Resources/authlib-injector.jar");
            Directory.CreateDirectory(ModBase.pathPure);
            // 先写临时文件再改名，避免半截文件被当成可用版本
            var temp = target + ".tmp";
            using (var file = File.Create(temp)) resource.CopyTo(file);
            File.Move(temp, target, true);
            ModBase.Log("[LocalSkin] 已从随包资源释放 authlib-injector.jar", ModBase.LogLevel.Developer);
        }
        catch (Exception ex)
        {
            throw new Exception(Lang.Text("Minecraft.Launch.Error.AuthlibInjectorMissing"), ex);
        }

        return target;
    }
}
