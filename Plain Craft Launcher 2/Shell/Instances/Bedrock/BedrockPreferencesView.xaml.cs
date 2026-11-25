using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockPreferencesView : IRefreshable
{
    public BedrockPreferencesView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnRename.Click += BtnRename_Click;
        BtnOpenFolder.Click += (_, _) => OpenFolder(CurrentVersion is null
            ? ""
            : ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, CurrentVersion));
        BtnOpenData.Click += (_, _) => OpenFolder(ModBedrockInstance.VersionsDirectory);
        BtnGotoActions.Click += (_, _) => ModMain.frmBedrockInstanceLeft?.PageChange(FormMain.PageSubType.BedrockActions);
    }

    public void Refresh()
    {
        RefreshData();
    }

    private static string? CurrentVersion
    {
        get
        {
            var version = Config.Launch.BedrockSelectedVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
    }

    public void RefreshData()
    {
        var version = CurrentVersion ?? ModBedrockInstance.SelectedVersionId;
        var dataFolder = ModBedrockInstance.VersionsDirectory;
        LabDataFolder.Text = BedrockInstanceWorkspace.FoldPath(dataFolder);
        LabDataFolder.ToolTip = dataFolder;
        BtnOpenData.IsEnabled = Directory.Exists(dataFolder);

        if (string.IsNullOrWhiteSpace(version))
        {
            TextName.Text = "";
            LabVersion.Text = "-";
            LabChannel.Text = "-";
            LabPath.Text = Lang.Text("Bedrock.Instance.None");
            LabPath.ToolTip = null;

            BtnRename.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
            return;
        }

        var versionsDirectory = ModBedrockInstance.VersionsDirectory;
        var channel = ModBedrockInstance.GetChannel(version);
        var channelText = channel switch
        {
            "preview" => Lang.Text("Download.Bedrock.Channel.Preview"),
            "beta" => Lang.Text("Download.Bedrock.Channel.Beta"),
            _ => Lang.Text("Download.Bedrock.Channel.Release")
        };

        TextName.Text = ModBedrockInstance.GetDisplayName(versionsDirectory, version);
        LabVersion.Text = version;
        LabChannel.Text = channelText;
        var instancePath = ModBedrockInstance.GetInstanceFolder(versionsDirectory, version);
        LabPath.Text = BedrockInstanceWorkspace.FoldPath(instancePath);
        LabPath.ToolTip = instancePath;
        BtnRename.IsEnabled = true;
        BtnOpenFolder.IsEnabled = true;
    }

    private void BtnRename_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var version = CurrentVersion;
        if (version is null) return;
        var name = TextName.Text.Trim();
        ModBedrockInstance.SetDisplayName(ModBedrockInstance.VersionsDirectory, version, name);
        ModMain.frmMain.PageNameRefresh();
        BedrockLaunchSession.Current.NotifyLibraryChanged();
        ModMain.frmBedrockInstanceLeft?.Refresh(FormMain.PageSubType.VersionOverall);
        HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Renamed",
            string.IsNullOrWhiteSpace(name) ? version : name), HintType.Success);
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

}
