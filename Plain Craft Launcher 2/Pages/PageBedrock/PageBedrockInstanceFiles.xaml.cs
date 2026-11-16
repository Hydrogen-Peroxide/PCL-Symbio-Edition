using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockInstanceFiles : IRefreshable
{
    /// <summary>0 = 模组管理，1 = 截图，2 = 服务器。</summary>
    public int Mode { get; set; }

    public PageBedrockInstanceFiles()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
        BtnRefresh.Click += (_, _) => RefreshData();
        BtnOpen.Click += (_, _) => OpenFolder(TargetDirectory);
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

    private static string InstanceDirectory
    {
        get
        {
            var version = VersionId;
            return version is null
                ? ""
                : ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
        }
    }

    private string TargetDirectory
    {
        get
        {
            var root = InstanceDirectory;
            if (root.Length == 0) return "";
            return Mode switch
            {
                1 => FirstExisting(Path.Combine(root, "Screenshots"), Path.Combine(root, "screenshots"),
                    Path.Combine(root, "..", "Screenshots")),
                _ => root
            };
        }
    }

    private static string FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
            if (Directory.Exists(path))
                return Path.GetFullPath(path);
        return paths.Length > 0 ? Path.GetFullPath(paths[0]) : "";
    }

    public void RefreshData()
    {
        PanList.Children.Clear();
        var root = InstanceDirectory;
        if (root.Length == 0)
        {
            LabPath.Text = Lang.Text("Bedrock.Instance.None");
            BtnOpen.IsEnabled = false;
            return;
        }

        var target = TargetDirectory;
        LabPath.Text = "📁 " + target;
        BtnOpen.IsEnabled = Directory.Exists(target);

        var entries = new List<(string Path, string Title, string Info, string Icon)>();
        try
        {
            switch (Mode)
            {
                case 0:
                    foreach (var dir in new[]
                             {
                                 "behavior_packs", "resource_packs", "development_behavior_packs",
                                 "development_resource_packs", "skin_packs", "world_templates"
                             })
                    {
                        var path = Path.Combine(root, dir);
                        if (Directory.Exists(path))
                            entries.Add((path, dir, DirInfo(path), "lucide/puzzle"));
                    }

                    foreach (var file in SafeFiles(root))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".mcpack" or ".mcaddon" or ".mcworld" or ".mctemplate")
                            entries.Add((file, Path.GetFileNameWithoutExtension(file), ext, "lucide/package"));
                    }

                    break;
                case 1:
                    foreach (var file in SafeFiles(target))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg")
                            entries.Add((file, Path.GetFileName(file), FileInfo(file), "lucide/image"));
                    }

                    break;
                default:
                    var servers = Path.Combine(root, "external_servers.txt");
                    if (!File.Exists(servers))
                        servers = Path.Combine(root, "..", "external_servers.txt");
                    if (File.Exists(servers))
                    {
                        foreach (var line in File.ReadAllLines(servers))
                        {
                            var text = line.Trim();
                            if (text.Length == 0 || text.StartsWith("#")) continue;
                            var parts = text.Split('|');
                            var name = parts.Length > 0 ? parts[0] : text;
                            var address = parts.Length > 1
                                ? parts[1] + (parts.Length > 2 ? ":" + parts[2] : "")
                                : "";
                            entries.Add((servers, name, address, "lucide/server"));
                        }
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 读取列表失败：{ex.Message}", ModBase.LogLevel.Debug);
        }

        if (entries.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Lang.Text(Mode switch
                {
                    0 => "Bedrock.Instance.Files.EmptyMods",
                    1 => "Bedrock.Instance.Files.EmptyScreenshots",
                    _ => "Bedrock.Instance.Files.EmptyServers"
                }),
                FontSize = 12.5d,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            PanList.Children.Add(empty);
            return;
        }

        foreach (var entry in entries)
        {
            var item = new MyListItem
            {
                Title = entry.Title,
                Info = entry.Info,
                SvgIcon = entry.Icon,
                Type = MyListItem.CheckType.Clickable,
                Height = 44
            };
            var path = entry.Path;
            item.Click += (_, _) =>
            {
                if (Directory.Exists(path)) OpenFolder(path);
                else if (File.Exists(path)) OpenFolder(Path.GetDirectoryName(path) ?? "");
            };
            PanList.Children.Add(item);
        }
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetFiles(path) : [];
        }
        catch
        {
            return [];
        }
    }

    private static string DirInfo(string path)
    {
        try
        {
            return Lang.Text("Bedrock.Instance.Files.Count", Directory.GetFileSystemEntries(path).Length);
        }
        catch
        {
            return "";
        }
    }

    private static string FileInfo(string path)
    {
        try
        {
            return new FileInfo(path).Length / 1024 + " KB";
        }
        catch
        {
            return "";
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