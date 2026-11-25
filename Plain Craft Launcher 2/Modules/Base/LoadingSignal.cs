namespace PCL;

public enum LoadingStatus
{
    Unloaded = -1,
    Run = 0,
    Stop = 1,
    Error = 2
}

public interface ILoadingTrigger
{
    delegate void LoadingStateChangedEventHandler(LoadingStatus newState, LoadingStatus oldState);
    delegate void ProgressChangedEventHandler(double newProgress, double oldProgress);

    bool IsLoader { get; }
    double Progress { get; }
    Exception? Error { get; }
    LoadingStatus LoadingState { get; set; }
    event LoadingStateChangedEventHandler? LoadingStateChanged;
    event ProgressChangedEventHandler? ProgressChanged;
}

internal sealed class ManualLoadingSignal(LoadingStatus initial) : ILoadingTrigger
{
    public LoadingStatus LoadingState
    {
        get => field;
        set
        {
            if (field == value) return;
            var previous = field;
            field = value;
            LoadingStateChanged?.Invoke(value, previous);
        }
    } = initial;

    public bool IsLoader => false;
    public double Progress => 0;
    public Exception? Error => null;
    public event ILoadingTrigger.LoadingStateChangedEventHandler? LoadingStateChanged;

    public event ILoadingTrigger.ProgressChangedEventHandler? ProgressChanged
    {
        add { }
        remove { }
    }
}
