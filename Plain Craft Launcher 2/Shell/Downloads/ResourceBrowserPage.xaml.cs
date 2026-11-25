using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using PCL.Core.App.Localization;

namespace PCL;

public partial class ResourceBrowserPage
{
    /// <summary>
    ///     每页展示的结果数量。
    /// </summary>
    private readonly ResourceSearchSession session;
    private ModComp.CompProjectStorage storage => session.Storage;
    private int page => session.Page;
    private const int pageSize = ResourceSearchSession.PageSize;
    private ModLoader.LoaderTask<ModComp.CompProjectRequest, int> loader => session.Loader;
    internal void RefreshSearch() => session.Refresh();

    // 结果 UI 化
    private void Load_OnFinish()
    {
        try
        {
            ModBase.Log($"[Comp] 开始可视化{TypeNameSpaced}列表，已储藏 {storage.results.Count} 个结果，当前在第 {page + 1} 页");
            // 列表项
            PanProjects.Children.Clear();
            var index = Math.Min(page * pageSize, storage.results.Count);
            foreach (var result in storage.results.GetRange(index, Math.Min(storage.results.Count - index, pageSize)))
            {
                var showQuickDownload = result.Type != ModComp.CompType.ModPack &&
                                        result.Type != ModComp.CompType.DataPack;
                PanProjects.Children.Add(result.ToCompItem(loader.input.gameVersion is null,
                    loader.input.modLoader == ModComp.CompLoaderType.Any &&
                    (PageType == ModComp.CompType.Mod || PageType == ModComp.CompType.ModPack),
                    showQuickDownload, new ResourceSearchOrigin(PageType, loader.input.gameVersion, loader.input.modLoader)));
            }
            // 页码
            CardPages.Visibility =
                storage.results.Count > 40 || storage.curseForgeOffset < storage.curseForgeTotal ||
                storage.modrinthOffset < storage.modrinthTotal
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            LabPage.Text = Lang.Number(page + 1, "N0");
            BtnPageFirst.IsEnabled = page > 1;
            BtnPageFirst.Opacity = page > 1 ? 1d : 0.2d;
            BtnPageLeft.IsEnabled = page > 0;
            BtnPageLeft.Opacity = page > 0 ? 1d : 0.2d;
            var isRightEnabled = storage.results.Count > pageSize * (page + 1) ||
                                 storage.curseForgeOffset < storage.curseForgeTotal ||
                                 storage.modrinthOffset <
                                 storage.modrinthTotal; // 由于 WPF 的未知 bug，读取到的 IsEnabled 可能是错误的值（#3319）
            BtnPageRight.IsEnabled = isRightEnabled;
            BtnPageRight.Opacity = isRightEnabled ? 1d : 0.2d;
            // 错误信息
            if (storage.errorMessage is null)
            {
                HintError.Visibility = Visibility.Collapsed;
            }
            else
            {
                HintError.Visibility = Visibility.Visible;
                HintError.Text = storage.errorMessage;
            }

            // 强制返回顶部
            ResultsScroll.ScrollToTop();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                $"可视化{TypeNameSpaced}列表出错",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Download.Comp.Error.OperationFailed"));
        }
    }

    // 自动重试
    private void Load_State(object sender, LoadingStatus state, LoadingStatus oldState)
    {
        switch (loader.State)
        {
            case ModBase.LoadState.Failed:
            {
                var errorMessage = "";
                if (loader.Error is not null)
                    errorMessage = loader.Error.Message;
                if (errorMessage.Contains(Lang.Text("Common.Error.InvalidJson")))
                {
                    ModBase.Log($"[Download] 下载的{TypeNameSpaced}列表 json 文件损坏，已自动重试", ModBase.LogLevel.Debug);
                    PageLoaderRestart();
                }

                break;
            }
        }
    }

    // 切换页码
    private void BtnPageFirst_Click(object sender, EventArgs e)
    {
        ChangePage(0);
    }

    private void BtnPageLeft_Click(object sender, EventArgs e)
    {
        ChangePage(page - 1);
    }

    private void BtnPageRight_Click(object sender, EventArgs e)
    {
        ChangePage(page + 1);
    }

    private void ChangePage(int newPage)
    {
        ModMain.frmMain.BackToTop();
        session.ChangePage(newPage, ReadQuery());
    }

    // 安装已有整合包按钮
    private void BtnSearchInstallModPack_Click(object sender, EventArgs e)
    {
        ModModpack.ModpackInstall();
    }

    /// <summary>
    ///     刷新所有已显示项目的收藏状态
    /// </summary>
    public void RefreshAllFavoriteStatus()
    {
        try
        {
            foreach (var item in PanProjects.Children)
                if (item is ResourceProjectRow)
                    ((ResourceProjectRow)item).RefreshFavoriteStatus();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "刷新收藏状态时出错");
        }
    }

    #region 属性

    /// <summary>
    ///     用于 XAML 快速设置的 Tag 下拉框列表。
    /// </summary>
    public bool SupportCurseForge { get; }
    public bool SupportModrinth { get; }

    /// <summary>
    ///     英文前后不含空格的可读资源类型名，例如 "Mod"、"整合包"。
    /// </summary>
    public string TypeName => ModComp.GetCompTypeName(PageType);

    /// <summary>
    ///     英文前后含一个空格的可读资源类型名，例如 " Mod "、"整合包"。
    /// </summary>
    public string TypeNameSpaced => TypeName;

    /// <summary>
    ///     该页面对应的资源类型。
    /// </summary>
    public ModComp.CompType PageType => session.Type;
    private bool isLoaderInited;

    internal ResourceBrowserPage(ModComp.CompType type, bool curseForge, bool modrinth)
    {
        session = new ResourceSearchSession(type);
        SupportCurseForge = curseForge;
        SupportModrinth = modrinth;
        Loaded += PageCompControls_Inited;
        IsVisibleChanged += PageComp_IsVisibleChanged;
        InitializeComponent();
        BtnSearchInstallModPack.Visibility = type == ModComp.CompType.ModPack ? Visibility.Visible : Visibility.Collapsed;
        PanSearchBox.HintText = ModComp.GetCompSearchName(type);
        Load.Text = ModComp.GetCompLoadingName(type);
        foreach (var filter in ResourceSearchSession.Filters(type))
        {
            var option = new SelectOption { Tag = filter.Tag, IsEnabled = filter.Enabled, Margin = new Thickness(filter.Indent, 0, 0, 0) };
            option.SetResourceReference(ContentControl.ContentProperty, filter.Label);
            if (filter.Tooltip is not null)
            {
                option.SetResourceReference(ToolTipProperty, filter.Tooltip);
                ToolTipService.SetHorizontalOffset(option, filter.TooltipOffset);
            }
            ComboSearchTag.Items.Add(option);
        }
        ComboSearchTag.SelectedIndex = 0;
        Load.StateChanged += Load_State;
        BtnPageFirst.Click += BtnPageFirst_Click;
        BtnPageLeft.Click += BtnPageLeft_Click;
        BtnPageRight.Click += BtnPageRight_Click;
        PanSearchBox.Search += (_, _) => StartNewSearch();
        TextSearchVersion.KeyDown += EnterTrigger;
        BtnSearchReset.Click += (_, _) => ResetFilter();
        BtnSearchInstallModPack.Click += BtnSearchInstallModPack_Click;
    }

    private void PageCompControls_Inited(object sender, EventArgs e)
    {
        // 不知道从 Initialized 改成 Loaded 会不会有问题，但用 Initialized 会导致初始的筛选器修改被覆盖回默认值
        if (ResourceSearchSession.SuggestedInstance is { } targetVersion)
        {
            // 设置目标
            ResetFilter(); // 重置筛选器
            TextSearchVersion.Text = targetVersion.Info.VanillaName;

            SelectOption GetTargetItemByName(string name)
            {
                foreach (SelectOption Item in ComboSearchLoader.Items)
                    if (string.Equals(Item.Content?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        return Item;
                return (SelectOption)ComboSearchLoader.Items[0];
            }

            ;
            if (targetVersion.Info.HasForge)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("Forge");
            else if (targetVersion.Info.HasFabric)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("Fabric");
            else if (targetVersion.Info.HasNeoForge)
                ComboSearchLoader.SelectedItem = GetTargetItemByName("NeoForge");
            else if (targetVersion.Info.HasQuilt) ComboSearchLoader.SelectedItem = GetTargetItemByName("Quilt");
            ResourceSearchSession.SuggestedInstance = null;
            // 如果已经完成请求，则重新开始
            if (isLoaderInited)
                StartNewSearch();
            ResultsScroll.ScrollToHome();
        }

        // 加载器初始化
        if (isLoaderInited)
            return;
        isLoaderInited = true;
        session.Query = ReadQuery();
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, loader, _ => Load_OnFinish(),
            session.Request);
        // 将最高 Drop 加入筛选
        if (!ModComp.IsBedrockType(PageType) && ModDownload.AllDrops is not null && ModDownload.AllDrops.Count != 0 &&
            ModDownload.AllDrops.First() > 250)
        {
            var highestVersion = McInstanceInfo.DropToVersion(ModDownload.AllDrops.First());
            if ((((SelectOption)TextSearchVersion.Items[1]).Content.ToString() ?? "") !=
                (highestVersion ?? "")) // 0 是全部
                TextSearchVersion.Items.Insert(1, new SelectOption { Content = highestVersion });
        }

        // 根据页面类型控制加载器选择的显示
        if (PageType == ModComp.CompType.Shader)
        {
            LabLoader.Visibility = Visibility.Visible;
            ComboSearchLoader.Visibility = Visibility.Collapsed;
            ComboSearchShaderLoader.Visibility = Visibility.Visible;
        }
        else if (PageType == ModComp.CompType.Mod || PageType == ModComp.CompType.ModPack)
        {
            LabLoader.Visibility = Visibility.Visible;
            ComboSearchLoader.Visibility = Visibility.Visible;
            ComboSearchShaderLoader.Visibility = Visibility.Collapsed;
        }
        else
        {
            LabLoader.Visibility = Visibility.Collapsed;
            ComboSearchLoader.Visibility = Visibility.Collapsed;
            ComboSearchShaderLoader.Visibility = Visibility.Collapsed;
        }

        // 基岩版资源与 Java 版版本号无关，隐藏版本筛选
        if (ModComp.IsBedrockType(PageType))
        {
            LabVersion.Visibility = Visibility.Collapsed;
            TextSearchVersion.Visibility = Visibility.Collapsed;
        }
    }

    private void PageComp_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 当页面变为可见时刷新收藏按钮状态
        if (IsVisible) RefreshAllFavoriteStatus();
    }

    private ResourceQuery ReadQuery()
    {
        var gameVersion = TextSearchVersion.Text == Lang.Text("Download.Comp.Filter.Version.AllInputAvailable") ? null :
            TextSearchVersion.Text.Contains(".") || TextSearchVersion.Text.Contains("w") ? TextSearchVersion.Text :
            null;
        if (ModComp.IsBedrockType(PageType))
            gameVersion = null; // 基岩版资源不按 Java 版本筛选
        var modLoader = ModComp.CompLoaderType.Any;
        if (PageType == ModComp.CompType.Mod || PageType == ModComp.CompType.ModPack) // 只有 Mod 考虑加载器
        {
            modLoader = (ModComp.CompLoaderType)ModBase.Val(((SelectOption)ComboSearchLoader.SelectedItem).Tag);
        }

        var tag = (ComboSearchTag.SelectedItem as FrameworkElement)?.Tag?.ToString() ?? "";
        var shader = (ComboSearchShaderLoader.SelectedItem as FrameworkElement)?.Tag?.ToString();
        if (PageType == ModComp.CompType.Shader && !string.IsNullOrEmpty(shader)) tag += shader;
        return new ResourceQuery(PanSearchBox.Text, gameVersion, tag, modLoader,
            (ModComp.CompSourceType)ModBase.Val(((SelectOption)ComboSearchSource.SelectedItem).Tag),
            (ModComp.CompSortType)ModBase.Val(((SelectOption)ComboSearchSort.SelectedItem).Tag));
    }

    #endregion

    #region 搜索

    // 搜索按钮
    private void StartNewSearch()
    {
        session.Search(ReadQuery());
    }

    private void EnterTrigger(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            StartNewSearch();
    }

    // 重置按钮
    private void ResetFilter()
    {
        PanSearchBox.Text = "";
        TextSearchVersion.Text = Lang.Text("Download.Comp.Filter.Version.AllInputAvailable");
        TextSearchVersion.SelectedIndex = 0;
        ComboSearchSource.SelectedIndex = 0;
        ComboSearchTag.SelectedIndex = 0;
        ComboSearchLoader.SelectedIndex = 0;
        ComboSearchShaderLoader.SelectedIndex = 0;
        ComboSearchSort.SelectedIndex = 0;
        loader.lastFinishedTime = 0L; // 要求强制重新开始
    }

    private void BtnSearchReset_Click(object sender, EventArgs e)
    {
        ResetFilter();
    }

    #endregion
}
