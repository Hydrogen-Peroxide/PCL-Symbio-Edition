using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceSetupPage : IRefreshable
{
    public PageBedrockInstanceSetupPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        Unloaded += (_, _) => SaveLoader();
        BtnRename.Click += BtnRename_Click;
        BtnOpenFolder.Click += BtnOpenFolder_Click;
        BtnDelete.Click += BtnDelete_Click;
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
        if (string.IsNullOrWhiteSpace(version))
        {
            TextName.Text = "";
            LabVersion.Text = "-";
            LabChannel.Text = "-";
            LabPath.Text = Lang.Text("Bedrock.Instance.None");
            RadioVanilla.Checked = true;
            LabLoaderTarget.Text = "";
            BtnRename.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
            BtnDelete.IsEnabled = false;
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
        LabPath.Text = ModBedrockInstance.GetInstanceFolder(versionsDirectory, version);
        BtnRename.IsEnabled = true;
        BtnOpenFolder.IsEnabled = true;
        BtnDelete.IsEnabled = true;

        var loader = ModBedrockLoader.GetLoader(version);
        RadioVanilla.Checked = loader == ModBedrockLoader.Vanilla;
        RadioLeviLamina.Checked = loader == ModBedrockLoader.LeviLamina;
        RadioBLoader.Checked = loader == ModBedrockLoader.BLoader;
        LabLoaderTarget.Text = Lang.Text("Bedrock.Instance.Loader", version);
    }

    private void SaveLoader()
    {
        var version = CurrentVersion;
        if (version is null) return;
        var loader = RadioLeviLamina.Checked
            ? ModBedrockLoader.LeviLamina
            : RadioBLoader.Checked
                ? ModBedrockLoader.BLoader
                : ModBedrockLoader.Vanilla;
        if (ModBedrockLoader.GetLoader(version) == loader) return;
        ModBedrockLoader.SetLoader(version, loader);
        HintService.Hint(Lang.Text("Bedrock.Instance.LoaderSaved", version), HintType.Success);
    }

    private void BtnRename_Click(object sender, MouseButtonEventArgs e)
    {
        var version = CurrentVersion;
        if (version is null) return;
        var name = TextName.Text.Trim();
        ModBedrockInstance.SetDisplayName(ModBedrockInstance.VersionsDirectory, version, name);
        ModMain.frmMain.PageNameRefresh();
        ModMain.frmBedrockSelect?.RefreshInstances();
        ModMain.frmBedrockInstanceLeft?.Refresh(FormMain.PageSubType.VersionOverall);
        HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Renamed",
            string.IsNullOrWhiteSpace(name) ? version : name), HintType.Success);
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        var version = CurrentVersion;
        if (version is null) return;
        var path = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
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

    private void BtnDelete_Click(object sender, MouseButtonEventArgs e)
    {
        var version = CurrentVersion;
        if (version is null) return;
        var name = ModBedrockInstance.GetDisplayName(ModBedrockInstance.VersionsDirectory, version);
        if (ModMain.MyMsgBox(Lang.Text("Bedrock.Instance.Settings.Delete.Confirm", name),
                Lang.Text("Bedrock.Instance.Settings.Delete.Title"),
                button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        var path = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Delete.Failed", ex.Message), HintType.Error);
            return;
        }

        Config.Launch.BedrockSelectedVersion = "";
        ModBedrockInstance.SelectedVersionId = "";
        ModMain.frmBedrockSelect?.RefreshInstances();
        ModMain.frmBedrockLeft?.RefreshStatus();
        ModMain.frmBedrockRight?.RefreshPage();
        HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Deleted", name), HintType.Success);
    }
}