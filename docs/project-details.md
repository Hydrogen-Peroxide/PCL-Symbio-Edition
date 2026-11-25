# PCL SE 项目详情

PCL SE 是 Windows 桌面上的 Minecraft Java 版与基岩版启动器。主程序使用 WPF，目标框架为 `net10.0-windows`。本文描述 `feat/symbio-shell` 的本地工作树，迁移起点为 `ddzmumo-use` 的 `66af0f5`。 [主程序项目文件](<../Plain Craft Launcher 2/Plain Craft Launcher 2.csproj>)

[前后端对接文档](frontend-backend-integration.md) · [全量接口索引](interface-index.html) · [迁移与验收记录](symbio-ui-migration.md) · [设计规范与交接](symbio-design-system.md)

## 项目结构

| 路径 | 职责 |
|---|---|
| `Plain Craft Launcher 2/Application.xaml(.cs)` | 程序入口、生命周期和应用级资源。 [应用入口](<../Plain Craft Launcher 2/Application.xaml.cs>) |
| `Plain Craft Launcher 2/FormMain*.cs` | WPF 主窗口、路由、菜单与操作入口。 `FormMain.SymbioShell.cs` 连接新外壳与业务状态。 [窗口路由](<../Plain Craft Launcher 2/FormMain.xaml.cs>) · [外壳接线](<../Plain Craft Launcher 2/FormMain.SymbioShell.cs>) |
| `Plain Craft Launcher 2/Shell` | 首页、账号、下载、实例、设置、工具、联机、帮助，以及共用控件、弹窗和通知。 [外壳和 Dock](<../Plain Craft Launcher 2/Shell/SymbioShell.xaml>) · [工作区基类](<../Plain Craft Launcher 2/Shell/Navigation/WorkspacePage.cs>) |
| `Plain Craft Launcher 2/Modules` | 启动会话、配置响应、资源、任务、文件、账号和联机处理。 [Java 启动会话](<../Plain Craft Launcher 2/Modules/Minecraft/JavaLaunchSession.cs>) · [基岩启动会话](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockLaunchSession.cs>) |
| `Plain Craft Launcher 2/Glass/Framework` 与 `Themes` | 弹簧动效、按压和滚动行为，与 Symbio 控件模板、配色资源。 [动效](<../Plain Craft Launcher 2/Glass/Framework/SpringMotion.cs>) · [主题](<../Plain Craft Launcher 2/Themes/Symbio.xaml>) |
| `PCL.Core` 与 `PCL.Core.SourceGenerators` | 配置与存储、I/O、网络、国际化、Java 发现，以及配置属性生成。 [核心项目](../PCL.Core/PCL.Core.csproj) · [源生成器项目](../PCL.Core.SourceGenerators/PCL.Core.SourceGenerators.csproj) |
| `PCL.Core.Test` 与 `tests` | Core 测试与启动器、基岩安装、基岩数据三个专项回归入口。 [Core 测试](../PCL.Core.Test/PCL.Core.Test.csproj) · [启动器专项](../tests/LauncherServicesRegression/README.md) · [基岩安装专项](../tests/BedrockInstallRegression/README.md) · [基岩数据专项](../tests/BedrockDataRegression/README.md) |
| `website` | 项目静态站点。 [网站目录](../website/) |

## 主窗口与业务状态

1. `Application` 初始化日志、配置、本地化等服务，再创建 `FormMain`。
2. 主窗口通过 `SymbioShell` 展示首页和导航。工作区按路由创建和切换，页面随加载或卸载建立和解除订阅。
3. 目录、实例、下载任务和日志会话由所属业务模块维护。版本设置与启动流程调用同一个 Java 内存分配模块。 [实例上下文](<../Plain Craft Launcher 2/Modules/Minecraft/InstanceWorkspace.cs>) · [基岩实例上下文](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockInstanceWorkspace.cs>) · [内存分配](<../Plain Craft Launcher 2/Modules/Minecraft/JavaMemoryAllocation.cs>) · [任务队列](<../Plain Craft Launcher 2/Modules/Network/Management/DownloadTasks.cs>)

## 数据位置

| 数据 | 位置与访问方式 |
|---|---|
| 共享配置 | `Paths.SharedData/config.v1.json`，由 `ConfigService` 选择配置来源。 [配置服务](../PCL.Core/App/Configuration/ConfigService.cs) |
| 本地配置 | `Paths.Data/config.v1.yml`，路径由 `Paths` 决定。 [路径服务](../PCL.Core/App/Paths.cs) |
| Java 实例配置 | 目标实例的 `PCL/config.v1.yml`。配置项带实例路径参数。 [配置定义](../PCL.Core/App/Config.cs) |
| 基岩本地实例 | 配置根目录的 `versions`，也支持直接选择版本目录。显示名称可存于实例的 `.pcl-instance.json`。 [本地实例](<../Plain Craft Launcher 2/Modules/Minecraft/ModBedrockInstance.cs>) |
| 自定义主页和帮助 | 主页由本地、预设或远程配置提供，帮助以配对的 JSON 和 XAML 加载。运行时解析旧控件标记并应用当前类型。 [主页](<../Plain Craft Launcher 2/Shell/Home/HomeContentView.xaml.cs>) · [帮助文档](<../Plain Craft Launcher 2/Modules/UI/HelpDocuments.cs>) · [标记转换](<../Plain Craft Launcher 2/Modules/Event/ViewMarkupNames.cs>) |

## 构建

在仓库根目录使用 .NET 10 SDK 构建 Windows x64 主程序。

```powershell
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug -p:Platform=x64
```

主程序的 Debug 与 Release 均已在本地编译。界面回归在独立应用、配置和游戏目录运行。真实登录、游戏启动、在线安装和双机联机的边界见[迁移与验收记录](symbio-ui-migration.md)。
