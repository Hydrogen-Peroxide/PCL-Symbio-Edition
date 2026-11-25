using System.Windows.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL;

internal static class LiveGameLogs
{
    private static readonly object gate = new();
    private static readonly List<Session> sessions = [];
    private static Session? selected;
    internal static event Action? SessionsChanged;
    internal static event Action<Session>? Updated;

    internal static Session[] Sessions
    {
        get { lock (gate) return sessions.ToArray(); }
    }

    internal static Session? Selected
    {
        get { lock (gate) return selected; }
    }

    internal static void Track(ModWatcher.Watcher watcher)
    {
        lock (gate)
        {
            if (sessions.Any(session => ReferenceEquals(session.Watcher, watcher))) return;
            selected = new Session(watcher);
            sessions.Add(selected);
        }
        SessionsChanged?.Invoke();
    }

    internal static void Select(Session session)
    {
        lock (gate)
        {
            if (ReferenceEquals(selected, session) || !sessions.Contains(session)) return;
            selected = session;
        }
        SessionsChanged?.Invoke();
    }

    internal static void Remove(Session session)
    {
        lock (gate)
        {
            var index = sessions.IndexOf(session);
            if (index < 0) return;
            sessions.RemoveAt(index);
            session.Detach();
            if (ReferenceEquals(selected, session))
                selected = sessions.Count == 0 ? null : sessions[Math.Min(index, sessions.Count - 1)];
        }
        SessionsChanged?.Invoke();
    }

    internal static int Limit(int slider) => slider switch
    {
        <= 5 => slider * 10 + 50,
        <= 13 => slider * 50 - 150,
        <= 28 => slider * 100 - 800,
        _ => int.MaxValue
    };

    internal readonly record struct Line(long Sequence, string Text, Color Color);

    internal sealed class Session
    {
        private readonly object lineGate = new();
        private readonly Queue<Line> lines = new();
        private long sequence;
        internal ModWatcher.Watcher Watcher { get; }
        internal string Title { get; }

        internal Session(ModWatcher.Watcher watcher)
        {
            Watcher = watcher;
            Title = $"{watcher.version.Name} · {Lang.Date(watcher.gameProcess.StartTime, "T")}";
            watcher.LogOutput += OnLine;
            watcher.GameExit += OnExit;
        }

        internal void Detach()
        {
            Watcher.LogOutput -= OnLine;
            Watcher.GameExit -= OnExit;
        }

        private void OnLine(ModWatcher.Watcher watcher, ModWatcher.LogOutputEventArgs e)
        {
            lock (lineGate)
            {
                lines.Enqueue(new Line(++sequence, e.logText, e.color.Color));
                Trim();
            }
            Updated?.Invoke(this);
        }

        private void OnExit() => Updated?.Invoke(this);

        internal (long First, Line[] Lines) Read(long after)
        {
            lock (lineGate)
            {
                Trim();
                var first = lines.TryPeek(out var line) ? line.Sequence : sequence + 1;
                return (first, lines.Where(line => line.Sequence > after).ToArray());
            }
        }

        internal void Clear()
        {
            lock (lineGate) lines.Clear();
            Updated?.Invoke(this);
        }

        internal string[] ReadFullLog()
        {
            lock (Watcher.fullLog) return Watcher.fullLog.ToArray();
        }

        private void Trim()
        {
            var limit = Limit(Config.System.MaxGameLog);
            while (lines.Count > limit) lines.Dequeue();
        }
    }
}
