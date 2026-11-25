using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using PCL.Core.Utils.Validate;

namespace PCL;

public partial class SkinLibraryView
{
    /// <summary>右侧皮肤卡片使用的全身图倍率，5 倍表示每个皮肤像素放大到 5 像素。</summary>
    private const int FigureScale = 5;

    private string? selectedFile;

    /// <summary>皮肤卡片上的选中高亮层，用于淡入淡出的选中动效。</summary>
    private readonly Dictionary<string, Border> _cellHighlights = new(StringComparer.OrdinalIgnoreCase);

    public SkinLibraryView()
    {
        InitializeComponent();
        // 模型是按皮肤单独记录的，选中皮肤时再读，这里先按经典模型显示账号皮肤
        UpdateModelSelection(false);
        Loaded += (_, _) => Reload();
    }

    /// <summary>皮肤库文件夹，存放导入的 PNG 皮肤。</summary>

    /// <summary>皮肤对应的模型记录文件，与皮肤同名，内容为 slim 或 classic。</summary>
    private static string ModelFile(string skinPath) => skinPath + ".model";

    /// <summary>读取某张皮肤的模型选择，没有记录时按经典模型。</summary>
    private static bool IsSlimSkin(string skinPath)
    {
        try
        {
            return File.Exists(ModelFile(skinPath)) && File.ReadAllText(ModelFile(skinPath)).Trim() == "slim";
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SkinLibrary] 读取模型设置失败（{skinPath}）", ModBase.LogLevel.Developer);
            return false;
        }
    }

    /// <summary>记录某张皮肤的模型选择，经典模型不写文件。</summary>
    private static void SaveSkinModel(string skinPath, bool slim)
    {
        try
        {
            var file = ModelFile(skinPath);
            if (slim) File.WriteAllText(file, "slim");
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SkinLibrary] 保存模型设置失败（{skinPath}）", ModBase.LogLevel.Developer);
        }
    }

    /// <summary>全身预览图缓存文件夹。</summary>
    private static string FigureCacheFolder => Path.Combine(ModBase.pathTemp, "Cache", "SkinFigure");

    /// <summary>刷新皮肤库。</summary>
    private void Reload()
    {
        try
        {
            Directory.CreateDirectory(ProfileAssetLibrary.LibraryFolder);
            PanSkinList.Children.Clear();
            _cellHighlights.Clear();
            PanSkinList.Children.Add(CreateAddCell());
            var files = Directory.GetFiles(ProfileAssetLibrary.LibraryFolder, "*.png")
                .OrderByDescending(File.GetLastWriteTime)
                .ToArray();
            if (selectedFile is not null && !File.Exists(selectedFile)) selectedFile = null;
            foreach (var file in files) PanSkinList.Children.Add(CreateSkinCell(file));
            LabEmpty.Visibility = files.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCellHighlight();
            UpdateCurrentPreview();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 读取皮肤库失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Tools.SkinLibrary.LoadFailed"));
        }
    }

    #region 卡片

    private const double CellWidth = 150d;
    private const double CellHeight = 216d;

    private FrameworkElement CreateAddCell()
    {
        var border = CreateCellBase(out var content, out _);
        border.ToolTip = Lang.Text("Tools.SkinLibrary.AddNew");
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var circle = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5d),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        circle.SetResourceReference(Border.BorderBrushProperty, "SyAccentText");
        var plus = new TextBlock
        {
            Text = "+", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "SyAccentText");
        circle.Child = plus;
        var label = new TextBlock
        {
            Text = Lang.Text("Tools.SkinLibrary.AddNew"), Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SyTextSecondary");
        panel.Children.Add(circle);
        panel.Children.Add(label);
        content.Children.Add(panel);
        border.MouseLeftButtonUp += (_, _) => ImportSkins();
        return border;
    }

    private FrameworkElement CreateSkinCell(string file)
    {
        var border = CreateCellBase(out var content, out var highlight);
        border.Tag = file;
        _cellHighlights[file] = highlight;
        var panel = new StackPanel();
        var label = new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(file), HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SyText");
        var image = new AssetImage
        {
            Height = 32 * FigureScale, Margin = new Thickness(0, 6, 0, 0), Stretch = Stretch.Uniform,
            Address = GetFigureImage(file, IsSlimSkin(file))
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        panel.Children.Add(image);
        panel.Children.Add(label);
        content.Children.Add(panel);
        border.MouseLeftButtonUp += (_, _) => SelectSkin(file);
        return border;
    }

    private Border CreateCellBase(out Grid content, out Border highlight)
    {
        // 选中高亮单独作为一层叠在卡片内，用透明度动画淡入淡出，避免直接改边框粗细造成跳动
        highlight = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5d),
            Opacity = 0d, IsHitTestVisible = false
        };
        highlight.SetResourceReference(Border.BorderBrushProperty, "SyAccentFill");

        content = new Grid { Margin = new Thickness(8) };
        // The outer border owns hover translation. Scale the inner surface independently.
        var root = new Grid
        {
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };
        root.Children.Add(highlight);
        root.Children.Add(content);

        var border = new Border
        {
            Width = CellWidth, Height = CellHeight, Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = root
        };
        border.SetResourceReference(StyleProperty, "SeSkinTile");
        return border;
    }

    private void UpdateCellHighlight()
    {
        foreach (var (path, highlight) in _cellHighlights)
            SymbioMotion.Fade(highlight, path == selectedFile ? 1 : 0);
    }

    private void SelectSkin(string file)
    {
        selectedFile = selectedFile == file ? null : file;
        AnimateCellPop(file);
        UpdateCellHighlight();
        UpdateCurrentPreview();
    }

    /// <summary>点击卡片时的缩放反馈：先轻微缩小，再弹回原大小。</summary>
    private void AnimateCellPop(string file)
    {
        var cell = PanSkinList.Children.OfType<Border>()
            .FirstOrDefault(border => border.Tag is string path && path == file);
        if (cell?.Child is not Grid content || !PCLLF.Wpf.Motion.MotionPreferences.AllowsAnimation ||
            ModAnimation.aniSpeed is <= 0 or >= 200) return;
        SymbioMotion.SpringScale(content, .97, SymbioMotion.Pop, immediate: true);
        SymbioMotion.SpringScale(content, 1, SymbioMotion.Pop);
    }

    /// <summary>刷新左侧的当前皮肤预览。</summary>
    private void UpdateCurrentPreview()
    {
        if (selectedFile is not null && File.Exists(selectedFile))
        {
            // 模型按皮肤单独记录，选中哪张就按哪张的设置预览
            ViewerCurrent.SlimModel = IsSlimSkin(selectedFile);
            UpdateModelSelection(ViewerCurrent.SlimModel);
            ViewerCurrent.SetSkin(selectedFile);
            LabCurrentName.Text = Path.GetFileNameWithoutExtension(selectedFile);
            BtnPrimary.Text = Lang.Text("Tools.SkinLibrary.ApplyToAccount");
            BtnPrimary.IsEnabled = true;
            BtnRename.Visibility = Visibility.Visible;
            BtnRemove.Visibility = Visibility.Visible;
        }
        else
        {
            selectedFile = null;
            // 账号皮肤没有单独记录，离线档案按已应用皮肤的模型预览，其余按经典模型预览
            var slim = ModProfile.selectedProfile is { Type: ModLaunch.McLoginType.Legacy } &&
                       !string.IsNullOrWhiteSpace(ModProfile.selectedProfile.LocalSkin) &&
                       File.Exists(ModProfile.selectedProfile.LocalSkin) &&
                       ModProfile.selectedProfile.LocalSkinSlim;
            ViewerCurrent.SlimModel = slim;
            UpdateModelSelection(slim);
            ViewerCurrent.SetSkin(GetAccountSkinSource());
            LabCurrentName.Text = ModProfile.selectedProfile?.Username ??
                                  Lang.Text("Tools.SkinLibrary.CurrentSkin");
            BtnPrimary.Text = Lang.Text("Tools.SkinLibrary.AddToLibrary");
            BtnPrimary.IsEnabled = ModProfile.selectedProfile is not null;
            BtnRename.Visibility = Visibility.Collapsed;
            BtnRemove.Visibility = Visibility.Collapsed;
        }

    }

    /// <summary>切换当前皮肤使用经典（手臂 4 像素）还是纤细（手臂 3 像素）模型，按皮肤单独保存。</summary>
    private void BtnModel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var slim = ReferenceEquals(sender, BtnSlimModel);
        if (slim == ViewerCurrent.SlimModel)
            return;
        ViewerCurrent.SlimModel = slim;
        UpdateModelSelection(slim);
        // 只记录到当前选中的皮肤；未选中皮肤时仅临时预览
        if (selectedFile is not null)
        {
            SaveSkinModel(selectedFile, slim);
            // 已应用到离线档案的皮肤，模型切换要同步生效
            var profile = ModProfile.SyncSelectedProfile();
            if (profile is { Type: ModLaunch.McLoginType.Legacy } && profile.LocalSkin == selectedFile)
            {
                profile.LocalSkinSlim = slim;
                ModProfile.SaveProfile();
                ModLocalSkin.Register(profile);
                ModLocalSkin.RefreshPreview();
            }
        }

        // 右侧缩略图也按新模型重画
        Reload();
    }

    private void UpdateModelSelection(bool slim)
    {
        BtnClassicModel.ColorType = slim ? SymbioButton.ColorState.Normal : SymbioButton.ColorState.Highlight;
        BtnSlimModel.ColorType = slim ? SymbioButton.ColorState.Highlight : SymbioButton.ColorState.Normal;
    }

    #endregion

    #region 导入与删除

    private void ImportSkins()
    {
        var files = SystemDialogs.SelectFiles(Lang.Text("Tools.SkinLibrary.ImportFilter"),
            Lang.Text("Tools.SkinLibrary.ImportTitle"), ProfileAssetLibrary.LibraryFolder, true);
        if (files.Length == 0) return;

        string? lastImported = null;
        foreach (var file in files)
            try
            {
                var size = ProfileAssetLibrary.ReadSkinSize(file);
                if (size.Width != 64 || (size.Height != 32 && size.Height != 64))
                {
                    HintService.Hint(Lang.Text("Tools.SkinLibrary.Invalid", Path.GetFileName(file)), HintType.Error);
                    continue;
                }

                var target = ProfileAssetLibrary.GetUniquePath(Path.Combine(ProfileAssetLibrary.LibraryFolder, Path.GetFileName(file)));
                File.Copy(file, target);
                lastImported = target;
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[SkinLibrary] 导入皮肤失败（{file}）", ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Tools.SkinLibrary.ImportFailed", Path.GetFileName(file)));
            }

        if (lastImported is null) return;
        selectedFile = lastImported;
        Reload();
        HintService.Hint(Lang.Text("Tools.SkinLibrary.ImportSuccess", files.Length), HintType.Success);
    }

    private void BtnRename_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (selectedFile is null || !File.Exists(selectedFile)) return;
        var oldName = Path.GetFileNameWithoutExtension(selectedFile);
        var newName = ModMain.MyMsgBoxInput(Lang.Text("Tools.SkinLibrary.RenameTitle"), "", oldName,
        [
            new NullOrWhiteSpaceValidator(), new StringLengthValidator(1, 40),
            new BlacklistValidator([":", "*", "?", "\"", "<", ">", "|", "/", "\\"])
        ]);
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        if (newName == oldName) return;

        var target = Path.Combine(Path.GetDirectoryName(selectedFile)!, newName + Path.GetExtension(selectedFile));
        if (File.Exists(target))
        {
            HintService.Hint(Lang.Text("Tools.SkinLibrary.RenameExists", newName), HintType.Warning);
            return;
        }

        try
        {
            var oldPath = selectedFile;
            File.Move(oldPath, target);
            // 模型记录跟着改名
            if (File.Exists(ModelFile(oldPath))) File.Move(ModelFile(oldPath), ModelFile(target));
            selectedFile = target;
            Reload();
            HintService.Hint(Lang.Text("Tools.SkinLibrary.RenameSuccess", newName), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SkinLibrary] 重命名皮肤失败（{selectedFile} -> {target}）", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.SkinLibrary.RenameFailed", newName));
        }
    }

    private void BtnRemove_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (selectedFile is null) return;
        var file = selectedFile;
        var name = Path.GetFileNameWithoutExtension(file);
        if (ModMain.MyMsgBox(Lang.Text("Tools.SkinLibrary.DeleteConfirmMessage", name),
                Lang.Text("Tools.SkinLibrary.DeleteConfirmTitle"),
                Lang.Text("Tools.SkinLibrary.RemoveFromLibrary"), Lang.Text("Common.Action.Cancel")) != 1)
            return;

        try
        {
            File.Delete(file);
            // 一起删掉模型记录，免得下次同名皮肤继承旧设置
            if (File.Exists(ModelFile(file))) File.Delete(ModelFile(file));
            // 正在被离线档案使用的皮肤被删除时，同步解除应用，避免启动时静默失效
            var profile = ModProfile.SyncSelectedProfile();
            if (profile is { Type: ModLaunch.McLoginType.Legacy } && profile.LocalSkin == file)
            {
                profile.LocalSkin = null;
                profile.LocalSkinSlim = false;
                ModProfile.SaveProfile();
                ModLocalSkin.Unregister(profile);
                ModLocalSkin.RefreshPreview();
            }

            selectedFile = null;
            Reload();
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Deleted", name), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SkinLibrary] 删除皮肤失败（{file}）", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.SkinLibrary.DeleteFailed", name));
        }
    }


    #endregion

    #region 应用与添加

    private void BtnPrimary_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (selectedFile is not null)
        {
            ApplySkin(selectedFile);
            return;
        }

        AddCurrentToLibrary();
    }

    /// <summary>把账号上当前的皮肤加入皮肤库。</summary>
    private void AddCurrentToLibrary()
    {
        var profile = ModProfile.selectedProfile;
        if (profile is null)
        {
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Apply.NoProfile"), HintType.Error);
            return;
        }

        try
        {
            var name = string.IsNullOrWhiteSpace(profile.Username)
                ? Lang.Text("Tools.SkinLibrary.CurrentSkin")
                : profile.Username;
            var target = ProfileAssetLibrary.GetUniquePath(Path.Combine(ProfileAssetLibrary.LibraryFolder, name + ".png"));
            var image = new MyBitmap(GetAccountSkinSource());
            image.Save(target);
            selectedFile = target;
            Reload();
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Added"), HintType.Success);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SkinLibrary] 添加当前皮肤失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.SkinLibrary.AddFailed", ex.Message));
        }
    }

    private void ApplySkin(string file)
    {
        var profile = ModProfile.SyncSelectedProfile();
        if (profile is null)
        {
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Apply.NoProfile"), HintType.Error);
            return;
        }

        if (profile.Type == ModLaunch.McLoginType.Ms)
        {
            var model = ModMain.MyMsgBox(Lang.Text("Launch.Skin.Model.SelectMessage"),
                Lang.Text("Launch.Skin.Model.SelectTitle"),
                Lang.Text("Launch.Skin.Model.Steve"), Lang.Text("Launch.Skin.Model.Alex"),
                Lang.Text("Common.Option.IDontKnow"));
            if (model is <= 0 or 3) return;
            ModProfile.ChangeSkinMs(file, model == 2);
        }
        else if (profile.Type == ModLaunch.McLoginType.Auth)
        {
            // 第三方登录的皮肤由皮肤站管理，只能打开网页上传
            ModBase.OpenWebsite(profile.Server.BeforeFirst("api/yggdrasil/authserver") + "user/closet");
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Apply.AuthHint"));
        }
        else
        {
            // 离线档案：由启动器内置的本地皮肤服务接管，启动游戏后即可在游戏内生效
            profile.LocalSkin = file;
            profile.LocalSkinSlim = IsSlimSkin(file);
            ModProfile.SaveProfile();
            ModLocalSkin.Register(profile);
            ModLocalSkin.RefreshPreview();
            HintService.Hint(Lang.Text("Tools.SkinLibrary.Apply.OfflineApplied"), HintType.Success);
        }
    }

    /// <summary>获取账号当前皮肤的文件路径，未加载时回退到默认皮肤。</summary>
    private static string GetAccountSkinSource()
    {
        var profile = ModProfile.selectedProfile;
        // 离线档案优先显示已启用的本地皮肤，避免预览与实际进游戏后的效果不一致
        if (profile is { Type: ModLaunch.McLoginType.Legacy } && !string.IsNullOrWhiteSpace(profile.LocalSkin) &&
            File.Exists(profile.LocalSkin))
            return profile.LocalSkin;
        var loader = profile?.Type switch
        {
            ModLaunch.McLoginType.Auth => ProfileSkins.skinAuth,
            ModLaunch.McLoginType.Ms => ProfileSkins.skinMs,
            _ => ProfileSkins.skinLegacy
        };
        var output = loader?.output;
        if (!string.IsNullOrEmpty(output) &&
            (output.StartsWith(ModBase.pathImage) || File.Exists(output)))
            return output;
        return ModBase.pathImage + "Skins/" + ModSkin.McSkinSex(profile?.Uuid ?? "") + ".png";
    }

    #endregion

    #region 按钮

    private void BtnRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Reload();
        HintService.Hint(Lang.Text("Tools.SkinLibrary.Refreshed"));
    }

    private void BtnOpenFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Directory.CreateDirectory(ProfileAssetLibrary.LibraryFolder);
        ModBase.OpenExplorer(ProfileAssetLibrary.LibraryFolder + @"\");
    }

    #endregion

    #region 图片处理

    /// <summary>读取皮肤分辨率，无法解析时抛出异常。</summary>

    /// <summary>
    ///     把皮肤纹理渲染为正面站立的全身图，缓存后返回文件路径。
    ///     绘制方式与百宝箱「生成头像」一致：直接从原图逐块绘制、按整数倍最近邻放大。
    /// </summary>
    private static string GetFigureImage(string source, bool slim)
    {
        try
        {
            Directory.CreateDirectory(FigureCacheFolder);
            var stamp = source.StartsWith(ModBase.pathImage)
                ? source
                : source + File.GetLastWriteTime(source).ToBinary();
            // 缓存按模型区分：纤细与经典的手臂宽度不同
            var cache = Path.Combine(FigureCacheFolder,
                ModBase.GetStringMD5($"v5{(slim ? "s" : "c")}{FigureScale}{stamp}") + ".png");
            if (File.Exists(cache)) return cache;

            var image = new MyBitmap(source);
            using var figure = ModSkinFigure.Generate(image.pic, slim, FigureScale);
            figure.Save(cache, ImageFormat.Png);
            return cache;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SkinLibrary] 生成全身预览失败（{source}）", ModBase.LogLevel.Developer);
            return "";
        }
    }

    #endregion
}
