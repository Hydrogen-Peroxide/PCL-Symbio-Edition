using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockWorldsView : IRefreshable
{
    public BedrockWorldsView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnOpenSaves.Click += (_, _) => OpenFolder(SavesDirectory);
        BtnRefresh.Click += (_, _) => RefreshData();
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

    private static string SavesDirectory
    {
        get
        {
            var version = VersionId;
            if (version is null) return "";
            var root = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
            var nested = Path.Combine(root, "minecraftWorlds");
            return Directory.Exists(nested) ? nested : Path.Combine(root, "data", "minecraftWorlds");
        }
    }

    public void RefreshData()
    {
        PanWorlds.Children.Clear();
        var path = SavesDirectory;
        LabSavesPath.Text = path.Length == 0 ? "" : "📁 " + path;
        BtnOpenSaves.IsEnabled = path.Length > 0 && Directory.Exists(path);

        var worlds = new List<string>();
        try
        {
            if (path.Length > 0 && Directory.Exists(path)) worlds.AddRange(Directory.GetDirectories(path));
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取存档失败：{ex.Message}", ModBase.LogLevel.Debug);
        }

        CardSaves.Title = Lang.Text("Bedrock.Instance.Saves.Title") + "  (" + worlds.Count + ")";
        if (worlds.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Lang.Text("Bedrock.Instance.Saves.Empty"),
                FontSize = 12.5d,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
            PanWorlds.Children.Add(empty);
            return;
        }

        foreach (var world in worlds)
        {
            var folder = Path.GetFileName(world);
            var item = new SelectionRow
            {
                Title = ReadWorldName(world) ?? folder,
                Info = folder,
                SvgIcon = "lucide/globe",
                Type = SelectionRow.CheckType.Clickable,
                Height = 44
            };
            var target = world;
            item.Click += (_, _) => OpenFolder(target);
            PanWorlds.Children.Add(item);
        }
    }

    private static string? ReadWorldName(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "levelname.txt");
            if (!File.Exists(file)) return null;
            var name = File.ReadAllText(file).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
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
