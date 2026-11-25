# Symbio 界面迁移

开发分支为 `feat/symbio-shell`。页面消费所属业务服务的状态，用户操作从当前界面进入业务命令。以下记录本地工作树的实现与验证。当前工作树的静态接线见[全量接口索引](interface-index.html)。界面风格、组件选型和后续任务见[设计规范与交接](symbio-design-system.md)。

| 范围 | 本地实现 | 验证 |
|---|---|---|
| 主窗口与首页 | 新 Dock、标题栏、版本选择和首页。Java、基岩启动由各自的会话模块处理。自定义内容按需打开 | 已检查切页、版本切换、启动页没有后台内容视图。真实游戏启动和系统包注册仍需专项验收 |
| 账号 | 新账号页调用微软、外置和离线账号服务，保留设备码、服务器预设、注册与找回入口及三种离线 UUID 模式 | 已检查三种离线 UUID 和表单切换。真实微软、外置和 Xbox 授权待验证 |
| 资源浏览 | 11 类共用 `ResourceBrowserPage`。查询、页码、Loader 与缓存由 `ResourceSearchSession` 持有，详情带上结果的检索上下文 | 已逐类检查入口、筛选和来源开关。远程搜索、游戏资源安装和收藏需要按服务验收 |
| 下载队列 | `DownloadTasks` 提供活动任务、进度、失败快照和取消。下载页和 Dock 分别从队列读数据 | 已驱动真实 Loader 验证进度、失败详情、取消、关闭失败项、队列结束后返回 |
| 版本下载目录 | Java 原版、Forge、NeoForge、Cleanroom、OptiFine、LiteLoader、Fabric、Legacy Fabric、LabyMod 共用版本目录视图和已有下载服务 | 已用各路由的离线清单检查装载和列表分组，实际远程文件下载待验收 |
| Java 新装与实例组件 | 外部版本建议经过安装请求，实例加载器页读取当前实例数据，返回动作交给实际可见的安装页 | 已验自动选择版本、组件页、Esc 返回以及已存在实例的组件安装入口 |
| Java 实例库 | `InstanceLibraryView` 集成目录和版本列表。注册目录和实例缓存刷新归 `ModFolder`，实例分类由 `ModInstanceList` 提供 | 已用两个独立临时目录和版本元数据验证目录登记、切换、搜索、清空查询及 F11 隐藏实例 |
| 资源详情与本地资源 | 详情页接收项目、游戏版本和加载器参数，展开状态保存在当前导航记录。在线资源与本地资源行使用新的按钮和选择逻辑 | 已验缓存数据分组、项目切换、返回恢复展开状态、本地资源键盘调用和连续选中 |
| 自定义主页 | 自定义内容按需加载，刷新命令使当前主页重新读取内容。启动器提示文本独立加载 | 已验临时主页文件、原地刷新及旧主页保存标记 |
| 基岩实例库及管理 | 基岩目录、可用版本和当前实例来自业务模块。概览、资料、操作、加载器、存档和统计按目标实例打开 | 已验目录切换、筛选、选择返回和七个实例管理入口 |
| 设置与基岩统计 | 语言、日志、外观设置和基岩实例统计接入工作区。外观页观察配置、音乐、背景和视频状态，导航显隐由 Shell 负责 | 已检查格式写入、日志列表、主页类型、背景刷新可用状态、模糊选项与真实基岩文件和存档数量 |
| 游戏日志与弹窗 | `LiveGameLogs` 持有会话、输出、行数限制与选择状态，日志页只在路由命中时创建。弹窗共用过渡与背景层 | 已检查空日志入口、加载、弹窗关闭、正常退出。真实游戏日志会话、进程操作与在线授权弹窗仍需专项验收 |
| 工具、联机与帮助 | 皮肤、披风、设备信息、投影、工具命令和本地帮助使用当前工作区。联机操作与房间信息使用同一个页面和服务状态 | 已验工具、皮肤预览、帮助和联机界面可见入口。实机联机另行验收 |
| 共用控件与动效 | 迁移按钮、图标动作、滚动、复选、单选、输入、下拉、菜单、滑块、卡片、加载、搜索、折叠、提示条、通知、图片和延迟视图。旧主页标签在解析时翻译成当前类型 | 已检查交互和布局回归。页面切换通过可取消的调度与弹簧动效，动画帧率和速度接入本地设置 |

## Fluent 设计框架

在迁移完成的工作区上，界面统一改成 Fluent 2 设计，搭配 LF 的磨砂玻璃和弹簧动效。规范、令牌和待办都在[设计规范与交接](symbio-design-system.md)里。

| 范围 | 实现 | 验证 |
|---|---|---|
| 外壳 | 磨砂标题栏，与内容之间留出间距；Windows 11 标题栏按钮；Sheet 改为磨砂；磨砂默认始终开启 | 已在默认背景和自定义壁纸下截图，确认文字可读 |
| Dock | `UiDockPlacement` 两种模式：窗口内，或窗口外悬浮。悬浮时窗口按"主面板 + Dock"两块裁剪，Dock 保持磨砂 | 已从个性化页切换两种模式并截图 |
| 设置组件 | `SettingsCard` / `SettingsExpander` / `SettingsRow`、`SyToggleSwitch`、`SyFrostPanel`、`SyPageTitle` / `SySectionHeader`；输入框改用 WinUI 的底边描边 | 已检查居中、展开动效、开关弹簧和输入框内边距 |
| 设置页 | 启动、Java、管理、个性化、语言、杂项、软件更新、反馈、日志已改为设置卡片布局。控件名、配置键和事件保持不变 | 已逐页截图；软件信息页待改 |

## 当前接线

| 动作或状态 | 接线 |
|---|---|
| 首页启动、取消 | [JavaLaunchSession](<../Plain Craft Launcher 2/Modules/Minecraft/JavaLaunchSession.cs>) · [BedrockLaunchSession](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockLaunchSession.cs>) |
| 账号切换与登录 | [SymbioAccountPanel](<../Plain Craft Launcher 2/Shell/SymbioAccountPanel.xaml.cs>) · [AccountSignIn](<../Plain Craft Launcher 2/Modules/Minecraft/AccountSignIn.cs>) |
| 资源查询、分页和详情 | [ResourceBrowserPage](<../Plain Craft Launcher 2/Shell/Downloads/ResourceBrowserPage.xaml.cs>) · [ResourceSearchSession](<../Plain Craft Launcher 2/Modules/Minecraft/ResourceSearchSession.cs>) |
| 下载队列、错误和取消 | [DownloadTasksView](<../Plain Craft Launcher 2/Shell/Downloads/DownloadTasksView.xaml.cs>) · [DownloadTasks](<../Plain Craft Launcher 2/Modules/Network/Management/DownloadTasks.cs>) |
| 手动版本下载 | [VersionCatalogView](<../Plain Craft Launcher 2/Shell/Downloads/VersionCatalogView.xaml.cs>) · [下载目录导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/DownloadNavigation.xaml.cs>) |
| 资源详情与列表操作 | [ResourceDetailsView](<../Plain Craft Launcher 2/Shell/Downloads/ResourceDetailsView.xaml.cs>) · [ResourceProjectRow](<../Plain Craft Launcher 2/Shell/Downloads/ResourceProjectRow.xaml.cs>) · [LocalProjectRow](<../Plain Craft Launcher 2/Shell/Instances/LocalProjectRow.xaml.cs>) |
| Java 安装请求与版本选择 | [JavaInstallRequests](<../Plain Craft Launcher 2/Modules/Minecraft/JavaInstallRequests.cs>) · [JavaInstallView](<../Plain Craft Launcher 2/Shell/Downloads/JavaInstallView.xaml.cs>) · [InstanceLoaderView](<../Plain Craft Launcher 2/Shell/Instances/InstanceLoaderView.xaml.cs>) |
| 实例目录与展示结果 | [InstanceLibraryView](<../Plain Craft Launcher 2/Shell/Instances/InstanceLibraryView.xaml.cs>) · [ModFolder](<../Plain Craft Launcher 2/Modules/Minecraft/ModFolder.cs>) · [ModInstanceList](<../Plain Craft Launcher 2/Modules/Minecraft/ModInstanceList.cs>) |
| 外观偏好、背景及导航显隐 | [AppearanceSettingsView](<../Plain Craft Launcher 2/Shell/Settings/AppearanceSettingsView.xaml.cs>) · [LauncherBackground](<../Plain Craft Launcher 2/Shell/Settings/LauncherBackground.cs>) · [ModSetup](<../Plain Craft Launcher 2/Modules/Base/ModSetup.cs>) · [ShellNavigation](<../Plain Craft Launcher 2/Shell/ShellNavigation.cs>) |
| 实时日志与生命周期 | [GameLogsView](<../Plain Craft Launcher 2/Shell/Diagnostics/GameLogsView.xaml.cs>) · [LiveGameLogs](<../Plain Craft Launcher 2/Modules/Minecraft/LiveGameLogs.cs>) |
| Java 内存与本地资源 | [JavaMemoryAllocation](<../Plain Craft Launcher 2/Modules/Minecraft/JavaMemoryAllocation.cs>) · [ResourceManagerView](<../Plain Craft Launcher 2/Shell/Instances/ResourceManagerView.xaml.cs>) · [ModLocalComp](<../Plain Craft Launcher 2/Modules/Minecraft/ModLocalComp.cs>) |
| 基岩安装与实例管理 | [BedrockCatalogView](<../Plain Craft Launcher 2/Shell/Downloads/BedrockCatalogView.xaml.cs>) · [BedrockLibraryView](<../Plain Craft Launcher 2/Shell/Instances/BedrockLibraryView.xaml.cs>) · [BedrockInstanceWorkspace](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockInstanceWorkspace.cs>) |
| 工具、联机和帮助 | [LauncherTools](<../Plain Craft Launcher 2/Modules/UI/LauncherTools.cs>) · [LanWorkspaceView](<../Plain Craft Launcher 2/Shell/Multiplayer/LanWorkspaceView.xaml.cs>) · [HelpDocuments](<../Plain Craft Launcher 2/Modules/UI/HelpDocuments.cs>) |
| 页面与控件过渡 | [FormMain](<../Plain Craft Launcher 2/FormMain.xaml.cs>) · [SymbioMotion](<../Plain Craft Launcher 2/Shell/SymbioMotion.cs>) · [SpringMotion](<../Plain Craft Launcher 2/Glass/Framework/SpringMotion.cs>) |
| 自定义主页保存数据 | [ViewMarkupNames](<../Plain Craft Launcher 2/Modules/Event/ViewMarkupNames.cs>) |

## 验收范围

原 `Pages` 下的界面和被替换的旧控件实现已从应用项目移除，业务模块通过请求、快照、配置和事件提供状态。保存的主页与帮助 XAML 继续由宿主解析兼容标记。

- .NET 10 Windows x64 的 Debug 和 Release 构建成功。
- 隔离 WPF 进程通过 200 项检查，覆盖 Java 与基岩目录、设置、资源扫描、安装入口、帮助、联机界面、工具、快速导航和关闭。进程以退出码 0 结束。
- Playwright 已验证接口索引的关键词检索、方法筛选和代表性源码链接。
- 基岩安装 29/29、基岩数据 133/133、启动器服务 44/44。
- 完整 Core 测试（PCL.Core.Test）复跑 257/257 通过，其中本地化测试 7/7。
- 真实微软与外置账号授权、游戏启动、远程安装、基岩系统包注册和双设备联机待对应环境验收。
