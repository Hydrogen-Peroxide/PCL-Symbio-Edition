using System.Windows;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageLoginProfileSkin
{
    public PageLoginProfileSkin()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
        // Handles
        PanData.MouseEnter += ShowPanel;
        PanData.MouseLeave += HidePanel;
        BtnSkinRefresh.Click += BtnSkinRefresh_Click;
        BtnEdit.Click += BtnEdit_Click;
        BtnSelect.Click += ChangeProfile;
    }

    /// <summary>
    ///     刷新页面显示的所有信息。
    /// </summary>
    public void Reload()
    {
        ModBase.Log("[Profile] 刷新档案界面");
        Skin.Clear();
        if (ModProfile.selectedProfile.Type == ModLaunch.McLoginType.Ms)
        {
            BtnEdit.Visibility = Visibility.Visible;
            ModBase.Log("[Profile] 使用正版皮肤加载器");
            Skin.loader = PageLaunchLeft.skinMs;
        }
        else if (ModProfile.selectedProfile.Type == ModLaunch.McLoginType.Auth)
        {
            BtnEdit.Visibility = Visibility.Visible;
            ModBase.Log("[Profile] 使用 Authlib 皮肤加载器");
            Skin.loader = PageLaunchLeft.skinAuth;
        }
        else
        {
            BtnEdit.Visibility = Visibility.Collapsed;
            ModBase.Log("[Profile] 使用离线皮肤加载器");
            Skin.loader = PageLaunchLeft.skinLegacy;
        }

        Skin.loader.Start(isForceRestart: true);
        TextName.Text = ModProfile.selectedProfile.Username;
        TextType.Text = (string)ModProfile.GetProfileInfo(ModProfile.selectedProfile);
    }

    #region 控制与编辑

    // 显示 / 隐藏控制
    private void ShowPanel(object sender, MouseEventArgs e)
    {
        ModAnimation.AniStart(ModAnimation.AaOpacity(PanButtons, 1d - PanButtons.Opacity, 120),
            "PageLoginProfileSkin Button");
    }

    private void HidePanel(object sender, EventArgs e)
    {
        if (BtnEdit.ContextMenu.IsOpen || PanData.IsMouseOver)
            return;
        ModAnimation.AniStart(ModAnimation.AaOpacity(PanButtons, -PanButtons.Opacity, 120),
            "PageLoginProfileSkin Button");
    }

    private void MenuAccountOptions_Closed(object sender, RoutedEventArgs e)
    {
        HidePanel(sender, e);
    }

    // 账号信息子菜单
    private void BtnEdit_Click(object sender, EventArgs e)
    {
        BtnEdit.ContextMenu.IsOpen = true;
    }

    // 修改密码
    private void BtnEditPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ModProfile.selectedProfile.Type == ModLaunch.McLoginType.Ms)
        {
            ModBase.OpenWebsite("https://account.live.com/password/Change");
        }
        else if (ModProfile.selectedProfile.Type == ModLaunch.McLoginType.Auth)
        {
            var server = ModProfile.selectedProfile.Server;
            ModBase.OpenWebsite(server.Replace("/api/yggdrasil/authserver" + (server.EndsWithF("/") ? "/" : ""),
                "/user/profile"));
        }
        else
        {
            HintService.Hint(Lang.Text("Launch.Account.ProfileSkin.PasswordUnsupported"));
        }
    }

    // 修改 ID
    private void BtnEditName_Click(object sender, RoutedEventArgs e)
    {
        ModProfile.EditProfileId();
    }

    // 选择档案
    private void ChangeProfile(object sender, EventArgs e)
    {
        ModProfile.selectedProfile = null;
        ModBase.RunInUi(() =>
        {
            ModMain.frmLaunchLeft.RefreshPage(true);
            ModMain.frmLaunchLeft.BtnLaunch.IsEnabled = false;
        });
    }

    // 刷新头像
    private void BtnSkinRefresh_Click(object sender, EventArgs e)
    {
        Skin.RefreshClick(sender, e);
    }

    // 跳转到皮肤库
    private void BtnSkinLibrary_Click(object sender, EventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Tools, FormMain.PageSubType.ToolsSkins);
    }

    #endregion
}