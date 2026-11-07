using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PCL;

/// <summary>
///     把皮肤纹理拼成 16×32 像素布局的正面站立全身图。
///     与「生成头像」一样直接从原图逐块绘制、按整数倍最近邻放大，避免二次缩放导致像素不齐。
/// </summary>
public static class ModSkinFigure
{
    /// <summary>生成全身图，scale 为每个皮肤像素放大后的边长。</summary>
    public static Bitmap Generate(Bitmap skin, bool slim, int scale = 5)
    {
        var textureScale = Math.Max(1, skin.Width / 64);
        var armWidth = slim ? 3 : 4;
        var rightArmX = slim ? 1 : 0;

        var figure = new Bitmap(16 * scale, 32 * scale, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(figure);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // 底层：腿、身体、手臂、头
        Draw(g, skin, textureScale, scale, 4, 20, 4, 12, 4, 20); // 右腿
        if (skin.Height >= 64)
            Draw(g, skin, textureScale, scale, 20, 52, 4, 12, 8, 20); // 左腿
        else
            Draw(g, skin, textureScale, scale, 4, 20, 4, 12, 8, 20); // 旧版皮肤镜像腿
        Draw(g, skin, textureScale, scale, 20, 20, 8, 12, 4, 8); // 身体
        Draw(g, skin, textureScale, scale, 44, 20, armWidth, 12, rightArmX, 8); // 右臂
        if (skin.Height >= 64)
            Draw(g, skin, textureScale, scale, 36, 52, armWidth, 12, 12, 8); // 左臂
        else
            Draw(g, skin, textureScale, scale, 44, 20, armWidth, 12, 12, 8); // 旧版皮肤镜像手臂
        Draw(g, skin, textureScale, scale, 8, 8, 8, 8, 4, 0); // 头

        // 叠加层：外套、袖子
        if (skin.Height >= 48)
        {
            Draw(g, skin, textureScale, scale, 4, 36, 4, 12, 4, 20); // 右裤腿
            Draw(g, skin, textureScale, scale, 20, 36, 8, 12, 4, 8); // 外套
            Draw(g, skin, textureScale, scale, 44, 36, armWidth, 12, rightArmX, 8); // 右袖
        }

        if (skin.Height >= 64)
        {
            Draw(g, skin, textureScale, scale, 4, 52, 4, 12, 8, 20); // 左裤腿
            Draw(g, skin, textureScale, scale, 52, 52, armWidth, 12, 12, 8); // 左袖
        }

        Draw(g, skin, textureScale, scale, 40, 8, 8, 8, 4, 0); // 帽子

        return figure;
    }

    private static void Draw(Graphics g, Bitmap skin, int textureScale, int scale,
        int sourceX, int sourceY, int width, int height, int destinationX, int destinationY)
    {
        g.DrawImage(skin,
            new Rectangle(destinationX * scale, destinationY * scale, width * scale, height * scale),
            new Rectangle(sourceX * textureScale, sourceY * textureScale, width * textureScale,
                height * textureScale), GraphicsUnit.Pixel);
    }
}
