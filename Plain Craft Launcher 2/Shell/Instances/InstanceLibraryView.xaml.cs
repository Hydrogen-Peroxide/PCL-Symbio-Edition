using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.FileIO;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Configuration.Storage;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class InstanceLibraryView : IRefreshable
{
    private const int normalDelay = 75;
    private const int quickDelay = 50;
    private readonly DispatcherTimer reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(normalDelay) };

    public InstanceLibraryView()
    {
        InitializeComponent();
        PanVerSearchBox.HintText = Lang.Text("Select.Instance.Search.Hint");
        PanVerSearchBox.TextChanged += SearchEdited;
        reloadTimer.Tick += ReloadResults;
        Loaded += (_, _) =>
        {
            ModFolder.RefreshInstances(ModFolder.mcFolderSelected);
            PanBack.ScrollToHome();
        };
        Unloaded += (_, _) => reloadTimer.Stop();
        LoaderInit();
    }

    void IRefreshable.Refresh() => ModFolder.RefreshInstances(ModFolder.mcFolderSelected, clearCache: true);

    private void SearchEdited(object sender, TextChangedEventArgs e)
    {
        reloadTimer.Stop();
        reloadTimer.Interval = TimeSpan.FromMilliseconds(string.IsNullOrWhiteSpace(PanVerSearchBox.Text) ? quickDelay : normalDelay);
        if (IsLoaded) reloadTimer.Start();
    }

    private void ReloadResults(object? sender, EventArgs e)
    {
        var state = ModInstanceList.mcInstanceListLoader.State;
        if (state is ModBase.LoadState.Loading or ModBase.LoadState.Waiting) return;
        reloadTimer.Stop();
        if (state == ModBase.LoadState.Finished) McInstanceListUI(ModInstanceList.mcInstanceListLoader);
    }

    private void LoaderInit()
    {
        PageLoaderInit(Load, PanLoad, PanAllBack, null, ModInstanceList.mcInstanceListLoader,
            a => this.McInstanceListUI((ModLoader.LoaderTask<string, int>)a),
            autoRun: false);
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (ModInstanceList.mcInstanceListLoader.State == ModBase.LoadState.Failed)
            ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\");
    }

    #region 结果 UI 化

    private void McInstanceListUI(ModLoader.LoaderTask<string, int> loader)
    {
        try
        {
            var path = loader.input;
            // 加载 UI
            PanMain.Children.Clear();

            var hasVisibleFolders = false;
            var searchText = PanVerSearchBox.Text.Trim().ToLower(); // 获取搜索框文本
            var hasAnyResults = false;
            var originalHasInstances = ModInstanceList.mcInstanceList.ToArray().Any(c => c.Value.Count > 0);

            // 搜索无结果时显示 PanEmptySearch
            PanEmptySearch.Visibility = Visibility.Collapsed; // 默认隐藏

            foreach (var Card in ModInstanceList.mcInstanceList.ToArray())
            {
                if ((Card.Key == McInstanceCardType.Hidden) ^ ModInstanceList.ShowHidden)
                    continue;
                var filteredInstances = Card.Value.Where(v =>
                {
                    if (string.IsNullOrEmpty(searchText))
                        return true;
                    return v.Name.ToLower().Contains(searchText) ||
                           (v.Desc is not null && v.Desc.ToLower().Contains(searchText)) || v.GetDefaultDescription()
                               .Replace(",", "").ToLower().Trim().Contains(searchText);
                }).ToList();
                if (filteredInstances.Count == 0)
                    continue;

                hasVisibleFolders = true;
                hasAnyResults = true;

                #region 确认卡片名称

                var cardName = "";
                switch (Card.Key)
                {
                    case McInstanceCardType.OriginalLike:
                    {
                        cardName = Lang.Text("Select.Instance.Card.Regular");
                        break;
                    }
                    case McInstanceCardType.API:
                    {
                        var isForgeExists = false;
                        var isNeoForgeExists = false;
                        var isFabricExists = false;
                        var isQuiltExists = false;
                        var isLiteExists = false;
                        var isCleanroomExists = false;
                        var isLabyModExists = false;
                        foreach (var instance in Card.Value)
                        {
                            if (!instance.IsLoaded)
                                instance.Load();
                            if (instance.Info.HasFabric)
                                isFabricExists = true;
                            if (instance.Info.HasQuilt)
                                isQuiltExists = true;
                            if (instance.Info.HasLiteLoader)
                                isLiteExists = true;
                            if (instance.Info.HasForge)
                                isForgeExists = true;
                            if (instance.Info.HasNeoForge)
                                isNeoForgeExists = true;
                            if (instance.Info.HasCleanroom)
                                isCleanroomExists = true;
                            if (instance.Info.HasLabyMod)
                                isLabyModExists = true;
                        }

                        if ((isLiteExists ? 1 : 0) + (isForgeExists ? 1 : 0) + (isFabricExists ? 1 : 0) +
                            (isNeoForgeExists ? 1 : 0) + (isQuiltExists ? 1 : 0) + (isCleanroomExists ? 1 : 0) +
                            (isLabyModExists ? 1 : 0) > 1)
                            cardName = Lang.Text("Select.Instance.Card.Modable");
                        else if (isForgeExists)
                            cardName = Lang.Text("Select.Instance.Card.Forge");
                        else if (isNeoForgeExists)
                            cardName = Lang.Text("Select.Instance.Card.NeoForge");
                        else if (isCleanroomExists)
                            cardName = Lang.Text("Select.Instance.Card.Cleanroom");
                        else if (isLabyModExists)
                            cardName = Lang.Text("Select.Instance.Card.LabyMod");
                        else if (isLiteExists)
                            cardName = Lang.Text("Select.Instance.Card.LiteLoader");
                        else if (isQuiltExists)
                            cardName = Lang.Text("Select.Instance.Card.Quilt");
                        else
                            cardName = Lang.Text("Select.Instance.Card.Fabric");

                        break;
                    }
                    case McInstanceCardType.Error:
                    {
                        cardName = Lang.Text("Select.Instance.Card.Error");
                        break;
                    }
                    case McInstanceCardType.Hidden:
                    {
                        cardName = Lang.Text("Select.Instance.Card.Hidden");
                        break;
                    }
                    case McInstanceCardType.Rubbish:
                    {
                        cardName = Lang.Text("Select.Instance.Card.LessUsed");
                        break;
                    }
                    case McInstanceCardType.Star:
                    {
                        cardName = Lang.Text("Select.Instance.Card.Favorites");
                        break;
                    }
                    case McInstanceCardType.Fool:
                    {
                        cardName = Lang.Text("Select.Instance.Card.AprilFools");
                        break;
                    }

                    default:
                    {
                        throw new ArgumentException($"未知的卡片种类（{(int)Card.Key}）");
                    }
                }

                #endregion

                // 建立控件
                var cardTitle = $"{cardName}{(Card.Key == McInstanceCardType.Star ? "" : $" ({Lang.Number(filteredInstances.Count, "N0")})")}";
                var newCard = new WorkspaceCard { Title = cardTitle, Margin = new Thickness(0d, 0d, 0d, 15d) };
                var newStack = new StackPanel
                {
                    Margin = new Thickness(20d, WorkspaceCard.SwapedHeight, 18d, 0d),
                    VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0d, 0d),
                    Tag = filteredInstances
                };
                newCard.Children.Add(newStack);
                newCard.SwapControl = newStack;
                PanMain.Children.Add(newCard);

                // 确定卡片是否展开
                void PutMethod(StackPanel stack)
                {
                    foreach (var item in (IEnumerable)stack.Tag)
                        stack.Children.Add(McVersionListItem((McInstance)item));
                }

                ;
                if (Card.Key == McInstanceCardType.Rubbish ||
                    Card.Key == McInstanceCardType.Error ||
                    Card.Key == McInstanceCardType.Fool)
                {
                    newCard.IsSwapped = true;
                    newCard.InstallMethod = PutMethod;
                }
                else
                {
                    WorkspaceCard.StackInstall(ref newStack, PutMethod);
                }
            }

            // 若只有一个卡片，则强制展开
            if (PanMain.Children.Count == 1 && ((WorkspaceCard)PanMain.Children[0]).IsSwapped)
                ((WorkspaceCard)PanMain.Children[0]).IsSwapped = false;

            PanVerSearchBox.Visibility = hasVisibleFolders ? Visibility.Visible : Visibility.Collapsed;

            // 判断应该显示哪一个页面
            if (!hasAnyResults)
            {
                if (!originalHasInstances)
                {
                    // 完全没有实例的情况
                    PanEmpty.Visibility = Visibility.Visible;
                    PanBack.Visibility = Visibility.Collapsed;
                    if (ModInstanceList.ShowHidden)
                    {
                        LabEmptyTitle.Text = Lang.Text("Select.Instance.Hidden.EmptyTitle");
                        LabEmptyContent.Text = Lang.Text("Select.Instance.Hidden.EmptyMessage");
                        BtnEmptyDownload.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        LabEmptyTitle.Text = Lang.Text("Select.Instance.Empty.Title");
                        LabEmptyContent.Text = Lang.Text("Select.Instance.Empty.Message");
                        BtnEmptyDownload.Visibility =
                            Config.Preference.Hide.PageDownload && !ShellNavigation.RevealHidden
                                ? Visibility.Collapsed
                                : Visibility.Visible;
                    }
                }
                // 有实例但搜索无结果的情况
                else if (ModInstanceList.ShowHidden && ModInstanceList.mcInstanceList.ToArray().Any(c =>
                             c.Key == McInstanceCardType.Hidden && c.Value.Count > 0))
                {
                    // 有隐藏实例但搜索无结果 - 显示搜索无结果提示
                    PanVerSearchBox.Visibility = Visibility.Visible;
                    PanEmpty.Visibility = Visibility.Collapsed;
                    PanBack.Visibility = Visibility.Visible;
                    PanEmptySearch.Visibility = Visibility.Visible;
                    LabEmptySearchTitle.Text = Lang.Text("Select.Instance.Hidden.EmptySearchTitle");
                    LabEmptySearchContent.Text = string.IsNullOrWhiteSpace(searchText)
                        ? Lang.Text("Select.Instance.Search.EmptyInput")
                        : Lang.Text("Select.Instance.Search.NoHiddenResult", searchText);
                }
                else if (ModInstanceList.ShowHidden)
                {
                    // 无隐藏实例 - 显示"无隐藏实例"提示
                    PanEmpty.Visibility = Visibility.Visible;
                    PanBack.Visibility = Visibility.Collapsed;
                    LabEmptyTitle.Text = Lang.Text("Select.Instance.Hidden.EmptyTitle");
                    LabEmptyContent.Text = Lang.Text("Select.Instance.Hidden.EmptyMessage");
                    BtnEmptyDownload.Visibility = Visibility.Collapsed;
                    PanVerSearchBox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // 普通模式下的搜索无结果
                    PanVerSearchBox.Visibility = Visibility.Visible;
                    PanEmpty.Visibility = Visibility.Collapsed;
                    PanBack.Visibility = Visibility.Visible;
                    PanEmptySearch.Visibility = Visibility.Visible;
                    LabEmptySearchTitle.Text = Lang.Text("Select.Instance.EmptySearch.Title");
                    LabEmptySearchContent.Text = string.IsNullOrWhiteSpace(searchText)
                        ? Lang.Text("Select.Instance.Search.EmptyInput")
                        : Lang.Text("Select.Instance.Search.NoResult", searchText);
                }
            }
            else
            {
                PanBack.Visibility = Visibility.Visible;
                PanEmpty.Visibility = Visibility.Collapsed;
                PanEmptySearch.Visibility = Visibility.Collapsed;
            } // 有结果时隐藏
        }


        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Instance.Error.UiUpdate"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Instance.Error.UiUpdate"));
        }
    }

    public static SelectionRow McVersionListItem(McInstance mcInstance)
    {
        var newItem = new SelectionRow
        {
            Title = mcInstance.Name, Info = mcInstance.Desc, Height = 42d, Tag = mcInstance, SnapsToDevicePixels = true,
            Type = SelectionRow.CheckType.Clickable
        };
        var instanceInfo = mcInstance.Info;
        var tags = new List<string>();
        tags.Add(instanceInfo.VanillaName);
        if (instanceInfo.HasForge)
            tags.Add("Forge " + instanceInfo.Forge);
        else if (instanceInfo.HasNeoForge)
            tags.Add("NeoForge " + instanceInfo.NeoForge);
        else if (instanceInfo.HasCleanroom)
            tags.Add("Cleanroom " + instanceInfo.Cleanroom);
        else if (instanceInfo.HasLabyMod)
            tags.Add("LabyMod " + instanceInfo.LabyMod);
        else if (instanceInfo.HasQuilt)
            tags.Add("Quilt " + instanceInfo.Quilt);
        else if (instanceInfo.HasFabric) tags.Add("Fabric " + instanceInfo.Fabric);
        if (instanceInfo.HasLiteLoader)
            tags.Add("LiteLoader");
        if (instanceInfo.HasOptiFine)
            tags.Add("OptiFine " + instanceInfo.OptiFine);
        newItem.Tags = tags;
        try
        {
            if (mcInstance.Logo.EndsWith(@"PCL\Logo.png"))
                newItem.Logo = mcInstance.PathInstance + @"PCL\Logo.png"; // 修复老版本中，存储的自定义 Logo 使用完整路径，导致移动后无法加载的 Bug
            else
                newItem.Logo = mcInstance.Logo;
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Instance.Error.IconLoad"),
                ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Select.Instance.Error.IconLoad"));
            newItem.Logo = "pack://application:,,,/images/Blocks/RedstoneBlock.png";
        }

        newItem.ContentHandler = McVersionListContent;
        return newItem;
    }

    private static void McVersionListContent(SelectionRow sender, EventArgs e)
    {
        var version = (McInstance)sender.Tag;
        // 注册点击事件
        sender.Click += (a, b) => Item_Click((SelectionRow)a, b);
        // 图标按钮
        var btnStar = new SymbioIconButton();
        if (version.IsStar)
        {
            btnStar.ToolTip = Lang.Text("Select.Instance.Unfavorite");
            ToolTipService.SetPlacement(btnStar, PlacementMode.Center);
            ToolTipService.SetVerticalOffset(btnStar, 30d);
            ToolTipService.SetHorizontalOffset(btnStar, 2d);
            btnStar.LogoScale = 1.1d;
            btnStar.SvgIcon = "lucide/heart-filled";
        }
        else
        {
            btnStar.ToolTip = Lang.Text("Select.Instance.Favorite");
            ToolTipService.SetPlacement(btnStar, PlacementMode.Center);
            ToolTipService.SetVerticalOffset(btnStar, 30d);
            ToolTipService.SetHorizontalOffset(btnStar, 2d);
            btnStar.LogoScale = 1.1d;
            btnStar.SvgIcon = "lucide/heart";
        }

        btnStar.Click += (_, _) =>
        {
            States.Instance.Starred[version.PathInstance] = !version.IsStar;
            ModInstanceList.mcInstanceListForceRefresh = true;
            ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\");
        };
        var btnOpenFolder = new SymbioIconButton { LogoScale = 1.1d, SvgIcon = "lucide/folder-open" };
        btnOpenFolder.ToolTip = Lang.Text("Select.Instance.OpenFolder");
        ToolTipService.SetPlacement(btnOpenFolder, PlacementMode.Center);
        ToolTipService.SetVerticalOffset(btnOpenFolder, 30d);
        ToolTipService.SetHorizontalOffset(btnOpenFolder, 2d);
        btnOpenFolder.Click += (_, _) => InstanceOverviewView.OpenVersionFolder(version);
        var btnDel = new SymbioIconButton { LogoScale = 1.1d, SvgIcon = "lucide/trash-2" };
        btnDel.ToolTip = Lang.Text("Common.Action.Delete");
        ToolTipService.SetPlacement(btnDel, PlacementMode.Center);
        ToolTipService.SetVerticalOffset(btnDel, 30d);
        ToolTipService.SetHorizontalOffset(btnDel, 2d);
        btnDel.Click += (_, _) => DeleteVersion(sender, version);
        if (version.state != McInstanceState.Error)
        {
            var btnCont = new SymbioIconButton { LogoScale = 1.1d, SvgIcon = "lucide/settings" };
            btnCont.ToolTip = Lang.Text("Select.Instance.Settings");
            ToolTipService.SetPlacement(btnCont, PlacementMode.Center);
            ToolTipService.SetVerticalOffset(btnCont, 30d);
            ToolTipService.SetHorizontalOffset(btnCont, 2d);
            btnCont.Click += (_, _) =>
            {
                InstanceWorkspace.Current = version;
                ModMain.frmMain.PageChange(FormMain.PageType.InstanceSetup);
            };
            sender.MouseRightButtonUp += (_, _) =>
            {
                InstanceWorkspace.Current = version;
                ModMain.frmMain.PageChange(FormMain.PageType.InstanceSetup);
            };
            sender.Buttons = new[] { btnStar, btnOpenFolder, btnDel, btnCont };
        }
        else
        {
            var btnCont = new SymbioIconButton { LogoScale = 1.15d, SvgIcon = "lucide/folder-open" };
            btnCont.ToolTip = Lang.Text("Common.Action.OpenFolder");
            ToolTipService.SetPlacement(btnCont, PlacementMode.Center);
            ToolTipService.SetVerticalOffset(btnCont, 30d);
            ToolTipService.SetHorizontalOffset(btnCont, 2d);
            btnCont.Click += (_, _) => InstanceOverviewView.OpenVersionFolder(version);
            sender.MouseRightButtonUp += (_, _) => InstanceOverviewView.OpenVersionFolder(version);
            sender.Buttons = new[] { btnStar, btnOpenFolder, btnDel, btnCont };
        }
    }

    #endregion

    #region 页面事件

    // 点击选项
    public static void Item_Click(SelectionRow sender, EventArgs e)
    {
        var instance = (McInstance)sender.Tag;
        if (new McInstance(instance.PathInstance).Check())
        {
            // 正常实例
            ModInstanceList.McMcInstanceSelected = instance;
            States.Game.SelectedInstance = ModInstanceList.McMcInstanceSelected.Name;
            ModMain.frmMain.PageBack();
        }
        else
        {
            // 错误实例
            InstanceOverviewView.OpenVersionFolder(instance);
        }
    }

    private void BtnDownload_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall);
    }

    // 修改此代码时，同时修改 InstanceOverviewView 中的代码
    public static void DeleteVersion(SelectionRow item, McInstance mcInstance)
    {
        try
        {
            var isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            var isHintIndie = mcInstance.state != McInstanceState.Error &&
                              (mcInstance.PathIndie ?? "") != (ModFolder.mcFolderSelected ?? "");
            var confirmMsg = isShiftPressed
                ? Lang.Text("Select.Instance.Delete.ConfirmPermanentMessage", mcInstance.Name)
                : Lang.Text("Select.Instance.Delete.ConfirmMessage", mcInstance.Name);
            var confirmFullMsg = confirmMsg +
                                 (isHintIndie ? "\r\n" + Lang.Text("Select.Instance.Delete.IsolatedWarning") : "");
            switch (ModMain.MyMsgBox(confirmFullMsg, Lang.Text("Select.Instance.Delete.ConfirmTitle"),
                        button2: Lang.Text("Common.Action.Cancel"), isWarn: true))
            {
                case 1:
                {
                    ModBase.IniClearCache(Path.Combine(mcInstance.PathIndie, "options.txt"));
                    ((DynamicCacheConfigStorage)ConfigService.GetProvider(ConfigSource.GameInstance)).InvalidateCache(
                        mcInstance.PathInstance);
                    if (isShiftPressed)
                    {
                        ModBase.DeleteDirectory(mcInstance.PathInstance);
                        HintService.Hint(Lang.Text("Select.Instance.Delete.PermanentSuccess", mcInstance.Name),
                            HintType.Success);
                    }
                    else
                    {
                        FileSystem.DeleteDirectory(mcInstance.PathInstance, UIOption.AllDialogs,
                            RecycleOption.SendToRecycleBin);
                        HintService.Hint(Lang.Text("Select.Instance.Delete.RecycleBinSuccess", mcInstance.Name),
                            HintType.Success);
                    }

                    break;
                }
                case 2:
                {
                    return;
                }
            }

            // 从 UI 中移除
            if (mcInstance.displayType == McInstanceCardType.Hidden || !mcInstance.IsStar)
            {
                // 仅出现在当前卡片
                var parent = (StackPanel)item.Parent;
                if (parent.Children.Count > 2) // 当前的项目与一个占位符
                {
                    // 删除后还有剩
                    var card = (WorkspaceCard)parent.Parent;
                    card.Title = card.Title.Replace(Lang.Number(parent.Children.Count - 1, "N0"),
                        Lang.Number(parent.Children.Count - 2, "N0")); // 有一个占位符
                    parent.Children.Remove(item);
                    if (ModInstanceList.McMcInstanceSelected is not null && (mcInstance.PathInstance ?? "") ==
                        (ModInstanceList.McMcInstanceSelected.PathInstance ?? ""))
                        // 删除当前实例就更改选择
                        ModInstanceList.McMcInstanceSelected = (McInstance)((SelectionRow)parent.Children[0]).Tag;
                    ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                        ModLoader.LoaderFolderRunType.UpdateOnly, 1, @"versions\");
                }
                else
                {
                    // 删除后没剩了
                    ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                        ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\");
                }
            }
            else
            {
                // 同时出现在当前卡片与收藏夹
                ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, ModFolder.mcFolderSelected,
                    ModLoader.LoaderFolderRunType.ForceRun, 1, @"versions\");
            }
        }
        catch (OperationCanceledException ex)
        {
            ModBase.Log(ex, $"删除实例 {mcInstance.Name} 被主动取消");
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Instance.Error.Delete", mcInstance.Name),
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Select.Instance.Error.Delete", mcInstance.Name));
        }
    }

    public void RefreshDownloadVisibility()
    {
        var newVisibility = (Config.Preference.Hide.PageDownload && !ShellNavigation.RevealHidden) || ModInstanceList.ShowHidden
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (BtnEmptyDownload.Visibility != newVisibility)
        {
            BtnEmptyDownload.Visibility = newVisibility;
            PanEmpty.TriggerForceResize();
        }
    }

    #endregion
}
