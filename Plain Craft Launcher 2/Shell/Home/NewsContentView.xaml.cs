using PCL.Core.ViewModel.Homepage;

namespace PCL;

public partial class NewsContentView : WorkspacePage
{
    public NewsContentView()
    {
        InitializeComponent();
        DataContext = new NewsViewModel();
    }
}
