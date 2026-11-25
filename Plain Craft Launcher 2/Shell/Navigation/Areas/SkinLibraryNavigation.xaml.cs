using System.Windows;

namespace PCL;

public partial class SkinLibraryNavigation
{
    internal FormMain.PageSubType SelectedPage => ShellNavigation.Selected(FormMain.PageType.SkinLibrary);

    public SkinLibraryNavigation()
    {
        InitializeComponent();
    }

    internal void SelectPage(FormMain.PageSubType page)
    {
        ShellNavigation.Select(FormMain.PageType.SkinLibrary, page);
        var item = page == FormMain.PageSubType.SkinLibraryCapes ? ItemCapes : ItemSkins;
        item.IsChecked = true;
    }

    private void TabChecked(object sender, RoutedEventArgs args)
    {
        var page = ReferenceEquals(sender, ItemCapes)
            ? FormMain.PageSubType.SkinLibraryCapes
            : FormMain.PageSubType.SkinLibrarySkins;
        if (page != SelectedPage)
            ModMain.frmMain.PageChange(FormMain.PageType.SkinLibrary, page);
    }
}
