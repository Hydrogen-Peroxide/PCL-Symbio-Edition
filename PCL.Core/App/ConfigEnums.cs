namespace PCL.Core.App;

/// <summary>
/// 主题模式（亮/暗/系统）
/// </summary>
public enum ColorMode
{
    Light = 0,
    Dark = 1,
    System = 2
}

/// <summary>
/// 配色主题
/// </summary>
public enum ColorTheme
{
    SkyBlue = 0,
    CatBlue = 1,
    DeathBlue = 2,
    HmclBlue = 3,

    /// <summary>
    /// 归藏设计 · 克莱因：纸白界面、墨黑文字，以国际克莱因蓝（IKB）作唯一强调色。
    /// </summary>
    KleinBlue = 4
}

/// <summary>
/// 更新通道
/// </summary>
public enum UpdateChannel
{
    Release = 0,
    Beta = 1,
    Dev = 2
}

/// <summary>
/// 游戏窗口大小模式
/// </summary>
public enum GameWindowSizeMode
{
    Fullscreen = 0,
    Default = 1,
    Launcher = 2,
    Custom = 3,
    Maximized = 4
}

/// <summary>
/// 游戏进程优先级
/// </summary>
public enum GameProcessPriority
{
    AboveNormal = 0,
    Normal = 1,
    BelowNormal = 2,
    High = 3,
    RealTime = 4
}

/// <summary>
/// 游戏启动后启动器可见性
/// </summary>
public enum LauncherVisibility
{
    ExitImmediately = 0,
    ObsoleteCaseDoNotUse = 1,
    HideAndExit = 2,
    HideAndReopen = 3,
    MinimizeAndReopen = 4,
    DoNothing = 5
}

/// <summary>
/// JVM 优先 IP 栈类型
/// </summary>
public enum JvmPreferredIpStack
{
    PreferV4 = 0,
    Default = 1,
    PreferV6 = 2
}

/// <summary>
/// 启动器更新行为
/// </summary>
public enum LauncherAutoUpdateBehavior
{
    DownloadAndInstall = 0,
    DownloadAndAnnounce = 1,
    AnnounceOnly = 2,
    Disable = 3
}

/// <summary>
/// 启动器更新源。
/// 自动模式下会按顺序尝试各个源，前一个源取不到可用版本时自动回退到下一个源。
/// </summary>
public enum LauncherUpdateSource
{
    /// <summary>
    /// CNB 优先，失败时回退 GitHub。
    /// </summary>
    CnbFirst = 0,

    /// <summary>
    /// GitHub 优先，失败时回退 CNB。
    /// </summary>
    GitHubFirst = 1,

    /// <summary>
    /// 仅使用 CNB，不回退。
    /// </summary>
    CnbOnly = 2,

    /// <summary>
    /// 仅使用 GitHub，不回退。
    /// </summary>
    GitHubOnly = 3
}

public enum LauncherTitleType
{
    None = 0,
    Default = 1,
    Text = 2,
    Image = 3
}
