# PCL SE 前后端对接文档

当前分支为 `feat/symbio-shell`。WPF 页面的主要对接方式是进程内调用、事件、Loader 和配置观察者。陶瓦使用本机 HTTP，启动器实例间的 RPC 由 Core 提供。 [项目详情](project-details.md) · [全量接口索引](interface-index.html)

## 全量接口索引

索引按当前工作树生成，覆盖 **829 个 C# 文件、94 个 XAML 文件**，包含 **94860 条静态关系**。其中 **390 处 XAML 事件**已定位源码处理器，另有 **4576 条界面到模块或核心库的直接调用**。 [打开接口索引](interface-index.html)

| 要追踪的接口 | 索引中的入口 |
|---|---|
| 控件事件和代码订阅 | 选择「XAML 事件」或「订阅与解绑」，按控件名、类型或方法名检索 |
| 界面触发的调用链 | 选择「入口处理器的直接去向」或「界面到模块调用」，查看调用方与目标 |
| 具体调用、构造、属性、字段或类型 | 选对应记录类别，可按源文件或成员定位 |
| 配置注册、绑定和资源 | 查「注册与生成」「XAML 绑定与触发」「资源与模板引用」 |
| 路由和网络 | 查「路由分支」「地址引用」「请求调用」及网站链接 |
| 运行时自定义内容 | 查 `HomeContentView`、`HelpDocuments`、`ViewMarkupNames` 与 `XamlEventSanitizer`。宿主及解析入口可静态定位 |

索引基于源码静态分析。运行时加载的内容、条件编译分支和生成成员可沿宿主、声明和回调继续定位。接口索引的文件链接指向当前工作树，显示的行号用于定位。

## 应用入口与工作区路由

| 功能 | 可见入口 | 业务对接 |
|---|---|---|
| 启动器外壳、导航与返回 | [SymbioShell](<../Plain Craft Launcher 2/Shell/SymbioShell.xaml>) · [FormMain](<../Plain Craft Launcher 2/FormMain.xaml.cs>) | `PageStackData` 持有本次导航参数，`ShellNavigation` 记录各工作区所选子页。 [ShellNavigation](<../Plain Craft Launcher 2/Shell/ShellNavigation.cs>) |
| Java 启动与取消 | [主窗体外壳接线](<../Plain Craft Launcher 2/FormMain.SymbioShell.cs>) | 启动会话管理所选实例、状态与取消，实际进程参数和启动由 `ModLaunch` 处理。 [JavaLaunchSession](<../Plain Craft Launcher 2/Modules/Minecraft/JavaLaunchSession.cs>) · [ModLaunch](<../Plain Craft Launcher 2/Modules/Minecraft/ModLaunch.cs>) |
| 基岩启动、导入与管理 | [BedrockManagementView](<../Plain Craft Launcher 2/Shell/Instances/Bedrock/BedrockManagementView.xaml.cs>) · [BedrockLibraryView](<../Plain Craft Launcher 2/Shell/Instances/BedrockLibraryView.xaml.cs>) | 启动、版本和目录由会话及本地实例模块提供。 [BedrockLaunchSession](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockLaunchSession.cs>) · [ModBedrockInstance](<../Plain Craft Launcher 2/Modules/Minecraft/ModBedrockInstance.cs>) · [ModBedrockFolders](<../Plain Craft Launcher 2/Modules/Minecraft/ModBedrockFolders.cs>) |
| 微软、外置与离线账号 | [SymbioAccountPanel](<../Plain Craft Launcher 2/Shell/SymbioAccountPanel.xaml.cs>) | 表单提交调用账号服务，由实际登录结果与档案状态返回到可见页面。 [AccountSignIn](<../Plain Craft Launcher 2/Modules/Minecraft/AccountSignIn.cs>) |
| 资源搜索、筛选与分页 | [ResourceBrowserPage](<../Plain Craft Launcher 2/Shell/Downloads/ResourceBrowserPage.xaml.cs>) | 查询参数、页码与缓存归搜索会话。 [ResourceSearchSession](<../Plain Craft Launcher 2/Modules/Minecraft/ResourceSearchSession.cs>) |
| 资源详情及收藏 | [ResourceDetailsView](<../Plain Craft Launcher 2/Shell/Downloads/ResourceDetailsView.xaml.cs>) · [ResourceProjectRow](<../Plain Craft Launcher 2/Shell/Downloads/ResourceProjectRow.xaml.cs>) · [ResourceFavoritesView](<../Plain Craft Launcher 2/Shell/Downloads/ResourceFavoritesView.xaml.cs>) | 路由携带资源、游戏版本与加载器，返回时恢复展开状态。资源文件和收藏读取 `ModComp`。 [ModComp](<../Plain Craft Launcher 2/Modules/Minecraft/ModComp.cs>) |
| 手动下载、Java 新装与组件安装 | [VersionCatalogView](<../Plain Craft Launcher 2/Shell/Downloads/VersionCatalogView.xaml.cs>) · [JavaInstallView](<../Plain Craft Launcher 2/Shell/Downloads/JavaInstallView.xaml.cs>) · [InstanceLoaderView](<../Plain Craft Launcher 2/Shell/Instances/InstanceLoaderView.xaml.cs>) | 版本清单由 Loader 提供，安装请求进入下载模块，外部版本建议通过请求状态交给可见安装页。 [ModDownload](<../Plain Craft Launcher 2/Modules/Minecraft/ModDownload.cs>) · [ModDownloadLib](<../Plain Craft Launcher 2/Modules/Minecraft/ModDownloadLib.cs>) · [JavaInstallRequests](<../Plain Craft Launcher 2/Modules/Minecraft/JavaInstallRequests.cs>) |
| 基岩库、安装目录与替代来源 | [BedrockCatalogView](<../Plain Craft Launcher 2/Shell/Downloads/BedrockCatalogView.xaml.cs>) · [BedrockSourceCatalogView](<../Plain Craft Launcher 2/Shell/Downloads/BedrockSourceCatalogView.xaml.cs>) | 目录和选中版本由模型提供，安装应用包仍沿基岩服务执行。 [基岩会话](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockLaunchSession.cs>) |
| 下载任务进度、失败与取消 | [DownloadTasksView](<../Plain Craft Launcher 2/Shell/Downloads/DownloadTasksView.xaml.cs>) | Loader 注册进入下载任务集合，Dock 与任务视图独立消费进度状态。 [DownloadTasks](<../Plain Craft Launcher 2/Modules/Network/Management/DownloadTasks.cs>) |
| Java 实例目录与版本 | [InstanceLibraryView](<../Plain Craft Launcher 2/Shell/Instances/InstanceLibraryView.xaml.cs>) | 目录登记与缓存刷新进入目录模块，游戏版本由实例列表 Loader 提供。 [ModFolder](<../Plain Craft Launcher 2/Modules/Minecraft/ModFolder.cs>) · [ModInstanceList](<../Plain Craft Launcher 2/Modules/Minecraft/ModInstanceList.cs>) |
| Java 实例设定、资源、内嵌组件 | [InstanceSettingsView](<../Plain Craft Launcher 2/Shell/Instances/InstanceSettingsView.xaml.cs>) · [ResourceManagerView](<../Plain Craft Launcher 2/Shell/Instances/ResourceManagerView.xaml.cs>) · [NestedComponentsView](<../Plain Craft Launcher 2/Shell/Instances/NestedComponentsView.xaml.cs>) | 配置写入以目标实例路径为参数。资源 Loader 持有目录、目标实例及扫描阶段，页面订阅同一请求的状态。 [InstanceWorkspace](<../Plain Craft Launcher 2/Modules/Minecraft/InstanceWorkspace.cs>) · [JavaMemoryAllocation](<../Plain Craft Launcher 2/Modules/Minecraft/JavaMemoryAllocation.cs>) · [ModLocalComp](<../Plain Craft Launcher 2/Modules/Minecraft/ModLocalComp.cs>) |
| Java 存档、数据包和衍生功能 | [WorldLibraryView](<../Plain Craft Launcher 2/Shell/Instances/WorldLibraryView.xaml.cs>) · [WorldDetailsView](<../Plain Craft Launcher 2/Shell/Instances/WorldDetailsView.xaml.cs>) · [WorldDatapacksView](<../Plain Craft Launcher 2/Shell/Instances/WorldDatapacksView.xaml.cs>) | 导航提供存档路径。截图、服务器、导出分别挂到当前实例工作区。 [实例导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/InstanceNavigation.xaml.cs>) · [存档导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/SaveNavigation.xaml.cs>) |
| 基岩实例详情 | [基岩实例导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/BedrockInstanceNavigation.xaml.cs>) | 概览、设置、存档、加载器、文件、操作和统计读取明确的实例版本。 [BedrockInstanceWorkspace](<../Plain Craft Launcher 2/Modules/Minecraft/BedrockInstanceWorkspace.cs>) |
| 设置与外观 | [设置导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/SettingsNavigation.xaml.cs>) · [AppearanceSettingsView](<../Plain Craft Launcher 2/Shell/Settings/AppearanceSettingsView.xaml.cs>) | 配置属性按来源保存，主题、内存、启动日志与背景观察当前状态。 [ModSetup](<../Plain Craft Launcher 2/Modules/Base/ModSetup.cs>) · [配置项](../PCL.Core/App/Config.cs) |
| 游戏日志、提示和弹窗 | [GameLogsView](<../Plain Craft Launcher 2/Shell/Diagnostics/GameLogsView.xaml.cs>) · [DialogPresentation](<../Plain Craft Launcher 2/Shell/Dialogs/DialogPresentation.cs>) | 游戏日志会话保存于服务，通知与弹窗统一从宿主呈现。 [LiveGameLogs](<../Plain Craft Launcher 2/Modules/Minecraft/LiveGameLogs.cs>) · [HintService](<../Plain Craft Launcher 2/Modules/UI/HintService.cs>) |
| 主页、帮助和自定义事件 | [HomeContentView](<../Plain Craft Launcher 2/Shell/Home/HomeContentView.xaml.cs>) · [HelpDocumentView](<../Plain Craft Launcher 2/Shell/Help/HelpDocumentView.xaml.cs>) | 宿主读取文件与配置，通过事件白名单解析保存的 XAML。 [自定义事件](<../Plain Craft Launcher 2/Modules/Event/CustomEvent.cs>) · [兼容类型](<../Plain Craft Launcher 2/Modules/Event/ViewMarkupNames.cs>) · [事件校验](<../Plain Craft Launcher 2/Modules/Event/XamlEventSanitizer.cs>) |
| 工具、皮肤与披风 | [工具导航](<../Plain Craft Launcher 2/Shell/Navigation/Areas/ToolsNavigation.xaml.cs>) · [SkinLibraryView](<../Plain Craft Launcher 2/Shell/Tools/SkinLibraryView.xaml.cs>) · [CapeLibraryView](<../Plain Craft Launcher 2/Shell/Tools/CapeLibraryView.xaml.cs>) | 清理、下载等共用命令在工具模块，皮肤与披风使用独立的本地资产库。 [LauncherTools](<../Plain Craft Launcher 2/Modules/UI/LauncherTools.cs>) · [ProfileAssetLibrary](<../Plain Craft Launcher 2/Modules/UI/ProfileAssetLibrary.cs>) |
| 联机大厅和本机 RPC | [LanWorkspaceView](<../Plain Craft Launcher 2/Shell/Multiplayer/LanWorkspaceView.xaml.cs>) | 大厅轮询同一份陶瓦服务状态。启动器实例间的 RPC 由命名管道服务提供。 [ModTerracotta](<../Plain Craft Launcher 2/Modules/Minecraft/ModTerracotta.cs>) · [RpcService](../PCL.Core/App/Essentials/RpcService.cs) |

## 状态与错误反馈

- Loader 提供开始、进度、完成、失败和中止状态。加载控件通过 `ILoadingTrigger` 读取状态，工作区按路由呈现内容或错误。 [ModLoader](<../Plain Craft Launcher 2/Modules/Base/ModLoader.cs>) · [LoadingSignal](<../Plain Craft Launcher 2/Modules/Base/LoadingSignal.cs>) · [LoadingIndicator](<../Plain Craft Launcher 2/Shell/Controls/LoadingIndicator.xaml.cs>)
- 配置调用可以经生成的强类型属性或 `ConfigService.TrySetValue`。实例参数指向目标实例目录。 [配置服务](../PCL.Core/App/Configuration/ConfigService.cs)
- 业务事件带当前请求、实例或会话，页面随路由和生命周期绑定视图。窗口切换与控件动效由 Symbio 驱动，关闭时的完成回调在 WPF 更新可视树后执行。 [工作区生命周期](<../Plain Craft Launcher 2/Shell/Navigation/WorkspacePage.cs>) · [Symbio 动效](<../Plain Craft Launcher 2/Shell/SymbioMotion.cs>) · [弹簧帧驱动](<../Plain Craft Launcher 2/Glass/Framework/SpringMotion.cs>)

## 验收记录

[迁移与验收记录](symbio-ui-migration.md)记录已通过的隔离 WPF 流程、专项套件结果和真实环境验收范围。界面风格与后续任务见[设计规范与交接](symbio-design-system.md)。
