using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockOverviewView : IRefreshable
{
    public BedrockOverviewView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnLaunch.Click += BtnLaunch_Click;
        BtnOpenFolder.Click += (_, _) => OpenFolder(CurrentInstancePath);
        BtnOpenSaves.Click += (_, _) => OpenFolder(CurrentSavesPath);
        BtnOpenPackages.Click += (_, _) => OpenFolder(CurrentPackagePath);
    }

    public void Refresh()
    {
        RefreshData();
    }

    private static string? CurrentVersion
    {
        get
        {
            var versionId = Config.Launch.BedrockSelectedVersion;
            if (string.IsNullOrWhiteSpace(versionId)) versionId = ModBedrockInstance.SelectedVersionId;
            return string.IsNullOrWhiteSpace(versionId) ? null : versionId;
        }
    }

    private static string CurrentInstancePath => CurrentVersion is null
        ? ""
        : ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, CurrentVersion);

    private static string CurrentSavesPath
    {
        get
        {
            var root = CurrentInstancePath;
            if (string.IsNullOrEmpty(root)) return "";
            var nested = Path.Combine(root, "minecraftWorlds");
            return Directory.Exists(nested) ? nested : Path.Combine(root, "data", "minecraftWorlds");
        }
    }

    private static string CurrentPackagePath => CurrentVersion is null
        ? ""
        : Path.Combine(ModBedrockInstance.VersionsDirectory, CurrentVersion);

    public void RefreshData()
    {
        var versionId = CurrentVersion;
        if (versionId is null)
        {
            LabInstanceName.Text = Lang.Text("Bedrock.Instance.None");
            LabInstanceSub.Text = "";
            LabVersion.Text = "-";
            LabChannel.Text = "-";
            LabMethod.Text = "-";
            LabPath.Text = "-";
            LabDataFolder.Text = "-";
            LabEmpty.Visibility = Visibility.Visible;
            BtnLaunch.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
            BtnOpenSaves.IsEnabled = false;
            BtnOpenPackages.IsEnabled = false;
            ImgLogo.Source = null;
            return;
        }

        var versionsDirectory = ModBedrockInstance.VersionsDirectory;
        var channel = ModBedrockInstance.GetChannel(versionId);
        LabEmpty.Visibility = Visibility.Collapsed;
        BtnLaunch.IsEnabled = true;
        BtnOpenFolder.IsEnabled = true;
        BtnOpenSaves.IsEnabled = true;
        BtnOpenPackages.IsEnabled = true;

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

        var instancePath = ModBedrockInstance.GetInstanceFolder(versionsDirectory, versionId);
        LabMethod.Text = ModBedrockGdk.IsGdkBuild(instancePath)
            ? Lang.Text("Bedrock.Instance.Method.Gdk")
            : Lang.Text("Bedrock.Instance.Method.Uwp");
        SetFoldablePath(LabPath, instancePath);
        SetFoldablePath(LabDataFolder, versionsDirectory);

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

    /// <summary>路径按固定长度折叠显示，完整路径放进悬停提示。</summary>
    private static void SetFoldablePath(System.Windows.Controls.TextBlock target, string path)
    {
        target.Text = BedrockInstanceWorkspace.FoldPath(path);
        target.ToolTip = path;
    }

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 打开文件夹失败：{ex.Message}", ModBase.LogLevel.Feedback);
        }
    }

    private void BtnLaunch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        BedrockLaunchSession.Current.RequestLaunch();
    }
}
