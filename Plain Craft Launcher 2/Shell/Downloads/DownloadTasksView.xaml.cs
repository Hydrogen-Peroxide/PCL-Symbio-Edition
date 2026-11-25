using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PCL.Core.App.Localization;
using PCL.Network;

namespace PCL;

public partial class DownloadTasksView
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<int, TaskCard> cards = [];

    public DownloadTasksView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ThreadStats.Visibility = ModBase.modeDebug ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
            timer.Start();
        };
        Unloaded += (_, _) => timer.Stop();
        timer.Tick += (_, _) => Refresh();
    }

    private void Refresh()
    {
        try
        {
            var (active, failures) = DownloadTasks.Snapshot();
            var visible = active.Where(task => task.show &&
                task.State is not (ModBase.LoadState.Finished or ModBase.LoadState.Aborted)).ToArray();
            var currentIds = visible.Select(task => task.Uuid).Concat(failures.Select(task => task.Id)).ToHashSet();
            foreach (var id in cards.Keys.Where(id => !currentIds.Contains(id)).ToArray())
            {
                var card = cards[id];
                cards.Remove(id);
                card.IsHitTestVisible = false;
                SymbioMotion.ExitDepth(card, -6, .97, () =>
                {
                    Tasks.Children.Remove(card);
                    ReturnWhenEmpty();
                });
            }

            foreach (var task in visible)
            {
                var card = GetCard(task.Uuid, task.name);
                if (task.State == ModBase.LoadState.Failed)
                    card.ShowFailure(task.Error?.ToString() ?? "");
                else
                    card.ShowProgress(task);
            }

            foreach (var failure in failures)
                GetCard(failure.Id, failure.Name).ShowFailure(failure.Details);

            var percent = visible.Length == 0 ? 1d : Math.Clamp(visible.Average(task => task.Progress), 0d, 1d);
            TotalProgress.Text = Lang.Number(percent, percent > 0.999999d ? "P0" : "P2");
            TransferRate.Text = ModBase.GetString(active.Length == 0 ? 0 : ModNet.NetManager.Speed) + "/s";
            var remaining = active.Length == 0 ? 0 : ModNet.NetManager.FileRemain;
            PendingFiles.Text = remaining < 0 ? "0*" : Lang.Number(remaining, "N0");
            TransferThreads.Text = Lang.Number(active.Length == 0 ? 0 : ModNet.NetManager.ThreadCount, "N0") + " / " +
                                   Lang.Number(ModNet.NetTaskThreadLimit, "N0");

            ReturnWhenEmpty();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "刷新下载任务管理视图失败", ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Speed.Error.OperationFailed"));
        }
    }

    private TaskCard GetCard(int id, string title)
    {
        if (cards.TryGetValue(id, out var card)) return card;
        card = new TaskCard(title, () =>
        {
            DownloadTasks.Dismiss(id);
            Refresh();
        });
        cards.Add(id, card);
        Tasks.Children.Insert(0, card);
        SymbioMotion.EnterDepth(card, rise: 8, fromScale: .99);
        return card;
    }

    private void ReturnWhenEmpty()
    {
        if (IsLoaded && Tasks.Children.Count == 0 &&
            Window.GetWindow(this) is FormMain shell && shell.pageCurrent == FormMain.PageType.TaskManager)
            shell.PageBack();
    }

    private sealed class TaskCard : WorkspaceCard
    {
        private readonly StackPanel body = new() { Margin = new Thickness(15, 40, 15, 12) };
        private readonly SymbioIconButton dismiss;
        private List<ModLoader.LoaderBase>? steps;
        private readonly List<TextBlock> statuses = [];
        private bool showingFailure;

        public TaskCard(string title, Action onDismiss)
        {
            Title = title;
            Margin = new Thickness(0, 0, 0, 12);
            Children.Add(body);
            dismiss = new SymbioIconButton
            {
                Name = "DismissTask",
                SvgIcon = "lucide/x",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Width = 29,
                Height = 28,
                Margin = new Thickness(0, 6, 6, 0)
            };
            dismiss.SetResourceReference(ToolTipProperty, "Common.Action.Cancel");
            dismiss.SetResourceReference(AutomationProperties.NameProperty, "Common.Action.Cancel");
            dismiss.Click += (_, e) =>
            {
                e.Handled = true;
                onDismiss();
            };
            Children.Add(dismiss);
        }

        public void ShowProgress(ModLoader.LoaderCombo task)
        {
            if (steps is null || showingFailure)
            {
                showingFailure = false;
                steps = task.GetLoaderList();
                body.Children.Clear();
                statuses.Clear();
                dismiss.SetResourceReference(ToolTipProperty, "Common.Action.Cancel");
                dismiss.SetResourceReference(AutomationProperties.NameProperty, "Common.Action.Cancel");
                foreach (var step in steps)
                {
                    var status = new TextBlock { Width = 55, TextAlignment = TextAlignment.Center };
                    status.SetResourceReference(TextBlock.ForegroundProperty, "SyAccentText");
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    row.Children.Add(status);
                    row.Children.Add(new TextBlock { Text = step.name, TextWrapping = TextWrapping.Wrap });
                    body.Children.Add(row);
                    statuses.Add(status);
                }
            }

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                statuses[i].Text = step.State switch
                {
                    ModBase.LoadState.Waiting => "…",
                    ModBase.LoadState.Loading => Lang.Number(step.Progress, "P0"),
                    ModBase.LoadState.Finished => "✓",
                    _ => "×"
                };
            }
        }

        public void ShowFailure(string error)
        {
            if (showingFailure) return;
            showingFailure = true;
            body.Children.Clear();
            dismiss.SetResourceReference(ToolTipProperty, "Common.Action.Close");
            dismiss.SetResourceReference(AutomationProperties.NameProperty, "Common.Action.Close");
            var details = new TextBlock
            {
                Text = string.IsNullOrEmpty(error) ? Lang.Text("Speed.Error.OperationFailed") : error,
                TextWrapping = TextWrapping.Wrap
            };
            details.SetResourceReference(ToolTipProperty, "Speed.Error.ClickToCopy");
            var copy = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            copy.SetResourceReference(StyleProperty, "SyButton");
            copy.SetResourceReference(ContentControl.ContentProperty, "Common.Action.Copy");
            void Copy()
            {
                ModBase.ClipboardSet(details.Text, false);
                HintService.Hint(Lang.Text("Speed.Error.Copied"), HintType.Success);
            }
            details.MouseLeftButtonDown += (_, _) => Copy();
            copy.Click += (_, _) => Copy();
            body.Children.Add(details);
            body.Children.Add(copy);
        }
    }
}
