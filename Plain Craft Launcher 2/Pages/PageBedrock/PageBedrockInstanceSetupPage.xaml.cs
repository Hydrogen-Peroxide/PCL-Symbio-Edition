using System.Windows;
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
    }

    public void Refresh()
    {
        RefreshData();
    }

    public void RefreshData()
    {
        var version = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = ModBedrockInstance.SelectedVersionId;

        CheckShowLaunchLog.Checked = States.UI.ShowLaunchLog;

        if (string.IsNullOrWhiteSpace(version))
        {
            RadioVanilla.Checked = true;
            LabLoaderTarget.Text = Lang.Text("Bedrock.Instance.None");
            return;
        }

        var loader = ModBedrockLoader.GetLoader(version);
        RadioVanilla.Checked = loader == ModBedrockLoader.Vanilla;
        RadioLeviLamina.Checked = loader == ModBedrockLoader.LeviLamina;
        RadioBLoader.Checked = loader == ModBedrockLoader.BLoader;
        LabLoaderTarget.Text = Lang.Text("Bedrock.Instance.Loader", version);
    }

    private void SaveLoader()
    {
        var version = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(version)) return;
        var loader = RadioLeviLamina.Checked
            ? ModBedrockLoader.LeviLamina
            : RadioBLoader.Checked
                ? ModBedrockLoader.BLoader
                : ModBedrockLoader.Vanilla;
        if (ModBedrockLoader.GetLoader(version) == loader) return;
        ModBedrockLoader.SetLoader(version, loader);
        HintService.Hint(Lang.Text("Bedrock.Instance.LoaderSaved", version), HintType.Success);
    }

    private void ShowLaunchLog_Change(object sender, bool user)
    {
        States.UI.ShowLaunchLog = CheckShowLaunchLog.Checked == true;
    }
}