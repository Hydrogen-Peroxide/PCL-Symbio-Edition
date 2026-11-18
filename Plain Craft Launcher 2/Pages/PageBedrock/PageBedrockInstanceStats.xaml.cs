using System.IO;
using System.Windows;
using PCL.Core.App;

namespace PCL;

public partial class PageBedrockInstanceStats : IRefreshable
{
    public PageBedrockInstanceStats()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnRefresh.Click += (_, _) => RefreshData();
    }

    public void Refresh() => RefreshData();

    public void RefreshData()
    {
        var version = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            LabSize.Text = LabFiles.Text = LabWorlds.Text = LabCreated.Text = LabModified.Text = "-";
            BtnRefresh.IsEnabled = false;
            return;
        }

        var root = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
        try
        {
            var info = new DirectoryInfo(root);
            if (!info.Exists)
            {
                LabSize.Text = LabFiles.Text = LabWorlds.Text = LabCreated.Text = LabModified.Text = "-";
                BtnRefresh.IsEnabled = false;
                return;
            }

            long bytes = 0;
            var files = 0;
            foreach (var file in info.EnumerateFiles("*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0
                     }))
            {
                bytes += file.Length;
                files++;
            }

            LabSize.Text = bytes > 1024L * 1024 * 1024
                ? (bytes / 1024d / 1024 / 1024).ToString("0.00") + " GB"
                : (bytes / 1024d / 1024).ToString("0.0") + " MB";
            LabFiles.Text = files.ToString();
            var worlds = Path.Combine(root, "minecraftWorlds");
            LabWorlds.Text = Directory.Exists(worlds) ? Directory.GetDirectories(worlds).Length.ToString() : "0";
            LabCreated.Text = info.CreationTime.ToString("yyyy-MM-dd HH:mm");
            LabModified.Text = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            BtnRefresh.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 统计实例信息失败：{ex.Message}", ModBase.LogLevel.Debug);
            LabSize.Text = LabFiles.Text = LabWorlds.Text = LabCreated.Text = LabModified.Text = "-";
            BtnRefresh.IsEnabled = false;
        }
    }
}
