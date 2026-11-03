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

    private readonly HashSet<(int X, int Z)> _selected = [];
    private readonly Dictionary<(int RegionX, int RegionZ), bool[]> _present = [];

    private string? _worldPath;
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
    public async Task LoadWorldAsync(string worldPath)
    {
        _worldPath = worldPath;
        _selected.Clear();
        _present.Clear();
        _loading = true;
        BtnDelete.IsEnabled = false;

        PanRegions.Children.Clear();
        PanSelection.Children.Clear();
        PanInput.Children.Clear();
        LabWorld.Text = Lang.Text("Instance.Saves.MapEditor.World", Path.GetFileName(worldPath));
        LabSelection.Text = "";
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var regionDirectory = Path.Combine(worldPath, "region");
            if (!Directory.Exists(regionDirectory))
            {
                LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Empty");
                return;
            }

            var files = Directory.GetFiles(regionDirectory, "*.mca");
            if (files.Length == 0)
            {
                LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Empty");
                return;
            }

            var rendered = new ConcurrentBag<ModRegionFile.RegionRender>();
            var done = 0;
            await Task.Run(() => Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) },
                file =>
                {
                    var render = ModRegionFile.RenderRegion(file);
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
            if (!ReferenceEquals(_worldPath, worldPath)) return;
            if (renders.Count == 0)
            {
                LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.LoadFailed", "无法读取任何区域文件");
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

                _present[(render.RegionX, render.RegionZ)] = render.Present;
            }

            foreach (var canvas in new[] { PanRegions, PanSelection, PanInput })
            {
                canvas.Width = width;
                canvas.Height = height;
            }

            PanMapRoot.Width = width;
            PanMapRoot.Height = height;
            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Status", renders.Count,
                renders.Sum(render => render.ChunkCount));
            ApplyZoom();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "加载地图编辑器失败", ModBase.LogLevel.Feedback);
            LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.LoadFailed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_worldPath, worldPath)) _loading = false;
        }
    }

    /// <summary>关闭当前世界，释放地图渲染结果。</summary>
    public void CloseWorld()
    {
        _worldPath = null;
        _loading = false;
        _selected.Clear();
        _present.Clear();
        _dragStart = null;
        PanRegions.Children.Clear();
        PanSelection.Children.Clear();
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

    private void BtnZoomIn_Click(object sender, MouseButtonEventArgs e)
    {
        _zoom *= 1.25d;
        ApplyZoom();
    }

    private void BtnZoomOut_Click(object sender, MouseButtonEventArgs e)
    {
        _zoom *= 0.8d;
        ApplyZoom();
    }

    private void BtnReload_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_loading && !string.IsNullOrEmpty(_worldPath))
            _ = LoadWorldAsync(_worldPath);
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

    private void PanInput_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;
        var chunk = GetChunkAt(e.GetPosition(PanInput));
        if (chunk is null) return;
        UpdateDragRectangle(chunk.Value);
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
        foreach (var (chunkX, chunkZ) in _selected)
        {
            var rectangle = new Rectangle
            {
                Width = ModRegionFile.ChunkPixels,
                Height = ModRegionFile.ChunkPixels,
                Fill = SelectionFill,
                Stroke = SelectionStroke,
                StrokeThickness = 1
            };
            Canvas.SetLeft(rectangle, (chunkX - _originChunkX) * ModRegionFile.ChunkPixels);
            Canvas.SetTop(rectangle, (chunkZ - _originChunkZ) * ModRegionFile.ChunkPixels);
            PanSelection.Children.Add(rectangle);
        }

        LabSelection.Text = Lang.Text("Instance.Saves.MapEditor.Selection", _selected.Count);
        BtnDelete.IsEnabled = _selected.Count > 0 && !_loading;
    }

    private async void BtnCopy_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;
        var worldPath = _worldPath;
        var chunks = _selected.ToList();
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = await Task.Run(() => ModRegionFile.CopyChunks(worldPath, chunks, 1, 0));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Copied", count));
            _loading = false;
            await LoadWorldAsync(worldPath);
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

    private async void BtnExport_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _selected.Count == 0 || _loading) return;
        var folder = SystemDialogs.SelectFolder(Lang.Text("Instance.Saves.MapEditor.ExportSelectFolder"), _worldPath);
        if (string.IsNullOrEmpty(folder)) return;

        var worldPath = _worldPath;
        var chunks = _selected.ToList();
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = await Task.Run(() => ModRegionFile.ExportChunks(worldPath, chunks, folder));
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
            if (!string.IsNullOrEmpty(_worldPath)) await LoadWorldAsync(_worldPath);
        }
    }

    private async void BtnImport_Click(object sender, MouseButtonEventArgs e)
    {
        if (_worldPath is null || _loading) return;
        var files = SystemDialogs.SelectFiles(Lang.Text("Instance.Saves.MapEditor.ImportFilter"),
            Lang.Text("Instance.Saves.MapEditor.Import"), _worldPath, false);
        if (files.Length == 0) return;

        if (ModMain.MyMsgBox(Lang.Text("Instance.Saves.MapEditor.ImportConfirmMessage"),
                Lang.Text("Instance.Saves.MapEditor.ImportConfirmTitle"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) != 1)
            return;

        var worldPath = _worldPath;
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Loading");

        try
        {
            var count = 0;
            foreach (var file in files)
                count += await Task.Run(() => ModRegionFile.ImportChunks(worldPath, file));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Imported", count));
            _loading = false;
            await LoadWorldAsync(worldPath);
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

        var worldPath = _worldPath;
        var chunks = _selected.ToList();
        _loading = true;
        BtnDelete.IsEnabled = false;
        LabStatus.Text = Lang.Text("Instance.Saves.MapEditor.Deleting", count);

        try
        {
            await Task.Run(() => ModRegionFile.DeleteChunksInWorld(worldPath, chunks));
            HintService.Hint(Lang.Text("Instance.Saves.MapEditor.Deleted", count));
            _loading = false;
            await LoadWorldAsync(worldPath);
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
}
