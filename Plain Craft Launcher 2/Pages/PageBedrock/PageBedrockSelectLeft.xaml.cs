using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockSelectLeft : IRefreshable
{
    private readonly List<ModBedrockFolders.Folder> _folders = [];
    private bool _actionsBuilt;
    private bool _foldersBuilt;
    private List<string> _folderSignature = [];
    // 配置写入是异步的（会晚一两秒落盘），界面上的"当前目录"不能每次都回读配置，
    // 否则切换后回读拿到旧值，勾选就会被弹回上一个目录
    private string? _currentPath;
    private string? _lastConfigPath;

    public PageBedrockSelectLeft()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // 列表项在页面加载后创建，与实例条目保持同一套资源与动画状态
            if (!_actionsBuilt)
            {
                _actionsBuilt = true;
                BuildActions();
            }

            RefreshFolders(false);
        };
    }

    public void Refresh()
    {
        RefreshFolders(true);
    }

    /// <summary>页面认为的当前目录：优先跟随用户刚点中的目录，配置追上后再以配置为准。</summary>
    private bool IsFolderCurrent(ModBedrockFolders.Folder folder) =>
        _currentPath is not null && ModBedrockFolders.SamePath(folder.Path, _currentPath);

    private void TrackCurrentFolder()
    {
        var configPath = ModBedrockInstance.DataRoot;
        // 只有配置路径真的变了（写入落盘或别处改过）才跟随，否则保留刚点中的目录
        if (!ModBedrockFolders.SamePath(configPath, _lastConfigPath ?? ""))
        {
            _lastConfigPath = configPath;
            _currentPath = configPath;
        }

        _currentPath ??= configPath;
        _lastConfigPath ??= configPath;
    }

    private void RefreshFolders(bool refreshInstances)
    {
        var folders = ModBedrockFolders.Enumerate();
        TrackCurrentFolder();

        // 目录集合没变时只同步勾选状态，避免整列表反复重建造成闪烁
        var signature = folders.Select(folder => folder.Path + "|" + folder.InstanceCount).ToList();
        var unchanged = _foldersBuilt && signature.SequenceEqual(_folderSignature);

        _folders.Clear();
        _folders.AddRange(folders);

        if (unchanged)
        {
            SyncFolderChecks();
        }
        else
        {
            _foldersBuilt = true;
            _folderSignature = signature;
            RebuildFolderItems();
        }

        if (refreshInstances) ModMain.frmBedrockSelect?.RefreshInstances();
    }

    private void RebuildFolderItems()
    {
        PanFolders.Children.Clear();
        foreach (var folder in _folders)
        {
            // 与 Java 版一致：不显示图标，标题就是文件夹本身的名字
            var item = new MyListItem
            {
                Title = folder.Title,
                Info = folder.Path,
                Type = MyListItem.CheckType.RadioBox,
                Height = 40
            };
            var target = folder;
            item.Click += (_, _) => SwitchFolder(target);
            PanFolders.Children.Add(item);
            if (IsFolderCurrent(folder)) item.SetChecked(true, false, false);
        }
    }

    /// <summary>只更新勾选状态，不动已经生成好的列表项。</summary>
    private void SyncFolderChecks()
    {
        for (var i = 0; i < _folders.Count && i < PanFolders.Children.Count; i++)
        {
            if (PanFolders.Children[i] is not MyListItem item) continue;
            var shouldCheck = IsFolderCurrent(_folders[i]);
            if (item.Checked != shouldCheck) item.SetChecked(shouldCheck, false, false);
        }
    }

    /// <summary>左下角「添加或导入」：与 Java 版一致的列表项，而不是一个普通按钮。</summary>
    private void BuildActions()
    {
        var addItem = new MyListItem
        {
            IsScaleAnimationEnabled = false,
            Type = MyListItem.CheckType.Clickable,
            Title = Lang.Text("Bedrock.Select.AddFolder"),
            Height = 34,
            ToolTip = Lang.Text("Bedrock.Select.AddFolder.ToolTip"),
            LogoScale = 0.9,
            SvgIcon = "lucide/folder-input"
        };
        ToolTipService.SetPlacement(addItem, PlacementMode.Right);
        addItem.Click += (_, _) => AddFolder();
        PanActions.Children.Add(addItem);

        var importItem = new MyListItem
        {
            IsScaleAnimationEnabled = false,
            Type = MyListItem.CheckType.Clickable,
            Title = Lang.Text("Bedrock.Action.Import"),
            Height = 34,
            ToolTip = Lang.Text("Bedrock.Select.Import.ToolTip"),
            LogoScale = 0.9,
            SvgIcon = "lucide/package-plus"
        };
        ToolTipService.SetPlacement(importItem, PlacementMode.Right);
        importItem.Click += (_, _) => ModMain.frmBedrockRight?.ImportPackage();
        PanActions.Children.Add(importItem);
    }

    private void SwitchFolder(ModBedrockFolders.Folder folder)
    {
        if (!Directory.Exists(folder.Path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderInvalid"), HintType.Error);
            return;
        }

        if (ModBedrockFolders.IsCurrent(folder)) return;

        ModBedrockFolders.Switch(folder);
        // 换了目录就重新挑实例，避免残留上一个目录里的选择
        ModBedrockInstance.SelectedVersionId = "";
        // 配置写入是异步的，勾选先以用户点中的目录为准，等配置落盘后 TrackCurrentFolder 再跟随
        _currentPath = folder.Path;
        RefreshFolders(false);
        // 直接按用户点中的目录读取，不再绕一次全局配置，避免切换后列表仍是旧目录的内容
        ModMain.frmBedrockSelect?.RefreshInstances(folder.Path);
        HintService.Hint(Lang.Text("Bedrock.Select.Applied", folder.Title), HintType.Success);
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.Text("Bedrock.Select.AddFolder"),
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderMissing"), HintType.Error);
            return;
        }

        if (!ModBedrockFolders.LooksValid(path))
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderInvalid"), HintType.Error);
            return;
        }

        if (ModBedrockFolders.Add(path) is null)
        {
            HintService.Hint(Lang.Text("Bedrock.Select.FolderExists"), HintType.Warning);
            return;
        }

        HintService.Hint(Lang.Text("Bedrock.Select.FolderAdded", path), HintType.Success);
        RefreshFolders(true);
    }
}
