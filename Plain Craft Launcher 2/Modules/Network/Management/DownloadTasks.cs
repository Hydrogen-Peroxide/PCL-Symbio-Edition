namespace PCL;

internal readonly record struct DownloadProgress(double Value, bool HasTasks, long Registration);
internal sealed record DownloadFailure(int Id, string Name, string Details);

internal static class DownloadTasks
{
    private static readonly object Gate = new();
    private static readonly List<ModLoader.LoaderCombo> Active = [];
    private static readonly Dictionary<string, DownloadFailure> Failures = new(StringComparer.Ordinal);
    private static double progress = 1d;
    private static long registration;

    public static void Add(ModLoader.LoaderCombo loader)
    {
        lock (Gate)
        {
            Failures.Remove(loader.name);
            if (Active.Contains(loader)) return;
            Active.Add(loader);
            if (loader.show) registration++;
        }

        ModBase.Log($"[Taskbar] {loader.name} 已加入任务列表");
    }

    public static ModLoader.LoaderCombo[] GetActive()
    {
        lock (Gate) return Active.ToArray();
    }

    public static (ModLoader.LoaderCombo[] Active, DownloadFailure[] Failures) Snapshot()
    {
        lock (Gate) return (Active.ToArray(), Failures.Values.ToArray());
    }

    public static bool HasVisibleTasks()
    {
        lock (Gate)
            return Failures.Count != 0 || Active.Any(task => task.show &&
                task.State is ModBase.LoadState.Loading or ModBase.LoadState.Failed);
    }

    public static DownloadProgress Update()
    {
        List<string>? removed = null;
        DownloadProgress result;
        lock (Gate)
        {
            if (Active.Count == 0)
            {
                progress = 1d;
                return new DownloadProgress(progress, false, registration);
            }

            var rawProgress = Math.Clamp(Active.Average(task => task.Progress), 0d, 1d);
            progress = rawProgress is <= 0d or >= 1d || progress > rawProgress
                ? rawProgress : progress * 0.9d + rawProgress * 0.1d;

            var idle = Active.All(task => task.State != ModBase.LoadState.Loading);
            Active.RemoveAll(task =>
            {
                var state = task.State;
                if (state == ModBase.LoadState.Loading ||
                    (!idle && state is not (ModBase.LoadState.Waiting or ModBase.LoadState.Aborted))) return false;

                if (task.show && state == ModBase.LoadState.Failed)
                    Failures[task.name] = new DownloadFailure(task.Uuid, task.name, task.Error?.ToString() ?? "");
                (removed ??= []).Add(task.name);
                return true;
            });
            result = new DownloadProgress(progress, Active.Count != 0, registration);
        }

        if (removed is not null)
            foreach (var name in removed)
                ModBase.Log($"[Taskbar] {name} 已移出任务列表");
        return result;
    }

    public static void Dismiss(int taskId)
    {
        ModLoader.LoaderCombo? active;
        lock (Gate)
        {
            active = Active.Find(task => task.Uuid == taskId);
            if (active is not null)
            {
                Active.Remove(active);
            }
            else
            {
                var failure = Failures.Values.FirstOrDefault(task => task.Id == taskId);
                if (failure is not null) Failures.Remove(failure.Name);
            }
        }

        if (active?.State is ModBase.LoadState.Loading or ModBase.LoadState.Waiting)
            ModBase.RunInThread(active.Abort);
    }
}
