using PCL.Core.App;

namespace PCL;

public partial class BedrockStatisticsView : IRefreshable
{
    private CancellationTokenSource? reading;
    public BedrockStatisticsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { reading?.Cancel(); reading = null; };
        BtnRefresh.Click += (_, _) => Refresh();
    }

    public async void Refresh()
    {
        reading?.Cancel();
        using var request = new CancellationTokenSource();
        reading = request;
        BedrockInstanceStatistics? result = null;
        try
        {
            var id = Config.Launch.BedrockSelectedVersion;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var path = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, id);
                result = await Task.Run(() => BedrockInstanceStatistics.Read(path, request.Token), request.Token);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return; }
        catch (Exception ex) { ModBase.Log(ex, "读取基岩版实例统计失败", ModBase.LogLevel.Debug); }
        finally { if (ReferenceEquals(reading, request)) reading = null; }
        if (request.IsCancellationRequested) return;
        LabSize.Text = result is null ? "-" : result.Bytes > (1L << 30)
            ? $"{result.Bytes / (double)(1L << 30):0.00} GB" : $"{result.Bytes / (double)(1L << 20):0.0} MB";
        LabFiles.Text = result?.Files.ToString() ?? "-";
        LabWorlds.Text = result?.Worlds.ToString() ?? "-";
        LabCreated.Text = result?.Created.ToString("yyyy-MM-dd HH:mm") ?? "-";
        LabModified.Text = result?.Modified.ToString("yyyy-MM-dd HH:mm") ?? "-";
        BtnRefresh.IsEnabled = result is not null;
    }
}
