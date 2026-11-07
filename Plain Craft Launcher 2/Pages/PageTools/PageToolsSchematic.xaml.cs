using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;
using SvgIconControl = PCL.Core.UI.Controls.SvgIcon.SvgIcon;

namespace PCL;

/// <summary>
///     百宝箱的投影页：布局与存档管理一致（实例卡片 + 搜索 + 快捷操作 + 卡片列表），
///     点击条目在页面内全屏预览。
/// </summary>
public partial class PageToolsSchematic
{
    private readonly List<McInstance> _instances = [];
    private MyListItem? _checkedInstanceItem;
    private List<string> _files = [];
    private McInstance? _instance;
    private bool _initializing;
    private bool _loadingPreview;
    private bool _sortByTime = true;

    public PageToolsSchematic()
    {
        InitializeComponent();
        BtnRefresh.Click += (_, _) => Reload();
        BtnOpenFolder.Click += (_, _) => OpenFolder();
        BtnSort.Click += (_, _) =>
        {
            _sortByTime = !_sortByTime;
            UpdateSortButton();
            Reload();
        };
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => Initialize();
        Unloaded += (_, _) => ClosePreview();
        UpdateSortButton();
    }

    private static bool IsSchematicFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".litematic" or ".schem" or ".schematic" or ".nbt";
    }

    private string? SchematicFolder => _instance is null ? null : Path.Combine(_instance.PathIndie, "schematics");

    private void Initialize()
    {
        try
        {
            _initializing = true;
            _instances.Clear();
            var versionsPath = Path.Combine(ModFolder.mcFolderSelected ?? "", "versions");
            if (Directory.Exists(versionsPath))
                foreach (var dir in Directory.GetDirectories(versionsPath).OrderBy(Path.GetFileName))
                    _instances.Add(new McInstance(Path.GetFileName(dir)));

            var target = PageInstanceLeft.McInstance?.Name ?? ModInstanceList.McMcInstanceSelected?.Name ??
                         States.Game.SelectedInstance;
            // 优先当前实例；当前实例没有投影时退回第一个有投影的实例
            var instance = _instances.FirstOrDefault(item => item.Name == target && GetSchematicCount(item) > 0)
                           ?? _instances.FirstOrDefault(item => GetSchematicCount(item) > 0)
                           ?? _instances.FirstOrDefault(item => item.Name == target)
                           ?? _instances.FirstOrDefault();
            _initializing = false;

            if (instance is null)
            {
                UpdateVersionPicker();
                Reload();
                return;
            }

            SelectInstance(instance, false);
        }
        catch (Exception ex)
        {
            _initializing = false;
            ModBase.Log(ex, "[SchematicPage] 初始化投影页失败", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.Schematic.LoadFailed", ex.Message));
        }
    }

    private void SelectInstance(McInstance instance, bool closePopup = true)
    {
        if (closePopup) PopupVersion.IsOpen = false;
        if (_instance?.Name == instance.Name && !_initializing) return;

        try
        {
            instance.Load();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPage] 加载实例失败（{instance.Name}）", ModBase.LogLevel.Hint,
                userSummary: Lang.Text("Tools.Schematic.LoadFailed", ex.Message));
            return;
        }

        ClosePreview();
        _instance = instance;
        UpdateVersionPicker();
        Reload();
        PlayTransition();
    }

    private void UpdateVersionPicker()
    {
        LabVersionTitle.Text = _instance?.Name ?? Lang.Text("Tools.Schematic.NoInstance");
        var folder = SchematicFolder;
        LabVersionInfo.Text = string.IsNullOrEmpty(folder) ? "" : folder;
        var logo = _instance is null ? "" : GetInstanceLogo(_instance);
        ImgVersionLogo.ActualSource = string.IsNullOrEmpty(logo) ? ModBase.pathImage + "Blocks/Grass.png" : logo;
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

    private static int GetSchematicCount(McInstance instance)
    {
        var folder = Path.Combine(instance.PathIndie, "schematics");
        if (!Directory.Exists(folder)) return 0;
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count(IsSchematicFile);
        }
        catch
        {
            return 0;
        }
    }

    private void UpdateSortButton() =>
        BtnSort.Text = Lang.Text("Instance.Saves.SortBy",
            Lang.Text(_sortByTime ? "Instance.Saves.SortModifyTime" : "Instance.Saves.SortFileName"));

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var files = string.IsNullOrEmpty(query)
            ? _files
            : _files.Where(path => Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        PanList.Children.Clear();
        foreach (var file in files) PanList.Children.Add(BuildRow(file));

        PanListBack.Title = Lang.Text("Tools.Schematic.Count", files.Count);
        LabEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reload()
    {
        _files = GetFiles();
        ApplyFilter();
    }

    private List<string> GetFiles()
    {
        try
        {
            var folder = SchematicFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return [];
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(IsSchematicFile);
            return (_sortByTime
                    ? files.OrderByDescending(File.GetLastWriteTime)
                    : files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SchematicPage] 扫描投影文件夹失败", ModBase.LogLevel.Developer);
            return [];
        }
    }

    /// <summary>紧凑的投影行：图标 + 名称/信息 + 预览按钮，行距与存档列表接近。</summary>
    private Border BuildRow(string path)
    {
        var border = new Border
        {
            Height = 48, Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand, Background = new SolidColorBrush(Colors.Transparent),
            RenderTransformOrigin = new Point(0.5d, 0.5d), RenderTransform = new ScaleTransform(1d, 1d)
        };
        border.MouseEnter += (_, _) =>
            ModAnimation.AniStart(new[]
            {
                ModAnimation.AaColor(border, Border.BackgroundProperty, "ColorBrushGray7", 120),
                ModAnimation.AaScaleTransform(border, 1.005d - ((ScaleTransform)border.RenderTransform).ScaleX, 140,
                    ease: new ModAnimation.AniEaseOutFluent())
            }, "SchematicRow " + ModBase.GetStringMD5(path));
        border.MouseLeave += (_, _) =>
            ModAnimation.AniStart(new[]
            {
                ModAnimation.AaColor(border, Border.BackgroundProperty, "ColorBrushTransparent", 160),
                ModAnimation.AaScaleTransform(border, 1d - ((ScaleTransform)border.RenderTransform).ScaleX, 160,
                    ease: new ModAnimation.AniEaseOutFluent())
            }, "SchematicRow " + ModBase.GetStringMD5(path));
        border.MouseLeftButtonUp += (_, _) => OpenPreview(path);

        var grid = new Grid { Margin = new Thickness(12, 5, 10, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SvgIconControl
        {
            Icon = "lucide/box", Width = 20, Height = 20, Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(SvgIconControl.IconBrushProperty, "ColorBrush4");
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(path), FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        var info = new TextBlock
        {
            Text = BuildRowInfo(path), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        info.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
        text.Children.Add(name);
        text.Children.Add(info);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var previewButton = new MyButton
        {
            Text = Lang.Text("Common.Action.Preview"), Height = 26, MinWidth = 64,
            Padding = new Thickness(10, 0, 10, 0), ColorType = MyButton.ColorState.Highlight,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
        };
        previewButton.Click += (_, _) => OpenPreview(path);
        Grid.SetColumn(previewButton, 2);
        grid.Children.Add(previewButton);

        border.Child = grid;
        return border;
    }

    private string BuildRowInfo(string path)
    {
        var file = new FileInfo(path);
        var relative = !string.IsNullOrEmpty(SchematicFolder) &&
                       path.StartsWith(SchematicFolder, StringComparison.OrdinalIgnoreCase)
            ? path[SchematicFolder.Length..].TrimStart('\\', '/')
            : file.Name;
        return $"{ModBase.GetString(file.Length)} · {file.LastWriteTime:g} · {relative}";
    }

    private void OpenFolder()
    {
        var folder = SchematicFolder;
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            ModBase.OpenExplorer(folder + @"\");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[SchematicPage] 打开投影文件夹失败", ModBase.LogLevel.Hint);
        }
    }

    #region 实例选择卡片

    private void PanVersionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        BuildInstanceMenu();
        PopupVersion.IsOpen = true;
    }

    private void PanVersionCard_MouseEnter(object sender, MouseEventArgs e) =>
        ModAnimation.AniStart(
            ModAnimation.AaColor(PanVersionCard, Border.BackgroundProperty, "ColorBrushBg1", 120),
            "SchematicVersionCard Color");

    private void PanVersionCard_MouseLeave(object sender, MouseEventArgs e) =>
        ModAnimation.AniStart(
            ModAnimation.AaColor(PanVersionCard, Border.BackgroundProperty, "ColorBrushGray7", 160),
            "SchematicVersionCard Color");

    private void BuildInstanceMenu()
    {
        PanVersionList.Children.Clear();
        _checkedInstanceItem = null;
        // 投影多的实例排在前面；没有投影的实例置底并禁用
        var entries = _instances
            .Select(instance => (Instance: instance, Count: GetSchematicCount(instance)))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Instance.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var (instance, count) in entries)
        {
            var item = new MyListItem
            {
                Height = 42,
                Type = MyListItem.CheckType.RadioBox,
                Title = instance.Name,
                Info = Lang.Text("Tools.Schematic.VersionInfo", count),
                MinPaddingRight = 20,
                IsEnabled = count > 0
            };
            item.SvgIcon = "";
            var logo = GetInstanceLogo(instance);
            item.Logo = string.IsNullOrEmpty(logo) ? ModBase.pathImage + "Blocks/Grass.png" : logo;
            if (count == 0)
            {
                item.Opacity = 0.4d;
                item.isMouseOverAnimationEnabled = false;
                item.Cursor = Cursors.No;
            }

            item.SetChecked(instance.Name == _instance?.Name, false, false);
            if (item.Checked) _checkedInstanceItem = item;
            item.Check += (_, _) =>
            {
                if (count > 0) SelectInstance(instance);
            };
            PanVersionList.Children.Add(item);
        }
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
        Dispatcher.BeginInvoke(new Action(() => _checkedInstanceItem?.BringIntoView()),
            DispatcherPriority.Background);
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

    /// <summary>切换实例时给卡片与列表做一段平滑过渡。</summary>
    private void PlayTransition()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        if (PanVersionCard.RenderTransform is ScaleTransform card)
        {
            card.ScaleX = card.ScaleY = 0.97d;
            var scale = new DoubleAnimation(0.97d, 1d, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
            card.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            card.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        PanContent.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    #endregion

    #region 预览

    private void OpenPreview(string path)
    {
        if (_instance is null || _loadingPreview || PanPreview.Visibility == Visibility.Visible) return;

        var instance = _instance;
        _loadingPreview = true;
        HintService.Hint(Lang.Text("Tools.Schematic.Loading"));
        ModBase.RunInNewThread(() =>
        {
            try
            {
                var data = ModSchematicPreview.Read(path);
                if (data.IsEmpty)
                {
                    var error = data.Error ?? Lang.Text("Tools.Schematic.LoadFailed", Path.GetFileName(path));
                    ModBase.RunInUi(() =>
                    {
                        _loadingPreview = false;
                        ModMain.MyMsgBox(error, Path.GetFileName(path), Lang.Text("Instance.Resource.Item.Info.Return"));
                    });
                    return;
                }

                var jarPath = Path.Combine(instance.PathInstance, instance.Name + ".jar");
                var textures = SchematicViewer3D.CanRender(data)
                    ? ModSchematicTextures.Build(data.Blocks.Select(block => block.Name).Distinct(), jarPath)
                    : null;
                var info = BuildPreviewInfo(path, data);
                ModBase.RunInUi(() =>
                {
                    _loadingPreview = false;
                    ShowPreview(path, info, data, textures);
                });
            }
            catch (Exception ex)
            {
                ModBase.RunInUi(() => _loadingPreview = false);
                ModBase.Log(ex, $"[SchematicPage] 读取投影失败（{path}）", ModBase.LogLevel.Hint,
                    userSummary: Lang.Text("Tools.Schematic.LoadFailed", Path.GetFileName(path)));
            }
        }, "Read Schematic");
    }

    private static string BuildPreviewInfo(string path, ModSchematicPreview.Data data)
    {
        var file = new FileInfo(path);
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var minZ = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var maxZ = int.MinValue;
        foreach (var block in data.Blocks)
        {
            minX = Math.Min(minX, block.X);
            minY = Math.Min(minY, block.Y);
            minZ = Math.Min(minZ, block.Z);
            maxX = Math.Max(maxX, block.X);
            maxY = Math.Max(maxY, block.Y);
            maxZ = Math.Max(maxZ, block.Z);
        }

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        var length = maxZ - minZ + 1;
        var lines = new List<string>
        {
            Lang.Text("Instance.Resource.Item.Info.File", file.Name, ModBase.GetString(file.Length)),
            Lang.Text("Instance.Resource.Item.Schematic.EnclosingDimensions") + $"{width} × {height} × {length}",
            Lang.Text("Instance.Resource.Item.Schematic.TotalBlocks") + Lang.Number(data.Blocks.Count, "N0"),
            Lang.Text("Instance.Resource.Item.Schematic.ModifiedTime") + $"{file.LastWriteTime:g}"
        };
        return string.Join("\r\n", lines);
    }

    private void ShowPreview(string path, string info, ModSchematicPreview.Data data,
        ModSchematicTextures? textures)
    {
        PanPreview.Children.Clear();
        var jarPath = _instance is null ? null : Path.Combine(_instance.PathInstance, _instance.Name + ".jar");
        var view = new SchematicPreviewView(Path.GetFileName(path), info, data, textures, null, ClosePreview, jarPath);
        PanPreview.Children.Add(view);
        PanBack.Visibility = Visibility.Collapsed;
        PanPreview.Visibility = Visibility.Visible;
    }

    private void ClosePreview()
    {
        PanPreview.Children.Clear();
        PanPreview.Visibility = Visibility.Collapsed;
        PanBack.Visibility = Visibility.Visible;
    }

    #endregion
}
