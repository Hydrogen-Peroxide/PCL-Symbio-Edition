using System.Drawing;
using System.Drawing.Drawing2D;

namespace PCL;

/// <summary>
///     按皮肤纹理生成方形头像：正脸居中并留出边距，再叠加帽子层。
///     百宝箱「生成头像」与皮肤库的缩略图共用这一套绘制逻辑。
/// </summary>
public static class ModSkinAvatar
{
    public static Bitmap Generate(Bitmap skin, int size)
    {
        var scale = Math.Max(1, skin.Width / 64);
        var head = new Bitmap(size, size);

        using (var g = Graphics.FromImage(head))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 正脸：脸放大到 7/8，四周各留 1/16 的边距
            var faceSize = size - size / 8;
            using var face = new Bitmap(faceSize, faceSize);
            using (var gFace = Graphics.FromImage(face))
            {
                gFace.InterpolationMode = InterpolationMode.NearestNeighbor;
                gFace.PixelOffsetMode = PixelOffsetMode.Half;
                gFace.DrawImage(skin, new Rectangle(0, 0, faceSize, faceSize),
                    new Rectangle(8 * scale, 8 * scale, 8 * scale, 8 * scale), GraphicsUnit.Pixel);
            }

            var offset = size / 16;
            g.DrawImage(face, offset, offset, faceSize, faceSize);
        }

        if (skin.Width < 64) return head;

        // 帽子层：按整张头像大小绘制，只把不透明的像素盖到正脸上
        using var hair = new Bitmap(size, size);
        using (var gHair = Graphics.FromImage(hair))
        {
            gHair.InterpolationMode = InterpolationMode.NearestNeighbor;
            gHair.PixelOffsetMode = PixelOffsetMode.Half;
            gHair.DrawImage(skin, new Rectangle(0, 0, size, size),
                new Rectangle(40 * scale, 8 * scale, 8 * scale, 8 * scale), GraphicsUnit.Pixel);
        }

        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            var pixel = hair.GetPixel(x, y);
            if (pixel.A > 0) head.SetPixel(x, y, pixel);
        }

        return head;
    }
}
