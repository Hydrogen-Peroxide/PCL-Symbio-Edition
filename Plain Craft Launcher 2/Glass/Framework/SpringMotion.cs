// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCLLF.Wpf.Motion;

/// <summary>Windows accessibility settings used by every native spring and press effect.</summary>
public static class MotionPreferences {
    private static int frameRateLimit = 60;
    private static double playbackRate = 1;

    public static int FrameRateLimit {
        get => Volatile.Read(ref frameRateLimit);
        set => Volatile.Write(ref frameRateLimit, Math.Clamp(value, 1, 240));
    }

    public static double PlaybackRate {
        get => Volatile.Read(ref playbackRate);
        set => Volatile.Write(ref playbackRate, double.IsFinite(value) ? value : 1);
    }

    internal static double FrameInterval => 1d / FrameRateLimit;

    public static bool AllowsAnimation => !PCL.Core.App.Config.Preference.DisableAllMotion &&
        PlaybackRate is > 0 and < 200 &&
        SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
}

internal struct MotionFrameLimit {
    private int framesPerSecond;
    private double nextFrame;

    public bool Advance(double timestamp) {
        int limit = MotionPreferences.FrameRateLimit;
        if (limit != framesPerSecond) {
            framesPerSecond = limit;
            nextFrame = timestamp;
        }

        if (timestamp + .0005 < nextFrame) return false;
        double interval = 1d / limit;
        nextFrame += interval * Math.Max(1d, Math.Floor((timestamp - nextFrame) / interval) + 1d);
        return true;
    }
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
    private bool _finishQueued;

    public SpringMotion(FrameworkElement owner, double initialValue, Action<double> apply,
        SpringParameters? parameters = null)
        : this(owner, initialValue, apply, parameters ?? MotionTokens.Spatial, 0) { }

    internal SpringMotion(FrameworkElement owner, double initialValue, Action<double> apply,
        SpringParameters parameters, double initialVelocity)
        : this(owner, initialValue, apply, parameters, new CompositionMotionClock(), () => MotionPreferences.AllowsAnimation,
            () => owner.IsLoaded && owner.IsVisible, initialVelocity) { }

    internal SpringMotion(FrameworkElement owner, double initialValue, Action<double> apply,
        SpringParameters parameters, IMotionClock clock, Func<bool> allowsAnimation, Func<bool> canAnimateOwner, double initialVelocity = 0) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(allowsAnimation);
        ArgumentNullException.ThrowIfNull(canAnimateOwner);
        owner.Dispatcher.VerifyAccess();
        _owner = owner;
        _apply = apply;
        _spring = new SpringValue(initialValue, parameters, initialVelocity);
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
        if (MaxFrameSeconds is double cap) elapsed = Math.Min(elapsed, Math.Max(cap, MotionPreferences.FrameInterval));
        _lastTimestamp = timestamp;
        _spring.Advance(elapsed * MotionPreferences.PlaybackRate);
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

    private void OnUnloaded(object sender, RoutedEventArgs arguments) => FinishWhenHidden();

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs arguments) {
        if (!(bool)arguments.NewValue) FinishWhenHidden();
    }

    private void FinishWhenHidden() {
        if (!IsRunning || _finishQueued || _owner.Dispatcher.HasShutdownStarted) return;
        _finishQueued = true;
        // Completion can remove children. Let WPF finish walking the visibility tree first.
        _owner.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            _finishQueued = false;
            if (IsRunning && !_disposed && !_canAnimateOwner()) Snap(Target);
        }));
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
    private MotionFrameLimit _frameLimit;
    public double TimestampSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal bool IsSubscribed => _frame is not null;

    public void Start(Action<double> frame) {
        ArgumentNullException.ThrowIfNull(frame);
        if (_frame is not null) throw new InvalidOperationException("A motion clock is already active.");
        _frame = frame;
        _frameLimit = default;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop() {
        if (_frame is null) return;
        CompositionTarget.Rendering -= OnRendering;
        _frame = null;
    }

    private void OnRendering(object? sender, EventArgs arguments) {
        double now = TimestampSeconds;
        if (!MotionPreferences.AllowsAnimation || _frameLimit.Advance(now))
            _frame?.Invoke(now);
    }
}
