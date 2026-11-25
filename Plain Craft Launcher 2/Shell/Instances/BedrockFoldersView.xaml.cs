using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class BedrockFoldersView
{
    private readonly List<ModBedrockFolders.Folder> _folders = [];
    private bool _actionsBuilt;
    private bool _foldersBuilt;
    private List<string> _folderSignature = [];

    public BedrockFoldersView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_actionsBuilt)
            {
                _actionsBuilt = true;
                BuildActions();
            }
            BedrockLaunchSession.Current.LibraryChanged += RefreshFolders;
            RefreshFolders();
        };
        Unloaded += (_, _) => BedrockLaunchSession.Current.LibraryChanged -= RefreshFolders;
    }

    internal void RefreshFolders()
    {
        var folders = ModBedrockFolders.Enumerate();

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

    }

    private void RebuildFolderItems()
    {
        PanFolders.Children.Clear();
        foreach (var folder in _folders)
        {
            // 与 Java 版一致：不显示图标，标题就是文件夹本身的名字
            var item = new SelectionRow
            {
                Title = folder.Title,
                Info = folder.Path,
                Type = SelectionRow.CheckType.RadioBox,
                Height = 40
            };
            var target = folder;
            item.Click += (_, args) =>
            {
                args.Handled = true;
                SwitchFolder(target);
            };
            PanFolders.Children.Add(item);
            if (ModBedrockFolders.IsCurrent(folder)) item.SetChecked(true, false, false);
        }
    }

    /// <summary>只更新勾选状态，不动已经生成好的列表项。</summary>
    private void SyncFolderChecks()
    {
        for (var i = 0; i < _folders.Count && i < PanFolders.Children.Count; i++)
        {
            if (PanFolders.Children[i] is not SelectionRow item) continue;
            var shouldCheck = ModBedrockFolders.IsCurrent(_folders[i]);
            if (item.Checked != shouldCheck) item.SetChecked(shouldCheck, false, false);
        }
    }

    /// <summary>左下角「添加或导入」：与 Java 版一致的列表项，而不是一个普通按钮。</summary>
    private void BuildActions()
    {
        var addItem = new SelectionRow
        {
            IsScaleAnimationEnabled = false,
            Type = SelectionRow.CheckType.Clickable,
            Title = Lang.Text("Bedrock.Select.AddFolder"),
            Height = 34,
            ToolTip = Lang.Text("Bedrock.Select.AddFolder.ToolTip"),
            LogoScale = 0.9,
            SvgIcon = "lucide/folder-input"
        };
        ToolTipService.SetPlacement(addItem, PlacementMode.Right);
        addItem.Click += (_, _) => AddFolder();
        PanActions.Children.Add(addItem);

        var importItem = new SelectionRow
        {
            IsScaleAnimationEnabled = false,
            Type = SelectionRow.CheckType.Clickable,
            Title = Lang.Text("Bedrock.Action.Import"),
            Height = 34,
            ToolTip = Lang.Text("Bedrock.Select.Import.ToolTip"),
            LogoScale = 0.9,
            SvgIcon = "lucide/package-plus"
        };
        ToolTipService.SetPlacement(importItem, PlacementMode.Right);
        importItem.Click += (_, _) => ModMain.frmMain.ImportBedrockPackage();
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
    }
}
