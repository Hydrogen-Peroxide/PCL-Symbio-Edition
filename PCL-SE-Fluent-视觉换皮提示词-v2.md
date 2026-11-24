# PCL SE 桌面端 Fluent 视觉换皮提示词（可执行版 v2）

> 用途：交给实现方（人或 AI）直接执行。本文件整体有效，请勿只摘取片段。
> 核心要求：只替换视觉外观，保留全部交互与业务行为；做成大圆角、毛玻璃、壁纸取色的现代 Fluent 风格。

---

## 0. 角色与目标

你是 PCL SE（Plain Craft Launcher Symbio Edition）桌面端的视觉实现者。

在**不改变任何业务逻辑、交互语义、数据流和可观察行为**的前提下，把以下页面的外观改造成现代 Fluent 风格：

- 启动（`PageLaunch`）
- 下载（`PageDownload`）
- 设置（`PageSetup`）
- 更多（`PageTools`）
- 皮肤库（`PageTools/PageToolsSkinLibrary`）

“换皮”的验收定义：

1. 同样的操作产生同样的结果、同样的时序、同样的提示文案。
2. 只允许改视觉树、样式、模板、布局容器与视觉参数。
3. 不允许顺手重构、重命名、抽象、升级依赖或“优化”业务代码。

---

## 1. 视觉参考（截图协议）

原需求中的“对齐截图”只有在截图真实存在时才成立。按以下规则处理：

**需要用户提供的素材（可选，但强烈建议）**

- 浅色、深色各 5 张页面截图（启动 / 下载 / 设置 / 更多 / 皮肤库）。
- 截图条件：窗口 1280×800、100% 缩放、无遮挡、包含标题栏。
- 标注重点：圆角、间距、层级、模糊强度、强调色用法。
- 把截图放在本节下方，并写明“参考”或“必须一致”。

**未提供截图时**

- 禁止在任何交付说明中写“已对齐截图”“与参考图一致”。
- 以本文件第 4 节的令牌和第 5 节的组件规格作为唯一视觉依据。
- 所有自行判断的取舍，必须写入交付说明的「假设与偏差」清单。
- 若截图与令牌冲突：以本文件令牌为准，除非用户以文字明确覆盖。

**提供截图时**

- 仍需按令牌执行；截图用于校准密度、层级和气质，不用于逐像素复刻。
- 验收时输出「与截图差异项」列表，而不是笼统声称“一致”。

```
【视觉参考】
浅色：
深色：
必须一致的项：
允许自由发挥的项：
```

---

## 2. 项目事实基线（已核对，不得推翻）

| 项 | 事实 | 对实现的要求 |
|---|---|---|
| 目标框架 | `net10.0-windows`，`LangVersion` 14，`UseWPF=true` | 技术栈就是 C# / WPF / .NET 10，不要降级，也不要改成 WinUI |
| 仓库结构 | `Plain Craft Launcher 2/` 是 WPF 宿主；`PCL.Core/` 是共享控件与业务库 | 视觉改动优先落在宿主层 |
| 控件命名 | 现有控件统一为 `My*`：`MyButton`、`MyCard`、`MyListItem`、`MyCheckBox`、`MyComboBox`、`MyTextBox`、`MySearchBox`、`MySlider`、`MyIconButton`、`MyIconTextButton`、`MyCollapseBar`、`MyScrollBar`、`MyPageLeft/Right` 等 | **不新建 `SeButton` / `SeCard` / `SeListItem` 控件类**；`Se*` 只用于新增样式资源键和资源字典文件名 |
| 图标系统 | `PCL.Core.UI.Controls.SvgIcon` + `PCL.Core/UI/Assets/IconPacks/lucide/*.svg` | 不引入 Segoe MDL2 Assets，不用 emoji 当图标 |
| 字体 | `Application.xaml` 的 `LaunchFontFamily` = `Resources/#PCL English, Microsoft YaHei UI, Segoe UI` | 不把全局字体改成纯 Segoe UI，避免中文回退异常 |
| 颜色体系 | `Application.xaml` 定义 `ColorBrush1..8`、`ColorBrushGray1..8`、`ColorBrushTransparentBackground`、`ColorBrushHalfWhite`、`ColorBrushSemiWhite`、`ColorBrushSemiTransparent`、`ColorBrushBackground` 等；运行时由 `PCL.Core.UI.Theme.ThemeService` 重写 | 页面与组件只引用这些既有键；不得新增硬编码色值，不得重命名既有键 |
| 主题机制 | `ThemeService.ApplyColorResources()` / `ApplyGrayResources()` 按 `ThemePreset`（OKLCH）刷新资源 | 必须使用 `DynamicResource`，否则切换主题/深色模式不刷新 |
| 模糊能力 | `PCL.Core/UI/Controls/BlurBorder.cs` + `PCL.Core/UI/BackgroundPresenter.cs`（受 EleCho.WpfSuite 启发）实现托管层背景模糊 | 毛玻璃统一走 `BlurBorder`，不要假设 WPF 有原生 AcrylicBrush |
| 模糊配置 | `Config.Preference.Blur.IsEnabled/Radius/SamplingRate/KernelType`；资源键 `BlurRadius`、`BlurSamplingRate`、`BlurType` | 复用现有参数；关闭高级材质时必须能退化为半透明纯色 |
| 壁纸配置 | `Config.Preference.Background.WallpaperOpacity / WallpaperBlurRadius / WallpaperSuitMode / WallpaperCarousel / BackgroundColorful` | 不新增壁纸配置项，不改默认值 |
| 窗口 | `FormMain.xaml` 使用 `WindowChrome`，当前 `CornerRadius=12` | 改窗口圆角前先验证最大化、贴靠、阴影和拖拽命中 |
| 页面枚举 | `FormMain.PageType`：`Launch=0`、`Download=1`、`Setup=2`、`Tools=3`、`Bedrock=13`；皮肤库是 `PageSubType.ToolsSkins` | 皮肤库属于“更多”的子页，不是顶层页面 |
| 缩放 | 已有 `ModBase.GetWPFSize()` 处理 DPI 缩放 | 新增尺寸优先用它，不写死物理像素 |

**明确排除**：基岩版页面（`PageBedrock`）不在本次换皮范围。共享控件改动后需回归验证它没有被破坏，但不要求主动改它的视觉。

---

## 3. 设计令牌（唯一数值来源）

所有新增数值必须集中在 `Plain Craft Launcher 2/Themes/SeFluent.xaml`，页面里只允许引用资源键。

### 3.1 圆角

| 令牌 | 值 | 用途 |
|---|---|---|
| `SeRadiusXs` | 4 | 标签、徽章、复选框内块 |
| `SeRadiusSm` | 8 | 按钮、输入框、列表行、菜单项 |
| `SeRadiusMd` | 12 | 卡片、弹窗、面板、窗口外框；高度 ≥ 40 的主行动按钮 |
| `SeRadiusLg` | 16 | 首屏大容器、整页玻璃面板 |
| `SeRadiusPill` | 999 | 胶囊标签、开关 |

规则：

- 不再出现“所有交互元素 16px”这类全局一刀切；按钮/输入统一 8，卡片统一 12。
- 同一容器内不混用 8 和 16 作为同级圆角。
- 既有元素若已有圆角且不在本次范围，保持原值。

### 3.2 间距与尺寸

- 基础栅格：4。
- 可用间距：4 / 8 / 12 / 16 / 20 / 24 / 32。
- 页面外边距：24；卡片内边距：16（紧凑）/ 20（常规）。
- 控件高度：按钮 32，主行动按钮 40–44，输入框 32，列表行 42，标题栏 40。
- 不允许用负 Margin 修补布局。

### 3.3 字体

| 层级 | 字号 | 字重 | 用途 |
|---|---|---|---|
| Caption | 11 | Normal | 辅助说明、角标 |
| Body | 12 | Normal | 次要信息 |
| BodyStrong | 13 | Normal / SemiBold | 正文、按钮、列表标题 |
| Subtitle | 16 | SemiBold | 分区标题 |
| Title | 20 | SemiBold | 页面标题 |
| Hero | 24 | SemiBold | 仅启动页可选择使用 |

- 字体族一律 `{StaticResource LaunchFontFamily}`。
- 不用 Light / Thin：中文回退字重不可控，易发虚。
- 行高：13 号配 20；16 号配 24。

### 3.4 颜色

只允许引用既有键，语义映射如下：

| 语义 | 资源键 |
|---|---|
| 主文字 | `ColorBrush1` |
| 次文字 | `ColorBrushGray2` |
| 三级文字 | `ColorBrushGray3` |
| 禁用文字 | `ColorBrushGray5` |
| 强调色 | `ColorBrush3` |
| 强调色按下 | `ColorBrush2` |
| 强调色悬停/浅色填充 | `ColorBrush4` / `ColorBrush6` |
| 卡片底 | `ColorBrushTransparentBackground` |
| 浮层底 | `ColorBrushHalfWhite` / `ColorBrushSemiWhite` |
| 分隔线/描边 | `ColorBrushGray6` / `ColorBrushGray7` |
| 错误/警告 | `ColorBrushError` / `ColorBrushWarn` |

规则：

- 组件模板中禁止出现 `#RRGGBB`、`#AARRGGBB` 字面量（`Themes/SeFluent.xaml` 与既有 `Application.xaml` 除外）。
- 所有颜色引用必须是 `DynamicResource`，保证 `ThemeService` 能刷新。
- 禁止给角色名、用户名等文本使用绿色（项目既有 UI 约束）。

### 3.5 材质与模糊

- 使用两级材质：
  - **玻璃层**：`BlurBorder` + 半透明底 + 1px 描边。
  - **实心层**：`ColorBrushTransparentBackground` / `ColorBrushBackground`，用于不支持模糊或需要稳定的区域。
- 模糊参数复用 `BlurRadius` / `BlurSamplingRate` / `BlurType`，不新增参数，不调低采样率默认值。
- `Config.Preference.Blur.IsEnabled=false` 时：全部退化为实心层，文字对比度仍需达标，不能出现“透明卡片叠透明文字”。
- 不做 Reveal 聚光跟随鼠标：WPF 无原生 Reveal，自绘光斑成本高且易掉帧；用 hover 底色 + 描边变化替代。

### 3.6 描边与阴影

- 描边统一 1px（高 DPI 下允许 1 逻辑像素），颜色 `ColorBrushGray6` / `ColorBrushGray7`。
- 阴影统一使用既有 `MyDropShadow`：静置透明度约 0.06，悬停约 0.12，圆角与容器一致。
- 不用彩色光晕、不用大面积 `DropShadowEffect` 叠加，避免滚动掉帧。

### 3.7 动效

| 场景 | 时长 | 缓动 |
|---|---|---|
| 悬停 / 按下 | 120ms | CubicEaseOut |
| 展开 / 折叠 | 180ms | CubicEaseOut |
| 页面 / 弹窗进入 | 180–240ms | CubicEaseOut |

- 只做透明度、背景色、位移 ≤ 4px、缩放 0.98–1.02 的变化。
- 所有列表项动效必须可被虚拟化回收，不能持有常驻动画。
- 动画实现二选一：既有 `PCL.Core.UI.Animation` 引擎，或 XAML `Storyboard`；同一元素禁止混用。
- 不引入 `Windows.UI.Composition`：WPF 需额外 COM 互操作，生命周期与 DPI 处理复杂，且与现有动画引擎重复。
- 不做循环呼吸、流光、粒子等持续动画。

### 3.8 图标

- 统一 `SvgIcon` + lucide 线性图标。
- 尺寸：密集列表 16，工具栏 20，页面标题 24。
- 颜色继承文字色；选中态可用 `ColorBrush3`。
- 新图标放 `Plain Craft Launcher 2/Images/`，保持 24×24 viewBox、线性描边风格；不改 `PCL.Core` 内置图标包。

---

## 4. 组件规格

> 下表中的 `SeButton` / `SeCard` / `SeListItem` 是**样式名**，对应既有 `MyButton` / `MyCard` / `MyListItem` 控件，不是新控件类。

| 组件 | 规格 |
|---|---|
| `MyButton` | 高度 32；`SeRadiusSm`；水平内边距 12；字号 13；三种变体：Primary（`ColorBrush3` 底 + 白字）、Secondary（`ColorBrushHalfWhite` 底 + `ColorBrush1` 字 + 1px 描边）、Subtle（透明底，悬停 6% 白/黑叠层）；按下缩放 0.98 |
| 主行动按钮 | 高度 40–44；`SeRadiusMd`；其余同 Primary |
| `MyIconButton` | 32×32；`SeRadiusSm`；图标 16；悬停底色；不改变原有点击区域语义 |
| `MyIconTextButton` | 高度 36；图标 16 + 文字 13；间距 6 |
| `MyCard` | `SeRadiusMd`；`BlurBorder`；1px 描边；静置阴影 0.06；内边距 16；标题 14 SemiBold；悬停阴影 0.12，不做位移 |
| `MyListItem` | 高度 42；`SeRadiusSm`；悬停底色 `ColorBrush7`（约 60%）；选中底色 `ColorBrush6` + 左侧 2px `ColorBrush3` 指示条；保留原有列结构 |
| `MyTextBox` / `MySearchBox` | 高度 32；`SeRadiusSm`；1px 描边；聚焦时 2px `ColorBrush4` 焦点环；占位文字 `ColorBrushGray3` |
| `MyComboBox` | 高度 32；`SeRadiusSm`；弹层 `SeRadiusSm` + 阴影；选项高度 32 |
| `MyCheckBox` / `MyRadioButton` | 指示块 18；`SeRadiusXs`（单选为圆形）；选中 `ColorBrush3`；禁用降到 38% 不透明度 |
| `MySlider` | 轨道 4px，`SeRadiusPill`；滑块 16；悬停放大到 18 |
| `MyScrollBar` | 宽 6；`SeRadiusPill`；默认 `ColorBrushGray5`，悬停 `ColorBrushGray4` |
| `MyHint` / `MyToast` / `MyMsg*` | `SeRadiusMd`；玻璃底；阴影同卡片；文字 13 |
| `MyCollapseBar` | 头部 `SeRadiusSm`；展开内容不额外套卡片 |
| `MyPageLeft` / `MyPageRight` | 左侧栏半透明玻璃；右侧内容区实心底；保持原有滚动与边距职责 |

通用状态：

- 悬停：底色变化或描边变化，二者择一，不同时叠加。
- 按下：缩放 0.98 或底色加深，二选一。
- 禁用：不透明度 38%，不改变布局尺寸。
- 焦点：必须有可见焦点环，键盘 Tab 可达。

---

## 5. 页面级要求

| 页面 | 目录 | 重点 | 禁区 |
|---|---|---|---|
| 启动 | `Pages/PageLaunch` | 壁纸可见；账号卡、版本卡、启动按钮使用玻璃层；主按钮 44 高；保留原有信息密度 | 不改启动流程、版本选择逻辑、账号状态机 |
| 下载 | `Pages/PageDownload` | 搜索框、分类标签、资源卡统一圆角与间距；列表虚拟化保持有效 | 不在虚拟化项内新增模糊层；不改下载/安装任务逻辑 |
| 设置 | `Pages/PageSetup` | 分区卡片化；控件密度不变；滚动位置逻辑不变 | 不改配置键、默认值、保存时机、重启提示逻辑 |
| 更多 | `Pages/PageTools` | 左侧导航选中态胶囊化；内容区卡片化 | 不改页面切换、工具入口、外部链接行为 |
| 皮肤库 | `Pages/PageTools/PageToolsSkinLibrary` | 只改外层容器、列表、预览框描边与圆角 | 不包裹、不模糊、不缩放 3D 预览/位图渲染区域；不改预览尺寸计算、模型切换、保存逻辑 |

补充：

- 皮肤库的 3D 预览、地图编辑器、投影预览等自绘/位图区域，一律不套 `BlurBorder`、不加 `RenderTransform`、不改裁剪，避免渲染与命中测试异常。
- 页面切换动画只作用于内容容器，不作用于 `WindowChrome` 或标题栏拖拽区域。
- 所有页面在 100% / 125% / 150% DPI 下不得出现文字截断、1px 缝隙、圆角锯齿明显。

---

## 6. 允许修改 / 禁止修改

### 6.1 白名单（只能改这些）

| 路径 | 允许内容 |
|---|---|
| `Plain Craft Launcher 2/Themes/**` | 新增资源字典（令牌、样式、模板） |
| `Plain Craft Launcher 2/Application.xaml` | 合并新字典、补充令牌资源；不改既有键名与语义 |
| `Plain Craft Launcher 2/Controls/My*.xaml`、`My*.xaml.cs` | 只改模板、视觉状态、视觉属性；不改公开成员签名与事件语义 |
| `Plain Craft Launcher 2/Controls/ControlVisualHelpers.cs`、`MyDropShadow.cs` | 仅视觉辅助 |
| `Plain Craft Launcher 2/Pages/{PageLaunch,PageDownload,PageSetup,PageTools}/**` | 只改 XAML 的容器、间距、圆角、背景、字体层级 |
| `Plain Craft Launcher 2/FormMain.xaml` | 只改外框、背景、圆角等视觉属性 |
| `Plain Craft Launcher 2/Images/**` | 仅新增图标资源 |

### 6.2 黑名单（一行都不许改）

| 路径 / 对象 | 原因 |
|---|---|
| `Plain Craft Launcher 2/Modules/**` | 业务逻辑、下载、启动、更新、配置监听 |
| `PCL.Core/**` | 共享业务、主题计算、配置、动画引擎 |
| `PCL.Core.SourceGenerators/**` | 构建期代码生成 |
| `Plain Craft Launcher 2/Pages/**/*.xaml.cs` | 页面事件与状态机 |
| `Plain Craft Launcher 2/FormMain.xaml.cs` | 导航、窗口行为、页面生命周期 |
| `PCL.Core/App/Config.cs`、`**/Localization/**` | 配置键与文案 |
| `*.csproj`、`*.slnx`、`global.json`、`build-local.ps1`、`.github/**` | 构建与 CI |
| `Plain Craft Launcher 2/Resources/**` | 二进制资源（jar / exe / 字体） |
| 公开 API、类名、`x:Name`、事件处理器名、绑定路径、命令名、配置键、本地化键 | 破坏调用方或主题刷新 |
| 新增 NuGet 包、在线字体、远程图片、CDN 资源 | 引入不可控依赖 |

### 6.3 例外流程

如果某个视觉效果**必须**改 `PCL.Core` 才能实现：

1. 停止实现，先说明“为什么现有宿主层能力不足”。
2. 给出最小改动方案与影响面。
3. 得到明确同意后再动，且只能做加法（新增资源/属性），不改既有行为。

---

## 7. 待验证事实（不得写成结论）

以下内容在实现和交付说明中必须标注“待验证”，不能当作既定事实：

| 主题 | 现状 | 处理方式 |
|---|---|---|
| DWM 亚克力 / Acrylic | 仓库现有方案是托管层 `BlurBorder`，不是系统 Acrylic API | 默认不使用 DWM 亚克力；如坚持尝试，需在 Win10 22H2 与 Win11 上实测，并保留 `BlurBorder` 回退 |
| 窗口圆角 | `WindowChrome.CornerRadius=12` 只影响 chrome 区域语义，不等于系统窗口外观圆角 | 改圆角后必须实测最大化、贴靠、阴影、拖拽；不得断言“系统一定圆角 16” |
| 壁纸动态取色 | 现有强调色来自 `ThemePreset`（OKLCH），没有从壁纸取色的既有实现 | 列为可选阶段；若实现，必须降采样、缓存、异步、失败回退主题预设色；不新增持久化配置 |
| 壁纸为视频时的取色 | 存在 `AutoPauseVideo` 配置，视频背景可能启用 | 视频取色不在本次范围；仅静态图可尝试 |
| 模糊性能 | `BlurSamplingRate` 默认 70 | 不调低；低端核显需实测滚动与页面切换 |
| 高 DPI 1px 描边 | 不同缩放比下可能发虚 | 以 100/125/150% 实测为准，必要时用 `UseLayoutRounding` 修正，不全局改动既有元素 |

---

## 8. 交付物

1. 代码改动（严格落在白名单内）。
2. 新增资源字典：`Plain Craft Launcher 2/Themes/SeFluent.xaml`。
3. 交付说明，包含：
   - 改动文件清单（逐条说明改了什么、为什么）。
   - 令牌表（圆角、间距、字号、颜色映射）。
   - 「假设与偏差」清单：哪些是自行判断，哪些与截图/原文不同。
   - 「待验证事实」清单及实测结果。
   - 验收结果：构建、页面清单、主题/DPI 覆盖情况。
4. 截图证据：浅色/深色各 5 页，100% 缩放，构建后真机运行截图。

不要求新增文档站页面，不要求改 README，不要求发版。

---

## 9. 验收标准

### 9.1 构建

按仓库既有方式构建，至少满足：

```powershell
dotnet build "Plain Craft Launcher 2.slnx" -c Debug -p:Platform=x64
```

或使用既有脚本：

```powershell
.\build-local.ps1 -Configuration Debug -OutputDir .\artifact-debug
```

判定：0 error；修改文件不引入新 warning；不因缺密钥而改代码绕过。

### 9.2 视觉

- 5 个页面在浅色 / 深色 / 跟随系统下均正常。
- 强调色切换后，按钮、选中态、焦点环同步刷新，无需重启。
- 圆角、间距、字号符合第 3、4 节令牌。
- 玻璃层后方内容可见且不过度干扰文字；关闭模糊后可读性不下降。
- 100% / 125% / 150% DPI 下无截断、无 1px 缝隙、无圆角毛刺。

### 9.3 交互回归

逐项确认：

- 启动页：版本选择、账号操作、启动按钮、页面切换。
- 下载页：搜索、筛选、进入详情、任务创建与进度显示。
- 设置页：开关、下拉、滑块、保存与重启提示。
- 更多页：侧栏切换、工具入口、皮肤库入口。
- 皮肤库：列表选择、模型切换、3D 预览旋转缩放、保存。
- 键盘：Tab 顺序不变，焦点可见，Enter/Space 行为不变。
- 窗口：拖拽、双击最大化、贴靠、最小化、关闭。

判定：行为与改动前一致；只允许视觉差异。

### 9.4 负面清单判定方式

每条都必须给出可执行的检查结果，不能只写“已遵守”。

| 编号 | 禁止项 | 检查方法 |
|---|---|---|
| N1 | 改业务逻辑 | `git diff --name-only` 结果全部落在白名单；`git diff -- "Plain Craft Launcher 2/Modules" "PCL.Core"` 为空 |
| N2 | 改事件/绑定/命名 | 对改动 XAML 的新增行执行 `Select-String -Pattern 'x:Name=\|Click=\|Command=\|Binding \|SelectionChanged='`，应无新增 |
| N3 | 硬编码颜色/圆角 | 对白名单内 XAML 新增行执行 `Select-String -Pattern '#[0-9A-Fa-f]{6,8}\|CornerRadius="[0-9]'`，只允许出现在 `Themes/` 与 `Application.xaml` |
| N4 | 改配置或文案 | `git diff -- "PCL.Core/App/Config.cs" "**/Localization/**"` 为空；无新增本地化键 |
| N5 | 改构建/依赖 | `git diff --name-only -- "*.csproj" "*.slnx" "global.json" "build-local.ps1" ".github"` 为空 |
| N6 | 引入未验证 API | 全仓搜索新增代码中不得出现 `Windows.UI.Composition`、`SetWindowCompositionAttribute`、`DwmSetWindowAttribute` |
| N7 | 模糊不可关闭 | 关闭高级材质后重启程序，所有页面仍可读、无透明叠透明 |
| N8 | 主题不刷新 | 运行中切换浅色/深色/跟随系统与强调色，颜色即时生效 |
| N9 | 高 DPI/窗口异常 | 100/125/150% 下最大化、还原、贴靠各一次，无裁切与缝隙 |
| N10 | 性能退化 | 下载列表滚动、卡片悬停、页面切换无可感知卡顿；同页 `BlurBorder` 数量不超过改动前基线 + 2 |

建议直接使用以下命令做白名单核对：

```powershell
git diff --name-only | Where-Object {
    $_ -notmatch '^(Plain Craft Launcher 2/(Application\.xaml|Controls/|Pages/(PageLaunch|PageDownload|PageSetup|PageTools)/|Themes/|Images/|FormMain\.xaml))'
}
```

输出为空才算通过。

---

## 10. 执行顺序与汇报点

1. **只读盘点**：列出 5 个页面现有视觉结构、控件使用情况、模糊与颜色来源；不改代码。
2. **令牌与资源字典**：落地 `SeFluent.xaml`，确认主题切换可刷新。
3. **基础控件**：先改 `MyButton`、`MyIconButton`、`MyCard`、`MyListItem`、输入类控件；每改一类即构建。
4. **页面套用**：按启动 → 下载 → 设置 → 更多 → 皮肤库顺序；每页完成即构建并自查。
5. **回归与验收**：按第 9 节逐项执行，记录结果。
6. **交付说明**：文件清单、假设与偏差、待验证事实、截图。

每个阶段结束都需给出简短进度说明；遇到需要动 `PCL.Core` 或无法满足的视觉要求时，先停下来说明，不要自行扩大范围。

---

## 11. 不确定时的决策规则

按优先级从高到低：

1. 不改变行为。
2. 不扩大改动范围。
3. 不引入新依赖、新配置、新公开 API。
4. 视觉上优先保证可读性、层级清晰、键盘可用。
5. 无法判断时，选择更保守的实现，并写入「假设与偏差」。

一句话约束：**这是一次换皮，不是一次重构。**
