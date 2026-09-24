using System.IO;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App;

namespace PCL;

public partial class PageBedrockLeft
{
    private bool isLoad;

    public PageBedrockLeft()
    {
        InitializeComponent();
        Loaded += PageBedrockLeft_Loaded;
        Unloaded += PageBedrockLeft_Unloaded;
    }

    private void PageBedrockLeft_Loaded(object sender, RoutedEventArgs e)
    {
        if (isLoad) return;
        isLoad = true;

        // 通知右侧页面刷新
        if (ModMain.frmBedrockRight is not null)
            ModMain.frmBedrockRight.RefreshPage();
    }

    private void PageBedrockLeft_Unloaded(object sender, RoutedEventArgs e)
    {
    }

    private void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (ModMain.frmBedrockRight is not null)
            ModMain.frmBedrockRight.BtnLaunch_Click(sender, e);
    }
}
