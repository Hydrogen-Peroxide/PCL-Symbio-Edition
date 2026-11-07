using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PCL;

/// <summary>
///     可拖动旋转、滚轮缩放的原理图 3D 预览。
///     完整方块合并成单个网格并剔除被邻居挡住的内部面；
///     楼梯、台阶、栅栏、门等非完整方块按方块状态还原形状。
/// </summary>
public sealed class SchematicViewer3D : UserControl
{
    public const int MaxRenderBlocks = 120_000;

    private const double MinZoom = 0.002d;

    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1d, 0d, 0d), 18d);
    private readonly DispatcherTimer _spinTimer;
    private readonly Viewport3D _viewport = new();
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 35d };
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0d, 1d, 0d), -35d);

    private readonly ModSchematicTextures _textures;
    private readonly Dictionary<long, Voxel> _voxels;

    private GeometryModel3D? _geometryModel;
    private int? _visibleLayer;
    private bool _rebuildPending;
    private bool _dragging;
    private Point _lastPoint;
    private double _radius = 1d;
    private double _halfHeight = 1d;
    private double _halfHorizontal = 1d;
    private double _zoom = 1d;

    public SchematicViewer3D(ModSchematicPreview.Data data, ModSchematicTextures textures)
    {
        _textures = textures;
        _voxels = new Dictionary<long, Voxel>(data.Blocks.Count);
        _viewport.Camera = _camera;
        _viewport.ClipToBounds = true;
        // 透明背景让空白区域也能接收拖动事件
        Background = Brushes.Transparent;
        Content = _viewport;
        Cursor = Cursors.Hand;

        BuildScene(data);

        // 自动缓慢旋转，用户开始拖动后停止，避免与手动操作抢夺视角
        _spinTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16d) };
        _spinTimer.Tick += (_, _) => _yaw.Angle -= 0.12d;

        SizeChanged += (_, _) => UpdateCamera();
        Loaded += (_, _) => _spinTimer.Start();
        Unloaded += (_, _) => _spinTimer.Stop();
        UpdateCamera();
    }

    /// <summary>原理图最低层（Y 坐标）。</summary>
    public int MinLayer { get; private set; }

    /// <summary>原理图最高层（Y 坐标）。</summary>
    public int MaxLayer { get; private set; }

    /// <summary>当前显示的最高层（Y 坐标）上限；小于该值时从下往上逐层剖开，为 null 时显示全部方块。</summary>
    public int? VisibleTopLayer
    {
        get => _visibleLayer;
        set
        {
            if (_visibleLayer == value) return;
            _visibleLayer = value;
            ScheduleRebuild();
        }
    }

    /// <summary>判断该原理图是否适合用 3D 预览；不满足时调用方应显示提示而不是构建模型。</summary>
    public static bool CanRender(ModSchematicPreview.Data? data) =>
        data is { IsEmpty: false } && data.Blocks.Count <= MaxRenderBlocks;

    private void BuildScene(ModSchematicPreview.Data data)
    {
        // 同坐标只保留一个方块：多区域 litematic 叠加时可能出现重复
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var minZ = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var maxZ = int.MinValue;

        foreach (var block in data.Blocks)
        {
            var id = TrimId(block.Name);
            // 优先用客户端 JAR 里的原版模型，模组方块等找不到模型时退回内置近似形状
            var model = _textures.Models.Get(id, block.Properties);
            var full = model?.IsFullCube ?? IsFullBlock(id, block.Properties);
            _voxels[Key(block.X, block.Y, block.Z)] = new Voxel(block.Name, block.Properties, full,
                full && IsOpaque(id), model);
            minX = Math.Min(minX, block.X);
            minY = Math.Min(minY, block.Y);
            minZ = Math.Min(minZ, block.Z);
            maxX = Math.Max(maxX, block.X);
            maxY = Math.Max(maxY, block.Y);
            maxZ = Math.Max(maxZ, block.Z);
        }

        if (_voxels.Count == 0) return;

        MinLayer = minY;
        MaxLayer = maxY;

        var width = maxX - minX + 1d;
        var height = maxY - minY + 1d;
        var length = maxZ - minZ + 1d;
        var centerX = minX + width / 2d;
        var centerY = minY + height / 2d;
        var centerZ = minZ + length / 2d;
        _radius = Math.Max(1d, 0.5d * Math.Sqrt(width * width + height * height + length * length));
        // 取景用：水平方向绕 Y 轴旋转时轮廓半径不变，竖直方向按俯仰角投影，比外接球紧得多
        _halfHeight = height / 2d;
        _halfHorizontal = Math.Max(0.5d, 0.5d * Math.Sqrt(width * width + length * length));

        var root = new Model3DGroup();
        // 纯环境光会让所有面颜色相同、看不出立体感，因此加一盏主光和一盏补光
        root.Children.Add(new AmbientLight(Color.FromRgb(115, 115, 115)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(130, 130, 130), new Vector3D(-0.4d, -0.9d, -0.6d)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(48, 48, 48), new Vector3D(0.5d, -0.2d, 0.8d)));

        // 先平移到原点，再绕原点俯仰、左右旋转，等价于围绕模型中心旋转
        var transform = new Transform3DGroup();
        transform.Children.Add(new TranslateTransform3D(-centerX, -centerY, -centerZ));
        transform.Children.Add(new RotateTransform3D(_pitch));
        transform.Children.Add(new RotateTransform3D(_yaw));

        var scene = new Model3DGroup { Transform = transform };
        var mesh = BuildMesh();
        if (mesh.Positions.Count > 0)
        {
            var brush = new ImageBrush(_textures.Image)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                // 必须用绝对坐标映射：默认的 RelativeToBoundingBox 会按当前网格的 UV 包围盒拉伸整张贴图集，
                // 分层剖切后只用到了部分方块状态、UV 包围盒跟着变化，贴图就会整体错位变形
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0d, 0d, 1d, 1d)
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            _geometryModel = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                // 同时设置背面材质，避免个别面绕序相反时被剔除而露出空洞
                BackMaterial = material
            };
            scene.Children.Add(_geometryModel);
        }

        root.Children.Add(scene);
        _viewport.Children.Add(new ModelVisual3D { Content = root });
    }

    /// <summary>层切换时合并到下一个空闲帧重建，避免拖动滑块时每次一格都卡一下。</summary>
    private void ScheduleRebuild()
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _rebuildPending = false;
            if (_geometryModel is null) return;
            _geometryModel.Geometry = BuildMesh();
        }));
    }

    private MeshGeometry3D BuildMesh()
    {
        var mesh = new MeshBuilder();
        var faceCache = new Dictionary<string, BlockFaces>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _voxels)
        {
            var (x, y, z) = Unkey(pair.Key);
            if (_visibleLayer is { } top && y > top) continue;

            var voxel = pair.Value;
            // 普通整格方块走快速路径：三张贴图 + 隐面剔除
            if (voxel.Model is { UseFastPath: true } cube)
            {
                AddFullBlock(mesh, x, y, z, new BlockFaces(_textures.GetTexture(cube.TopTexture).Uv,
                    _textures.GetTexture(cube.BottomTexture).Uv, _textures.GetTexture(cube.SideTexture).Uv));
                continue;
            }

            // 非完整方块：使用原版模型解析出来的面
            if (voxel.Model is not null)
            {
                AddModelQuads(mesh, voxel.Model, x, y, z);
                continue;
            }

            // 没有对应模型（模组方块、旧版本命名等）：用内置的近似形状兜底
            var id = TrimId(voxel.Name);
            var faces = GetFaces(id, faceCache);
            if (TryAddShaped(mesh, id, voxel.Properties, x, y, z, faces)) continue;
            AddFullBlock(mesh, x, y, z, faces);
        }

        return mesh.ToMeshGeometry3D();
    }

    /// <summary>按模型面列表添加几何；带 cullface 的面在邻居是实心整格时剔除。</summary>
    private void AddModelQuads(MeshBuilder mesh, ModSchematicModelLoader.BlockModel model, int x, int y, int z)
    {
        foreach (var quad in model.Quads)
        {
            if (quad.Cull is { } cull && IsOccluded(x, y, z, cull)) continue;
            var texture = _textures.GetTexture(quad.Texture);
            mesh.AddQuad(
                new Point3D(x + quad.P0.X, y + quad.P0.Y, z + quad.P0.Z),
                new Point3D(x + quad.P1.X, y + quad.P1.Y, z + quad.P1.Z),
                new Point3D(x + quad.P2.X, y + quad.P2.Y, z + quad.P2.Z),
                new Point3D(x + quad.P3.X, y + quad.P3.Y, z + quad.P3.Z),
                quad.Uv[0], quad.Uv[1], quad.Uv[2], quad.Uv[3], texture);
        }
    }

    /// <summary>判断某个方向的邻居是否会把该面完全挡住。</summary>
    private bool IsOccluded(int x, int y, int z, string cullFace)
    {
        var (dx, dy, dz) = cullFace switch
        {
            "north" => (0, 0, -1),
            "south" => (0, 0, 1),
            "west" => (-1, 0, 0),
            "east" => (1, 0, 0),
            "up" => (0, 1, 0),
            _ => (0, -1, 0)
        };
        return IsVisibleOpaqueBlockAt(x + dx, y + dy, z + dz);
    }

    private void AddFullBlock(MeshBuilder mesh, int x, int y, int z, BlockFaces faces)
    {
        var x0 = x;
        var x1 = x + 1d;
        var y0 = y;
        var y1 = y + 1d;
        var z0 = z;
        var z1 = z + 1d;

        // 只在邻居是完整方块时才剔除该面；台阶、楼梯等非完整方块挡不住整面
        // 分层剖开时，被剖掉的邻居不算遮挡，否则顶面会露出空洞
        if (!IsVisibleOpaqueBlockAt(x, y, z + 1))
            mesh.AddQuad(new Point3D(x0, y1, z1), new Point3D(x1, y1, z1),
                new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), faces.Side);
        if (!IsVisibleOpaqueBlockAt(x, y, z - 1))
            mesh.AddQuad(new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
                new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), faces.Side);
        if (!IsVisibleOpaqueBlockAt(x + 1, y, z))
            mesh.AddQuad(new Point3D(x1, y1, z1), new Point3D(x1, y1, z0),
                new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), faces.Side);
        if (!IsVisibleOpaqueBlockAt(x - 1, y, z))
            mesh.AddQuad(new Point3D(x0, y1, z0), new Point3D(x0, y1, z1),
                new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), faces.Side);
        if (!IsVisibleOpaqueBlockAt(x, y + 1, z))
            mesh.AddQuad(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0),
                new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), faces.Top);
        if (!IsVisibleOpaqueBlockAt(x, y - 1, z))
            mesh.AddQuad(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1),
                new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), faces.Bottom);
    }

    /// <summary>按方块名与状态生成非完整方块的形状；返回 false 时按完整方块处理。</summary>
    private bool TryAddShaped(MeshBuilder mesh, string id, string? props, int x, int y, int z, BlockFaces f)
    {
        var x0 = x;
        var x1 = x + 1d;
        var y0 = y;
        var y1 = y + 1d;
        var z0 = z;
        var z1 = z + 1d;
        const double eps = 0.002d;

        // 告示牌、床、箱子由方块实体渲染，原版 JAR 里没有模型，这里按大致比例还原
        if (id.EndsWith("_sign", StringComparison.Ordinal) || id.EndsWith("_hanging_sign", StringComparison.Ordinal))
        {
            var (fx, fz) = FacingVector(Prop(props, "facing"));
            const double t = 0.0625d;
            if (id.Contains("_wall_", StringComparison.Ordinal))
            {
                var by0 = y0 + 0.25d;
                var by1 = y0 + 0.75d;
                if (fx > 0) AddBox(mesh, x1 - t, by0, z0, x1, by1, z1, f);
                else if (fx < 0) AddBox(mesh, x0, by0, z0, x0 + t, by1, z1, f);
                else if (fz > 0) AddBox(mesh, x0, by0, z1 - t, x1, by1, z1, f);
                else AddBox(mesh, x0, by0, z0, x1, by1, z0 + t, f);
                return true;
            }

            // 立式告示牌：中间立柱 + 上半部分的牌子
            AddBox(mesh, x0 + 0.4375d, y0, z0 + 0.4375d, x0 + 0.5625d, y0 + 0.5d, z0 + 0.5625d, f);
            if (fx != 0)
                AddBox(mesh, x0 + 0.4375d, y0 + 0.5d, z0, x0 + 0.5625d, y1, z1, f);
            else
                AddBox(mesh, x0, y0 + 0.5d, z0 + 0.4375d, x1, y1, z0 + 0.5625d, f);
            return true;
        }

        if (id.EndsWith("_bed", StringComparison.Ordinal))
        {
            AddBox(mesh, x0, y0, z0, x1, y0 + 0.5625d, z1, f);
            return true;
        }

        if (id is "chest" or "trapped_chest" or "ender_chest")
        {
            AddBox(mesh, x0 + 0.0625d, y0, z0 + 0.0625d, x1 - 0.0625d, y0 + 0.875d, z1 - 0.0625d, f);
            return true;
        }

        if (id is "chain" or "iron_chain" or "copper_chain" or "exposed_copper_chain" or "weathered_copper_chain"
            or "oxidized_copper_chain")
        {
            AddBox(mesh, x0 + 0.4375d, y0, z0 + 0.4375d, x0 + 0.5625d, y1, z0 + 0.5625d, f);
            return true;
        }

        if (id == "snow")
        {
            var layers = Math.Clamp(ParseInt(Prop(props, "layers"), 1), 1, 8);
            AddBox(mesh, x0, y0, z0, x1, y0 + layers * 0.125d, z1, f);
            return true;
        }

        if (id.EndsWith("_slab", StringComparison.Ordinal))
        {
            var type = Prop(props, "type") ?? Prop(props, "half");
            // 双台阶就是完整方块，交给完整方块分支
            if (string.Equals(type, "double", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(type, "top", StringComparison.OrdinalIgnoreCase))
                AddBox(mesh, x0, y0 + 0.5d, z0, x1, y1, z1, f);
            else
                AddBox(mesh, x0, y0, z0, x1, y0 + 0.5d, z1, f);
            return true;
        }

        if (id.EndsWith("_stairs", StringComparison.Ordinal))
        {
            var top = string.Equals(Prop(props, "half"), "top", StringComparison.OrdinalIgnoreCase);
            var (fx, fz) = FacingVector(Prop(props, "facing"));

            // 台阶部分占据朝向一侧的半格；倒置楼梯的台阶在朝向的反侧
            double sx0 = x0, sx1 = x1, sz0 = z0, sz1 = z1;
            if (fx > 0) sx0 = x0 + 0.5d;
            else if (fx < 0) sx1 = x0 + 0.5d;
            if (fz > 0) sz0 = z0 + 0.5d;
            else if (fz < 0) sz1 = z0 + 0.5d;

            if (!top)
            {
                AddBox(mesh, x0, y0, z0, x1, y0 + 0.5d, z1, f);
                AddBox(mesh, sx0, y0 + 0.5d - eps, sz0, sx1, y1, sz1, f);
            }
            else
            {
                AddBox(mesh, x0, y0 + 0.5d, z0, x1, y1, z1, f);
                double rx0 = x0, rx1 = x1, rz0 = z0, rz1 = z1;
                if (fx > 0) rx1 = x0 + 0.5d;
                else if (fx < 0) rx0 = x0 + 0.5d;
                if (fz > 0) rz1 = z0 + 0.5d;
                else if (fz < 0) rz0 = z0 + 0.5d;
                AddBox(mesh, rx0, y0, rz0, rx1, y0 + 0.5d + eps, rz1, f);
            }

            return true;
        }

        if (id.EndsWith("_fence_gate", StringComparison.Ordinal))
        {
            var (fx, fz) = FacingVector(Prop(props, "facing"));
            var open = string.Equals(Prop(props, "open"), "true", StringComparison.OrdinalIgnoreCase);
            var alongX = fz != 0;
            const double t0 = 0.4375d;
            const double t1 = 0.5625d;
            if (alongX)
            {
                AddBox(mesh, x0, y0, z0 + t0, x0 + 0.125d, y1, z0 + t1, f);
                AddBox(mesh, x1 - 0.125d, y0, z0 + t0, x1, y1, z0 + t1, f);
                if (!open) AddBox(mesh, x0 + 0.125d, y0, z0 + t0, x1 - 0.125d, y1, z0 + t1, f);
            }
            else
            {
                AddBox(mesh, x0 + t0, y0, z0, x0 + t1, y1, z0 + 0.125d, f);
                AddBox(mesh, x0 + t0, y0, z1 - 0.125d, x0 + t1, y1, z1, f);
                if (!open) AddBox(mesh, x0 + t0, y0, z0 + 0.125d, x0 + t1, y1, z1 - 0.125d, f);
            }

            return true;
        }

        if (id.EndsWith("_fence", StringComparison.Ordinal))
        {
            AddBox(mesh, x0 + 0.375d, y0, z0 + 0.375d, x0 + 0.625d, y1, z0 + 0.625d, f);
            AddFenceConnections(mesh, x, y, z, x0, y0, z0, x1, z1, f);
            return true;
        }

        if (id.EndsWith("_wall", StringComparison.Ordinal))
        {
            AddBox(mesh, x0 + 0.25d, y0, z0 + 0.25d, x0 + 0.75d, y1, z0 + 0.75d, f);
            if (HasBlock(x + 1, y, z)) AddBox(mesh, x0 + 0.5d, y0, z0 + 0.25d, x1, y0 + 0.75d, z0 + 0.75d, f);
            if (HasBlock(x - 1, y, z)) AddBox(mesh, x0, y0, z0 + 0.25d, x0 + 0.5d, y0 + 0.75d, z0 + 0.75d, f);
            if (HasBlock(x, y, z + 1)) AddBox(mesh, x0 + 0.25d, y0, z0 + 0.5d, x0 + 0.75d, y0 + 0.75d, z1, f);
            if (HasBlock(x, y, z - 1)) AddBox(mesh, x0 + 0.25d, y0, z0, x0 + 0.75d, y0 + 0.75d, z0 + 0.5d, f);
            return true;
        }

        if (id.EndsWith("_pane", StringComparison.Ordinal) || id == "iron_bars")
        {
            const double c0 = 0.4375d;
            const double c1 = 0.5625d;
            var connectX = HasBlock(x - 1, y, z) || HasBlock(x + 1, y, z);
            var connectZ = HasBlock(x, y, z - 1) || HasBlock(x, y, z + 1);
            if (connectX && connectZ)
            {
                AddBox(mesh, x0, y0, z0 + c0, x1, y1, z0 + c1, f);
                AddBox(mesh, x0 + c0, y0, z0, x0 + c1, y1, z1, f);
            }
            else if (connectX)
            {
                AddBox(mesh, x0, y0, z0 + c0, x1, y1, z0 + c1, f);
            }
            else if (connectZ)
            {
                AddBox(mesh, x0 + c0, y0, z0, x0 + c1, y1, z1, f);
            }
            else
            {
                AddBox(mesh, x0 + c0, y0, z0 + c0, x0 + c1, y1, z0 + c1, f);
            }

            return true;
        }

        if (id.EndsWith("_trapdoor", StringComparison.Ordinal))
        {
            const double t = 0.1875d;
            var open = string.Equals(Prop(props, "open"), "true", StringComparison.OrdinalIgnoreCase);
            var top = string.Equals(Prop(props, "half"), "top", StringComparison.OrdinalIgnoreCase);
            if (!open)
            {
                if (top) AddBox(mesh, x0, y1 - t, z0, x1, y1, z1, f);
                else AddBox(mesh, x0, y0, z0, x1, y0 + t, z1, f);
            }
            else
            {
                var (fx, fz) = FacingVector(Prop(props, "facing"));
                if (fx > 0) AddBox(mesh, x1 - t, y0, z0, x1, y1, z1, f);
                else if (fx < 0) AddBox(mesh, x0, y0, z0, x0 + t, y1, z1, f);
                else if (fz > 0) AddBox(mesh, x0, y0, z1 - t, x1, y1, z1, f);
                else AddBox(mesh, x0, y0, z0, x1, y1, z0 + t, f);
            }

            return true;
        }

        if (id.EndsWith("_door", StringComparison.Ordinal))
        {
            const double t = 0.1875d;
            var open = string.Equals(Prop(props, "open"), "true", StringComparison.OrdinalIgnoreCase);
            var (fx, fz) = FacingVector(Prop(props, "facing"));
            var alongX = fz != 0;
            if (open) alongX = !alongX;
            if (alongX)
                AddBox(mesh, x0, y0, z0 + 0.5d - t / 2d, x1, y1, z0 + 0.5d + t / 2d, f);
            else
                AddBox(mesh, x0 + 0.5d - t / 2d, y0, z0, x0 + 0.5d + t / 2d, y1, z1, f);
            return true;
        }

        if (id.EndsWith("_carpet", StringComparison.Ordinal))
        {
            AddBox(mesh, x0, y0, z0, x1, y0 + 0.0625d, z1, f);
            return true;
        }

        if (id.EndsWith("_pressure_plate", StringComparison.Ordinal) ||
            id.EndsWith("_button", StringComparison.Ordinal))
        {
            const double t = 0.0625d;
            var face = (Prop(props, "face") ?? "floor").ToLowerInvariant();
            var (fx, fz) = FacingVector(Prop(props, "facing"));
            if (face == "ceiling") AddBox(mesh, x0, y1 - t, z0, x1, y1, z1, f);
            else if (face == "wall" && fx > 0) AddBox(mesh, x1 - t, y0, z0, x1, y1, z1, f);
            else if (face == "wall" && fx < 0) AddBox(mesh, x0, y0, z0, x0 + t, y1, z1, f);
            else if (face == "wall" && fz > 0) AddBox(mesh, x0, y0, z1 - t, x1, y1, z1, f);
            else if (face == "wall") AddBox(mesh, x0, y0, z0, x1, y1, z0 + t, f);
            else AddBox(mesh, x0, y0, z0, x1, y0 + t, z1, f);
            return true;
        }

        if (id is "rail" or "powered_rail" or "detector_rail" or "activator_rail")
        {
            AddBox(mesh, x0, y0, z0, x1, y0 + 0.0625d, z1, f);
            return true;
        }

        if (id is "torch" or "wall_torch" or "soul_torch" or "soul_wall_torch" or "redstone_torch"
            or "redstone_wall_torch")
        {
            AddBox(mesh, x0 + 0.4375d, y0, z0 + 0.4375d, x0 + 0.5625d, y0 + 0.625d, z0 + 0.5625d, f);
            return true;
        }

        if (id == "ladder")
        {
            const double t = 0.0625d;
            var (fx, fz) = FacingVector(Prop(props, "facing"));
            if (fx > 0) AddBox(mesh, x1 - t, y0, z0, x1, y1, z1, f);
            else if (fx < 0) AddBox(mesh, x0, y0, z0, x0 + t, y1, z1, f);
            else if (fz > 0) AddBox(mesh, x0, y0, z1 - t, x1, y1, z1, f);
            else AddBox(mesh, x0, y0, z0, x1, y1, z0 + t, f);
            return true;
        }

        // 花盆、铁砧、工作台等不到一整格高的方块：按大致尺寸缩小
        var partial = PartialSize(id);
        if (partial is not null)
        {
            var (width, height) = partial.Value;
            var offset = (1d - width) / 2d;
            AddBox(mesh, x0 + offset, y0, z0 + offset, x1 - offset, y0 + height, z1 - offset, f);
            return true;
        }

        if (id == "lily_pad")
        {
            AddBox(mesh, x0, y0, z0, x1, y0 + 0.0625d, z1, f);
            return true;
        }

        // 花草：用两张交叉的竖直面片代替整格方块
        if (IsPlant(id))
        {
            var xc = x0 + 0.5d;
            var zc = z0 + 0.5d;
            mesh.AddQuad(new Point3D(x0, y1, zc), new Point3D(x1, y1, zc),
                new Point3D(x1, y0, zc), new Point3D(x0, y0, zc), f.Side);
            mesh.AddQuad(new Point3D(xc, y1, z0), new Point3D(xc, y1, z1),
                new Point3D(xc, y0, z1), new Point3D(xc, y0, z0), f.Side);
            return true;
        }

        return false;
    }

    /// <summary>栅栏的两个横杆；只有同一层存在邻居方块时才伸出横杆。</summary>
    private void AddFenceConnections(MeshBuilder mesh, int x, int y, int z, double x0, double y0, double z0,
        double x1, double z1, BlockFaces f)
    {
        const double c0 = 0.4375d;
        const double c1 = 0.5625d;
        const double r0 = 0.375d;
        const double r1 = 0.5625d;
        const double r2 = 0.75d;
        const double r3 = 0.9375d;

        if (HasBlock(x + 1, y, z))
        {
            AddBox(mesh, x0 + 0.5d, y0 + r0, z0 + c0, x1, y0 + r1, z0 + c1, f);
            AddBox(mesh, x0 + 0.5d, y0 + r2, z0 + c0, x1, y0 + r3, z0 + c1, f);
        }

        if (HasBlock(x - 1, y, z))
        {
            AddBox(mesh, x0, y0 + r0, z0 + c0, x0 + 0.5d, y0 + r1, z0 + c1, f);
            AddBox(mesh, x0, y0 + r2, z0 + c0, x0 + 0.5d, y0 + r3, z0 + c1, f);
        }

        if (HasBlock(x, y, z + 1))
        {
            AddBox(mesh, x0 + c0, y0 + r0, z0 + 0.5d, x0 + c1, y0 + r1, z1, f);
            AddBox(mesh, x0 + c0, y0 + r2, z0 + 0.5d, x0 + c1, y0 + r3, z1, f);
        }

        if (HasBlock(x, y, z - 1))
        {
            AddBox(mesh, x0 + c0, y0 + r0, z0, x0 + c1, y0 + r1, z0 + 0.5d, f);
            AddBox(mesh, x0 + c0, y0 + r2, z0, x0 + c1, y0 + r3, z0 + 0.5d, f);
        }
    }

    private bool HasBlock(int x, int y, int z) => _voxels.ContainsKey(Key(x, y, z));

    private bool IsFullBlockAt(int x, int y, int z) =>
        _voxels.TryGetValue(Key(x, y, z), out var voxel) && voxel.Full;

    /// <summary>只有实心的整格方块才挡得住邻居的面；玻璃、树叶、水这类透明方块不算。</summary>
    private bool IsVisibleOpaqueBlockAt(int x, int y, int z) =>
        _voxels.TryGetValue(Key(x, y, z), out var voxel) && voxel.Opaque &&
        (_visibleLayer is not { } top || y <= top);

    /// <summary>常见透明方块，参与渲染但不能用来剔除邻居的面。</summary>
    private static bool IsOpaque(string id)
    {
        if (id.Contains("glass", StringComparison.Ordinal)) return false;
        if (id.EndsWith("leaves", StringComparison.Ordinal)) return false;
        return id switch
        {
            "water" or "lava" or "ice" or "frosted_ice" or "slime_block" or "honey_block" or "barrier"
                or "light" or "structure_void" or "tinted_glass" or "cobweb" or "bamboo_sapling" => false,
            _ => true
        };
    }

    /// <summary>不到一整格高的常见方块，返回（水平宽度，高度）。</summary>
    private static (double Width, double Height)? PartialSize(string id)
    {
        if (id.StartsWith("potted_", StringComparison.Ordinal)) return (0.375d, 0.375d);
        if (id == "cake" || id.EndsWith("_candle_cake", StringComparison.Ordinal)) return (1d, 0.5d);
        if (id == "candle" || id.EndsWith("_candle", StringComparison.Ordinal)) return (0.125d, 0.375d);

        return id switch
        {
            "anvil" or "chipped_anvil" or "damaged_anvil" => (1d, 0.625d),
            "flower_pot" => (0.375d, 0.375d),
            "end_rod" => (0.25d, 1d),
            "sea_pickle" => (0.375d, 0.375d),
            "turtle_egg" or "sniffer_egg" => (0.4375d, 0.4375d),
            "dragon_egg" => (0.875d, 0.875d),
            "enchanting_table" => (1d, 0.75d),
            "ender_chest" or "chest" or "trapped_chest" or "barrel" => (1d, 0.875d),
            "grindstone" => (1d, 0.75d),
            "stonecutter" => (1d, 0.5625d),
            "hopper" => (1d, 0.625d),
            "brewing_stand" => (1d, 0.75d),
            "cauldron" or "water_cauldron" or "lava_cauldron" or "powder_snow_cauldron" => (1d, 0.9375d),
            "composter" => (1d, 0.875d),
            "daylight_detector" => (1d, 0.375d),
            "campfire" or "soul_campfire" => (1d, 0.4375d),
            "lectern" => (1d, 0.875d),
            "lantern" or "soul_lantern" or "copper_lantern" => (0.375d, 0.4375d),
            "chain" => (0.125d, 1d),
            _ => null
        };
    }

    private static readonly HashSet<string> PlantIds = new(StringComparer.Ordinal)
    {
        "short_grass", "grass", "fern", "large_fern", "dead_bush", "short_dry_grass", "tall_dry_grass",
        "sugar_cane", "kelp", "kelp_plant", "seagrass", "tall_seagrass", "sweet_berry_bush", "nether_wart",
        "cocoa", "wheat", "carrots", "potatoes", "beetroots", "torchflower", "torchflower_crop",
        "pitcher_plant", "pitcher_crop", "melon_stem", "pumpkin_stem", "bamboo", "bamboo_sapling",
        "brown_mushroom", "red_mushroom", "crimson_fungus", "warped_fungus", "crimson_roots", "warped_roots",
        "nether_sprouts", "spore_blossom", "big_dripleaf", "big_dripleaf_stem", "small_dripleaf",
        "chorus_flower", "chorus_plant", "pink_petals", "wildflowers", "leaf_litter", "bush", "firefly_bush",
        "dandelion", "poppy", "blue_orchid", "allium", "azure_bluet", "red_tulip", "orange_tulip",
        "white_tulip", "pink_tulip", "oxeye_daisy", "cornflower", "lily_of_the_valley", "wither_rose",
        "sunflower", "lilac", "rose_bush", "peony", "vine", "glow_lichen", "cactus_flower"
    };

    private static bool IsPlant(string id) =>
        PlantIds.Contains(id) || id.EndsWith("_sapling", StringComparison.Ordinal);

    /// <summary>判断方块是否占据完整 1×1×1 空间，用于决定能否剔除相邻面。</summary>
    private static bool IsFullBlock(string id, string? props)
    {
        if (id == "snow") return Math.Clamp(ParseInt(Prop(props, "layers"), 1), 1, 8) >= 8;
        if (id.EndsWith("_slab", StringComparison.Ordinal))
            return string.Equals(Prop(props, "type") ?? Prop(props, "half"), "double",
                StringComparison.OrdinalIgnoreCase);
        if (id == "lily_pad" || IsPlant(id) || PartialSize(id) is not null) return false;

        return !(id.EndsWith("_stairs", StringComparison.Ordinal) ||
                 id.EndsWith("_fence_gate", StringComparison.Ordinal) ||
                 id.EndsWith("_fence", StringComparison.Ordinal) ||
                 id.EndsWith("_wall", StringComparison.Ordinal) ||
                 id.EndsWith("_pane", StringComparison.Ordinal) ||
                 id == "iron_bars" ||
                 id.EndsWith("_trapdoor", StringComparison.Ordinal) ||
                 id.EndsWith("_door", StringComparison.Ordinal) ||
                 id.EndsWith("_carpet", StringComparison.Ordinal) ||
                 id.EndsWith("_pressure_plate", StringComparison.Ordinal) ||
                 id.EndsWith("_button", StringComparison.Ordinal) ||
                 id is "rail" or "powered_rail" or "detector_rail" or "activator_rail" ||
                 id is "torch" or "wall_torch" or "soul_torch" or "soul_wall_torch" or "redstone_torch"
                     or "redstone_wall_torch" ||
                 id == "ladder");
    }

    private BlockFaces GetFaces(string id, Dictionary<string, BlockFaces> cache)
    {
        if (cache.TryGetValue(id, out var cached)) return cached;
        var textures = _textures.GetLegacyFaces(id);
        var faces = new BlockFaces(_textures.GetTexture(textures[0]).Uv, _textures.GetTexture(textures[1]).Uv,
            _textures.GetTexture(textures[2]).Uv);
        cache[id] = faces;
        return faces;
    }

    /// <summary>添加一个长方体；上下面使用顶底贴图，四周使用侧面贴图。</summary>
    private static void AddBox(MeshBuilder mesh, double x0, double y0, double z0, double x1, double y1, double z1,
        BlockFaces f)
    {
        mesh.AddQuad(new Point3D(x0, y1, z1), new Point3D(x1, y1, z1),
            new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), f.Side);
        mesh.AddQuad(new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
            new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), f.Side);
        mesh.AddQuad(new Point3D(x1, y1, z1), new Point3D(x1, y1, z0),
            new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), f.Side);
        mesh.AddQuad(new Point3D(x0, y1, z0), new Point3D(x0, y1, z1),
            new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), f.Side);
        mesh.AddQuad(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0),
            new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), f.Top);
        mesh.AddQuad(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1),
            new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), f.Bottom);
    }

    private static string TrimId(string name)
    {
        var id = name.Trim();
        if (id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) id = id[10..];
        var cut = id.IndexOfAny(['[', '{']);
        if (cut >= 0) id = id[..cut];
        return id.ToLowerInvariant();
    }

    private static string? Prop(string? properties, string key)
    {
        if (string.IsNullOrEmpty(properties)) return null;
        foreach (var part in properties.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            if (string.Equals(part[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return part[(eq + 1)..].Trim();
        }

        return null;
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var result) ? result : fallback;

    private static (int Dx, int Dz) FacingVector(string? facing) => facing?.ToLowerInvariant() switch
    {
        "north" => (0, -1),
        "south" => (0, 1),
        "west" => (-1, 0),
        "east" => (1, 0),
        _ => (0, 1)
    };

    private void UpdateCamera()
    {
        if (ActualWidth <= 0d || ActualHeight <= 0d) return;
        // FieldOfView 是水平视角，纵向视角随控件宽高比变化
        var tanHorizontal = Math.Tan(_camera.FieldOfView * Math.PI / 360d);
        var tanVertical = tanHorizontal * ActualHeight / ActualWidth;
        var pitch = Math.Abs(_pitch.Angle) * Math.PI / 180d;
        // 水平方向绕 Y 轴旋转时模型轮廓半径固定；竖直方向随俯仰角变化
        var verticalHalf = _halfHeight * Math.Cos(pitch) + _halfHorizontal * Math.Sin(pitch);
        var distance = Math.Max(_halfHorizontal / Math.Max(0.05d, tanHorizontal),
            verticalHalf / Math.Max(0.05d, tanVertical));
        distance = Math.Max(1d, distance * 1.08d / _zoom);

        _camera.Position = new Point3D(0d, 0d, distance);
        _camera.LookDirection = new Vector3D(0d, 0d, -distance);
        _camera.UpDirection = new Vector3D(0d, 1d, 0d);
        _camera.NearPlaneDistance = Math.Max(0.01d, distance - _radius * 2d);
        _camera.FarPlaneDistance = distance + _radius * 3d;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _spinTimer.Stop();
        _dragging = true;
        _lastPoint = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var point = e.GetPosition(this);
        _yaw.Angle += (point.X - _lastPoint.X) * 0.5d;
        _pitch.Angle = Math.Clamp(_pitch.Angle + (point.Y - _lastPoint.Y) * 0.4d, -80d, 80d);
        _lastPoint = point;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // 不做上限限制，可以一直放大到方块内部，也可以缩到很远看整体
        _zoom = Math.Max(MinZoom, _zoom * (e.Delta > 0 ? 1.15d : 1d / 1.15d));
        UpdateCamera();
        e.Handled = true;
    }

    /// <summary>21 位一轴的坐标打包，取值范围 ±1048576，足够覆盖原理图的局部坐标。</summary>
    private static long Key(int x, int y, int z) =>
        ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

    private static (int X, int Y, int Z) Unkey(long key)
    {
        var x = (int)((key >> 42) & 0x1FFFFF);
        var y = (int)((key >> 21) & 0x1FFFFF);
        var z = (int)(key & 0x1FFFFF);
        if (x >= 0x100000) x -= 0x200000;
        if (y >= 0x100000) y -= 0x200000;
        if (z >= 0x100000) z -= 0x200000;
        return (x, y, z);
    }

    private readonly record struct Voxel(string Name, string? Properties, bool Full, bool Opaque,
        ModSchematicModelLoader.BlockModel? Model);

    private readonly record struct BlockFaces(Rect Top, Rect Bottom, Rect Side);

    private sealed class MeshBuilder
    {
        private readonly List<int> _indices = [];
        private readonly List<Point3D> _positions = [];
        private readonly List<Point> _textureCoordinates = [];

        public bool IsEmpty => _positions.Count == 0;

        /// <summary>四个顶点按「左上、右上、右下、左下」传入，贴图坐标随之对应。</summary>
        public void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Rect uv)
        {
            var start = _positions.Count;
            _positions.Add(p0);
            _positions.Add(p1);
            _positions.Add(p2);
            _positions.Add(p3);
            _textureCoordinates.Add(new Point(uv.X, uv.Y));
            _textureCoordinates.Add(new Point(uv.Right, uv.Y));
            _textureCoordinates.Add(new Point(uv.Right, uv.Bottom));
            _textureCoordinates.Add(new Point(uv.X, uv.Bottom));
            _indices.Add(start);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
            _indices.Add(start);
            _indices.Add(start + 2);
            _indices.Add(start + 3);
        }

        /// <summary>按模型面添加四边形；<paramref name="uv" /> 是贴图内坐标（按贴图原始像素），映射到贴图集中的对应格子。</summary>
        public void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Point t0, Point t1, Point t2,
            Point t3, ModSchematicTextures.TextureInfo texture)
        {
            var start = _positions.Count;
            _positions.Add(p0);
            _positions.Add(p1);
            _positions.Add(p2);
            _positions.Add(p3);
            _textureCoordinates.Add(ToAtlas(t0));
            _textureCoordinates.Add(ToAtlas(t1));
            _textureCoordinates.Add(ToAtlas(t2));
            _textureCoordinates.Add(ToAtlas(t3));
            _indices.Add(start);
            _indices.Add(start + 1);
            _indices.Add(start + 2);
            _indices.Add(start);
            _indices.Add(start + 2);
            _indices.Add(start + 3);
            return;

            Point ToAtlas(Point uv) => new(
                texture.Uv.X + uv.X / Math.Max(1, texture.Size.Width) * texture.Uv.Width,
                texture.Uv.Y + uv.Y / Math.Max(1, texture.Size.Height) * texture.Uv.Height);
        }

        public MeshGeometry3D ToMeshGeometry3D()
        {
            var positions = new Point3DCollection(_positions);
            var coordinates = new PointCollection(_textureCoordinates);
            var indices = new Int32Collection(_indices);
            positions.Freeze();
            coordinates.Freeze();
            indices.Freeze();
            return new MeshGeometry3D
            {
                Positions = positions,
                TextureCoordinates = coordinates,
                TriangleIndices = indices
            };
        }
    }
}
