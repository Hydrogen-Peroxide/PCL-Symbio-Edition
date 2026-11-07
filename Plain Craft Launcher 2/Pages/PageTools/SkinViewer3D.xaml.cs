using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PCL;

/// <summary>可拖动旋转的 3D 皮肤预览。</summary>
public partial class SkinViewer3D : UserControl
{
    /// <summary>贴图最近邻放大的倍数，详见 Upscale 的说明。</summary>
    private const int TextureScale = 8;

    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0d, 1d, 0d), -24d);
    private readonly Transform3DGroup _transform = new();
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 30d };
    // 走路动作：四肢各自绕肩 / 胯摆动，角度由同一个正弦相位驱动
    private readonly AxisAngleRotation3D _rightArmSwing = new(new Vector3D(1d, 0d, 0d), 0d);
    private readonly AxisAngleRotation3D _leftArmSwing = new(new Vector3D(1d, 0d, 0d), 0d);
    private readonly AxisAngleRotation3D _rightLegSwing = new(new Vector3D(1d, 0d, 0d), 0d);
    private readonly AxisAngleRotation3D _leftLegSwing = new(new Vector3D(1d, 0d, 0d), 0d);
    private DispatcherTimer? _walkTimer;
    private double _walkPhase;
    private BitmapSource? _skin;
    private Point _lastPoint;
    private bool _dragging;

    public SkinViewer3D()
    {
        InitializeComponent();
        _transform.Children.Add(new RotateTransform3D(_yaw));
        Viewport.Camera = _camera;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => UpdateCamera();
        Loaded += (_, _) => StartWalking();
        Unloaded += (_, _) => StopWalking();
    }

    /// <summary>设置要预览的皮肤，传 Nothing 或无效文件时清空预览。</summary>
    public void SetSkin(string? path)
    {
        try
        {
            BitmapSource? skin = null;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                if (image.PixelWidth == 64 && image.PixelHeight is 64 or 32)
                {
                    var formatted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0d);
                    formatted.Freeze();
                    skin = formatted;
                }
            }

            // 整张贴图先放大一次，后续所有裁切都基于放大后的图，等价于逐面放大但只需计算一次
            _skin = skin is null ? null : Upscale(skin);
        }
        catch (Exception ex)
        {
            _skin = null;
            ModBase.Log(ex, $"[SkinViewer3D] 载入皮肤失败（{path}）", ModBase.LogLevel.Developer);
        }

        RefreshModel();
    }

    private void RefreshModel()
    {
        Viewport.Children.Clear();
        if (_skin is not null)
            Viewport.Children.Add(new ModelVisual3D { Content = BuildModel(_skin) });
        UpdateCamera();
    }

    /// <summary>是否为纤细模型（手臂宽 3 像素，对应 Alex）；否则为经典模型（手臂宽 4 像素，对应 Steve）。</summary>
    public bool SlimModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            RefreshModel();
        }
    }

    private Model3DGroup BuildModel(BitmapSource skin)
    {
        var group = new Model3DGroup();
        var hasLeft = skin.PixelHeight >= 64;
        // 纯白环境光配合 DiffuseMaterial，等价于直接显示贴图颜色
        group.Children.Add(new AmbientLight(Colors.White));

        // 手臂，经典 4px / 纤细 3px。内侧面压进身体 0.05px，既不与身体侧面共面闪烁，
        // 也不会因为身体侧面贴图透明而露出空隙
        var armWidth = SlimModel ? 3d : 4d;
        // 纤细手臂的贴图只有 3 列，按 4 列裁切会把旁边一列（手臂侧面的描边）拉伸到手臂内侧，
        // 渲染出来就是手臂与躯干之间那条突兀的竖向色带，所以贴图宽度要跟着手臂宽度走
        var armTexture = SlimModel ? 3 : 4;
        var armOffset = 4d + armWidth / 2d - 0.05d;

        // 外层（帽子、外套、袖子、裤腿），厚度固定 0.5 像素；旧版皮肤中超出图片范围的面会自动跳过。
        // 相邻外层箱体互相重叠，若按同一厚度膨胀，它们的表面会完全共面而出现深度冲突（闪烁、纹理错乱），
        // 因此逐层多加一点点厚度让表面错开；最大只差 0.05 像素，肉眼看不出来
        const double layer = 0.5d;

        // 身体、头及其外层不走路，放在同一个静态组里跟随整体旋转
        var staticPart = new Model3DGroup { Transform = _transform };
        AddBox(staticPart, 0d, 12d, 0d, 8d, 12d, 4d, Faces(skin, 20, 20, 8, 12, 4),
            hide: Hide.Top | Hide.Bottom);
        AddBox(staticPart, 0d, 24d, 0d, 8d, 8d, 8d, Faces(skin, 8, 8, 8, 8, 8));
        AddBox(staticPart, 0d, 24d, 0d, 8d, 8d, 8d, Faces(skin, 40, 8, 8, 8, 8), layer);
        if (hasLeft) AddBox(staticPart, 0d, 12d, 0d, 8d, 12d, 4d, Faces(skin, 20, 36, 8, 12, 4), layer + 0.01d);
        group.Children.Add(staticPart);

        // 四肢各自成一个组，绕肩 / 胯摆动；外层（袖子、裤腿）与本体在同一个组里，保证一起动。
        // 两条腿彼此内收 0.02 像素：静止时内侧互相重叠，避免共面闪烁；走路错开后内侧仍封闭，
        // 不会出现「腿内侧是空的」。原来隐藏内侧面的做法在迈步时会把腿里面漏出来
        var rightLeg = CreateLimbModel(_rightLegSwing, -1.98d, 12d);
        AddBox(rightLeg, -1.98d, 0d, 0d, 4d, 12d, 4d, Faces(skin, 4, 20, 4, 12, 4), hide: Hide.Top);
        if (hasLeft)
            AddBox(rightLeg, -1.98d, 0d, 0d, 4d, 12d, 4d, Faces(skin, 4, 36, 4, 12, 4), layer + 0.04d);
        group.Children.Add(rightLeg);

        var leftLeg = CreateLimbModel(_leftLegSwing, 1.98d, 12d);
        AddBox(leftLeg, 1.98d, 0d, 0d, 4d, 12d, 4d,
            hasLeft ? Faces(skin, 20, 52, 4, 12, 4) : Faces(skin, 4, 20, 4, 12, 4, true),
            hide: Hide.Top);
        if (hasLeft)
            AddBox(leftLeg, 1.98d, 0d, 0d, 4d, 12d, 4d, Faces(skin, 4, 52, 4, 12, 4), layer + 0.05d);
        group.Children.Add(leftLeg);

        var rightArm = CreateLimbModel(_rightArmSwing, -armOffset, 24d);
        AddBox(rightArm, -armOffset, 12d, 0d, armWidth, 12d, 4d, Faces(skin, 44, 20, armTexture, 12, 4));
        if (hasLeft)
            AddBox(rightArm, -armOffset, 12d, 0d, armWidth, 12d, 4d, Faces(skin, 44, 36, armTexture, 12, 4),
                layer + 0.02d);
        group.Children.Add(rightArm);

        var leftArm = CreateLimbModel(_leftArmSwing, armOffset, 24d);
        AddBox(leftArm, armOffset, 12d, 0d, armWidth, 12d, 4d,
            hasLeft ? Faces(skin, 36, 52, armTexture, 12, 4) : Faces(skin, 44, 20, armTexture, 12, 4, true));
        if (hasLeft)
            AddBox(leftArm, armOffset, 12d, 0d, armWidth, 12d, 4d, Faces(skin, 52, 52, armTexture, 12, 4),
                layer + 0.03d);
        group.Children.Add(leftArm);

        return group;
    }

    /// <summary>创建一个四肢组：先绕肩 / 胯小幅摆动，再应用整体的左右旋转。</summary>
    private Model3DGroup CreateLimbModel(AxisAngleRotation3D swing, double pivotX, double pivotY)
    {
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(swing, new Point3D(pivotX, pivotY, 0d)));
        transform.Children.Add(_transform);
        return new Model3DGroup { Transform = transform };
    }

    /// <summary>按 16 倍标准皮肤布局，从正面贴图坐标推导出六个面的贴图。</summary>
    private static FaceSet Faces(BitmapSource skin, int frontX, int frontY, int width, int height, int depth,
        bool flipU = false)
    {
        return new FaceSet(
            Crop(skin, frontX, frontY, width, height),
            Crop(skin, frontX + width + depth, frontY, width, height),
            Crop(skin, frontX + width, frontY, depth, height),
            Crop(skin, frontX - depth, frontY, depth, height),
            Crop(skin, frontX, frontY - depth, width, depth),
            Crop(skin, frontX + width, frontY - depth, width, depth),
            flipU);
    }

    private static BitmapSource? Crop(BitmapSource skin, int x, int y, int width, int height)
    {
        // 传入的是 16 倍皮肤布局的像素坐标，贴图已按 TextureScale 放大
        x *= TextureScale;
        y *= TextureScale;
        width *= TextureScale;
        height *= TextureScale;
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > skin.PixelWidth || y + height > skin.PixelHeight)
            return null;
        var cropped = new CroppedBitmap(skin, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return HasVisiblePixel(cropped) ? cropped : null;
    }

    private static bool HasVisiblePixel(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0)
                return true;
        return false;
    }

    private void AddBox(Model3DGroup group, double centerX, double bottom, double centerZ,
        double width, double height, double depth, FaceSet faces, double inflate = 0d, Hide hide = Hide.None)
    {
        width += inflate * 2d;
        height += inflate * 2d;
        depth += inflate * 2d;
        bottom -= inflate;

        var x0 = centerX - width / 2d;
        var x1 = centerX + width / 2d;
        var y0 = bottom;
        var y1 = bottom + height;
        var z0 = centerZ - depth / 2d;
        var z1 = centerZ + depth / 2d;

        AddFace(group, faces.Front, faces.FlipU,
            new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1));
        AddFace(group, faces.Back, faces.FlipU,
            new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x1, y0, z0));
        if (!hide.HasFlag(Hide.PosX))
            AddFace(group, faces.Left, faces.FlipU,
                new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1));
        if (!hide.HasFlag(Hide.NegX))
            AddFace(group, faces.Right, faces.FlipU,
                new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1), new Point3D(x0, y0, z0));
        if (!hide.HasFlag(Hide.Top))
            AddFace(group, faces.Top, faces.FlipU,
                new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1));
        if (!hide.HasFlag(Hide.Bottom))
            AddFace(group, faces.Bottom, faces.FlipU,
                new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0));
    }

    [Flags]
    private enum Hide
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        PosX = 4,
        NegX = 8
    }

    private void AddFace(Model3DGroup group, BitmapSource? texture, bool flipU,
        Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        if (texture is null) return;
        Point[] coordinates = flipU
            ? [new Point(1d, 0d), new Point(0d, 0d), new Point(0d, 1d), new Point(1d, 1d)]
            : [new Point(0d, 0d), new Point(1d, 0d), new Point(1d, 1d), new Point(0d, 1d)];
        var brush = new ImageBrush(texture)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0d, 0d, 1d, 1d)
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        var material = new DiffuseMaterial(brush);
        group.Children.Add(new GeometryModel3D
        {
            Geometry = new MeshGeometry3D
            {
                Positions = new Point3DCollection { p0, p1, p2, p3 },
                TextureCoordinates = new PointCollection(coordinates),
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
            },
            Material = material,
            BackMaterial = material
        });
    }

    /// <summary>
    /// 以最近邻方式放大贴图。WPF 3D 的纹理采样固定使用线性过滤，直接贴原始低分辨率皮肤会明显糊化，
    /// 先放大像素块可以让线性过滤只作用在 1/TextureScale 像素宽度内，从而保持像素风细节。
    /// </summary>
    private static BitmapSource Upscale(BitmapSource source)
    {
        const int scale = TextureScale;
        var sourceWidth = source.PixelWidth;
        var sourceHeight = source.PixelHeight;
        var targetWidth = sourceWidth * scale;
        var targetHeight = sourceHeight * scale;
        var sourceStride = sourceWidth * 4;
        var targetStride = targetWidth * 4;
        var sourcePixels = new byte[sourceStride * sourceHeight];
        source.CopyPixels(sourcePixels, sourceStride, 0);
        var targetPixels = new byte[targetStride * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceRow = y / scale * sourceStride;
            var targetRow = y * targetStride;
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceIndex = sourceRow + x / scale * 4;
                var targetIndex = targetRow + x * 4;
                targetPixels[targetIndex] = sourcePixels[sourceIndex];
                targetPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                targetPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                targetPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
            }
        }

        var result = BitmapSource.Create(targetWidth, targetHeight, 96d, 96d, PixelFormats.Bgra32, null,
            targetPixels, targetStride);
        result.Freeze();
        return result;
    }

    private void UpdateCamera()
    {
        if (ActualWidth <= 0d || ActualHeight <= 0d) return;
        // 模型连同外层的高度约 32.5，上下各留一点余量；镜头略微俯视，若余量太小球顶会贴到框上沿
        const double targetY = 16.25d;
        const double fitHeight = 38d;
        const double fitWidth = 21d;
        // WPF 的 FieldOfView 是水平视角
        var horizontalHalf = Math.Tan(_camera.FieldOfView * Math.PI / 360d);
        var verticalHalf = horizontalHalf * ActualHeight / ActualWidth;
        var distance = Math.Max(fitHeight / 2d / verticalHalf, fitWidth / 2d / horizontalHalf);
        var tilt = distance * 0.08d;
        _camera.Position = new Point3D(0d, targetY + tilt, distance);
        _camera.LookDirection = new Vector3D(0d, -tilt, -distance);
        _camera.UpDirection = new Vector3D(0d, 1d, 0d);
        _camera.NearPlaneDistance = 0.5d;
        _camera.FarPlaneDistance = distance * 4d;
    }

    /// <summary>开始走路动作；页面隐藏后停止，避免在不可见时持续重绘。</summary>
    private void StartWalking()
    {
        if (_walkTimer is not null) return;
        _walkTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16d) };
        _walkTimer.Tick += WalkTimer_Tick;
        _walkTimer.Start();
    }

    private void StopWalking()
    {
        if (_walkTimer is null) return;
        _walkTimer.Stop();
        _walkTimer.Tick -= WalkTimer_Tick;
        _walkTimer = null;
    }

    /// <summary>正弦摆动：同侧手脚反向，左右腿相反，周期约 1.5 秒。</summary>
    private void WalkTimer_Tick(object? sender, EventArgs e)
    {
        _walkPhase += 0.065d;
        if (_walkPhase > Math.PI * 2d) _walkPhase -= Math.PI * 2d;
        var swing = Math.Sin(_walkPhase) * 24d;
        _rightLegSwing.Angle = swing;
        _leftLegSwing.Angle = -swing;
        _rightArmSwing.Angle = -swing * 0.85d;
        _leftArmSwing.Angle = swing * 0.85d;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        _lastPoint = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var point = e.GetPosition(this);
        _yaw.Angle += (point.X - _lastPoint.X) * 0.6d;
        _lastPoint = point;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    private sealed record FaceSet(BitmapSource? Front, BitmapSource? Back, BitmapSource? Left,
        BitmapSource? Right, BitmapSource? Top, BitmapSource? Bottom, bool FlipU);
}
