// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PCLLF.Wpf.Motion;

/// <summary>Subtle pointer-down feedback for native buttons. It does not capture input or change click/focus behavior.</summary>
public static class PressFeedback {
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnIsEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(PressFeedback));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs arguments) {
        if (element is not ButtonBase button) return;
        if (button.GetValue(StateProperty) is State previous) previous.Dispose();
        button.ClearValue(StateProperty);
        if ((bool)arguments.NewValue) button.SetValue(StateProperty, new State(button));
    }

    private sealed class State : IDisposable {
        private readonly ButtonBase _button;
        private readonly Transform _previousTransform;
        private readonly TransformGroup _group;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly SpringMotion _motion;
        private bool _pointerDown;

        internal State(ButtonBase button) {
            _button = button;
            _previousTransform = button.RenderTransform;
            _group = new TransformGroup();
            _group.Children.Add(_previousTransform);
            _group.Children.Add(_scale);
            button.SetCurrentValue(UIElement.RenderTransformProperty, _group);
            UpdateScaleOrigin();
            _motion = new SpringMotion(button, 1, value => _scale.ScaleX = _scale.ScaleY = value, MotionTokens.Press);
            button.PreviewMouseLeftButtonDown += OnMouseDown;
            button.PreviewMouseLeftButtonUp += OnMouseUp;
            button.LostMouseCapture += OnLostCapture;
            button.MouseLeave += OnMouseLeave;
            button.MouseEnter += OnMouseEnter;
            button.IsEnabledChanged += OnEnabledChanged;
            button.SizeChanged += OnSizeChanged;
            button.Unloaded += OnUnloaded;
        }

        public void Dispose() {
            _motion.Dispose();
            _button.PreviewMouseLeftButtonDown -= OnMouseDown;
            _button.PreviewMouseLeftButtonUp -= OnMouseUp;
            _button.LostMouseCapture -= OnLostCapture;
            _button.MouseLeave -= OnMouseLeave;
            _button.MouseEnter -= OnMouseEnter;
            _button.IsEnabledChanged -= OnEnabledChanged;
            _button.SizeChanged -= OnSizeChanged;
            _button.Unloaded -= OnUnloaded;
            if (ReferenceEquals(_button.RenderTransform, _group))
                _button.SetCurrentValue(UIElement.RenderTransformProperty, _previousTransform);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs arguments) {
            if (!_button.IsEnabled || arguments.ChangedButton != MouseButton.Left) return;
            _pointerDown = true;
            _motion.Retarget(MotionTokens.PressedScale);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs arguments) {
            if (arguments.ChangedButton == MouseButton.Left) Release();
        }

        private void OnLostCapture(object sender, MouseEventArgs arguments) => Release();
        private void OnMouseLeave(object sender, MouseEventArgs arguments) => _motion.Retarget(1);
        private void OnMouseEnter(object sender, MouseEventArgs arguments) {
            if (_pointerDown && _button.IsMouseCaptured && Mouse.LeftButton == MouseButtonState.Pressed)
                _motion.Retarget(MotionTokens.PressedScale);
        }
        private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs arguments) {
            if (!(bool)arguments.NewValue) Release();
        }
        private void OnSizeChanged(object sender, SizeChangedEventArgs arguments) => UpdateScaleOrigin();
        private void UpdateScaleOrigin() {
            // Keep the existing transform's origin intact. Only the appended feedback scale is centred.
            _scale.CenterX = _button.ActualWidth * (.5 - _button.RenderTransformOrigin.X);
            _scale.CenterY = _button.ActualHeight * (.5 - _button.RenderTransformOrigin.Y);
        }
        private void OnUnloaded(object sender, RoutedEventArgs arguments) { _pointerDown = false; _motion.Snap(1); }
        private void Release() { _pointerDown = false; _motion.Retarget(1); }
    }
}
