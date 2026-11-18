using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     基岩版 DLL 模组下载页：列出 Lip 注册表中的 LeviLamina 插件，并用 lip 安装到当前实例。
///     页面结构与 Java 版下载页一致：搜索框 + 来源/排序筛选卡 + 列表。<br/>
///     安装支持排队：可以连续点多个插件，按队列依次安装（lip 的缓存不是并发安全的），
///     每个条目会显示自己的下载进度；已经装进实例的插件会标出当前版本，
///     和当前游戏版本不兼容的插件会提前标注并禁用安装按钮。
/// </summary>
public partial class PageDownloadBedrockLip : IRefreshable
{
    private readonly List<ModBedrockLipRegistry.LipPackage> _packages = [];
    private readonly Queue<ModBedrockLipRegistry.LipPackage> _queue = new();
    private readonly HashSet<string> _enqueued = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (MyListItem Item, MyIconButton Button, ModBedrockLipRegistry.LipPackage Package)>
        _controls = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string Stage, double Percent)> _progress =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string Text, bool Success)> _finished =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>实例里已经装好的插件（包标识 → 版本），用来把条目显示成「已安装 x.y.z」。</summary>
    private readonly Dictionary<string, string> _installed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前实例装不了的插件（包标识 → 原因），例如要求 LeviLamina 26.51/26.60 的插件。</summary>
    private readonly Dictionary<string, string> _incompatible = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _queueLock = new();
    private DispatcherTimer? _debounce;
    private bool _workerRunning;
    private bool _loaded;
    private int _generation;

    public PageDownloadBedrockLip()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => ScheduleReload();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            Reload();
        };
    }

    public void Refresh()
    {
        if (_loaded) Reload();
    }

    private int SortMode => ComboSort?.SelectedItem is MyComboBoxItem { Tag: not null } item
        ? (int)ModBase.Val(item.Tag)
        : 0;

    private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) Reload();
    }

    /// <summary>当前选中的基岩版实例标识（形如 26.40）。</summary>
    private static string? CurrentInstanceVersion()
    {
        var version = Config.Launch.BedrockSelectedVersion;
        if (string.IsNullOrWhiteSpace(version)) version = PageBedrockInstanceLeft.CurrentVersion;
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    /// <summary>当前实例的解包目录；没选实例或目录不存在时返回 null。</summary>
    private static string? ResolveInstanceDirectory()
    {
        var version = CurrentInstanceVersion();
        if (version is null) return null;
        var instanceDir = ModBedrockInstance.GetInstanceFolder(ModBedrockInstance.VersionsDirectory, version);
        return Directory.Exists(instanceDir) ? instanceDir : null;
    }

    /// <summary>
    ///     标出哪些插件在当前实例里装不了（版本要求的 LeviLamina 和实例对不上）。
    ///     还没装 LeviLamina 时不判断，交给安装流程处理。
    /// </summary>
    private static Dictionary<string, string> BuildCompatibilityMap(
        IEnumerable<ModBedrockLipRegistry.LipPackage> packages, string? gameVersion, string? instanceDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (instanceDir is null || string.IsNullOrWhiteSpace(gameVersion)) return result;

        var loaderVersion = ModBedrockLeviLamina.GetRecordedVersion(instanceDir);
        if (string.IsNullOrWhiteSpace(loaderVersion)) return result;

        foreach (var package in packages)
            if (!ModBedrockLipRegistry.TryResolveVersion(package, gameVersion!, loaderVersion!, out _, out var reason))
                result[package.Id] = reason;

        return result;
    }

    /// <summary>输入时防抖，避免每敲一个字都发一次请求、并把最后一次输入漏掉。</summary>
    private void ScheduleReload()
    {
        if (!_loaded) return;
        _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick -= Debounce_Tick;
        _debounce.Tick += Debounce_Tick;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Debounce_Tick(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Reload();
    }

    private void SetStatus(string text)
    {
        if (Dispatcher.CheckAccess()) LabStatus.Text = text;
        else ModBase.RunInUi(() => LabStatus.Text = text);
    }

    private void Reload()
    {
        var keyword = SearchBox.Text ?? "";
        var sort = SortMode;
        var generation = ++_generation;
        var instanceDir = ResolveInstanceDirectory();
        var instanceVersion = CurrentInstanceVersion();
        SetStatus(Lang.Text("Download.BedrockLip.Loading"));

        Task.Run(() =>
        {
            try
            {
                var items = ModBedrockLipRegistry.Search(keyword);
                ModBase.Log($"[BedrockLip] 搜索「{keyword}」→ {items.Count} 个", ModBase.LogLevel.Debug);
                items = sort switch
                {
                    1 => items.OrderByDescending(pkg => pkg.Stars).ThenBy(pkg => pkg.Name).ToList(),
                    2 => items.OrderByDescending(pkg => pkg.UpdatedAt ?? DateTime.MinValue)
                        .ThenBy(pkg => pkg.Name).ToList(),
                    _ => items
                };

                var installed = ModBedrockLipRegistry.GetInstalledVersions(instanceDir);
                var incompatible = BuildCompatibilityMap(items, instanceVersion, instanceDir);

                // 只渲染最后一次搜索的结果，避免慢请求把新结果顶掉
                if (generation != _generation) return;

                ModBase.RunInUi(() =>
                {
                    if (generation != _generation) return;
                    PanList.Children.Clear();
                    _controls.Clear();
                    _packages.Clear();
                    _packages.AddRange(items);
                    // 列表整体重建过，上次安装的结果交给安装清单（_installed）体现
                    _finished.Clear();
                    ApplyInstalled(installed);
                    ApplyIncompatible(incompatible);
                    if (items.Count == 0)
                    {
                        PanList.Children.Add(CreateHint(Lang.Text("Download.BedrockLip.Empty")));
                        SetStatus("");
                        return;
                    }

                    foreach (var package in items) PanList.Children.Add(BuildItem(package));
                    RefreshQueueStatus();
                });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Bedrock] 读取 Lip 插件列表失败", ModBase.LogLevel.Hint);
                ModBase.RunInUi(() =>
                {
                    if (generation != _generation) return;
                    PanList.Children.Clear();
                    _controls.Clear();
                    PanList.Children.Add(CreateHint(Lang.Text("Download.BedrockLip.Failed", ex.Message)));
                    SetStatus("");
                });
            }
        });
    }

    /// <summary>把「已安装的插件」写进缓存并刷新所有条目（只在 UI 线程调用）。</summary>
    private void ApplyInstalled(Dictionary<string, string> installed)
    {
        _installed.Clear();
        foreach (var pair in installed) _installed[pair.Key] = pair.Value;
        foreach (var id in _controls.Keys.ToList()) ApplyItemState(id);
    }

    /// <summary>把「当前实例装不了的插件」写进缓存并刷新所有条目（只在 UI 线程调用）。</summary>
    private void ApplyIncompatible(Dictionary<string, string> incompatible)
    {
        _incompatible.Clear();
        foreach (var pair in incompatible) _incompatible[pair.Key] = pair.Value;
        foreach (var id in _controls.Keys.ToList()) ApplyItemState(id);
    }

    /// <summary>条目信息行：版本 + 适用（依赖的 LeviLamina 约束）+ 星标 + 更新时间。</summary>
    private static string BuildInfoText(ModBedrockLipRegistry.LipPackage package)
    {
        var updated = package.UpdatedAt?.ToString("yyyy-MM-dd") ?? "-";
        var constraint = ModBedrockLipRegistry.GetLoaderConstraint(package);
        if (string.IsNullOrWhiteSpace(constraint))
            return Lang.Text("Download.BedrockLip.ItemInfo", package.Versions[0], package.Stars, updated);

        var formatted = constraint.Replace(">=", "≥").Replace("<=", "≤").Trim();
        return Lang.Text("Download.BedrockLip.ItemInfoCompat", package.Versions[0], formatted, package.Stars, updated);
    }

    private MyListItem BuildItem(ModBedrockLipRegistry.LipPackage package)
    {
        var item = new MyListItem
        {
            Title = package.Name,
            Info = BuildInfoText(package),
            ToolTip = package.Id + "\n" + package.Description,
            Type = MyListItem.CheckType.Clickable,
            Height = 52,
            Tag = package,
            SvgIcon = string.IsNullOrWhiteSpace(package.AvatarUrl) ? "lucide/package" : "",
            Logo = package.AvatarUrl ?? ""
        };

        var install = new MyIconButton
        {
            SvgIcon = "lucide/download",
            LogoScale = 0.9,
            ToolTip = Lang.Text("Download.BedrockLip.Install")
        };
        install.Click += (_, _) => EnqueueInstall(package);
        item.Buttons = [install];

        _controls[package.Id] = (item, install, package);
        ApplyItemState(package.Id);
        return item;
    }

    /// <summary>把队列 / 进度 / 结果 / 兼容性状态画到条目上（只在 UI 线程调用）。</summary>
    private void ApplyItemState(string packageId)
    {
        if (!_controls.TryGetValue(packageId, out var control)) return;
        var (item, button, package) = control;

        if (_progress.TryGetValue(packageId, out var progress))
        {
            button.IsEnabled = false;
            button.SvgIcon = "lucide/refresh-cw";
            button.ToolTip = progress.Stage;
            item.Info = progress.Stage;
            return;
        }

        if (_finished.TryGetValue(packageId, out var finished))
        {
            // 失败时保持可点击，用户可以直接重试
            button.IsEnabled = !finished.Success;
            button.SvgIcon = finished.Success ? "lucide/circle-check" : "lucide/triangle-alert";
            button.ToolTip = finished.Text;
            item.Info = finished.Text;
            return;
        }

        if (_installed.TryGetValue(ModBedrockLipRegistry.NormalizePackageId(packageId), out var installedVersion))
        {
            button.IsEnabled = true;
            button.SvgIcon = "lucide/circle-check";
            button.ToolTip = Lang.Text("Download.BedrockLip.Reinstall");
            item.Info = Lang.Text("Download.BedrockLip.InstalledVersion", installedVersion);
            return;
        }

        if (_incompatible.TryGetValue(ModBedrockLipRegistry.NormalizePackageId(packageId), out var reason))
        {
            var text = Lang.Text("Download.BedrockLip.Incompatible", reason);
            button.IsEnabled = false;
            button.SvgIcon = "lucide/triangle-alert";
            button.ToolTip = text;
            item.Info = text;
            return;
        }

        button.IsEnabled = true;
        button.SvgIcon = "lucide/download";
        button.ToolTip = Lang.Text("Download.BedrockLip.Install");
        item.Info = BuildInfoText(package);
    }

    private void EnqueueInstall(ModBedrockLipRegistry.LipPackage package)
    {
        if (!_enqueued.Add(package.Id)) return; // 已在队列或正在安装

        var version = CurrentInstanceVersion();
        var instanceDir = ResolveInstanceDirectory();
        if (version is null || instanceDir is null)
        {
            _enqueued.Remove(package.Id);
            HintService.Hint(Lang.Text("Download.BedrockLip.NoInstance"), HintType.Error);
            return;
        }

        if (ModBedrockLoader.GetLoader(version) != ModBedrockLoader.LeviLamina)
            HintService.Hint(Lang.Text("Download.BedrockLip.NeedLoader"), HintType.Warning);

        lock (_queueLock) _queue.Enqueue(package);
        _finished.Remove(package.Id);
        _progress[package.Id] = (Lang.Text("Download.BedrockLip.Queued"), 0.02d);
        ApplyItemState(package.Id);
        RefreshQueueStatus();
        EnsureWorker();
    }

    private void EnsureWorker()
    {
        lock (_queueLock)
        {
            if (_workerRunning) return;
            _workerRunning = true;
        }

        Task.Run(async () =>
        {
            while (true)
            {
                ModBedrockLipRegistry.LipPackage package;
                lock (_queueLock)
                {
                    if (_queue.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }

                    package = _queue.Dequeue();
                }

                await Task.Run(() => InstallOne(package)).ConfigureAwait(false);
            }
        });
    }

    private void InstallOne(ModBedrockLipRegistry.LipPackage package)
    {
        var version = CurrentInstanceVersion() ?? "";
        var instanceDir = ResolveInstanceDirectory() ?? "";
        SetProgress(package.Id, Lang.Text("Download.BedrockLip.Installing", package.Name), 0.02d);

        try
        {
            var result = ModBedrockLipRegistry.Install(package, instanceDir, version,
                p => SetProgress(package.Id, p.Stage, p.Percent));

            // 安装清单要在工作线程读完再回 UI 线程，避免在 UI 线程做文件解析
            var installed = ModBedrockLipRegistry.GetInstalledVersions(instanceDir);
            ModBase.RunInUi(() =>
            {
                ApplyInstalled(installed);
                _progress.Remove(package.Id);
                _finished[package.Id] = (Lang.Text("Download.BedrockLip.Installed"), true);
                _enqueued.Remove(package.Id);
                ApplyItemState(package.Id);
                RefreshQueueStatus();
                HintService.Hint(result, HintType.Success);
            });
        }
        catch (ModBedrockLipRegistry.LipInstallException ex)
        {
            // 版本不兼容、下载地址失效、lip 报错这类预期内的失败：只给提示，不弹"是否反馈此问题"
            ModBase.Log($"[Bedrock] 安装 Lip 插件失败：{ex.Message}", ModBase.LogLevel.Hint);
            FinishWithError(package, instanceDir, ex.Message);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Bedrock] 安装 Lip 插件失败", ModBase.LogLevel.Feedback);
            FinishWithError(package, instanceDir, ex.Message);
        }
    }

    private void FinishWithError(ModBedrockLipRegistry.LipPackage package, string instanceDir, string message)
    {
        var installed = ModBedrockLipRegistry.GetInstalledVersions(instanceDir);
        ModBase.RunInUi(() =>
        {
            ApplyInstalled(installed);
            _progress.Remove(package.Id);
            _finished[package.Id] = (message, false);
            _enqueued.Remove(package.Id);
            ApplyItemState(package.Id);
            RefreshQueueStatus();
        });
    }

    private void SetProgress(string packageId, string stage, double percent)
    {
        ModBase.RunInUi(() =>
        {
            _progress[packageId] = (stage, percent);
            ApplyItemState(packageId);
            RefreshQueueStatus();
        });
    }

    /// <summary>底部状态：有任务时显示当前进度，空闲时回到"共 N 个插件"。</summary>
    private void RefreshQueueStatus()
    {
        if (!Dispatcher.CheckAccess())
        {
            ModBase.RunInUi(RefreshQueueStatus);
            return;
        }

        var current = _progress.Values.FirstOrDefault();
        int queued;
        lock (_queueLock) queued = _queue.Count;

        if (!string.IsNullOrEmpty(current.Stage))
        {
            SetStatus(Lang.Text("Download.BedrockLip.QueueStatus", current.Stage, queued));
            return;
        }

        SetStatus(Lang.Text("Download.BedrockLip.Total", _packages.Count));
    }

    private static TextBlock CreateHint(string text)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontSize = 12.5d,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 10, 6, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
        return hint;
    }
}
