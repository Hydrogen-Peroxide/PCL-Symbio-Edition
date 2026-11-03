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

    public sealed record RegionRender(int RegionX, int RegionZ, byte[] Pixels, bool[] Present, int ChunkCount);

    /// <summary>把区域文件渲染成 128×128 的 BGRA 地形图；无法读取时返回 null。</summary>
    public static RegionRender? RenderRegion(string regionPath)
    {
        try
        {
            var (regionX, regionZ) = ParseRegionName(regionPath);
            var data = File.ReadAllBytes(regionPath);
            if (data.Length < 8192) return null;

            var pixels = new byte[RegionPixels * RegionPixels * 4];
            var present = new bool[RegionChunks * RegionChunks];
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

                var (surface, floor) = heightmap.Value;
                for (var pixelZ = 0; pixelZ < ChunkPixels; pixelZ++)
                for (var pixelX = 0; pixelX < ChunkPixels; pixelX++)
                {
                    var blockX = pixelX * 4 + 2;
                    var blockZ = pixelZ * 4 + 2;
                    var column = blockZ * 16 + blockX;
                    var height = GetHeight(surface, column);
                    var floorHeight = GetHeight(floor, column);
                    var water = height > 0 && floorHeight > 0 && height <= 63 && height > floorHeight;
                    var color = GetColor(height, water);

                    var targetX = chunkX * ChunkPixels + pixelX;
                    var targetZ = chunkZ * ChunkPixels + pixelZ;
                    var target = (targetZ * RegionPixels + targetX) * 4;
                    pixels[target] = color.B;
                    pixels[target + 1] = color.G;
                    pixels[target + 2] = color.R;
                    pixels[target + 3] = 255;
                }
            }

            return new RegionRender(regionX, regionZ, pixels, present, chunkCount);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"读取区域文件失败：{regionPath}", ModBase.LogLevel.Normal);
            return null;
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

    private static (long[] Surface, long[] Floor)? ReadHeightmap(NbtCompound chunk)
    {
        var level = chunk["Level"] as NbtCompound ?? chunk;
        var heightmaps = level["Heightmaps"] as NbtCompound;
        // 新生成、尚未升级的区块可能只有 *_WG 世界生成高度图
        var surface = (heightmaps?["WORLD_SURFACE"] ?? heightmaps?["WORLD_SURFACE_WG"]) as NbtLongArray;
        var floor = (heightmaps?["OCEAN_FLOOR"] ?? heightmaps?["OCEAN_FLOOR_WG"]) as NbtLongArray;
        return surface is null || floor is null ? null : (surface.Value, floor.Value);
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
                header[index * 4] = 0;
                header[index * 4 + 1] = 0;
                header[index * 4 + 2] = 0;
                header[index * 4 + 3] = 0;
                header[4096 + index * 4] = 0;
                header[4096 + index * 4 + 1] = 0;
                header[4096 + index * 4 + 2] = 0;
                header[4096 + index * 4 + 3] = 0;
                anyDeleted = true;
                continue;
            }

            var length = ReadInt32BigEndian(data, offset);
            var total = length + 4;
            if (length <= 0 || offset + total > data.Length)
            {
                // 丢弃损坏或未使用的条目，避免复制到新文件后仍指向无效数据
                header[index * 4] = 0;
                header[index * 4 + 1] = 0;
                header[index * 4 + 2] = 0;
                header[index * 4 + 3] = 0;
                header[4096 + index * 4] = 0;
                header[4096 + index * 4 + 1] = 0;
                header[4096 + index * 4 + 2] = 0;
                header[4096 + index * 4 + 3] = 0;
                continue;
            }

            while (output.Length % 4096 != 0) output.WriteByte(0);
            var newSectorOffset = (int)(output.Length / 4096);
            var newSectors = (total + 4095) / 4096;
            if (newSectorOffset > 0xFFFFFF) throw new InvalidDataException("区域文件过大，无法写入 3 字节偏移");

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
