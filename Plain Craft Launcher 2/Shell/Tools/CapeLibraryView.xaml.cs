using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Network;

namespace PCL;

public partial class CapeLibraryView
{
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private static string CapeFolder => Path.Combine(ProfileAssetLibrary.LibraryFolder, "Capes");

    private sealed record CapeOption(string? Key, string Name, BitmapSource? Preview, string Dimensions, bool Active);

    private int loadGeneration;
    private bool busy;
    private ModProfile.McProfile? displayedProfile;

    public CapeLibraryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCapesAsync();
        Unloaded += (_, _) => loadGeneration++;
    }

    private async Task LoadCapesAsync()
    {
        var generation = ++loadGeneration;
        var profile = ModProfile.SyncSelectedProfile();
        displayedProfile = profile;
        var local = profile?.Type == ModLaunch.McLoginType.Legacy;
        var microsoft = profile?.Type == ModLaunch.McLoginType.Ms;
        var auth = profile?.Type == ModLaunch.McLoginType.Auth;

        CapeCards.ItemsSource = null;
        BtnApply.Visibility = local || microsoft ? Visibility.Visible : Visibility.Collapsed;
        // 选图导入只写入本地档案（ApplyLocalCape），对微软/外置账号无意义，仍然只对离线账号显示
        BtnImport.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        BtnManage.Visibility = auth ? Visibility.Visible : Visibility.Collapsed;
        BtnReload.Visibility = local || microsoft ? Visibility.Visible : Visibility.Collapsed;
        if (!local && !microsoft)
        {
            SetBusy(false);
            SetStatus(Lang.Text(auth ? "Tools.SkinLibrary.Cape.Auth" : "Tools.SkinLibrary.Cape.NoProfile"));
            return;
        }

        SetBusy(true);
        SetStatus(microsoft ? Lang.Text("Launch.Skin.Cape.FetchingList") : string.Empty);
        var login = microsoft ? ModProfile.GetLoginData() : null;
        try
        {
            var options = await Task.Run(() => local
                ? ReadLocalCapes(profile.LocalCape)
                : ReadMicrosoftCapes(login!, profile.Uuid));
            if (generation != loadGeneration || !ReferenceEquals(profile, ModProfile.SyncSelectedProfile()))
                return;
            CapeCards.ItemsSource = options;
            CapeCards.SelectedItem = options.FirstOrDefault(cape => cape.Active);
            SetStatus(string.Empty);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 读取披风列表失败", ModBase.LogLevel.Debug);
            if (generation == loadGeneration)
                SetStatus(Lang.Text("Launch.Skin.Cape.Error.List"));
        }
        finally
        {
            if (generation == loadGeneration)
                SetBusy(false);
        }
    }

    private static List<CapeOption> ReadLocalCapes(string? active)
    {
        var hasActive = File.Exists(active);
        var options = new List<CapeOption> { NoCape(!hasActive) };
        var files = Directory.Exists(CapeFolder)
            ? Directory.EnumerateFiles(CapeFolder, "*.png").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();
        if (hasActive && !files.Contains(active!, StringComparer.OrdinalIgnoreCase))
            files.Insert(0, active!);
        foreach (var path in files)
            options.Add(ReadTexture(path, Path.GetFileNameWithoutExtension(path), path,
                hasActive && string.Equals(path, active, StringComparison.OrdinalIgnoreCase)));
        return options;
    }

    private static List<CapeOption> ReadMicrosoftCapes(ModLaunch.McLoginData login, string expectedUuid)
    {
        var token = GetMicrosoftToken(login, expectedUuid);
        var response = Requester.Fetch(MinecraftProfileUrl, new FetchParam
        {
            Method = "GET",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
        });
        var json = JsonNode.Parse(response) as JsonObject ?? throw new InvalidDataException("Missing Minecraft profile");
        if (json["id"]?.ToString() is not { } uuid || !MatchesProfile(expectedUuid, uuid))
            throw new InvalidDataException("Minecraft profile changed before loading capes");

        var options = new List<CapeOption>();
        if (json["capes"] is JsonArray capes)
        {
            foreach (var cape in capes.OfType<JsonObject>())
            {
                if (!Guid.TryParse(cape["id"]?.ToString(), out var id))
                    continue;
                var key = cape["id"]!.ToString();
                var name = ProfileSkins.GetCapeDisplayName(cape["alias"]?.ToString() ?? key);
                var active = string.Equals(cape["state"]?.ToString(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
                try
                {
                    var path = Path.Combine(ModBase.pathTemp, "Cache", "Capes", id.ToString("N") + ".png");
                    if (!File.Exists(path) && Uri.TryCreate(cape["url"]?.ToString(), UriKind.Absolute, out var uri) &&
                        uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) &&
                        uri.Scheme is "http" or "https")
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var source = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                        FileDownloader.DownloadByLoader(source.Uri.AbsoluteUri, path);
                    }
                    options.Add(ReadTexture(key, name, path, active));
                }
                catch (Exception ex)
                {
                    ModBase.Log(ex, "[SkinLibrary] 读取披风缩略图失败", ModBase.LogLevel.Debug);
                    options.Add(new CapeOption(key, name, null, string.Empty, active));
                }
            }
        }
        options.Insert(0, NoCape(!options.Any(cape => cape.Active)));
        return options;
    }

    private static bool MatchesProfile(string expected, string actual) =>
        Guid.TryParse(expected, out var expectedId) && Guid.TryParse(actual, out var actualId) && expectedId == actualId;

    private static string GetMicrosoftToken(ModLaunch.McLoginData login, string expectedUuid)
    {
        var loader = ModLaunch.mcLoginMsLoader;
        if (loader.State != ModBase.LoadState.Finished || !MatchesProfile(expectedUuid, loader.output.Uuid))
            loader.WaitForExit(login);
        var result = loader.output;
        if (!MatchesProfile(expectedUuid, result.Uuid) || string.IsNullOrWhiteSpace(result.AccessToken))
            throw new InvalidOperationException("The current Minecraft login does not match the selected account");
        return result.AccessToken;
    }

    private static CapeOption NoCape(bool active) =>
        new(null, Lang.Text("Launch.Skin.Cape.None"), null, string.Empty, active);

    private static CapeOption ReadTexture(string key, string name, string path, bool active)
    {
        if (!File.Exists(path))
            return new CapeOption(key, name, null, string.Empty, active);
        try
        {
            using var stream = File.OpenRead(path);
            var texture = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var scale = Math.Max(1, texture.PixelWidth / 64);
            BitmapSource preview = texture.PixelWidth >= 11 * scale && texture.PixelHeight >= 16 * scale
                ? new CroppedBitmap(texture, new Int32Rect(scale, scale, 10 * scale, 16 * scale))
                : texture;
            preview.Freeze();
            return new CapeOption(key, name, preview, $"{texture.PixelWidth}×{texture.PixelHeight}", active);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 无法解码披风图片", ModBase.LogLevel.Debug);
            return new CapeOption(key, name, null, string.Empty, active);
        }
    }

    private void SetStatus(string text)
    {
        LabCapeStatus.Text = text;
        LabCapeStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        CapeCards.IsEnabled = !value;
        BtnImport.IsEnabled = !value;
        BtnReload.IsEnabled = !value;
        UpdateApply();
    }

    private void UpdateApply()
    {
        if (BtnApply is not null)
            BtnApply.IsEnabled = !busy && CapeCards.SelectedItem is CapeOption { Active: false };
    }

    private void CapeCards_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateApply();

    private async void BtnReload_Click(object sender, System.Windows.RoutedEventArgs e) => await LoadCapesAsync();

    private void BtnManage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 「更换披风」对外置账号只是跳转验证服务器的衣柜页面，点了没有任何界面反馈，
        // 很容易被当成按钮失效。这里补一条提示，并在拿不到服务器时明确告知原因。
        if (ModProfile.SyncSelectedProfile() is not { Type: ModLaunch.McLoginType.Auth } profile ||
            string.IsNullOrWhiteSpace(profile.Server))
        {
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Cape.ManageUnavailable"), HintType.Info);
            return;
        }

        try
        {
            ModBase.OpenWebsite(profile.Server.BeforeFirst("api/yggdrasil/authserver") + "user/closet");
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Cape.ManageOpened"), HintType.Info);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 打开披风管理页面失败", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Cape.ManageUnavailable"), HintType.Error);
        }
    }

    private async void BtnApply_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (busy || CapeCards.SelectedItem is not CapeOption { Active: false } cape ||
            ModProfile.SyncSelectedProfile() is not { } profile || !ReferenceEquals(profile, displayedProfile))
            return;

        var generation = loadGeneration;
        SetBusy(true);
        try
        {
            if (profile.Type == ModLaunch.McLoginType.Ms)
            {
                var login = ModProfile.GetLoginData();
                var uuid = profile.Uuid;
                await Task.Run(() =>
                {
                    var token = GetMicrosoftToken(login, uuid);
                    var result = Requester.Fetch(MinecraftProfileUrl + "/capes/active", new FetchParam
                    {
                        Method = cape.Key is null ? "DELETE" : "PUT",
                        Content = cape.Key is null ? "" : new JsonObject { ["capeId"] = cape.Key }.ToJsonString(),
                        ContentType = "application/json",
                        Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
                    });
                    if (result.Contains("\"errorMessage\""))
                        throw new InvalidOperationException(Lang.Text("Launch.Skin.Cape.ChangeFailed"));
                });
            }
            else if (profile.Type == ModLaunch.McLoginType.Legacy)
            {
                if (cape.Key is not null && !File.Exists(cape.Key))
                    throw new FileNotFoundException();
                ApplyLocalCape(profile, cape.Key);
            }
            else
            {
                return;
            }

            HintService.Hint(Lang.Text("Launch.Skin.Cape.ChangeSuccess"), HintType.Success);
            if (generation == loadGeneration && ReferenceEquals(profile, ModProfile.SyncSelectedProfile()))
                await LoadCapesAsync();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 应用披风失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Launch.Skin.Cape.ChangeFailed"));
        }
        finally
        {
            if (generation == loadGeneration)
                SetBusy(false);
        }
    }

    private async void BtnImport_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (busy || ModProfile.SyncSelectedProfile() is not { Type: ModLaunch.McLoginType.Legacy } profile)
            return;
        try
        {
            Directory.CreateDirectory(CapeFolder);
            var files = SystemDialogs.SelectFiles(Lang.Text("Tools.SkinLibrary.ImportFilter"),
                Lang.Text("Tools.SkinLibrary.Cape.PickTitle"), CapeFolder, false);
            if (files.Length == 0 || !ReferenceEquals(profile, ModProfile.SyncSelectedProfile()))
                return;

            var file = files[0];
            var size = ProfileAssetLibrary.ReadSkinSize(file);
            if (size.Width < 64 || size.Height < 32 || size.Width % 64 != 0 || size.Height % 32 != 0)
            {
                HintService.Hint(Lang.Text("Tools.SkinLibrary.Cape.Invalid", $"{size.Width}×{size.Height}"), HintType.Error);
                return;
            }

            var target = ProfileAssetLibrary.GetUniquePath(Path.Combine(CapeFolder, Path.GetFileName(file)));
            File.Copy(file, target);
            ApplyLocalCape(profile, target);
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Cape.Applied", Path.GetFileNameWithoutExtension(target)), HintType.Success);
            await LoadCapesAsync();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 导入披风失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.SkinLibrary.Cape.ImportFailed", ex.Message));
        }
    }

    private static void ApplyLocalCape(ModProfile.McProfile profile, string? path)
    {
        profile.LocalCape = path;
        ModProfile.SaveProfile();
        ModLocalSkin.Register(profile);
    }
}
