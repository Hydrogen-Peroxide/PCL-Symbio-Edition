using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using fNbt;

namespace PCL;

/// <summary>
///     Minecraft Anvil 区域文件（.mca）的读取、地形渲染与区块删除。
///     渲染使用 Heightmaps 中的 WORLD_SURFACE / OCEAN_FLOOR，不解析方块调色板。
/// </summary>
public static class ModRegionFile
{
    public const int ChunkPixels = 4;
    public const int RegionChunks = 32;
    public const int RegionPixels = ChunkPixels * RegionChunks;

    /// <summary>
    ///     区块元数据。区域文件没有逐区块的保存时间，LastSavedUtc 取区域文件的最后写入时间。
    ///     区块 NBT 内的 LastUpdate 是世界游戏刻，不是 Unix 时间，不能直接当日期显示。
    /// </summary>
    public sealed record ChunkMeta(string? Status, int DataVersion, long InhabitedTime, DateTime LastSavedUtc);

    public sealed record RegionRender(int RegionX, int RegionZ, byte[] Pixels, bool[] Present, int ChunkCount,
        ChunkMeta?[] Meta);

    /// <summary>把区域文件渲染成 128×128 的 BGRA 地形图；无法读取时返回 null。</summary>
    /// <param name="layer">不为 null 时渲染指定 Y 范围顶部的方块（洞穴模式），空气保持透明。</param>
    public static RegionRender? RenderRegion(string regionPath, (int Bottom, int Top)? layer = null)
    {
        try
        {
            var (regionX, regionZ) = ParseRegionName(regionPath);
            var data = File.ReadAllBytes(regionPath);
            if (data.Length < 8192) return null;
            var lastSavedUtc = File.GetLastWriteTimeUtc(regionPath);

            var pixels = new byte[RegionPixels * RegionPixels * 4];
            var present = new bool[RegionChunks * RegionChunks];
            var meta = new ChunkMeta?[RegionChunks * RegionChunks];
            var chunkCount = 0;
            for (var chunkZ = 0; chunkZ < RegionChunks; chunkZ++)
            for (var chunkX = 0; chunkX < RegionChunks; chunkX++)
            {
                var index = chunkX + chunkZ * RegionChunks;
                var offset = ((data[index * 4] << 16) | (data[index * 4 + 1] << 8) | data[index * 4 + 2]) * 4096;
                var sectors = data[index * 4 + 3];
                if (offset == 0 || sectors == 0 || offset + 5 > data.Length) continue;

                var chunk = ReadChunk(data, offset);
                if (chunk is null) continue;

                present[index] = true;
                chunkCount++;
                meta[index] = ReadChunkMeta(chunk, lastSavedUtc);

                if (layer is { } range)
                {
                    RenderLayer(chunk, range.Bottom, range.Top, chunkX, chunkZ, pixels);
                    continue;
                }

                var heightmap = ReadHeightmap(chunk);
                if (heightmap is null)
                {
                    // 没有高度图（旧版本或特殊区块）时用灰色标出，仍允许选中删除
                    for (var pixelZ = 0; pixelZ < ChunkPixels; pixelZ++)
                    for (var pixelX = 0; pixelX < ChunkPixels; pixelX++)
                    {
                        var targetX = chunkX * ChunkPixels + pixelX;
                        var targetZ = chunkZ * ChunkPixels + pixelZ;
                        var target = (targetZ * RegionPixels + targetX) * 4;
                        pixels[target] = 80;
                        pixels[target + 1] = 80;
                        pixels[target + 2] = 80;
                        pixels[target + 3] = 255;
                    }

                    continue;
                }

                var (surface, floor, minY) = heightmap.Value;
                var sections = ReadSections(chunk);
                for (var pixelZ = 0; pixelZ < ChunkPixels; pixelZ++)
                for (var pixelX = 0; pixelX < ChunkPixels; pixelX++)
                {
                    var blockX = pixelX * 4 + 2;
                    var blockZ = pixelZ * 4 + 2;
                    var column = blockZ * 16 + blockX;
                    var height = GetHeight(surface, column) + minY;
                    var floorHeight = GetHeight(floor, column) + minY;
                    var water = height > 0 && floorHeight > 0 && height <= 63 && height > floorHeight;
                    var blockName = GetTopBlockName(sections, blockX, height - 1, blockZ);
                    if (blockName is not null && blockName.Contains("water", StringComparison.OrdinalIgnoreCase))
                        water = true;
                    var color = GetBlockColor(blockName, height, water);

                    var targetX = chunkX * ChunkPixels + pixelX;
                    var targetZ = chunkZ * ChunkPixels + pixelZ;
                    var target = (targetZ * RegionPixels + targetX) * 4;
                    pixels[target] = color.B;
                    pixels[target + 1] = color.G;
                    pixels[target + 2] = color.R;
                    pixels[target + 3] = 255;
                }
            }

            return new RegionRender(regionX, regionZ, pixels, present, chunkCount, meta);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"读取区域文件失败：{regionPath}", ModBase.LogLevel.Normal);
            return null;
        }
    }

    private static void RenderLayer(NbtCompound chunk, int bottomY, int topY, int chunkX, int chunkZ, byte[] pixels)
    {
        if (bottomY > topY) (bottomY, topY) = (topY, bottomY);
        var sections = ReadSections(chunk);
        for (var pixelZ = 0; pixelZ < ChunkPixels; pixelZ++)
        for (var pixelX = 0; pixelX < ChunkPixels; pixelX++)
        {
            var blockX = pixelX * 4 + 2;
            var blockZ = pixelZ * 4 + 2;
            string? blockName = null;
            var blockY = topY;
            for (var y = topY; y >= bottomY; y--)
            {
                var name = GetTopBlockName(sections, blockX, y, blockZ);
                if (name is null or "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air") continue;
                blockName = name;
                blockY = y;
                break;
            }

            if (blockName is null) continue;

            var water = blockName.Contains("water", StringComparison.OrdinalIgnoreCase);
            var color = GetBlockColor(blockName, blockY, water);

            var targetX = chunkX * ChunkPixels + pixelX;
            var targetZ = chunkZ * ChunkPixels + pixelZ;
            var target = (targetZ * RegionPixels + targetX) * 4;
            pixels[target] = color.B;
            pixels[target + 1] = color.G;
            pixels[target + 2] = color.R;
            pixels[target + 3] = 255;
        }
    }

    private static NbtCompound? ReadChunk(byte[] data, int offset)
    {
        try
        {
            var length = ReadInt32BigEndian(data, offset);
            if (length <= 1 || offset + 4 + length > data.Length) return null;

            var compression = data[offset + 4];
            using var payload = new MemoryStream(data, offset + 5, length - 1);
            using Stream decompressed = compression switch
            {
                1 => new GZipStream(payload, CompressionMode.Decompress),
                2 => new ZLibStream(payload, CompressionMode.Decompress),
                3 => payload,
                _ => throw new NotSupportedException($"不支持的区块压缩类型：{compression}")
            };

            var nbt = new NbtFile();
            nbt.LoadFromStream(decompressed, NbtCompression.None);
            return nbt.RootTag as NbtCompound;
        }
        catch
        {
            // 区域文件头中可能存在未使用或被截断的条目，跳过即可
            return null;
        }
    }

    private static ChunkMeta ReadChunkMeta(NbtCompound chunk, DateTime lastSavedUtc)
    {
        var level = chunk["Level"] as NbtCompound ?? chunk;
        var status = (chunk["Status"] ?? level["Status"]) is NbtString value ? value.Value : null;
        return new ChunkMeta(status, GetInt(chunk["DataVersion"]),
            GetLong(chunk["InhabitedTime"] ?? level["InhabitedTime"]), lastSavedUtc);
    }

    private static long GetLong(NbtTag? tag) => tag switch
    {
        NbtByte value => unchecked((sbyte)value.Value),
        NbtShort value => value.Value,
        NbtInt value => value.Value,
        NbtLong value => value.Value,
        _ => 0
    };

    private static (long[] Surface, long[] Floor, int MinY)? ReadHeightmap(NbtCompound chunk)
    {
        var level = chunk["Level"] as NbtCompound ?? chunk;
        var heightmaps = level["Heightmaps"] as NbtCompound;
        // 新生成、尚未升级的区块可能只有 *_WG 世界生成高度图
        var surface = (heightmaps?["WORLD_SURFACE"] ?? heightmaps?["WORLD_SURFACE_WG"]) as NbtLongArray;
        var floor = (heightmaps?["OCEAN_FLOOR"] ?? heightmaps?["OCEAN_FLOOR_WG"]) as NbtLongArray;
        if (surface is null || floor is null || surface.Value.Length == 0 || floor.Value.Length == 0) return null;

        // 1.18 起高度图存储的是相对最低建造高度的值（如 -64 会记为 0），而非绝对 Y 坐标
        var minY = 0;
        if (GetInt(chunk["DataVersion"]) >= 2825)
            minY = chunk["yPos"] is NbtInt yPos ? yPos.Value * 16 : -64;

        return (surface.Value, floor.Value, minY);
    }

    private sealed class SectionData
    {
        public required NbtList Palette { get; init; }
        public NbtLongArray? Data { get; init; }
        public int Bits => Math.Max(4, (int)Math.Ceiling(Math.Log2(Math.Max(1, Palette.Count))));
    }

    private static Dictionary<int, SectionData> ReadSections(NbtCompound chunk)
    {
        var result = new Dictionary<int, SectionData>();
        var level = chunk["Level"] as NbtCompound ?? chunk;
        var list = level["sections"] as NbtList ?? level["Sections"] as NbtList;
        if (list is null) return result;

        foreach (var tag in list)
        {
            if (tag is not NbtCompound section) continue;
            var blockStates = section["block_states"] as NbtCompound;
            var palette = blockStates?["palette"] as NbtList ?? section["Palette"] as NbtList;
            if (palette is null || palette.Count == 0) continue;

            result[GetInt(section["Y"])] = new SectionData
            {
                Palette = palette,
                Data = blockStates?["data"] as NbtLongArray ?? section["BlockStates"] as NbtLongArray
            };
        }

        return result;
    }

    private static int GetInt(NbtTag? tag) => tag switch
    {
        // NbtByte.Value 是 byte，1.18 起区块节的 Y 可能为负（如 -4 存成 0xFC），必须按有符号字节解释
        NbtByte value => unchecked((sbyte)value.Value),
        NbtShort value => value.Value,
        NbtInt value => value.Value,
        NbtLong value => (int)value.Value,
        _ => 0
    };

    private static string? GetTopBlockName(Dictionary<int, SectionData> sections, int x, int y, int z)
    {
        // 1.18 起主世界存在 Y < 0 的区块节，负数同样可以渲染（旧版本没有负数节，查表自然返回 null）
        if (!sections.TryGetValue(y >> 4, out var section)) return null;

        var index = (y & 15) * 256 + (z & 15) * 16 + (x & 15);
        var paletteIndex = section.Data is { Value.Length: > 0 } data
            ? ReadPacked(data.Value, index, section.Bits)
            : 0;
        if (paletteIndex < 0 || paletteIndex >= section.Palette.Count) return null;
        return section.Palette[paletteIndex] is NbtCompound entry
            ? (entry["Name"] as NbtString)?.Value
            : null;
    }

    private static int ReadPacked(long[] values, int index, int bits)
    {
        var perLong = 64 / bits;
        var nonSpanningLength = (4096 + perLong - 1) / perLong;
        if (values.Length == nonSpanningLength)
        {
            var longIndex = index / perLong;
            if (longIndex >= values.Length) return 0;
            var offset = index % perLong * bits;
            return (int)(((ulong)values[longIndex] >> offset) & ((1UL << bits) - 1));
        }

        // 1.15 及更早版本的调色板数据允许跨 long
        var bitIndex = (long)index * bits;
        var oldLongIndex = (int)(bitIndex >> 6);
        if (oldLongIndex >= values.Length) return 0;
        var oldOffset = (int)(bitIndex & 63);
        var value = (ulong)values[oldLongIndex] >> oldOffset;
        if (oldOffset > 64 - bits && oldLongIndex + 1 < values.Length)
            value |= (ulong)values[oldLongIndex + 1] << (64 - oldOffset);
        return (int)(value & ((1UL << bits) - 1));
    }

    internal static (byte R, byte G, byte B) GetBlockColor(string? name, int height, bool water)
    {
        if (water) return (44, 98, 190);
        if (string.IsNullOrEmpty(name)) return GetColor(height, false);

        var block = name.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? name[10..] : name;
        if (block.Contains("lava", StringComparison.OrdinalIgnoreCase)) return (220, 100, 20);
        if (block.Contains("water", StringComparison.OrdinalIgnoreCase)) return (44, 98, 190);
        if (block.Contains("grass", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("moss", StringComparison.OrdinalIgnoreCase)) return (92, 148, 68);
        if (block.Contains("leaves", StringComparison.OrdinalIgnoreCase)) return (60, 118, 48);
        if (block.Contains("sand", StringComparison.OrdinalIgnoreCase)) return (214, 199, 148);
        if (block.Contains("gravel", StringComparison.OrdinalIgnoreCase)) return (130, 124, 114);
        if (block.Contains("snow", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("ice", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("powder", StringComparison.OrdinalIgnoreCase)) return (232, 240, 245);
        if (block.Contains("stone", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("cobble", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("deepslate", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("andesite", StringComparison.OrdinalIgnoreCase)) return (120, 120, 120);
        if (block.Contains("dirt", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("mud", StringComparison.OrdinalIgnoreCase)) return (118, 84, 52);
        if (block.Contains("wood", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("plank", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("log", StringComparison.OrdinalIgnoreCase)) return (150, 110, 66);
        if (block.Contains("terracotta", StringComparison.OrdinalIgnoreCase) ||
            block.Contains("concrete", StringComparison.OrdinalIgnoreCase)) return (160, 120, 100);
        if (block.Contains("netherrack", StringComparison.OrdinalIgnoreCase)) return (110, 50, 50);
        if (block.Contains("end_stone", StringComparison.OrdinalIgnoreCase)) return (210, 208, 160);
        return GetColor(height, false);
    }

    private static int GetHeight(long[] heights, int index)
    {
        const int bits = 9;
        const int mask = (1 << bits) - 1;

        // 1.16+ 的高度图按 long 对齐打包（每个 long 放 7 个 9 位值，不跨 long）
        if (heights.Length >= 37)
        {
            var perLong = 64 / bits;
            var longIndex = index / perLong;
            if (longIndex < 0 || longIndex >= heights.Length) return 0;
            var offset = index % perLong * bits;
            return (int)(((ulong)heights[longIndex] >> offset) & mask);
        }

        // 旧版本（36 个 long）使用连续位流，允许跨 long
        var bitIndex = (long)index * bits;
        var oldLongIndex = (int)(bitIndex >> 6);
        if (oldLongIndex < 0 || oldLongIndex >= heights.Length) return 0;

        var oldOffset = (int)(bitIndex & 63);
        var value = (ulong)heights[oldLongIndex] >> oldOffset;
        if (oldOffset > 64 - bits && oldLongIndex + 1 < heights.Length)
            value |= (ulong)heights[oldLongIndex + 1] << (64 - oldOffset);

        return (int)(value & mask);
    }

    private static (byte R, byte G, byte B) GetColor(int height, bool water)
    {
        if (water)
        {
            var depth = Math.Clamp(63 - height, 0, 30);
            return ((byte)(40 + depth * 2), (byte)(90 + depth), (byte)(170 + depth));
        }

        if (height <= 0) return (36, 36, 36);
        return height switch
        {
            <= 62 => (214, 198, 156), // 沙滩
            <= 75 => (104, 158, 84), // 草地
            <= 90 => (82, 128, 68), // 深色草地
            <= 105 => (120, 120, 120), // 山地
            <= 125 => (150, 150, 150), // 高山岩石
            _ => (235, 235, 235) // 雪
        };
    }

    /// <summary>删除指定区块；会同时处理 region、poi、entities 三个目录。</summary>
    public static void DeleteChunksInWorld(string worldPath, IReadOnlyCollection<(int X, int Z)> chunks)
    {
        if (chunks.Count == 0) return;
        var byRegion = chunks.GroupBy(chunk => (RegionX: FloorDiv(chunk.X, 32), RegionZ: FloorDiv(chunk.Z, 32)));
        foreach (var group in byRegion)
        foreach (var folder in new[] { "region", "poi", "entities" })
        {
            var path = Path.Combine(worldPath, folder, $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            if (File.Exists(path))
                DeleteChunksInRegion(path, group.Key.RegionX, group.Key.RegionZ, group.ToHashSet());
        }
    }

    public sealed record ChunkData(int X, int Z, byte[] Raw);

    /// <summary>读取区域文件中的原始区块数据（含 4 字节长度、压缩类型与压缩负载）。</summary>
    public static List<ChunkData> ReadChunks(string regionPath)
    {
        var result = new List<ChunkData>();
        var (regionX, regionZ) = ParseRegionName(regionPath);
        var data = File.ReadAllBytes(regionPath);
        if (data.Length < 8192) return result;

        for (var index = 0; index < RegionChunks * RegionChunks; index++)
        {
            var offset = ((data[index * 4] << 16) | (data[index * 4 + 1] << 8) | data[index * 4 + 2]) * 4096;
            if (offset == 0 || data[index * 4 + 3] == 0 || offset + 4 > data.Length) continue;

            var length = ReadInt32BigEndian(data, offset);
            if (length <= 1 || offset + 4 + length > data.Length) continue;

            var raw = new byte[length + 4];
            Array.Copy(data, offset, raw, 0, raw.Length);
            result.Add(new ChunkData(regionX * 32 + index % 32, regionZ * 32 + index / 32, raw));
        }

        return result;
    }

    /// <summary>导出选中区块到目录，按原区域坐标生成 .mca 文件。</summary>
    public static int ExportChunks(string worldPath, IReadOnlyCollection<(int X, int Z)> chunks, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var exported = 0;
        foreach (var group in chunks.GroupBy(chunk => (RegionX: FloorDiv(chunk.X, 32), RegionZ: FloorDiv(chunk.Z, 32))))
        {
            var regionPath = Path.Combine(worldPath, "region",
                $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            if (!File.Exists(regionPath)) continue;

            var selected = group.Select(chunk => (chunk.X, chunk.Z)).ToHashSet();
            var payloads = ReadChunks(regionPath)
                .Where(chunk => selected.Contains((chunk.X, chunk.Z)))
                .ToDictionary(chunk => (chunk.X - group.Key.RegionX * 32) +
                                       (chunk.Z - group.Key.RegionZ * 32) * 32, chunk => chunk.Raw);
            if (payloads.Count == 0) continue;

            var outputPath = Path.Combine(outputDirectory,
                $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            WriteRegionFile(outputPath, payloads);
            exported += payloads.Count;
        }

        return exported;
    }

    /// <summary>把另一个区域文件中的全部区块合并进当前世界（同坐标覆盖）。</summary>
    public static int ImportChunks(string worldPath, string sourceRegionPath)
    {
        var chunks = ReadChunks(sourceRegionPath);
        if (chunks.Count == 0) return 0;
        WriteChunkPayloads(worldPath, chunks);
        return chunks.Count;
    }

    /// <summary>复制选中区块到偏移位置（会同步修改区块 NBT 中的 xPos/zPos）。</summary>
    public static int CopyChunks(string worldPath, IReadOnlyCollection<(int X, int Z)> chunks,
        int offsetX, int offsetZ)
    {
        var copied = new List<ChunkData>();
        foreach (var group in chunks.GroupBy(chunk => (RegionX: FloorDiv(chunk.X, 32), RegionZ: FloorDiv(chunk.Z, 32))))
        {
            var regionPath = Path.Combine(worldPath, "region",
                $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            if (!File.Exists(regionPath)) continue;

            var selected = group.Select(chunk => (chunk.X, chunk.Z)).ToHashSet();
            copied.AddRange(ReadChunks(regionPath)
                .Where(chunk => selected.Contains((chunk.X, chunk.Z)))
                .Select(chunk => new ChunkData(chunk.X + offsetX, chunk.Z + offsetZ,
                    MoveChunk(chunk.Raw, chunk.X + offsetX, chunk.Z + offsetZ))));
        }

        if (copied.Count == 0) return 0;
        WriteChunkPayloads(worldPath, copied);
        return copied.Count;
    }

    /// <summary>把选中区块移动到偏移后的坐标；重叠时不会误删刚复制出的区块。</summary>
    public static int MoveChunks(string worldPath, IReadOnlyCollection<(int X, int Z)> chunks, int offsetX, int offsetZ)
    {
        if (chunks.Count == 0 || offsetX == 0 && offsetZ == 0) return 0;

        var copied = CopyChunks(worldPath, chunks, offsetX, offsetZ);
        var destinations = chunks.Select(chunk => (chunk.X + offsetX, chunk.Z + offsetZ)).ToHashSet();
        DeleteChunksInWorld(worldPath, chunks.Where(chunk => !destinations.Contains(chunk)).ToList());
        return copied;
    }

    private static void WriteChunkPayloads(string worldPath, IEnumerable<ChunkData> chunks)
    {
        foreach (var group in chunks.GroupBy(chunk => (RegionX: FloorDiv(chunk.X, 32), RegionZ: FloorDiv(chunk.Z, 32))))
        {
            var regionPath = Path.Combine(worldPath, "region",
                $"r.{group.Key.RegionX}.{group.Key.RegionZ}.mca");
            var payloads = File.Exists(regionPath)
                ? ReadChunks(regionPath).ToDictionary(chunk => (chunk.X - group.Key.RegionX * 32) +
                                                              (chunk.Z - group.Key.RegionZ * 32) * 32,
                    chunk => chunk.Raw)
                : new Dictionary<int, byte[]>();

            foreach (var chunk in group)
                payloads[(chunk.X - group.Key.RegionX * 32) + (chunk.Z - group.Key.RegionZ * 32) * 32] = chunk.Raw;

            WriteRegionFile(regionPath, payloads);
        }
    }

    private static void WriteRegionFile(string regionPath, IReadOnlyDictionary<int, byte[]> chunks)
    {
        using var output = new MemoryStream();
        output.Write(new byte[8192]);
        var header = new byte[8192];

        foreach (var (index, raw) in chunks.OrderBy(pair => pair.Key))
        {
            if (index < 0 || index >= 1024 || raw.Length < 5) continue;

            var sectors = (raw.Length + 4095) / 4096;
            if (sectors > 255) throw new InvalidDataException($"区块数据过大（{sectors} 个扇区），无法写入区域文件");
            while (output.Length % 4096 != 0) output.WriteByte(0);
            var sectorOffset = (int)(output.Length / 4096);
            if (sectorOffset > 0xFFFFFF) throw new InvalidDataException("区域文件过大，无法写入 3 字节偏移");

            output.Write(raw, 0, raw.Length);
            for (var i = raw.Length; i < sectors * 4096; i++) output.WriteByte(0);

            header[index * 4] = (byte)(sectorOffset >> 16);
            header[index * 4 + 1] = (byte)(sectorOffset >> 8);
            header[index * 4 + 2] = (byte)sectorOffset;
            header[index * 4 + 3] = (byte)sectors;
        }

        output.Position = 0;
        output.Write(header, 0, header.Length);

        if (File.Exists(regionPath))
            File.Copy(regionPath, regionPath + ".bak", true);

        var temp = regionPath + ".tmp";
        File.WriteAllBytes(temp, output.ToArray());
        File.Move(temp, regionPath, true);

        ModBase.Log($"[MapEditor] 已写入区域文件 {Path.GetFileName(regionPath)}（{chunks.Count} 个区块）",
            ModBase.LogLevel.Normal);
    }

    private static byte[] MoveChunk(byte[] raw, int x, int z)
    {
        try
        {
            var length = ReadInt32BigEndian(raw, 0);
            if (length <= 1 || length + 4 > raw.Length) return raw;

            using var payload = new MemoryStream(raw, 5, length - 1);
            using Stream decompressed = raw[4] switch
            {
                1 => new GZipStream(payload, CompressionMode.Decompress),
                2 => new ZLibStream(payload, CompressionMode.Decompress),
                3 => payload,
                _ => throw new NotSupportedException($"不支持的区块压缩类型：{raw[4]}")
            };

            var nbt = new NbtFile();
            nbt.LoadFromStream(decompressed, NbtCompression.None);
            if (nbt.RootTag is not NbtCompound root) return raw;

            var level = root["Level"] as NbtCompound;
            root["xPos"] = new NbtInt("xPos", x);
            root["zPos"] = new NbtInt("zPos", z);
            if (level is not null)
            {
                level["xPos"] = new NbtInt("xPos", x);
                level["zPos"] = new NbtInt("zPos", z);
            }

            using var plain = new MemoryStream();
            nbt.SaveToStream(plain, NbtCompression.None);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(plain.ToArray());

            var body = compressed.ToArray();
            var result = new byte[body.Length + 5];
            var newLength = body.Length + 1;
            result[0] = (byte)(newLength >> 24);
            result[1] = (byte)(newLength >> 16);
            result[2] = (byte)(newLength >> 8);
            result[3] = (byte)newLength;
            result[4] = 2; // zlib
            body.CopyTo(result, 5);
            return result;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "复制区块时修改坐标失败", ModBase.LogLevel.Hint);
            return raw;
        }
    }

    private static void DeleteChunksInRegion(string regionPath, int regionX, int regionZ,
        IReadOnlySet<(int X, int Z)> chunks)
    {
        var data = File.ReadAllBytes(regionPath);
        if (data.Length < 8192) return;

        var header = new byte[8192];
        Array.Copy(data, header, 8192);

        using var output = new MemoryStream();
        output.Write(new byte[8192]); // 预留头部

        var anyDeleted = false;
        for (var chunkZ = 0; chunkZ < RegionChunks; chunkZ++)
        for (var chunkX = 0; chunkX < RegionChunks; chunkX++)
        {
            var index = chunkX + chunkZ * RegionChunks;
            var offset = ((data[index * 4] << 16) | (data[index * 4 + 1] << 8) | data[index * 4 + 2]) * 4096;
            var sectors = data[index * 4 + 3];
            if (offset == 0 || sectors == 0) continue;

            var globalX = regionX * 32 + chunkX;
            var globalZ = regionZ * 32 + chunkZ;
            if (chunks.Contains((globalX, globalZ)))
            {
                ClearHeaderSlot(header, index);
                anyDeleted = true;
                continue;
            }

            // 头部条目可能指向文件外，先确认长度字段可读，避免整个删除因一个坏条目失败
            if (offset + 4 > data.Length)
            {
                ClearHeaderSlot(header, index);
                continue;
            }

            var length = ReadInt32BigEndian(data, offset);
            var total = length + 4;
            if (length <= 0 || offset + total > data.Length)
            {
                // 丢弃损坏或未使用的条目，避免复制到新文件后仍指向无效数据
                ClearHeaderSlot(header, index);
                continue;
            }

            while (output.Length % 4096 != 0) output.WriteByte(0);
            var newSectorOffset = (int)(output.Length / 4096);
            var newSectors = (total + 4095) / 4096;
            if (newSectorOffset > 0xFFFFFF) throw new InvalidDataException("区域文件过大，无法写入 3 字节偏移");
            if (newSectors > 255) throw new InvalidDataException($"区块数据过大（{newSectors} 个扇区），无法写入区域文件");

            output.Write(data, offset, total);
            for (var i = total; i < newSectors * 4096; i++) output.WriteByte(0);

            header[index * 4] = (byte)(newSectorOffset >> 16);
            header[index * 4 + 1] = (byte)(newSectorOffset >> 8);
            header[index * 4 + 2] = (byte)newSectorOffset;
            header[index * 4 + 3] = (byte)newSectors;
        }

        if (!anyDeleted) return;

        output.Position = 0;
        output.Write(header, 0, header.Length);

        var backup = regionPath + ".bak";
        File.Copy(regionPath, backup, true);

        var temp = regionPath + ".tmp";
        File.WriteAllBytes(temp, output.ToArray());
        File.Move(temp, regionPath, true);

        ModBase.Log($"[MapEditor] 已从 {Path.GetFileName(regionPath)} 删除 {chunks.Count} 个区块（备份：{backup}）",
            ModBase.LogLevel.Normal);
    }

    /// <summary>清空区域文件头部中某个区块的偏移与时间戳。</summary>
    private static void ClearHeaderSlot(byte[] header, int index)
    {
        Array.Clear(header, index * 4, 4);
        Array.Clear(header, 4096 + index * 4, 4);
    }

    private static (int X, int Z) ParseRegionName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var z))
            return (x, z);
        throw new FormatException($"无效的区域文件名：{name}");
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

    /// <summary>区域文件中的区块长度与时间戳均为大端序。</summary>
    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
