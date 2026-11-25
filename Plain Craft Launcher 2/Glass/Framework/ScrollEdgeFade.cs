// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCLLF.Wpf.Glass;

/// <summary>
/// Hosts a <see cref="ScrollViewer"/> and softly fades its content out at the top and bottom edges while more content
/// continues beyond them. The fade is a single opacity mask on the content viewport (no blurred copies, so there are no ghost
/// images or strip boundaries): a smoothstep profile from fully transparent at the edge to opaque over the fade length,
/// whose strength grows with the distance still scrollable past that edge. High contrast shows the plain scroller.
/// </summary>
public sealed class ScrollEdgeFade : Grid {
    private const int Samples = 9;
    /// <summary>Scroll distance over which an edge fades in, so the fade never pops.</summary>
    private const double RampDistance = 80;
    private readonly ScrollViewer _scroll;
    private readonly double _topLength, _bottomLength;
    private UIElement? _viewport;
    private readonly LinearGradientBrush _mask = new() { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0) };

    public ScrollEdgeFade(ScrollViewer scroll, double topLength = 56, double bottomLength = 48) {
        ArgumentNullException.ThrowIfNull(scroll);
        _scroll = scroll;
        _topLength = topLength;
        _bottomLength = bottomLength;
        for (int index = 0; index < Samples * 2; index++) _mask.GradientStops.Add(new GradientStop(Colors.Black, 0));
        Children.Add(scroll);
        scroll.ScrollChanged += (_, _) => Update();
        scroll.SizeChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
    }

    public ScrollViewer ScrollViewer => _scroll;

    private void Update() {
        var viewport = _scroll.Template?.FindName("PART_ScrollContentPresenter", _scroll) as FrameworkElement;
        if (!ReferenceEquals(viewport, _viewport))
        {
            if (_viewport is not null && ReferenceEquals(_viewport.OpacityMask, _mask)) _viewport.OpacityMask = null;
            _viewport = viewport;
        }
        if (viewport is null) return;
        double height = viewport.ActualHeight;
        if (!IsLoaded || height <= 1 || SystemParameters.HighContrast) { viewport.OpacityMask = null; return; }
        double top = Smooth(_scroll.VerticalOffset / RampDistance);
        double bottom = Smooth(Math.Max(0, _scroll.ScrollableHeight - _scroll.VerticalOffset) / RampDistance);
        if (top <= .005 && bottom <= .005) { viewport.OpacityMask = null; return; }
        double topLength = Math.Min(_topLength, height / 3), bottomLength = Math.Min(_bottomLength, height / 3);
        _mask.EndPoint = new Point(0, height);
        for (int index = 0; index < Samples; index++) {
            double t = index / (double)(Samples - 1);   // 0 at the edge … 1 at the inner end of the fade
            double visible = Smooth(t);                  // eased: soft at both ends of the fade band
            // Stop offsets are fractions of the gradient line even with absolute endpoints.
            Set(index, topLength * t / height, 1 - top * (1 - visible));
            Set(Samples * 2 - 1 - index, (height - bottomLength * t) / height, 1 - bottom * (1 - visible));
        }
        if (!ReferenceEquals(viewport.OpacityMask, _mask)) viewport.OpacityMask = _mask;

        void Set(int index, double offset, double alpha) {
            _mask.GradientStops[index].Offset = Math.Clamp(offset, 0, 1);
            _mask.GradientStops[index].Color = Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), 0, 0, 0);
        }
    }

    private static double Smooth(double value) { double t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
}
