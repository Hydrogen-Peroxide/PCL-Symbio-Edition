using System.IO;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App.Localization;

namespace PCL;

public partial class PageBedrockSelectLeft : IRefreshable
{
    private readonly List<ModBedrockFolders.Folder> _folders = [];

    public PageBedrockSelectLeft()
    {
        InitializeComponent();
        BtnAddFolder.Text = Lang.Text("Bedrock.Select.AddFolder");
        BtnAddFolder.Click += BtnAddFolder_Click;
        Loaded += (_, _) => RefreshFolders(false);
    }

    public void Refresh()
    {
        RefreshFolders(true);
    }

    private void RefreshFolders(bool refreshInstances)
    {
        _folders.Clear();
        _folders.AddRange(ModBedrockFolders.Enumerate());
        PanFolders.Children.Clear();

        foreach (var folder in _folders)
        {
            var isCurrent = ModBedrockFolders.IsCurrent(folder);
            var item = new MyListItem
            {
                Title = folder.Title,
                Info = folder.Path,
                SvgIcon = isCurrent ? "lucide/folder-open" : "lucide/folder-input",
                Type = MyListItem.CheckType.RadioBox,
                Height = 48
            };
            var target = folder;
            item.Click += (_, _) => SwitchFolder(target);
            PanFolders.Children.Add(item);
            if (isCurrent) item.SetChecked(true, false, false);
        }

        if (refreshInstances) ModMain.frmBedrockSelect?.RefreshInstances();
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
        RefreshFolders(false);
        // 直接按用户点中的目录读取，不再绕一次全局配置，避免切换后列表仍是旧目录的内容
        ModMain.frmBedrockSelect?.RefreshInstances(folder.Path);
        HintService.Hint(Lang.Text("Bedrock.Select.Applied", folder.Title), HintType.Success);
    }

    private void BtnAddFolder_Click(object sender, MouseButtonEventArgs e)
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