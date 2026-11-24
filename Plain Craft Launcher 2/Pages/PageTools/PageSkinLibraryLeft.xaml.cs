using System.Windows;

namespace PCL;

public partial class PageSkinLibraryLeft
{
    internal FormMain.PageSubType SelectedPage { get; private set; } = FormMain.PageSubType.SkinLibrarySkins;

    public PageSkinLibraryLeft()
    {
        InitializeComponent();
    }

    internal void SelectPage(FormMain.PageSubType page)
    {
        SelectedPage = page;
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
