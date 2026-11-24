// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Windows.Media;

namespace PCLLF.Wpf.Glass;

/// <summary>A presentation snapshot. The shell never reads launcher settings or game data.</summary>
public sealed class GlassLauncherViewState
{
    public bool IsHome { get; init; } = true;
    public int SelectedNavigationId { get; init; }
    public string PageTitle { get; init; } = string.Empty;
    public bool CanGoBack { get; init; }
    public string AccountName { get; init; } = "未登录";
    public ImageSource AccountAvatar { get; init; }
    public bool HasInstance { get; init; }
    public string InstanceName { get; init; } = "选择实例";
    public string InstanceDescription { get; init; } = string.Empty;
    public ImageSource InstanceIcon { get; init; }
    public bool CanLaunch { get; init; }
    public bool IsLaunching { get; init; }
    public bool LaunchOptionsAvailable { get; init; } = true;
    public string LaunchText { get; init; } = "启动游戏";
    public string LaunchStatus { get; init; } = string.Empty;
    public double? LaunchProgress { get; init; }
    public bool DependencyCheckEnabled { get; init; } = true;
    public bool AutoFillEnabled { get; init; } = true;
    public bool RecentWorldAvailable { get; init; }
    public string RecentWorldName { get; init; } = string.Empty;
    public string RecentWorldDetail { get; init; } = string.Empty;
    public ImageSource RecentWorldIcon { get; init; }
    public bool UseDarkText { get; init; }
    public bool ReduceMotion { get; init; }
}

/// <summary>A contextual navigation action supplied by the page adapter.</summary>
public sealed class GlassSidebarItem
{
    public string Label { get; init; } = string.Empty;
    public string ToolTip { get; init; }
    public string IconData { get; init; }
    public ImageSource Image { get; init; }
    public bool IsSelected { get; init; }
    public bool IsEnabled { get; init; } = true;
    public Action Invoked { get; init; }
}
