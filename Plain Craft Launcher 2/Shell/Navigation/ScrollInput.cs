using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.Core.UI.Controls;
using PCLLF.Wpf.Motion;

namespace PCL;

public static class ScrollInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollInput), new PropertyMetadata(false, Attach));
    public static readonly DependencyProperty DeltaMultiplierProperty = DependencyProperty.RegisterAttached(
        "DeltaMultiplier", typeof(double), typeof(ScrollInput), new PropertyMetadata(1d));

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static double GetDeltaMultiplier(DependencyObject target) => (double)target.GetValue(DeltaMultiplierProperty);
    public static void SetDeltaMultiplier(DependencyObject target, double value) => target.SetValue(DeltaMultiplierProperty, value);

    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollViewer scroll) return;
        if ((bool)e.NewValue)
        {
            scroll.PreviewMouseWheel += Wheel;
            scroll.ScrollChanged += Changed;
            scroll.IsVisibleChanged += Visible;
            scroll.PreviewGotKeyboardFocus += FocusChanged;
        }
        else
        {
            scroll.PreviewMouseWheel -= Wheel;
            scroll.ScrollChanged -= Changed;
            scroll.IsVisibleChanged -= Visible;
            scroll.PreviewGotKeyboardFocus -= FocusChanged;
            SmoothScroll.Stop(scroll);
        }
    }

    private static void Wheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        if (e.Handled || e.Delta == 0 || scroll.ScrollableHeight <= 0) return;
        if (scroll.Content is FrameworkElement { TemplatedParent: null } &&
            e.Source is ComboBox { IsDropDownOpen: true } or TextBox { AcceptsReturn: true } or ComboBoxItem or CheckBox) return;
        e.Handled = true;
        SmoothScroll.ScrollBy(scroll, -e.Delta * GetDeltaMultiplier(scroll));
        Tooltip.Dismiss();
    }

    private static void Changed(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0) ModMain.frmMain?.BtnExtraBack?.ShowRefresh();
    }
    private static void Visible(object sender, DependencyPropertyChangedEventArgs e) => ModMain.frmMain?.BtnExtraBack?.ShowRefresh();
    private static void FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is SymbioSlider) e.Handled = true;
    }
}
