using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PCL.Core.App.Localization;
using PCL.Core.UI;
using Path = System.IO.Path;

namespace PCL;

public partial class MapEditorView
{
    private static readonly Brush SelectionFill = CreateBrush(96, 64, 140, 255);
    private static readonly Brush SelectionStroke = CreateBrush(220, 120, 190, 255);
    private static readonly Brush FilterFill = CreateBrush(70, 255, 150, 20);

    private enum SelectionMode
    {
        Rectangle,
        Brush
    }

    private enum ChunkFilter
    {
        None,
        NotFull,
        VersionMismatch,
        Recent
    }

    private readonly HashSet<(int X, int Z)> _selected = [];
    private readonly Dictionary<(int X, int Z), ModRegionFile.ChunkMeta> _metas = [];
    private readonly Dictionary<(int RegionX, int RegionZ), bool[]> _present = [];
    private readonly Dictionary<(int RegionX, int RegionZ), Image> _regionImages = [];
    private List<string> _regionFiles = [];

    private string? _worldPath;
    private string? _dimensionRoot;
    private string _dimension = "";
    private string _statusText = "";
    private bool _showGrid;
    private bool _layerMode;
    private bool _layerRendering;
    private bool _layerPending;
    private bool _suppressLayerChange;
    private int _layerMinY;
    private int _layerMaxY = 255;
    private int _layerTopY = 63;
    private int _layerBottomY = 63;
    private SelectionMode _selectionMode = SelectionMode.Rectangle;
    private ChunkFilter _filter = ChunkFilter.None;
    private bool _painting;
    private int _dominantDataVersion;
    private bool _suppressDimensionChange;
    private List<(string Path, byte[]? Data)>? _undoSnapshot;
    private int _originChunkX;
    private int _originChunkZ;
    private int _mapWidthChunks;
    private int _mapHeightChunks;
    private double _zoom = 1d;
    private bool _loading;
    private (int X, int Z)? _dragStart;
    private Rectangle? _dragRectangle;

    public MapEditorView()
    {
        InitializeComponent();
    }

    private static Brush CreateBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>加载存档的 region 目录并渲染地形图。</summary>
    public Task LoadWorldAsync(string worldPath) => LoadWorldAsync(worldPath, string.Empty, null);

    private (int Bottom, int Top)? CurrentLayerRange => _layerMode ? (_layerBottomY, _layerTopY) : null;

    private async Task LoadWorldAsync(string worldPath, string dimension, (int Bottom, int Top)? layer)
    {
        var sameTarget = ReferenceEquals(_worldPath, worldPath) && _dimension == dimension;
        _worldPath = worldPath;
        _dimension = dimension;
        _dimensionRoot = dimension.Length == 0 ? worldPath : Path.Combine(worldPath, dimension);
        if (!sameTarget)
        {
            _undoSnapshot = null;
            BtnUndo.IsEnabled = false;
        }

        SyncDimensionSelection();
        _selected.Clear();
        _present.Clear();
        _metas.Clear();
        _regionFiles = [];
        _regionImages.Clear();
        _loading = true;
        BtnDelete.IsEnabled = false;

        PanRegions.Children.Clear();
        PanSelection.Children.Clear();
        PanFilter.Children.Clear();
        PanInput.Children.Clear();
        LabWorld.Text = Lang.Text("Instance.Saves.MapEditor.World", Path.GetFileName(worldPath));
        LabSelection.Text = "";
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var regionDirectory = Path.Combine(_dimensionRoot, "region");
            if (!Directory.Exists(regionDirectory))
            {
                _statusText = Lang.Text("Instance.Saves.MapEditor.Empty");
                LabStatus.Text = _statusText;
                return;
            }

            var files = Directory.GetFiles(regionDirectory, "*.mca");
            if (files.Length == 0)
            {
                _statusText = Lang.Text("Instance.Saves.MapEditor.Empty");
                LabStatus.Text = _statusText;
                return;
            }

            _regionFiles = files.ToList();

            var rendered = new ConcurrentBag<ModRegionFile.RegionRender>();
            var done = 0;
            await Task.Run(() => Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) },
                file =>
                {
                    var render = ModRegionFile.RenderRegion(file, layer);
                    if (render is not null) rendered.Add(render);
                    var current = Interlocked.Increment(ref done);
                    if (current % 8 == 0)
                        ModBase.RunInUi(() =>
                            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Progress", current, files.Length));
                }));

            var renders = rendered
                .OrderBy(render => render.RegionZ)
                .ThenBy(render => render.RegionX)
                .ToList();

            // 加载期间可能已切换或关闭编辑器
            if (!ReferenceEquals(_worldPath, worldPath) || _dimension != dimension) return;
            if (renders.Count == 0)
            {
                _statusText = Lang.Text("Instance.Saves.MapEditor.LoadFailed", "无法读取任何区域文件");
                LabStatus.Text = _statusText;
                return;
            }

            var minRegionX = renders.Min(render => render.RegionX);
            var maxRegionX = renders.Max(render => render.RegionX);
            var minRegionZ = renders.Min(render => render.RegionZ);
            var maxRegionZ = renders.Max(render => render.RegionZ);

            var width = (maxRegionX - minRegionX + 1) * ModRegionFile.RegionPixels;
            var height = (maxRegionZ - minRegionZ + 1) * ModRegionFile.RegionPixels;
            _originChunkX = minRegionX * ModRegionFile.RegionChunks;
            _originChunkZ = minRegionZ * ModRegionFile.RegionChunks;
            _mapWidthChunks = width / ModRegionFile.ChunkPixels;
            _mapHeightChunks = height / ModRegionFile.ChunkPixels;

            foreach (var render in renders)
            {
                var bitmap = BitmapSource.Create(ModRegionFile.RegionPixels, ModRegionFile.RegionPixels,
                    96, 96, PixelFormats.Bgra32, null, render.Pixels, ModRegionFile.RegionPixels * 4);
                bitmap.Freeze();

                var image = new Image
                {
                    Source = bitmap,
                    Width = ModRegionFile.RegionPixels,
                    Height = ModRegionFile.RegionPixels,
                    Stretch = Stretch.Fill
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, (render.RegionX - minRegionX) * ModRegionFile.RegionPixels);
                Canvas.SetTop(image, (render.RegionZ - minRegionZ) * ModRegionFile.RegionPixels);
                PanRegions.Children.Add(image);
                _regionImages[(render.RegionX, render.RegionZ)] = image;

                _present[(render.RegionX, render.RegionZ)] = render.Present;
                for (var index = 0; index < ModRegionFile.RegionChunks * ModRegionFile.RegionChunks; index++)
                {
                    if (render.Meta[index] is not { } meta) continue;
                    _metas[(render.RegionX * ModRegionFile.RegionChunks + index % ModRegionFile.RegionChunks,
                        render.RegionZ * ModRegionFile.RegionChunks + index / ModRegionFile.RegionChunks)] = meta;
                }
            }

            foreach (var canvas in new[] { PanRegions, PanFilter, PanGrid, PanSelection, PanInput })
            {
                canvas.Width = width;
                canvas.Height = height;
            }

            GridChunk.Width = width;
            GridChunk.Height = height;
            GridRegion.Width = width;
            GridRegion.Height = height;

            PanMapRoot.Width = width;
            PanMapRoot.Height = height;
            _dominantDataVersion = _metas.Values
                .GroupBy(meta => meta.DataVersion)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .FirstOrDefault()?.Key ?? 0;
            RefreshFilterHighlight();
            _statusText = Lang.Text("Instance.Saves.MapEditor.Status", renders.Count,
                renders.Sum(render => render.ChunkCount));
            LabStatus.Text = _statusText;
            UpdateLayerRange(layer);
            ApplyZoom();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "加载地图编辑器失败", ModBase.LogLevel.Feedback);
            _statusText = Lang.Text("Instance.Saves.MapEditor.LoadFailed", ex.Message);
            LabStatus.Text = _statusText;
        }
        finally
        {
            if (ReferenceEquals(_worldPath, worldPath) && _dimension == dimension) _loading = false;
        }
    }

    /// <summary>关闭当前世界，释放地图渲染结果。</summary>
    public void CloseWorld()
    {
        _worldPath = null;
        _dimensionRoot = null;
        _loading = false;
        _selected.Clear();
        _present.Clear();
        _metas.Clear();
        _regionFiles = [];
        _regionImages.Clear();
        _undoSnapshot = null;
        BtnUndo.IsEnabled = false;
        _dragStart = null;
        PanRegions.Children.Clear();
        PanSelection.Children.Clear();
        PanFilter.Children.Clear();
        PanGrid.Width = 0;
        PanGrid.Height = 0;
        PanFilter.Width = 0;
        PanFilter.Height = 0;
        GridChunk.Width = 0;
        GridChunk.Height = 0;
        GridRegion.Width = 0;
        GridRegion.Height = 0;
        PanRegions.Width = 0;
        PanRegions.Height = 0;
        PanSelection.Width = 0;
        PanSelection.Height = 0;
        PanInput.Width = 0;
        PanInput.Height = 0;
        PanMapRoot.Width = 0;
        PanMapRoot.Height = 0;
    }

    private void ApplyZoom()
    {
        _zoom = Math.Clamp(_zoom, 0.5d, 6d);
        PanMapRoot.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        LabZoom.Text = $"{_zoom:0.##}×";
    }

    private void BtnZoomIn_Click(object sender, EventArgs e)
    {
        _zoom *= 1.25d;
        ApplyZoom();
    }

    private void BtnZoomOut_Click(object sender, EventArgs e)
    {
        _zoom *= 0.8d;
        ApplyZoom();
    }

    private void BtnReload_Click(object sender, EventArgs e)
    {
        if (!_loading && !string.IsNullOrEmpty(_worldPath))
            _ = LoadWorldAsync(_worldPath, _dimension, CurrentLayerRange);
    }

    private void BtnLayer_Click(object sender, EventArgs e)
    {
        if (_worldPath is null || _loading) return;
        _layerMode = !_layerMode;
        BtnLayer.Opacity = _layerMode ? 1d : 0.55d;
        PanLayer.Visibility = _layerMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_layerMode)
        {
            _ = LoadWorldAsync(_worldPath, _dimension, null);
            return;
        }

        _layerTopY = Math.Clamp(_layerTopY, _layerMinY, _layerMaxY);
        _layerBottomY = Math.Clamp(_layerBottomY, _layerMinY, _layerMaxY);
        SetLayerSliders();
        _ = LoadWorldAsync(_worldPath, _dimension, (_layerBottomY, _layerTopY));
    }

    private void SliderLayer_Change(object sender, bool user)
    {
        if (_suppressLayerChange || !_layerMode || _worldPath is null) return;
        if (_loading && !_layerRendering) return;

        _layerTopY = _layerMinY + SliderLayer.Value;
        UpdateLayerLabels();
        if (_layerRendering)
        {
            _layerPending = true;
            return;
        }

        _ = RenderLayerAsync();
    }

    private void SliderLayerBottom_Change(object sender, bool user)
    {
        if (_suppressLayerChange || !_layerMode || _worldPath is null) return;
        if (_loading && !_layerRendering) return;

        _layerBottomY = _layerMinY + SliderLayerBottom.Value;
        UpdateLayerLabels();
        if (_layerRendering)
        {
            _layerPending = true;
            return;
        }

        _ = RenderLayerAsync();
    }

    private async Task RenderLayerAsync()
    {
        _layerRendering = true;
        try
        {
            do
            {
                _layerPending = false;
                if (_worldPath is null) return;
                await RefreshLayerAsync(_layerBottomY, _layerTopY);
            } while (_layerPending);
        }
        finally
        {
            _layerRendering = false;
        }
    }

    /// <summary>只重新渲染当前 Y 范围的图片，不清空选区与画布。</summary>
    private async Task RefreshLayerAsync(int bottomY, int topY)
    {
        if (_dimensionRoot is null || _regionFiles.Count == 0) return;
        _loading = true;
        try
        {
            var range = (Bottom: bottomY, Top: topY);
            var files = _regionFiles.ToArray();
            var rendered = new ConcurrentBag<ModRegionFile.RegionRender>();
            await Task.Run(() => Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) },
                file =>
                {
                    var render = ModRegionFile.RenderRegion(file, range);
                    if (render is not null) rendered.Add(render);
                }));

            if (!_layerMode || _layerBottomY != bottomY || _layerTopY != topY) return;
            foreach (var render in rendered)
            {
                if (!_regionImages.TryGetValue((render.RegionX, render.RegionZ), out var image)) continue;
                var bitmap = BitmapSource.Create(ModRegionFile.RegionPixels, ModRegionFile.RegionPixels,
                    96, 96, PixelFormats.Bgra32, null, render.Pixels, ModRegionFile.RegionPixels * 4);
                bitmap.Freeze();
                image.Source = bitmap;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "渲染地图 Y 层失败", ModBase.LogLevel.Hint);
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetLayerSliders()
    {
        _suppressLayerChange = true;
        try
        {
            SliderLayer.MaxValue = _layerMaxY - _layerMinY;
            SliderLayerBottom.MaxValue = _layerMaxY - _layerMinY;
            SliderLayer.Value = Math.Clamp(_layerTopY - _layerMinY, 0, SliderLayer.MaxValue);
            SliderLayerBottom.Value = Math.Clamp(_layerBottomY - _layerMinY, 0, SliderLayer.MaxValue);
            UpdateLayerLabels();
        }
        finally
        {
            _suppressLayerChange = false;
        }
    }

    private void UpdateLayerLabels()
    {
        LabLayerTop.Text = Lang.Text("Instance.Saves.MapEditor.LayerTop", _layerTopY);
        LabLayerBottom.Text = Lang.Text("Instance.Saves.MapEditor.LayerBottom", _layerBottomY);
    }

    private void UpdateLayerRange((int Bottom, int Top)? requestedLayer)
    {
        var modern = _metas.Values.Any(meta => meta.DataVersion >= 2825);
        _layerMinY = modern ? -64 : 0;
        _layerMaxY = modern ? 319 : 255;
        SliderLayer.getHintText = new Func<int, string>(value => $"Y {_layerMinY + value}");
        SliderLayerBottom.getHintText = new Func<int, string>(value => $"Y {_layerMinY + value}");
        if (requestedLayer is { } range)
        {
            _layerTopY = Math.Clamp(range.Top, _layerMinY, _layerMaxY);
            _layerBottomY = Math.Clamp(range.Bottom, _layerMinY, _layerMaxY);
        }
        else if (!_layerMode)
        {
            _layerTopY = Math.Clamp(63, _layerMinY, _layerMaxY);
            _layerBottomY = _layerTopY;
        }
        else
        {
            _layerTopY = Math.Clamp(_layerTopY, _layerMinY, _layerMaxY);
            _layerBottomY = Math.Clamp(_layerBottomY, _layerMinY, _layerMaxY);
        }

        SetLayerSliders();
    }

    private void BtnGrid_Click(object sender, EventArgs e)
    {
        _showGrid = !_showGrid;
        PanGrid.Visibility = _showGrid ? Visibility.Visible : Visibility.Collapsed;
        BtnGrid.Opacity = _showGrid ? 1d : 0.55d;
    }

    private void BtnSelectRect_Click(object sender, MouseButtonEventArgs e) =>
        SetSelectionMode(SelectionMode.Rectangle);

    private void BtnSelectBrush_Click(object sender, MouseButtonEventArgs e) =>
        SetSelectionMode(SelectionMode.Brush);

    private void SetSelectionMode(SelectionMode mode)
    {
        _selectionMode = mode;
        BtnSelectRect.ColorType =
            mode == SelectionMode.Rectangle ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal;
        BtnSelectBrush.ColorType =
            mode == SelectionMode.Brush ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal;
    }

    private void ComboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _filter = ComboFilter.SelectedItem is MyComboBoxItem { Tag: string tag } && int.TryParse(tag, out var value)
            ? (ChunkFilter)value
            : ChunkFilter.None;
        RefreshFilterHighlight();
    }

    private bool MatchesFilter(ModRegionFile.ChunkMeta meta) => _filter switch
    {
        ChunkFilter.NotFull => meta.Status is not "minecraft:full",
        ChunkFilter.VersionMismatch => meta.DataVersion != _dominantDataVersion,
        ChunkFilter.Recent => meta.LastSavedUtc >= DateTime.UtcNow.AddDays(-7d),
        _ => false
    };

    private List<(int X, int Z)> GetFilterMatches()
    {
        if (_filter == ChunkFilter.None) return [];
        return _metas.Where(pair => MatchesFilter(pair.Value)).Select(pair => pair.Key).ToList();
    }

    private void RefreshFilterHighlight()
    {
        PanFilter.Children.Clear();
        if (_worldPath is null || _filter == ChunkFilter.None)
        {
            BtnFilterSelect.IsEnabled = false;
            return;
        }

        var matches = GetFilterMatches();
        BtnFilterSelect.IsEnabled = matches.Count > 0;
        if (matches.Count == 0) return;

        var geometry = new GeometryGroup();
        foreach (var (chunkX, chunkZ) in matches)
            geometry.Children.Add(new RectangleGeometry(new Rect(
                (chunkX - _originChunkX) * ModRegionFile.ChunkPixels,
                (chunkZ - _originChunkZ) * ModRegionFile.ChunkPixels,
                ModRegionFile.ChunkPixels, ModRegionFile.ChunkPixels)));
        PanFilter.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Fill = FilterFill,
            IsHitTestVisible = false
        });
    }

    private void BtnFilterSelect_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _loading) return;
        var matches = GetFilterMatches();
        if (matches.Count == 0) return;

        _selected.Clear();
        foreach (var chunk in matches) _selected.Add(chunk);
        RenderSelection();
        HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Filter.Selected", matches.Count));
    }

    private void ComboDimension_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDimensionChange || !IsLoaded || _worldPath is null || _loading) return;
        var dimension = ComboDimension.SelectedItem is MyComboBoxItem item ? item.Tag as string ?? "" : "";
        if (dimension == _dimension) return;
        _ = LoadWorldAsync(_worldPath, dimension, CurrentLayerRange);
    }

    private void SyncDimensionSelection()
    {
        _suppressDimensionChange = true;
        try
        {
            for (var i = 0; i < ComboDimension.Items.Count; i++)
            {
                if (ComboDimension.Items[i] is not MyComboBoxItem item) continue;
                if ((item.Tag as string ?? "") != _dimension) continue;
                ComboDimension.SelectedIndex = i;
                break;
            }
        }
        finally
        {
            _suppressDimensionChange = false;
        }
    }

    private void BtnJump_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _loading) return;
        if (!int.TryParse(TextJumpX.Text.Trim(), out var chunkX) ||
            !int.TryParse(TextJumpZ.Text.Trim(), out var chunkZ))
        {
            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.JumpInvalid");
            return;
        }

        if (!ChunkExists(chunkX, chunkZ))
        {
            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.JumpMissing", chunkX, chunkZ);
            return;
        }

        var centerX = ((chunkX - _originChunkX) * ModRegionFile.ChunkPixels + ModRegionFile.ChunkPixels / 2d) * _zoom;
        var centerZ = ((chunkZ - _originChunkZ) * ModRegionFile.ChunkPixels + ModRegionFile.ChunkPixels / 2d) * _zoom;
        PanScroll.ScrollToHorizontalOffset(Math.Max(0d, centerX - PanScroll.ViewportWidth / 2d));
        PanScroll.ScrollToVerticalOffset(Math.Max(0d, centerZ - PanScroll.ViewportHeight / 2d));
        UpdateHoverInfo(new Point((chunkX - _originChunkX) * ModRegionFile.ChunkPixels,
            (chunkZ - _originChunkZ) * ModRegionFile.ChunkPixels));
    }

    private void PanInput_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        _zoom *= e.Delta > 0 ? 1.2d : 0.8d;
        ApplyZoom();
        e.Handled = true;
    }

    private (int X, int Z)? GetChunkAt(Point point)
    {
        if (_mapWidthChunks <= 0 || _mapHeightChunks <= 0) return null;

        var chunkOffsetX = (int)Math.Floor(point.X / ModRegionFile.ChunkPixels);
        var chunkOffsetZ = (int)Math.Floor(point.Y / ModRegionFile.ChunkPixels);
        if (chunkOffsetX < 0 || chunkOffsetZ < 0 ||
            chunkOffsetX >= _mapWidthChunks || chunkOffsetZ >= _mapHeightChunks) return null;

        var chunkX = _originChunkX + chunkOffsetX;
        var chunkZ = _originChunkZ + chunkOffsetZ;
        if (!ChunkExists(chunkX, chunkZ)) return null;
        return (chunkX, chunkZ);
    }

    private bool ChunkExists(int chunkX, int chunkZ)
    {
        var regionX = FloorDiv(chunkX, ModRegionFile.RegionChunks);
        var regionZ = FloorDiv(chunkZ, ModRegionFile.RegionChunks);
        if (!_present.TryGetValue((regionX, regionZ), out var present)) return false;

        var localX = chunkX - regionX * ModRegionFile.RegionChunks;
        var localZ = chunkZ - regionZ * ModRegionFile.RegionChunks;
        return present[localX + localZ * ModRegionFile.RegionChunks];
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

    private void PanInput_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _loading) return;
        var chunk = GetChunkAt(e.GetPosition(PanInput));
        if (chunk is null) return;

        if (_selectionMode == SelectionMode.Brush)
        {
            _painting = true;
            PanInput.CaptureMouse();
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) _selected.Clear();
            PaintChunk(chunk.Value);
            return;
        }

        _dragStart = chunk;
        PanInput.CaptureMouse();

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            _selected.Clear();
            RenderSelection();
        }

        _dragRectangle = new Rectangle
        {
            Width = ModRegionFile.ChunkPixels,
            Height = ModRegionFile.ChunkPixels,
            Fill = SelectionFill,
            Stroke = SelectionStroke,
            StrokeThickness = 1
        };
        PanSelection.Children.Add(_dragRectangle);
        UpdateDragRectangle(chunk.Value);
    }

    private void PaintChunk((int X, int Z) chunk)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) _selected.Remove(chunk);
        else _selected.Add(chunk);
        RenderSelection();
    }

    private void PanInput_MouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(PanInput);
        if (_selectionMode == SelectionMode.Brush)
        {
            if (_painting && e.LeftButton == MouseButtonState.Pressed)
            {
                var brushChunk = GetChunkAt(position);
                if (brushChunk is not null) PaintChunk(brushChunk.Value);
            }
            else if (e.LeftButton != MouseButtonState.Pressed)
            {
                UpdateHoverInfo(position);
            }

            return;
        }

        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateHoverInfo(position);
            return;
        }

        var chunk = GetChunkAt(position);
        if (chunk is null) return;
        UpdateDragRectangle(chunk.Value);
    }

    private void PanInput_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_painting) RestoreStatus();
    }

    private void UpdateHoverInfo(Point position)
    {
        if (_worldPath is null || _loading) return;
        var chunk = GetChunkAt(position);
        if (chunk is null || !_metas.TryGetValue(chunk.Value, out var meta))
        {
            RestoreStatus();
            return;
        }

        var status = meta.Status?.Replace("minecraft:", "") ?? "-";
        // 区块 NBT 的 LastUpdate 是游戏刻，只有区域文件的修改时间才是真实时间
        var time = meta.LastSavedUtc > DateTime.MinValue
            ? meta.LastSavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "-";
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.ChunkInfo", chunk.Value.X, chunk.Value.Z,
            status, meta.DataVersion, time, meta.InhabitedTime / 1200);
    }

    private void RestoreStatus()
    {
        if (_worldPath is null || _loading) return;
        LabStatus.Text = _statusText;
    }

    private void UpdateDragRectangle((int X, int Z) current)
    {
        if (_dragRectangle is null || _dragStart is null) return;

        var left = Math.Min(_dragStart.Value.X, current.X);
        var top = Math.Min(_dragStart.Value.Z, current.Z);
        var width = Math.Abs(_dragStart.Value.X - current.X) + 1;
        var height = Math.Abs(_dragStart.Value.Z - current.Z) + 1;

        Canvas.SetLeft(_dragRectangle, (left - _originChunkX) * ModRegionFile.ChunkPixels);
        Canvas.SetTop(_dragRectangle, (top - _originChunkZ) * ModRegionFile.ChunkPixels);
        _dragRectangle.Width = width * ModRegionFile.ChunkPixels;
        _dragRectangle.Height = height * ModRegionFile.ChunkPixels;
    }

    private void PanInput_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionMode == SelectionMode.Brush)
        {
            if (!_painting) return;
            _painting = false;
            PanInput.ReleaseMouseCapture();
            RenderSelection();
            return;
        }

        if (_dragStart is null) return;

        var end = GetChunkAt(e.GetPosition(PanInput)) ?? _dragStart.Value;
        var left = Math.Min(_dragStart.Value.X, end.X);
        var right = Math.Max(_dragStart.Value.X, end.X);
        var top = Math.Min(_dragStart.Value.Z, end.Z);
        var bottom = Math.Max(_dragStart.Value.Z, end.Z);

        for (var chunkX = left; chunkX <= right; chunkX++)
        for (var chunkZ = top; chunkZ <= bottom; chunkZ++)
        {
            if (ChunkExists(chunkX, chunkZ)) _selected.Add((chunkX, chunkZ));
        }

        _dragStart = null;
        if (_dragRectangle is not null)
        {
            PanSelection.Children.Remove(_dragRectangle);
            _dragRectangle = null;
        }

        PanInput.ReleaseMouseCapture();
        RenderSelection();
    }

    private void RenderSelection()
    {
        PanSelection.Children.Clear();
        if (_selected.Count > 0)
        {
            var geometry = new GeometryGroup();
            foreach (var (chunkX, chunkZ) in _selected)
                geometry.Children.Add(new RectangleGeometry(new Rect(
                    (chunkX - _originChunkX) * ModRegionFile.ChunkPixels,
                    (chunkZ - _originChunkZ) * ModRegionFile.ChunkPixels,
                    ModRegionFile.ChunkPixels, ModRegionFile.ChunkPixels)));
            PanSelection.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = SelectionFill,
                Stroke = SelectionStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        LabSelection.Text = Lang.Text("Instance.Saves.MapEditor.Selection", _selected.Count);
        BtnDelete.IsEnabled = _selected.Count > 0 && !_loading;
    }

    private async void BtnMove_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;
        if (!int.TryParse(TextMoveX.Text.Trim(), out var offsetX) ||
            !int.TryParse(TextMoveZ.Text.Trim(), out var offsetZ) || offsetX == 0 && offsetZ == 0)
        {
            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.MoveInvalid");
            return;
        }

        var count = _selected.Count;
        if (ModMain.MyMsgBox(Lang.Text("Instance.Saves.MapEditor.MoveConfirmMessage", count, offsetX, offsetZ),
                Lang.Text("Instance.Saves.MapEditor.MoveConfirmTitle"),
                Lang.Text("Instance.Saves.MapEditor.Move"),
                Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        var root = _dimensionRoot!;
        var worldPath = _worldPath;
        var dimension = _dimension;
        var chunks = _selected.ToList();
        var targets = chunks.Select(chunk => (chunk.X + offsetX, chunk.Z + offsetZ)).ToList();
        var snapshot = SnapshotRegionFiles(chunks, includeRelated: true);
        var seen = new HashSet<string>(snapshot.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SnapshotRegionFiles(targets))
            if (seen.Add(entry.Path)) snapshot.Add(entry);

        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Moving", count);
        try
        {
            var moved = await Task.Run(() => ModRegionFile.MoveChunks(root, chunks, offsetX, offsetZ));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Moved", moved));
            SetUndoSnapshot(snapshot);
            _loading = false;
            await LoadWorldAsync(worldPath, dimension, CurrentLayerRange);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "移动区块失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.MoveFailed", ex.Message), HintType.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void BtnCopy_Click(object sender, EventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;
        var root = _dimensionRoot!;
        var worldPath = _worldPath;
        var dimension = _dimension;
        var chunks = _selected.ToList();
        var snapshot = SnapshotRegionFiles(chunks.Select(chunk => (chunk.X + 1, chunk.Z)));
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = await Task.Run(() => ModRegionFile.CopyChunks(root, chunks, 1, 0));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Copied", count));
            SetUndoSnapshot(snapshot);
            _loading = false;
            await LoadWorldAsync(worldPath, dimension, CurrentLayerRange);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "复制区块失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.CopyFailed", ex.Message), HintType.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void BtnExport_Click(object sender, EventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;
        var folder = SystemDialogs.SelectFolder(Lang.Text("Instance.Saves.MapEditor.ExportSelectFolder"), _worldPath);
        if (string.IsNullOrEmpty(folder)) return;

        var root = _dimensionRoot!;
        var chunks = _selected.ToList();
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = await Task.Run(() => ModRegionFile.ExportChunks(root, chunks, folder));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Exported", count));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "导出区块失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.ExportFailed", ex.Message), HintType.Error);
        }
        finally
        {
            _loading = false;
            if (!string.IsNullOrEmpty(_worldPath)) await LoadWorldAsync(_worldPath, _dimension, CurrentLayerRange);
        }
    }

    private async void BtnImport_Click(object sender, EventArgs e)
    {
        if (_worldPath is null || _loading) return;
        var files = SystemDialogs.SelectFiles(Lang.Text("Instance.Saves.MapEditor.ImportFilter"),
            Lang.Text("Instance.Saves.MapEditor.Import"), _worldPath, false);
        if (files.Length == 0) return;

        if (ModMain.MyMsgBox(Lang.Text("Instance.Saves.MapEditor.ImportConfirmMessage"),
                Lang.Text("Instance.Saves.MapEditor.ImportConfirmTitle"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        var root = _dimensionRoot!;
        var worldPath = _worldPath;
        var dimension = _dimension;
        var snapshot = SnapshotRegionFiles(files
            .Select(ParseRegionCoordinates)
            .Where(coordinates => coordinates is not null)
            .Select(coordinates => (coordinates!.Value.X * ModRegionFile.RegionChunks,
                coordinates.Value.Z * ModRegionFile.RegionChunks)));
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = 0;
            foreach (var file in files)
                count += await Task.Run(() => ModRegionFile.ImportChunks(root, file));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Imported", count));
            SetUndoSnapshot(snapshot);
            _loading = false;
            await LoadWorldAsync(worldPath, dimension, CurrentLayerRange);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "导入区块失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.ImportFailed", ex.Message), HintType.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void BtnDelete_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;

        var count = _selected.Count;
        var result = ModMain.MyMsgBox(
            Lang.Text("Instance.Saves.MapEditor.DeleteConfirmMessage", count),
            Lang.Text("Instance.Saves.MapEditor.DeleteConfirmTitle"),
            Lang.Text("Instance.Saves.MapEditor.DeleteButton"),
            Lang.Text("Common.Action.Cancel"), isWarn: true);
        if (result != 1) return;

        var root = _dimensionRoot!;
        var worldPath = _worldPath;
        var dimension = _dimension;
        var chunks = _selected.ToList();
        var snapshot = SnapshotRegionFiles(chunks, includeRelated: true);
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Deleting", count);

        try
        {
            await Task.Run(() => ModRegionFile.DeleteChunksInWorld(root, chunks));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Deleted", count));
            SetUndoSnapshot(snapshot);
            _loading = false;
            await LoadWorldAsync(worldPath, dimension, CurrentLayerRange);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "删除区块失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.DeleteFailed", ex.Message), HintType.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void BtnUndo_Click(object sender, EventArgs e)
    {
        if (_undoSnapshot is null || _worldPath is null || _loading) return;
        var snapshot = _undoSnapshot;
        var worldPath = _worldPath;
        var dimension = _dimension;
        _loading = true;
        BtnUndo.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Undoing");

        try
        {
            await Task.Run(() =>
            {
                foreach (var (path, data) in snapshot)
                {
                    if (data is null)
                    {
                        if (File.Exists(path)) File.Delete(path);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temp = path + ".undo";
                    File.WriteAllBytes(temp, data);
                    File.Move(temp, path, true);
                }
            });
            _undoSnapshot = null;
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Undone"));
            _loading = false;
            await LoadWorldAsync(worldPath, dimension, CurrentLayerRange);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "撤销地图修改失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.UndoFailed", ex.Message), HintType.Error);
            BtnUndo.IsEnabled = true;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>记录修改前的区域文件内容，用于撤销；路径不存在时记为 null，撤销时会删除该文件。</summary>
    private List<(string Path, byte[]? Data)> SnapshotRegionFiles(IEnumerable<(int X, int Z)> chunks,
        bool includeRelated = false)
    {
        var folders = includeRelated ? new[] { "region", "poi", "entities" } : new[] { "region" };
        var snapshot = new List<(string Path, byte[]? Data)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in chunks.GroupBy(chunk =>
                     (RegionX: FloorDiv(chunk.X, ModRegionFile.RegionChunks),
                         RegionZ: FloorDiv(chunk.Z, ModRegionFile.RegionChunks))))
        foreach (var folder in folders)
        {
            var path = Path.Combine(_dimensionRoot!, folder,
                $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            if (!seen.Add(path)) continue;
            snapshot.Add((path, File.Exists(path) ? File.ReadAllBytes(path) : null));
        }

        return snapshot;
    }

    private void SetUndoSnapshot(List<(string Path, byte[]? Data)> snapshot)
    {
        _undoSnapshot = snapshot.Count > 0 ? snapshot : null;
        BtnUndo.IsEnabled = _undoSnapshot is not null;
    }

    private static (int X, int Z)? ParseRegionCoordinates(string path)
    {
        var parts = Path.GetFileNameWithoutExtension(path).Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var z))
            return (x, z);
        return null;
    }
}
