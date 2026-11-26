using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PCLLF.Wpf.Motion;

namespace PCL;

/// <summary>
///     Spring-driven depth motion used across the launcher, modelled on PCL Living Fork's shell: content enters
///     from slightly below and slightly smaller on an under-damped spring and settles with a soft overshoot; it
///     leaves by shrinking a little, drifting the other way and fading. Interrupting a motion retargets from the
///     current presentation, and reduced-motion settings complete instantly.
/// </summary>
internal static class SymbioMotion
{
    /// <summary>Page and surface entrances: soft, slightly under-damped.</summary>
    public static readonly SpringParameters Depth = new(.46, .78);

    /// <summary>Dialogs, toasts and popovers: quicker, a little more bounce.</summary>
    public static readonly SpringParameters Pop = new(.34, .72);

    /// <summary>Exits: quick and fully damped so nothing lingers.</summary>
    public static readonly SpringParameters Exit = new(.18, 1);

    private static readonly ConditionalWeakTable<FrameworkElement, DepthState> States = new();

    /// <summary>Plays a depth entrance on <paramref name="element" />.</summary>
    /// <param name="rise">Start offset in DIP; positive enters from below, negative from above.</param>
    /// <param name="fromScale">Start scale, e.g. 0.97.</param>
    public static void EnterDepth(FrameworkElement element, double rise = 22, double fromScale = .97,
        SpringParameters? spring = null, Action? completed = null)
    {
        var state = States.GetValue(element, e => new DepthState(e));
        state.Run(rise, fromScale, true, spring ?? Depth, completed);
    }

    /// <summary>Plays a depth exit; the element ends transparent and the callback runs once it is gone.</summary>
    public static void ExitDepth(FrameworkElement element, double drift = -8, double toScale = .97, Action? completed = null)
    {
        var state = States.GetValue(element, e => new DepthState(e));
        state.Run(drift, toScale, false, Exit, completed);
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<string, Channel>> Channels = new();

    // A new destination supersedes the previous completion (e.g. hiding a panel which has since reopened).
    private sealed class Channel
    {
        private SpringMotion? _motion;
        private SpringParameters _parameters;
        private Action<double> _apply = null!;
        private Action? _completed;

        public void Retarget(FrameworkElement owner, Func<double> current, Action<double> apply,
            double target, SpringParameters parameters, bool immediate, Action? completed)
        {
            _completed = null;
            _apply = apply;
            if (_motion is null || !_parameters.Equals(parameters))
            {
                var velocity = _motion is { IsRunning: true } ? _motion.Velocity : 0;
                _motion?.Dispose();
                _parameters = parameters;
                _motion = new SpringMotion(owner, current(), Apply, parameters, velocity);
            }
            else if (!_motion.IsRunning && current() != _motion.Value)
            {
                // Direct manipulation and template changes can move an idle visual.
                _motion.Snap(current());
            }

            _motion.Retarget(target, immediate || ModAnimation.AniExitsAnyControl() || ModAnimation.aniSpeed is <= 0 or >= 200);
            if (_motion.IsRunning)
                _completed = completed;
            else
                completed?.Invoke();
        }

        public void Stop()
        {
            _completed = null;
            _motion?.Dispose();
        }

        private void Apply(double value)
        {
            _apply(value);
            if (_motion?.IsRunning != false) return;
            var completed = _completed;
            _completed = null;
            completed?.Invoke();
        }
    }

    public static void Spring(FrameworkElement owner, string channel, Func<double> current, Action<double> apply,
        double target, SpringParameters? spring = null, bool immediate = false, Action? completed = null)
    {
        var channels = Channels.GetOrCreateValue(owner);
        if (!channels.TryGetValue(channel, out var motion))
            channels[channel] = motion = new Channel();
        motion.Retarget(owner, current, apply, target, spring ?? Depth, immediate, completed);
    }

    public static void Stop(FrameworkElement owner, string channel)
    {
        if (Channels.TryGetValue(owner, out var channels) && channels.Remove(channel, out var motion))
            motion.Stop();
    }

    public static void Fade(FrameworkElement element, double opacity, bool immediate = false, Action? completed = null) =>
        Spring(element, "opacity", () => element.Opacity, v => element.Opacity = Math.Clamp(v, 0, 1),
            opacity, MotionTokens.Reveal, immediate, completed);

    // Keep opacity and the corresponding pose on one interruptible progress channel.
    public static void Reveal(FrameworkElement element, bool visible, Action<double> pose,
        SpringParameters spring, bool immediate = false, Action? completed = null)
    {
        Spring(element, "reveal", () => element.Opacity, v =>
        {
            element.Opacity = Math.Clamp(v, 0, 1);
            pose(v);
        }, visible ? 1 : 0, spring, immediate, completed);
    }

    public static void Progress(FrameworkElement owner, System.Windows.Controls.ColumnDefinition filled,
        System.Windows.Controls.ColumnDefinition remaining, double target, bool immediate = false)
    {
        Spring(owner, "launchProgress", () => filled.Width.Value, value =>
        {
            var fraction = Math.Clamp(value, 0, 1);
            filled.Width = new GridLength(fraction, GridUnitType.Star);
            remaining.Width = new GridLength(1 - fraction, GridUnitType.Star);
        }, Math.Clamp(target, 0, 1), PCLLF.Wpf.Motion.MotionTokens.Reveal, immediate);
    }

    public static void ScaleReveal(FrameworkElement element, bool visible, Action? completed = null)
    {
        var scale = Scale(element);
        Reveal(element, visible, v => scale.ScaleX = scale.ScaleY = .97 + .03 * v, Depth, completed: completed);
    }

    public static void SlideReveal(FrameworkElement element, bool visible, double x, double y, Action? completed = null)
    {
        var offset = Translate(element);
        Reveal(element, visible, v =>
        {
            offset.X = x * (1 - v);
            offset.Y = y * (1 - v);
        }, Depth, completed: completed);
    }

    public static void SelectionBar(FrameworkElement bar, TranslateTransform offset, bool selected, bool addedToSelection)
    {
        if (selected) bar.Visibility = Visibility.Visible;
        bar.IsHitTestVisible = selected;
        if (addedToSelection && MotionPreferences.AllowsAnimation && Math.Abs(offset.Y + 25) < .1)
            Spring(bar, "selectionY", () => offset.Y, v => offset.Y = v, -27, Pop, immediate: true);
        Spring(bar, "selectionY", () => offset.Y, v => offset.Y = v, selected ? -25 : -10, Pop);
        Fade(bar, selected ? 1 : 0, completed: selected ? null : () => bar.Visibility = Visibility.Collapsed);
    }

    public static TranslateTransform Translate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform translate && !translate.IsFrozen) return translate;
        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    public static ScaleTransform Scale(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform scale && !scale.IsFrozen) return scale;
        scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        element.RenderTransformOrigin = new Point(.5, .5);
        return scale;
    }

    public static void SpringTranslateX(FrameworkElement element, double target, SpringParameters? spring = null) =>
        Spring(element, "tx", () => Translate(element).X, v => Translate(element).X = v, target, spring);

    public static void SpringTranslateY(FrameworkElement element, double target, SpringParameters? spring = null) =>
        Spring(element, "ty", () => Translate(element).Y, v => Translate(element).Y = v, target, spring);

    public static void SpringScale(FrameworkElement element, double target, SpringParameters? spring = null, bool immediate = false) =>
        Spring(element, "scale", () => Scale(element).ScaleX, v => { var s = Scale(element); s.ScaleX = s.ScaleY = v; },
            target, spring, immediate);

    public static void SpringRotate(FrameworkElement element, double target, SpringParameters? spring = null, bool immediate = false)
    {
        RotateTransform Rotate()
        {
            if (element.RenderTransform is RotateTransform rotate && !rotate.IsFrozen) return rotate;
            rotate = new RotateTransform();
            element.RenderTransform = rotate;
            element.RenderTransformOrigin = new Point(.5, .5);
            return rotate;
        }

        Spring(element, "rotate", () => Rotate().Angle, v => Rotate().Angle = v, target, spring, immediate);
    }

    /// <summary>Springs the left margin (PCL's AaX), keeping the other margin sides.</summary>
    public static void SpringMarginLeft(FrameworkElement element, double target, SpringParameters? spring = null) =>
        Spring(element, "ml", () => element.Margin.Left,
            v => element.Margin = new Thickness(v, element.Margin.Top, element.Margin.Right, element.Margin.Bottom),
            target, spring);

    /// <summary>Stops any depth motion on <paramref name="element" /> and leaves it fully visible at rest.</summary>
    public static void Reset(FrameworkElement element)
    {
        if (States.TryGetValue(element, out var state)) state.Reset();
    }

    /// <summary>Approximate time a depth exit takes to finish.</summary>
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(190);

    /// <summary>
    ///     LF-style spring press for PCL's custom controls (LF's PressFeedback only handles ButtonBase):
    ///     <paramref name="visual" /> sinks to <paramref name="pressedScale" /> while <paramref name="input" /> is held
    ///     and springs back on release or when the pointer leaves.
    /// </summary>
    public static void AttachPress(FrameworkElement visual, UIElement input, double pressedScale = MotionTokens.PressedScale)
    {
        ScaleTransform scale;
        if (visual.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
        {
            scale = existing;
        }
        else
        {
            scale = new ScaleTransform(1, 1);
            var group = new TransformGroup();
            if (visual.RenderTransform is { } current && !current.Value.IsIdentity) group.Children.Add(current);
            group.Children.Add(scale);
            visual.RenderTransform = group;
        }
        visual.RenderTransformOrigin = new Point(.5, .5);

        var motion = new SpringMotion(visual, 1, value => scale.ScaleX = scale.ScaleY = value, MotionTokens.Press);
        var held = false;
        input.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (!input.IsEnabled) return;
            held = true;
            motion.Retarget(pressedScale);
        };
        void Release()
        {
            held = false;
            motion.Retarget(1);
        }
        input.PreviewMouseLeftButtonUp += (_, _) => Release();
        input.MouseLeave += (_, _) => Release();
        input.LostMouseCapture += (_, _) => { if (held) Release(); };
    }

    /// <summary>Cascades the items in with LF's staggered entrance (scale 96 %, rise 14 DIP, 30 ms apart).</summary>
    public static void Stagger(IEnumerable<UIElement> items) => StaggeredEntrance.Play(items);

    /// <summary>Total time a stagger of <paramref name="count" /> items needs to finish.</summary>
    public static TimeSpan StaggerDuration(int count)
    {
        if (count == 0 || !MotionPreferences.AllowsAnimation) return TimeSpan.Zero;
        var steps = Math.Min(count - 1, StaggeredEntrance.MaxSteps);
        return (StaggeredEntrance.Duration + StaggeredEntrance.Step * steps) / MotionPreferences.PlaybackRate;
    }

    /// <summary>One element's depth transform and spring, created on first use and reused afterwards.</summary>
    private sealed class DepthState
    {
        private readonly FrameworkElement _element;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _offset = new();
        private readonly TransformGroup _group = new();
        private Transform? _restingTransform;
        private Point _restingOrigin;
        private int _generation;

        public DepthState(FrameworkElement element) => _element = element;

        public void Run(double distance, double scale, bool entering, SpringParameters spring, Action? completed)
        {
            var generation = ++_generation;
            if (!ReferenceEquals(_element.RenderTransform, _group))
            {
                _restingTransform = _element.RenderTransform;
                _restingOrigin = _element.RenderTransformOrigin;
                _group.Children.Clear();
                if (_restingTransform is not null) _group.Children.Add(_restingTransform);
                _group.Children.Add(_scale);
                _group.Children.Add(_offset);
                _element.RenderTransformOrigin = new Point(.5, .5);
                _element.RenderTransform = _group;
            }

            // 起始姿态必须每次都重置：之前只在"首次挂上 TransformGroup"时设置，
            // 若上一次进场被后续切换打断、Settled 没跑完，元素就停在终态附近，
            // 下一次进场等于从终点弹到终点 —— 看起来就是「切换页面没有动效」。
            if (entering)
            {
                _offset.Y = distance;
                _scale.ScaleX = _scale.ScaleY = scale;
                _element.Opacity = 0;
            }

            var pending = 3;
            void Settled()
            {
                if (_generation != generation || --pending != 0) return;
                RestoreTransform();
                completed?.Invoke();
            }

            Spring(_element, "depthY", () => _offset.Y, v => _offset.Y = v,
                entering ? 0 : distance, spring, completed: Settled);
            Spring(_element, "depthScale", () => _scale.ScaleX, v => _scale.ScaleX = _scale.ScaleY = v,
                entering ? 1 : scale, spring, completed: Settled);
            Spring(_element, "depthOpacity", () => _element.Opacity, v => _element.Opacity = Math.Clamp(v, 0, 1),
                entering ? 1 : 0, spring, completed: Settled);
        }

        public void Reset()
        {
            ++_generation;
            if (ReferenceEquals(_element.RenderTransform, _group))
            {
                Spring(_element, "depthY", () => _offset.Y, v => _offset.Y = v, 0, immediate: true);
                Spring(_element, "depthScale", () => _scale.ScaleX, v => _scale.ScaleX = _scale.ScaleY = v, 1, immediate: true);
                Spring(_element, "depthOpacity", () => _element.Opacity, v => _element.Opacity = Math.Clamp(v, 0, 1),
                    1, immediate: true);
                RestoreTransform();
            }
            _element.Opacity = 1;
        }

        private void RestoreTransform()
        {
            if (!ReferenceEquals(_element.RenderTransform, _group)) return;
            _element.RenderTransform = _restingTransform;
            _element.RenderTransformOrigin = _restingOrigin;
        }
    }
}

/// <summary>Lets a WPF template drive its vertical offset with the same spring as the shell.</summary>
public static class SpringOffset
{
    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached(
        "Y", typeof(double), typeof(SpringOffset), new PropertyMetadata(0d, (sender, change) =>
        {
            if (sender is FrameworkElement element)
                SymbioMotion.SpringTranslateY(element, (double)change.NewValue, PCLLF.Wpf.Motion.MotionTokens.Press);
        }));

    public static double GetY(DependencyObject element) => (double)element.GetValue(YProperty);
    public static void SetY(DependencyObject element, double value) => element.SetValue(YProperty, value);
}
