using System.Windows.Media;

namespace PCL;

/// <summary>Top-level destinations shown in the dock, in visual order.</summary>
public enum SymbioDestination
{
    Launch = 0,
    Download = 1,
    Lan = 2,
    SkinLibrary = 3,
    Setup = 4,
    Tools = 5
}

/// <summary>
///     Immutable presentation snapshot for <see cref="SymbioShell" />. The shell never reads launcher
///     settings or game data itself; FormMain composes this from the legacy pages and modules.
/// </summary>
public sealed record SymbioShellState
{
    public bool IsHome { get; init; } = true;
    public SymbioDestination Destination { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public bool CanGoBack { get; init; }
    public bool ShowSidebar { get; init; } = true;
    public double SidebarWidth { get; init; }

    public bool IsBedrock { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string AccountDetail { get; init; } = string.Empty;
    public ImageSource? AccountAvatar { get; init; }

    public bool HasInstance { get; init; }
    public string InstanceName { get; init; } = string.Empty;
    public string InstanceDescription { get; init; } = string.Empty;
    public ImageSource? InstanceIcon { get; init; }

    public bool CanLaunch { get; init; }
    public string LaunchText { get; init; } = string.Empty;
    public bool IsLaunching { get; init; }
    public bool CanCancelLaunch { get; init; }
    public string LaunchStatus { get; init; } = string.Empty;
    public double? LaunchProgress { get; init; }
}
