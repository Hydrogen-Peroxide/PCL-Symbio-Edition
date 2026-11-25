using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCL;

public class WorkspaceSidebar : Grid
{
    public static DependencyProperty AnimatedControlProperty =
        DependencyProperty.Register("AnimatedControl", typeof(FrameworkElement), typeof(WorkspaceSidebar));

    public FrameworkElement? AnimatedControl
    {
        get => (FrameworkElement?)GetValue(AnimatedControlProperty);
        set => SetValue(AnimatedControlProperty, value);
    }

    public void TriggerShowAnimation()
    {
        SymbioMotion.Reset(this); // a hide that is still running must not fade the column back out
        if (AnimatedControl is null)
        {
            // Whole column rises in on an under-damped spring (LF depth entrance).
            SymbioMotion.EnterDepth(this, 14, .97);
            return;
        }

        // Rows cascade in with LF's staggered entrance.
        Opacity = 1d;
        var items = new List<UIElement>();
        foreach (var elementRaw in GetAllAnimControls(true))
        {
            var element = DeferredView.TryInit(elementRaw);
            // 还原之前的隐藏动画可能导致的改变（#2436）
            element.Opacity = element is TextBlock ? 0.6d : 1d;
            if (element.RenderTransform is TranslateTransform)
                element.RenderTransform = null; // leftovers of old slide tweens; press scales stay attached
            if (element is SelectionRow listItem)
                listItem.isMouseOverAnimationEnabled = true;
            if (element.Visibility != Visibility.Collapsed)
                items.Add(element);
        }

        SymbioMotion.Stagger(items);
    }

    public void TriggerHideAnimation()
    {
        // The column drifts up, shrinks a little and fades on a quick spring (LF page exit).
        SymbioMotion.ExitDepth(this, -6, .98);
    }

    // 遍历获取所有需要生成动画的控件
    private List<FrameworkElement> GetAllAnimControls(bool ignoreInvisibility = false)
    {
        var allControls = new List<FrameworkElement>();
        GetAllAnimControls(AnimatedControl, ref allControls, ignoreInvisibility);
        return allControls;
    }

    private void GetAllAnimControls(FrameworkElement element, ref List<FrameworkElement> allControls,
        bool ignoreInvisibility)
    {
        if (!ignoreInvisibility && element.Visibility == Visibility.Collapsed)
            return;
        if (element is SymbioButton)
            allControls.Add(element);
        else if (element is SelectionRow or NavigationEntry)
            allControls.Add(element);
        else if (element is ContentControl)
        {
            if (((ContentControl)element).Content is FrameworkElement content)
                GetAllAnimControls(content, ref allControls, ignoreInvisibility);
        }
        else if (element is Panel)
            foreach (FrameworkElement Element2 in ((Panel)element).Children)
                GetAllAnimControls(Element2, ref allControls, ignoreInvisibility);
        else
            allControls.Add(element);
    }
}

public interface IRefreshable
{
    void Refresh();
}

internal interface IWorkspaceBackAction
{
    bool TryGoBack();
}
