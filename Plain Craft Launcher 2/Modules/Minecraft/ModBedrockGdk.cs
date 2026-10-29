using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
///     基岩版 GDK（便携）版本支持。
///     参考 BedrockBoot / BedrockLauncher.Core：GDK 版下发的是加密的 .msixvc（XVC 容器），
///     先用内置内容密钥（CIK）解密并解包到独立目录，之后直接运行目录内的 Minecraft.Windows.exe。
///     与 UWP 版不同，GDK 版不向系统注册包，因此天然支持多版本共存、甚至可以同时运行。
/// </summary>
public static class ModBedrockGdk
{
    public const string ExecutableName = "Minecraft.Windows.exe";

    // 内容密钥（CIK）不写入源码，构建时通过 PCL_BEDROCK_RELEASE_CIK / PCL_BEDROCK_PREVIEW_CIK 注入，
    // 存放于 Secrets，详见 PCL.Core/App/Secrets.cs

    public static bool IsGdkPackage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.Equals(".msixvc", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".xvc", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>目录内是否已有可运行的 GDK 版（存在 Minecraft.Windows.exe）。</summary>
    public static bool IsInstalled(string directory)
    {
        return FindExecutable(directory) is not null;
    }

    /// <summary>在解包目录中查找游戏主程序。</summary>
    public static string? FindExecutable(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        try
        {
            var root = Path.Combine(directory, ExecutableName);
            if (File.Exists(root)) return root;

            foreach (var file in Directory.GetFiles(directory, ExecutableName, SearchOption.AllDirectories))
                return file;
        }
        catch { }

        return null;
    }

    /// <summary>当前构建是否内置了可用于该安装包的内容密钥（CIK）。</summary>
    public static bool HasContentKey(string packagePath) => GetContentKey(packagePath) is { Length: >= 48 };

    private static byte[]? GetContentKey(string packagePath) =>
        packagePath.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
        packagePath.Contains("beta", StringComparison.OrdinalIgnoreCase)
            ? Secrets.BedrockPreviewCik
            : Secrets.BedrockReleaseCik;

    /// <summary>
    ///     解包 .msixvc 到目标目录。
    /// </summary>
    /// <param name="packagePath">.msixvc 文件路径</param>
    /// <param name="targetDirectory">解包目标目录</param>
    /// <param name="onProgress">进度回调：文件名、当前序号、总文件数</param>
    /// <param name="token">取消标记</param>
    public static void Extract(string packagePath, string targetDirectory,
        Action<string, int, int>? onProgress = null, CancellationToken token = default)
    {
        var keyBytes = GetContentKey(packagePath);
        if (keyBytes is not { Length: >= 48 })
            throw new InvalidOperationException("当前构建未包含基岩版 GDK 内容密钥（CIK），无法解包安装包。");

        var key = new XvcContentKey(keyBytes);

        using var container = new XvcContainer(packagePath);
        container.Parse();
        container.Extract(targetDirectory, key, onProgress, token);
    }

    /// <summary>
    ///     直接启动 GDK 版：不经过系统包注册，多版本可同时运行。
    ///     版本目录下的 preload 文件夹中的 DLL 一律注入；BLoader 会先注入 BLoader.dll（均需要 x64 启动器）。
    ///     LeviLamina 通过 lip 给 exe 打补丁后自行加载，这里不再重复注入 mods 目录，否则会重复装载。
    ///     返回游戏进程，供调用方在游戏退出后做清理（例如还原版本隔离）；找不到主程序时返回 null。
    /// </summary>
    public static Process? Launch(string gameDirectory, string? arguments = null, string? versionId = null)
    {
        var exePath = FindExecutable(gameDirectory);
        if (exePath is null) return null;

        var workingDirectory = Path.GetDirectoryName(exePath) ?? gameDirectory;
        var loader = ModBedrockLoader.GetLoader(versionId);
        var mods = new List<string>();

        // BLoader 需要最先进入进程，后续原生 Mod 由它接管装载
        if (loader == ModBedrockLoader.BLoader)
        {
            var bLoaderPath = ModBedrockBLoader.GetDllPath();
            if (File.Exists(bLoaderPath))
                mods.Add(bLoaderPath);
            else
                ModBase.Log($"[Bedrock] 版本 {versionId} 已选择 BLoader，但 BLoader.dll 尚未就绪，本次按原版启动",
                    ModBase.LogLevel.Hint, userSummary: Lang.Text("Bedrock.Loader.BLoader.Missing"));
        }

        var preloads = ModBedrockMods.GetPreloadFiles(gameDirectory);
        mods.AddRange(preloads);

        if (mods.Count > 0)
            ModBase.Log(
                $"[Bedrock] 检测到 {mods.Count} 个待注入 DLL（preload {preloads.Length} 个，加载器 {loader}）: {ModBedrockMods.GetPreloadDirectory(gameDirectory)}",
                ModBase.LogLevel.Normal);

        return ModBedrockMods.Launch(exePath, workingDirectory, arguments, mods);
    }

    /// <summary>结束正在运行的 GDK 版进程（解包前释放文件占用）。</summary>
    public static void StopProcesses()
    {
        foreach (var name in new[] { "Minecraft.Windows", "Minecraft.Win10.DX11" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch { }
        }
    }
}

#region XVC 容器解析（移植自 BedrockLauncher.Core.GdkDecode）

internal sealed class XvcContentKey
{
    public readonly byte[] TweakKey;
    public readonly byte[] DataKey;

    public XvcContentKey(byte[] bytes)
    {
        TweakKey = bytes[16..32];
        DataKey = bytes[32..48];
    }
}

internal sealed class XvcContainer : IDisposable
{
    private const ulong HeaderInclSignatureSize = 12288;
    private const int PageSize = 4096;

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    private ulong _hashTreePageCount;
    private ulong _hashTreePageOffset;
    private ulong _mutableDataOffset;
    private bool _dataIntegrity;
    private bool _resiliency;
    private ulong _hashTreeLevels;
    private ulong _xvdUserDataOffset;
    private bool _hasSegmentMetadata;

    private SegmentMetadataHeader _segmentMetadataHeader;
    private Segment[] _segments = [];
    private string[] _segmentPaths = [];

    private XvcInfo _xvcInfo;
    private XvcRegionHeader[] _xvcRegions = [];
    private XvcUpdateSegment[] _xvcUpdateSegments = [];

    private UserDataHeader _userDataHeader;
    private readonly System.Collections.Generic.Dictionary<string, byte[]> _userDataContents = [];

    private int _hashEntryLength;

    private MsiXvdHeader Header { get; set; }

    public bool IsEncrypted { get; private set; }

    public XvcContainer(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("找不到基岩版安装包", filePath);

        _stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream);
    }

    public void Parse()
    {
        _stream.Position = 0;
        ParseFileHeader();
        _resiliency = Header.Volumes.HasFlag(XvdVolumeAttributes.ResiliencyEnabled);
        _dataIntegrity = !Header.Volumes.HasFlag(XvdVolumeAttributes.DataIntegrityDisabled);
        _hashTreePageCount = CalculateNumberHashPages(out _hashTreeLevels, Header.NumberOfHashedPages, _resiliency);
        _mutableDataOffset = PageToOffset(Header.EmbeddedXvdPageCount) + HeaderInclSignatureSize;
        _hashTreePageOffset = Header.MutableDataLength + _mutableDataOffset;
        _xvdUserDataOffset = (_dataIntegrity ? PageToOffset(_hashTreePageCount) : 0) + _hashTreePageOffset;

        ParseUserData();
        if (_userDataContents.ContainsKey("SegmentMetadata.bin"))
            ParseSegment();
        ParseArea();
    }

    private void ParseFileHeader()
    {
        var size = Marshal.SizeOf<MsiXvdHeader>();
        Header = ReadStruct<MsiXvdHeader>(_reader.ReadBytes(size));
        IsEncrypted = !Header.Volumes.HasFlag(XvdVolumeAttributes.EncryptionDisabled);
        _hashEntryLength = IsEncrypted ? 20 : 24;
    }

    private void ParseUserData()
    {
        _stream.Position = (long)_xvdUserDataOffset;
        var data = new byte[Header.UserDataLength];
        ReadFully(_stream, data);
        using var reader = new BinaryReader(new MemoryStream(data));

        _userDataHeader = ReadStruct<UserDataHeader>(reader.ReadBytes(Marshal.SizeOf<UserDataHeader>()));
        if (_userDataHeader.Type != XvdUserDataType.PackageFiles) return;

        reader.BaseStream.Position = _userDataHeader.Length;
        var filesHeader = ReadStruct<UserDataPackageFilesHeader>(
            reader.ReadBytes(Marshal.SizeOf<UserDataPackageFilesHeader>()));

        var fileCount = (int)filesHeader.FileCount;
        var entrySize = Marshal.SizeOf<UserDataPackageFileEntry>();
        var entries = ReadStructs<UserDataPackageFileEntry>(
            reader.ReadBytes(entrySize * fileCount), fileCount);

        foreach (var entry in entries)
        {
            reader.BaseStream.Position = _userDataHeader.Length + entry.Offset;
            var content = reader.ReadBytes((int)entry.Size);
            _userDataContents[entry.FilePath] = content;
        }
    }

    private void ParseSegment()
    {
        using var reader = new BinaryReader(new MemoryStream(_userDataContents["SegmentMetadata.bin"]));
        _segmentMetadataHeader = ReadStruct<SegmentMetadataHeader>(
            reader.ReadBytes(Marshal.SizeOf<SegmentMetadataHeader>()));
        _hasSegmentMetadata = true;

        var count = (int)_segmentMetadataHeader.SegmentCount;
        _segments = ReadStructs<Segment>(reader.ReadBytes(Marshal.SizeOf<Segment>() * count), count);
        _segmentPaths = new string[count];

        var pathsOffset = _segmentMetadataHeader.HeaderLength + _segmentMetadataHeader.SegmentCount * 16u;
        for (var i = 0; i < _segments.Length; i++)
        {
            reader.BaseStream.Position = pathsOffset + _segments[i].PathOffset;
            var bytes = reader.ReadBytes(_segments[i].PathLength * 2);
            _segmentPaths[i] = new string(MemoryMarshal.Cast<byte, char>(bytes));
        }
    }

    private void ParseArea()
    {
        var offset = PageToOffset(Header.UserDataPageCount) + _xvdUserDataOffset;
        _stream.Position = (long)offset;

        var data = new byte[Header.XvcDataLength];
        ReadFully(_stream, data);
        using var reader = new BinaryReader(new MemoryStream(data));

        _xvcInfo = ReadStruct<XvcInfo>(reader.ReadBytes(Marshal.SizeOf<XvcInfo>()));
        if (_xvcInfo.Version < 1) return;

        var regionSize = Marshal.SizeOf<XvcRegionHeader>();
        _xvcRegions = ReadStructs<XvcRegionHeader>(
            reader.ReadBytes(regionSize * (int)_xvcInfo.RegionCount), (int)_xvcInfo.RegionCount);

        var updateSize = Marshal.SizeOf<XvcUpdateSegment>();
        _xvcUpdateSegments = ReadStructs<XvcUpdateSegment>(
            reader.ReadBytes(updateSize * (int)_xvcInfo.UpdateSegmentCount), (int)_xvcInfo.UpdateSegmentCount);
    }

    public void Extract(string outputDirectory, XvcContentKey key,
        Action<string, int, int>? onProgress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_hasSegmentMetadata || _segments.Length == 0)
            throw new InvalidOperationException("安装包内没有文件段信息，可能不是有效的 GDK 包");
        if (_xvcUpdateSegments.Length == 0)
            throw new InvalidOperationException("安装包内没有可解包的区域信息");

        // Validate the complete manifest before writing any package contents.
        foreach (var path in _segmentPaths)
            _ = BedrockFileSystem.GetChildPath(outputDirectory, path);
        Directory.CreateDirectory(outputDirectory);

        using var decoder = new XvcDecoder(key);
        var firstSegmentOffset = PageToOffset(_xvcUpdateSegments[0].PageNum);

        foreach (var region in _xvcRegions)
        {
            token.ThrowIfCancellationRequested();

            // 文件段可能被拆分到多个区域（例如主程序、DLL、资源各占一个区域），必须逐个解包；
            // 元数据区域（XVC-HD / EXVD / MDU / MD / FS-MD）的 FirstSegmentIndex 同样是 0，
            // 但起始偏移与 0 号段的偏移不同，据此排除。
            if (region.FirstSegmentIndex >= _segments.Length || region.Length == 0) continue;
            if (region.FirstSegmentIndex == 0 && firstSegmentOffset != region.Offset) continue;

            ExtractRegion(outputDirectory, decoder, (uint)region.Id, region.Offset, region.Length,
                region.FirstSegmentIndex, IsEncrypted && region.KeyId != ushort.MaxValue, onProgress, token);
        }
    }

    private void ExtractRegion(string outputDirectory, XvcDecoder decoder, uint regionId,
        ulong regionStartOffset, ulong regionLength, uint startSegmentIndex, bool shouldDecrypt,
        Action<string, int, int>? onProgress, CancellationToken token)
    {
        Span<byte> tweak = stackalloc byte[16];
        if (shouldDecrypt)
        {
            MemoryMarshal.Cast<byte, uint>(tweak)[1] = regionId;
            Header.VdUid.AsSpan(0, 8).CopyTo(tweak[8..]);
        }

        var buffer = new byte[1024 * 1024];
        var hashBuffer = new byte[1024 * 1024];
        var decryptBuffer = new byte[4096];
        var plainBuffer = new byte[4096];

        var needDataRead = true;
        var needHashRead = _dataIntegrity;
        var bufferOffset = 0;
        long regionPosition = (long)regionStartOffset;

        var hashFileOffset = (long)CalculateHashEntryBlockOffset(
            GetPageOffset(regionStartOffset - _xvdUserDataOffset), out var hashEntryId);
        var hashEntryOffset = (int)(hashEntryId * 24);
        var segmentIndex = startSegmentIndex;
        var pageCounter = 0;
        var totalPages = (long)GetPageOffset(regionLength);

        while (_segments.Length > segmentIndex && totalPages > pageCounter)
        {
            token.ThrowIfCancellationRequested();
            var fileSize = _segments[segmentIndex].FileSize;
            var relativePath = _segmentPaths[segmentIndex];
            var path = BedrockFileSystem.GetChildPath(outputDirectory, relativePath);
            var directory = Path.GetDirectoryName(path);
            if (directory is not null) Directory.CreateDirectory(directory);

            using (var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var remaining = fileSize;
                do
                {
                    token.ThrowIfCancellationRequested();
                    var writeLength = (int)Math.Min(remaining, PageSize);

                    if (needHashRead)
                    {
                        _stream.Position = hashFileOffset;
                        ReadFully(_stream, hashBuffer);
                        needHashRead = false;
                    }

                    if (needDataRead)
                    {
                        _stream.Position = regionPosition;
                        ReadFully(_stream, buffer);
                        needDataRead = false;
                    }

                    var page = buffer.AsSpan(bufferOffset, PageSize);
                    if (_dataIntegrity)
                    {
                        var entry = hashBuffer.AsSpan(hashEntryOffset, 24);
                        if (shouldDecrypt)
                            MemoryMarshal.Cast<byte, uint>(tweak)[0] =
                                MemoryMarshal.Cast<byte, uint>(entry[_hashEntryLength..])[0];

                        hashEntryOffset += 24;
                        hashEntryId++;
                        if (hashEntryId == 170)
                        {
                            hashEntryId = 0;
                            hashEntryOffset += 16;
                        }

                        if (hashEntryOffset == hashBuffer.Length)
                        {
                            hashFileOffset += hashEntryOffset;
                            hashEntryOffset = 0;
                            hashEntryId = 0;
                            needHashRead = true;
                        }
                    }

                    if (shouldDecrypt)
                    {
                        page.CopyTo(decryptBuffer);
                        decoder.Decrypt(decryptBuffer, plainBuffer, tweak);
                        file.Write(plainBuffer, 0, writeLength);
                    }
                    else
                    {
                        file.Write(page[..writeLength]);
                    }

                    remaining -= (uint)writeLength;
                    bufferOffset += PageSize;
                    if (bufferOffset == buffer.Length)
                    {
                        regionPosition += bufferOffset;
                        bufferOffset = 0;
                        needDataRead = true;
                    }

                    pageCounter++;
                } while (remaining != 0);
            }

            onProgress?.Invoke(relativePath, (int)segmentIndex + 1, _segments.Length);
            segmentIndex++;
        }
    }

    private ulong CalculateHashEntryBlockOffset(ulong blockNo, out ulong hashEntryId)
    {
        var index = ComputeHashBlockIndex(Header.Kind, _hashTreeLevels, Header.NumberOfHashedPages,
            blockNo, 0, out hashEntryId, _resiliency);
        return _hashTreePageOffset + PageToOffset(index);
    }

    private static ulong CalculateNumberHashPages(out ulong hashTreeLevels, ulong hashedPagesCount, bool resilient)
    {
        var count = (hashedPagesCount + 170 - 1) / 170;
        hashTreeLevels = 1;
        if (count > 1)
        {
            ulong previous = 2;
            while (previous > 1)
            {
                ulong next = 0;
                switch (hashTreeLevels)
                {
                    case 0: next = (hashedPagesCount + 170 - 1) / 170; break;
                    case 1: next = (hashedPagesCount + 28900 - 1) / 28900; break;
                    case 2: next = (hashedPagesCount + 4913000 - 1) / 4913000; break;
                    case 3: next = (hashedPagesCount + 835210000 - 1) / 835210000; break;
                }

                previous = next;
                hashTreeLevels++;
                count += next;
            }
        }

        if (resilient) count *= 2;
        return count;
    }

    private static ulong ComputeHashBlockIndex(XvdKind imageType, ulong hashTreeDepth, ulong totalHashedPages,
        ulong dataBlockIndex, uint currentHashLevel, out ulong entryIndexInHashBlock, bool isResilient = false)
    {
        entryIndexInHashBlock = 0;
        if (imageType > XvdKind.Dynamic || currentHashLevel > 3)
            return 65535;

        entryIndexInHashBlock = currentHashLevel == 0
            ? dataBlockIndex % 170
            : dataBlockIndex / LevelMultiplier(currentHashLevel) % 170;

        if (currentHashLevel == 3) return 0;

        var result = dataBlockIndex / LevelMultiplier(currentHashLevel + 1);
        hashTreeDepth -= currentHashLevel + 1;

        if (currentHashLevel == 0 && hashTreeDepth != 0)
        {
            result += (totalHashedPages + LevelMultiplier(2) - 1) / LevelMultiplier(2);
            hashTreeDepth--;
        }

        if (currentHashLevel is 0 or 1 && hashTreeDepth != 0)
        {
            result += (totalHashedPages + LevelMultiplier(3) - 1) / LevelMultiplier(3);
            hashTreeDepth--;
        }

        if (hashTreeDepth != 0)
            result += (totalHashedPages + LevelMultiplier(4) - 1) / LevelMultiplier(4);

        if (isResilient) result *= 2;
        return result;

        static ulong LevelMultiplier(ulong level) => (ulong)Math.Pow(170.0, level);
    }

    private static ulong PageToOffset(ulong pages) => pages * PageSize;
    private static ulong GetPageOffset(ulong bytes) => bytes / PageSize;

    private static void ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0) break;
            total += read;
        }
    }

    private static T ReadStruct<T>(ReadOnlySpan<byte> bytes) where T : struct
    {
        var array = bytes.ToArray();
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static T[] ReadStructs<T>(ReadOnlySpan<byte> bytes, int count) where T : struct
    {
        var array = bytes.ToArray();
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        try
        {
            var address = handle.AddrOfPinnedObject();
            var size = Marshal.SizeOf<T>();
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = Marshal.PtrToStructure<T>(address + size * i);
            return result;
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}

/// <summary>
///     XTS-AES 解密器（软件实现，按 4KB 块批量处理）。
/// </summary>
internal sealed class XvcDecoder : IDisposable
{
    private readonly Aes _tweakAes;
    private readonly Aes _dataAes;

    public XvcDecoder(XvcContentKey key)
    {
        _tweakAes = CreateAes(key.TweakKey);
        _dataAes = CreateAes(key.DataKey);
    }

    public int Decrypt(ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<byte> tweakIv)
    {
        if (tweakIv.Length < 16) return 0;

        var length = Math.Min(input.Length, output.Length);
        if (length == 0) return 0;

        var blockCount = length / 16;
        if (blockCount == 0) return 0;

        Span<byte> tweak = stackalloc byte[16];
        _tweakAes.EncryptEcb(tweakIv[..16], tweak, PaddingMode.None);

        var inputBuffer = new byte[blockCount * 16];
        var outputBuffer = new byte[blockCount * 16];
        var tweaks = new byte[blockCount * 16];

        for (var i = 0; i < blockCount; i++)
        {
            var offset = i * 16;
            tweak.CopyTo(tweaks.AsSpan(offset, 16));
            for (var j = 0; j < 16; j++)
                inputBuffer[offset + j] = (byte)(input[offset + j] ^ tweak[j]);
            MultiplyGf128(tweak);
        }

        _dataAes.DecryptEcb(inputBuffer, outputBuffer, PaddingMode.None);

        for (var i = 0; i < blockCount * 16; i++)
            output[i] = (byte)(outputBuffer[i] ^ tweaks[i]);

        return blockCount * 16;
    }

    private static Aes CreateAes(byte[] key)
    {
        var aes = Aes.Create();
        aes.KeySize = 128;
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        return aes;
    }

    /// <summary>GF(2^128) 上乘以 x（与 BedrockLauncher.Core 的 Gf128MulSoftware 等价）。</summary>
    private static void MultiplyGf128(Span<byte> iv)
    {
        var high = (iv[15] >> 7) & 1;
        var low = (iv[7] >> 7) & 1;

        for (var i = 7; i > 0; i--)
            iv[i] = (byte)((iv[i] << 1) | (iv[i - 1] >> 7));
        iv[0] <<= 1;

        for (var i = 15; i > 8; i--)
            iv[i] = (byte)((iv[i] << 1) | (iv[i - 1] >> 7));
        iv[8] <<= 1;

        if (low != 0) iv[8] ^= 1;
        if (high != 0) iv[0] ^= 135;
    }

    public void Dispose()
    {
        _tweakAes.Dispose();
        _dataAes.Dispose();
    }
}

#region XVC 结构定义

[Flags]
internal enum XvdVolumeAttributes : uint
{
    ReadOnly = 1,
    EncryptionDisabled = 2,
    DataIntegrityDisabled = 4,
    LegacySectorSize = 8,
    ResiliencyEnabled = 0x10,
    SraReadOnly = 0x20,
    RegionIdInXts = 0x40,
    EraSpecific = 0x80
}

internal enum XvdKind : uint
{
    Fixed,
    Dynamic
}

internal enum XvdUserDataType : uint
{
    PackageFiles
}

[StructLayout(LayoutKind.Sequential)]
internal struct ExtEntry
{
    public uint Code;
    public uint Length;
    public ulong Offset;
    public uint DataLength;
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct MsiXvdHeader
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
    public byte[] Signature;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public char[] Magic;

    public XvdVolumeAttributes Volumes;
    public uint FormatVersion;
    public long FileTimeCreated;
    public ulong DriveSize;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] VdUid;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] UdUid;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] TopHashBlockHash;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] OriginalXvcDataHash;

    public XvdKind Kind;
    public uint Category;
    public uint EmbeddedXvdLength;
    public uint UserDataLength;
    public uint XvcDataLength;
    public uint DynamicHeaderLength;
    public uint BlockSize;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public ExtEntry[] ExtEntries;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public ushort[] Capabilities;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] PeCatalogHash;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] EmbeddedXvdPdUid;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] Reserved13C;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] KeyMaterial;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] UserDataHash;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public char[] SandboxId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ProductId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] PdUid;

    public ushort PackageVersion1;
    public ushort PackageVersion2;
    public ushort PackageVersion3;
    public ushort PackageVersion4;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public ushort[] PeCatalogCaps;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    public byte[] PeCatalogs;

    public uint WriteableExpirationDate;
    public uint WriteablePolicyFlags;
    public uint PersistentLocalStorageSize;
    public byte MutableDataPageCount;
    public byte Unknown271;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] Unknown272;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public byte[] Reserved282;

    public long SequenceNumber;
    public ushort Unknown1;
    public ushort Unknown2;
    public ushort Unknown3;
    public ushort Unknown4;
    public uint OdkKeyslotId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2900)]
    public byte[] Reserved2A0;

    public ulong ResilientDataOffset;
    public uint ResilientDataLength;

    public ulong MutableDataLength => MutableDataPageCount * 4096UL;
    public ulong UserDataPageCount => BytesToPages(UserDataLength);
    public ulong XvcInfoPageCount => BytesToPages(XvcDataLength);
    public ulong EmbeddedXvdPageCount => BytesToPages(EmbeddedXvdLength);
    public ulong DynamicHeaderPageCount => BytesToPages(DynamicHeaderLength);
    public ulong DrivePageCount => BytesToPages(DriveSize);

    public ulong NumberOfHashedPages =>
        DrivePageCount + UserDataPageCount + XvcInfoPageCount + DynamicHeaderPageCount;

    private static ulong BytesToPages(ulong bytes) => (bytes + 4095) / 4096;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct SegmentMetadataHeader
{
    public uint Magic;
    public uint Version0;
    public uint Version1;
    public uint HeaderLength;
    public uint SegmentCount;
    public uint FilePathsLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] PDUID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public byte[] Unknown;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1, Size = 16)]
internal struct Segment
{
    public ushort Flags;
    public ushort PathLength;
    public uint PathOffset;
    public ulong FileSize;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct UserDataHeader
{
    public uint Length;
    public uint Version;
    public XvdUserDataType Type;
    public uint Unknown;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct UserDataPackageFilesHeader
{
    public uint Version;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string PackageFullName;

    public uint FileCount;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct UserDataPackageFileEntry
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string FilePath;

    public uint Size;
    public uint Offset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XvcEncryptionKeyId
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] KeyId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XvcInfo
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ContentId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 192)]
    public XvcEncryptionKeyId[] EncryptionKeyIds;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] Description;

    public uint Version;
    public uint RegionCount;
    public uint Flags;
    public ushort PaddingD1C;
    public ushort KeyCount;
    public uint UnknownD20;
    public uint InitialPlayRegionId;
    public ulong InitialPlayOffset;
    public long FileTimeCreated;
    public uint PreviewRegionId;
    public uint UpdateSegmentCount;
    public ulong PreviewOffset;
    public ulong UnusedSpace;
    public uint RegionSpecifierCount;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 84)]
    public byte[] ReservedD54;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct XvcRegionHeader
{
    public uint Id;
    public ushort KeyId;
    public ushort Padding6;
    public uint Flags;
    public uint FirstSegmentIndex;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string Description;

    public ulong Offset;
    public ulong Length;
    public ulong Hash;
    public ulong Unknown68;
    public ulong Unknown70;
    public ulong Unknown78;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XvcUpdateSegment
{
    public uint PageNum;
    public ulong Hash;
}

#endregion
#endregion
