using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockInstanceActionsView : IRefreshable
{
    public BedrockInstanceActionsView()
    {
        InitializeComponent();
        BtnOpenFolder.Click += BtnOpenFolder_Click;
        BtnCopy.Click += BtnCopy_Click;
        BtnDelete.Click += BtnDelete_Click;
        Loaded += (_, _) => RefreshData();
    }

    public void Refresh()
    {
        RefreshData();
    }

    private static string? VersionId
    {
        get
        {
            var version = Config.Launch.BedrockSelectedVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
    }

    public void RefreshData()
    {
        var enabled = VersionId is not null;
        BtnOpenFolder.IsEnabled = enabled;
        BtnCopy.IsEnabled = enabled;
        BtnDelete.IsEnabled = enabled;
    }

    private void BtnOpenFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var version = VersionId;
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

    private void BtnCopy_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var version = VersionId;
        if (version is null) return;
        var versionsDirectory = ModBedrockInstance.VersionsDirectory;
        try
        {
            var newId = ModBedrockInstance.CreateCopyId(versionsDirectory, version);
            ModBedrockInstance.CopyPortableInstance(versionsDirectory, version, newId);
            ModBedrockInstance.SetDisplayName(versionsDirectory, newId,
                ModBedrockInstance.GetDisplayName(versionsDirectory, version) + " 副本");
            HintService.Hint(Lang.Text("Bedrock.Instance.Actions.Copied", newId), HintType.Success);
        }
        catch (Exception ex)
        {
            HintService.Hint(Lang.Text("Bedrock.Instance.Actions.CopyFailed", ex.Message), HintType.Error);
        }
    }

    private void BtnDelete_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var version = VersionId;
        if (version is null) return;
        var versionsDirectory = ModBedrockInstance.VersionsDirectory;
        var name = ModBedrockInstance.GetDisplayName(versionsDirectory, version);
        if (ModMain.MyMsgBox(Lang.Text("Bedrock.Instance.Settings.Delete.Confirm", name),
                Lang.Text("Bedrock.Instance.Settings.Delete.Title"),
                button2: Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        try
        {
            var folder = ModBedrockInstance.GetInstanceFolder(versionsDirectory, version);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception ex)
        {
            HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Delete.Failed", ex.Message), HintType.Error);
            return;
        }

        Config.Launch.BedrockSelectedVersion = "";
        ModBedrockInstance.SelectedVersionId = "";
        BedrockLaunchSession.Current.NotifyLibraryChanged();
        BedrockLaunchSession.Current.DetectBedrockInstallation();
        ModMain.frmMain.RefreshShell();
        HintService.Hint(Lang.Text("Bedrock.Instance.Settings.Deleted", name), HintType.Success);
        ModMain.frmMain.PageBack();
    }
}
