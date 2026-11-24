using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL;

/// <summary>
/// BLoader GDK XUser Bridge 的独立启动器端实现。
/// 调用方负责先取得短时效 Xbox/GDK 预认证数据，再调用
/// <see cref="LaunchSuspendedAsync"/> 启动 Minecraft.Windows.exe。
/// </summary>
public static class BLoaderXUserBridge
{
    private const string PipePrefix = "BMCBL.XUser.";
    private const uint ProtocolVersion = 1;
    private const int HeaderSize = 80;
    private const int MaxPayloadSize = 256 * 1024;

    /// <summary>由独立的 Xbox/GDK 登录模块写入，未登录时保持 null。</summary>
    public static Session? CurrentSession { get; private set; }

    public static void SetSession(Session? session) => CurrentSession = session;

    public sealed record Session(
        [property: JsonPropertyName("ecc_private_blob_b64")] string EccPrivateBlobBase64,
        [property: JsonPropertyName("xbl_xuid")] string XblXuid,
        [property: JsonPropertyName("xbl_gamertag")] string XblGamertag,
        [property: JsonPropertyName("xbl_token")] string XblToken,
        [property: JsonPropertyName("xbl_uhs")] string XblUhs,
        [property: JsonPropertyName("xbl_token_expiry_epoch")] string XblTokenExpiryEpoch,
        [property: JsonPropertyName("sisu_token")] string SisuToken,
        [property: JsonPropertyName("sisu_uhs")] string SisuUhs,
        [property: JsonPropertyName("sisu_rp")] string? SisuRelyingParty,
        [property: JsonPropertyName("sisu_expiry_epoch")] string SisuExpiryEpoch,
        [property: JsonPropertyName("mp_token")] string MultiplayerToken,
        [property: JsonPropertyName("mp_uhs")] string MultiplayerUhs,
        [property: JsonPropertyName("mp_rp")] string? MultiplayerRelyingParty,
        [property: JsonPropertyName("mp_expiry_epoch")] string MultiplayerExpiryEpoch,
        [property: JsonPropertyName("realms_token")] string RealmsToken,
        [property: JsonPropertyName("realms_uhs")] string RealmsUhs,
        [property: JsonPropertyName("realms_rp")] string? RealmsRelyingParty,
        [property: JsonPropertyName("realms_expiry_epoch")] string RealmsExpiryEpoch,
        [property: JsonPropertyName("lic_token")] string? LicensingToken = null,
        [property: JsonPropertyName("lic_uhs")] string? LicensingUhs = null,
        [property: JsonPropertyName("lic_rp")] string? LicensingRelyingParty = null,
        [property: JsonPropertyName("lic_expiry_epoch")] string? LicensingExpiryEpoch = null,
        [property: JsonPropertyName("xbl_age_group")] string? AgeGroup = null,
        [property: JsonPropertyName("xbl_privileges")] string? Privileges = null);

    public static async Task<Process> LaunchSuspendedAsync(
        string executable,
        string arguments,
        Session session,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Minecraft GDK 进程。");
        _ = Task.Run(() => ServeAsync(process, session, cancellationToken), cancellationToken);
        return process;
    }

    /// <summary>为已经创建的 Minecraft PID 提供一次性 XUser 会话。</summary>
    public static async Task ServeAsync(Process minecraft, Session session, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(session, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        if (payload.Length is 0 or > MaxPayloadSize) throw new InvalidDataException("XUser 会话载荷超出 BLoader 限制。");

        using var pipe = new NamedPipeServerStream(
            PipePrefix + minecraft.Id,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        await pipe.WaitForConnectionAsync(cancellationToken);

        var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expires = issued + 90;
        var header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("BMCBLXU1").CopyTo(header, 0);
        WriteUInt32(header, 8, ProtocolVersion);
        WriteUInt32(header, 12, (uint)minecraft.Id);
        WriteUInt32(header, 16, (uint)Environment.ProcessId);
        WriteUInt64(header, 24, (ulong)issued);
        WriteUInt64(header, 32, (ulong)expires);
        WriteUInt32(header, 40, (uint)payload.Length);
        SHA256.HashData(payload).CopyTo(header, 48);
        await pipe.WriteAsync(header, cancellationToken);
        await pipe.WriteAsync(payload, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 在 CreateProcessW 返回且游戏仍处于挂起状态时创建管道。
    /// BLoader 只能在这个时机可靠地发现一次性 XUser 会话。
    /// </summary>
    public static void BeginForSuspendedProcess(int processId, Session session)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(session,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        if (payload.Length is 0 or > MaxPayloadSize)
            throw new InvalidDataException("XUser 会话载荷超出 BLoader 限制。");

        var pipe = new NamedPipeServerStream(
            PipePrefix + processId,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        _ = Task.Run(async () =>
        {
            try
            {
                await pipe.WaitForConnectionAsync();
                var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var header = BuildHeader((uint)processId, (uint)Environment.ProcessId, issued, issued + 90, payload);
                await pipe.WriteAsync(header);
                await pipe.WriteAsync(payload);
                await pipe.FlushAsync();
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "BLoader XUser 管道发送失败", ModBase.LogLevel.Debug);
            }
            finally
            {
                pipe.Dispose();
                CryptographicOperations.ZeroMemory(payload);
            }
        });
    }

    private static byte[] BuildHeader(uint targetPid, uint launcherPid, long issued, long expires, byte[] payload)
    {
        var header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("BMCBLXU1").CopyTo(header, 0);
        WriteUInt32(header, 8, ProtocolVersion);
        WriteUInt32(header, 12, targetPid);
        WriteUInt32(header, 16, launcherPid);
        WriteUInt64(header, 24, (ulong)issued);
        WriteUInt64(header, 32, (ulong)expires);
        WriteUInt32(header, 40, (uint)payload.Length);
        SHA256.HashData(payload).CopyTo(header, 48);
        return header;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) => BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), value);
    private static void WriteUInt64(byte[] buffer, int offset, ulong value) => BitConverter.TryWriteBytes(buffer.AsSpan(offset, 8), value);
}
