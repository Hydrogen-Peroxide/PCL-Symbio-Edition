namespace PCL;

internal enum PageSection { Common, Content, Loader }

internal sealed class PageLifecycle(Func<ModBase.LoadState?> load, Action<bool, PageSection[]> render,
    Action<int, Action> delay, Action cancel)
{
    public WorkspacePhase State { get; internal set; } = WorkspacePhase.Empty;
    private bool commonShown;

    public void Enter()
    {
        var common = State == WorkspacePhase.Empty || !commonShown;
        if (State is WorkspacePhase.ContentEnter or WorkspacePhase.ContentStay) return;
        if (load() == ModBase.LoadState.Loading)
        {
            cancel();
            State = WorkspacePhase.LoaderWait;
            delay(400, () =>
            {
                if (State == WorkspacePhase.LoaderWait)
                    Present(WorkspacePhase.LoaderEnter, PageSection.Loader, !commonShown);
            });
        }
        else if (load() == ModBase.LoadState.Failed)
            Present(WorkspacePhase.LoaderEnter, PageSection.Loader, common);
        else
            Present(WorkspacePhase.ContentEnter, PageSection.Content, common);
    }

    public void Leave()
    {
        switch (State)
        {
            case WorkspacePhase.Empty:
            case WorkspacePhase.PageExit: return;
            case WorkspacePhase.LoaderWait:
                Exit(WorkspacePhase.PageExit, PageSection.Common);
                break;
            case WorkspacePhase.LoaderEnter:
            case WorkspacePhase.LoaderStay:
            case WorkspacePhase.LoaderStayForce:
            case WorkspacePhase.LoaderExit:
                Exit(WorkspacePhase.PageExit, PageSection.Common, PageSection.Loader);
                break;
            default:
                Exit(WorkspacePhase.PageExit, PageSection.Common, PageSection.Content);
                break;
        }
    }

    public void Detach()
    {
        cancel();
        State = WorkspacePhase.Empty;
        commonShown = false;
    }

    public void ReplaceContent()
    {
        if (State is WorkspacePhase.LoaderExit or WorkspacePhase.ContentExit) { State = WorkspacePhase.ContentExit; return; }
        if (State is WorkspacePhase.Empty or WorkspacePhase.LoaderWait) { Enter(); return; }
        Exit(WorkspacePhase.ContentExit, State is WorkspacePhase.ContentEnter or WorkspacePhase.ContentStay
            ? PageSection.Content : PageSection.Loader);
    }

    public void LoaderChanged(ModBase.LoadState next, ModBase.LoadState previous)
    {
        static bool Pending(ModBase.LoadState value) => value is ModBase.LoadState.Failed or ModBase.LoadState.Loading;
        if (Pending(next) == Pending(previous)) return;
        if (Pending(next))
        {
            if (State is WorkspacePhase.ContentEnter or WorkspacePhase.ContentStay) Exit(WorkspacePhase.ContentExit, PageSection.Content);
            else if (State == WorkspacePhase.LoaderExit) State = WorkspacePhase.ContentExit;
        }
        else if (State == WorkspacePhase.LoaderWait) Present(WorkspacePhase.ContentEnter, PageSection.Content, !commonShown);
        else if (State == WorkspacePhase.LoaderStay) Exit(WorkspacePhase.LoaderExit, PageSection.Loader);
    }

    private void Present(WorkspacePhase state, PageSection section, bool includeCommon)
    {
        cancel();
        State = state;
        commonShown |= includeCommon;
        render(true, includeCommon ? [PageSection.Common, section] : [section]);
    }

    private void Exit(WorkspacePhase state, params PageSection[] parts)
    {
        cancel();
        State = state;
        render(false, parts);
    }

    internal void Entered()
    {
        if (State == WorkspacePhase.ContentEnter) State = WorkspacePhase.ContentStay;
        else if (State == WorkspacePhase.LoaderEnter)
        {
            State = WorkspacePhase.LoaderStayForce;
            delay(400, () =>
            {
                if (State != WorkspacePhase.LoaderStayForce) return;
                if (load() is ModBase.LoadState.Finished or ModBase.LoadState.Aborted or ModBase.LoadState.Waiting)
                    Exit(WorkspacePhase.LoaderExit, PageSection.Loader);
                else State = WorkspacePhase.LoaderStay;
            });
        }
    }

    internal void Exited()
    {
        switch (State)
        {
            case WorkspacePhase.PageExit: Detach(); break;
            case WorkspacePhase.ContentExit: Enter(); break;
            case WorkspacePhase.LoaderExit: Present(WorkspacePhase.ContentEnter, PageSection.Content, false); break;
        }
    }
}

public enum WorkspacePhase
{
    Empty, LoaderWait, LoaderEnter, LoaderStayForce, LoaderStay, LoaderExit, ContentEnter, ContentStay, ContentExit, PageExit
}
