using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCL;

/// <summary>
///     从实例客户端 JAR 提取方块贴图，并按「贴图名」打包成一张贴图集。
///     贴图名与方块模型里引用的贴图一致（如 block/oak_planks、entity/chest/normal），
///     这样解析出来的模型几何可以精确地取到原版贴图；找不到贴图时退回纯色块。
/// </summary>
public sealed class ModSchematicTextures : IDisposable
{
    /// <summary>贴图集内单格的边长（原版 16×16 放大后）。</summary>
    public const int TileSize = 64;

    /// <summary>原版中需要按生物群系着色的贴图，这里使用默认群系色。</summary>
    private static readonly string[] IconSuffixPriority = ["_side", "_front", "_top", ""];

    private readonly ModSchematicAssets _assets;
    private readonly Dictionary<string, string> _blockIcons;
    private readonly int _columns;
    private readonly Dictionary<string, string[]> _legacyFaces;
    private readonly Rect[] _rects;
    private readonly Dictionary<string, (int Width, int Height)> _sizes;
    private readonly Dictionary<string, int> _tiles;

    private ModSchematicTextures(ModSchematicAssets assets, ModSchematicModelLoader models, BitmapSource image,
        Dictionary<string, int> tiles, Dictionary<string, (int Width, int Height)> sizes, Rect[] rects,
        Dictionary<string, string> blockIcons, Dictionary<string, string[]> legacyFaces, int columns)
    {
        _assets = assets;
        Models = models;
        Image = image;
        _tiles = tiles;
        _sizes = sizes;
        _rects = rects;
        _blockIcons = blockIcons;
        _legacyFaces = legacyFaces;
        _columns = columns;
    }

    /// <summary>打包好的贴图集位图。</summary>
    public BitmapSource Image { get; }

    /// <summary>方块模型解析器（与贴图集共用同一份 JAR 资源）。</summary>
    public ModSchematicModelLoader Models { get; }

    public void Dispose() => _assets.Dispose();

    /// <summary>取某张贴图的 UV 范围与原始像素尺寸；贴图不存在时返回 0 号灰色块。</summary>
    public TextureInfo GetTexture(string texture)
    {
        if (_tiles.TryGetValue(texture, out var index))
            return new TextureInfo(_rects[index], _sizes.TryGetValue(texture, out var size) ? size : (16, 16));
        return new TextureInfo(_rects[0], 16, 16);
    }

    /// <summary>取方块列表缩略图，优先使用侧面贴图。</summary>
    public BitmapSource GetBlockIcon(string blockName)
    {
        var key = Normalize(blockName);
        if (!_blockIcons.TryGetValue(key, out var texture) || !_tiles.TryGetValue(texture, out var tile))
            tile = 0;
        return GetTileImage(tile);
    }

    /// <summary>退回内置近似形状时使用的顶 / 底 / 侧三张贴图。</summary>
    public string[] GetLegacyFaces(string blockName)
    {
        var key = Normalize(blockName);
        if (_legacyFaces.TryGetValue(key, out var faces)) return faces;
        return ["", "", ""];
    }

    /// <summary>裁出单张贴图的位图，供列表缩略图直接使用。</summary>
    public BitmapSource GetTileImage(int tile)
    {
        tile = Math.Clamp(tile, 0, _rects.Length - 1);
        var column = tile % _columns;
        var row = tile / _columns;
        var cropped = new CroppedBitmap(Image, new Int32Rect(column * TileSize, row * TileSize, TileSize, TileSize));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>构建贴图集；可在后台线程调用，返回的位图已冻结。</summary>
    public static ModSchematicTextures Build(IEnumerable<string> blockNames, string? jarPath)
    {
        var assets = new ModSchematicAssets(jarPath);
        var models = new ModSchematicModelLoader(assets);

        var blockIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var legacyFaces = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var tilePixels = new List<byte[]>();
        var sizes = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        var tiles = new Dictionary<string, int>(StringComparer.Ordinal);

        // 0 号贴图留给无法识别的方块
        tilePixels.Add(ColorTile(128, 128, 128));

        int AddTile(string texture)
        {
            if (string.IsNullOrEmpty(texture)) return 0;
            if (tiles.TryGetValue(texture, out var existing)) return existing;

            var index = tilePixels.Count;
            var decoded = assets.TryGetTextureEntry(texture, out var entry)
                ? DecodeTile(entry, TintFor(texture))
                : null;
            tilePixels.Add(decoded?.Pixels ?? ColorTile(128, 128, 128));
            sizes[texture] = decoded is { } value ? (value.Width, value.Height) : (16, 16);
            tiles[texture] = index;
            return index;
        }

        foreach (var rawName in blockNames)
        {
            var name = Normalize(rawName);
            if (name.Length == 0 || blockIcons.ContainsKey(name)) continue;

            var textures = models.CollectTextureNames(name);
            if (textures.Count > 0)
            {
                foreach (var texture in textures) AddTile(texture);
                blockIcons[name] = PickIcon(textures);
                continue;
            }

            // 没有原版模型（模组方块、旧版本命名等）：退回按候选贴图名找图
            var candidates = Candidates(name);
            var side = Find(assets, candidates, ["_side", "_front", ""]);
            var top = Find(assets, candidates, ["_top", ""]) ?? side;
            var bottom = Find(assets, candidates, ["_bottom", ""]) ?? side;
            if (side is null && top is null && bottom is null)
            {
                // 连候选贴图都没有（方块实体渲染的告示牌、床、箱子等）：按方块平均色生成纯色块
                var key = "color/" + name;
                if (!tiles.ContainsKey(key))
                {
                    var color = ModRegionFile.GetBlockColor(name, 0, false);
                    tilePixels.Add(ColorTile(color.R, color.G, color.B));
                    sizes[key] = (16, 16);
                    tiles[key] = tilePixels.Count - 1;
                }

                side = top = bottom = key;
            }

            legacyFaces[name] = [top ?? "", bottom ?? "", side ?? ""];
            if (side is not null) AddTile(side);
            if (top is not null) AddTile(top);
            if (bottom is not null) AddTile(bottom);
            blockIcons[name] = side ?? top ?? "";
        }

        var count = tilePixels.Count;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        var width = columns * TileSize;
        var height = rows * TileSize;
        var atlas = new byte[width * height * 4];

        var rects = new Rect[count];
        for (var index = 0; index < count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            for (var y = 0; y < TileSize; y++)
            {
                var sourceOffset = y * TileSize * 4;
                var targetOffset = ((row * TileSize + y) * width + column * TileSize) * 4;
                Buffer.BlockCopy(tilePixels[index], sourceOffset, atlas, targetOffset, TileSize * 4);
            }

            // 内缩半个像素：采样恰好落在贴图边界时不会取到相邻贴图的像素
            rects[index] = new Rect(
                ((double)column * TileSize + 0.5d) / width, ((double)row * TileSize + 0.5d) / height,
                (TileSize - 1d) / width, (TileSize - 1d) / height);
        }

        var bitmap = BitmapSource.Create(width, height, 96d, 96d, PixelFormats.Bgra32, null, atlas, width * 4);
        bitmap.Freeze();
        return new ModSchematicTextures(assets, models, bitmap, tiles, sizes, rects, blockIcons, legacyFaces, columns);
    }

    private static string PickIcon(IReadOnlyList<string> textures)
    {
        foreach (var suffix in IconSuffixPriority)
        foreach (var texture in textures)
        {
            if (!texture.EndsWith(suffix, StringComparison.Ordinal)) continue;
            if (suffix.Length == 0 && texture.EndsWith("_top", StringComparison.Ordinal)) continue;
            return texture;
        }

        return textures[0];
    }

    /// <summary>方块实体（没有方块模型）常用的替代贴图：床用羊毛、箱子用木板、告示牌用对应木材。</summary>
    private static string? SpecialTexture(string name)
    {
        if (name.EndsWith("_bed", StringComparison.Ordinal))
            return "block/" + name[..^4] + "_wool";
        if (name.EndsWith("_wall_banner", StringComparison.Ordinal))
            return "block/" + name[..^12] + "_wool";
        if (name.EndsWith("_banner", StringComparison.Ordinal))
            return "block/" + name[..^7] + "_wool";
        if (name is "chest" or "trapped_chest")
            return "block/oak_planks";
        if (name == "ender_chest")
            return "block/obsidian";
        if (name.EndsWith("_wall_hanging_sign", StringComparison.Ordinal))
            return "block/" + name[..^18] + "_planks";
        if (name.EndsWith("_hanging_sign", StringComparison.Ordinal))
            return "block/" + name[..^13] + "_planks";
        if (name.EndsWith("_wall_sign", StringComparison.Ordinal))
            return "block/" + name[..^10] + "_planks";
        if (name.EndsWith("_sign", StringComparison.Ordinal))
            return "block/" + name[..^5] + "_planks";
        if (name.Contains("chain", StringComparison.Ordinal))
            return "block/iron_chain";
        return null;
    }

    /// <summary>将方块名归一化：去掉命名空间、方块状态与空白。</summary>
    private static string Normalize(string name)
    {
        var value = name.Trim();
        if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) value = value[10..];
        var cut = value.IndexOfAny(['[', '{']);
        if (cut >= 0) value = value[..cut];
        return value.ToLowerInvariant();
    }

    /// <summary>生成候选贴图名，楼梯、台阶等会顺带尝试基础材质。</summary>
    private static List<string> Candidates(string name)
    {
        switch (name)
        {
            case "water":
            case "water_cauldron":
                return ["block/water_still", "block/water_flow"];
            case "lava":
            case "lava_cauldron":
                return ["block/lava_still", "block/lava_flow"];
        }

        var list = new List<string>();
        var special = SpecialTexture(name);
        if (special is not null) list.Add(special);
        list.Add("block/" + name);
        foreach (var suffix in ShapeSuffixes)
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var baseName = name[..^suffix.Length];
            Add(baseName);
            Add(baseName + "s");
            if (WoodPlanks.TryGetValue(baseName, out var planks)) Add(planks);
            break;
        }

        return list;

        void Add(string value)
        {
            if (string.IsNullOrEmpty(value) || list.Contains("block/" + value)) return;
            list.Add("block/" + value);
        }
    }

    /// <summary>按后缀优先级在候选贴图名中查找存在的贴图。</summary>
    private static string? Find(ModSchematicAssets assets, List<string> candidates, string[] suffixes)
    {
        foreach (var candidate in candidates)
        foreach (var suffix in suffixes)
        {
            var key = candidate + suffix;
            if (assets.TryGetTextureEntry(key, out _)) return key;
        }

        return null;
    }

    private static Color? TintFor(string texture)
    {
        if (texture.StartsWith("block/grass_block_top") || texture == "block/grass_top")
            return Color.FromRgb(0x91, 0xBD, 0x59);
        if (texture.Contains("leaves") || texture is "block/vine" or "block/lily_pad")
            return Color.FromRgb(0x48, 0xB5, 0x18);
        if (texture.StartsWith("block/water"))
            return Color.FromRgb(0x3F, 0x76, 0xE4);
        return null;
    }

    /// <summary>原版木板贴图名，供楼梯、台阶等复用材质的方块使用。</summary>
    private static readonly Dictionary<string, string> WoodPlanks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oak"] = "oak_planks",
        ["spruce"] = "spruce_planks",
        ["birch"] = "birch_planks",
        ["jungle"] = "jungle_planks",
        ["acacia"] = "acacia_planks",
        ["dark_oak"] = "dark_oak_planks",
        ["mangrove"] = "mangrove_planks",
        ["cherry"] = "cherry_planks",
        ["pale_oak"] = "pale_oak_planks",
        ["bamboo"] = "bamboo_planks",
        ["crimson"] = "crimson_planks",
        ["warped"] = "warped_planks"
    };

    /// <summary>台阶、楼梯等非完整方块的后缀，去掉后复用基础材质。</summary>
    private static readonly string[] ShapeSuffixes =
    [
        "_fence_gate", "_pressure_plate", "_trapdoor", "_stairs", "_slab", "_wall",
        "_fence", "_door", "_button", "_carpet", "_pane"
    ];

    /// <summary>解码贴图；动画贴图只取第一帧，随后缩放到贴图集格大小。</summary>
    private static (byte[] Pixels, int Width, int Height)? DecodeTile(ZipArchiveEntry entry, Color? tint)
    {
        try
        {
            using var buffer = new MemoryStream();
            using (var source = entry.Open())
                source.CopyTo(buffer);
            buffer.Position = 0;

            var decoder = BitmapDecoder.Create(buffer,
                BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0d);
            converted.Freeze();

            var width = Math.Min(converted.PixelWidth, 256);
            var height = Math.Min(converted.PixelHeight, 256);
            // 动画贴图是竖着拼的多帧，只取第一帧
            if (height > width && height % width == 0) height = width;

            var pixels = new byte[width * height * 4];
            converted.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

            if (tint is { } color)
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = (byte)(pixels[i] * color.B / 255);
                    pixels[i + 1] = (byte)(pixels[i + 1] * color.G / 255);
                    pixels[i + 2] = (byte)(pixels[i + 2] * color.R / 255);
                }

            return (Resize(pixels, width, height), width, height);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 解码贴图失败（{entry.FullName}）", ModBase.LogLevel.Developer);
            return null;
        }
    }

    /// <summary>最近邻缩放到贴图集格大小，保持像素风。</summary>
    private static byte[] Resize(byte[] source, int sourceWidth, int sourceHeight)
    {
        var target = new byte[TileSize * TileSize * 4];
        var targetStride = TileSize * 4;
        for (var y = 0; y < TileSize; y++)
        {
            var sourceRow = (int)((long)y * sourceHeight / TileSize) * sourceWidth;
            var targetRow = y * targetStride;
            for (var x = 0; x < TileSize; x++)
            {
                var sourceIndex = (sourceRow + (int)((long)x * sourceWidth / TileSize)) * 4;
                var targetIndex = targetRow + x * 4;
                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
                target[targetIndex + 3] = source[sourceIndex + 3];
            }
        }

        return target;
    }

    private static byte[] ColorTile(byte r, byte g, byte b)
    {
        var pixels = new byte[TileSize * TileSize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    /// <summary>贴图在贴图集里的位置与原始像素尺寸。</summary>
    public readonly record struct TextureInfo(Rect Uv, (int Width, int Height) Size)
    {
        public TextureInfo(Rect uv, int width, int height) : this(uv, (width, height))
        {
        }
    }
}
