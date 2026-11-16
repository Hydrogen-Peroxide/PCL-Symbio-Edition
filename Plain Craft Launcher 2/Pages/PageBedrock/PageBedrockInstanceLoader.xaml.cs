using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceLoader : IRefreshable
{
    public PageBedrockInstanceLoader()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        Unloaded += (_, _) => SaveLoader();
    }

    public void Refresh() => RefreshData();

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
        var version = VersionId;
        if (version is null)
        {
            RadioVanilla.Checked = true;
            LabTarget.Text = Lang.Text("Bedrock.Instance.None");
            return;
        }

        var loader = ModBedrockLoader.GetLoader(version);
        RadioVanilla.Checked = loader == ModBedrockLoader.Vanilla;
        RadioLeviLamina.Checked = loader == ModBedrockLoader.LeviLamina;
        RadioBLoader.Checked = loader == ModBedrockLoader.BLoader;
        LabTarget.Text = Lang.Text("Bedrock.Instance.Loader", version);
    }

    private void SaveLoader()
    {
        var version = VersionId;
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
}