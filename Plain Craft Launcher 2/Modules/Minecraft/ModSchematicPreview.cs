using System;
using System.Collections.Generic;
using System.IO;
using fNbt;

namespace PCL;

/// <summary>
///     投影原理图（.litematic / .schem / .schematic / .nbt）的方块解析。
///     只负责解析方块列表，不修改原理图文件。
/// </summary>
public static class ModSchematicPreview
{
    /// <summary>
    ///     单个方块。Properties 保存方块状态（如 facing=east,type=bottom），用于还原楼梯、台阶等非完整方块的形状。
    /// </summary>
    public readonly record struct Block(int X, int Y, int Z, string Name, string? Properties = null);

    public sealed class Data
    {
        public int Width;
        public int Height;
        public int Length;
        public List<Block> Blocks = [];
        public string? Error;

        public bool IsEmpty => Blocks.Count == 0;
    }

    /// <summary>读取原理图文件并提取方块列表；解析失败时在 Data.Error 中记录原因。</summary>
    public static Data Read(string path)
    {
        var data = new Data();
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".litematic":
                    ReadLitematic(path, data);
                    break;
                case ".schem":
                    ReadSchem(path, data);
                    break;
                case ".nbt":
                    ReadStructure(path, data);
                    break;
                case ".schematic":
                    data.Error = "MCEdit/WorldEdit 旧版 schematic 暂不支持预览";
                    break;
                default:
                    data.Error = "不支持的原理图格式";
                    break;
            }
        }
        catch (Exception ex)
        {
            data.Error = ex.Message;
        }

        return data;
    }

    private static void ReadStructure(string path, Data data)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var nbt = new NbtFile();
        nbt.LoadFromStream(fs, NbtCompression.AutoDetect);
        if (nbt.RootTag is not NbtCompound root) return;

        var size = root.Get<NbtList>("size");
        if (size is null || size.Count < 3) return;
        data.Length = size[0].IntValue;
        data.Height = size[1].IntValue;
        data.Width = size[2].IntValue;

        var palette = root.Get<NbtList>("palette");
        var blocks = root.Get<NbtList>("blocks");
        if (palette is null || blocks is null) return;

        var states = new (string Name, string? Properties)[palette.Count];
        for (var i = 0; i < palette.Count; i++)
            states[i] = ReadPaletteEntry(palette[i]);

        foreach (var tag in blocks)
        {
            if (tag is not NbtCompound block) continue;
            var pos = block.Get<NbtList>("pos");
            var state = block.Get<NbtInt>("state");
            if (pos is null || pos.Count < 3 || state is null) continue;

            var x = pos[0].IntValue;
            var y = pos[1].IntValue;
            var z = pos[2].IntValue;
            var stateIndex = state.Value;
            if (stateIndex < 0 || stateIndex >= states.Length) continue;

            if (IsSolid(states[stateIndex].Name))
                data.Blocks.Add(new Block(x, y, z, states[stateIndex].Name, states[stateIndex].Properties));
        }
    }

    private static void ReadSchem(string path, Data data)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var nbt = new NbtFile();
        nbt.LoadFromStream(fs, NbtCompression.AutoDetect);
        if (nbt.RootTag is not NbtCompound root) return;

        var widthTag = root.Get<NbtShort>("Width");
        var heightTag = root.Get<NbtShort>("Height");
        var lengthTag = root.Get<NbtShort>("Length");
        if (widthTag is null || heightTag is null || lengthTag is null) return;

        data.Width = widthTag.Value;
        data.Height = heightTag.Value;
        data.Length = lengthTag.Value;

        var palette = root.Get<NbtCompound>("Palette");
        var blockData = root.Get<NbtByteArray>("BlockData");
        if (palette is null || blockData is null)
        {
            data.Error = "Sponge v1 schematic 暂不支持预览";
            return;
        }

        // 索引 -> 方块名与方块状态；Sponge 的调色板键名形如 minecraft:oak_stairs[facing=east,type=bottom]
        var paletteCount = palette.Count;
        var states = new (string Name, string? Properties)[paletteCount];
        foreach (var tag in palette)
        {
            if (tag is NbtInt index && index.Value >= 0 && index.Value < paletteCount)
                states[index.Value] = SplitState(tag.Name);
        }

        var bits = Math.Max(2, (int)Math.Ceiling(Math.Log2(Math.Max(1, paletteCount))));
        var bytes = blockData.Value;

        for (var y = 0; y < data.Height; y++)
        for (var z = 0; z < data.Length; z++)
        for (var x = 0; x < data.Width; x++)
        {
            var blockIndex = (y * data.Length + z) * data.Width + x;
            var stateIndex = ReadBits(bytes, blockIndex * bits, bits);
            if (stateIndex < 0 || stateIndex >= paletteCount) continue;

            var state = states[stateIndex];
            if (string.IsNullOrEmpty(state.Name)) continue;
            if (!IsSolid(state.Name)) continue;

            data.Blocks.Add(new Block(x, y, z, state.Name, state.Properties));
        }
    }

    private static void ReadLitematic(string path, Data data)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var nbt = new NbtFile();
        nbt.LoadFromStream(fs, NbtCompression.AutoDetect);
        if (nbt.RootTag is not NbtCompound root) return;

        var regions = root.Get<NbtCompound>("Regions");
        if (regions is null) return;

        foreach (var regionTag in regions)
        {
            if (regionTag is not NbtCompound region) continue;
            var sizeTag = region.Get<NbtCompound>("Size");
            var paletteTag = region.Get<NbtList>("BlockStatePalette");
            var statesTag = region.Get<NbtLongArray>("BlockStates");
            if (sizeTag is null || paletteTag is null || statesTag is null) continue;

            // Litematica 的 Size 可能带符号（部分版本用负数表示包围盒方向），取绝对值
            var sizeX = Math.Abs(sizeTag.Get<NbtInt>("x")?.Value ?? 0);
            var sizeY = Math.Abs(sizeTag.Get<NbtInt>("y")?.Value ?? 0);
            var sizeZ = Math.Abs(sizeTag.Get<NbtInt>("z")?.Value ?? 0);
            if (sizeX == 0 || sizeY == 0 || sizeZ == 0) continue;

            // 多区域原理图用 Position 作为局部坐标原点，叠加后各区域才能对齐
            var positionTag = region.Get<NbtCompound>("Position");
            var posX = positionTag?.Get<NbtInt>("x")?.Value ?? 0;
            var posY = positionTag?.Get<NbtInt>("y")?.Value ?? 0;
            var posZ = positionTag?.Get<NbtInt>("z")?.Value ?? 0;

            data.Width = sizeX;
            data.Height = sizeY;
            data.Length = sizeZ;

            var states = new (string Name, string? Properties)[paletteTag.Count];
            for (var i = 0; i < paletteTag.Count; i++)
                states[i] = ReadPaletteEntry(paletteTag[i]);

            var bits = Math.Max(2, (int)Math.Ceiling(Math.Log2(Math.Max(1, paletteTag.Count))));
            var values = statesTag.Value;

            for (var y = 0; y < sizeY; y++)
            for (var z = 0; z < sizeZ; z++)
            for (var x = 0; x < sizeX; x++)
            {
                var blockIndex = (y * sizeZ + z) * sizeX + x;
                var stateIndex = ReadLongBits(values, blockIndex * bits, bits);
                if (stateIndex < 0 || stateIndex >= states.Length) continue;

                var state = states[stateIndex];
                if (!IsSolid(state.Name)) continue;

                data.Blocks.Add(new Block(posX + x, posY + y, posZ + z, state.Name, state.Properties));
            }
        }
    }

    private static int ReadBits(byte[] data, int bitIndex, int bitCount)
    {
        if (bitCount <= 0 || bitCount > 32) return 0;
        var result = 0;
        for (var i = 0; i < bitCount; i++)
        {
            var globalBit = bitIndex + i;
            var byteIndex = globalBit >> 3;
            if (byteIndex >= data.Length) return 0;
            var bit = (data[byteIndex] >> (globalBit & 7)) & 1;
            result |= bit << i;
        }

        return result;
    }

    private static int ReadLongBits(long[] data, int bitIndex, int bitCount)
    {
        if (bitCount <= 0 || bitCount > 32) return 0;
        var result = 0UL;
        for (var i = 0; i < bitCount; i++)
        {
            var globalBit = bitIndex + i;
            var longIndex = globalBit >> 6;
            if (longIndex >= data.Length) return 0;
            var bit = (int)((ulong)data[longIndex] >> (globalBit & 63)) & 1;
            result |= (ulong)bit << i;
        }

        return (int)result;
    }

    private static (string Name, string? Properties) ReadPaletteEntry(NbtTag? tag)
    {
        if (tag is not NbtCompound entry) return ("minecraft:stone", null);
        var name = entry.Get<NbtString>("Name")?.Value ?? "minecraft:stone";
        var properties = entry.Get<NbtCompound>("Properties");
        if (properties is null) return (name, null);

        var parts = new List<string>();
        foreach (var child in properties)
            if (child is NbtString value)
                parts.Add(child.Name + "=" + value.Value);
        return (name, parts.Count == 0 ? null : string.Join(",", parts));
    }

    /// <summary>把 Sponge 的调色板键名拆成方块名与状态串。</summary>
    private static (string Name, string? Properties) SplitState(string raw)
    {
        var open = raw.IndexOf('[');
        if (open < 0 || !raw.EndsWith("]", StringComparison.Ordinal)) return (raw, null);
        return (raw[..open], raw[(open + 1)..^1]);
    }

    private static bool IsSolid(string name)
    {
        var id = name;
        var cut = id.IndexOfAny(['[', '{']);
        if (cut >= 0) id = id[..cut];
        id = id.Trim().ToLowerInvariant();
        if (id.StartsWith("minecraft:", StringComparison.Ordinal)) id = id[10..];
        // 只排除三种空气，不能用包含判断：oak_stairs 这类名字里也含有 "air"
        return id is not ("air" or "cave_air" or "void_air" or "structure_void");
    }
}
