using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Provision;
using PCL.Core.UI;

namespace PCL;

public partial class WorldLibraryView : IRefreshable
{
    private readonly DispatcherTimer fileSystemRefreshTimer;
    private readonly DispatcherTimer searchTimer;
    private FileSystemWatcher fileSystemWatcher;
    private bool isLoad;

    private object quickPlayFeature = false;

    private List<string> saveFolders = new();
    private string worldPath;
    private McInstance currentInstance;
    private readonly List<McInstance> instanceList = new();
    private bool isStandalone;
    private bool instanceSelectedByUser;
    private SelectionRow? checkedVersionItem;

    public WorldLibraryView()
    {
        InitializeComponent();
        fileSystemRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100d) };
        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100d) };
        Loaded += PageSetupLaunch_Loaded;
        Unloaded += Page_Unloaded;
        fileSystemRefreshTimer.Tick += FileSystemRefreshTimer_Tick;
        searchTimer.Tick += SearchTimer_Tick;
        SearchBox.TextChanged += SearchRun;
    }

    void IRefreshable.Refresh()
    {
        RefreshSelf();
    }

    private void RefreshSelf()
    {
        Refresh();
        CheckQuickPlay();
    }

    public void Refresh()
    {
        Reload();
        if (!isStandalone) ModMain.frmInstanceLeft.ItemWorld.Selected = true;
        HintService.Hint(Lang.Text("Instance.Saves.Status.Refreshing"), log: false);
    }

    /// <summary>作为百宝箱的独立存档入口显示时，提供版本切换。</summary>
    public void EnableStandaloneMode()
    {
        isStandalone = true;
        PanInstanceSelect.Visibility = Visibility.Visible;
    }

    private void PageSetupLaunch_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();
        if (isStandalone)
        {
            currentInstance ??= InstanceWorkspace.Current ?? ModInstanceList.McMcInstanceSelected;
            RefreshInstanceList();
        }
        else
        {
            currentInstance = InstanceWorkspace.Current;
        }

        ApplyInstancePath();
        Reload();
        SetConvertState(ToolProvisionService.Resolve(ChunkerTool.Id) is null
            ? SaveFeatureState.ToolMissing
            : SaveFeatureState.Ready);

        // 非重复加载部分
        if (isLoad)
            return;
        isLoad = true;
        CheckQuickPlay();

        // 初始化文件系统监视器和排序按钮
        SetupFileSystemWatcher();
        BtnSort.Click += BtnSortClick;
        SetSortMethod(_currentSortMethod);
    }

    private void ApplyInstancePath()
    {
        worldPath = currentInstance.PathIndie + @"saves\";
        if (!Directory.Exists(worldPath))
            Directory.CreateDirectory(worldPath);
    }

    /// <summary>扫描当前 Minecraft 文件夹下的所有版本，并统计各自可见的存档数量。</summary>
    private void RefreshInstanceList()
    {
        try
        {
            instanceList.Clear();
            var versionsPath = Path.Combine(ModFolder.mcFolderSelected, "versions");
            if (Directory.Exists(versionsPath))
                foreach (var dir in Directory.GetDirectories(versionsPath).OrderBy(Path.GetFileName))
                {
                    var instance = new McInstance(Path.GetFileName(dir));
                    try
                    {
                        instance.Load();
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log(ex, Lang.Text("Instance.Saves.Error.OperationFailed"), ModBase.LogLevel.Hint);
                    }

                    instanceList.Add(instance);
                }

            // 按存档数从多到少排序，没有存档的版本自然排在最后
            instanceList.Sort((a, b) =>
            {
                var countA = GetSaveCount(a);
                var countB = GetSaveCount(b);
                return countA != countB
                    ? countB - countA
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            var targetName = instanceSelectedByUser
                ? currentInstance?.Name
                : States.Game.SelectedInstance;
            if (string.IsNullOrEmpty(targetName)) targetName = currentInstance?.Name;

            var index = instanceList.FindIndex(instance => instance.Name == targetName);
            if (index < 0) index = instanceList.Count > 0 ? 0 : -1;
            if (index >= 0) currentInstance = instanceList[index];

            UpdateVersionPicker();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Saves.Error.OperationFailed"), ModBase.LogLevel.Hint);
        }
    }

    private void UpdateVersionPicker()
    {
        if (currentInstance is null)
        {
            LabVersionTitle.Text = Lang.Text("Instance.Saves.Version");
            LabVersionInfo.Text = "";
            ImgVersionLogo.Address = ModBase.pathImage + "Blocks/Grass.png";
        }
        else
        {
            LabVersionTitle.Text = currentInstance.Name;
            LabVersionInfo.Text = BuildVersionInfo(currentInstance);
            var logo = GetInstanceLogo(currentInstance);
            ImgVersionLogo.Address = string.IsNullOrEmpty(logo) ? ModBase.pathImage + "Blocks/Grass.png" : logo;
        }

        PlayVersionCardContentTransition();
    }

    /// <summary>版本卡片内容切换时滑入淡入。</summary>
    private void PlayVersionCardContentTransition()
    {
        SymbioMotion.EnterDepth(PanVersionCardContent, 8, 1);
    }

    private void ApplyVersionLogo(SelectionRow item, McInstance instance)
    {
        var logo = GetInstanceLogo(instance);
        if (string.IsNullOrEmpty(logo))
        {
            // 与 Java 版实例列表保持一致：无法读取图标时使用通用草方块
            item.SvgIcon = "";
            item.Logo = ModBase.pathImage + "Blocks/Grass.png";
            return;
        }

        item.SvgIcon = "";
        item.Logo = logo;
    }

    private static string GetInstanceLogo(McInstance instance)
    {
        try
        {
            if (string.IsNullOrEmpty(instance.Logo)) return "";
            return instance.Logo.EndsWith(@"PCL\Logo.png") ? instance.PathInstance + @"PCL\Logo.png" : instance.Logo;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    ///     统计一个实例可见的存档数量。必须与 <see cref="LoadFileList" /> 用同一套判定
    ///     （目录里有 level.dat 才算存档），否则会出现「版本选择器说有 2 个存档、列表却是空的」。
    /// </summary>
    private static int GetSaveCount(McInstance instance)
    {
        try
        {
            var savesPath = Path.Combine(instance.PathIndie, "saves");
            if (!Directory.Exists(savesPath)) return 0;
            return Directory.EnumerateDirectories(savesPath)
                .Count(folder => File.Exists(Path.Combine(folder, "level.dat")));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[World] 统计存档数量失败", ModBase.LogLevel.Debug);
            return 0;
        }
    }

    private static string BuildVersionInfo(McInstance instance)
    {
        var text = Lang.Text("Instance.Saves.VersionInfo", GetSaveCount(instance));
        if (instance.PathIndie.TrimEnd('\\') == ModFolder.mcFolderSelected.TrimEnd('\\'))
            text += " · " + Lang.Text("Instance.Saves.VersionShared");
        return text;
    }

    private void PanVersionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isStandalone) return;
        BuildVersionMenu();
        PopupVersion.IsOpen = true;
    }

    private void PanVersionCard_MouseEnter(object sender, MouseEventArgs e) =>
        SymbioMotion.Fade(VersionHover, 1);

    private void PanVersionCard_MouseLeave(object sender, MouseEventArgs e) =>
        SymbioMotion.Fade(VersionHover, 0);

    private void BuildVersionMenu()
    {
        PanVersionList.Children.Clear();
        checkedVersionItem = null;
        foreach (var instance in instanceList)
        {
            var saveCount = GetSaveCount(instance);
            var item = new SelectionRow
            {
                Height = 42,
                Type = SelectionRow.CheckType.RadioBox,
                Title = instance.Name,
                Info = BuildVersionInfo(instance) +
                       (saveCount > 0 ? "" : " · " + Lang.Text("Instance.Saves.VersionDisabled")),
                MinPaddingRight = 20,
                IsEnabled = saveCount > 0
            };
            ApplyVersionLogo(item, instance);
            if (!item.IsEnabled)
            {
                item.Opacity = 0.4d;
                item.isMouseOverAnimationEnabled = false;
                item.Cursor = Cursors.No;
            }

            item.SetChecked(instance.Name == currentInstance?.Name, false, false);
            if (item.Checked) checkedVersionItem = item;
            item.Check += (_, _) => SelectVersion(instance);
            PanVersionList.Children.Add(item);
        }
    }

    private void SelectVersion(McInstance instance)
    {
        PopupVersion.IsOpen = false;
        if (currentInstance?.Name == instance.Name) return;
        if (GetSaveCount(instance) == 0) return;

        currentInstance = instance;
        instanceSelectedByUser = true;
        UpdateVersionPicker();
        CloseMapEditor();
        ApplyInstancePath();
        SetupFileSystemWatcher();
        Reload();
        PlayVersionTransition();
    }

    /// <summary>切换版本时给存档区域与版本卡片做一段平滑过渡。</summary>
    private void PlayVersionTransition()
    {
        SymbioMotion.EnterDepth(PanVersionCard, 0, .97);
        SymbioMotion.EnterDepth(PanSavesBody, 16, .97);
    }

    private void PopupVersion_Opened(object sender, EventArgs e)
    {
        AnimateVersionChevron(270d);
        SymbioMotion.EnterDepth(PanVersionPopup, 8, .97, SymbioMotion.Pop);
        Dispatcher.BeginInvoke(new Action(() => checkedVersionItem?.BringIntoView()), DispatcherPriority.Background);
    }

    private void PopupVersion_Closed(object sender, EventArgs e) => AnimateVersionChevron(90d);

    private void AnimateVersionChevron(double angle)
    {
        SymbioMotion.SpringRotate(IconVersionChevron, angle);
    }

    private string GetFolderNameFromPath(string fullPath)
    {
        return string.IsNullOrEmpty(fullPath) ? "" :
            fullPath.EndsWith(@"\") ? new DirectoryInfo(fullPath).Parent?.Name : new DirectoryInfo(fullPath).Name;
    }

    private string GetFileNameFromPath(string fullPath)
    {
        return Path.GetFileName(fullPath);
    }

    private void SetupFileSystemWatcher()
    {
        if (fileSystemWatcher is not null) fileSystemWatcher.Dispose();

        // 确保目录存在
        if (!Directory.Exists(worldPath))
            Directory.CreateDirectory(worldPath);

        fileSystemWatcher = new FileSystemWatcher();
        fileSystemWatcher.Path = worldPath;
        fileSystemWatcher.IncludeSubdirectories = false;
        fileSystemWatcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

        fileSystemWatcher.Created += OnFileSystemChanged;
        fileSystemWatcher.Deleted += OnFileSystemChanged;
        fileSystemWatcher.Renamed += OnFileSystemChanged;

        fileSystemWatcher.EnableRaisingEvents = true;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        fileSystemRefreshTimer.Stop();
        fileSystemRefreshTimer.Start();
    }

    private void FileSystemRefreshTimer_Tick(object sender, EventArgs e)
    {
        fileSystemRefreshTimer.Stop();
        ModBase.RunInUi(() => Reload(), true);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (fileSystemWatcher is not null)
        {
            fileSystemWatcher.Created -= OnFileSystemChanged;
            fileSystemWatcher.Deleted -= OnFileSystemChanged;
            fileSystemWatcher.Renamed -= OnFileSystemChanged;
            fileSystemWatcher.Dispose();
            fileSystemWatcher = null;
        }

        fileSystemRefreshTimer.Stop();
        searchTimer.Stop();
    }

    /// <summary>
    ///     确保当前页面上的信息已正确显示。
    /// </summary>
    public void Reload()
    {
        ModAnimation.AniBeginControl();
        PanBack.ScrollToHome();
        LoadFileList();
        ModAnimation.AniEndControl();
    }

    private void RefreshUI()
    {
        try
        {
            if (IsSearching)
            {
                var resultCount = _searchResult is null ? 0 : _searchResult.Count;
                PanListBack.Title = Lang.Text("Instance.Saves.SearchResultTitle", resultCount.ToString());
            }
            else
            {
                PanListBack.Title = Lang.Text("Instance.Saves.SaveListTitle", saveFolders.Count.ToString());
            }

            if (saveFolders.Count == 0)
            {
                PanNoWorld.Visibility = Visibility.Visible;
                PanContent.Visibility = Visibility.Collapsed;
                PanNoWorld.UpdateLayout();
            }
            else
            {
                PanNoWorld.Visibility = Visibility.Collapsed;
                PanContent.Visibility = Visibility.Visible;
                PanContent.UpdateLayout();

                var showingSaves = (IsSearching ? _searchResult : saveFolders).ToList();

                if (showingSaves.Any())
                {
                    var sortMethod = GetSortMethod(_currentSortMethod);
                    showingSaves.Sort((a, b) => sortMethod(a, b));
                }

                using var _anim = ModAnimation.Suppress();
                PanList.Children.Clear();

                foreach (var curFolder in showingSaves)
                {
                    // 检查文件夹是否仍然存在
                    if (!Directory.Exists(curFolder)) continue;

                    var saveLogo = Path.Combine(curFolder, "icon.png");
                    var tmpCurFolder = curFolder;
                    if (File.Exists(saveLogo))
                    {
                        var target =
                            $@"{currentInstance.PathInstance}PCL\ImgCache\{ModBase.GetStringMD5(saveLogo)}.png";
                        ModBase.CopyFile(saveLogo, target);
                        saveLogo = target;
                    }
                    else
                    {
                        saveLogo = ModBase.pathImage + "Icons/NoIcon.png";
                    }

                    var worldItem = new SelectionRow
                    {
                        Logo = saveLogo,
                        Title = GetFolderNameFromPath(curFolder),
                        Info =
                            Lang.Text("Instance.Saves.CreationTime", Lang.Date(Directory.GetCreationTime(curFolder), "d"), Lang.Date(Directory.GetLastWriteTime(curFolder), "d")),
                        Type = SelectionRow.CheckType.Clickable
                    };
                    worldItem.Click += (_, _) => ModMain.frmMain.PageChange(new FormMain.PageStackData
                        { page = FormMain.PageType.VersionSaves, additional = (null, null, null, ModComp.CompLoaderType.Any, ModComp.CompType.Any, tmpCurFolder) });

                    var btnOpen = new SymbioIconButton
                    {
                        SvgIcon = "lucide/folder-open",
                        ToolTip = Lang.Text("Common.Action.Open")
                    };
                    btnOpen.Click += (_, _) => ModBase.OpenExplorer(tmpCurFolder);
                    var btnDelete = new SymbioIconButton
                    {
                        SvgIcon = "lucide/trash-2",
                        ToolTip = Lang.Text("Common.Action.Delete")
                    };
                    btnDelete.Click += (_, _) =>
                    {
                        worldItem.IsEnabled = false;
                        worldItem.Info = Lang.Text("Instance.Saves.Deleting");
                        ModBase.RunInNewThread(() =>
                        {
                            try
                            {
                                FileSystem.DeleteDirectory(tmpCurFolder, UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                                HintService.Hint(Lang.Text("Instance.Saves.DeletedToRecycleBin"));
                                ModBase.RunInUiWait(() => RemoveItem(worldItem));
                            }
                            catch (Exception ex)
                            {
                                ModBase.Log(
                                    ex,
                                    Lang.Text("Instance.Saves.DeleteFailed"),
                                    ModBase.LogLevel.Hint,
                                    userSummary: Lang.Text("Instance.Saves.DeleteFailed"));
                                ModBase.RunInUiWait(() => Reload());
                            }
                        });
                    };
                    var btnCopy = new SymbioIconButton
                    {
                        SvgIcon = "lucide/copy",
                        ToolTip = Lang.Text("Common.Action.Copy")
                    };
                    btnCopy.Click += (_, _) =>
                    {
                        try
                        {
                            if (Directory.Exists(tmpCurFolder))
                            {
                                Clipboard.SetFileDropList(new StringCollection { tmpCurFolder });
                                HintService.Hint(Lang.Text("Instance.Saves.CopiedToClipboard"));
                                HintService.Hint(Lang.Text("Instance.Saves.CopyPasteWarning"));
                            }
                            else
                            {
                                HintService.Hint(Lang.Text("Instance.Saves.FolderNotFound"));
                            }
                        }
                        catch (Exception ex)
                        {
                            ModBase.Log(
                                ex,
                                Lang.Text("Instance.Saves.CopyFailed"),
                                ModBase.LogLevel.Hint,
                                userSummary: Lang.Text("Instance.Saves.CopyFailed"));
                        }
                    };
                    var btnMap = new SymbioIconButton
                    {
                        SvgIcon = "lucide/grid-2x2",
                        ToolTip = Lang.Text("Instance.Saves.MapEditor")
                    };
                    btnMap.Click += (_, _) => OpenMapEditor(tmpCurFolder);
                    var btnInfo = new SymbioIconButton
                    {
                        SvgIcon = "lucide/info",
                        ToolTip = Lang.Text("Instance.Saves.Details")
                    };
                    btnInfo.Click += (_, _) => ModMain.frmMain.PageChange(new FormMain.PageStackData
                        { page = FormMain.PageType.VersionSaves, additional = (null, null, null, ModComp.CompLoaderType.Any, ModComp.CompType.Any, tmpCurFolder) });

                    var btnLaunch = new SymbioIconButton
                    {
                        SvgIcon = "lucide/play",
                        ToolTip = Lang.Text("Instance.Saves.QuickPlay")
                    };
                    btnLaunch.Click += (_, _) =>
                    {
                        var worldName = GetFileNameFromPath(tmpCurFolder);
                        var launchOptions = new ModLaunch.McLaunchOptions
                        {
                            WorldName = worldName,
                            instance = currentInstance
                        };
                        ModLaunch.McLaunchStart(launchOptions);
                        ModMain.frmMain.PageChange(new FormMain.PageStackData { page = FormMain.PageType.Launch });
                    };
                    if ((bool)quickPlayFeature)
                        worldItem.Buttons = new[] { btnOpen, btnDelete, btnCopy, btnMap, btnInfo, btnLaunch };
                    else
                        worldItem.Buttons = new[] { btnOpen, btnDelete, btnCopy, btnMap, btnInfo };

                    PanList.Children.Add(worldItem);
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Instance.Saves.RefreshUiFailed"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Instance.Saves.RefreshUiFailed"));
        }
    }

    private void CheckQuickPlay()
    {
        try
        {
            var cur = new ModLaunch.LaunchArgument(currentInstance);
            quickPlayFeature = cur.HasArguments("--quickPlaySingleplayer");
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "检查存档快捷启动失败",
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Instance.Saves.Error.OperationFailed"));
        }
    }

    private void LoadFileList()
    {
        try
        {
            ModBase.Log("[World] 刷新存档文件");
            saveFolders.Clear();
            if (Directory.Exists(worldPath))
                saveFolders = Directory.EnumerateDirectories(worldPath)
                    .Where(folder => File.Exists(Path.Combine(folder, "level.dat")))
                    .ToList();
            else
                saveFolders = new List<string>();

            if (ModBase.modeDebug)
                ModBase.Log("[World] 共发现 " + saveFolders.Count + " 个存档文件夹", ModBase.LogLevel.Debug);
            PanList.Children.Clear();
            CheckQuickPlay();

            if (ModBase.modeDebug)
            {
                if ((bool)quickPlayFeature)
                    ModBase.Log("[World] 该实例支持存档快捷启动", ModBase.LogLevel.Debug);
                else
                    ModBase.Log("[World] 该实例不支持存档快捷启动", ModBase.LogLevel.Debug);
            }

            RefreshUI(); // 确保UI刷新
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Instance.Saves.LoadListFailed"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Instance.Saves.LoadListFailed"));
        }
    }

    private void RemoveItem(SelectionRow item)
    {
        if (PanList.Children.IndexOf(item) == -1)
            return;
        PanList.Children.Remove(item);
        RefreshUI();
    }

    private void BtnMapBack_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        CloseMapEditor();
    }

    private async void OpenMapEditor(string savePath)
    {
        PanBack.Visibility = Visibility.Collapsed;
        PanMapEditor.Visibility = Visibility.Visible;
        await MapEditor.LoadWorldAsync(savePath);
    }

    private void CloseMapEditor()
    {
        MapEditor.CloseWorld();
        PanMapEditor.Visibility = Visibility.Collapsed;
        PanBack.Visibility = Visibility.Visible;
        Reload();
    }

    private void BtnOpenFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModBase.OpenExplorer(worldPath);
    }

    private void BtnPaste_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var files = Clipboard.GetFileDropList();
        var loaders = new List<ModLoader.LoaderBase>();
        loaders.Add(new ModLoader.LoaderTask<int, int>("Copy saves", _ =>
        {
            var copied = 0;
            foreach (var i in files)
                try
                {
                    if (Directory.Exists(i))
                    {
                        if (Directory.Exists(worldPath + GetFolderNameFromPath(i)))
                        {
                            HintService.Hint(Lang.Text("Instance.Saves.DuplicateFolder", GetFolderNameFromPath(i)));
                        }
                        else
                        {
                            ModBase.CopyDirectory(i, worldPath + GetFolderNameFromPath(i));
                            copied += 1;
                        }
                    }
                    else
                    {
                        HintService.Hint(Lang.Text("Instance.Saves.SourceNotFolder"));
                    }
                }
                catch (Exception ex)
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Instance.Saves.PasteFolderFailed"),
                        ModBase.LogLevel.Hint,
                        userSummary: Lang.Text("Instance.Saves.PasteFolderFailed"));
                }

            if (copied > 0)
                HintService.Hint(Lang.Text("Instance.Saves.PastedCount", copied.ToString()), HintType.Success);
            ModBase.RunInUi(() => Reload());
        }));
        var loader = new ModLoader.LoaderCombo<int>($"{currentInstance.Name} - {Lang.Text("Instance.Saves.CopySave")}", loaders)
            { OnStateChanged = ModDownloadLib.LoaderStateChangedHintOnly };
        loader.Start(1);
        ModLoader.LoaderTaskbarAdd(loader);
    }

    private void BtnConvert_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var choice = ModMain.MyMsgBox(
            Lang.Text("Instance.Saves.Convert.Message"),
            Lang.Text("Instance.Saves.Convert.Title"),
            Lang.Text("Instance.Saves.Convert.Local.Button"),
            Lang.Text("Common.Action.Cancel"));
        if (choice == 1) LocalConvert();
    }

    /// <summary>存档转换状态机：ToolMissing → ToolDownloading → Ready / ToolFailed。</summary>
    private SaveFeatureState convertState = SaveFeatureState.ToolMissing;
    private double convertProgress;

    private void SetConvertState(SaveFeatureState state, string? detail = null)
    {
        convertState = state;
        LabConvertState.Text = string.IsNullOrWhiteSpace(detail)
            ? state switch
            {
                SaveFeatureState.ToolMissing => "转换工具未就绪，点击「转换存档」会自动下载",
                SaveFeatureState.ToolDownloading => $"正在准备转换工具… {convertProgress:P0}",
                SaveFeatureState.ToolFailed => "转换工具不可用",
                SaveFeatureState.FormatUnsupported => "格式不兼容，非工具问题",
                SaveFeatureState.PlatformLimited => "当前平台不支持该转换",
                _ => "转换工具已就绪"
            }
            : detail;

        PanConvertState.Visibility = state == SaveFeatureState.Ready ? Visibility.Collapsed : Visibility.Visible;
        var failed = state == SaveFeatureState.ToolFailed;
        BtnConvertRetry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        BtnConvertLocate.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        BtnConvertLog.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        BtnConvert.IsEnabled = state is not SaveFeatureState.ToolDownloading;
    }

    private void BtnConvertRetry_Click(object sender, RoutedEventArgs e) => LocalConvert();

    private void BtnConvertLocate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择本地 Chunker CLI（jar）",
            Filter = "Chunker CLI (*.jar)|*.jar|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            ToolProvisionService.RegisterManual(ChunkerTool.Id, dialog.FileName);
            SetConvertState(SaveFeatureState.Ready);
            HintService.Hint("已使用手动指定的 Chunker，可以直接转换了", HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "登记本地 Chunker 失败", ModBase.LogLevel.Hint);
            SetConvertState(SaveFeatureState.ToolFailed, "无法使用该文件：" + ex.Message);
        }
    }

    private void BtnConvertLog_Click(object sender, RoutedEventArgs e) =>
        ModBase.OpenExplorer(ModBase.exePath + @"PCL\Log\");

    private async void LocalConvert()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.Text("Instance.Saves.Convert.SelectSource"),
            InitialDirectory = Directory.Exists(worldPath) ? worldPath : ""
        };
        if (dialog.ShowDialog() != true) return;
        var source = dialog.FolderName;
        if (!File.Exists(Path.Combine(source, "level.dat")) && !Directory.Exists(Path.Combine(source, "db")))
        {
            HintService.Hint(Lang.Text("Instance.Saves.Convert.InvalidFolder"));
            return;
        }

        var direction = ModMain.MyMsgBox(
            Lang.Text("Instance.Saves.Convert.DirectionMessage"),
            Lang.Text("Instance.Saves.Convert.DirectionTitle"),
            Lang.Text("Instance.Saves.Convert.ToBedrock"),
            Lang.Text("Instance.Saves.Convert.ToJava"),
            Lang.Text("Common.Action.Cancel"));
        if (direction is not (1 or 2)) return;
        var toBedrock = direction == 1;
        var sourceName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));

        try
        {
            // 1) 备好工具：命中清单直接返回，未命中自动下载（带进度）
            var cli = ToolProvisionService.Resolve(ChunkerTool.Id);
            if (cli is null)
            {
                convertProgress = 0;
                SetConvertState(SaveFeatureState.ToolDownloading);
                var definition = await ChunkerTool.ResolveLatestAsync();
                var progress = new Progress<double>(value =>
                {
                    convertProgress = value;
                    if (convertState == SaveFeatureState.ToolDownloading) SetConvertState(SaveFeatureState.ToolDownloading);
                });
                cli = await ToolProvisionService.EnsureAsync(definition, progress);
            }

            SetConvertState(SaveFeatureState.Ready);

            // 2) 转换：Java 探测、格式列举、Chunker 进程全部在后台线程，UI 不阻塞
            SetConvertState(SaveFeatureState.ToolDownloading, "正在转换存档…");
            var (output, format) = await Task.Run(() =>
            {
                var java = ModSaveConverter.FindJava() ??
                           throw new InvalidOperationException(Lang.Text("Instance.Saves.Convert.Local.NoJava"));
                return RunChunker(java, cli, source, toBedrock, sourceName);
            });
            _ = format;
            SetConvertState(SaveFeatureState.Ready);

            if (toBedrock) ImportBedrockOutput(output, sourceName);
            else ImportJavaOutput(output);
            Reload();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "Chunker 转换失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Instance.Saves.Convert.Local.Failed"));
            SetConvertState(SaveFeatureState.ToolFailed, Truncate("转换失败：" + ex.Message, 40));
        }
    }

    /// <summary>跑一次 Chunker 转换，返回输出目录与所用格式。纯后台逻辑，不碰 UI。</summary>
    private (string Output, string Format) RunChunker(string java, string cli, string source, bool toBedrock,
        string sourceName)
    {
        var formats = ModSaveConverter.ListFormats(java, cli);
        // Java 输出优先用当前实例的版本，避免把存档升到过新的格式
        string? preferred = null;
        if (!toBedrock)
        {
            var match = System.Text.RegularExpressions.Regex.Match(currentInstance.Name, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (match.Success)
                preferred = "JAVA_" + match.Groups[1].Value + "_" + match.Groups[2].Value +
                            (match.Groups[3].Success ? "_" + match.Groups[3].Value : "");
        }

        var format = formats.FirstOrDefault(item =>
                         string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase)) ??
                     (toBedrock
                         ? ModSaveConverter.PickBedrockFormat(formats)
                         : ModSaveConverter.PickLatestFormat(formats, "JAVA")) ??
                     throw new InvalidOperationException(Lang.Text("Instance.Saves.Convert.Local.NoFormat"));
        var output = Path.Combine(ModSaveConverter.CacheDirectory, "out",
            $"{sourceName}-{DateTime.Now:yyyyMMdd-HHmmss}");
        ModSaveConverter.Convert(java, cli, source, format, output);
        return (output, format);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private void ImportJavaOutput(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        var target = Path.Combine(worldPath, name);
        var index = 2;
        while (Directory.Exists(target)) target = Path.Combine(worldPath, $"{name} ({index++})");
        ModBase.CopyDirectory(folder, target);
        ModBase.RunInUi(() => HintService.Hint(
            Lang.Text("Instance.Saves.Convert.Success", Path.GetFileName(target)), HintType.Success));
    }

    private void ImportBedrockOutput(string folder, string name)
    {
        var worlds = FindBedrockWorldsFolder();
        if (worlds is not null)
        {
            var target = Path.Combine(worlds, name);
            var index = 2;
            while (Directory.Exists(target)) target = Path.Combine(worlds, $"{name} ({index++})");
            ModBase.CopyDirectory(folder, target);
            ModBase.RunInUi(() =>
            {
                HintService.Hint(Lang.Text("Instance.Saves.Convert.Local.DoneBedrock", Path.GetFileName(target)),
                    HintType.Success);
                ModBase.OpenExplorer(worlds);
            });
            return;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var zip = Path.Combine(desktop, name + ".mcworld");
        var fileIndex = 2;
        while (File.Exists(zip)) zip = Path.Combine(desktop, $"{name} ({fileIndex++}).mcworld");
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
        ModBase.RunInUi(() =>
        {
            HintService.Hint(Lang.Text("Instance.Saves.Convert.Local.DoneBedrockFile", zip), HintType.Success);
            ModBase.OpenExplorer(desktop);
        });
    }

    private static string? FindBedrockWorldsFolder()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages",
                "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock",
                "Users", "Shared", "games", "com.mojang", "minecraftWorlds")
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private void BtnDownloadNew_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadWorld);
        ResourceSearchSession.SuggestedInstance = currentInstance; // 将当前实例设置为筛选器
    }

    #region 搜索和排序

    private SortMethod _currentSortMethod = SortMethod.FileName;
    private List<string> _searchResult;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchBox.Text);

    private enum SortMethod
    {
        FileName,
        CreateTime,
        ModifyTime
    }

    private string GetSortName(SortMethod method)
    {
        switch (method)
        {
            case SortMethod.FileName:
            {
                return Lang.Text("Instance.Saves.SortFileName");
            }
            case SortMethod.CreateTime:
            {
                return Lang.Text("Instance.Saves.SortCreateTime");
            }
            case SortMethod.ModifyTime:
            {
                return Lang.Text("Instance.Saves.SortModifyTime");
            }

            default:
            {
                return Lang.Text("Instance.Saves.SortFileName");
            }
        }
    }

    private void SetSortMethod(SortMethod target)
    {
        _currentSortMethod = target;
        BtnSort.Text = Lang.Text("Instance.Saves.SortBy", GetSortName(target));
        RefreshUI();
    }

    private void BtnSortClick(object sender, EventArgs e)
    {
        var body = new ContextMenu();
        foreach (SortMethod i in Enum.GetValues(typeof(SortMethod)))
        {
            var item = new SymbioMenuAction();
            item.Header = GetSortName(i);
            item.Click += (_, _) => SetSortMethod(i);
            body.Items.Add(item);
        }

        body.PlacementTarget = (UIElement)sender;
        body.Placement = PlacementMode.Bottom;
        body.IsOpen = true;
    }

    private void SearchRun(object sender, EventArgs e)
    {
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void SearchTimer_Tick(object sender, EventArgs e)
    {
        searchTimer.Stop();
        PerformSearch();
    }

    private void PerformSearch()
    {
        try
        {
            if (IsSearching)
            {
                var queryList = new List<ModBase.SearchEntry<string>>();
                foreach (var saveFolder in saveFolders)
                {
                    var folderName = GetFolderNameFromPath(saveFolder);
                    var searchSource = new List<ModBase.SearchSource>();
                    searchSource.Add(new ModBase.SearchSource(folderName, 1d));
                    queryList.Add(new ModBase.SearchEntry<string> { item = saveFolder, searchSource = searchSource });
                }

                _searchResult = ModBase.Search(queryList, SearchBox.Text, ModBase.MaxLocalSearchDepth, 0.35d).Select(r => r.item).ToList();
            }
            else
            {
                _searchResult = null;
            }

            RefreshUI();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Instance.Saves.SearchError"));
        }
    }

    private Func<string, string, int> GetSortMethod(SortMethod method)
    {
        switch (method)
        {
            case SortMethod.FileName:
            {
                return (a, b) => string.Compare(GetFolderNameFromPath(a), GetFolderNameFromPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            case SortMethod.CreateTime:
            {
                return (a, b) => Directory.GetCreationTime(b).CompareTo(Directory.GetCreationTime(a));
            }
            case SortMethod.ModifyTime:
            {
                return (a, b) => Directory.GetLastWriteTime(b).CompareTo(Directory.GetLastWriteTime(a));
            }

            default:
            {
                return (a, b) => string.Compare(GetFolderNameFromPath(a), GetFolderNameFromPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    #endregion
}
