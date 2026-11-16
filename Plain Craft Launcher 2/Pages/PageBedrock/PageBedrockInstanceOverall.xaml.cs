using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceOverall : IRefreshable
{
    public PageBedrockInstanceOverall()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnLaunch.Click += BtnLaunch_Click;
        BtnOpenFolder.Click += BtnOpenFolder_Click;
    }

    public void Refresh()
    {
        RefreshData();
    }

    public void RefreshData()
    {
        var versionId = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(versionId))
            versionId = ModBedrockInstance.SelectedVersionId;

        if (string.IsNullOrWhiteSpace(versionId))
        {
            LabInstanceName.Text = Lang.Text("Bedrock.Instance.None");
            LabInstanceSub.Text = "";
            LabVersion.Text = "-";
            LabChannel.Text = "-";
            LabPath.Text = "-";
            LabEmpty.Visibility = Visibility.Visible;
            BtnLaunch.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
            ImgLogo.Source = null;
            return;
        }

        var versionsDirectory = ModBedrockInstance.VersionsDirectory;
        var channel = ModBedrockInstance.GetChannel(versionId);
        LabEmpty.Visibility = Visibility.Collapsed;
        BtnLaunch.IsEnabled = true;
        BtnOpenFolder.IsEnabled = true;

        var channelText = channel switch
        {
            "preview" => Lang.Text("Download.Bedrock.Channel.Preview"),
            "beta" => Lang.Text("Download.Bedrock.Channel.Beta"),
            _ => Lang.Text("Download.Bedrock.Channel.Release")
        };
        LabInstanceName.Text = ModBedrockInstance.GetDisplayName(versionsDirectory, versionId);
        LabInstanceSub.Text = Lang.Text("Bedrock.Status.Version", versionId) + " · " + channelText;
        LabVersion.Text = versionId;
        LabChannel.Text = channelText;
        LabPath.Text = ModBedrockInstance.GetInstanceFolder(versionsDirectory, versionId);
        var icon = channel switch
        {
            "preview" => "CommandBlock.png",
            "beta" => "GoldBlock.png",
            _ => "Grass.png"
        };
        try
        {
            ImgLogo.Source = new BitmapImage(new Uri(ModBase.pathImage + "Blocks/" + icon));
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 加载实例图标失败：{ex.Message}", ModBase.LogLevel.Debug);
        }
    }

    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmBedrockRight?.BtnLaunch_Click(this, null!);
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var path = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory,
            Config.Launch.BedrockSelectedVersion);
        if (!Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 打开实例文件夹失败：{ex.Message}", ModBase.LogLevel.Feedback);
        }
    }
}