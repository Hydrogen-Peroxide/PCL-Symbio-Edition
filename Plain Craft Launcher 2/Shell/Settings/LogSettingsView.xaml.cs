using System.Globalization;
using System.Windows;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class LogSettingsView : IRefreshable
{
    public LogSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Scroll.ScrollToHome(); Refresh(); };
    }

    public void Refresh() => Files.ItemsSource = LauncherLogFiles.List();
    public void LoadList() => Refresh();
    private void OpenDirectory(object sender, RoutedEventArgs e) => Basics.OpenPath(LauncherLogFiles.DirectoryPath);
    private void OpenFile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherLogFiles.Entry entry }) Basics.OpenPath(entry.Path);
    }

    private void Clean(object sender, RoutedEventArgs e)
    {
        if (ModMain.MyMsgBox(Lang.Text("Setup.Log.Clear.Confirm.Message"), Lang.Text("Setup.Log.Clear.Confirm.Title"),
                Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel"), isWarn: true) != 1) return;
        LauncherLogFiles.CleanHistory();
        Refresh();
        HintService.Hint(Lang.Text("Setup.Log.Clear.Success"), HintType.Success);
    }

    private void ExportCurrent(object sender, RoutedEventArgs e) => Export(false);
    private void ExportAll(object sender, RoutedEventArgs e) => Export(true);

    private void Export(bool all)
    {
        var filename = "PCL_CE_Logs_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".zip";
        var path = SystemDialogs.SelectSaveFile(Lang.Text("Setup.Log.ExportSaveTitle"), filename,
            Lang.Text("Setup.Log.ExportFilter"), Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            LauncherLogFiles.Export(path, all);
            HintService.Hint(Lang.Text("Setup.Log.ExportSuccess"), HintType.Success);
        }
        catch (Exception ex) { ModBase.Log(ex, Lang.Text("Setup.Log.ExportFailed"), ModBase.LogLevel.Hint); }
    }
}
