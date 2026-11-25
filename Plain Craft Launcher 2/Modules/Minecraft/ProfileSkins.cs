using System.IO;
using System.Drawing.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;

namespace PCL;

internal static class ProfileSkins
{
    internal static event Action? AvatarChanged;

    internal static void LoadSelected(bool force = false)
    {
        if (ModProfile.selectedProfile is not { } profile) return;
        var loader = profile.Type switch
        {
            ModLaunch.McLoginType.Auth => skinAuth,
            ModLaunch.McLoginType.Ms => skinMs,
            _ => skinLegacy
        };
        loader.Start(isForceRestart: force);
    }

    private static void PublishAvatar(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> loader)
    {
        if (loader.IsAborted || loader.input is not { Count: >= 2 } input || string.IsNullOrEmpty(loader.output)) return;
        var uuid = input[1];
        var address = loader.output;
        ModBase.RunInUi(() =>
        {
            if (ModProfile.selectedProfile is not { } profile || profile.Uuid != uuid) return;
            try
            {
                var id = address.StartsWith(ModBase.pathImage, StringComparison.OrdinalIgnoreCase) ||
                    address.Contains(@"Cache\Skin\", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(address) : "local_" + ModBase.GetStringMD5(address);
                var folder = Path.Combine(ModBase.pathTemp, "Cache", "Skin", "Head");
                Directory.CreateDirectory(folder);
                var bitmap = new MyBitmap(address);
                using var head = ModSkinAvatar.Generate(bitmap.pic, 56);
                head.Save(Path.Combine(folder, id + ".png"), ImageFormat.Png);
                if (profile.SkinHeadId != id)
                {
                    profile.SkinHeadId = id;
                    ModProfile.SaveProfile();
                }
                AvatarChanged?.Invoke();
            }
            catch (Exception ex) { ModBase.Log(ex, "生成账号头像失败", ModBase.LogLevel.Debug); }
        });
    }

    // 正版皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinMs = new("Loader Skin Ms", SkinMsLoad,
        SkinMsInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinMsInput()
    {
        // 获取名称
        return new ModBase.EqualableList<string>
            { ModProfile.selectedProfile.Username, ModProfile.selectedProfile.Uuid };
    }

    private static void SkinMsLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 获取 Url
        var userName = data.input[0];
        var uuid = data.input[1];
        if (ModProfile.selectedProfile is not null)
        {
            userName = ModProfile.selectedProfile.Username;
            uuid = ModProfile.selectedProfile.Uuid;
        }

        if (string.IsNullOrEmpty(userName))
        {
            data.output = ModBase.pathImage + "Skins/" + ModSkin.McSkinSex(ModProfile.GetOfflineUuid(userName)) +
                          ".png";
            ModBase.Log("[Minecraft] 获取微软正版皮肤失败，ID 为空");
            goto Finish;
        }

        try
        {
            var result = ModSkin.McSkinGetAddress(uuid, "Ms");
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            result = ModSkin.McSkinDownload(result);
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            data.output = result;
        }
        catch (Exception ex)
        {
            if (ex is ThreadInterruptedException)
            {
                data.output = "";
                ModBase.Log("[Minecraft] 已取消皮肤获取：" + userName);
                return;
            }

            if (ex.ToString().Contains("429"))
            {
                data.output = ModBase.pathImage + "Skins/" +
                              ModSkin.McSkinSex(ModProfile.GetOfflineUuid(userName)) + ".png";
                ModBase.Log(
                    Lang.Text("Launch.Skin.Error.MsRateLimited", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.MsRateLimited", userName));
            }
            else if (ex.ToString().Contains("未设置自定义皮肤"))
            {
                data.output = ModBase.pathImage + "Skins/" +
                              ModSkin.McSkinSex(ModProfile.GetOfflineUuid(userName)) + ".png";
                ModBase.Log("[Minecraft] 用户未设置自定义皮肤，跳过皮肤加载");
            }
            else
            {
                data.output = ModBase.pathImage + "Skins/" +
                              ModSkin.McSkinSex(ModProfile.GetOfflineUuid(userName)) + ".png";
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Error.MsGet", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.MsGet", userName));
            }
        }

        Finish: ;

        // 刷新显示
        PublishAvatar(data);
    }

    // 离线皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinLegacy = new("Loader Skin Legacy",
        SkinLegacyLoad, SkinLegacyInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinLegacyInput()
    {
        // 把本地皮肤也纳入输入，换皮肤或切换模型后才能重新渲染
        var profile = ModProfile.selectedProfile;
        return new ModBase.EqualableList<string>
        {
            profile.Username, profile.Uuid, profile.LocalSkin ?? "", profile.LocalSkinSlim ? "slim" : "classic"
        };
    }

    private static void SkinLegacyLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 离线档案启用了本地皮肤时优先显示它，否则回退到按 UUID 推断的默认皮肤
        var localSkin = ModProfile.selectedProfile?.LocalSkin;
        data.output = !string.IsNullOrWhiteSpace(localSkin) && File.Exists(localSkin)
            ? localSkin
            : ModBase.pathImage + "Skins/" + ModSkin.McSkinSex(data.input[1]) + ".png";
        // 刷新显示
        PublishAvatar(data);
    }

    // Authlib-Injector 皮肤
    public static ModLoader.LoaderTask<ModBase.EqualableList<string>, string> skinAuth = new("Loader Skin Auth",
        SkinAuthLoad, SkinAuthInput, ThreadPriority.AboveNormal);

    private static ModBase.EqualableList<string> SkinAuthInput()
    {
        // 获取名称
        return new ModBase.EqualableList<string>
            { ModProfile.selectedProfile.Username, ModProfile.selectedProfile.Uuid };
    }

    private static void SkinAuthLoad(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> data)
    {
        // 获取 Url
        var userName = data.input[0];
        var uuid = data.input[1];
        if (string.IsNullOrEmpty(userName))
        {
            data.output = ModBase.pathImage + "Skins/Steve.png";
            ModBase.Log("[Minecraft] 获取 Authlib-Injector 皮肤失败，ID 为空");
            goto Finish;
        }

        try
        {
            var result = ModSkin.McSkinGetAddress(uuid, "Auth");
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            result = ModSkin.McSkinDownload(result);
            if (data.IsAborted)
                throw new ThreadInterruptedException("当前任务已取消：" + userName);
            data.output = result;
        }
        catch (Exception ex)
        {
            if (ex is ThreadInterruptedException)
            {
                data.output = "";
                return;
            }

            if (ex.ToString().Contains("429"))
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log(
                    $"[Minecraft] 获取 Authlib-Injector 皮肤失败（{userName}）：获取皮肤太过频繁，请 5 分钟后再试！",
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.AuthlibRateLimited"));
            }
            else if (ex.ToString().Contains("未设置自定义皮肤"))
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log("[Minecraft] 用户未设置自定义皮肤，跳过皮肤加载");
            }
            else
            {
                data.output = ModBase.pathImage + "Skins/Steve.png";
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Error.AuthGet", userName),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Launch.Skin.Error.AuthGet", userName));
            }
        }

        Finish: ;

        // 刷新显示
        PublishAvatar(data);
    }

    // 全部皮肤加载器
    // 需要放在其中元素的后面，否则会因为它提前被加载而莫名其妙变成 Nothing
    public static List<ModLoader.LoaderTask<ModBase.EqualableList<string>, string>> skinLoaders = new()
        { skinMs, skinLegacy, skinAuth };


    internal static void RefreshSelected()
    {
        if (ModProfile.selectedProfile is not { } profile) return;
        RefreshCache(profile.Type switch
        {
            ModLaunch.McLoginType.Ms => skinMs,
            ModLaunch.McLoginType.Auth => skinAuth,
            _ => skinLegacy
        });
    }

    public static void RefreshCache(ModLoader.LoaderTask<ModBase.EqualableList<string>, string> sender = null)
    {
        var hasLoaderRunning =
            skinLoaders.Any(skinLoader => skinLoader.State == ModBase.LoadState.Loading);

        if (hasLoaderRunning)
            // 由于 Abort 不是实时的，暂时不会释放文件，会导致删除报错，故只能取消执行
            HintService.Hint(Lang.Text("Launch.Skin.Refresh.Busy"));
        else
            // 清空缓存
            // 刷新控件
            ModBase.RunInThread(() =>
            {
                try
                {
                    HintService.Hint(Lang.Text("Launch.Skin.Refreshing"));
                    ModBase.Log("[Skin] 正在清空皮肤缓存");
                    if (Directory.Exists(ModBase.pathTemp + @"Cache\Skin"))
                        ModBase.DeleteDirectory(ModBase.pathTemp + @"Cache\Skin");
                    if (Directory.Exists(ModBase.pathTemp + @"Cache\Uuid"))
                        ModBase.DeleteDirectory(ModBase.pathTemp + @"Cache\Uuid");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Skin\IndexMs.ini");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Skin\IndexAuth.ini");
                    ModBase.IniClearCache(ModBase.pathTemp + @"Cache\Uuid\Mojang.ini");
                    foreach (var SkinLoader in sender is not null
                                 ? new[] { sender }
                                 : new[] { skinLegacy, skinMs })
                        SkinLoader.WaitForExit(isForceRestart: true);
                    HintService.Hint(Lang.Text("Launch.Skin.RefreshSuccess"), HintType.Success);
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Launch.Skin.Refresh.Error"),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Launch.Skin.Refresh.Error"));
                }
            });
    }

    /// <summary>
    ///     在更换正版皮肤后，刷新正版皮肤。
    /// </summary>
    /// <param name="skinAddress">新的正版皮肤完整地址。</param>
    public static void ReloadCache(string skinAddress)
    {
        // 更新缓存
        // 刷新控件
        // 完成提示
        ModBase.RunInThread(() =>
        {
            try
            {
                ModBase.WriteIni(ModBase.pathTemp + @"Cache\Skin\IndexMs.ini", ModProfile.selectedProfile.Uuid,
                    skinAddress);
                ModBase.Log($"[Skin] 已写入皮肤地址缓存 {ModProfile.selectedProfile.Uuid} -> {skinAddress}");
                skinMs.WaitForExit(isForceRestart: true);
                HintService.Hint(Lang.Text("Launch.Skin.ChangeSuccess"), HintType.Success);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Launch.Skin.Change.Error.MsRefresh"),
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Launch.Skin.Change.Error.MsRefresh"));
            }
        });
    }


    internal static string GetCapeDisplayName(string capeAlias)
    {
        var safeName = capeAlias
            .Replace("-", "")
            .Replace(" ", "")
            .Replace("'", "");
        var key = $"Launch.Skin.Cape.Name.{safeName}";
        var name = Lang.Text(key);
        if (name == $"!{key}!" || name == key)
            return capeAlias;
        return name;
    }
}
