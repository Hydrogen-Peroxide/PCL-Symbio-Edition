using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     原理图预览子界面：像存档编辑一样直接嵌在实例页面里，不再单独开窗口。
///     包含 3D 预览、右上角所需方块列表、分层显示与详情浮层。
/// </summary>
public sealed class SchematicPreviewView : UserControl
{
    private readonly Action _onBack;
    private readonly SchematicViewer3D? _viewer;
    private readonly MyCheckBox _checkLayer;
    private readonly MySlider _sliderLayer;
    private readonly TextBlock _labLayer;
    private readonly Border _detailPanel;
    private readonly int _layerCount;
    private readonly DispatcherTimer _layerWatch;
    private readonly string? _jarPath;

    public SchematicPreviewView(string title, string info, ModSchematicPreview.Data data,
        ModSchematicTextures? textures, string? url, Action onBack, string? jarPath = null)
    {
        _onBack = onBack;
        _jarPath = jarPath;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1d, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        #region 顶部栏

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backButton = new MyButton
        {
            Text = Lang.Text("Instance.Resource.Item.Info.Return"), Height = 32, MinWidth = 96,
            Padding = new Thickness(14, 0, 14, 0)
        };
        backButton.Click += (_, _) => _onBack();
        header.Children.Add(backButton);

        var titleText = new TextBlock
        {
            Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        if (!string.IsNullOrEmpty(url))
        {
            var openButton = new MyButton
            {
                Text = Lang.Text("Instance.Resource.Item.Info.OpenWebsite"), Height = 32, MinWidth = 104,
                Padding = new Thickness(14, 0, 14, 0), ColorType = MyButton.ColorState.Highlight
            };
            openButton.Click += (_, _) => ModBase.OpenWebsite(url);
            Grid.SetColumn(openButton, 2);
            header.Children.Add(openButton);
        }

        var detailButton = new MyButton
        {
            Text = Lang.Text("Instance.Resource.Schematic.Preview.Detail"), Height = 32, MinWidth = 72,
            Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(detailButton, 3);
        header.Children.Add(detailButton);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        #endregion

        #region 预览区

        var body = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, BorderThickness = new Thickness(1)
        };
        body.SetResourceReference(Border.BackgroundProperty, "ColorBrushGray8");
        body.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray5");
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var bodyGrid = new Grid();
        body.Child = bodyGrid;

        if (textures is not null)
            try
            {
                _viewer = new SchematicViewer3D(data, textures);
                bodyGrid.Children.Add(_viewer);
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "构建原理图 3D 预览失败", ModBase.LogLevel.Developer);
            }

        if (_viewer is null)
        {
            var notice = new TextBlock
            {
                Text = textures is null
                    ? Lang.Text("Instance.Resource.Schematic.Preview.TooLarge", data.Blocks.Count,
                        SchematicViewer3D.MaxRenderBlocks)
                    : Lang.Text("Instance.Resource.Schematic.Preview.BuildFailed"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40), FontSize = 13, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            notice.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            bodyGrid.Children.Add(notice);
        }

        // 右上角所需方块列表
        var listPanel = BuildBlockList(data, textures);
        bodyGrid.Children.Add(listPanel);

        // 详情浮层，默认收起
        _detailPanel = BuildDetailPanel(info);
        _detailPanel.Visibility = Visibility.Collapsed;
        bodyGrid.Children.Add(_detailPanel);
        detailButton.Click += (_, _) =>
            _detailPanel.Visibility = _detailPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        #endregion

        #region 底部分层控制

        // 没有 3D 视图时（例如方块数超限）也要算出层数，避免显示成只有一层
        _layerCount = _viewer is not null
            ? _viewer.MaxLayer - _viewer.MinLayer + 1
            : data.Blocks.Count == 0
                ? 0
                : data.Blocks.Max(block => block.Y) - data.Blocks.Min(block => block.Y) + 1;

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _checkLayer = new MyCheckBox
        {
            Text = Lang.Text("Instance.Resource.Schematic.Preview.LayerOnly"),
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_checkLayer);

        // MySlider 内部会用 MaxValue 做除数，只有一层时也要保证 MaxValue 至少为 1
        _sliderLayer = new MySlider
        {
            Width = 180, Margin = new Thickness(16, 0, 0, 0), IsEnabled = false,
            MaxValue = Math.Max(1, _layerCount - 1),
            Visibility = _layerCount > 1 ? Visibility.Visible : Visibility.Collapsed
        };
        // 默认停在最高层，打开分层显示时从顶层往下看最自然
        _sliderLayer.Value = _layerCount > 1 ? _layerCount - 1 : 0;
        Grid.SetColumn(_sliderLayer, 1);
        footer.Children.Add(_sliderLayer);

        _labLayer = new TextBlock
        {
            Text = _layerCount > 1
                ? Lang.Text("Instance.Resource.Schematic.Preview.LayerAll")
                : Lang.Text("Instance.Resource.Schematic.Preview.LayerTotal", _layerCount),
            Margin = new Thickness(12, 0, 0, 0), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_viewer is null || _layerCount <= 1)
        {
            _checkLayer.Visibility = Visibility.Collapsed;
            _labLayer.Visibility = Visibility.Collapsed;
        }

        _labLayer.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
        Grid.SetColumn(_labLayer, 2);
        footer.Children.Add(_labLayer);

        if (_viewer is not null)
        {
            var hint = new TextBlock
            {
                Text = Lang.Text("Instance.Resource.Schematic.Preview.Hint"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            Grid.SetColumn(hint, 4);
            footer.Children.Add(hint);
        }

        var footerBar = new Border
        {
            Child = footer, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0)
        };
        footerBar.SetResourceReference(Border.BackgroundProperty, "ColorBrushTransparentBackground");
        footerBar.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray5");
        footerBar.Visibility = _viewer is null ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(footerBar, 2);
        root.Children.Add(footerBar);

        _checkLayer.Change += (_, _) => ApplyLayer();
        _sliderLayer.Change += (_, _) => ApplyLayer();

        // MySlider 的 Change 事件在有其他动画时可能不触发，这里再做一层轮询兜底
        _layerWatch = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _layerWatch.Tick += (_, _) => SyncLayer();
        Loaded += (_, _) => _layerWatch.Start();
        Unloaded += (_, _) => _layerWatch.Stop();

        #endregion

        Content = root;
    }

    private void ApplyLayer()
    {
        SyncLayer();
    }

    /// <summary>同步分层状态；同时作为 MySlider 拖动时的轮询兜底，避免 Change 事件被动画吞掉。</summary>
    private void SyncLayer()
    {
        if (_viewer is null) return;
        var show = _checkLayer.Checked == true;
        _sliderLayer.IsEnabled = show;
        if (!show)
        {
            if (_viewer.VisibleTopLayer is not null)
            {
                _viewer.VisibleTopLayer = null;
                _labLayer.Text = Lang.Text("Instance.Resource.Schematic.Preview.LayerAll");
            }

            return;
        }

        var count = Math.Max(1, _layerCount);
        var index = Math.Clamp(_sliderLayer.Value, 0, count - 1);
        var top = _viewer.MinLayer + index;
        if (_viewer.VisibleTopLayer == top) return;
        _viewer.VisibleTopLayer = top;
        _labLayer.Text = Lang.Text("Instance.Resource.Schematic.Preview.LayerInfo", index + 1, count, top);
    }

    private Border BuildBlockList(ModSchematicPreview.Data data, ModSchematicTextures? textures)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in data.Blocks)
        {
            var id = TrimId(block.Name);
            counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        var panel = new Border
        {
            Width = 224, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1)
        };
        panel.SetResourceReference(Border.BackgroundProperty, "ColorBrushTransparentBackground");
        panel.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray5");

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1d, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = Lang.Text("Instance.Resource.Schematic.Preview.BlockList", counts.Count, data.Blocks.Count),
            FontSize = 12.5, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 10, 12, 8),
            TextWrapping = TextWrapping.Wrap
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
        stack.Children.Add(header);

        var line = new Rectangle { Height = 1 };
        line.SetResourceReference(Shape.FillProperty, "ColorBrushGray5");
        Grid.SetRow(line, 1);
        stack.Children.Add(line);

        var list = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(600))
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (textures is not null)
            {
                var image = new Image
                {
                    Width = 20, Height = 20, Stretch = Stretch.Uniform,
                    Source = textures.GetBlockIcon(pair.Key), Margin = new Thickness(0, 0, 8, 0),
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                row.Children.Add(image);
            }

            var name = new TextBlock
            {
                Text = ModSchematicBlockNames.Get(pair.Key, _jarPath), FontSize = 12,
                ToolTip = pair.Key, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var count = new TextBlock
            {
                Text = "×" + pair.Value, FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            count.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
            Grid.SetColumn(count, 2);
            row.Children.Add(count);

            list.Children.Add(row);
        }

        var scroll = CreateScrollViewer(list, 320);
        Grid.SetRow(scroll, 2);
        stack.Children.Add(scroll);
        panel.Child = stack;
        return panel;
    }

    private static Border BuildDetailPanel(string info)
    {
        var text = new TextBlock { Text = info, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2");

        var panel = new Border
        {
            Child = CreateScrollViewer(text, 240), Width = 380, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 12),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1)
        };
        panel.SetResourceReference(Border.BackgroundProperty, "ColorBrushTransparentBackground");
        panel.SetResourceReference(Border.BorderBrushProperty, "ColorBrushGray5");
        return panel;
    }

    /// <summary>使用带独立滚动条列的样式，避免原生滚动条盖住列表内容。</summary>
    private static ScrollViewer CreateScrollViewer(UIElement content, double maxHeight)
    {
        var scroll = new MyScrollViewer
        {
            Content = content, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.SetResourceReference(FrameworkElement.StyleProperty, "ScrollViewerFullMargin");
        return scroll;
    }

    private static string TrimId(string name)
    {
        var id = name.Trim();
        if (id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) id = id[10..];
        var cut = id.IndexOfAny(['[', '{']);
        if (cut >= 0) id = id[..cut];
        return id.ToLowerInvariant();
    }
}
