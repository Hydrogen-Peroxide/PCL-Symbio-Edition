using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.Utils.Validate;
using PCL.Network;

namespace PCL;

public partial class InstanceFoldersView
{
    private List<ModFolder.McFolder>? mcFolderListLast;
    private string? selectedFolder;

    public InstanceFoldersView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ModFolder.mcFolderListLoader.PreviewFinish += FolderListReady;
            McFolderListUI();
        };
        Unloaded += (_, _) => ModFolder.mcFolderListLoader.PreviewFinish -= FolderListReady;
    }

    private void FolderListReady(ModLoader.LoaderBase loader) => ModBase.RunInUi(() =>
    {
        if (IsLoaded) McFolderListUI();
    });

    private void McFolderListUI()
    {
        try
        {
            var folders = ModFolder.mcFolderList.ToArray();
            var current = ModFolder.mcFolderSelected;
            if (mcFolderListLast is not null && mcFolderListLast.SequenceEqual(folders) && selectedFolder == current)
                return;

            mcFolderListLast = new List<ModFolder.McFolder>(folders);
            selectedFolder = current;

            // 创建 UI
            PanList.Children.Clear();

            // 文件夹列表标题
            PanList.Children.Add(new TextBlock
            {
                Text = Lang.Text("Select.Folder.ListTitle"),
                Margin = new Thickness(13, 18, 5, 4),
                Opacity = 0.6,
                FontSize = 12
            });

            for (var i = 0; i < folders.Length; i++)
            {
                var folder = folders[i];

                // 创建 ContextMenu
                var contMenu = new ContextMenu();

                // 添加菜单项
                void AddMenuItem(string name, string header, string svgIcon = null, Thickness? padding = null,
                    RoutedEventHandler clickHandler = null)
                {
                    var item = new SymbioMenuAction
                    {
                        Name = name,
                        Header = header,
                        SvgIcon = svgIcon,
                        Padding = padding ?? new Thickness(0)
                    };
                    if (clickHandler is not null)
                        item.Click += clickHandler;
                    contMenu.Items.Add(item);
                }

                const string iconRestore = "lucide/rotate-ccw";
                const string iconRename = "lucide/pencil";
                const string iconMoveup = "lucide/move-up";
                const string iconMovedown = "lucide/move-down";
                const string iconOpen = "lucide/folder-open";
                const string iconRefresh = "lucide/refresh-cw";
                const string iconDelete = "lucide/trash-2";
                const string iconRemove = "lucide/list-x";

                switch (folder.type)
                {
                    case ModFolder.McFolder.Types.Original:
                        AddMenuItem("Rename", Lang.Text("Select.Folder.Rename"), iconRename, new Thickness(0, 2, 0, 0),
                            Rename_Click);
                        AddMenuItem("MoveUp", Lang.Text("Select.Folder.MoveUp"), iconMoveup, null,
                            MoveUp_Click);
                        AddMenuItem("MoveDown", Lang.Text("Select.Folder.MoveDown"), iconMovedown, null,
                            MoveDown_Click);
                        AddMenuItem("Open", Lang.Text("Common.Action.Open"), iconOpen, null,
                            Open_Click);
                        AddMenuItem("Refresh", Lang.Text("Common.Action.Refresh"), iconRefresh, null,
                            Refresh_Click);
                        AddMenuItem("Delete",
                            folders.Length == 1 &&
                            folder.Location == Path.Combine(ModBase.exePath, ".minecraft") + @"\"
                                ? Lang.Text("Select.Folder.Clear")
                                : Lang.Text("Common.Action.Delete"), iconDelete, new Thickness(0, 0, 0, 2),
                            Delete_Click);
                        break;

                    case ModFolder.McFolder.Types.RenamedOriginal:
                        AddMenuItem("Restore", Lang.Text("Select.Folder.RestoreName"), iconRestore,
                            new Thickness(0, 2, 0, 0),
                            Restore_Click);
                        AddMenuItem("Rename", Lang.Text("Select.Folder.Rename"), iconRename, null,
                            Rename_Click);
                        AddMenuItem("MoveUp", Lang.Text("Select.Folder.MoveUp"), iconMoveup, null,
                            MoveUp_Click);
                        AddMenuItem("MoveDown", Lang.Text("Select.Folder.MoveDown"), iconMovedown, null,
                            MoveDown_Click);
                        AddMenuItem("Open", Lang.Text("Common.Action.Open"), iconOpen, null,
                            Open_Click);
                        AddMenuItem("Refresh", Lang.Text("Common.Action.Refresh"), iconRefresh, null,
                            Refresh_Click);
                        AddMenuItem("Delete", Lang.Text("Common.Action.Delete"), iconDelete, new Thickness(0, 0, 0, 2),
                            Delete_Click);
                        break;

                    case ModFolder.McFolder.Types.Custom:
                        AddMenuItem("Rename", Lang.Text("Select.Folder.Rename"), iconRename, new Thickness(0, 2, 0, 0),
                            Rename_Click);
                        AddMenuItem("MoveUp", Lang.Text("Select.Folder.MoveUp"), iconMoveup, null,
                            MoveUp_Click);
                        AddMenuItem("MoveDown", Lang.Text("Select.Folder.MoveDown"), iconMovedown, null,
                            MoveDown_Click);
                        AddMenuItem("Open", Lang.Text("Common.Action.Open"), iconOpen, null,
                            Open_Click);
                        AddMenuItem("Refresh", Lang.Text("Common.Action.Refresh"), iconRefresh, null,
                            Refresh_Click);
                        AddMenuItem("Remove", Lang.Text("Select.Folder.RemoveFromList"), iconRemove,
                            null, Remove_Click);
                        AddMenuItem("Delete", Lang.Text("Common.Action.Delete"), iconDelete, new Thickness(0, 0, 0, 2),
                            Delete_Click);
                        break;
                }

                // 控制上移下移显示
                var moveUpItem = contMenu.Items.OfType<SymbioMenuAction>().FirstOrDefault(x => x.Name == "MoveUp");
                var moveDownItem = contMenu.Items.OfType<SymbioMenuAction>().FirstOrDefault(x => x.Name == "MoveDown");

                // 如果是第一个项目，隐藏上移按钮
                if (i == 0) moveUpItem.Visibility = Visibility.Collapsed;

                // 如果是最后一个项目，隐藏下移按钮
                if (i == folders.Length - 1) moveDownItem.Visibility = Visibility.Collapsed;

                // 构建列表项
                var newItem = new SelectionRow
                {
                    IsScaleAnimationEnabled = false,
                    Type = SelectionRow.CheckType.RadioBox,
                    MinPaddingRight = 30,
                    Title = folder.Name,
                    Info = folder.Location,
                    Height = 40,
                    ContextMenu = contMenu,
                    Tag = folder
                };

                newItem.Changed += (a, b) => Folder_Change((SelectionRow)a, b);

                // 拖拽
                newItem.AllowDrop = true;
                newItem.MouseMove += Item_MouseMove;
                newItem.DragEnter += Item_DragEnter;
                newItem.DragOver += Item_DragOver;
                newItem.DragLeave += Item_DragLeave;
                newItem.Drop += Item_Drop;

                // 图标按钮
                var newIconButton = new SymbioIconButton
                {
                    SvgIcon = "lucide/settings",
                    LogoScale = 1.1
                };
                newIconButton.Click += (_, _) =>
                {
                    contMenu.PlacementTarget = newItem;
                    contMenu.IsOpen = true;
                };
                newItem.Buttons = [newIconButton];

                PanList.Children.Add(newItem);

                LogWrapper.Info($"[Minecraft] 有效的 Minecraft 文件夹：{folder.Name} > {folder.Location}");
            }

            // 标题文本
            PanList.Children.Add(new TextBlock
            {
                Text = Lang.Text("Select.Folder.AddOrImport"),
                Margin = new Thickness(13, 18, 5, 4),
                Opacity = 0.6,
                FontSize = 12
            });

            // 创建新文件夹按钮
            if (!Directory.Exists(Path.Combine(ModBase.exePath, ".minecraft")))
            {
                var itemCreate = new SelectionRow
                {
                    IsScaleAnimationEnabled = false,
                    Type = SelectionRow.CheckType.Clickable,
                    Title = Lang.Text("Select.Folder.CreateNew.Title"),
                    Height = 34,
                    ToolTip = Lang.Text("Select.Folder.CreateNew.ToolTip"),
                    LogoScale = 0.9,
                    SvgIcon = "lucide/folder-plus"
                };
                ToolTipService.SetPlacement(itemCreate, PlacementMode.Right);
                itemCreate.Click += (_, _) => Create_Click();
                PanList.Children.Add(itemCreate);
            }

            // 添加按钮
            var itemAdd = new SelectionRow
            {
                IsScaleAnimationEnabled = false,
                Type = SelectionRow.CheckType.Clickable,
                Title = Lang.Text("Select.Folder.AddExisting.Title"),
                Height = 34,
                ToolTip = Lang.Text("Select.Folder.AddExisting.ToolTip"),
                SvgIcon = "lucide/folder-input"
            };
            ToolTipService.SetPlacement(itemAdd, PlacementMode.Right);
            itemAdd.Click += (_, _) => Add_Click();
            PanList.Children.Add(itemAdd);

            // 导入整合包
            var itemInstall = new SelectionRow
            {
                IsScaleAnimationEnabled = false,
                Type = SelectionRow.CheckType.Clickable,
                Title = Lang.Text("Select.Folder.ImportModpack.Title"),
                Height = 34,
                ToolTip = Lang.Text("Select.Folder.ImportModpack.ToolTip"),
                SvgIcon = "lucide/package-plus"
            };
            ToolTipService.SetPlacement(itemInstall, PlacementMode.Right);
            itemInstall.Click += (_, _) => ModModpack.ModpackInstall();
            PanList.Children.Add(itemInstall);

            // 边距
            PanList.Children.Add(new FrameworkElement { Height = 10, IsHitTestVisible = false });

            // 确认勾选状态
            for (var i = 0; i < folders.Length; i++)
                if (folders[i].Location == ModFolder.mcFolderSelected)
                {
                    ((SelectionRow)PanList.Children[i + 1]).Checked = true; //去掉第一个标题
                    return;
                }

            if (folders.Length == 0) return;
            States.Game.SelectedFolder = folders[0].Location.Replace(ModBase.exePath, "$");
            ((SelectionRow)PanList.Children[1]).Checked = true;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "构建 Minecraft 文件夹列表 UI 出错");
        }
        finally
        {
            ModFolder.RefreshInstances(ModFolder.mcFolderSelected);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var folder =
            (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
                .PlacementTarget)
            .Tag;
        var index = ModFolder.mcFolderList.IndexOf(folder);
        if (index <= 0) return;
        ModFolder.mcFolderList.RemoveAt(index);
        ModFolder.mcFolderList.Insert(index - 1, folder);
        UpdateFolderOrder();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var folder =
            (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
                .PlacementTarget)
            .Tag;
        var index = ModFolder.mcFolderList.IndexOf(folder);
        if (index >= ModFolder.mcFolderList.Count - 1) return;
        ModFolder.mcFolderList.RemoveAt(index);
        ModFolder.mcFolderList.Insert(index + 1, folder);
        UpdateFolderOrder();
    }

    private void UpdateFolderOrder()
    {
        States.Game.Folders = ModFolder.mcFolderList
            .Select(folder => $"{folder.Name}>{folder.Location}")
            .ToArray()
            .Join("|");
        McFolderListUI();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var folder =
            (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
                .PlacementTarget)
            .Tag;
        var index = ModFolder.mcFolderList.IndexOf(folder);
        ModFolder.mcFolderList[index].type = ModFolder.McFolder.Types.Original;
        ModFolder.mcFolderList[index].Name = Lang.Text("Select.Folder.OfficialLauncherFolder");
        UpdateFolderOrder();
    }

    // 添加文件夹
    private void Add_Click()
    {
        var newFolder = "";
        // 检查是否有下载任务
        if (ModNet.HasDownloadingTask())
        {
            HintService.Hint(Lang.Text("Select.Folder.CannotAddWhileDownloading"), HintType.Error);
            return;
        }

        try
        {
            // 获取输入
            newFolder = SystemDialogs.SelectFolder();
            if (string.IsNullOrEmpty(newFolder))
                return;
            if (newFolder.Contains('!') || newFolder.Contains(';'))
            {
                HintService.Hint(Lang.Text("Select.Folder.InvalidPathChars"), HintType.Error);
                return;
            }

            // 要求输入显示名称
            var splitedNames = newFolder.TrimEnd('\\').Split(@"\");
            var defaultName = splitedNames.Last() == ".minecraft"
                ? splitedNames.Length >= 3 ? splitedNames[^2] : ""
                : splitedNames.Last();
            if (defaultName.Length > 40)
                defaultName = defaultName[..39];
            var newName = ModMain.MyMsgBoxInput(Lang.Text("Select.Folder.InputDisplayName.Title"),
                Lang.Text("Select.Folder.InputDisplayName.Message"), defaultName,
                [new NullOrWhiteSpaceValidator(), new StringLengthValidator(), new BlacklistValidator([">", "|"])]);
            if (string.IsNullOrWhiteSpace(newName))
                return;
            // 添加文件夹
            ModFolder.AddFolder(newFolder, newName, true);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Folder.Error.Add", newFolder),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Folder.Error.Add", newFolder));
        }
    }

    // 创建文件夹
    public void Create_Click()
    {
        // 检查是否有下载任务
        if (ModNet.HasDownloadingTask())
        {
            HintService.Hint(Lang.Text("Select.Folder.CannotCreateWhileDownloading"), HintType.Error);
            return;
        }

        if (!Directory.Exists(ModBase.exePath + @".minecraft\"))
        {
            Directory.CreateDirectory(ModBase.exePath + @".minecraft\");
            Directory.CreateDirectory(ModBase.exePath + @".minecraft\versions\");
            States.Game.SelectedFolder = @"$.minecraft\";
            ModFolder.McFolderLauncherProfilesJsonCreate(ModBase.exePath + @".minecraft\");
            HintService.Hint(Lang.Text("Select.Folder.CreateSuccess"), HintType.Success);
        }

        ModFolder.mcFolderListLoader.Start(isForceRestart: true);
    }

    // 右键菜单
    public void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder =
                (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
                    .PlacementTarget).Tag;
            switch (ModMain.MyMsgBox(
                        Lang.Text("Select.Folder.Cleanup.Message"),
                        Lang.Text("Select.Folder.Cleanup.Title"), Lang.Text("Common.Action.Delete"),
                        Lang.Text("Select.Folder.Cleanup.Keep"), Lang.Text("Common.Action.Cancel")))
            {
                case 1:
                {
                    // 删除配置文件
                    if (File.Exists(folder.Location + "PCL.ini"))
                        File.Delete(folder.Location + "PCL.ini");
                    if (Directory.Exists(folder.Location + @"versions\"))
                        foreach (var Version in new DirectoryInfo(folder.Location + @"versions\")
                                     .EnumerateDirectories())
                            if (Directory.Exists(Path.Combine(Version.FullName, "PCL")))
                                Directory.Delete(Path.Combine(Version.FullName, "PCL"), true);

                    break;
                }
                case 2:
                {
                    break;
                }
                // 不删除
                case 3:
                {
                    // 取消
                    return;
                }
            }

            // 若修改了本部分代码，应对应修改 Delete_Click 中的代码
            // 获取并删除列表项
            var folders = new List<string>(States.Game.Folders.Split("|"));
            var name = "";
            for (int i = 0, loopTo = folders.Count - 1; i <= loopTo; i++)
            {
                if (string.IsNullOrEmpty(folders[i]))
                    break;
                if (!folders[i].EndsWith(folder.Location)) continue;
                name = folders[i].BeforeFirst(">");
                folders.RemoveAt(i);
                break;
            }

            // 保存
            States.Game.Folders = folders.Count == 0 ? "" : folders.ToArray().Join("|");
            HintService.Hint(
                folder.type == ModFolder.McFolder.Types.Custom
                    ? Lang.Text("Select.Folder.RemoveSuccess", name)
                    : Lang.Text("Select.Folder.RestoreSuccess"),
                HintType.Success);
            ModFolder.mcFolderListLoader.Start(isForceRestart: true);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Folder.Error.Remove"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Folder.Error.Remove"));
        }
    }

    public void Delete_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = (SymbioMenuAction)sender;
        var contextMenu = (ContextMenu)menuItem.Parent;
        var popup = (Popup)contextMenu.Parent;
        var listItem = (SelectionRow)popup.PlacementTarget;
        var folder = (ModFolder.McFolder)listItem.Tag;

        var isClearing =
            folder.type is ModFolder.McFolder.Types.Original or ModFolder.McFolder.Types.RenamedOriginal
            && folder.Location == ModBase.exePath + @".minecraft\"
            && ModFolder.mcFolderList.Count == 1;

        var deleteText = Lang.Text(isClearing ? "Select.Folder.Clear" : "Common.Action.Delete");
        var firstWarning =
            Lang.Text(isClearing ? "Select.Folder.Clear.FirstWarning" : "Select.Folder.Delete.FirstWarning",
                folder.Location);
        var finalWarning =
            Lang.Text(isClearing ? "Select.Folder.Clear.FinalWarning" : "Select.Folder.Delete.FinalWarning",
                folder.Location);
        var confirmTitle = Lang.Text(isClearing ? "Select.Folder.Clear.Confirm" : "Select.Folder.Delete.Confirm");
        var inProgress = Lang.Text(isClearing ? "Select.Folder.Clear.InProgress" : "Select.Folder.Delete.InProgress",
            folder.Name);
        var success = Lang.Text(isClearing ? "Select.Folder.Clear.Success" : "Select.Folder.Delete.Success",
            folder.Name);

        if (ModMain.MyMsgBox(firstWarning, Lang.Text("Select.Folder.Delete.WarningTitle"),
                Lang.Text("Common.Action.Cancel"), Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel")) != 2)
            return;

        if (ModMain.MyMsgBox(finalWarning, Lang.Text("Select.Folder.Delete.WarningTitle"),
                confirmTitle, Lang.Text("Common.Action.Cancel"),
                isWarn: true) != 1)
            return;

        var folders = States.Game.Folders.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        var index = folders.FindIndex(f => f.EndsWith(folder.Location));
        if (index >= 0)
            folders.RemoveAt(index);
        States.Game.Folders = string.Join("|", folders);

        ModBase.RunInNewThread(() =>
        {
            try
            {
                HintService.Hint(inProgress);
                ModBase.DeleteDirectory(folder.Location);
                if (isClearing)
                    Directory.CreateDirectory(folder.Location);
                HintService.Hint(success, HintType.Success);
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Select.Folder.Error.Operate", deleteText, folder.Name),
                    ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Select.Folder.Error.Operate", deleteText, folder.Name));
            }
            finally
            {
                ModFolder.mcFolderListLoader.Start(isForceRestart: true);
            }
        }, "Folder Delete " + ModBase.GetUuid(), ThreadPriority.BelowNormal);
    }

    public void Open_Click(object sender, RoutedEventArgs e)
    {
        ModBase.OpenExplorer(((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent).PlacementTarget)
            .Info);
    }

    public void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var data = (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
            .PlacementTarget).Tag;
        ModFolder.RefreshInstances(data.Location, clearCache: true);
    }

    public void Rename_Click(object sender, RoutedEventArgs e)
    {
        var folder =
            (ModFolder.McFolder)((SelectionRow)((Popup)((ContextMenu)((SymbioMenuAction)sender).Parent).Parent)
                .PlacementTarget)
            .Tag;
        try
        {
            // 获取输入
            var newName = ModMain.MyMsgBoxInput(Lang.Text("Select.Folder.Rename.Title"), "", folder.Name,
            [
                new NullOrWhiteSpaceValidator(), new StringLengthValidator(1, 30),
                new BlacklistValidator([">", "|"])
            ]);
            if (string.IsNullOrWhiteSpace(newName))
                return;
            // 修改自定义名
            var folders = new List<string>(States.Game.Folders.Split("|"));
            var isAdded = false;
            for (int i = 0, loopTo = folders.Count - 1; i <= loopTo; i++)
            {
                var folderCurrent = folders[i];
                if (string.IsNullOrEmpty(folderCurrent))
                    continue;
                if (folderCurrent.Split(">")[1] != (folder.Location ?? "")) continue;
                isAdded = true;
                if (folderCurrent.Split(">")[0] == newName)
                    // 名称未修改
                    return;

                folders[i] = $"{newName}>{folder.Location}";
                break;
            }

            // 如果没有添加过，则添加进去（因为修改了默认项的名称）
            if (!isAdded)
                folders.Add($"{newName}>{folder.Location}");
            HintService.Hint(Lang.Text("Select.Folder.NameUpdated", newName), HintType.Success);
            // 保存
            States.Game.Folders = folders.ToArray().Join("|");
            ModFolder.mcFolderListLoader.Start(isForceRestart: true);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Folder.Error.Rename"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Folder.Error.Rename"));
        }
    }

    // 点击选项
    public void Folder_Change(SelectionRow sender, ModBase.RouteEventArgs e)
    {
        if (!e.raiseByMouse || !sender.Checked)
            return;
        // 检查是否有下载任务
        if (ModNet.HasDownloadingTask(true))
        {
            HintService.Hint(Lang.Text("Select.Folder.SwitchBlockedByDownload"), HintType.Error);
            e.handled = true;
            return;
        }

        // 更换
        States.Game.SelectedFolder = ((ModFolder.McFolder)sender.Tag).Location.Replace(ModBase.exePath, "$");
        ModFolder.mcFolderListLoader.Start(isForceRestart: true);
        ModFolder.RefreshInstances(ModFolder.mcFolderSelected);
    }

    #region 拖拽排序功能

    // 拖拽开始时的鼠标移动处理
    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        var item = (SelectionRow)sender;
        // 当按住鼠标左键时开始拖拽操作
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try
        {
            DragDrop.DoDragDrop(item, item.Tag, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "开始拖拽操作失败");
        }
    }

    // 拖拽进入时的处理
    private void Item_DragEnter(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(typeof(ModFolder.McFolder)))
            {
                e.Effects = DragDropEffects.Move;
                // 添加视觉反馈
                var item = (SelectionRow)sender;
                item.Opacity = 0.7d;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        catch (Exception)
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // 拖拽悬停时的处理
    private void Item_DragOver(object sender, DragEventArgs e)
    {
        try
        {
            e.Effects = e.Data.GetDataPresent(typeof(ModFolder.McFolder))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }
        catch (Exception)
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // 拖拽离开时的处理
    private void Item_DragLeave(object sender, DragEventArgs e)
    {
        try
        {
            // 恢复视觉状态
            var item = (SelectionRow)sender;
            item.Opacity = 1.0d;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "拖拽离开处理失败");
        }

        e.Handled = true;
    }

    // 拖拽放下时的处理
    private void Item_Drop(object sender, DragEventArgs e)
    {
        try
        {
            var targetItem = (SelectionRow)sender;
            var targetFolder = (ModFolder.McFolder)targetItem.Tag;

            // 恢复视觉状态
            targetItem.Opacity = 1.0d;

            // 检查数据有效性
            if (!e.Data.GetDataPresent(typeof(ModFolder.McFolder)))
            {
                e.Handled = true;
                return;
            }

            var sourceFolder = (ModFolder.McFolder)e.Data.GetData(typeof(ModFolder.McFolder));

            // 检查是否为有效的拖拽操作
            if (ReferenceEquals(sourceFolder, targetFolder))
            {
                e.Handled = true;
                return;
            }

            // 检查文件夹是否在列表中
            if (!ModFolder.mcFolderList.Contains(sourceFolder) || !ModFolder.mcFolderList.Contains(targetFolder))
            {
                e.Handled = true;
                return;
            }

            // 获取源文件夹和目标文件夹的索引
            var sourceIndex = ModFolder.mcFolderList.IndexOf(sourceFolder);
            var targetIndex = ModFolder.mcFolderList.IndexOf(targetFolder);

            // 执行移动操作
            if (sourceIndex == targetIndex) return;
            // 先移除源文件夹
            ModFolder.mcFolderList.RemoveAt(sourceIndex);

            // 计算新的插入位置
            int newTargetIndex;

            // 向下拖拽：插入到目标项目的后面
            // 由于移除了源项目，目标索引已经自动减1，所以直接使用TargetIndex就是插入到目标后面
            // 向上拖拽：插入到目标项目的前面
            newTargetIndex = targetIndex;

            // 确保插入位置不超出列表范围
            if (newTargetIndex > ModFolder.mcFolderList.Count)
                newTargetIndex = ModFolder.mcFolderList.Count;
            else if (newTargetIndex < 0) newTargetIndex = 0;

            // 插入到新位置
            ModFolder.mcFolderList.Insert(newTargetIndex, sourceFolder);

            // 更新文件夹顺序并刷新UI
            UpdateFolderOrder();

            var direction = sourceIndex < targetIndex ? "后面" : "前面";
            ModBase.Log(
                $"[Control] 文件夹拖拽排序：{sourceFolder.Name} -> 位置 {newTargetIndex} (在 {targetFolder.Name} {direction})",
                ModBase.LogLevel.Debug);
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Folder.Error.DragDrop"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Folder.Error.DragDrop"));
        }
        finally
        {
            e.Handled = true;
        }
    }

    #endregion
}
