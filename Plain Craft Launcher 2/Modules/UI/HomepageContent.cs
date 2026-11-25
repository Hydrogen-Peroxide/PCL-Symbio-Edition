using PCL.Core.App;

namespace PCL;

internal static class HomepageContent
{
    private static int revision;
    public static int Revision => Volatile.Read(ref revision);
    public static event Action? RefreshRequested;

    public static void RequestRefresh()
    {
        States.UI.SavedHomepageUrl = "";
        States.UI.SavedHomepageVersion = "";
        Interlocked.Increment(ref revision);
        ModBase.RunInUi(() => RefreshRequested?.Invoke());
    }
}
