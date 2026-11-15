using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Minecraft.Yggdrasil;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.Validate;

namespace PCL;

public partial class PageLoginAuth
{
    public static string draggedAuthServer;

    // 预设服务器
    private static readonly Dictionary<string, string> predefinedAuthServers = new()
    {
        { Lang.Text("Launch.Account.Auth.Preset.LittleSkin"), "https://littleskin.cn/api/yggdrasil" },
        { Lang.Text("Common.Option.Customize"), "" }
    };

    private bool _isRegisterMode = true;

    /// <summary>
    ///     当前服务器支持的 Yggdrasil Connect（网页登录）信息，为 null 表示不支持
    /// </summary>
    private ModYggdrasilConnect.ServerInfo _connectInfo;

    public PageLoginAuth()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
        Loaded += (_, _) => ReloadRegisterButton();
        // Handles
        BtnBack.Click += BtnBack_Click;
        BtnLogin.Click += BtnLogin_Click;
        BtnWebLogin.Click += BtnWebLogin_Click;
        TextServer.TextChanged += TextServer_TextChanged;
        BtnLink.Click += Btn_Click;
    }

    private void Reload()
    {
        var serverItems = TextServer.Items;
        serverItems.Clear();
        foreach (var serverName in predefinedAuthServers.Keys)
            serverItems.Add(new MyComboBoxItem { Content = serverName });
        TextServer.Text = draggedAuthServer;
        draggedAuthServer = null;
        RefreshWebLogin();
    }

    /// <summary>
    ///     探测当前验证服务器是否支持 Yggdrasil Connect，并据此显示网页登录按钮
    /// </summary>
    private async void RefreshWebLogin()
    {
        var server = TextServer?.Text?.Trim();
        _connectInfo = null;
        if (BtnWebLogin is not null) BtnWebLogin.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(server) || !server.IsMatch(RegexPatterns.HttpUri)) return;
        try
        {
            var info = await ModYggdrasilConnect.DiscoverAsync(server);
            if (!string.Equals(TextServer?.Text?.Trim(), server, StringComparison.Ordinal)) return;
            _connectInfo = info;
            if (BtnWebLogin is not null)
                BtnWebLogin.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "探测网页登录支持失败");
        }
    }

    /// <summary>
    ///     用设备代码流在浏览器里完成 Yggdrasil Connect 登录
    /// </summary>
    private void BtnWebLogin_Click(object sender, EventArgs e)
    {
        var info = _connectInfo;
        if (info is null)
        {
            HintService.Hint(Lang.Text("Launch.Account.Auth.WebLogin.Unsupported"), HintType.Error);
            return;
        }

        BtnWebLogin.IsEnabled = false;
        BtnLogin.IsEnabled = false;
        BtnBack.IsEnabled = false;
        Dispatcher.BeginInvoke(new Func<Task>(async () =>
        {
            try
            {
                ModProfile.isCreatingProfile = true;
                var result = await ModYggdrasilConnect.LoginAsync(info, code =>
                {
                    var website = string.IsNullOrWhiteSpace(code.VerificationUriComplete)
                        ? code.VerificationUri ?? ""
                        : code.VerificationUriComplete!;
                    ModBase.RunInUi(() =>
                    {
                        ModBase.ClipboardSet(code.UserCode ?? "");
                        if (!string.IsNullOrWhiteSpace(website)) ModBase.OpenWebsite(website);
                        ModMain.MyMsgBox(
                            Lang.Text("Launch.Account.Auth.WebLogin.Instructions", code.UserCode ?? "", website),
                            Lang.Text("Launch.Account.Auth.WebLogin.Title"));
                    });
                });

                // 服务器名称，取不到就退回根地址
                var serverName = info.Root;
                try
                {
                    using var response = await HttpRequest.Create(info.Root).SendAsync();
                    if (response.IsSuccess)
                    {
                        var json = ModBase.GetJson(await response.AsStringAsync()) as JsonObject;
                        serverName = json?["meta"]?["serverName"]?.ToString() ?? info.Root;
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "获取验证服务器名称失败");
                }

                var newProfile = new ModProfile.McProfile
                {
                    Type = ModLaunch.McLoginType.Auth,
                    Uuid = result.Uuid,
                    Username = result.UserName,
                    Server = info.AuthServer,
                    ServerName = serverName,
                    Name = "",
                    Password = "",
                    AccessToken = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    ClientToken = result.Uuid,
                    Discovery = info.Discovery,
                    OAuthClientId = info.ClientId,
                    Expires = result.Expires,
                    Desc = ""
                };
                ModProfile.profileList.Add(newProfile);
                ModProfile.selectedProfile = newProfile;
                ModProfile.SaveProfile();
                ModProfile.ProfileLog($"登录成功（Yggdrasil Connect, {result.UserName}）");
                HintService.Hint(Lang.Text("Launch.Account.Auth.WebLogin.Success"), HintType.Success);
                ModMain.frmLaunchLeft.RefreshPage(true);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, Lang.Text("Launch.Account.Auth.WebLogin.FailedTitle"), ModBase.LogLevel.Msgbox,
                    userSummary: Lang.Text("Launch.Account.Auth.WebLogin.FailedTitle"));
            }
            finally
            {
                ModProfile.isCreatingProfile = false;
                BtnWebLogin.IsEnabled = true;
                BtnLogin.IsEnabled = true;
                BtnBack.IsEnabled = true;
            }
        }));
    }

    private void BtnBack_Click(object sender, EventArgs e)
    {
        TextServer.Text = null;
        TextName.Text = null;
        TextPass.Password = null;
        ModMain.frmLaunchLeft.RefreshPage(true);
    }

    private void BtnLogin_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextServer.Text) || string.IsNullOrWhiteSpace(TextName.Text) ||
            string.IsNullOrWhiteSpace(TextPass.Password))
        {
            HintService.Hint(Lang.Text("Launch.Account.Auth.EmptyFields"), HintType.Error);
            return;
        }

        if (!TextServer.Text.IsMatch(RegexPatterns.HttpUri))
        {
            HintService.Hint(Lang.Text("Launch.Account.Auth.InvalidServer"), HintType.Error);
            return;
        }

        BtnLogin.IsEnabled = false;
        BtnBack.IsEnabled = false;
        var loginData = new ModLaunch.McLoginServer(ModLaunch.McLoginType.Auth)
        {
            BaseUrl = TextServer.Text.EndsWithF("/") ? $"{TextServer.Text}authserver" : $"{TextServer.Text}/authserver",
            UserName = TextName.Text, Password = TextPass.Password, Description = "Authlib-Injector",
            LoginType = ModLaunch.McLoginType.Auth
        };
        Dispatcher.BeginInvoke(new Func<Task>(async () =>
        {
            try
            {
                ModProfile.isCreatingProfile = true;
                ModLaunch.mcLoginAuthLoader.Start(loginData, true);
                while (ModLaunch.mcLoginAuthLoader.State == ModBase.LoadState.Loading)
                {
                    BtnLogin.Text = Lang.Number(ModLaunch.mcLoginAuthLoader.Progress, "P0");
                    await Task.Delay(50);
                }

                switch (ModLaunch.mcLoginAuthLoader.State)
                {
                    case ModBase.LoadState.Finished:
                        ModMain.frmLaunchLeft.RefreshPage(true);
                        break;
                    case ModBase.LoadState.Aborted:
                        HintService.Hint(Lang.Text("Launch.Account.Auth.Cancelled"));
                        break;
                    case ModBase.LoadState.Waiting:
                    case ModBase.LoadState.Loading:
                    case ModBase.LoadState.Failed:
                    default:
                    {
                        if (ModLaunch.mcLoginAuthLoader.Error is null)
                            throw new Exception(Lang.Text("Launch.Account.Microsoft.Error.Unknown"));
                        throw new Exception(ModLaunch.mcLoginAuthLoader.Error.Message,
                            ModLaunch.mcLoginAuthLoader.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message == "$$")
                {
                }
                else if (ex.Message.StartsWith("$"))
                {
                    HintService.Hint(
                        Lang.Text("Launch.Account.Auth.LoginFailed.WithDetail",ex.Message.TrimStart('$')),
                        HintType.Error);
                }
                else
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Launch.Account.Auth.LoginFailed"),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Launch.Account.Auth.LoginFailed"));
                }
            }
            finally
            {
                ModProfile.isCreatingProfile = false;
                BtnLogin.IsEnabled = true;
                BtnBack.IsEnabled = true;
                BtnLogin.Text = Lang.Text("Launch.Account.Auth.Login");
            }
        }));
    }

    // 获取验证服务器名称
    private void GetServerName()
    {
        var serverUriInput = TextServer.Text;
        if (string.IsNullOrWhiteSpace(serverUriInput))
        {
            TextServerName.Visibility = Visibility.Hidden;
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            string serverUri = null;
            string serverName = null;
            try
            {
                serverUri = await ApiLocation.TryRequestAsync(serverUriInput);
                using var resp = await HttpRequest.Create(serverUri).SendAsync();
                var responseText = await resp.AsStringAsync();
                serverName = await Task.Run(() => ModBase.GetJson(responseText)["meta"]?["serverName"]?.ToString());
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "从服务器获取名称失败");
            }

            if (serverUri is not null) TextServer.Text = serverUri;
            if (serverName is null)
            {
                TextServerName.Visibility = Visibility.Hidden;
            }
            else
            {
                TextServerName.Text = Lang.Text("Launch.Account.Auth.ServerLabel", serverName);
                TextServerName.Visibility = Visibility.Visible;
            }
        });
    }

    // 链接处理
    private void ComboName_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isRegisterMode = string.IsNullOrEmpty(TextName.Text);
        BtnLink.Content = _isRegisterMode
            ? Lang.Text("Launch.Account.Auth.Register")
            : Lang.Text("Launch.Account.Auth.ForgotPassword");
    }

    private void Btn_Click(object sender, EventArgs e)
    {
        ModBase.OpenWebsite(_isRegisterMode
            ? Config.InstanceAuth.AuthRegisterAddress.ToString()
            : Config.InstanceAuth.AuthRegisterAddress.ToString().Replace("/auth/register", "/auth/forgot"));
    }

    // 切换注册按钮可见性
    private void ReloadRegisterButton()
    {
        var address = Config.InstanceAuth.AuthRegisterAddress.ToString();
        BtnLink.Visibility = new HttpValidator().Validate(address).IsValid
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TextServer_TextChanged(object sender, TextChangedEventArgs e)
    {
        predefinedAuthServers.TryGetValue(TextServer.Text, out var server);
        if (server is not null) TextServer.Text = server;
        RefreshWebLogin();
    }
}