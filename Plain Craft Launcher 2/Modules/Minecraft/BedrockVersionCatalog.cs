using System.Text.Json.Nodes;

namespace PCL;

internal static class BedrockVersionCatalog
{
    private static readonly ModLoader.LoaderTask<string, JsonObject> Loader =
        new("BedrockVersionList", ModDownload.DlBedrockListMain);

    internal static JsonArray? Versions { get; private set; }
    internal static event Action? Changed;

    static BedrockVersionCatalog()
    {
        Loader.PreviewFinish += _ => ModBase.RunInUiWait(() =>
        {
            Versions = Loader.output?["versions"] as JsonArray;
            Changed?.Invoke();
        });
    }

    internal static void Load() => Loader.Start(isForceRestart: Loader.State == ModBase.LoadState.Failed);
}
