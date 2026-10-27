**简体中文** | [English](README-EN.md) | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Symbio Edition

[![Stars](https://img.shields.io/github/stars/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&logo=github&label=stars&labelColor=444444&color=eac54f)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/stargazers)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Hydrogen-Peroxide/PCL-Symbio-Edition/build-test.yml?style=for-the-badge)

[![Pull requests](https://img.shields.io/github/issues-pr/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&label=pull%20requests&labelColor=444444&color=1F883D&logo=github)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/pulls)

[下载 PCL SE](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases) |
[上游存储库](https://github.com/Meloong-Git/PCL)

[提交问题](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/issues) |
[贡献指南](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/CONTRIBUTING.md)

</div>

PCL SE（Plain Craft Launcher Symbio Edition）是由 Hydrogen-Peroxide 维护的第三方 Minecraft 启动器，基于 PCL 开源代码开发，在保留原有交互习惯的基础上补齐了基岩版支持、实例管理与资源下载等能力。

- 当前版本：`2.17.11`
- 上游基线：PCL `2.12.1`
- 技术栈：C# / WPF / .NET 10

PCL SE 与 PCL 官方版没有隶属关系，请不要向官方仓库反馈 PCL SE 的问题。

## 功能概览

### Java 版

- 官方正式版、快照与历史版本一键安装，自动补全依赖并选择合适的 Java 运行环境
- 8 种安装选项：Forge、NeoForge、Fabric、Legacy Fabric、OptiFine、LiteLoader、Cleanroom、LabyMod
- 支持整合包安装与模组加载器切换

### 基岩版

- 读取系统中已安装的 Minecraft 基岩版，区分 UWP 旧版与 GDK 新版
- 支持导入安装包并解包到独立目录，不影响其他版本
- 内置 Xbox 账号登录，登录后可直接启动
- 按版本类型筛选，支持按版本号搜索与本地管理

### 资源下载

- CurseForge 与 Modrinth 双源检索
- 覆盖模组、整合包、光影、资源包、数据包与世界存档，下载后自动放入对应目录

### 实例管理

- 每个实例拥有独立的模组、存档、截图与配置
- 单个模组可随时禁用，便于排查冲突
- 存档与截图集中浏览、备份，并可一键打开所在目录
- 实例内维护常用服务器列表，支持整体打包导出

### 使用体验

- 简体中文与 English (US) 提供完整界面资源；繁體中文、English (UK)、日本語、Français、Español 目前仅有语言名称等占位资源，界面文本回退为简体中文
- 主题配色与背景可自定义，页面切换与列表滚动带有过渡动画
- 自动扫描本机 Java，减少手动配置
- 启动失败时对日志进行分析，给出可读的原因与建议

> 本分支不包含联机大厅功能，相关模块已从代码库中移除，目前没有已确认的恢复排期。

## 支持平台

| 操作系统 | 支持情况 | 环境要求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | 完整支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | 理论能跑，酌情提供支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | 不支持 | / |
| macOS / Linux / 其他操作系统 | 仅支持交叉编译开发，无法直接运行 | [.NET 10 SDK](https://get.dot.net/10) |

**完整支持**：尽可能提供一切相关支持，但必须确保启动器为最新版本。

**理论能跑，酌情提供支持**：PCL SE 应该可以在这些平台上运行，但不保证功能完全可用。你可能需要升级到完整支持的系统版本以获得进一步技术支持。

**不支持**：PCL SE 在这些平台的可用性较低，甚至无法启动。请升级操作系统以使用 PCL SE。

**仅支持交叉编译开发**：源代码可以在 macOS 与 Linux 上编译，但无法直接运行。作为开发者，你可以在这些平台上开发，再将编译产物转移到 Windows 测试。

**注**：仅对最新版本的启动器提供支持。取决于部分问题的特殊性（如系统不完整），有时你仍然必须升级操作系统以继续获得支持。你仍然可以尝试在不受支持的系统上使用 PCL SE，但可能会遇到很多额外问题。

## 下载与使用

1. 从 [Releases](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases) 下载最新版本的发布包
2. 解压到任意目录（启动器为绿色便携版，不会写入系统目录）
3. 运行 `Plain Craft Launcher 2.exe`，选择 Java 版或基岩版后即可启动游戏

官方发布包为框架依赖版本，需要预先安装 [.NET 10 Desktop Runtime](https://get.dot.net/10)；若使用自包含方式自行构建，则无需额外安装运行时。

## 从源码构建

需要 [.NET 10 SDK](https://get.dot.net/10)。SDK 基线由 [global.json](global.json) 固定为 `10.0.403`，允许同一功能带的补丁更新；CI 安装该基线版本。请在仓库根目录执行：

```powershell
# 与 CI 一致的框架依赖发布
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained

# 自包含单文件发布（内置运行时，产物体积更大）
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true --self-contained true -o ./publish
```

将 `-p:Platform=x64` 换成 `-p:Platform=ARM64` 即可构建 ARM64 版本。

可用配置（`-c`）：`Release`、`Beta`、`Debug`、`CI`；可用平台（`-p:Platform`）：`x64`、`ARM64`、`AnyCPU`。

仅需验证编译是否通过时，可直接执行 `dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Release -p:Platform=x64`。

测试命令、离线测试范围与发布配置见 [贡献指南](CONTRIBUTING.md#本地构建与测试)。解决方案默认不构建核心测试项目，需要单独运行该项目。

## 项目结构

| 目录 | 说明 |
|---|---|
| `Plain Craft Launcher 2/` | WPF 主程序：界面、页面与 Minecraft 启动逻辑 |
| `PCL.Core/` | 核心库：配置、网络与下载、Java、存档、动画、本地化、日志等 |
| `PCL.Core.SourceGenerators/` | 配置与依赖注入相关的源生成器 |
| `PCL.Core.Test/` | PCL.Core 的单元测试 |
| `.github/` | CI 构建与发布工作流 |

## 与上游的差异

- 新增基岩版管理：版本检测、安装包导入、Xbox 账号登录与本地筛选
- 移除联机大厅及配套模块，不再提供大厅与联机相关入口
- 使用独立版本号（`2.17.11`），上游基线为 PCL `2.12.1`

## 声明

> 以下声明同时会在启动器更新后的「版本说明」弹窗中展示。

- **项目性质**：PCL Symbio Edition 是从 PCL Community Edition（PCL CE）分支衍生而来的独立版本，由个人开发者面向 Minecraft 国际版维护，与 LTCatt（龙腾猫跃）维护的 PCL2 官方项目、PCL-Community 维护的 PCL CE 官方项目均无隶属关系。
- **支持范围**：PCL Symbio Edition 不支持 Minecraft 游戏开发商 Mojang Studios 在中国大陆的代理公司运营的「我的世界中国版」，也不提供与该版本相关的任何功能或适配。
- **API 使用**：PCL Symbio Edition 仅通过经 Mojang Studios 批准的 API 访问 Minecraft 国际版的相关服务。
- **代码来源**：本项目不包含任何 Minecraft（含「我的世界中国版」）的代码，也不包含 Mojang Studios 及其中国代理商的任何代码。
- **名称使用**：文档与演示中出现的「Minecraft」字样仅用于描述本项目所支持的游戏，属于《中华人民共和国著作权法》规定的合理使用；相关截图与视频的版权归其各自开发者所有。
- **关系说明**：PCL Symbio Edition 与 PCL2、PCL CE、Mojang Studios 及其中国代理商之间不存在任何隶属、合作、维护或授权关系。
- **正版倡导**：我们鼓励通过官方渠道购买并游玩正版 Minecraft。
- **联机大厅**：PCL Symbio Edition 当前不提供 PCL CE 的联机大厅，相关模块已移除，目前没有已确认的恢复排期。

## 许可证

- `Plain Craft Launcher 2/` 使用 [自定义许可证](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- 其余所有目录使用 [Apache License 2.0](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/LICENSE)

## 致谢

- [PCL](https://github.com/Meloong-Git/PCL) — 原作者 龙腾猫跃
- PCL CE — 社区维护分支，本项目的直接上游
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) 与 [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Round Studio 开发的基岩版项目（GPL-3.0），本项目基岩版安装、解密与启动方案的移植参考，来源与适用范围见 [NOTICE](NOTICE)
- 以及 Dapper、Downloader、Markdig.Wpf、NAudio、Microsoft.Data.Sqlite、protobuf-net、Lucide 等开源项目，完整的第三方组件与许可信息见 [metadata.json](Plain%20Craft%20Launcher%202/metadata.json) 与启动器「关于」页面


**此页浏览量**（总计 / 今日）：[![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition)](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition&web=true)

