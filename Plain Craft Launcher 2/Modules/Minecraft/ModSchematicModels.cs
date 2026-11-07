using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PCL;

/// <summary>
///     实例客户端 JAR 的只读访问：方块状态、模型 JSON 与方块贴图。
///     所有条目都按需读取并缓存，可在后台线程使用。
/// </summary>
public sealed class ModSchematicAssets : IDisposable
{
    private const string TextureRoot = "assets/minecraft/textures/";

    private readonly Dictionary<string, JsonDocument?> _blockStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ZipArchive? _jar;

    private readonly Dictionary<string, JsonDocument?> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ZipArchiveEntry> _textures = new(StringComparer.Ordinal);

    public ModSchematicAssets(string? jarPath)
    {
        try
        {
            if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath)) return;
            _jar = ZipFile.OpenRead(jarPath);
            IndexTextures(_jar);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 读取客户端资源失败（{jarPath}）", ModBase.LogLevel.Developer);
        }
    }

    /// <summary>可用贴图数量，0 表示没有可用的客户端资源。</summary>
    public int TextureCount => _textures.Count;

    public void Dispose() => _jar?.Dispose();

    /// <summary>按键名（如 block/oak_planks、entity/chest/normal）取贴图条目。</summary>
    public bool TryGetTextureEntry(string key, out ZipArchiveEntry entry) => _textures.TryGetValue(key, out entry!);

    /// <summary>读取方块状态 JSON，<paramref name="id" /> 已去掉命名空间。</summary>
    public JsonElement? GetBlockState(string id) =>
        GetJson(_blockStates, "assets/minecraft/blockstates/" + id + ".json");

    /// <summary>读取模型 JSON，<paramref name="path" /> 形如 block/oak_stairs 或 minecraft:block/oak_stairs。</summary>
    public JsonElement? GetModel(string path) =>
        GetJson(_models, "assets/minecraft/models/" + NormalizeModelPath(path) + ".json");

    private JsonElement? GetJson(Dictionary<string, JsonDocument?> cache, string entryPath)
    {
        if (cache.TryGetValue(entryPath, out var cached)) return cached?.RootElement;

        JsonDocument? document = null;
        try
        {
            var entry = _jar?.GetEntry(entryPath);
            if (entry is not null)
                using (var stream = entry.Open())
                {
                    document = JsonDocument.Parse(stream);
                }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 解析资源失败（{entryPath}）", ModBase.LogLevel.Developer);
        }

        cache[entryPath] = document;
        return document?.RootElement;
    }

    internal static string NormalizeModelPath(string path)
    {
        var value = path.Trim();
        if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) value = value[10..];
        else
        {
            var colon = value.IndexOf(':');
            if (colon > 0) value = value[(colon + 1)..];
        }

        if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) value = value[..^5];
        if (value.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        return value;
    }

    /// <summary>模型里写的贴图引用（minecraft:block/oak_planks）转成贴图表里的键（block/oak_planks）。</summary>
    internal static string NormalizeTexturePath(string path)
    {
        var value = path.Trim();
        if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) value = value[10..];
        else
        {
            var colon = value.IndexOf(':');
            if (colon > 0) value = value[(colon + 1)..];
        }

        if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        if (value.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) value = value[9..];
        if (value.StartsWith("blocks/", StringComparison.OrdinalIgnoreCase)) value = "block/" + value[7..];
        else if (!value.StartsWith("block/", StringComparison.OrdinalIgnoreCase) &&
                 !value.StartsWith("entity/", StringComparison.OrdinalIgnoreCase))
            value = "block/" + value;

        return value;
    }

    private void IndexTextures(ZipArchive jar)
    {
        foreach (var entry in jar.Entries)
        {
            var path = entry.FullName;
            if (!path.StartsWith(TextureRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

            var key = path[TextureRoot.Length..^4].Replace('\\', '/');
            // 旧版 blocks/ 目录与新版 block/ 等价，优先保留新版
            if (key.StartsWith("blocks/", StringComparison.OrdinalIgnoreCase))
            {
                var modern = "block/" + key[7..];
                if (!_textures.ContainsKey(modern)) _textures[modern] = entry;
                continue;
            }

            _textures[key] = entry;
        }
    }
}

/// <summary>
///     把方块状态解析成实际几何：blockstates → models → 元素与贴图，
///     处理父模型继承、变体匹配、multipart 条件、方块旋转与元素旋转。
/// </summary>
public sealed class ModSchematicModelLoader
{
    private static readonly Dictionary<string, string> FallbackProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["facing"] = "north",
        ["half"] = "bottom",
        ["shape"] = "straight",
        ["type"] = "bottom",
        ["axis"] = "y",
        ["open"] = "false",
        ["hinge"] = "left",
        ["face"] = "wall",
        ["part"] = "head",
        ["attachment"] = "wall",
        ["lit"] = "false",
        ["powered"] = "false",
        ["waterlogged"] = "false",
        ["up"] = "false"
    };

    private readonly ModSchematicAssets _assets;
    private readonly Dictionary<string, BlockModel?> _blocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelDefinition?> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _texturesOfStates = new(StringComparer.OrdinalIgnoreCase);

    public ModSchematicModelLoader(ModSchematicAssets assets) => _assets = assets;

    /// <summary>按方块状态取几何；找不到对应模型时返回 null，由调用方退回内置近似形状。</summary>
    public BlockModel? Get(string name, string? properties)
    {
        var key = name + "|" + properties;
        if (_blocks.TryGetValue(key, out var cached)) return cached;

        BlockModel? model = null;
        try
        {
            model = Build(name, properties);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 解析方块模型失败（{name}）", ModBase.LogLevel.Developer);
        }

        _blocks[key] = model;
        return model;
    }

    /// <summary>收集该方块所有变体用到的贴图键，供打包贴图集使用。</summary>
    public IReadOnlyList<string> CollectTextureNames(string name)
    {
        if (_texturesOfStates.TryGetValue(name, out var cached)) return cached;

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var blockState = _assets.GetBlockState(StripNamespace(name));
            if (blockState is { } root)
                foreach (var model in EnumerateAllModels(root))
                {
                    var definition = LoadDefinition(model, 0);
                    if (definition is null) continue;
                    foreach (var element in definition.Elements)
                    foreach (var face in element.Faces)
                    {
                        var texture = ResolveTextureReference(face.Texture, definition.Textures);
                        if (texture is null || !seen.Add(texture)) continue;
                        result.Add(texture);
                    }
                }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"[SchematicPreview] 收集贴图失败（{name}）", ModBase.LogLevel.Developer);
        }

        var array = result.ToArray();
        _texturesOfStates[name] = array;
        return array;
    }

    #region 方块状态 → 模型

    private BlockModel? Build(string name, string? properties)
    {
        var id = StripNamespace(name);
        if (id.Length == 0) return null;

        var blockState = _assets.GetBlockState(id);
        if (blockState is not { } root) return null;

        var props = ParseProperties(properties);
        var applications = EnumerateApplications(root, props);
        if (applications.Count == 0) return null;

        var quads = new List<FaceQuad>();
        var elementCount = 0;
        var cubeCandidate = true;

        foreach (var application in applications)
        {
            var definition = LoadDefinition(application.Model, 0);
            if (definition is null) continue;

            foreach (var element in definition.Elements)
            {
                elementCount++;
                if (!IsUnitCube(element) || element.Rotation is not null || application.X != 0 ||
                    application.Y != 0)
                    cubeCandidate = false;
                AddElementQuads(quads, element, application.X, application.Y, definition.Textures);
            }
        }

        if (elementCount == 0) return null;

        var result = new BlockModel();
        if (cubeCandidate && applications.Count == 1 && quads.Count == 6 &&
            TryGetCubeTextures(applications[0].Model, out var top, out var bottom, out var side))
        {
            result.IsFullCube = true;
            result.UseFastPath = true;
            result.TopTexture = top;
            result.BottomTexture = bottom;
            result.SideTexture = side;
            return result;
        }

        result.Quads = quads;
        result.IsFullCube = elementCount == 1 && quads.Count == 6 && IsFullCubeGeometry(quads);
        return result;
    }

    private bool TryGetCubeTextures(string modelPath, out string top, out string bottom, out string side)
    {
        top = bottom = side = "";
        var definition = LoadDefinition(modelPath, 0);
        if (definition is null || definition.Elements.Count != 1) return false;
        var element = definition.Elements[0];
        if (!IsUnitCube(element) || element.Rotation is not null) return false;

        string? Get(string direction)
        {
            var face = element.Faces.FirstOrDefault(item => item.Direction == direction);
            return face is null ? null : ResolveTextureReference(face.Texture, definition.Textures);
        }

        var up = Get("up");
        var down = Get("down");
        var north = Get("north");
        var south = Get("south");
        var east = Get("east");
        var west = Get("west");
        if (up is null || down is null || north is null || south is null || east is null || west is null)
            return false;
        // 四侧贴图一致才可以用「顶 / 底 / 侧」三张贴图的快速路径，否则（如熔炉正面）改走面列表
        if (north != south || north != east || north != west) return false;

        top = up;
        bottom = down;
        side = north;
        return true;
    }

    private static bool IsUnitCube(ElementDefinition element) =>
        Math.Abs(element.X1) < 0.001d && Math.Abs(element.Y1) < 0.001d && Math.Abs(element.Z1) < 0.001d &&
        Math.Abs(element.X2 - 16d) < 0.001d && Math.Abs(element.Y2 - 16d) < 0.001d &&
        Math.Abs(element.Z2 - 16d) < 0.001d;

    /// <summary>6 个面正好拼成一个完整正方体（例如旋转过的原木）。</summary>
    private static bool IsFullCubeGeometry(List<FaceQuad> quads)
    {
        if (quads.Count != 6) return false;
        double minX = 1d, minY = 1d, minZ = 1d, maxX = 0d, maxY = 0d, maxZ = 0d;
        foreach (var quad in quads)
        foreach (var point in new[] { quad.P0, quad.P1, quad.P2, quad.P3 })
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        return Math.Abs(minX) < 0.001d && Math.Abs(minY) < 0.001d && Math.Abs(minZ) < 0.001d &&
               Math.Abs(maxX - 1d) < 0.001d && Math.Abs(maxY - 1d) < 0.001d && Math.Abs(maxZ - 1d) < 0.001d;
    }

    private readonly record struct ModelApplication(string Model, int X, int Y);

    /// <summary>列出所有命中的变体 / multipart 条目。</summary>
    private List<ModelApplication> EnumerateApplications(JsonElement blockState, Dictionary<string, string> props)
    {
        var result = new List<ModelApplication>();
        if (blockState.TryGetProperty("variants", out var variants))
        {
            var bestScore = -1;
            JsonElement best = default;
            foreach (var variant in variants.EnumerateObject())
            {
                var score = ScoreVariant(variant.Name, props);
                if (score < 0 || score <= bestScore) continue;
                bestScore = score;
                best = variant.Value;
            }

            if (bestScore >= 0)
            {
                AddApplications(best, result);
                return result;
            }

            // 没有精确命中时，用默认状态补齐缺失属性再试一次
            var relaxed = new Dictionary<string, string>(props, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in FallbackProperties)
                if (!relaxed.ContainsKey(pair.Key))
                    relaxed[pair.Key] = pair.Value;
            foreach (var variant in variants.EnumerateObject())
            {
                var score = ScoreVariant(variant.Name, relaxed);
                if (score < 0 || score <= bestScore) continue;
                bestScore = score;
                best = variant.Value;
            }

            if (bestScore >= 0) AddApplications(best, result);
        }
        else if (blockState.TryGetProperty("multipart", out var multipart))
        {
            foreach (var part in multipart.EnumerateArray())
            {
                if (part.TryGetProperty("when", out var when) && !ConditionMatches(when, props)) continue;
                if (part.TryGetProperty("apply", out var apply)) AddApplications(apply, result);
            }
        }

        return result;
    }

    /// <summary>列出所有变体 / multipart 用到的模型，仅用于收集贴图。</summary>
    private static IEnumerable<string> EnumerateAllModels(JsonElement blockState)
    {
        if (blockState.TryGetProperty("variants", out var variants))
        {
            foreach (var variant in variants.EnumerateObject())
            {
                if (variant.Value.ValueKind == JsonValueKind.Object)
                {
                    if (variant.Value.TryGetProperty("model", out var model) && model.GetString() is { } path)
                        yield return path;
                }
                else if (variant.Value.ValueKind == JsonValueKind.Array)
                    foreach (var item in variant.Value.EnumerateArray())
                        if (item.TryGetProperty("model", out var model) && model.GetString() is { } path)
                            yield return path;
            }
        }
        else if (blockState.TryGetProperty("multipart", out var multipart))
        {
            foreach (var part in multipart.EnumerateArray())
            {
                if (!part.TryGetProperty("apply", out var apply)) continue;
                if (apply.ValueKind == JsonValueKind.Object)
                {
                    if (apply.TryGetProperty("model", out var model) && model.GetString() is { } path)
                        yield return path;
                }
                else if (apply.ValueKind == JsonValueKind.Array)
                    foreach (var item in apply.EnumerateArray())
                        if (item.TryGetProperty("model", out var model) && model.GetString() is { } path)
                            yield return path;
            }
        }
    }

    private static void AddApplications(JsonElement value, List<ModelApplication> target)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) AddApplication(item, target);
            return;
        }

        AddApplication(value, target);
    }

    private static void AddApplication(JsonElement value, List<ModelApplication> target)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var model = value.TryGetProperty("model", out var modelTag) ? modelTag.GetString() : null;
        if (string.IsNullOrEmpty(model)) return;
        var x = value.TryGetProperty("x", out var xTag) && xTag.TryGetInt32(out var xValue) ? xValue : 0;
        var y = value.TryGetProperty("y", out var yTag) && yTag.TryGetInt32(out var yValue) ? yValue : 0;
        target.Add(new ModelApplication(model, x, y));
    }

    /// <summary>返回变体键与给定状态的匹配程度，-1 表示不匹配；键里写到的属性必须全部相等。</summary>
    private static int ScoreVariant(string variantKey, Dictionary<string, string> props)
    {
        if (string.IsNullOrWhiteSpace(variantKey)) return 0;
        var score = 0;
        foreach (var pair in variantKey.Split(','))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (!props.TryGetValue(key, out var actual)) return -1;
            if (!string.Equals(actual, value, StringComparison.OrdinalIgnoreCase)) return -1;
            score++;
        }

        return score;
    }

    private static bool ConditionMatches(JsonElement condition, Dictionary<string, string> props)
    {
        foreach (var entry in condition.EnumerateObject())
        {
            if (entry.Name.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                if (!entry.Value.EnumerateArray().Any(item => ConditionMatches(item, props))) return false;
                continue;
            }

            if (entry.Name.Equals("AND", StringComparison.OrdinalIgnoreCase))
            {
                if (!entry.Value.EnumerateArray().All(item => ConditionMatches(item, props))) return false;
                continue;
            }

            var expected = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString()
                : entry.Value.ToString();
            if (!props.TryGetValue(entry.Name, out var actual) ||
                !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    #endregion

    #region 模型 JSON

    private ModelDefinition? LoadDefinition(string path, int depth)
    {
        if (depth > 12) return null;
        var key = ModSchematicAssets.NormalizeModelPath(path);
        if (_definitions.TryGetValue(key, out var cached)) return cached;
        _definitions[key] = null; // 防止父子循环

        var json = _assets.GetModel(key);
        if (json is not { } root)
        {
            _definitions[key] = null;
            return null;
        }

        ModelDefinition definition;
        if (root.TryGetProperty("parent", out var parentTag) && parentTag.GetString() is { } parent)
            definition = (LoadDefinition(parent, depth + 1) ?? new ModelDefinition()).Clone();
        else
            definition = new ModelDefinition();

        if (root.TryGetProperty("textures", out var textures))
            foreach (var texture in textures.EnumerateObject())
                definition.Textures[texture.Name] = texture.Value.GetString() ?? "";

        if (root.TryGetProperty("elements", out var elements))
            definition.Elements = ParseElements(elements);

        _definitions[key] = definition;
        return definition;
    }

    private static List<ElementDefinition> ParseElements(JsonElement elements)
    {
        var result = new List<ElementDefinition>();
        foreach (var element in elements.EnumerateArray())
        {
            var from = ReadVector(element, "from");
            var to = ReadVector(element, "to");
            if (from is null || to is null) continue;

            var definition = new ElementDefinition
            {
                X1 = Math.Min(from.Value.X, to.Value.X),
                Y1 = Math.Min(from.Value.Y, to.Value.Y),
                Z1 = Math.Min(from.Value.Z, to.Value.Z),
                X2 = Math.Max(from.Value.X, to.Value.X),
                Y2 = Math.Max(from.Value.Y, to.Value.Y),
                Z2 = Math.Max(from.Value.Z, to.Value.Z)
            };

            if (element.TryGetProperty("rotation", out var rotation) && rotation.ValueKind == JsonValueKind.Object)
            {
                var origin = ReadVector(rotation, "origin");
                var axis = rotation.TryGetProperty("axis", out var axisTag) ? axisTag.GetString() : "y";
                var angle = rotation.TryGetProperty("angle", out var angleTag) && angleTag.TryGetDouble(out var value)
                    ? value
                    : 0d;
                if (origin is not null && Math.Abs(angle) > 0.001d)
                    definition.Rotation = new ElementRotation(axis?.ToLowerInvariant() ?? "y",
                        origin.Value.X, origin.Value.Y, origin.Value.Z, angle);
            }

            if (element.TryGetProperty("faces", out var faces) && faces.ValueKind == JsonValueKind.Object)
                foreach (var face in faces.EnumerateObject())
                {
                    var direction = face.Name.ToLowerInvariant();
                    if (direction is not ("north" or "south" or "east" or "west" or "up" or "down")) continue;
                    var texture = face.Value.TryGetProperty("texture", out var textureTag)
                        ? textureTag.GetString()
                        : null;
                    if (string.IsNullOrEmpty(texture)) continue;

                    double[]? uv = null;
                    if (face.Value.TryGetProperty("uv", out var uvTag) && uvTag.ValueKind == JsonValueKind.Array &&
                        uvTag.GetArrayLength() == 4)
                        uv = uvTag.EnumerateArray().Select(item => item.GetDouble()).ToArray();

                    var cull = face.Value.TryGetProperty("cullface", out var cullTag) ? cullTag.GetString() : null;
                    var faceRotation = face.Value.TryGetProperty("rotation", out var rotationTag) &&
                                       rotationTag.TryGetInt32(out var rotationValue)
                        ? rotationValue
                        : 0;

                    definition.Faces.Add(new FaceDefinition(direction, texture, uv, cull, faceRotation));
                }

            result.Add(definition);
        }

        return result;
    }

    private static Point3D? ReadVector(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var tag) || tag.ValueKind != JsonValueKind.Array ||
            tag.GetArrayLength() < 3)
            return null;
        var values = tag.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        return new Point3D(values[0], values[1], values[2]);
    }

    #endregion

    #region 元素 → 面

    private static void AddElementQuads(List<FaceQuad> quads, ElementDefinition element, int blockRotationX,
        int blockRotationY, Dictionary<string, string> textures)
    {
        foreach (var face in element.Faces)
        {
            var texture = ResolveTextureReference(face.Texture, textures);
            if (string.IsNullOrEmpty(texture)) continue;

            var corners = GetFaceCorners(element, face.Direction);
            var positions = new Point3D[4];
            var defaultU = new double[4];
            var defaultV = new double[4];

            for (var i = 0; i < 4; i++)
            {
                var (u, v) = GetDefaultUv(face.Direction, corners[i]);
                defaultU[i] = u;
                defaultV[i] = v;
                positions[i] = Transform(corners[i], element.Rotation, blockRotationX, blockRotationY);
            }

            var uMin = defaultU.Min();
            var uMax = defaultU.Max();
            var vMin = defaultV.Min();
            var vMax = defaultV.Max();
            var box = face.Uv is { Length: 4 } uvBox ? uvBox : [uMin, vMin, uMax, vMax];

            var quad = new FaceQuad { Texture = texture, Cull = face.Cull };
            for (var i = 0; i < 4; i++)
            {
                var du = uMax - uMin < 0.0001d ? 0.5d : (defaultU[i] - uMin) / (uMax - uMin);
                var dv = vMax - vMin < 0.0001d ? 0.5d : (defaultV[i] - vMin) / (vMax - vMin);
                switch (face.Rotation & 3)
                {
                    case 1:
                        (du, dv) = (dv, 1d - du);
                        break;
                    case 2:
                        (du, dv) = (1d - du, 1d - dv);
                        break;
                    case 3:
                        (du, dv) = (1d - dv, du);
                        break;
                }

                quad.Uv[i] = new Point(box[0] + du * (box[2] - box[0]), box[1] + dv * (box[3] - box[1]));
            }

            quad.P0 = new Point3D(positions[0].X / 16d, positions[0].Y / 16d, positions[0].Z / 16d);
            quad.P1 = new Point3D(positions[1].X / 16d, positions[1].Y / 16d, positions[1].Z / 16d);
            quad.P2 = new Point3D(positions[2].X / 16d, positions[2].Y / 16d, positions[2].Z / 16d);
            quad.P3 = new Point3D(positions[3].X / 16d, positions[3].Y / 16d, positions[3].Z / 16d);
            quads.Add(quad);
        }
    }

    private static Point3D[] GetFaceCorners(ElementDefinition e, string direction)
    {
        // 顶点按「从外侧看逆时针」排列，保证法线朝外
        return direction switch
        {
            "north" =>
            [
                new Point3D(e.X1, e.Y2, e.Z1), new Point3D(e.X2, e.Y2, e.Z1),
                new Point3D(e.X2, e.Y1, e.Z1), new Point3D(e.X1, e.Y1, e.Z1)
            ],
            "south" =>
            [
                new Point3D(e.X2, e.Y2, e.Z2), new Point3D(e.X1, e.Y2, e.Z2),
                new Point3D(e.X1, e.Y1, e.Z2), new Point3D(e.X2, e.Y1, e.Z2)
            ],
            "west" =>
            [
                new Point3D(e.X1, e.Y2, e.Z2), new Point3D(e.X1, e.Y2, e.Z1),
                new Point3D(e.X1, e.Y1, e.Z1), new Point3D(e.X1, e.Y1, e.Z2)
            ],
            "east" =>
            [
                new Point3D(e.X2, e.Y2, e.Z1), new Point3D(e.X2, e.Y2, e.Z2),
                new Point3D(e.X2, e.Y1, e.Z2), new Point3D(e.X2, e.Y1, e.Z1)
            ],
            "up" =>
            [
                new Point3D(e.X1, e.Y2, e.Z1), new Point3D(e.X1, e.Y2, e.Z2),
                new Point3D(e.X2, e.Y2, e.Z2), new Point3D(e.X2, e.Y2, e.Z1)
            ],
            _ =>
            [
                new Point3D(e.X1, e.Y1, e.Z1), new Point3D(e.X2, e.Y1, e.Z1),
                new Point3D(e.X2, e.Y1, e.Z2), new Point3D(e.X1, e.Y1, e.Z2)
            ]
        };
    }

    /// <summary>没有显式 UV 时，按方块模型规范把顶点位置投影成贴图坐标（0~16）。</summary>
    private static (double U, double V) GetDefaultUv(string direction, Point3D point) => direction switch
    {
        "up" => (point.X, point.Z),
        "down" => (point.X, 16d - point.Z),
        "north" => (16d - point.X, 16d - point.Y),
        "south" => (point.X, 16d - point.Y),
        "west" => (point.Z, 16d - point.Y),
        _ => (16d - point.Z, 16d - point.Y)
    };

    private static Point3D Transform(Point3D point, ElementRotation? elementRotation, int blockRotationX,
        int blockRotationY)
    {
        var result = point;
        if (elementRotation is { } rotation)
        {
            var origin = new Point3D(rotation.OriginX, rotation.OriginY, rotation.OriginZ);
            var offset = result - origin;
            var angle = rotation.Angle * Math.PI / 180d;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            offset = rotation.Axis switch
            {
                "x" => new Vector3D(offset.X, offset.Y * cos - offset.Z * sin, offset.Y * sin + offset.Z * cos),
                "y" => new Vector3D(offset.X * cos - offset.Z * sin, offset.Y, offset.X * sin + offset.Z * cos),
                _ => new Vector3D(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, offset.Z)
            };
            result = origin + offset;
        }

        if (blockRotationX != 0)
        {
            var center = new Point3D(8d, 8d, 8d);
            var offset = result - center;
            var angle = blockRotationX * Math.PI / 180d;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            result = center + new Vector3D(offset.X, offset.Y * cos - offset.Z * sin, offset.Y * sin + offset.Z * cos);
        }

        if (blockRotationY != 0)
        {
            var center = new Point3D(8d, 8d, 8d);
            var offset = result - center;
            var angle = blockRotationY * Math.PI / 180d;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            result = center + new Vector3D(offset.X * cos - offset.Z * sin, offset.Y, offset.X * sin + offset.Z * cos);
        }

        return result;
    }

    private static string? ResolveTextureReference(string reference, Dictionary<string, string> textures)
    {
        var value = reference;
        for (var depth = 0; depth < 8; depth++)
        {
            if (!value.StartsWith("#", StringComparison.Ordinal)) break;
            if (!textures.TryGetValue(value[1..], out var next) || string.IsNullOrEmpty(next)) return null;
            value = next;
        }

        return string.IsNullOrEmpty(value) ? null : ModSchematicAssets.NormalizeTexturePath(value);
    }

    #endregion

    private static string StripNamespace(string name)
    {
        var value = name.Trim();
        if (value.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) value = value[10..];
        var cut = value.IndexOfAny(['[', '{']);
        if (cut >= 0) value = value[..cut];
        return value.ToLowerInvariant();
    }

    private static Dictionary<string, string> ParseProperties(string? properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(properties))
            foreach (var part in properties.Split(','))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                result[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }

        return result;
    }

    #region 数据结构

    public sealed class BlockModel
    {
        /// <summary>是否占满一整格，用于邻居面的剔除。</summary>
        public bool IsFullCube;

        /// <summary>是否可以用「顶 / 底 / 侧三张贴图 + 隐面剔除」的快速路径渲染。</summary>
        public bool UseFastPath;

        public string TopTexture = "";
        public string BottomTexture = "";
        public string SideTexture = "";
        public List<FaceQuad> Quads = [];
    }

    public sealed class FaceQuad
    {
        public string? Cull;
        public Point3D P0;
        public Point3D P1;
        public Point3D P2;
        public Point3D P3;
        public string Texture = "";
        public Point[] Uv = new Point[4]; // 贴图内坐标 0~16
    }

    private sealed class ModelDefinition
    {
        public List<ElementDefinition> Elements = [];
        public Dictionary<string, string> Textures = new(StringComparer.Ordinal);

        public ModelDefinition Clone() => new()
        {
            Elements = Elements,
            Textures = new Dictionary<string, string>(Textures, StringComparer.Ordinal)
        };
    }

    private sealed class ElementDefinition
    {
        public List<FaceDefinition> Faces = [];
        public ElementRotation? Rotation;
        public double X1;
        public double X2;
        public double Y1;
        public double Y2;
        public double Z1;
        public double Z2;
    }

    private sealed class FaceDefinition(string direction, string texture, double[]? uv, string? cull, int rotation)
    {
        public string Direction { get; } = direction;
        public string Texture { get; } = texture;
        public double[]? Uv { get; } = uv;
        public string? Cull { get; } = cull;
        public int Rotation { get; } = rotation;
    }

    private sealed record ElementRotation(string Axis, double OriginX, double OriginY, double OriginZ, double Angle);

    #endregion
}
