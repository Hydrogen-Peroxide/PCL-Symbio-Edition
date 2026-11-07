using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class PageInstanceSaves : IRefreshable
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
    private MyListItem? checkedVersionItem;

    public PageInstanceSaves()
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

    public static void Refresh()
    {
        if (ModMain.frmInstanceSaves is not null)
            ModMain.frmInstanceSaves.Reload();
        ModMain.frmInstanceLeft.ItemWorld.Checked = true;
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
            currentInstance ??= PageInstanceLeft.McInstance ?? ModInstanceList.McMcInstanceSelected;
            RefreshInstanceList();
        }
        else
        {
            currentInstance = PageInstanceLeft.McInstance;
        }

        ApplyInstancePath();
        Reload();

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
            ImgVersionLogo.ActualSource = ModBase.pathImage + "Blocks/Grass.png";
        }
        else
        {
            LabVersionTitle.Text = currentInstance.Name;
            LabVersionInfo.Text = BuildVersionInfo(currentInstance);
            var logo = GetInstanceLogo(currentInstance);
            ImgVersionLogo.ActualSource = string.IsNullOrEmpty(logo) ? ModBase.pathImage + "Blocks/Grass.png" : logo;
        }

        PlayVersionCardContentTransition();
    }

    /// <summary>版本卡片内容切换时滑入淡入。</summary>
    private void PlayVersionCardContentTransition()
    {
        if (PanVersionCardContent.RenderTransform is not TranslateTransform slide) return;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        slide.Y = 8d;
        PanVersionCardContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8d, 0d, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void ApplyVersionLogo(MyListItem item, McInstance instance)
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

    private static int GetSaveCount(McInstance instance)
    {
        var savesPath = Path.Combine(instance.PathIndie, "saves");
        return Directory.Exists(savesPath) ? Directory.GetDirectories(savesPath).Length : 0;
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
        ModAnimation.AniStart(
            ModAnimation.AaColor(PanVersionCard, Border.BackgroundProperty, "ColorBrushBg1", 120),
            "VersionCard Color");

    private void PanVersionCard_MouseLeave(object sender, MouseEventArgs e) =>
        ModAnimation.AniStart(
            ModAnimation.AaColor(PanVersionCard, Border.BackgroundProperty, "ColorBrushGray7", 160),
            "VersionCard Color");

    private void BuildVersionMenu()
    {
        PanVersionList.Children.Clear();
        checkedVersionItem = null;
        foreach (var instance in instanceList)
        {
            var saveCount = GetSaveCount(instance);
            var item = new MyListItem
            {
                Height = 42,
                Type = MyListItem.CheckType.RadioBox,
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
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        if (PanVersionCard.RenderTransform is ScaleTransform card)
        {
            card.ScaleX = card.ScaleY = 0.97d;
            var scale = new DoubleAnimation(0.97d, 1d, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
            card.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            card.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        if (PanSavesBody.RenderTransform is not TranslateTransform slide) return;
        slide.Y = 16d;
        PanSavesBody.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16d, 0d, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
    }

    private void PopupVersion_Opened(object sender, EventArgs e)
    {
        AnimateVersionChevron(270d);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        if (PanVersionPopup.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = scale.ScaleY = 0.94d;
            var openScale = new DoubleAnimation(0.94d, 1d, TimeSpan.FromMilliseconds(180))
                { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, openScale);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, openScale);
        }

        PanVersionPopup.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(new Action(() => checkedVersionItem?.BringIntoView()), DispatcherPriority.Background);
    }

    private void PopupVersion_Closed(object sender, EventArgs e) => AnimateVersionChevron(90d);

    private void AnimateVersionChevron(double angle)
    {
        if (IconVersionChevron.RenderTransform is not RotateTransform rotate) return;
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
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

                ModAnimation.AniBeginControl();
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

                    var worldItem = new MyListItem
                    {
                        Logo = saveLogo,
                        Title = GetFolderNameFromPath(curFolder),
                        Info =
                            Lang.Text("Instance.Saves.CreationTime", Lang.Date(Directory.GetCreationTime(curFolder), "d"), Lang.Date(Directory.GetLastWriteTime(curFolder), "d")),
                        Type = MyListItem.CheckType.Clickable
                    };
                    worldItem.Click += (_, _) => ModMain.frmMain.PageChange(new FormMain.PageStackData
                        { page = FormMain.PageType.VersionSaves, additional = (null, null, null, ModComp.CompLoaderType.Any, ModComp.CompType.Any, tmpCurFolder) });

                    var btnOpen = new MyIconButton
                    {
                        SvgIcon = "lucide/folder-open",
                        ToolTip = Lang.Text("Common.Action.Open")
                    };
                    btnOpen.Click += (_, _) => ModBase.OpenExplorer(tmpCurFolder);
                    var btnDelete = new MyIconButton
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
                    var btnCopy = new MyIconButton
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
                    var btnMap = new MyIconButton
                    {
                        SvgIcon = "lucide/grid-2x2",
                        ToolTip = Lang.Text("Instance.Saves.MapEditor")
                    };
                    btnMap.Click += (_, _) => OpenMapEditor(tmpCurFolder);
                    var btnInfo = new MyIconButton
                    {
                        SvgIcon = "lucide/info",
                        ToolTip = Lang.Text("Instance.Saves.Details")
                    };
                    btnInfo.Click += (_, _) => ModMain.frmMain.PageChange(new FormMain.PageStackData
                        { page = FormMain.PageType.VersionSaves, additional = (null, null, null, ModComp.CompLoaderType.Any, ModComp.CompType.Any, tmpCurFolder) });

                    var btnLaunch = new MyIconButton
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

                ModAnimation.AniEndControl();
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
                saveFolders = Directory.EnumerateDirectories(worldPath).ToList();
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

    private void RemoveItem(MyListItem item)
    {
        if (PanList.Children.IndexOf(item) == -1)
            return;
        PanList.Children.Remove(item);
        RefreshUI();
    }

    private void BtnMapBack_Click(object sender, MouseButtonEventArgs e)
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

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        ModBase.OpenExplorer(worldPath);
    }

    private void BtnPaste_Click(object sender, MouseButtonEventArgs e)
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
        ModMain.frmMain.BtnExtraDownload.ShowRefresh();
        ModMain.frmMain.BtnExtraDownload.Ribble();
    }

    private void BtnDownloadNew_Click(object sender, MouseButtonEventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadWorld);
        PageComp.targetVersion = currentInstance; // 将当前实例设置为筛选器
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
            var item = new MyMenuItem();
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
