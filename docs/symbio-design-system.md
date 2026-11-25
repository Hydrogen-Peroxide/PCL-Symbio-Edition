# Symbio 界面设计规范与交接

本文是 `feat/symbio-shell` 分支界面重做的设计约定，供后续开发者和 agent 按同一风格继续改页面。业务接线见[前后端对接文档](frontend-backend-integration.md)，迁移进度见[迁移与验收记录](symbio-ui-migration.md)。

## 设计方向

- **视觉语言是 Fluent 2，按 Windows 11 / WinUI 3 的规格来做。** 布局、控件尺寸、圆角、字号和颜色都以 WinUI 的主题值为准，不要做成 visionOS 那样的大圆角，也不要用发光胶囊按钮。
- **材质沿用 PCL LF 的磨砂玻璃。** 窗口铺满壁纸，所有面板、卡片、Dock 和标题栏都是半透明底色加实时模糊的磨砂玻璃，不能用不透明色块挡住壁纸。
- **动效沿用 PCL LF 的弹簧纵深动效。** 入场是"上浮加缩放"，列表依次错开入场，按下时有弹簧回弹。
- **不保留 PCL 原版的观感。** 不用蓝色渐变标题栏，不用"标题加整块可折叠的大卡片"，不用 Label + 下拉框的表格排版，也不用 PCL 的颜色阶。

## 框架分层

| 层 | 内容 | 源码 |
|---|---|---|
| 窗口 | 透明分层窗口（`AllowsTransparency`），用 `PanBack.Clip` 裁出可见形状 | [FormMain.xaml](<../Plain Craft Launcher 2/FormMain.xaml>) · [FormMain.SymbioShell.cs](<../Plain Craft Launcher 2/FormMain.SymbioShell.cs>) |
| 外壳 | 壁纸、磨砂标题栏、首页卡片、内容面板（Sheet）、Dock | [SymbioShell.xaml](<../Plain Craft Launcher 2/Shell/SymbioShell.xaml>) |
| Dock 位置 | `UiDockPlacement`：0 是窗口内底部；1 是窗口外悬浮，窗口被裁成"主面板 + Dock 岛"两块，中间露出桌面且点击穿透 | [SymbioShell.Dock.cs](<../Plain Craft Launcher 2/Shell/SymbioShell.Dock.cs>) |
| 页面 | `WorkspacePage` 管理页面生命周期和进出动效；左侧是二级导航，右侧是页面内容 | [WorkspacePage.cs](<../Plain Craft Launcher 2/Shell/Navigation/WorkspacePage.cs>) · `Shell/Navigation/Areas/` |
| 材质 | `GlassSurface` / `GlassMaterial`，从壁纸缓存里取出控件背后那一块做模糊，再叠一层底色 | [Glass/Framework](<../Plain Craft Launcher 2/Glass/Framework/>)（移植自 LF，MPL-2.0） |
| 配色 | `SymbioPalette` 按浅色/深色模式和主题色写入 `Sy*` 画刷，高对比度模式下自动退回系统色 | [SymbioPalette.cs](<../Plain Craft Launcher 2/Shell/SymbioPalette.cs>) |
| 样式 | `Sy*` 令牌与控件样式 | [Themes/Symbio.xaml](<../Plain Craft Launcher 2/Themes/Symbio.xaml>) |
| 动效 | `SymbioMotion` 封装 LF 的 `SpringMotion` / `StaggeredEntrance` | [SymbioMotion.cs](<../Plain Craft Launcher 2/Shell/SymbioMotion.cs>) |

磨砂效果默认一直开启。Windows 关闭"透明效果"或开启高对比度时，`GlassMaterial` 会自动改用纯色背景，页面里不需要自己判断。

## 设计令牌

一律用 `{DynamicResource Sy*}` / `{StaticResource Sy*}` 引用，不要在页面里写死颜色或尺寸。

| 类别 | 令牌 | 用途 |
|---|---|---|
| 文字 | `SyText` `SyTextSecondary` `SyTextTertiary` `SyTextDisabled` `SyTextOnAccent` `SyAccentText` | 正文、说明、占位与次要信息、禁用、主题色底上的文字、链接与强调 |
| 磨砂材质 | `SyFrostFill` `SyFrostHover` `SyFrostEdge` | 卡片和面板的底色、悬停底色、边缘描边 |
| 层级底色 | `SyVeil`（Sheet） `SyTitleBarFill`（标题栏） `SyAcrylicTint`（浮层、首页卡片） `SyFlyoutFill`（菜单） | 各层固定的底色，只在外壳层使用 |
| 控件 | `SyControl` `SyControlHover` `SyControlPressed` `SyControlAlt` `SyControlInputActive` | 按钮、输入框等控件的底色 |
| 描边 | `SyControlStroke` `SyControlElevation` `SyTextControlElevation` `SyControlStrongStroke` `SyDivider` | 控件立体描边、输入框底边、分隔线 |
| 主题色 | `SyAccentFill` `SyAccentHover` `SyAccentPressed` `SyAccentElevation` | 主要按钮、开关打开、选中指示条、聚焦底边 |
| 轻量交互 | `SySubtleHover` `SySubtlePressed` `SySelected` | 无边框按钮和列表行的悬停、按下、选中 |
| 状态 | `SyCritical` | 错误、危险操作 |
| 圆角 | `SyRadiusControl`=4 `SyRadiusOverlay`=8 `SyRadiusPill` | 控件和设置卡片用 4；面板、浮层、Dock 用 8；胶囊仅限开关轨道和指示条 |
| 尺寸 | `SyControlHeight`=32 `SyLargeControlHeight` | 按钮、输入框、下拉框统一 32 高 |
| 字体 | `SyFontFamily` `SyDisplayFontFamily`；`SyFontCaption`=12 `SyFontBody`=14 `SyFontSubtitle`=20 `SyFontTitle`=28 | Segoe UI Variable，中文回退到微软雅黑 UI |

新增颜色时，要在 `SymbioPalette` 的浅色和深色两套里各写一次，加进高对比度回退的键列表，并在 `Symbio.xaml` 里放一个浅色默认值，供设计器预览。

## 组件选型

| 需求 | 用什么 | 说明 |
|---|---|---|
| 一项设置 | `SettingsCard`（[源码](<../Plain Craft Launcher 2/Shell/Controls/SettingsCard.cs>)） | 左侧依次是图标、标题、说明，控件放右侧（`ActionWidth` 控制宽度）；控件放不下时用 `Stacked="True"` 改为放在说明下方 |
| 一组相关设置 / 高级选项 | `SettingsExpander` + `SettingsRow` | 默认收起；标题右侧可以放控件（`HeaderAction`），子项是 `SettingsRow` |
| 布尔开关 | `SymbioCheck` + `Style="{StaticResource SyToggleSwitch}"` | 在设置卡片里一律用开关；同一卡片里的多选列表才用复选框（默认的 `SymbioCheck` 样式） |
| 单选（不超过 3 项） | `SymbioRadio` 横排放在卡片右侧 | 超过 3 项改用下拉框 |
| 单选（多项） | `SymbioSelect` + `SelectOption` | 不要用原生 `ComboBox` |
| 文本输入 | `SymbioTextField` | 32 高，底边是渐变描边，聚焦时变成 2px 主题色；不要再给它写外层内边距（TextBox 会自己加 Padding） |
| 数值 | `SymbioSlider` | 放在卡片右侧，`ActionWidth` 260 左右 |
| 按钮 | `SymbioButton`（`MinHeight="32"`），主要操作 `ColorType="Highlight"`，危险操作 `ColorType="Red"`；也可以用 `Button` + `SyButton` / `SyAccentButton` / `SySubtleButton` | 每个区域最多一个主要按钮 |
| 列表 / 分组内容 | `glass:GlassSurface Style="{StaticResource SyFrostPanel}"` 包住列表行 | 列表行用 `SySubtleButton`，结构是"图标 + 标题/副标题 + 右箭头" |
| 状态大卡片 | `SyFrostPanel`，`Padding="24,20"`，内容是"56px 图标 + 标题/说明 + 右侧按钮" | 参考[软件更新页](<../Plain Craft Launcher 2/Shell/Settings/UpdateSettingsView.xaml>) |
| 提示条 | `SymbioNotice`（`Theme="Blue/Yellow/Red"`） | 放在相关卡片的上方 |
| 加载中 | `LoadingIndicator` | |
| 图标 | `svgIcon:SvgIcon` / `SettingsCard.Icon`，写作 `lucide/<名称>` | 图标库只收录了 93 个，用之前先确认 `PCL.Core/UI/Assets/IconPacks/lucide/<名称>.svg` 存在，缺的话从 lucide-static 补进来（ISC 许可） |
| 旧的大卡片 | `WorkspaceCard` | 只在还没改的旧页面里保留，新页面不要再用 |

## 页面模板

设置类页面（参考[启动设置页](<../Plain Craft Launcher 2/Shell/Settings/LaunchSettingsView.xaml>)）：

```xml
<ScrollViewer x:Name="PanBack" Style="{StaticResource SyScrollViewer}" local:ScrollInput.Enabled="True"
              VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel x:Name="PanMain" Style="{StaticResource SySettingsPage}">   <!-- 36,24,36,32，最大宽度 1000 -->
        <TextBlock Style="{StaticResource SyPageTitle}" Text="{DynamicResource ...}" />
        <TextBlock Style="{StaticResource SySectionHeader}" Margin="1,0,0,8" Text="第一个分组" />
        <local:SettingsCard Icon="lucide/gauge" Header="..." Description="..." ActionWidth="240">
            <local:SymbioSelect x:Name="..." Tag="配置键" SelectionChanged="ComboChange">...</local:SymbioSelect>
        </local:SettingsCard>
        <local:SettingsCard Icon="lucide/cpu" Header="..." Description="...">
            <local:SymbioCheck Style="{StaticResource SyToggleSwitch}" x:Name="..." Tag="配置键" Change="CheckBoxChange" />
        </local:SettingsCard>
        <TextBlock Style="{StaticResource SySectionHeader}" Text="第二个分组" />
        ...
        <!-- 相关操作：卡片下方的一排按钮 -->
        <WrapPanel Margin="0,8,0,0"> <local:SymbioButton MinHeight="32" Margin="0,0,8,8" ... /> </WrapPanel>
    </StackPanel>
</ScrollViewer>
```

- **页面标题**：直接用二级导航里对应项的文案（`Setup.Left.Item.*` 等），不再新增文案。
- **卡片说明文字**：原控件的 ToolTip 文案通常就是说明，搬到 `Description` 里。说明最多显示两行，完整内容会显示在悬停提示里。
- **"显示 / 隐藏"类子选项**：
  - 放在父卡片下方，用 `Padding="58,12,16,12" MinHeight="52"` 缩进，表示从属关系。
  - 如果代码只控制控件本身的可见性，卡片用 `Visibility="{Binding Visibility, ElementName=控件名}"` 跟着它走。
- **列表页**：页面标题放左边，主要操作放右上角（参考 [Java 页](<../Plain Craft Launcher 2/Shell/Settings/JavaSettingsView.xaml>)），内容放进一个 `SyFrostPanel`。
- **命令栏**：一排按钮放在标题下方，第一个用 `SyAccentButton`（参考[日志页](<../Plain Craft Launcher 2/Shell/Settings/LogSettingsView.xaml>)）。

## 改页面时的规矩

1. **保留所有 `x:Name`、`Tag`（配置键）和事件处理器名。** 代码后台和 `ModSetup`/`ConfigService` 按名字读写，改之前先 `grep` 一下 `.xaml.cs` 里引用了哪些名字。
2. **代码后台调用了 `WorkspaceCard` 专有接口**（`.Title`、`TriggerForceResize()`、`WorkspaceCard.TitleProperty`）的，要做三件事：
   - 把对应分组改成 `StackPanel x:Name="原名"` 加一个带名字的标题 `TextBlock`；
   - 代码里改成给这个 TextBlock 的 `.Text` 赋值；
   - 删掉 `TriggerForceResize()`。

   参考[个性化页](<../Plain Craft Launcher 2/Shell/Settings/AppearanceSettingsView.xaml.cs>)。
3. **只用 `.Visibility` 的容器**（Grid、StackPanel）可以直接换成 `SettingsCard` 或 `WrapPanel`，名字不变。
4. **文案只用现有的本地化键。** 确实要新增时，至少写进 `zh-CN.xaml` 和 `en-US.xaml`；`zh-TW` 和 `en-GB` 缺的键会自动回退。
5. **隐式 TextBlock 样式会覆盖继承来的字体。** 用图标字体（例如 Segoe Fluent Icons）时，必须在 `TextBlock` 上直接写 `FontFamily`。
6. **代码注释用英文。**

## 动效

- 页面切换由 `WorkspacePage` 统一处理，页面里不要自己写进出动画。
- 列表展开、子项出现用 `SymbioMotion.Stagger`。位移、缩放、旋转用 `SymbioMotion.Spring*`，参数取 `MotionTokens` 或 `SymbioMotion.Depth/Pop/Exit`。
- 不要用 `DoubleAnimation` 加缓动曲线，也不要用 PCL 的 `ModAnimation` 来做视觉动画。`ModAnimation` 只用来承载可以被取消的页面状态回调。
- 按下反馈用 `SymbioMotion.AttachPress` 或 `PressFeedback.SetIsEnabled`。

## 构建与验证

```bash
export DOTNET_ROOT=<.NET 10 SDK 目录> PATH=<.NET 10 SDK 目录>:$PATH
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug -p:Platform=x64
```

- **截图前**：启动 Debug 构建时设置 `PCL_DISABLE_DEBUG_HINT=1`，以隐藏调试提示。
- **截图方式**：启动器是单实例程序，测试前先正常关掉其他实例。截图用 `PrintWindow(hwnd, hdc, 2)`，可以避开窗口重叠；用 UI Automation 按控件名切换页面。
- **每改完一页**：
  - 浅色和深色模式各看一次；
  - 换一张花一点的壁纸看文字是否清楚；
  - Dock 的两种模式各看一次；
  - 窗口缩小到最小尺寸时检查有没有溢出。

## 进度与后续任务

| 区域 | 状态 | 方向 |
|---|---|---|
| 外壳 | 已完成 | 磨砂标题栏（与内容之间留 8px）、Windows 11 标题栏按钮、两种 Dock 模式、磨砂 Sheet |
| 共享控件 | 已完成 | 开关、输入框（Fluent 底边）、`SettingsCard` / `SettingsExpander` / `SettingsRow`、`SyFrostPanel`；`WorkspaceCard` 的折叠箭头已修复 |
| 设置 | 10 页里完成 9 页 | **待做：软件信息（AboutSettingsView）**。改成"应用信息状态大卡片 + 致谢 / 许可 / 链接的 `SettingsExpander` 分组" |
| 下载 | 未开始 | 版本目录：顶部放"最新版本"状态卡，下面是正式版 / 预览版 / 远古版 / 愚人节版四个 `SettingsExpander`（行是"图标 + 版本 + 日期 + 安装按钮"）。资源浏览：顶部是搜索框 + 筛选行（下拉框 / 分段按钮），结果放在 `SyFrostPanel` 列表里。资源详情：顶部状态大卡片（图标、名称、作者、主要按钮"安装"），文件按版本分组展开。下载队列：每个任务一行"名称 + 进度条 + 取消"。Java 安装与基岩目录同样处理 |
| 实例 | 未开始 | 实例库按"目录列表 + 实例卡片网格 / 列表"重做。实例概览：顶部状态大卡片（图标、版本、加载器，主要按钮"启动"）加设置卡片。实例设置照搬启动设置页的结构。资源管理、存档、服务器、截图、导出用列表页模板。基岩管理按同样方式处理 |
| 工具 | 未开始 | 工具首页改成图标磁贴网格；皮肤库、披风库是画廊网格；服务器查询、设备信息、投影用设置卡片 / 列表模板 |
| 联机 | 未开始 | 顶部房间状态大卡片 + 加入 / 创建步骤卡片 + 成员列表 |
| 首页与账号 | 部分完成 | 首页卡片已是磨砂；"启动游戏"应改成主题色主要按钮。账号面板改用设置卡片模板 |
| 对话框 | 未开始 | 按 ContentDialog 规格：圆角 8、磨砂浮层、标题 20、底部右对齐按钮（主要按钮在最左） |
| 二级导航 | 待统一 | `Shell/Navigation/Areas/*` 改成 NavigationView 风格的列表项（`SyNavItem`），每项右侧的动作图标只在悬停时显示 |

## 已知问题

- **软件更新页**：`BtnCheckAgain` 设置了 `ColorType="Highlight"`，但显示成普通按钮，需要检查 `SyActionButton` 的触发器或代码后台。
- **`GlassSurface`**：为了让 `SettingsCard` 继承它，去掉了 `sealed`，其他与 LF 版本一致。以后从 LF 同步时，要保留这一处改动。
- **窗口外悬浮的 Dock**：Dock 本身不显示阴影（会被窗口裁掉），只靠描边区分边界。
