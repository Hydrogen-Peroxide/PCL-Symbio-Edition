// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCLLF.Wpf.Motion;

/// <summary>
/// Inertial wheel scrolling for pixel-based <see cref="ScrollViewer"/>s: each notch moves a target offset and the
/// viewport glides towards it with an exponential ease (fast start, soft landing). Consecutive notches accumulate,
/// so flicking the wheel builds speed. Nested scrollers hand the wheel to their parent at either end. Reduced motion,
/// item-based scrolling (CanContentScroll) and touchpad horizontal input keep the default behaviour.
/// </summary>
public static class SmoothScroll {
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(SmoothScroll));

    /// <summary>Pixels per wheel notch (120 delta units).</summary>
    private const double NotchDistance = 96;
    /// <summary>Time constant of the glide; about 95% of the distance is covered in three of these.</summary>
    private const double GlideSeconds = .085;
    private static int _activeCount;
    public static bool IsScrolling => _activeCount > 0;

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void Stop(ScrollViewer viewer) => (viewer.GetValue(StateProperty) as State)?.Stop();

    public static void ScrollBy(ScrollViewer viewer, double delta) {
        if (viewer.GetValue(StateProperty) is not State state) {
            state = new State(viewer, handleWheel: false);
            viewer.SetValue(StateProperty, state);
        }
        state.ScrollBy(delta);
    }

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) {
        if (element is not ScrollViewer scroll) return;
        (scroll.GetValue(StateProperty) as State)?.Dispose();
        scroll.ClearValue(StateProperty);
        if ((bool)args.NewValue) scroll.SetValue(StateProperty, new State(scroll));
    }

    private sealed class State : IDisposable {
        private readonly ScrollViewer _scroll;
        private readonly Stopwatch _clock = new();
        private double _target, _position, _last, _written = double.NaN;
        private bool _running, _writing;
        private MotionFrameLimit _frameLimit;

        internal State(ScrollViewer scroll, bool handleWheel = true) {
            _scroll = scroll;
            if (handleWheel) scroll.PreviewMouseWheel += OnWheel;
            scroll.ScrollChanged += OnScrollChanged;
            scroll.Unloaded += OnUnloaded;
        }

        private void OnWheel(object sender, MouseWheelEventArgs args) {
            if (args.Handled || _scroll.CanContentScroll || !MotionPreferences.AllowsAnimation || _scroll.ScrollableHeight <= 0) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            // Preview events tunnel outer-first: leave the wheel to a nested scroller under the pointer.
            // An inner scroller already at its end in the wheel direction passes the wheel on to this one.
            if (NearestScroller(args.OriginalSource as DependencyObject, args.Delta) is { } inner && !ReferenceEquals(inner, _scroll)) return;
            double start = _running ? _target : _scroll.VerticalOffset;
            double next = Math.Clamp(start - args.Delta / 120d * NotchDistance, 0, _scroll.ScrollableHeight);
            // At an end, let an outer scroller take the wheel instead of swallowing it.
            if (Math.Abs(next - start) < .5 && !_running) return;
            args.Handled = true;
            GlideTo(next);
        }

        internal void ScrollBy(double delta) {
            var next = Math.Clamp((_running ? _target : _scroll.VerticalOffset) + delta, 0, _scroll.ScrollableHeight);
            if (!MotionPreferences.AllowsAnimation || !_scroll.IsVisible) {
                Stop();
                _scroll.ScrollToVerticalOffset(next);
                return;
            }
            GlideTo(next);
        }

        private void GlideTo(double next) {
            _target = next;
            if (!_running) {
                _position = _scroll.VerticalOffset;
                _running = true;
                _activeCount++;
                _clock.Restart(); _last = 0;
                _frameLimit = default;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        private void OnFrame(object? sender, EventArgs args) {
            double now = _clock.Elapsed.TotalSeconds;
            bool animate = MotionPreferences.AllowsAnimation;
            if (animate && !_frameLimit.Advance(now)) return;
            double dt = Math.Clamp(now - _last, 0, Math.Max(.05, MotionPreferences.FrameInterval));
            _last = now;
            _target = Math.Clamp(_target, 0, _scroll.ScrollableHeight);
            _position = animate
                ? _position + (_target - _position) * (1 - Math.Exp(-dt * MotionPreferences.PlaybackRate / GlideSeconds))
                : _target;
            if (Math.Abs(_target - _position) < .5) { _position = _target; Stop(); }
            _writing = true;
            try { _written = _position; _scroll.ScrollToVerticalOffset(_position); }
            finally { _writing = false; }
        }

        // Scrollbar drags, keyboard and programmatic scrolls take over immediately.
        private void OnScrollChanged(object sender, ScrollChangedEventArgs args) {
            if (_writing || !_running || Math.Abs(args.VerticalChange) < .01) return;
            // Offsets apply on the next layout pass, so compare with what this glide last wrote.
            if (Math.Abs(_scroll.VerticalOffset - _written) > 1) { _position = _target = _scroll.VerticalOffset; Stop(); }
        }

        internal void Stop() {
            if (!_running) return;
            _running = false;
            if (_activeCount > 0) _activeCount--;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnUnloaded(object sender, RoutedEventArgs args) => Stop();

        private static ScrollViewer? NearestScroller(DependencyObject? source, int delta) {
            for (DependencyObject? item = source; item is not null; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item is ScrollViewer { ScrollableHeight: > 0 } viewer && GetIsEnabled(viewer) && CanScroll(viewer, delta)) return viewer;
            return null;
        }

        private static bool CanScroll(ScrollViewer viewer, int delta) => delta > 0
            ? viewer.VerticalOffset > .5
            : viewer.VerticalOffset < viewer.ScrollableHeight - .5;

        public void Dispose() {
            Stop();
            _scroll.PreviewMouseWheel -= OnWheel;
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll.Unloaded -= OnUnloaded;
        }
    }
}
