// SPDX-License-Identifier: MPL-2.0
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCLLF.Wpf.Motion;

/// <summary>
/// Native toggle/checkbox feedback. Templates may supply ThumbScale, TrackActive, CheckCoreScale, CheckMark, or ThumbTransform.
/// The original ToggleButton still owns checking, keyboard behavior, focus and UI Automation.
/// </summary>
public static class ToggleFeedback {
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ToggleFeedback), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty TravelProperty = DependencyProperty.RegisterAttached(
        "Travel", typeof(double), typeof(ToggleFeedback), new PropertyMetadata(14d, OnTravelChanged),
        value => value is double distance && double.IsFinite(distance) && distance >= 0 && distance <= 1024);
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached(
        "ReduceMotion", typeof(bool), typeof(ToggleFeedback), new PropertyMetadata(false, OnReduceMotionChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(ToggleFeedback));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static double GetTravel(DependencyObject element) => (double)element.GetValue(TravelProperty);
    public static void SetTravel(DependencyObject element, double value) => element.SetValue(TravelProperty, value);
    public static bool GetReduceMotion(DependencyObject element) => (bool)element.GetValue(ReduceMotionProperty);
    public static void SetReduceMotion(DependencyObject element, bool value) => element.SetValue(ReduceMotionProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs arguments) {
        if (element is not ToggleButton toggle) return;
        if (toggle.GetValue(StateProperty) is State old) old.Dispose();
        toggle.ClearValue(StateProperty);
        if ((bool)arguments.NewValue) toggle.SetValue(StateProperty, new State(toggle));
    }
    private static void OnTravelChanged(DependencyObject element, DependencyPropertyChangedEventArgs arguments) =>
        (element.GetValue(StateProperty) as State)?.Apply();
    private static void OnReduceMotionChanged(DependencyObject element, DependencyPropertyChangedEventArgs arguments) {
        if ((bool)arguments.NewValue && element.GetValue(StateProperty) is State state) state.Finish();
    }

    private sealed class State : IDisposable {
        private readonly ToggleButton _owner;
        private readonly SpringMotion _position;
        private readonly SpringMotion _press;
        private System.Windows.Controls.ControlTemplate? _resolvedTemplate;
        private TranslateTransform? _thumb;
        private ScaleTransform? _scale;
        private FrameworkElement? _active;
        private ScaleTransform? _coreScale;
        private Shape? _checkMark;
        private double _progress, _pressed;
        private bool _keyboard, _pointerDown;

        internal State(ToggleButton owner) {
            _owner = owner;
            _progress = owner.IsChecked == true ? 1 : 0;
            _position = new SpringMotion(owner, _progress, value => { _progress = value; Apply(); }, MotionTokens.Toggle);
            _press = new SpringMotion(owner, 0, value => { _pressed = value; Apply(); }, MotionTokens.Press);
            owner.Checked += OnChecked;
            owner.Unchecked += OnChecked;
            owner.Indeterminate += OnChecked;
            owner.Loaded += OnLoaded;
            owner.Unloaded += OnUnloaded;
            owner.PreviewKeyDown += OnKeyDown;
            owner.PreviewMouseLeftButtonDown += OnMouseDown;
            owner.PreviewMouseLeftButtonUp += OnMouseUp;
            owner.LostMouseCapture += OnLostCapture;
            owner.MouseLeave += OnMouseLeave;
            owner.MouseEnter += OnMouseEnter;
            owner.IsEnabledChanged += OnControlEnabledChanged;
        }

        internal void Apply() {
            ResolveParts();
            double p = Math.Clamp(_progress, 0, 1);
            if (_thumb is not null) _thumb.X = GetTravel(_owner) * _progress;
            if (_scale is not null) {
                double deflection = (1 - MotionTokens.PressedScale) * _pressed;
                _scale.ScaleX = 1 - deflection;
                _scale.ScaleY = 1 - deflection;
            }
            if (_active is not null) _active.Opacity = p;
            if (_coreScale is not null) {
                double core = 0.52 + 0.48 * p;
                _coreScale.ScaleX = core;
                _coreScale.ScaleY = core;
            }
            if (_checkMark is not null) {
                _checkMark.Opacity = Math.Clamp(p * 2.5, 0, 1);
                _checkMark.StrokeDashOffset = 7.6 * (1 - p);
            }
        }
        internal void Finish() { _position.Snap(_owner.IsChecked == true ? 1 : 0); _press.Snap(0); }
        private bool ResolveParts() {
            var template = _owner.Template;
            if (template is null) { _resolvedTemplate = null; return false; }
            if (ReferenceEquals(template, _resolvedTemplate) && (_scale is not null || _thumb is not null || _active is not null))
                return false;
            var thumb = template.FindName("ThumbTransform", _owner) as TranslateTransform;
            var scale = template.FindName("ThumbScale", _owner) as ScaleTransform;
            var active = template.FindName("TrackActive", _owner) as FrameworkElement;
            var coreScale = template.FindName("CheckCoreScale", _owner) as ScaleTransform;
            var checkMark = template.FindName("CheckMark", _owner) as Shape;
            bool changed = !ReferenceEquals(thumb, _thumb) || !ReferenceEquals(scale, _scale) ||
                !ReferenceEquals(active, _active) || !ReferenceEquals(coreScale, _coreScale) || !ReferenceEquals(checkMark, _checkMark);
            _thumb = thumb; _scale = scale; _active = active; _coreScale = coreScale; _checkMark = checkMark;
            if (thumb is not null || scale is not null || active is not null)
                _resolvedTemplate = template;
            return changed;
        }
        private void OnLoaded(object sender, RoutedEventArgs arguments) { _resolvedTemplate = null; _owner.ApplyTemplate(); _position.Snap(_owner.IsChecked == true ? 1 : 0); Apply(); }
        private void OnUnloaded(object sender, RoutedEventArgs arguments) { _pointerDown = false; _press.Snap(0); }
        private void OnChecked(object sender, RoutedEventArgs arguments) {
            if (!ReferenceEquals(arguments.OriginalSource, _owner)) return;
            _position.Retarget(_owner.IsChecked == true ? 1 : 0, _keyboard || GetReduceMotion(_owner));
            _keyboard = false;
        }
        private void OnKeyDown(object sender, KeyEventArgs arguments) { if (arguments.Key is Key.Space or Key.Enter) _keyboard = true; }
        private void OnMouseDown(object sender, MouseButtonEventArgs arguments) {
            _keyboard = false;
            if (!_owner.IsEnabled) return;
            _pointerDown = true;
            _press.Retarget(1, GetReduceMotion(_owner));
        }
        private void OnMouseUp(object sender, MouseButtonEventArgs arguments) => Release();
        private void OnLostCapture(object sender, MouseEventArgs arguments) => Release();
        private void OnMouseLeave(object sender, MouseEventArgs arguments) => _press.Retarget(0);
        private void OnMouseEnter(object sender, MouseEventArgs arguments) {
            if (_pointerDown && _owner.IsMouseCaptured && Mouse.LeftButton == MouseButtonState.Pressed) _press.Retarget(1);
        }
        private void OnControlEnabledChanged(object sender, DependencyPropertyChangedEventArgs arguments) { if (!(bool)arguments.NewValue) Release(); }
        private void Release() { _pointerDown = false; _press.Retarget(0, GetReduceMotion(_owner)); }

        public void Dispose() {
            _press.Snap(0);
            _position.Dispose(); _press.Dispose();
            _owner.Checked -= OnChecked; _owner.Unchecked -= OnChecked; _owner.Indeterminate -= OnChecked;
            _owner.Loaded -= OnLoaded; _owner.Unloaded -= OnUnloaded;
            _owner.PreviewKeyDown -= OnKeyDown;
            _owner.PreviewMouseLeftButtonDown -= OnMouseDown; _owner.PreviewMouseLeftButtonUp -= OnMouseUp;
            _owner.LostMouseCapture -= OnLostCapture; _owner.MouseLeave -= OnMouseLeave; _owner.MouseEnter -= OnMouseEnter;
            _owner.IsEnabledChanged -= OnControlEnabledChanged;
        }
    }
}
