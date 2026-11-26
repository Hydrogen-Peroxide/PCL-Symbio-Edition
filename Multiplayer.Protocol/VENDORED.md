# PaperConnect 官方源码引入说明

本目录下除 `PaperConnectCore` / `AclConfig` 之外的 `.cs` 文件，是从
[Round-Studio/PaperConnect](https://github.com/Round-Studio/PaperConnect) **原样引入**的（命名空间保持不变，
方便以后再从上游同步），用于基岩版联机（`ModPaperConnect` 负责编排）。

## 来源

| 项 | 值 |
|---|---|
| 仓库 | https://github.com/Round-Studio/PaperConnect |
| 分支 / 提交 | `master` @ `fafad81819fd9f040c455739542b3e62d368ac1f` |
| 协议规范 | https://docs.roundstudio.top/docs/standard/paperconnect |
| 引入日期 | 2026-11-25 |

## 引入的文件

| 文件 | 上游路径 | 改动 |
|---|---|---|
| `RoomCodeGenerator.cs` | `src/PaperConnect.Core/Room/RoomCodeGenerator.cs` | 无（逐行一致） |
| `PaperConnectServer.cs` | `src/PaperConnect.Core/PaperConnectServer.cs` | 仅补 `using`（本工程未开 ImplicitUsings） |
| `PaperConnectClient.cs` | `src/PaperConnect.Core/PaperConnectClient.cs` | 补 `using`；响应改为「读到 EOF」（见下）；心跳对空响应做空引用保护 |
| `AgreementEntry.cs` | `src/PaperConnect.Core/Entry/AgreementEntry.cs` | 无 |
| `PeerInfo.cs` | `src/PaperConnect.Core/Entry/Easytier/PeerInfo.cs` | 无 |
| `EnvironmentLabel.cs` | `src/PaperConnect.Core/Module/Global/EnvironmentLabel.cs` | 无 |
| `IPaperConnect.cs` | `src/PaperConnect.Core/Interface/IPaperConnect.cs` | 无 |

## 没有引入的部分及原因

- `PaperConnectCore.cs`：把 `config.toml` 写到**当前工作目录**、用 PowerShell `-Verb RunAs` 提权拉起 EasyTier、
  发现逻辑依赖 EasyTier 的控制台日志 `new peer added.`、`Stop()` 会杀掉**所有** `easytier-core.exe`。
  这些在启动器里都不合适，因此编排层（`Modules/Minecraft/ModPaperConnect.cs`）自己实现，
  其余逻辑（联机码、控制面协议、DTO）仍然全部走官方代码。
- `Utils/AclConfig.cs`：依赖 NuGet 包 `Tomlyn`，用于生成 EasyTier 的 `acl_v1` 端口白名单。
  本次没有生成 ACL（私有网络已由 network-secret 隔离），因此不引入，也就不需要新增 Tomlyn 依赖。
  后续要补这条加固链路时，再把该文件连同 `Tomlyn` 一起加进来。

## 两处行为修正

1. **响应读取**（`PaperConnectClient.SendRequestAsync`）：上游用 `while (stream.DataAvailable)` 判断读完，
   对端稍慢就会读到 0 字节并抛 `No response from server.`。服务端发完 JSON 就 `client.Close()`，
   按「读到 EOF」才是协议语义，已改为 `while ((read = await stream.ReadAsync(...)) > 0)`。
2. **心跳空响应**（`StartHeartbeat`）：上游直接 `players.Players`，`players` 为 null 时空引用，已加保护。

## 许可提醒

上游仓库**没有 LICENSE 文件**（GitHub API 返回 `license: null`），而本项目（PCL / PCL Symbio Edition）是 GPL-3.0。
严格来说，引入这段代码需要上游明确授权；此处按「同一作者生态、本地自用」处理，
若要公开发布或合并回上游，建议先在 PaperConnect 仓库提 issue 请作者补一个 GPL 兼容许可。
