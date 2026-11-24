// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace PCLLF.Wpf.Motion;

/// <summary>Windows accessibility settings used by every native spring and press effect.</summary>
public static class MotionPreferences {
    // The launcher owns its transitions. Windows' client-area animation switch
    // should not silently turn every page and control transition into a snap.
    public static bool AllowsAnimation => !SystemParameters.HighContrast;
}

/// <summary>
/// Display-synchronised native motion owned by one visual. The setter should change transforms or opacity,
/// and may project one progress value onto several such properties. It must not change layout each frame.
/// Call on the owner's Dispatcher and dispose when the view is permanently closed.
/// </summary>
public sealed class SpringMotion : IDisposable {
    private readonly FrameworkElement _owner;
    private readonly Action<double> _apply;
    private readonly SpringValue _spring;
    private readonly IMotionClock _clock;
    private readonly Func<bool> _allowsAnimation;
    private readonly Func<bool> _canAnimateOwner;
    private double _lastTimestamp;

    /// <summary>
    /// Optional cap on the time one rendered frame may advance. Leave unset for strictly time-based motion; set it
    /// for transitions that must stay visible when the UI thread stalls (the motion pauses instead of skipping ahead).
    /// </summary>
    public double? MaxFrameSeconds { get; set; }
    private bool _disposed;

    public SpringMotion(FrameworkElement owner, double initialValue, Action<double> apply,
        SpringParameters? parameters = null)
        : this(owner, initialValue, apply, parameters ?? MotionTokens.Spatial,
            new CompositionMotionClock(), () => MotionPreferences.AllowsAnimation,
            () => owner.IsLoaded && owner.IsVisible) { }

    internal SpringMotion(FrameworkElement owner, double initialValue, Action<double> apply,
        SpringParameters parameters, IMotionClock clock, Func<bool> allowsAnimation, Func<bool> canAnimateOwner) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(allowsAnimation);
        ArgumentNullException.ThrowIfNull(canAnimateOwner);
        owner.Dispatcher.VerifyAccess();
        _owner = owner;
        _apply = apply;
        _spring = new SpringValue(initialValue, parameters);
        _clock = clock;
        _allowsAnimation = allowsAnimation;
        _canAnimateOwner = canAnimateOwner;
        _owner.Unloaded += OnUnloaded;
        _owner.IsVisibleChanged += OnVisibilityChanged;
        try { _apply(initialValue); }
        catch {
            _owner.Unloaded -= OnUnloaded;
            _owner.IsVisibleChanged -= OnVisibilityChanged;
            throw;
        }
    }

    public double Value => _spring.Value;
    public double Velocity => _spring.Velocity;
    public double Target => _spring.Target;
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Retargets from the current presentation and velocity. Input never waits for the spring to finish.
    /// Use immediate for keyboard navigation or other interactions that should complete without motion.
    /// </summary>
    public void Retarget(double target, bool immediate = false) {
        VerifyUsable();
        if (!double.IsFinite(target) || !double.IsFinite(Value - target))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (immediate || !_allowsAnimation() || !_canAnimateOwner()) { Snap(target); return; }
        if (IsRunning) AdvanceTo(_clock.TimestampSeconds);
        _spring.Retarget(target);
        if (_spring.IsAtRest) { Snap(target); return; }
        if (!IsRunning) {
            _lastTimestamp = _clock.TimestampSeconds;
            IsRunning = true;
            try { _clock.Start(AdvanceTo); }
            catch { Stop(); throw; }
        }
    }

    public void Snap(double value) {
        VerifyUsable();
        _spring.Snap(value);
        Stop();
        Apply();
    }

    public void Dispose() {
        _owner.Dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        Stop();
        _owner.Unloaded -= OnUnloaded;
        _owner.IsVisibleChanged -= OnVisibilityChanged;
    }

    private void AdvanceTo(double timestamp) {
        if (!IsRunning || _disposed) return;
        if (!_allowsAnimation() || !_canAnimateOwner()) {
            _spring.Snap(Target);
            Stop();
            Apply();
            return;
        }
        double elapsed = Math.Max(0, timestamp - _lastTimestamp);
        if (MaxFrameSeconds is double cap) elapsed = Math.Min(elapsed, cap);
        _lastTimestamp = timestamp;
        _spring.Advance(elapsed);
        if (_spring.IsAtRest) Stop();
        Apply();
    }

    private void Apply() {
        try { _apply(Value); }
        catch { Stop(); throw; }
    }

    private void Stop() {
        IsRunning = false;
        _clock.Stop();
    }

    private void OnUnloaded(object sender, RoutedEventArgs arguments) {
        if (!_disposed) Snap(Target);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs arguments) {
        if (!_disposed && !(bool)arguments.NewValue) Snap(Target);
    }

    private void VerifyUsable() {
        _owner.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal interface IMotionClock {
    double TimestampSeconds { get; }
    void Start(Action<double> frame);
    void Stop();
}

/// <summary>No permanent static subscription: an idle or unloaded visual cannot keep the render loop alive.</summary>
internal sealed class CompositionMotionClock : IMotionClock {
    private Action<double>? _frame;
    public double TimestampSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal bool IsSubscribed => _frame is not null;

    public void Start(Action<double> frame) {
        ArgumentNullException.ThrowIfNull(frame);
        if (_frame is not null) throw new InvalidOperationException("A motion clock is already active.");
        _frame = frame;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop() {
        if (_frame is null) return;
        CompositionTarget.Rendering -= OnRendering;
        _frame = null;
    }

    private void OnRendering(object? sender, EventArgs arguments) => _frame?.Invoke(TimestampSeconds);
}
