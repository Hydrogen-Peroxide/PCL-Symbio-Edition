using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using PCLLF.Wpf.Motion;

namespace PCL;

public sealed class ExpandableSection : Expander
{
    private static readonly SpringParameters RevealSpring = new(.4, .9);
    private ContentPresenter? presenter;
    private Path? chevron;

    public ExpandableSection()
    {
        Content = ContentPanel;
        IsExpanded = true;
        Unloaded += (_, _) => ApplyExpanded(false);
    }

    public StackPanel ContentPanel { get; } = new() { Margin = new Thickness(10, 4, 0, 0) };
    public string Title { get => Header as string ?? ""; set => SetCurrentValue(HeaderProperty, value); }
    public bool IsCollapsed { get => !IsExpanded; set => SetCurrentValue(IsExpandedProperty, !value); }

    public override void OnApplyTemplate()
    {
        if (presenter is not null) SymbioMotion.Stop(presenter, "sectionHeight");
        base.OnApplyTemplate();
        presenter = GetTemplateChild("PART_Body") as ContentPresenter;
        chevron = GetTemplateChild("PART_Chevron") as Path;
        if (GetTemplateChild("HeaderSite") is ToggleButton header) PressFeedback.SetIsEnabled(header, true);
        ApplyExpanded(false);
    }

    protected override void OnExpanded()
    {
        ApplyExpanded(IsLoaded);
        base.OnExpanded();
    }

    protected override void OnCollapsed()
    {
        ApplyExpanded(IsLoaded);
        base.OnCollapsed();
    }

    private void ApplyExpanded(bool animate)
    {
        if (chevron is not null) SymbioMotion.SpringRotate(chevron, IsExpanded ? 180 : 0, immediate: !animate);
        if (presenter is not { } body) return;

        if (!animate || ActualWidth <= 0)
        {
            SymbioMotion.Stop(body, "sectionHeight");
            body.Height = double.NaN;
            body.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var from = body.Visibility == Visibility.Visible ? body.ActualHeight : 0d;
        var target = 0d;
        body.Visibility = Visibility.Visible;
        if (IsExpanded)
        {
            body.Height = double.NaN;
            body.Measure(new Size(ActualWidth, double.PositiveInfinity));
            target = body.DesiredSize.Height;
        }

        body.Height = from;
        SymbioMotion.Spring(body, "sectionHeight", () => body.Height, height => body.Height = Math.Max(height, 0),
            target, RevealSpring, completed: () =>
            {
                body.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                body.Height = double.NaN;
            });
    }
}
