using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.UI;

namespace PCL;

public partial class GameLogsView
{
    private readonly Dictionary<Color, SolidColorBrush> brushes = new();
    private LiveGameLogs.Session? displayed;
    private long last;
    private bool updating;
    private bool exportingStack;
    private int pending;

    public GameLogsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LiveGameLogs.SessionsChanged += QueueRefresh;
            LiveGameLogs.Updated += OnUpdated;
            RefreshSessions();
        };
        Unloaded += (_, _) =>
        {
            LiveGameLogs.SessionsChanged -= QueueRefresh;
            LiveGameLogs.Updated -= OnUpdated;
        };
    }

    private void OnUpdated(LiveGameLogs.Session session)
    {
        if (ReferenceEquals(displayed, session)) QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref pending, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref pending, 0);
            if (IsLoaded) RefreshSessions();
        });
    }

    private void RefreshSessions()
    {
        updating = true;
        try
        {
            var available = LiveGameLogs.Sessions;
            var selected = LiveGameLogs.Selected;
            if (Sessions.ItemsSource is not LiveGameLogs.Session[] previous || !previous.SequenceEqual(available))
                Sessions.ItemsSource = available;
            Sessions.SelectedItem = selected;
            RetainSlider.Value = Config.System.MaxGameLog;
            if (!ReferenceEquals(displayed, selected))
            {
                displayed = selected;
                last = 0;
                LogText.Document.Blocks.Clear();
            }
            var hasLog = selected is not null;
            Empty.Visibility = hasLog ? Visibility.Collapsed : Visibility.Visible;
            Viewer.Visibility = Tools.Visibility = Footer.Visibility = hasLog ? Visibility.Visible : Visibility.Collapsed;
            RemoveSessionButton.IsEnabled = hasLog;
            if (selected is null) return;
            Render(selected);
        }
        finally { updating = false; }
    }

    private void Render(LiveGameLogs.Session selected)
    {
        var (first, next) = selected.Read(last);
        var blocks = LogText.Document.Blocks;
        while (blocks.FirstBlock?.Tag is long sequence && sequence < first)
            blocks.Remove(blocks.FirstBlock);
        foreach (var line in next)
        {
            if (!brushes.TryGetValue(line.Color, out var brush))
            {
                brush = new SolidColorBrush(line.Color);
                brush.Freeze();
                brushes[line.Color] = brush;
            }
            blocks.Add(new Paragraph(new Run(line.Text)) { Foreground = brush, Margin = new Thickness(0), Tag = line.Sequence });
            last = line.Sequence;
        }
        if (next.Length > 0 && AutoScroll.IsChecked == true) LogText.ScrollToEnd();
        var watcher = selected.Watcher;
        var alive = !watcher.gameProcess.HasExited;
        KillButton.IsEnabled = alive;
        DumpButton.IsEnabled = alive && !exportingStack && !string.IsNullOrWhiteSpace(watcher.jStackPath);
        Counts.Text = string.Join(" · ",
            $"{watcher.countDebug} {Lang.Text("LogPage.Level.Debug")}",
            $"{watcher.countInfo} {Lang.Text("LogPage.Level.Info")}",
            $"{watcher.countWarn} {Lang.Text("LogPage.Level.Warn")}",
            $"{watcher.countError} {Lang.Text("LogPage.Level.Error")}",
            $"{watcher.countFatal} {Lang.Text("LogPage.Level.Fatal")}");
    }

    private void SessionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && Sessions.SelectedItem is LiveGameLogs.Session selected) LiveGameLogs.Select(selected);
    }

    private void RetentionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || !IsLoaded) return;
        Config.System.MaxGameLog = (int)e.NewValue;
        if (displayed is { } selected) Render(selected);
    }

    private void RemoveSession(object sender, RoutedEventArgs e)
    {
        if (displayed is { } session) LiveGameLogs.Remove(session);
    }

    private void ClearLog(object sender, RoutedEventArgs e) => displayed?.Clear();

    private void KillGame(object sender, RoutedEventArgs e)
    {
        if (displayed?.Watcher is not { } watcher || watcher.State > ModWatcher.Watcher.MinecraftState.Running) return;
        watcher.Kill();
        HintService.Hint(Lang.Text("LogPage.Action.GameClosed", watcher.version.Name), HintType.Success);
    }

    private async void ExportLog(object sender, RoutedEventArgs e)
    {
        if (displayed is not { } session) return;
        var save = SystemDialogs.SelectSaveFile(Lang.Text("LogPage.Export.SelectLocation"),
            Lang.Text("LogPage.Export.GameLog.FileName", session.Watcher.version.Name), Lang.Text("LogPage.Export.GameLog.Filter"));
        if (string.IsNullOrWhiteSpace(save)) return;
        try
        {
            await File.WriteAllLinesAsync(save, session.ReadFullLog());
            HintService.Hint(Lang.Text("LogPage.Export.Success"), HintType.Success);
            ModBase.OpenExplorer(save);
        }
        catch (Exception ex) { ModBase.Log(ex, "导出游戏日志失败", ModBase.LogLevel.Hint); }
    }

    private async void ExportStack(object sender, RoutedEventArgs e)
    {
        if (displayed?.Watcher is not { } watcher || exportingStack) return;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss", CultureInfo.InvariantCulture);
        var save = SystemDialogs.SelectSaveFile(Lang.Text("LogPage.Export.SelectLocation"),
            Lang.Text("LogPage.ExportStack.FileName", stamp), Lang.Text("LogPage.ExportStack.Filter"));
        if (string.IsNullOrWhiteSpace(save)) return;
        exportingStack = true;
        DumpButton.IsEnabled = false;
        HintService.Hint(Lang.Text("LogPage.ExportStack.Progress"));
        try
        {
            var dump = await Task.Run(() => watcher.ExportStackDump(save));
            await File.WriteAllLinesAsync(save, dump);
            HintService.Hint(Lang.Text("LogPage.ExportStack.Success"), HintType.Success);
            ModBase.OpenExplorer(save);
        }
        catch (Exception ex) { ModBase.Log(ex, "导出线程堆栈失败", ModBase.LogLevel.Hint); }
        finally
        {
            exportingStack = false;
            QueueRefresh();
        }
    }
}
