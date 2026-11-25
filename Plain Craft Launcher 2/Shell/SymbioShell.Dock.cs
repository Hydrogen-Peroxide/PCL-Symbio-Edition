using System.Windows;
using System.Windows.Media;

namespace PCL;

/// <summary>
///     Dock placement. Inside: the dock floats over the bottom of the main panel. Detached: like PCL LF's floating
///     rail, the dock becomes its own frosted island below the panel with desktop showing through the gap. The shell
///     still spans the whole window (so the dock keeps sampling the wallpaper for its frost); the host window cuts
///     the visible shape out of it with <see cref="BuildWindowShape" />.
/// </summary>
public partial class SymbioShell
{
    /// <summary>Transparent gap between the main panel and the detached dock.</summary>
    public const double DetachedDockGap = 10;

    private const double InsideDockMargin = 14;
    private const double InsideSheetBottom = 80;
    private const double InsideHeroBottom = 92;
    private const double PanelRadius = 8;

    /// <summary>Raised when the window shape must be recomputed (placement or dock size changed).</summary>
    public event EventHandler? WindowShapeChanged;

    /// <summary>Dock height. Before the first layout pass <see cref="FrameworkElement.ActualHeight" /> is 0.</summary>
    private double DockHeight => double.IsFinite(DockSurface.Height) ? DockSurface.Height : DockSurface.ActualHeight;

    /// <summary>
    ///     Bottom band the detached dock takes away from the main panel (dock + gap), measured from the window's bottom
    ///     edge. 0 while the dock sits inside the panel, so callers can offset floating overlays by it unconditionally.
    /// </summary>
    internal double DetachedDockBand => DockDetached ? DockHeight + DetachedDockGap : 0;

    public bool DockDetached
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            ApplyDockPlacement();
        }
    }

    private void InitializeDockPlacement()
    {
        DockSurface.SizeChanged += (_, _) => WindowShapeChanged?.Invoke(this, EventArgs.Empty);
        ApplyDockPlacement();
    }

    private void ApplyDockPlacement()
    {
        var dockBand = DockHeight + DetachedDockGap; // dock height + gap below the panel
        DockLayer.Margin = new Thickness(0, 0, 0, DockDetached ? 0 : InsideDockMargin);
        SheetLayer.Margin = SheetLayer.Margin with { Bottom = DockDetached ? dockBand + 12 : InsideSheetBottom };
        HeroHost.Margin = HeroHost.Margin with { Bottom = DockDetached ? dockBand + 24 : InsideHeroBottom };
        // The dock's drop shadow would be cut off by the window shape; a detached dock relies on its stroke.
        DockShadow.Visibility = DockDetached ? Visibility.Collapsed : Visibility.Visible;
        WindowShapeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Visible window shape in <paramref name="host" /> coordinates, or null for the plain rounded rectangle.
    ///     Detached: the main panel (top edge to dock top minus the gap) united with the dock pill.
    /// </summary>
    public Geometry? BuildWindowShape(Visual host, Size hostSize)
    {
        if (!DockDetached || !DockSurface.IsVisible || DockSurface.ActualWidth <= 0) return null;
        Rect dock;
        try { dock = DockSurface.TransformToAncestor(host).TransformBounds(new Rect(DockSurface.RenderSize)); }
        catch (InvalidOperationException) { return null; }

        // Ignore render transforms of an in-flight dock entrance: the shape follows the dock's layout slot.
        var panelBottom = Math.Max(0, hostSize.Height - DockSurface.ActualHeight - DetachedDockGap);
        var dockTop = hostSize.Height - DockSurface.ActualHeight;
        var dockRect = new Rect(dock.Left, dockTop, dock.Width, DockSurface.ActualHeight);
        var dockRadius = DockSurface.CornerRadius.TopLeft;

        var shape = new GeometryGroup { FillRule = FillRule.Nonzero };
        shape.Children.Add(new RectangleGeometry(new Rect(0, 0, hostSize.Width, panelBottom), PanelRadius, PanelRadius));
        shape.Children.Add(new RectangleGeometry(dockRect, dockRadius, dockRadius));
        shape.Freeze();
        return shape;
    }
}
