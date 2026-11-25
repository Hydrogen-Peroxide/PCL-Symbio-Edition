using System.Text.Json.Nodes;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.Yggdrasil;
using PCL.Core.Utils.Validate;

namespace PCL;

internal enum OfflineUuidMode { Standard, Legacy, Custom }

internal static class AccountSignIn
{
    private static int active;
    internal static bool IsActive => Volatile.Read(ref active) != 0;
    internal static bool TryBegin()
    {
        if (ModLaunch.mcLaunchLoader.State == ModBase.LoadState.Loading || Interlocked.CompareExchange(ref active, 1, 0) != 0)
            return false;
        ModProfile.isCreatingProfile = true;
        return true;
    }

    internal static void End()
    {
        ModProfile.isCreatingProfile = false;
        Interlocked.Exchange(ref active, 0);
    }

    public static bool ValidPlayerName(string name) =>
        new RegexValidator("^[A-z0-9_]{3,16}$").Validate(name).IsValid;

    public static void CreateOffline(string username, OfflineUuidMode mode, string customUuid)
    {
        string uuid;
        if (mode == OfflineUuidMode.Custom)
        {
            uuid = customUuid.Replace("-", "");
            if (!new RegexValidator("^[a-fA-F0-9]{32}$").Validate(uuid).IsValid)
                throw new ArgumentException(Lang.Text("Launch.Account.Offline.InvalidUuid", customUuid));
        }
        else uuid = ModProfile.GetOfflineUuid(username, isLegacy: mode == OfflineUuidMode.Legacy);

        Save(new ModProfile.McProfile
        {
            Type = ModLaunch.McLoginType.Legacy, Uuid = uuid, Username = username, Desc = ""
        });
    }

    public static async Task<(string Root, string? Name)> ResolveServerAsync(string input)
    {
        var root = await ApiLocation.TryRequestAsync(input);
        using var response = await HttpRequest.Create(root).SendAsync();
        var json = ModBase.GetJson(await response.AsStringAsync()) as JsonObject;
        return (root, json?["meta"]?["serverName"]?.ToString());
    }

    public static async Task LoginWithPasswordAsync(string server, string username, string password, IProgress<double> progress)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(Lang.Text("Launch.Account.Auth.EmptyFields"));
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException(Lang.Text("Launch.Account.Auth.InvalidServer"));

        var request = new ModLaunch.McLoginServer(ModLaunch.McLoginType.Auth)
        {
            BaseUrl = server.TrimEnd('/') + "/authserver", UserName = username, Password = password,
            Description = "Authlib-Injector", LoginType = ModLaunch.McLoginType.Auth
        };
        var loader = ModLaunch.mcLoginAuthLoader;
        loader.Start(request, true);
        while (loader.State == ModBase.LoadState.Loading)
        {
            progress.Report(loader.Progress);
            await Task.Delay(75);
        }
        if (loader.State == ModBase.LoadState.Aborted)
            throw new OperationCanceledException(Lang.Text("Launch.Account.Auth.Cancelled"));
        if (loader.State != ModBase.LoadState.Finished)
            throw loader.Error ?? new InvalidOperationException(Lang.Text("Launch.Account.Microsoft.Error.Unknown"));
    }

    public static async Task LoginWithBrowserAsync(ModYggdrasilConnect.ServerInfo info,
        Action<PCL.Core.Minecraft.IdentityModel.OAuth.DeviceCodeData> showCode)
    {
        var result = await ModYggdrasilConnect.LoginAsync(info, showCode);
        var name = info.Root;
        try
        {
            using var response = await HttpRequest.Create(info.Root).SendAsync();
            if (response.IsSuccess)
                name = (ModBase.GetJson(await response.AsStringAsync()) as JsonObject)?["meta"]?["serverName"]?.ToString() ?? name;
        }
        catch (Exception ex) { ModBase.Log(ex, "获取验证服务器名称失败"); }

        Save(new ModProfile.McProfile
        {
            Type = ModLaunch.McLoginType.Auth, Uuid = result.Uuid, Username = result.UserName,
            Server = info.AuthServer, ServerName = name, Name = "", Password = "",
            AccessToken = result.AccessToken, RefreshToken = result.RefreshToken, ClientToken = result.Uuid,
            Discovery = info.Discovery, OAuthClientId = info.ClientId, Expires = result.Expires, Desc = ""
        });
    }

    private static void Save(ModProfile.McProfile profile)
    {
        ModProfile.profileList.Add(profile);
        ModProfile.selectedProfile = profile;
        ModProfile.SaveProfile();
    }
}
