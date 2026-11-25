using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace PCL;

public class DeferredView : FrameworkElement
{
    private readonly Func<FrameworkElement> createView;
    private readonly LazyLoadBehavior visible;
    private FrameworkElement? realized;

    public DeferredView(Func<FrameworkElement> createView)
    {
        this.createView = createView;
        visible = new LazyLoadBehavior { Action = () => Init() };
        Interaction.GetBehaviors(this).Add(visible);
    }

    public FrameworkElement Init()
    {
        if (realized is not null) return realized;
        var parent = Parent;
        if (parent is not null && parent is not Panel)
            throw new InvalidOperationException("A deferred view must be hosted by a panel.");

        var content = createView();
        if (parent is Panel panel)
        {
            var index = panel.Children.IndexOf(this);
            panel.Children.RemoveAt(index);
            panel.Children.Insert(index, content);
        }

        realized = content;
        Interaction.GetBehaviors(this).Remove(visible);
        return content;
    }

    public static FrameworkElement TryInit(FrameworkElement element) =>
        element is DeferredView deferred ? deferred.Init() : element;
}

public sealed class DeferredView<T>(Func<T> createView) : DeferredView(createView) where T : FrameworkElement
{
    public new T Init() => (T)base.Init();
    public static implicit operator T(DeferredView<T> view) => view.Init();
}
