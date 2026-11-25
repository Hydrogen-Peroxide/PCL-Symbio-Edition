// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCLLF.Wpf.Motion;

/// <summary>
/// Staggered list entrance: each item fades in while rising a few pixels and growing from 96 %, one after another.
/// Timing follows the list entrance of BlockHelm Launcher (330 ms cubic ease-out, 30 ms steps, at most 12 steps);
/// independent implementation.
/// <para>
/// The start state is applied synchronously, so items never flash at rest before the cascade. The cascade is
/// frame-driven: time starts at the first rendered frame and each frame advances at most <see cref="MaxFrame"/>, so a
/// stalled UI thread pauses the entrance instead of skipping it. Original opacity and transforms are restored when an
/// item finishes or when it is replayed; reduced motion leaves items untouched.
/// </para>
/// </summary>
public static class StaggeredEntrance {
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(330);
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(30);
    public const int MaxSteps = 12;
    public const double StartScale = .96, Rise = 14;
    private const double MaxFrame = 1 / 40d;

    private static readonly List<Entry> Active = new();
    private static readonly Dictionary<UIElement, Entry> ByElement = new(ReferenceEqualityComparer.Instance);
    private static double _last = double.NaN;
    private static MotionFrameLimit _frameLimit;

    /// <summary>True while any entrance is still playing.</summary>
    public static bool IsPlaying => Active.Count > 0;

    public static void Play(IEnumerable<UIElement> items) {
        if (!MotionPreferences.AllowsAnimation) return;
        int index = 0;
        foreach (UIElement item in items) {
            if (item.Visibility != Visibility.Visible) continue;
            if (ByElement.TryGetValue(item, out Entry? previous)) { previous.Restore(); Active.Remove(previous); }
            // Long lists only stagger the first screenful; later items enter with the last step.
            var entry = new Entry(item, Step.TotalSeconds * Math.Min(index, MaxSteps));
            Active.Add(entry); ByElement[item] = entry;
            index++;
        }
        if (Active.Count > 0 && double.IsNaN(_last)) {
            _last = double.NegativeInfinity; // first rendered frame sets the time origin
            _frameLimit = default;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>The first panel under <paramref name="root"/> that lays out several visible items (cards, rows).</summary>
    public static Panel? FindItemsPanel(DependencyObject root, int maxDepth = 10) {
        var queue = new Queue<(DependencyObject Node, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0) {
            (DependencyObject node, int depth) = queue.Dequeue();
            if (node is Panel panel && panel.Children.OfType<UIElement>().Count(child => child.Visibility == Visibility.Visible) >= 2) return panel;
            if (depth >= maxDepth) continue;
            int count = node is Visual ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (int i = 0; i < count; i++) queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }

    private static void OnFrame(object? sender, EventArgs args) {
        if (!MotionPreferences.AllowsAnimation) {
            while (Active.Count > 0) Cancel(Active[^1]);
            return;
        }
        double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        if (!_frameLimit.Advance(now)) return;
        double step = double.IsNegativeInfinity(_last) ? 0 :
            Math.Clamp(now - _last, 0, Math.Max(MaxFrame, MotionPreferences.FrameInterval)) * MotionPreferences.PlaybackRate;
        _last = now;
        for (int index = Active.Count - 1; index >= 0; index--) {
            Entry entry = Active[index];
            if (entry.Advance(step)) continue;
            Active.RemoveAt(index);
            if (ByElement.TryGetValue(entry.Element, out Entry? current) && ReferenceEquals(current, entry)) ByElement.Remove(entry.Element);
        }
        if (Active.Count > 0) return;
        CompositionTarget.Rendering -= OnFrame;
        _last = double.NaN;
    }

    private static void Cancel(Entry entry) {
        entry.Restore(); Active.Remove(entry);
        if (ByElement.TryGetValue(entry.Element, out Entry? current) && ReferenceEquals(current, entry)) ByElement.Remove(entry.Element);
        if (Active.Count != 0) return;
        CompositionTarget.Rendering -= OnFrame; _last = double.NaN;
    }

    private sealed class Entry {
        private readonly object _localOpacity;
        private readonly double _targetOpacity;
        private readonly Transform _transform;
        private readonly Point _origin;
        private readonly TransformGroup _group = new();
        private readonly ScaleTransform _scale = new(StartScale, StartScale);
        private readonly TranslateTransform _rise = new(0, Rise);
        private readonly double _delay;
        private double _elapsed;
        private bool _done;
        internal UIElement Element { get; }

        internal Entry(UIElement element, double delay) {
            Element = element;
            _delay = delay;
            _localOpacity = element.ReadLocalValue(UIElement.OpacityProperty);
            _targetOpacity = element.Opacity; // disabled or dimmed items keep their own resting opacity
            _transform = element.RenderTransform;
            _origin = element.RenderTransformOrigin;
            if (_transform is not null && !_transform.Value.IsIdentity) _group.Children.Add(_transform);
            _group.Children.Add(_scale);
            _group.Children.Add(_rise);
            element.RenderTransformOrigin = new Point(.5, .5);
            element.RenderTransform = _group;
            element.Opacity = 0;
            if (element is FrameworkElement framework) framework.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs args) => Cancel(this);

        /// <returns>False once the item has finished and its original state is restored.</returns>
        internal bool Advance(double step) {
            if (_done) return false;
            if (!ReferenceEquals(Element.RenderTransform, _group)) { Restore(); return false; }
            _elapsed += step;
            double t = Math.Clamp((_elapsed - _delay) / Duration.TotalSeconds, 0, 1);
            double eased = 1 - Math.Pow(1 - t, 3);
            Element.Opacity = _targetOpacity * eased;
            _scale.ScaleX = _scale.ScaleY = StartScale + (1 - StartScale) * eased;
            _rise.Y = Rise * (1 - eased);
            if (t < 1) return true;
            Restore();
            return false;
        }

        internal void Restore() {
            if (_done) return;
            _done = true;
            if (Element is FrameworkElement framework) framework.Unloaded -= OnUnloaded;
            if (_localOpacity == DependencyProperty.UnsetValue) Element.ClearValue(UIElement.OpacityProperty);
            else if (_localOpacity is System.Windows.Data.BindingExpressionBase binding)
                System.Windows.Data.BindingOperations.SetBinding(Element, UIElement.OpacityProperty, binding.ParentBindingBase);
            else Element.SetValue(UIElement.OpacityProperty, _localOpacity);
            if (!ReferenceEquals(Element.RenderTransform, _group)) return;
            Element.RenderTransform = _transform;
            Element.RenderTransformOrigin = _origin;
        }
    }
}
